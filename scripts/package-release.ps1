param(
    [string]$PublishDir = "",
    [string]$OutputDir = "out\release",
    [string]$Version = "",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = "out\$Runtime"
}

$publishDir = Resolve-RepoPath $PublishDir
$outputDir = Resolve-RepoPath $OutputDir

if (-not (Test-Path (Join-Path $publishDir "rgh.exe"))) {
    throw "Published rgh.exe was not found in $publishDir"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$suffix = if ([string]::IsNullOrWhiteSpace($Version)) { $Runtime } else { "$Version-$Runtime" }
$baseName = "XeCLI-$suffix"
$zipPath = Join-Path $outputDir "$baseName.zip"
$hashPath = "$zipPath.sha256"

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

if (Test-Path $hashPath) {
    Remove-Item -Force $hashPath
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $hashPath -Value "$hash *$([System.IO.Path]::GetFileName($zipPath))"

Write-Host "Packaged XeCLI $Runtime release to $zipPath"
Write-Host "Wrote SHA256 to $hashPath"
