# Integrations

This page is for developers who want to build other tools around XeCLI or reuse its shipped assets.

## Integration Modes
There are three useful ways to integrate with XeCLI:

1. Call the CLI directly.
2. Parse its JSON output.
3. Read the bundled metadata files directly.

Use the smallest integration surface that solves the problem.

## Use the CLI Directly
XeCLI is useful as an orchestration backend when your tool needs live console interaction but you do not want to duplicate transport code.

Good examples:

- launchers that need title and module metadata
- dashboards that need status snapshots
- save tools that need extraction and injection
- reverse-engineering helpers that need XEX pulls or Ghidra automation

Typical calls:

```powershell
rgh status --json
rgh title --json
rgh modules list --json
rgh content list --json
```

## Parse JSON Output
Prefer `--json` when you need stable machine-readable output instead of screen scraping terminal tables.

Common commands with useful JSON output:

- `status`
- `profiles`
- `title`
- `scan`
- `modules list`
- `modules info`
- `mem regions`
- `mem strings`
- `content list`
- `ghidra verify`

## Reuse the Title ID Database
Bundled files:

- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

External tools can consume these files directly to:

- resolve Title IDs
- annotate content inventory
- label saves and dumps
- enrich UI views with media and region data

That database is shipped with the repo and the release output. There is no runtime fetch requirement.

## Use XeCLI as a Console Worker
Example pattern:

```powershell
rgh xex dump --out .\title.xex
rgh screenshot --out .\screen.bmp
rgh save extract --titleid 415608C3 --out .\saves
```

This works well when your application wants the files that XeCLI can pull rather than embedding transport logic itself.

## Ghidra Integration Pattern
If your tool needs decompile output but you do not want to own a Ghidra automation layer:

```powershell
rgh ghidra decompile --in .\title.xex --out .\decomp
rgh ghidra verify --dir .\decomp --json
```

This keeps import, decompile, and verification logic in one place.

## Config and Override Files
Runtime config:

- `%APPDATA%\XeCLI\config.json`

Optional user metadata override:

- `%APPDATA%\XeCLI\titleids.local.csv`

If you build another tool around XeCLI, do not hardcode machine-specific paths. Resolve config and asset paths relative to the XeCLI executable or the documented per-user config locations.

## When to Reuse XeCLI Instead of Rewriting It
Use XeCLI directly when:

- you want stable operator-facing commands
- you need live XBDM/JRPC2/FTP coordination
- you want the shipped metadata and Ghidra flow
- you need a tool that can still be used manually in terminal

Reimplement only if:

- you need a library API instead of a process boundary
- you must own every transport and retry detail internally
- your application cannot tolerate CLI process orchestration
