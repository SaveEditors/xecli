# Beginner Guide

This guide is for users who want to get from a clean setup to a working XeCLI session with minimal guesswork.

## What XeCLI Is For
XeCLI is designed to replace scattered one-off tools with a consistent command-line workflow for:
- Finding consoles on your LAN
- Saving a default target
- Checking live console state
- Browsing files
- Pulling XEX files and modules
- Reading memory
- Sending notifications

## Requirements
- An Xbox 360 RGH or JTAG console
- XBDM enabled on the console
- LAN access between your PC and the console
- Optional: JRPC2 for extended RPC features
- Optional: FTP credentials if you want FTP-backed file access

## First Run
Build or run the CLI, then discover consoles:
```bash
rgh start
```

If discovery is not needed, set the console directly:
```bash
rgh target --set <console-ip>
```

Confirm connectivity:
```bash
rgh ping
rgh status
```

## Understanding the Main Command Groups
- `status`, `profiles`, `title`: operational visibility and metadata
- `modules`, `mem`, `threads`, `debug`: live analysis and debugging
- `xex`, `ghidra`: static analysis workflows
- `fs`, `ftp`: file access
- `jrpc2`, `notify`: RPC-backed helpers
- `god`: ISO to Games on Demand conversion

## Common Early Tasks

### Check the Console State
```bash
rgh status
rgh status --quick
```

Use `--quick` when you only need a fast snapshot and do not want slower profile or JRPC2 checks.

### List Files
XBDM path example:
```bash
rgh fs list --path "HDD:\\"
```

FTP path example:
```bash
rgh ftp list --path "/Hdd1/"
```

### Download the Running XEX
```bash
rgh xex dump --out .\\running-title.xex
```

### Send a Notification
```bash
rgh notify --message "XeCLI connected"
```

## Target Persistence
XeCLI stores your default target in `%APPDATA%\XeCLI\config.json`. Once the target is set, most commands can omit `--ip`.

If you want to clear the saved target:
```bash
rgh target --clear
```

## FTP Setup
FTP settings are stored separately so users can work with different FTP credentials without hardcoding them in scripts.

Example:
```bash
rgh ftp target --set <console-ip> --port 21 --user <ftp-user> --pass <ftp-pass>
```

## Title ID Lookup
XeCLI ships with a bundled Title ID database. You do not need to download metadata to resolve common game names.

Lookup a title manually:
```bash
rgh title 4D5307E6
```

Read more:
- [Title ID Database](Title-ID-Database.md)

## Safe Next Steps
After you are comfortable with the basics, move to:
- [Commands Reference](Commands.md)
- [Advanced Guide](Advanced-Guide.md)
- [Troubleshooting](Troubleshooting.md)


