param(
    [Parameter(Mandatory = $true)][string]$ConfigFile,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [string]$DisplayName = "XeLL Launch",
    [string]$Description = "XeLL shortcut created by XeCLI-XellFetch.",
    [string]$X360DllPath
)

$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$assetRoot = Join-Path $repoRoot "launch\\QuickBoot"
$mainRepoRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot ".."))
$x360Dll = if ([string]::IsNullOrWhiteSpace($X360DllPath)) {
    Join-Path $mainRepoRoot "third_party\\X360\\lib\\X360.dll"
}
else {
    [IO.Path]::GetFullPath($X360DllPath)
}
$expectedX360DllSha256 = "F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9"
$defaultXex = Join-Path $assetRoot "default.xex"
$resolvedConfig = [IO.Path]::GetFullPath($ConfigFile)
$resolvedOutput = [IO.Path]::GetFullPath($OutputFile)

foreach ($path in @($x360Dll, $defaultXex, $resolvedConfig)) {
    if (-not (Test-Path $path)) {
        throw "Required file not found: $path"
    }
}

$actualX360DllSha256 = (Get-FileHash -LiteralPath $x360Dll -Algorithm SHA256).Hash
if (-not $actualX360DllSha256.Equals($expectedX360DllSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw "QuickBoot X360.dll SHA-256 mismatch. Expected $expectedX360DllSha256; found $actualX360DllSha256."
}

[void][Reflection.Assembly]::LoadFrom($x360Dll)

if (Test-Path $resolvedOutput) {
    Remove-Item -Force $resolvedOutput
}

$outputDir = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($outputDir)) {
    throw "Output directory could not be resolved from: $resolvedOutput"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType File -Force -Path $resolvedOutput | Out-Null

$session = [X360.STFS.CreateSTFS]::new()
$session.STFSType = [X360.STFS.STFSType]::Type0
$session.HeaderData.Description = $Description
$session.HeaderData.Title_Display = $DisplayName
$session.HeaderData.ThisType = [X360.STFS.PackageType]::GamesOnDemand
$session.HeaderData.TitleID = [Convert]::ToUInt32("C0DE9999", 16)
$session.HeaderData.Publisher = "F586558"
$session.HeaderData.Title_Package = "QuickBoot"
$session.HeaderData.SeriesID = [byte[]](0..15 | ForEach-Object { 0 })
$session.HeaderData.SeasonID = [byte[]](0..15 | ForEach-Object { 0 })
$session.HeaderData.DeviceID = [byte[]](0..19 | ForEach-Object { 0 })
$session.HeaderData.IDTransfer = [X360.STFS.TransferLock]::AllowTransfer

if (-not $session.AddFile($defaultXex, "default.xex")) {
    throw "Failed to add default.xex to the QuickBoot package."
}
if (-not $session.AddFile($resolvedConfig, "config.ini")) {
    throw "Failed to add config.ini to the QuickBoot package."
}

$signing = [X360.STFS.RSAParams]::new([X360.STFS.StrongSigned]::LIVE)
$log = [X360.Other.LogRecord]::new()
$package = [X360.STFS.STFSPackage]::new($session, $signing, $resolvedOutput, $log)
if (-not $package.ParseSuccess) {
    throw "The QuickBoot package did not parse successfully after creation."
}
if (-not $package.CloseIO()) {
    throw "The QuickBoot package stream did not close successfully."
}
