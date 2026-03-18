# Frameworks and Internals

This page explains how XeCLI is structured internally and how the major subsystems relate to each other.

## High-Level Architecture
XeCLI is a command-line orchestration layer over several Xbox 360 access patterns:
- XBDM for live console inspection and control
- JRPC2 for high-level RPC and extra telemetry
- FTP for alternate file access
- Ghidra headless for static XEX analysis
- Local metadata assets for Title ID resolution

The important design choice is that the CLI is self-contained. Runtime metadata comes from bundled files or user-provided local overrides, not from remote fetch steps.

## XBDM Layer
XBDM is the primary transport. XeCLI uses it for:
- Console info and status
- Running XEX path resolution
- Module enumeration and dumping
- Memory reads and writes
- Thread listing and context reads
- Breakpoints and execution control
- File system operations
- Screenshot capture

Operational characteristics:
- Default port: `730`
- Automatic reconnect attempts: `3`
- Reconnect delay/countdown: `10` seconds
- Cancellation during reconnect: type `stop`

## JRPC2 Layer
JRPC2 is optional. If present, XeCLI exposes:
- CPU key retrieval
- Sensor reads
- Title ID lookup
- Dashboard version lookup
- Motherboard identification
- Notifications
- Function resolution and RPC calls

If JRPC2 is missing, XeCLI still remains useful for XBDM and FTP workflows.

## FTP Layer
FTP is used for:
- Alternative file listings and transfers
- Pulling XEX files from storage paths
- Recursive searches when XBDM is not the best fit

The FTP subsystem stores:
- Default IP
- Default port
- Default username
- Default password

This separation allows a user to keep XBDM targeting and FTP credentials aligned without hardcoding them into scripts.

## Ghidra Integration
XeCLI uses Ghidra headless to automate XEX analysis and decompile export.

What is included:
- A bundled post-script: `ghidra_scripts/DecompileAllToC.java`
- Config storage for Ghidra install path, Java path, and project path
- Validation via `ghidra verify`

Expected workflow:
1. Obtain a real XEX using `xex dump` or FTP.
2. Run `ghidra analyze` or `ghidra decompile`.
3. Validate outputs with `ghidra verify`.

## Title ID Database
Title metadata is provided by bundled files in the repository:
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

Load order:
1. Bundled CSV
2. Bundled TXT
3. Optional user override: `%APPDATA%\XeCLI\titleids.local.csv`

This means the release always has a working metadata baseline even on a machine with no prior configuration.

## Why the Title ID Database Matters
The database is more than a convenience label table. It provides reusable metadata for:
- Title name resolution in CLI output
- Media ID matching
- Region-aware labeling
- Disc and content metadata for external tools

It is suitable for reuse by:
- Trainers
- Launchers
- Dashboards
- Save editors
- Asset managers
- Reporting scripts

## Configuration Files
Main config:
- `%APPDATA%\XeCLI\config.json`

Cache:
- `%LOCALAPPDATA%\XeCLI\cache`

Optional local metadata extension:
- `%APPDATA%\XeCLI\titleids.local.csv`

## Build-Time Asset Inclusion
The CLI project file explicitly copies the bundled asset files into the output and publish directories. That keeps the release self-contained and avoids runtime metadata fetches.


