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
The bundled database is loaded from local assets, not fetched remotely.

Load sources:

1. `Assets/xbox360_gamelist.csv`
2. `Assets/xbox360_titleids.txt`
3. `%APPDATA%\XeCLI\titleids.local.csv`

This gives XeCLI a stable metadata base while still allowing private overrides.

## Active Title Naming
The active-title pipeline does more than a raw database lookup:

1. Resolve Title ID from the live console when possible.
2. Resolve the running XEX path from XBDM.
3. Look up the Title ID in the bundled database.
4. If the database entry is too generic, prefer a path-based fallback name for the displayed title.

This is why `rgh title` can show `Aurora` while still exposing the underlying `Xbox 360 Dashboard` database entry.

## Pending Module Verification
Module load and unload do not always behave like a clean request-response API on a live RGH console. XeCLI tracks pending module operations in `%APPDATA%\XeCLI\config.json` when a reboot or disconnect is part of the expected flow.

Tracked fields include:

- action
- module name
- module path
- thread mode
- creation time

`rgh modules pending` and `rgh status` can then verify the post-reboot state instead of guessing.

## Reverse-Engineering Integration
XeCLI uses Ghidra and IDA Pro as external headless analysis backends.

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
A clean release should contain:

- `rgh.exe`
- `Assets/`
- `ghidra_scripts/`
- `ida_scripts/`

The source repo should not contain:

- captured screenshots
- memory dumps
- publish output
- smoke-test artifacts

## Design Goals
The project is optimized for:

- direct operator workflows
- automation-safe command surfaces
- explicit handling of dangerous operations
- reuse by other tools

That is why the project keeps the command line advanced but organizes it by namespaces instead of flattening everything into one giant help screen.
