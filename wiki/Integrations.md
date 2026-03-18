# Integrations

This page is for developers who want to build other tools around XeCLI or reuse its bundled assets.

## Integration Modes
There are three practical ways to integrate with XeCLI:
- Shell out to rgh commands and consume text output
- Shell out to rgh commands with `--json`
- Read the bundled asset files directly, especially the Title ID database

## Recommended Approach
For most external tooling, the best approach is:
1. Use XeCLI for live console interactions
2. Use `--json` for structured responses
3. Use the bundled Title ID CSV directly for metadata resolution

This keeps the live-console logic in one place while letting your own tool stay focused on its domain.

## Good Candidate Integrations
- Save editors
- Trainers
- Dashboards
- Launchers
- Capture and archival tools
- Reverse engineering workflow scripts
- Metadata reporting tools

## JSON-Friendly Commands
Useful commands for structured output:
```bash
rgh status --json
rgh profiles --json
rgh title 4D5307E6 --json
rgh modules list --json
rgh mem regions --json
rgh mem strings --addr 0x82000000 --size 0x20000 --json
rgh ftp find --path "/Hdd1/" --name "*.xex" --json
rgh ghidra verify --dir .\\decomp --json
```

## When to Use XeCLI Instead of Reimplementing
Use XeCLI directly when you need:
- XBDM reconnect behavior
- XBDM or JRPC2 command handling
- XEX pull and decompile workflows
- Reusable JSON snapshots

Read bundled files directly when you need:
- Title metadata without opening a live console session
- Lightweight game-name lookup inside another app
- Consistent naming or labeling in reports

## Using the Bundled Title ID Files
Repo locations:
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

Published locations:
- `Assets/xbox360_gamelist.csv`
- `Assets/xbox360_titleids.txt`

Recommended pattern for another tool:
- Resolve the asset folder relative to your executable or the XeCLI executable
- Load the CSV as the primary source
- Fall back to the TXT list for simple lookups

## Script Integration Examples

### Status snapshot for a dashboard
```bash
rgh status --json
```

### Resolve a title before applying a game-specific workflow
```bash
rgh jrpc2 title-id
rgh title <title-id> --json
```

### Build a module inventory report
```bash
rgh modules list --json
```

### Use XeCLI as a XEX fetch helper
```bash
rgh xex dump --out .\\current.xex
```

## Release-Ready Integration Guidance
- Do not rely on machine-specific paths.
- Do not assume a specific local IP.
- Do not assume the user has a local override file.
- Prefer published asset-relative paths when packaging your integration.
- Treat XeCLI JSON as the stable automation surface where available.

## Questions to Ask Before Integrating
- Do you need live console access or only metadata
- Do you need XBDM, JRPC2, or both
- Do you need a real XEX file or only a module dump
- Do you need human-readable title names or raw Title IDs

Answering those questions usually tells you whether to call XeCLI, read the database directly, or do both.


