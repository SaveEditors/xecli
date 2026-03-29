param(
    [string]$SetupExe = "",
    [string]$Language = "es",
    [ValidateSet("Auto", "CurrentUser", "AllUsers")]
    [string]$InstallScope = "Auto"
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
    $latestSetup = Get-ChildItem -Path $defaultDir -Filter "XeCLI-*-setup.exe" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestSetup) {
        throw "No XeCLI installer executable was found in $defaultDir"
    }

    return $latestSetup.FullName
}

function Assert-UniversalInstallerName([string]$SetupExePath) {
    $fileName = [System.IO.Path]::GetFileName($SetupExePath)
    if ($fileName -notmatch '^XeCLI-.+-setup\.exe$') {
        throw "Expected a universal XeCLI installer named XeCLI-<version>-setup.exe, but found $fileName"
    }
}

function Test-IsElevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-InstallScope([string]$RequestedScope) {
    switch ($RequestedScope) {
        "CurrentUser" { return "CurrentUser" }
        "AllUsers" {
            if (-not (Test-IsElevated)) {
                throw "The AllUsers verification path requires an elevated PowerShell session."
            }

            return "AllUsers"
        }
        default {
            if (Test-IsElevated) {
                return "AllUsers"
            }

            return "CurrentUser"
        }
    }
}

function Get-InstallScopeSwitch([string]$Scope) {
    if ($Scope -eq "AllUsers") {
        return "/ALLUSERS"
    }

    return "/CURRENTUSER"
}

function Get-InstallScopeLabel([string]$Scope) {
    if ($Scope -eq "AllUsers") {
        return "HKLM all-users"
    }

    return "HKCU current-user"
}

function Get-InstallScopeRootKey([string]$Scope) {
    if ($Scope -eq "AllUsers") {
        return [Microsoft.Win32.Registry]::LocalMachine
    }

    return [Microsoft.Win32.Registry]::CurrentUser
}

function Get-InstallScopeSubkey([string]$Scope) {
    if ($Scope -eq "AllUsers") {
        return "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
    }

    return "Environment"
}

function Get-OtherScopeRootKey([string]$Scope) {
    if ($Scope -eq "AllUsers") {
        return [Microsoft.Win32.Registry]::CurrentUser
    }

    return [Microsoft.Win32.Registry]::LocalMachine
}

function Get-OtherScopeSubkey([string]$Scope) {
    if ($Scope -eq "AllUsers") {
        return "Environment"
    }

    return "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
}

function New-InstallerConfig([string]$LanguageCode, [string]$PreservedSettingValue, [bool]$PathPromptHandled) {
    $config = [ordered]@{
        UiLanguage = $LanguageCode
        PathPromptHandled = $PathPromptHandled
    }

    if (-not [string]::IsNullOrWhiteSpace($PreservedSettingValue)) {
        $config.PreservedSetting = $PreservedSettingValue
    }

    return [pscustomobject]$config
}

function Test-RegistryPathStateEquivalent($Left, $Right) {
    if ($null -eq $Left -and $null -eq $Right) {
        return $true
    }

    if ($null -eq $Left -or $null -eq $Right) {
        return $false
    }

    return ($Left.Exists -eq $Right.Exists) -and ($Left.Value -eq $Right.Value)
}

