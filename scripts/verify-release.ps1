param(
    [string]$PublishDir = "out\win-x64",
    [string]$BuildDir = "src\Xbox360.Remote.Cli\bin\Release\net10.0-windows"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = Join-Path $repoRoot "tmp\release-verify"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

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

$publishDir = Resolve-RepoPath $PublishDir
$buildDir = Resolve-RepoPath $BuildDir
$publishExe = Join-Path $publishDir "rgh.exe"

if (-not (Test-Path $publishExe)) {
    throw "Published rgh.exe was not found at $publishExe"
}

$publishRuntimeConfig = Join-Path $publishDir "rgh.runtimeconfig.json"
if (Test-Path $publishRuntimeConfig) {
    throw "Expected a single-file self-contained publish, but found $publishRuntimeConfig"
}

$requiredPaths = @(
    "Assets",
    "ghidra_scripts",
    "ida_scripts",
    "ConsoleDependencies",
    "LICENSE"
)

foreach ($relativePath in $requiredPaths) {
    $fullPath = Join-Path $publishDir $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Required published asset was not found: $fullPath"
    }
}

foreach ($plugin in @("xbdm.xex", "JRPC2.xex", "XDRPC.xex")) {
    $pluginPath = Join-Path $publishDir "ConsoleDependencies\$plugin"
    if (-not (Test-Path $pluginPath)) {
        throw "Required console dependency was not found: $pluginPath"
    }
}

& $publishExe --version | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Published rgh.exe --version failed with exit code $LASTEXITCODE"
}

& $publishExe help | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Published rgh.exe help failed with exit code $LASTEXITCODE"
}

& $publishExe fatman --help | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Published rgh.exe fatman --help failed with exit code $LASTEXITCODE"
}

Write-Host "Release verification passed for $publishDir"
