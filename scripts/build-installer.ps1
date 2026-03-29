param(
    [Alias("PublishDir")]
    [string]$StageRoot = "out\installer-stage",
    [string]$OutputDir = "out\installer",
    [string]$Version = "",
    [string]$IsccPath = "",
    [switch]$SkipPublish,
    [switch]$StageOnly,
    [switch]$VerifyInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Get-AppVersion {
    param([string]$ProjectPath, [string]$RequestedVersion)

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        if ($RequestedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $RequestedVersion.Substring(1)
        }

        return $RequestedVersion
    }

    [xml]$project = Get-Content $ProjectPath
    foreach ($propertyGroup in $project.Project.PropertyGroup) {
        if (-not [string]::IsNullOrWhiteSpace($propertyGroup.Version)) {
            return [string]$propertyGroup.Version
        }
    }

    throw "Unable to determine XeCLI version from $ProjectPath"
}

function Get-AssetVersionLabel {
    param([string]$RequestedVersion, [string]$AppVersion)

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return $RequestedVersion
    }

    return $AppVersion
}

function Find-IsccPath {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolved = Resolve-RepoPath $RequestedPath
        if (-not (Test-Path $resolved)) {
            throw "ISCC.exe was not found at $resolved"
        }

        return $resolved
    }

    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($basePath in @(
        ${env:ProgramFiles(x86)},
        $env:ProgramFiles
    )) {
        if (-not [string]::IsNullOrWhiteSpace($basePath)) {
            $candidates.Add((Join-Path $basePath "Inno Setup 6\ISCC.exe"))
        }
    }

    foreach ($registryPath in @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe"
    )) {
        if (Test-Path $registryPath) {
            $appPath = (Get-ItemProperty $registryPath).'(default)'
            if (-not [string]::IsNullOrWhiteSpace($appPath)) {
                $candidates.Add($appPath)
            }
        }
    }

    foreach ($uninstallKey in @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )) {
        $installLocations = Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like "Inno Setup*" -and -not [string]::IsNullOrWhiteSpace($_.InstallLocation) } |
            Select-Object -ExpandProperty InstallLocation
        foreach ($installLocation in $installLocations) {
            $candidates.Add((Join-Path $installLocation "ISCC.exe"))
        }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

$projectPath = Join-Path $repoRoot "src\Xbox360.Remote.Cli\Xbox360.Remote.Cli.csproj"
$stageRoot = Resolve-RepoPath $StageRoot
$outputDir = Resolve-RepoPath $OutputDir
$issPath = Join-Path $repoRoot "installer\XeCLI.iss"
$appVersion = Get-AppVersion -ProjectPath $projectPath -RequestedVersion $Version
$assetVersionLabel = Get-AssetVersionLabel -RequestedVersion $Version -AppVersion $appVersion

if (-not (Test-Path $issPath)) {
    throw "Installer script was not found at $issPath"
}

$publishDirs = @{
    "win-x64" = Join-Path $stageRoot "win-x64"
    "win-x86" = Join-Path $stageRoot "win-x86"
}

if (-not $SkipPublish) {
    foreach ($runtime in @("win-x64", "win-x86")) {
        & (Join-Path $repoRoot "scripts\publish-release.ps1") -Runtime $runtime -Output $publishDirs[$runtime]
    }
}

foreach ($runtime in @("win-x64", "win-x86")) {
    & (Join-Path $repoRoot "scripts\verify-release.ps1") -PublishDir $publishDirs[$runtime]
}

if ($StageOnly) {
    Write-Host "Verified XeCLI release payloads for installer staging at $stageRoot"
    return
}

$resolvedIsccPath = Find-IsccPath -RequestedPath $IsccPath
if ([string]::IsNullOrWhiteSpace($resolvedIsccPath)) {
    throw "ISCC.exe was not found. Install Inno Setup 6 or rerun with -StageOnly."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$isccArgs = @(
    "/Qp",
    "/DAppVersion=$appVersion",
    "/DReleaseDir=$stageRoot",
    "/DOutputDir=$outputDir",
    $issPath
)

& $resolvedIsccPath @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE"
}

$builtSetupExe = Join-Path $outputDir "XeCLI-$appVersion-setup.exe"
$releaseSetupExe = Join-Path $outputDir "XeCLI-$assetVersionLabel-setup.exe"
if (-not $builtSetupExe.Equals($releaseSetupExe, [System.StringComparison]::OrdinalIgnoreCase)) {
    if (-not (Test-Path $builtSetupExe)) {
        throw "Expected installer output was not found at $builtSetupExe"
    }

    if (Test-Path $releaseSetupExe) {
        Remove-Item -Force $releaseSetupExe
    }

    Move-Item -Force $builtSetupExe $releaseSetupExe
}

if ($VerifyInstaller) {
    $setupExe = $releaseSetupExe
    & (Join-Path $repoRoot "scripts\verify-installer.ps1") -SetupExe $setupExe
}

Write-Host "Built XeCLI installer $assetVersionLabel in $outputDir"
