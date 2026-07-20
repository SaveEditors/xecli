[CmdletBinding()]
param(
    [string]$MSBuildPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExpectedSha256 = 'F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9'
$Root = [System.IO.Path]::GetFullPath($PSScriptRoot)
$Project = Join-Path $Root 'src\X360\X360.csproj'
$ExpectedLibrary = Join-Path $Root 'lib\X360.dll'

function Resolve-MSBuild {
    param([string]$Candidate)

    if (-not [string]::IsNullOrWhiteSpace($Candidate)) {
        if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) {
            throw "MSBuild was not found: $Candidate"
        }
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $command = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $resolved = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($resolved) -and (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $resolved).Path
        }
    }

    throw 'MSBuild was not found. Install Visual Studio Build Tools or pass -MSBuildPath.'
}

if (-not (Test-Path -LiteralPath $Project -PathType Leaf)) {
    throw "X360 project was not found: $Project"
}
if (-not (Test-Path -LiteralPath $ExpectedLibrary -PathType Leaf)) {
    throw "Checked X360 library was not found: $ExpectedLibrary"
}

$resolvedMSBuild = Resolve-MSBuild -Candidate $MSBuildPath
& $resolvedMSBuild $Project /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /nologo /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "X360 build failed with exit code $LASTEXITCODE."
}

$builtLibrary = Join-Path (Split-Path -Parent $Project) 'bin\Release\X360.dll'
if (-not (Test-Path -LiteralPath $builtLibrary -PathType Leaf)) {
    throw "X360 build output was not found: $builtLibrary"
}

$builtHash = (Get-FileHash -LiteralPath $builtLibrary -Algorithm SHA256).Hash
$checkedHash = (Get-FileHash -LiteralPath $ExpectedLibrary -Algorithm SHA256).Hash
if (-not $builtHash.Equals($ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Built X360.dll SHA-256 mismatch. Expected $ExpectedSha256; found $builtHash."
}
if (-not $checkedHash.Equals($ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Checked X360.dll SHA-256 mismatch. Expected $ExpectedSha256; found $checkedHash."
}

Write-Output "Verified deterministic X360.dll: $builtHash"
