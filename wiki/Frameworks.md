# Frameworks and Architecture

This page explains how XeCLI is structured and how the major subsystems fit together.

## Command Layer
The CLI surface is implemented in `src/Xbox360.Remote.Cli/`. Commands are grouped by operational area and exposed through the `rgh` command.

Important command groups:

- Core console workflows
- XBDM-backed live operations
- JRPC2-backed RPC helpers
- FTP-backed file and content workflows
- Ghidra headless orchestration
- Games on Demand conversion

## XBDM Transport
XBDM is the primary transport and the backbone of the live-console feature set.

XeCLI uses XBDM for:

- Console identity and status
- Running XEX path resolution
- Module list, info, and dumps
- Memory reads and writes
- Thread control
- Breakpoints and debug events
- Screenshots
- XBDM-backed file operations

Recent hardening in this area focuses on:

- fail-fast connect/read timeouts
- one-shot fast connection paths for commands that should never hang for a minute
- clear handling for disconnect-prone operations such as module load

## JRPC2 Layer
JRPC2 is optional but important. When present, XeCLI uses it for:

- Title ID
- dashboard version
- motherboard type
- CPU key
- temperatures
- notifications
- generic ordinal/address RPC calls

The CLI is designed so a missing JRPC2 plugin degrades functionality instead of making the entire tool unusable.

## FTP Layer
FTP is used where the storage tree matters more than live debugger state:

- browsing files
- transferring saves
- listing installed content
- editing `launch.ini`
- pulling large files without XBDM-specific limitations

FTP-backed commands are separate on purpose. That keeps it clear which operations depend on console-side FTP service versus XBDM.

## Title ID Database
Title metadata is optional, local, and operator supplied. XeCLI does not ship or download a title database.

One user source is selected in this order:

1. per-command `rgh title --database <PATH>`
2. `XECLI_TITLE_DATABASE`
3. `TitleDatabasePath` in `config.json`
4. `titleids.csv` in the active XeCLI configuration directory
5. legacy `titleids.local.csv` in that same directory

The configuration directory is `%APPDATA%\XeCLI` when installed, package-local `UserData` in portable mode, or the directory selected by `XECLI_HOME`. A lower-priority `titleids.discovered.csv` cache can hold non-generic names observed from live XEX paths. A user source replaces cached rows by Title ID. CSV/TXT parse warnings are retained without preventing startup or raw-ID operation.

## Active Title Naming
The active-title pipeline does more than a raw database lookup:

1. Resolve Title ID from the live console when possible.
2. Resolve the running XEX path from XBDM.
3. Look up the Title ID in the selected user file and local discovery cache.
4. If no trusted metadata exists, use a non-generic live XEX name or the raw hexadecimal Title ID.

This is why `rgh title` can show `Aurora` when the path supports it and still remain fully usable as `0xXXXXXXXX` when no database is configured.

## Pending Module Verification
Module load and unload do not always behave like a clean request-response API on a live RGH console. XeCLI tracks pending module operations in the active `config.json` when a reboot or disconnect is part of the expected flow.

Tracked fields include:

- action
- module name
- module path
- thread mode
- creation time

`rgh modules pending` and `rgh status` can then verify the post-reboot state instead of guessing. The record follows the active storage mode: installed configuration under `%APPDATA%\XeCLI`, package-local `UserData` in portable mode, or the directory selected by `XECLI_HOME`.

## Reverse-Engineering Integration
XeCLI uses Ghidra and IDA Pro as external headless analysis tools.

What XeCLI adds:

- consistent command-line entry points
- running-title and FTP-backed source acquisition
- helper-loader installation
- decompile export orchestration
- verification of output quality

What XeCLI does not do:

- replace Ghidra
- replace IDA Pro
- provide its own decompiler
- ship Ghidra itself
- ship IDA Pro itself

## Release Layout
A published package contains one self-contained `win-x64` application tree. Its principal entries are:

- `rgh.exe` and `XeTerminal.exe`
- the managed assemblies, native libraries, and .NET runtime files required by those executables
- `Assets/`, including the XeLL launch and QuickBoot payloads used by documented commands
- `ghidra_scripts/` and `ida_scripts/`
- `LICENSE`, `THIRD-PARTY-NOTICES.md`, and the minimal required files under `THIRD-PARTY-LICENSES/`
- `PROVENANCE/`, containing payload checksums and directions to the exact matching-tag source
- `release-manifest.json` and `xecli-owned-files.txt` for package integrity and safe upgrades

The portable ZIP also includes the empty `xecli.portable` marker. The setup installer deliberately omits that marker so installed copies use Windows profile storage. `ConsoleDependencies/` is not supplied by XeCLI; operators may add permitted console-side plugin files there for homebrew staging.

## Design Goals
The project is optimized for:

- direct operator workflows
- automation-safe command surfaces
- explicit handling of dangerous operations
- reuse by other tools

That is why the project keeps the command line advanced but organizes it by namespaces instead of flattening everything into one giant help screen.
