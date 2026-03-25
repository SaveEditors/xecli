param(
    [string]$Output = "out\win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "decompiled\rgh.csproj"

if ([System.IO.Path]::IsPathRooted($Output)) {
    $publishDir = $Output
}
else {
    $publishDir = Join-Path $repoRoot $Output
}

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

foreach ($asset in @(
    "ghidra_scripts",
    "ida_scripts",
    "ConsoleDependencies",
    "LICENSE"
)) {
    $sourcePath = Join-Path $repoRoot $asset
    $destinationPath = Join-Path $publishDir $asset
    if (-not (Test-Path $sourcePath)) {
        throw "Required release asset was not found at $sourcePath"
    }

    if (Test-Path $destinationPath) {
        Remove-Item -Recurse -Force $destinationPath
    }

    Copy-Item -Recurse -Force $sourcePath $destinationPath
}

Write-Host "Published XeCLI to $publishDir"
