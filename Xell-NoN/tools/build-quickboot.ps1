param(
    [Parameter(Mandatory = $true)][string]$ConfigFile,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [string]$DisplayName = "XeLL Launch",
    [string]$Description = "XeLL shortcut created by Xell-NoN."
)

$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$assetRoot = Join-Path $repoRoot "launch\\QuickBoot"
$x360Dll = Join-Path $assetRoot "X360.dll"
$defaultXex = Join-Path $assetRoot "default.xex"
$resolvedConfig = [IO.Path]::GetFullPath($ConfigFile)
$resolvedOutput = [IO.Path]::GetFullPath($OutputFile)

foreach ($path in @($x360Dll, $defaultXex, $resolvedConfig)) {
    if (-not (Test-Path $path)) {
        throw "Required file not found: $path"
    }
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

$endian = [X360.IO.EndianType]::BigEndian
$session = [X360.STFS.CreateContents]::new()
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

$stub = [X360.IO.DJsIO]::new($defaultXex, [X360.IO.DJFileMode]::Open, $endian)
$config = [X360.IO.DJsIO]::new($resolvedConfig, [X360.IO.DJFileMode]::Open, $endian)

try {
    if (-not $session.AddFile($stub, "default.xex")) {
        throw "Failed to add default.xex to the QuickBoot package."
    }
    if (-not $session.AddFile($config, "config.ini")) {
        throw "Failed to add config.ini to the QuickBoot package."
    }

    $outIo = [X360.IO.DJsIO]::new($resolvedOutput, [X360.IO.DJFileMode]::Open, $endian)
    try {
        $signing = [X360.STFS.RSAParams]::new([X360.STFS.StrongSigned]::LIVE)
        $log = [X360.Other.LogRecord]::new()
        $package = [X360.STFS.STFSPackage]::new($session, $signing, $outIo, $log)
        if (-not $package.ParseSuccess) {
            throw "The QuickBoot package did not parse successfully after creation."
        }
        [void]$package.CloseIO()
    }
    finally {
        if ($outIo) {
            $outIo.Close()
        }
    }
}
finally {
    if ($stub) {
        $stub.Close()
    }
    if ($config) {
        $config.Close()
    }
}