function Get-RegistryPathState([Microsoft.Win32.RegistryKey]$RootKey, [string]$Subkey) {
    try {
        $key = $RootKey.OpenSubKey($Subkey, $false)
        if ($null -eq $key) {
            return $null
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
    catch {
        return $null
    }
}

function Set-RegistryPathState([Microsoft.Win32.RegistryKey]$RootKey, [string]$Subkey, [string]$Value) {
    $key = $RootKey.OpenSubKey($Subkey, $true)
    if ($null -eq $key) {
        throw "Unable to open the registry key '$Subkey' for writing."
    }

    try {
        $key.SetValue("Path", $Value, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    }
    finally {
        $key.Dispose()
    }
}

function Restore-RegistryPathState([Microsoft.Win32.RegistryKey]$RootKey, [string]$Subkey, $State) {
    try {
        $key = $RootKey.OpenSubKey($Subkey, $true)
        if ($null -eq $key) {
            throw "Unable to open the registry key '$Subkey' for restoration."
        }

        try {
            if ($null -eq $State) {
                if ($null -ne $key.GetValue("Path", $null)) {
                    $key.DeleteValue("Path", $false)
                }
            }
            elseif ($State.Exists) {
                $key.SetValue("Path", $State.Value, [Microsoft.Win32.RegistryValueKind]::ExpandString)
            }
            elseif ($null -ne $key.GetValue("Path", $null)) {
                $key.DeleteValue("Path", $false)
            }
        }
        finally {
            $key.Dispose()
        }
    }
    catch {
        throw
    }
}

function Get-UserPathState {
    Get-RegistryPathState ([Microsoft.Win32.Registry]::CurrentUser) "Environment"
}

function Get-MachinePathState {
    Get-RegistryPathState ([Microsoft.Win32.Registry]::LocalMachine) "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
}

function Restore-UserPathState($state) {
    Restore-RegistryPathState ([Microsoft.Win32.Registry]::CurrentUser) "Environment" $state
}

function Restore-MachinePathState($state) {
    Restore-RegistryPathState ([Microsoft.Win32.Registry]::LocalMachine) "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" $state
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

function Get-PathEntryCount([string]$PathValue, [string]$ExpectedDirectory) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return 0
    }

    $normalizedExpected = [System.IO.Path]::GetFullPath($ExpectedDirectory).TrimEnd('\')
    $count = 0
    foreach ($entry in $PathValue.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries -bor [System.StringSplitOptions]::TrimEntries)) {
        try {
            $normalizedEntry = [System.IO.Path]::GetFullPath($entry).TrimEnd('\')
        }
        catch {
            continue
        }

        if ($normalizedEntry.Equals($normalizedExpected, [System.StringComparison]::OrdinalIgnoreCase)) {
            $count++
        }
    }

    return $count
}

function Get-CombinedPathEntryCount([string[]]$PathValues, [string]$ExpectedDirectory) {
    $count = 0
    foreach ($pathValue in $PathValues) {
        $count += Get-PathEntryCount $pathValue $ExpectedDirectory
    }

    return $count
}

function Test-AnyPathEntry([string[]]$PathValues, [string]$ExpectedDirectory) {
    foreach ($pathValue in $PathValues) {
        if (Test-PathEntry $pathValue $ExpectedDirectory) {
            return $true
        }
    }

    return $false
}

function New-SeededPathValue([string]$ExistingPath, [string]$InstallDir, [string]$NearMissDir) {
    $seedPathPrefix = @(
        $InstallDir,
        $InstallDir + "\",
        $NearMissDir
    ) -join ';'

    if ([string]::IsNullOrWhiteSpace($ExistingPath)) {
        return $seedPathPrefix
    }

    return $seedPathPrefix + ';' + $ExistingPath
}

$setupExe = Resolve-SetupExe $SetupExe
Assert-UniversalInstallerName $setupExe
$installScope = Resolve-InstallScope $InstallScope
$installScopeSwitch = Get-InstallScopeSwitch $installScope
$installScopeLabel = Get-InstallScopeLabel $installScope
$targetRootKey = Get-InstallScopeRootKey $installScope
$targetSubkey = Get-InstallScopeSubkey $installScope
$otherRootKey = Get-OtherScopeRootKey $installScope
$otherSubkey = Get-OtherScopeSubkey $installScope
$installDir = Join-Path $tempRoot "XeCLI"
$installLog = Join-Path $tempRoot "install.log"
$upgradeLog = Join-Path $tempRoot "upgrade.log"
$uninstallLog = Join-Path $tempRoot "uninstall.log"
$cmdProbeDir = Join-Path $tempRoot "cmdprobe"
$configPath = Join-Path $env:APPDATA "XeCLI\config.json"
$configBackupPath = Join-Path $tempRoot "config-backup.json"
$configExisted = Test-Path $configPath
$preTargetPathState = Get-RegistryPathState $targetRootKey $targetSubkey
$preOtherPathState = Get-RegistryPathState $otherRootKey $otherSubkey

if (Test-Path $tempRoot) {
    Remove-Item -Recurse -Force $tempRoot
}

New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
New-Item -ItemType Directory -Force -Path $cmdProbeDir | Out-Null

if ($configExisted) {
    Copy-Item -Force $configPath $configBackupPath
}

if (Test-Path $configPath) {
    Remove-Item -Force $configPath
}

$nearMissDir = Join-Path $tempRoot "XeCLI-not-owned"
$existingTargetPath = if ($null -ne $preTargetPathState) { $preTargetPathState.Value } else { $null }
$seedTargetPathValue = New-SeededPathValue $existingTargetPath $installDir $nearMissDir

Set-RegistryPathState $targetRootKey $targetSubkey $seedTargetPathValue

function Invoke-InstallerRun([string]$RequestedLanguage, [string]$LogPath) {
    $installArgs = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        $installScopeSwitch,
        "/DIR=$installDir",
        "/LANG=$RequestedLanguage",
        '/TASKS="modifypath"',
        "/LOG=$LogPath"
    )

    $installProcess = Start-Process -FilePath $setupExe -ArgumentList $installArgs -Wait -PassThru
    if ($installProcess.ExitCode -ne 0) {
        throw "Installer exited with code $($installProcess.ExitCode)"
    }
}

function Assert-InstallSucceeded([string]$ExpectedLanguage, [string]$LogPath, [switch]$ExpectPreservedSetting) {
    if (-not (Test-Path $LogPath)) {
        throw "Installer log was not created at $LogPath"
    }

    $installedExe = Join-Path $installDir "rgh.exe"
    if (-not (Test-Path $installedExe)) {
        throw "Installed rgh.exe was not found at $installedExe"
    }

    & $installedExe --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Installed rgh.exe --version failed with exit code $LASTEXITCODE"
    }

    & $installedExe --lang $ExpectedLanguage --help | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Installed rgh.exe --lang $ExpectedLanguage --help failed with exit code $LASTEXITCODE"
    }

    $targetPathState = Get-RegistryPathState $targetRootKey $targetSubkey
    if ($null -eq $targetPathState) {
        throw "Installer did not create a PATH value in the $installScopeLabel registry branch."
    }

    if ((Get-PathEntryCount $targetPathState.Value $installDir) -ne 1) {
        throw "Installer did not leave exactly one authoritative XeCLI PATH entry in the $installScopeLabel branch."
    }

    if (-not (Test-PathEntry $targetPathState.Value $nearMissDir)) {
        throw "Installer removed a non-owned PATH entry that only matched XeCLI heuristically in the $installScopeLabel branch."
    }

    $otherPathState = Get-RegistryPathState $otherRootKey $otherSubkey
    if (-not (Test-RegistryPathStateEquivalent $preOtherPathState $otherPathState)) {
        throw "Installer changed the non-target PATH branch while verifying the $installScopeLabel branch."
    }

    if (-not (Test-Path $configPath)) {
        throw "Installer did not create the XeCLI config file at $configPath"
    }

    $configJson = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($configJson.UiLanguage -ne $ExpectedLanguage) {
        throw "Installer did not persist the selected UI language. Expected '$ExpectedLanguage', got '$($configJson.UiLanguage)'."
    }

    if ($configJson.PathPromptHandled -ne $true) {
        throw "Installer did not mark the path prompt as handled."
    }

    if ($ExpectPreservedSetting) {
        if ($configJson.PreservedSetting -ne "keep") {
            throw "Installer did not preserve existing config data while updating the language."
        }
    }
    elseif ($null -ne $configJson.PreservedSetting) {
        throw "Fresh install unexpectedly preserved an existing config field."
    }
}

