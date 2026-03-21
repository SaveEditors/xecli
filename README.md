<p align="center">
  <img src="assets/readme/xecli-logo.jpg" alt="XeCLI logo" width="320">
</p>

# XeCLI
[![GitHub stars](https://img.shields.io/github/stars/SaveEditors/xecli)](https://github.com/SaveEditors/xecli)
[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Xbox%20360%20RGH%2FJTAG-orange)](https://github.com/SaveEditors/xecli)

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit built for live console work. It combines XBDM, JRPC2, FTP, XEX tooling, memory inspection, module control, screenshot capture, Ghidra headless automation, and Games on Demand conversion in one CLI and one release package.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

Created by [Pew7s](https://www.se7ensins.com/members/pepe-le-pew.527865/).

<p align="center">
  <strong>Support our work? Buy us a coffee!</strong>
</p>
<p align="center">
  <a href="https://ko-fi.com/saveeditors">
    <img src="https://storage.ko-fi.com/cdn/kofi3.png?v=3" alt="Support XeCLI on Ko-fi" width="180">
  </a>
</p>

## Documentation
- [Wiki Home](wiki/Home.md)
- [Commands Reference](wiki/Commands.md)
- [CLI Help Output](wiki/CLI-Help.md)
- [Beginner Guide](wiki/Beginner-Guide.md)
- [Hardware and System Controls](wiki/Hardware-and-System.md)
- [XNotify](wiki/XNotify.md)
- [Avatar Item Collection](wiki/Avatar-Item-Collection.md)
- [Advanced Guide](wiki/Advanced-Guide.md)
- [Published Docs Site](https://saveeditors.github.io/xecli/wiki/Home.html)

## Release Changelog
### v1.0.1 Avatar Update
- Added hosted and local `Avatar-Item-Collection` support with `rgh avatar games`, `rgh avatar items`, `rgh avatar choose`, `rgh avatar browse`, `rgh avatar install`, and `rgh avatar apply`.
- Added terminal and Windows picker flows for avatar item selection, with current-user ownership patching, cached downloads, and multi-item progress bars.
- Added hardware and session controls including sign-in state, LED presets, fan commands, tray control, shutdown, native popup messages, and title-aware spoof helpers.
- Expanded the wiki with [Hardware and System Controls](wiki/Hardware-and-System.md), [XNotify](wiki/XNotify.md), and [Avatar Item Collection](wiki/Avatar-Item-Collection.md), and cleaned the wording across the public docs.

### v1.0.0 Initial Release
- Shipped the first public XeCLI release with the `rgh` command, XBDM discovery, console status, module inspection, memory inspection, thread control, and debug helpers.
- Added JRPC2 helpers for Title ID, temperatures, notifications, CPU key, motherboard, and dashboard queries, plus FTP-backed file, save, content, and plugin workflows.
- Added XEX dumping, string extraction, Ghidra headless integration, ISO to GOD conversion, and the bundled Title ID database in the release package.
- Published the initial wiki set including [Home](wiki/Home.md), [Commands Reference](wiki/Commands.md), [CLI Help Output](wiki/CLI-Help.md), [Beginner Guide](wiki/Beginner-Guide.md), [Advanced Guide](wiki/Advanced-Guide.md), and [Troubleshooting](wiki/Troubleshooting.md).

## Interface Preview
Top-level help:

![XeCLI help](assets/readme/rgh-help.png)

Live status:

![XeCLI status](assets/readme/rgh-status.png)

## Why XeCLI
XeCLI is designed for three kinds of work:

- Daily console workflows: discovery, connection management, status, FTP, plugin control, and title launching.
- Reverse engineering workflows: module listing, memory reads and writes, thread context, breakpoints, XEX dumping, strings, and Ghidra export.
- Tool integration workflows: JSON output, bundled metadata, and stable command-based orchestration from scripts or companion apps.

It is intended to replace scattered one-off console utilities with a consistent command surface that can be used interactively or scripted.

## What Ships
The repository and release package include:

- The CLI source and managed project dependencies.
- Common console-side `.xex` dependency files used with XeCLI workflows.
- A bundled Title ID database.
- Avatar item browsing and install workflows from either a local `Avatar-Item-Collection` corpus or the hosted GitHub-backed collection, with cached downloads and progress bars.
- Ghidra helper scripts.
- Wiki documentation and release-facing README content.

Bundled assets:

- `src/Xbox360.Remote.Cli/ConsoleDependencies/xbdm.xex`
- `src/Xbox360.Remote.Cli/ConsoleDependencies/XDRPC.xex`
- `src/Xbox360.Remote.Cli/ConsoleDependencies/JRPC2.xex`
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`
- `src/Xbox360.Remote.Cli/ghidra_scripts/DecompileAllToC.java`

At publish time, these assets are copied into the release output so the package remains self-contained. The hosted avatar corpus itself lives separately at [SaveEditors/Avatar-Item-Collection](https://github.com/SaveEditors/Avatar-Item-Collection).

## What the Console Must Provide
XeCLI does not replace console-side plugins or services. The target console must already provide the pieces you want to use:

- XBDM for console control, module enumeration, memory access, threads, breakpoints, screenshots, and file-system commands.
- JRPC2 for CPU key, temperatures, Title ID, dashboard version, motherboard type, notifications, sign-in helpers, LED control, and generic RPC.
- FTP service for FTP-backed file browsing, save management, content management, and DashLaunch plugin edits.

XeCLI is shipped as a self-contained local toolchain and now includes common console-side `.xex` dependency files in `ConsoleDependencies/` for packaging convenience, but those services still have to be installed and enabled on the target console.

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

- XBDM file-system access and FTP access.
- Save listing, extraction, and injection.
- Installed-content inventory and deletion.
- DashLaunch plugin listing and slot management.

XEX and analysis workflows:

- Dump the active XEX.
- Extract XEX strings from local files, FTP, or the running title.
- Run Ghidra headless analysis and decompile exports.
- Verify decompile output for bad-instruction placeholders.

Packaging and automation:

- ISO to Games on Demand conversion.
- Folder watchdog for unattended ISO processing.
- JSON output on automation-friendly commands.
- Command-based avatar browsing, remote-hosted downloads, and console-side avatar item installs.
- A bundled Title ID database that other tools can consume directly.

## Avatar Item Collection
XeCLI now exposes two user-facing avatar selection paths on top of the same install pipeline:

- `rgh avatar choose` for a terminal-first game and item picker
- `rgh avatar browse` for a Windows picker with title search, item filtering, and multi-select

Both feed the same ownership-patch and install flow used by `rgh avatar install`.

The same flow works against either:

- the local `Avatar-Item-Collection` corpus
- the hosted GitHub-backed collection, with local caching and verified downloads

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

On first interactive launch from a packaged build, XeCLI offers a one-time prompt to add its directory to the machine PATH. That operation requires administrator approval and makes `rgh` available in new terminals.

Manual install commands:

```powershell
rgh install
rgh install --machine-path
```

### From source
```powershell
git clone https://github.com/SaveEditors/xecli
cd XeCLI
dotnet build -c Release
dotnet run --project src/Xbox360.Remote.Cli -- --help
```

Source builds require the .NET 10 SDK/runtime. That requirement does not apply to the published `win-x64` release archive.

## Quick Start
Discover and select a console:

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

- `wiki/Home.md`
- `wiki/Beginner-Guide.md`
- `wiki/Commands.md`

Deep reference:

- `wiki/Advanced-Guide.md`
- `wiki/Frameworks.md`
- `wiki/Title-ID-Database.md`
- `wiki/Integrations.md`
- `wiki/Troubleshooting.md`
- `wiki/FAQ.md`

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
