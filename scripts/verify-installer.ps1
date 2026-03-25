param(
    [string]$SetupExe = "",
    [string]$Language = "es"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = Join-Path $repoRoot "tmp\installer-verify"

function Resolve-RepoPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Resolve-SetupExe([string]$RequestedPath) {
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolved = Resolve-RepoPath $RequestedPath
        if (-not (Test-Path $resolved)) {
            throw "Installer executable was not found at $resolved"
        }

        return $resolved
    }

    $defaultDir = Join-Path $repoRoot "out\installer"
    $latestSetup = Get-ChildItem -Path $defaultDir -Filter "XeCLI-*-setup-win-x64.exe" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestSetup) {
        throw "No XeCLI installer executable was found in $defaultDir"
    }

    return $latestSetup.FullName
}

function Get-UserPathState {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $false)
    if ($null -eq $key) {
        return @{
            Exists = $false
            Value = $null
        }
    }

    try {
        $value = $key.GetValue("Path", $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        return @{
            Exists = $null -ne $value
            Value = if ($null -eq $value) { $null } else { [string]$value }
        }
    }
    finally {
        $key.Dispose()
    }
}

function Restore-UserPathState($state) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $true)
    if ($null -eq $key) {
        throw "Unable to open the current-user environment registry key."
    }

    try {
        if ($state.Exists) {
            $key.SetValue("Path", $state.Value, [Microsoft.Win32.RegistryValueKind]::ExpandString)
        }
        elseif ($null -ne $key.GetValue("Path", $null)) {
            $key.DeleteValue("Path", $false)
        }
    }
    finally {
        $key.Dispose()
    }
}

function Test-PathEntry([string]$PathValue, [string]$ExpectedDirectory) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $false
    }

    $normalizedExpected = [System.IO.Path]::GetFullPath($ExpectedDirectory).TrimEnd('\')
    foreach ($entry in $PathValue.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries -bor [System.StringSplitOptions]::TrimEntries)) {
        try {
            $normalizedEntry = [System.IO.Path]::GetFullPath($entry).TrimEnd('\')
        }
        catch {
            continue
        }

        if ($normalizedEntry.Equals($normalizedExpected, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

$setupExe = Resolve-SetupExe $SetupExe
$installDir = Join-Path $tempRoot "XeCLI"
$installLog = Join-Path $tempRoot "install.log"
$uninstallLog = Join-Path $tempRoot "uninstall.log"
$cmdProbeDir = Join-Path $tempRoot "cmdprobe"
$userPathState = Get-UserPathState

if (Test-Path $tempRoot) {
    Remove-Item -Recurse -Force $tempRoot
}

New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
New-Item -ItemType Directory -Force -Path $cmdProbeDir | Out-Null

try {
    $installArgs = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/CURRENTUSER",
        "/DIR=$installDir",
        "/LANG=$Language",
        '/TASKS="modifypath"',
        "/LOG=$installLog"
    )
    $installProcess = Start-Process -FilePath $setupExe -ArgumentList $installArgs -Wait -PassThru
    if ($installProcess.ExitCode -ne 0) {
        throw "Installer exited with code $($installProcess.ExitCode)"
    }

    $installedExe = Join-Path $installDir "rgh.exe"
    if (-not (Test-Path $installedExe)) {
        throw "Installed rgh.exe was not found at $installedExe"
    }

    & $installedExe --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Installed rgh.exe --version failed with exit code $LASTEXITCODE"
    }

    & $installedExe --lang $Language --help | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Installed rgh.exe --lang $Language --help failed with exit code $LASTEXITCODE"
    }

    $updatedUserPath = (Get-UserPathState).Value
    if (-not (Test-PathEntry $updatedUserPath $installDir)) {
        throw "Installer did not add the install directory to the current-user PATH."
    }

    $cmdExe = Join-Path $env:SystemRoot "System32\cmd.exe"
    & $cmdExe /d /c "cd /d ""$cmdProbeDir"" && set ""PATH=$installDir;%SystemRoot%\System32;%SystemRoot%"" && rgh --version >nul"
    if ($LASTEXITCODE -ne 0) {
        throw "Bare rgh --version did not resolve from the installed release directory."
    }

    $uninstaller = Join-Path $installDir "unins000.exe"
    if (-not (Test-Path $uninstaller)) {
        throw "Installed uninstaller was not found at $uninstaller"
    }

    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/LOG=$uninstallLog"
    ) -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstallProcess.ExitCode)"
    }

    Start-Sleep -Seconds 2

    if (Test-Path (Join-Path $installDir "rgh.exe")) {
        throw "Installed files still exist after uninstall."
    }

    $finalUserPath = (Get-UserPathState).Value
    if (Test-PathEntry $finalUserPath $installDir) {
        throw "Installer PATH entry still exists after uninstall."
    }
}
finally {
    Restore-UserPathState $userPathState

    if (Test-Path $tempRoot) {
        Remove-Item -Recurse -Force $tempRoot
    }
}

Write-Host "Installer verification passed for $setupExe"
