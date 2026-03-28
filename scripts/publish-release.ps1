param(
    [string]$Runtime = "win-x64",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\Xbox360.Remote.Cli\Xbox360.Remote.Cli.csproj"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = "out\$Runtime"
}

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
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

Write-Host "Published XeCLI ($Runtime) to $publishDir"
