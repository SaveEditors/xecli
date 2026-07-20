# Third-Party Notices

XeCLI application source is distributed under GNU GPL version 3; see `LICENSE`. Separately bundled components retain the licenses stated below. This notice lists only software that is included in the repository or release packages; tools and content obtained separately by users are not part of the inventory.

## Included software

| Component | Version | License and notice |
| --- | --- | --- |
| FluentFTP | 52.1.0 | MIT; Copyright (c) 2015 Robin Rodricks and FluentFTP Contributors. |
| SharpCompress | 0.48.1 | MIT; Copyright (c) 2014 Adam Hathcock. |
| Spectre.Console and Spectre.Console.Cli | 0.50.0 | MIT; Copyright (c) 2020 Patrik Svensson, Phil Scott, and Nils Andresen. |
| System.Management and the self-contained .NET runtime | 10.0.0 | MIT; copyright Microsoft Corporation, .NET Foundation, and contributors. |
| X360 library | 1.0.0.42 | GNU GPL version 3; originally by DJ Shepherd, with XeCLI-maintained changes. |

The shared MIT terms are in `THIRD-PARTY-LICENSES/MIT.txt`. The required aggregate .NET notices are in `THIRD-PARTY-LICENSES/dotnet-10.0.0-THIRD-PARTY-NOTICES.txt`. Exact NuGet versions and content hashes are locked by the committed project lock files.

SharpCompress incorporates code based on XZ.NET, managed-lzma, and ZstdSharp. Its Bouncy Castle portions are Copyright (c) 2000-2011 The Legion of the Bouncy Castle and use the shared MIT terms.

## X360 corresponding source

`Assets/QuickBoot/X360.dll` is built from the complete corresponding source at `third_party/X360` in the matching public release tag. `PROVENANCE/CORRESPONDING-SOURCE.md` in each binary package links the exact tagged source.

- Upstream source: [mtolly/X360](https://github.com/mtolly/X360/tree/573dda3cd841ba370b2110567a6b4ee2b7099c9c)
- Upstream revision: `573dda3cd841ba370b2110567a6b4ee2b7099c9c`
- Maintained assembly version: `1.0.0.42`
- Distributed SHA-256: `F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9`

The maintained build removes retired network callbacks and artwork with unclear redistribution terms, fixes exact-4-KiB final-block handling, preserves XeCLI's existing QuickBoot package format, and produces a deterministic Release library. `SOURCE-MANIFEST.json` records the complete source and binary inventory.

A redundant historical README is not redistributed. The repository-level `LICENSE` supplies the GPLv3 terms for the maintained X360 source and binary, while `third_party/X360/src/X360/Resources/GPL30.txt` remains a required embedded build input.

## Bundled console components

The following console components shipped with XeCLI v1 and remain part of the supported XeLL and QuickBoot workflows:

| Released path | SHA-256 |
| --- | --- |
| `Assets/XellLaunch/xell.bin` | `E364FAD816CD651B1F27C1A7DCD5AE8A0C3A3C70BF11D04B6A66123A38AF2C9C` |
| `Assets/XellLaunch/default.xex` | `414492CE7DED99D9D8EFE7E35407168CDF4F357C0B4BBFF0CF6F27E473A7B2EC` |
| `Assets/QuickBoot/default.xex` | `C225F3E174FBF551D76BAA64567B75F018CF70D2DBA4929697A69E04DE1FE6C3` |

`xell.bin` is XeCLI's maintained XeLL Reloaded build and is distributed under GPL-2.0-only. Required GPLv2, Newlib, GCC Runtime Library Exception, BSD, and zlib notices ship under `THIRD-PARTY-LICENSES/XeLL`. `XellLaunch/default.xex` and `QuickBoot/default.xex` are original XeCLI components retained as first-party release assets.

Complete XeLL source and build provenance is under `XeCLI-XellFetch/source` in the matching public release tag linked by `PROVENANCE/CORRESPONDING-SOURCE.md`. Keeping source beside the tagged release avoids installing a large development tree on end-user systems. Checksums identify exact binaries and do not imply Authenticode signing.

## Material not included

XeCLI does not bundle a title database, console plugins supplied by users, downloaded homebrew packages, or generated `.live` files. Those remain separate from the release body and subject to their respective sources and terms.
