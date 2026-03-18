# XeCLI
[![GitHub stars](https://img.shields.io/github/stars/SaveEditors/xecli)](https://github.com/SaveEditors/xecli)
[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Xbox%20360%20RGH%2FJTAG-orange)](https://github.com/SaveEditors/xecli)

> A terminal-first Xbox 360 RGH/JTAG toolkit for discovery, XBDM control, RPC, file access, XEX workflows, memory inspection, and automation.

## Overview
XeCLI is built for operators, reverse engineers, and tool developers who want repeatable Xbox 360 workflows without relying on GUI-only tools. It combines XBDM, JRPC2, FTP, Ghidra headless integration, and a bundled Title ID database into one source tree and one release artifact.

The project ships with its own local metadata and helper assets. Nothing in the Title ID workflow depends on fetching remote data at runtime.
The repository and product name are `XeCLI`; the installed terminal command remains `rgh`.

## Features
- Console discovery, target persistence, and reconnect handling
- XBDM status, memory, modules, threads, breakpoints, screenshots, and file system access
- JRPC2 helpers for CPU key, temperatures, Title ID, motherboard, dashboard version, notifications, and RPC calls
- FTP access for alternate file operations and XEX retrieval
- XEX dump, string extraction, and Ghidra-driven decompile flows
- ISO to Games on Demand conversion and watchdog mode
- Bundled Title ID database for game-name resolution, region/media metadata, and downstream tool integration
- Structured JSON output for scripts, launchers, dashboards, and companion tools

## Bundled Assets
XeCLI includes the following repo-local assets:
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`
- `src/Xbox360.Remote.Cli/ghidra_scripts/DecompileAllToC.java`

At build and publish time, the asset files are copied into the CLI output so the release remains self-contained.

## What Ships vs What the Console Must Provide
What ships with XeCLI:
- Source code
- Managed runtime dependencies restored by the project
- Bundled metadata assets
- Ghidra helper script
- Release documentation

What must already exist on the target console:
- XBDM for live console control
- JRPC2 if you want RPC, notifications, CPU key, temperatures, and related helpers
- FTP service if you want FTP-backed file access

XeCLI is release-ready as a local toolchain, but it does not replace console-side plugins. Those remain console prerequisites.

## Title ID Database
The Title ID database is part of the repository and part of the release output.

Primary files:
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

What it contains:
- Title ID
- Media ID when available
- Game name
- Serial
- Content type
- Region
- XEX CRC when available
- Wave metadata when available

What it is useful for:
- Turning raw Title IDs into human-readable names in status views
- Matching disc variants by media ID
- Powering dashboards, trainers, save editors, and profile tools
- Resolving game metadata in external scripts and companion applications

Local overrides are optional and live at:
- `%APPDATA%\XeCLI\titleids.local.csv`

Bundled files are always loaded first. The local override file is additive and intended for custom or private entries.

## Installation
From source:
```bash
git clone https://github.com/SaveEditors/xecli
cd XeCLI
dotnet build -c Release
dotnet run --project src/Xbox360.Remote.Cli -- --help
```

Standalone build:
```bash
rgh.exe --help
rgh install
rgh install --machine-path
```

On first interactive launch from the packaged executable, XeCLI offers a one-time prompt to add its directory to the machine PATH with administrator approval.

## Quick Start
Discover consoles and choose a default target:
```bash
rgh start
```

Connect directly if you already know the IP:
```bash
rgh target --set <console-ip>
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
```

Check the console:
```bash
rgh ping
rgh status
rgh status --quick
```

Inspect modules and memory:
```bash
rgh modules list
rgh mem hexdump --addr 0x82000000 --size 0x200
rgh mem dump --addr 0x82000000 --size 0x20000 --out .\\mem.bin
```

Work with XEX files:
```bash
rgh xex dump --out .\\title.xex
rgh xex strings --running --min 6 --unicode
rgh xex decompile --running --out .\\decomp
```

## Documentation
The release-ready documentation set is intentionally limited to:
- `README.md`
- `wiki/`

Start with:
- `wiki/Home.md`
- `wiki/Commands.md`
- `wiki/Title-ID-Database.md`
- `wiki/Integrations.md`

## Configuration
Primary config file:
- `%APPDATA%\XeCLI\config.json`

Cache directory:
- `%LOCALAPPDATA%\XeCLI\cache`

## Release Notes
The repository is structured so that the CLI, bundled assets, and wiki can be released together without relying on machine-specific paths or external documentation files.

## License
GPLv3


