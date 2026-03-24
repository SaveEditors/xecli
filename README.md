<p align="center">
  <img src="assets/readme/xecli-logo.jpg" alt="XeCLI logo" width="320">


# XeCLI
[![GitHub stars](https://img.shields.io/github/stars/SaveEditors/xecli)](https://github.com/SaveEditors/xecli)
[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Xbox%20360%20RGH%2FJTAG-orange)](https://github.com/SaveEditors/xecli)
</p>
XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit built for live console work. It combines XBDM, JRPC2, FTP, avatar browsing and remote avatar downloads, XEX tooling, memory inspection, module control, screenshot capture, Ghidra headless automation, pinned IDA Pro 9.1 headless reverse-engineering helpers, and Games on Demand conversion in one CLI and one release package. The same command surface also works cleanly from terminal agents such as Claude and Codex when you want repeatable console automation instead of a GUI-only workflow.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

Ghidra remains an external `(Free)` dependency, and XeCLI's supported Ghidra XEX import path uses the maintained [`SaveEditors/XEXLoaderWV`](https://github.com/SaveEditors/XEXLoaderWV) fork. The IDA workflow is pinned to `IDA Pro 9.1.250226` with [`idaxex`](https://github.com/emoose/idaxex) `0.42b`, and IDA Pro is required if you want the IDA debugger/decompiler workflow.

Created by Pew7s.

<p align="center">
  <strong>Support our work? Buy us a coffee!</strong>
</p>
<p align="center">
  <a href="https://ko-fi.com/saveeditors">
    <img src="https://storage.ko-fi.com/cdn/kofi3.png?v=3" alt="Support XeCLI on Ko-fi" width="180">
  </a>
</p>

## Documentation
- [Wiki Home](https://saveeditors.github.io/xecli/wiki/Home.html)
- [Latest Features](https://saveeditors.github.io/xecli/wiki/Latest-Features.html)
- [Releases](https://saveeditors.github.io/xecli/wiki/Releases.html)
- [FTP and File Transfer](https://saveeditors.github.io/xecli/wiki/FTP-and-File-Transfer.html)
- [Commands Reference](https://saveeditors.github.io/xecli/wiki/Commands.html)
- [CLI Help Output](https://saveeditors.github.io/xecli/wiki/CLI-Help.html)
- [Beginner Guide](https://saveeditors.github.io/xecli/wiki/Beginner-Guide.html)
- [Hardware and System Controls](https://saveeditors.github.io/xecli/wiki/Hardware-and-System.html)
- [XNotify](https://saveeditors.github.io/xecli/wiki/XNotify.html)
- [Homebrew and USB](https://saveeditors.github.io/xecli/wiki/Homebrew-and-USB.html)
- [Original Xbox Compatibility](https://saveeditors.github.io/xecli/wiki/Original-Xbox-Compatibility.html)
- [Fatman](https://saveeditors.github.io/xecli/wiki/FATX-Manager.html)
- [Avatar Item Collection](https://saveeditors.github.io/xecli/wiki/Avatar-Item-Collection.html)
- [Advanced Guide](https://saveeditors.github.io/xecli/wiki/Advanced-Guide.html)
- [Reverse Engineering](https://saveeditors.github.io/xecli/wiki/Reverse-Engineering.html)
- [Integrations](https://saveeditors.github.io/xecli/wiki/Integrations.html)
- [Published Docs Site](https://saveeditors.github.io/xecli/wiki/)

## High-Value Workflows
- FTP and file work: save a target once, then browse, search, pull, push, rename, and delete directly from `rgh`.
- Pulled content repair: inspect local CON/profile/GPD files, rehash/resign packages, repair dashboard title records, and edit profile achievements, settings, and avatar colors without leaving XeCLI.
- Avatar downloader and installer: use the local or hosted `Avatar-Item-Collection`, browse by game or item, cache remote packages, patch ownership, and install to the console.
- Claude/Codex-friendly automation: drive `rgh` from terminal agents, shell scripts, or companion tools with explicit commands and `--json` output where supported.
- Live debugging and reverse engineering: inspect modules, memory, threads, breakpoints, screenshots, XEX dumps, and both Ghidra and IDA output without switching tools.

For the current feature surface beyond this short summary, use the wiki's [Latest Features](https://saveeditors.github.io/xecli/wiki/Latest-Features.html) page.

## Recent Releases
- `v1.0.5` local content and profile workflows: native `rgh con`, `rgh profile`, and `rgh xdbf` commands for pulled packages, profile edits, avatar colors, and dashboard title-record repair. [Release notes](https://saveeditors.github.io/xecli/wiki/Releases.html#v105-local-content-and-release-polish) · [GitHub release](https://github.com/SaveEditors/xecli/releases/tag/v1.0.5)
- `v1.0.4` IDA and Fatman expansion: headless IDA Pro 9.1 support, Ghidra helper-loader install flows, and expanded FATX/Fatman image tooling. [Release notes](https://saveeditors.github.io/xecli/wiki/Releases.html#v104-ida-and-fatman-expansion) · [GitHub release](https://github.com/SaveEditors/xecli/releases/tag/v1.0.4)
- Full release notes: [Wiki Releases](https://saveeditors.github.io/xecli/wiki/Releases.html) · [GitHub Releases](https://github.com/SaveEditors/xecli/releases)

## Known Issues
- Spoofing is still under active work. The current spoofing commands can be unstable or incomplete depending on the title, so expect issues while that feature set is being finished.

## Interface Preview
Top-level help:

![XeCLI help](assets/readme/rgh-help.png)

Live status:

![XeCLI status](assets/readme/rgh-status.png)

## Why XeCLI
XeCLI is designed for three kinds of work:

- Daily console workflows: discovery, connection management, status, FTP, plugin control, and title launching.
- Reverse engineering workflows: module listing, memory reads and writes, thread context, breakpoints, XEX dumping, strings, Ghidra export, and IDA Pro 9.1 headless import/decompile flows.
- Tool integration workflows: JSON output, bundled metadata, and stable command-based orchestration from scripts or companion apps.

It is intended to replace scattered one-off console utilities with a consistent command surface that can be used interactively or scripted.

## What Ships
The repository and release package include:

- The CLI source and managed project dependencies.
- Common console-side `.xex` dependency files used with XeCLI workflows.
- A bundled Title ID database.
- Avatar item browsing and install workflows from either a local `Avatar-Item-Collection` corpus or the hosted GitHub-backed collection, with cached downloads, progress bars, and metadata-first item names instead of raw content-ID labels.
- Ghidra and IDA helper scripts.
- Wiki documentation and release-facing README content.

Bundled assets:

- `xbdm.xex`
- `XDRPC.xex`
- `JRPC2.xex`
- `src/Xbox360.Remote.Cli/ConsoleDependencies/xbdm.xex`
- `src/Xbox360.Remote.Cli/ConsoleDependencies/XDRPC.xex`
- `src/Xbox360.Remote.Cli/ConsoleDependencies/JRPC2.xex`
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`
- `src/Xbox360.Remote.Cli/ghidra_scripts/DecompileAllToC.java`
- `src/Xbox360.Remote.Cli/ida_scripts/*.py`

Published releases include these assets so the package stays self-contained. The hosted avatar corpus itself lives separately at [SaveEditors/Avatar-Item-Collection](https://github.com/SaveEditors/Avatar-Item-Collection).
XeCLI does not bundle Ghidra or IDA Pro themselves. Ghidra is `(Free)`. IDA Pro `9.1.250226` is required for the IDA debugger/decompiler workflow. After you configure each tool path, XeCLI can install the supported XEX loader helpers with `rgh ghidra install-loader` and `rgh ida install-loader`.

## Credits
- [`SaveEditors/XEXLoaderWV`](https://github.com/SaveEditors/XEXLoaderWV) is the maintained external Ghidra XEX loader fork used by XeCLI's headless Ghidra workflow and `rgh ghidra install-loader`, based on Warranty Voider's original `zeroKilo/XEXLoaderWV` project.
- [`idaxex`](https://github.com/emoose/idaxex) by emoose is the pinned external XEX loader project used by XeCLI's supported IDA workflow.

## What the Console Must Provide
XeCLI ships the expected console-side plugin payload in the repository and release package so users can deploy the exact versions the tool was built and tested against:

- `xbdm.xex`
- `JRPC2.xex`
- `XDRPC.xex`

The target console still needs those services enabled for the related features to work:

- XBDM for console control, module enumeration, memory access, threads, breakpoints, screenshots, and file-system commands.
- JRPC2 for CPU key, temperatures, Title ID, dashboard version, motherboard type, notifications, sign-in helpers, LED control, and generic RPC.
- FTP service for FTP-backed file browsing, save management, content management, and DashLaunch plugin edits.

## Feature Summary
Core console workflows:

- Console discovery and saved default targeting.
- Fast `status`, `ping`, `profiles`, and `title` queries.
- Sign-in state inspection with gamertag and XUID output.
- Ring-of-light LED control with cached state display.
- Manual fan command dispatch and optional SMC version probing.
- Launch and reboot control with optional on-console success notifications.

Live inspection and debugging:

- Module list, info, dump, load, unload, and pending verification.
- Memory dump, hexdump, peek, poke, watch, strings, and pattern search with optional freeze writes.
- Thread list, context, suspend, and resume.
- Debug stop/go, code breakpoints, data breakpoints, and live debug-event watch.

File and content workflows:

- XBDM file-system access and saved-target FTP workflows.
- FTP list, find, get, put, cat, mkdir, move, and delete operations through `rgh ftp ...`.
- Save listing, extraction, and injection.
- Installed-content inventory and deletion.
- DashLaunch plugin listing and slot management.
- Avatar library browsing, hosted remote downloads, ownership patching, and console-side installs.
- Public homebrew package staging for Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch through `rgh homebrew install <package>`, either to USB/folder targets or directly onto detected console drives, with a confirmation prompt unless `--auto-confirm` is used.
- Original Xbox compatibility staging and install through `rgh ogxbox install <hacked|hud|retail>`, with public XeFu pack downloads, optional HDD Compatibility Partition Fixer support, and `HddX:\Compatibility` targeting.
- Fatman image and Windows disk workflows for scan, manual-open, extraction, mutation, FATX formatting, metadata backup/restore, and safe chain-map repair.

XEX and analysis workflows:

- Dump the active XEX.
- Extract XEX strings from local files, FTP, or the running title.
- Run Ghidra headless analysis and decompile exports.
- Run IDA Pro 9.1.250226 headless import, decompile, and verify workflows.
- Verify decompile output for bad-instruction placeholders.

Packaging and automation:

- ISO to Games on Demand conversion.
- Folder watchdog for unattended ISO processing.
- JSON output on automation-friendly commands.
- Command-based orchestration from scripts, Claude, Codex, or companion tools.
- Command-based avatar browsing, remote-hosted downloads, and console-side avatar item installs.
- A bundled Title ID database that other tools can consume directly.

## Avatar Item Collection
XeCLI now exposes two user-facing avatar selection paths on top of the same install pipeline:

- `rgh avatar choose` for a terminal-first game and item picker
- `rgh avatar browse` for a Windows picker with title search, item filtering, and multi-select

Both feed the same ownership-patch and install flow used by `rgh avatar install`.

The same flow works against either:

- the local `Avatar-Item-Collection` corpus
- the hosted GitHub-backed collection, with local caching, verified downloads, and the same metadata-first item naming used by the local corpus

Remote browsing examples:

```powershell
rgh avatar library show
rgh avatar games --remote --search "Black Ops"
rgh avatar items --remote --titleid 415608C3 --limit 10
rgh avatar choose --remote --search "Black Ops" --current-user
rgh avatar browse --remote
rgh avatar install --remote --titleid 415608C3 --all --current-user
```

Multi-item installs show progress bars and per-item transfer status so large title packs stay visible during download and upload. The hosted corpus lives at [SaveEditors/Avatar-Item-Collection](https://github.com/SaveEditors/Avatar-Item-Collection).

## Installation
### Release package
Download the release archive, extract it, and run `rgh.exe`.

Release contents:

- `rgh.exe`
- native .NET runtime files bundled beside `rgh.exe`
- `ConsoleDependencies/`
- `Assets/`
- `ghidra_scripts/`

The release package is self-contained for `win-x64`. It does not require a separate .NET runtime install on the target PC.

For a first-time install, open a terminal in the extracted release folder and run:

```powershell
.\rgh.exe install
```

The installer now walks through:

- install scope: current user or all users
- install directory selection
- optional PATH registration for new terminals
- a silent console discovery pass after setup

If a console is found after installation, XeCLI can immediately ask whether to connect and then run `rgh status` on the detected target.

After installation, open a new terminal and use `rgh` normally.

Manual install commands:

```powershell
.\rgh.exe install
rgh install
rgh install --path C:\Tools\XeCLI
rgh install --machine
```

### Homebrew package install and staging
XeCLI also exposes a dedicated `homebrew` group to either stage public homebrew packages onto a USB drive or folder, or install them directly onto a detected console drive:

```powershell
rgh homebrew install aurora --usb E:
rgh homebrew install aurora --device Hdd1 --ini-mode merge
rgh homebrew install all --device Hdd1 --ini-mode generated --auto-confirm
rgh ogxbox install hacked --usb E:
rgh ogxbox install hud --include-fixer
rgh homebrew install dashlaunch --usb E:
rgh homebrew install xexmenu --usb E:
rgh homebrew install fsd --usb E:
rgh homebrew install all --usb E: --auto-confirm
```

When `--usb` is present, XeCLI stages the downloaded packages onto the selected USB drive or folder.

When `--usb` is omitted, XeCLI connects to the console over FTP, detects only `Hdd1`, `Usb0`, `Usb1`, and `Usb2`, asks which detected drive to use, and then installs the selected packages directly onto that console drive.

Console install mode also asks how `launch.ini` should be handled:

- `generated` writes a fresh XeCLI `launch.ini`
- `merge` keeps the existing file and adds or updates the bundled plugin entries
- `skip` installs the homebrew only and leaves `launch.ini` alone

Read [Homebrew and USB](https://saveeditors.github.io/xecli/wiki/Homebrew-and-USB.html) for the full workflow.

### Original Xbox compatibility install and staging
XeCLI also exposes a dedicated `ogxbox` group for original Xbox backwards-compatibility files:

```powershell
rgh ogxbox list
rgh ogxbox install hacked --usb E:
rgh ogxbox install hud --include-fixer --usb E:
rgh ogxbox install retail
```

When `--usb` is present, XeCLI stages a `Compatibility\` folder plus the optional `HddCompatibilityPartitionFixer\` helper to the selected USB drive or folder.

When `--usb` is omitted, XeCLI connects over FTP and targets `HddX:\Compatibility` directly. If `HddX` is missing, rerun with `--include-fixer` so XeCLI also stages the HDD Compatibility Partition Fixer onto a writable console drive.

Read [Original Xbox Compatibility](https://saveeditors.github.io/xecli/wiki/Original-Xbox-Compatibility.html) for the XeFu set differences and the full `HddX` workflow.

### From source
```powershell
git clone https://github.com/SaveEditors/xecli
cd XeCLI
dotnet build -c Release
dotnet run --project src/Xbox360.Remote.Cli -- --help
```

Source builds require the .NET 10 SDK/runtime. That requirement does not apply to the published `win-x64` release archive.

## Fatman
Fatman is XeCLI's FATX manager for local Xbox 360 disks and images. The current implementation supports image-backed inspection, recovery, partition dumping, search, export, and direct FATX file-system mutations inside an image. Windows-only mount, format, repartition, repair, and low-level metadata work remain separate layers.

Current workflow:

- detect FATX-capable disks or image sources
- inspect partitions and volume details
- browse directories and locate entries
- search by name or path
- recover data from `.img` and `.bin` sources
- create FATX directories inside an image
- write host files into a FATX image
- rename or move FATX entries inside an image
- remove FATX files or directories inside an image
- print small files in the terminal
- dump partitions to a host directory
- export selected files or directory trees to the host

Current command surface:

```powershell
rgh fatman devices
rgh fatman partitions
rgh fatman scan
rgh fatman info
rgh fatman list
rgh fatman find
rgh fatman cat
rgh fatman get
rgh fatman mkdir
rgh fatman put
rgh fatman mv
rgh fatman rm
rgh fatman extract
rgh fatman dump
```

`rgh fatx` remains a supported alias for the same command group.

Manual-open workflow:

- use `--offset` to open a FATX/XTAF volume directly from a byte offset
- use `--length` with `--offset` when you want to clamp the manual partition range
- offsets and lengths accept decimal bytes or `0x`-prefixed hex
- use `rgh fatman scan` first when you need help finding plausible FATX/XTAF header offsets

Examples:

```powershell
rgh fatman scan --image .\hdd.img
rgh fatman info --image .\hdd.img --offset 0xB6600000
rgh fatman list --image .\hdd.img --offset 0xB6600000 --path /
rgh fatman mkdir --image .\hdd.img --path /XeCLI
rgh fatman put --image .\hdd.img --path /XeCLI/readme.txt --in .\readme.txt --overwrite
rgh fatman mv --image .\hdd.img --path /XeCLI/readme.txt --to /XeCLI/readme-old.txt
rgh fatman rm --image .\hdd.img --path /XeCLI --recursive
rgh fatman dump --image .\hdd.img --offset 0xB6600000 --length 0x10000000 --out .\partition-dump
```

Read [Fatman](https://saveeditors.github.io/xecli/wiki/FATX-Manager.html) for the read-only scope and the current documentation shell.

Validation note:

- the runtime has been verified against a synthetic FATX fixture image
- manual-open and scan flows were also validated against a nonstandard AMPED HDD image, where `Fatman` surfaced real `XTAF` offsets and opened the compatibility volume by bounded offset

## Quick Start
Recommended first run:

```powershell
.\rgh.exe install
```

After setup completes, XeCLI can silently discover consoles and offer to connect immediately. If you skip that prompt, use the manual discovery path below.

Manual discovery and selection:

```powershell
rgh start
```

If you already know the target:

```powershell
rgh target --set <console-ip>
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
```

Check the console:

```powershell
rgh ping
rgh status
rgh status --quick
rgh title
```

Inspect live state:

```powershell
rgh modules list
rgh modules info --name Aurora.xex
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh screenshot --out .\screen.bmp
```

Work with saves, content, and plugins:

```powershell
rgh save list --titleid FFFE07D1 --device Hdd1
rgh content list --device Hdd1 --show-types
rgh plugin list
```

Run analysis workflows:

```powershell
rgh xex dump --out .\title.xex
rgh xex strings --running --unicode --min 6
rgh ghidra decompile --running --out .\decomp
```

## Active Title Resolution
`rgh title` with no arguments resolves the currently active title from the connected console.

Examples:

```powershell
rgh title
rgh title --json
rgh title 415608C3
rgh title 415608C3 2B7302D6
```

When the running XEX is a dashboard replacement or homebrew shell, XeCLI can prefer a path-based fallback name such as `Aurora` while still showing the bundled database entry separately.

## Module Load and Unload
Live module unload is supported and verified against the live module list. Module load is more nuanced: some plugin stacks and modules do not hot-load safely on every console.

Supported commands:

```powershell
rgh modules load --path Hdd:\HvP2.xex
rgh modules unload --name HvP2.xex --force
rgh modules pending
```

For modules that trigger a reboot or disconnect as part of load, use:

```powershell
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
```

Then, after the console returns:

```powershell
rgh modules pending
```

That flow persists pending verification in the XeCLI config and confirms the final module state after reboot.

## Bundled Title ID Database
XeCLI includes its Title ID data in the repository and in the published release. It does not fetch title metadata from the internet at runtime.

Primary files:

- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

Optional local extension file:

- `%APPDATA%\XeCLI\titleids.local.csv`

The bundled data is useful beyond the CLI itself. Other tools can reuse it to:

- Resolve Title IDs into readable names.
- Match media-specific variants.
- Label saves, dumps, screenshots, and reports.
- Enrich launchers, dashboards, trainers, and save editors.

## Configuration
Primary config file:

- `%APPDATA%\XeCLI\config.json`

Cache directory:

- `%LOCALAPPDATA%\XeCLI\cache`

The config stores items such as:

- Default XBDM target.
- Default FTP target and credentials.
- Notification icon presets.
- Ghidra path configuration.
- Pending module operations that need post-reboot verification.

## Release Layout
The source repository is intended to stay clean:

- Source under `src/`
- Docs under `README.md` and `wiki/`
- No runtime dumps or captures checked into the repo
- No machine-specific paths in release documentation

Release artifacts should be built outside the repository root so publish output, dumps, screenshots, and smoke-test files do not pollute the source tree.

## Documentation Map
Start here:

- [Wiki Home](https://saveeditors.github.io/xecli/wiki/Home.html)
- [Beginner Guide](https://saveeditors.github.io/xecli/wiki/Beginner-Guide.html)
- [Commands Reference](https://saveeditors.github.io/xecli/wiki/Commands.html)

Deep reference:

- [Advanced Guide](https://saveeditors.github.io/xecli/wiki/Advanced-Guide.html)
- [Frameworks and Architecture](https://saveeditors.github.io/xecli/wiki/Frameworks.html)
- [Title ID Database](https://saveeditors.github.io/xecli/wiki/Title-ID-Database.html)
- [Integrations](https://saveeditors.github.io/xecli/wiki/Integrations.html)
- [Troubleshooting](https://saveeditors.github.io/xecli/wiki/Troubleshooting.html)
- [FAQ](https://saveeditors.github.io/xecli/wiki/FAQ.html)

## Safety Notes
XeCLI includes both safe operational commands and commands that can destabilize the running title or console session.

Treat these areas with care:

- `mem poke`
- `mem search --freeze`
- `debug break` and `debug databreak`
- `modules unload --force`
- `modules load --system`
- `reboot`
- content and save deletion commands

If a command is expected to trigger a reboot or disconnect, XeCLI should say so explicitly. Use the pending-verification flow where appropriate instead of assuming the operation completed cleanly.

## License
GPLv3
