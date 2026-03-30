param(
    [string]$ZipPath,
    [string]$ExtractDir = "tmp\zip-smoke",
    [string]$InstallDir = "tmp\zip-install"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

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

function Test-PathEntry([string]$pathValue, [string]$expectedDirectory) {
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        return $false
    }

    $normalizedExpected = [System.IO.Path]::GetFullPath($expectedDirectory).TrimEnd('\')
    foreach ($entry in $pathValue.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries -bor [System.StringSplitOptions]::TrimEntries)) {
        $normalizedEntry = [System.IO.Path]::GetFullPath($entry).TrimEnd('\')
        if ($normalizedEntry.Equals($normalizedExpected, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Resolve-RepoPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

$zipPath = Resolve-RepoPath $ZipPath
$extractDir = Resolve-RepoPath $ExtractDir
$installDir = Resolve-RepoPath $InstallDir

if (-not (Test-Path $zipPath)) {
    throw "Release zip was not found at $zipPath"
}

if (Test-Path $extractDir) {
    Remove-Item -Recurse -Force $extractDir
}

if (Test-Path $installDir) {
    Remove-Item -Recurse -Force $installDir
}

try {
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    foreach ($relativePath in @("Assets", "ghidra_scripts", "ida_scripts", "ConsoleDependencies", "LICENSE")) {
        $fullPath = Join-Path $extractDir $relativePath
        if (-not (Test-Path $fullPath)) {
            throw "Extracted release zip is missing required content: $fullPath"
        }
    }

    foreach ($plugin in @("xbdm.xex", "JRPC2.xex", "XDRPC.xex")) {
        $pluginPath = Join-Path $extractDir "ConsoleDependencies\$plugin"
        if (-not (Test-Path $pluginPath)) {
            throw "Extracted release zip is missing required console dependency: $pluginPath"
        }
    }

    $extractExe = Join-Path $extractDir "rgh.exe"
    if (-not (Test-Path $extractExe)) {
        throw "Extracted release zip is missing rgh.exe"
    }

    $extractTerminalExe = Join-Path $extractDir "XeTerminal.exe"
    if (-not (Test-Path $extractTerminalExe)) {
        throw "Extracted release zip is missing XeTerminal.exe"
    }

    & $extractExe --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Extracted rgh.exe --version failed with exit code $LASTEXITCODE"
    }

    & $extractTerminalExe --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Extracted XeTerminal.exe --version failed with exit code $LASTEXITCODE"
    }

    & $extractExe help | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Extracted rgh.exe help failed with exit code $LASTEXITCODE"
    }

    & $extractExe fatman --help | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Extracted rgh.exe fatman --help failed with exit code $LASTEXITCODE"
    }

    Write-Host "Release zip smoke test passed for $zipPath"
}
finally {
    if (Test-Path $extractDir) {
        Remove-Item -Recurse -Force $extractDir
    }

    if (Test-Path $installDir) {
        Remove-Item -Recurse -Force $installDir
    }
}