try {
    Invoke-InstallerRun -RequestedLanguage $Language -LogPath $installLog
    Assert-InstallSucceeded -ExpectedLanguage $Language -LogPath $installLog

    $cmdExe = Join-Path $env:SystemRoot "System32\cmd.exe"
    & $cmdExe /d /c "cd /d ""$cmdProbeDir"" && set ""PATH=$installDir;%SystemRoot%\System32;%SystemRoot%"" && rgh --version >nul"
    if ($LASTEXITCODE -ne 0) {
        throw "Bare rgh --version did not resolve from the installed release directory."
    }

    $upgradeLanguage = if ($Language -eq "es") { "en" } else { "es" }
    $upgradeConfig = New-InstallerConfig -LanguageCode $upgradeLanguage -PreservedSettingValue "keep" -PathPromptHandled $false
    New-Item -ItemType Directory -Force -Path (Split-Path $configPath -Parent) | Out-Null
    $upgradeConfig | ConvertTo-Json -Depth 32 | Set-Content -Path $configPath -Encoding utf8

    Invoke-InstallerRun -RequestedLanguage $Language -LogPath $upgradeLog
    Assert-InstallSucceeded -ExpectedLanguage $Language -LogPath $upgradeLog -ExpectPreservedSetting

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

    if (-not (Test-Path $uninstallLog)) {
        throw "Uninstaller log was not created at $uninstallLog"
    }

    Start-Sleep -Seconds 2

    if (Test-Path (Join-Path $installDir "rgh.exe")) {
        throw "Installed files still exist after uninstall."
    }

    $finalTargetPathState = Get-RegistryPathState $targetRootKey $targetSubkey
    if ($null -eq $finalTargetPathState) {
        throw "Uninstaller removed the PATH value from the $installScopeLabel branch instead of leaving the near-miss entry untouched."
    }

    if ((Get-PathEntryCount $finalTargetPathState.Value $installDir) -ne 0) {
        throw "Installer PATH entry still exists after uninstall in the $installScopeLabel branch."
    }

    if (-not (Test-PathEntry $finalTargetPathState.Value $nearMissDir)) {
        throw "Uninstaller removed a non-owned PATH entry that only matched XeCLI heuristically in the $installScopeLabel branch."
    }

    $finalOtherPathState = Get-RegistryPathState $otherRootKey $otherSubkey
    if (-not (Test-RegistryPathStateEquivalent $preOtherPathState $finalOtherPathState)) {
        throw "Uninstaller changed the non-target PATH branch while verifying the $installScopeLabel branch."
    }
}
finally {
    Restore-RegistryPathState $targetRootKey $targetSubkey $preTargetPathState

    if ($configExisted) {
        if (-not (Test-Path (Split-Path $configPath -Parent))) {
            New-Item -ItemType Directory -Force -Path (Split-Path $configPath -Parent) | Out-Null
        }
        Copy-Item -Force $configBackupPath $configPath
    }
    elseif (Test-Path $configPath) {
        Remove-Item -Force $configPath
    }

    if (Test-Path $tempRoot) {
        Remove-Item -Recurse -Force $tempRoot
    }
}

Write-Host "Installer verification passed for $setupExe ($installScopeLabel; fresh install, upgrade-language reapplication, and PATH cleanup)"
