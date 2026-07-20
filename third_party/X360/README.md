# X360 library for XeCLI

This directory contains the complete corresponding source for the `X360.dll` that XeCLI uses to build QuickBoot STFS packages. The maintained library is distributed under GNU GPL version 3.

> Modified by XeCLI contributors on 2026-07-19.

## Provenance

- Original author: DJ Shepherd (also known as DJ SkunkieButt)
- Preserved source archive: [mtolly/X360](https://github.com/mtolly/X360/tree/573dda3cd841ba370b2110567a6b4ee2b7099c9c)
- Upstream revision: `573dda3cd841ba370b2110567a6b4ee2b7099c9c`
- Upstream version: `1.0.0.41`
- XeCLI-maintained version: `1.0.0.42`
- License: GNU GPL version 3 (repository `LICENSE`; embedded build copy at `src/X360/Resources/GPL30.txt`)

The upstream README repeated GPLv3 and mixed in historical terms, so it is not redistributed. The repository-level `LICENSE` provides the authoritative GPLv3 terms for XeCLI and the maintained X360 library. `src/X360/Resources/GPL30.txt` remains because it is an embedded build input required to reproduce `X360.dll`; a duplicate X360-only license file is intentionally not carried. The maintained source and binary are distributed under GPLv3.

## XeCLI changes

- Removed the retired outbound privilege callback and update-check implementation so package creation remains offline.
- Corrected final-block handling when an input length is an exact multiple of 4 KiB.
- Preserved the legacy QuickBoot `GamesOnDemand` STFS container used by prior XeCLI releases.
- Removed upstream artwork with unclear redistribution terms and replaced its public image accessors with generated placeholders.
- Enabled deterministic, symbol-free Release builds and advanced the assembly version to `1.0.0.42`.
- Replaced conflicting source-header restrictions with a clear GPLv3 notice while retaining original authorship.

## Build

Requirements:

- Windows
- Visual Studio Build Tools with MSBuild
- .NET Framework 3.5 reference assemblies

From this directory, run:

```powershell
.\build.ps1
```

The script rebuilds `src/X360/X360.csproj` as `Release|AnyCPU` and verifies the result against the checked release library. The expected SHA-256 is:

```text
F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9
```

`SOURCE-MANIFEST.json` records the exact source and library inventory included with XeCLI.
