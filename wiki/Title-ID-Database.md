# Title ID Database

This page documents the bundled Title ID database that ships with XeCLI and explains how other tools can consume it.

## Purpose
The Title ID database exists to translate low-level Xbox 360 title metadata into something useful for humans and downstream tools.

It is used by XeCLI to:
- Resolve Title IDs to game names
- Improve status output
- Return readable results in `rgh title`
- Support automation and external integrations

It can also be used independently of XeCLI by other tooling.

## What Ships in the Repo
The database is included directly in the repository:
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

It is also copied into the built CLI output:
- `Assets/xbox360_gamelist.csv`
- `Assets/xbox360_titleids.txt`

This is important for release readiness. The database is provided with the project. XeCLI does not need to fetch it from the internet at runtime.

## File Roles

### xbox360_gamelist.csv
This is the richer metadata source.

Typical fields:
- Game name
- Title ID
- Serial
- Type
- Region
- XEX CRC
- Media ID
- Wave

This file is the best choice if you want structured metadata in another tool.

### xbox360_titleids.txt
This is a simpler fallback list using a compact format:
`TITLEID~Name`

This is useful when:
- You only need Title ID to name resolution
- You want a lightweight fallback source
- You are building a quick parser

## Load Order in XeCLI
XeCLI loads data in this order:
1. `Assets/xbox360_gamelist.csv`
2. `Assets/xbox360_titleids.txt`
3. `%APPDATA%\XeCLI\titleids.local.csv`

The local file is optional and intended for:
- Private homebrew entries
- Internal test builds
- Corrections or additions without modifying the bundled files

## What External Tools Can Do With It
The database is useful for far more than just `rgh title`.

Examples:
- Save editors can label saves by Title ID
- Trainers can resolve active titles before enabling offsets
- Dashboards can show clean game names in logs and history
- Reporting tools can annotate dump folders automatically
- XEX managers can group titles by region or media ID
- Screenshot or capture tools can name outputs using resolved titles

## Recommended Consumption Pattern
If you are building another tool, use this approach:
1. Read the CSV first.
2. Match on Title ID.
3. If you also have Media ID, prefer the row with the matching Media ID.
4. Fall back to the TXT list only if the CSV has no match.
5. Optionally merge a local override file for private entries.

## CSV Integration Example
Suggested fields to preserve in downstream tools:
- `Name`
- `TitleId`
- `MediaId`
- `Serial`
- `Type`
- `Region`
- `XexCrc`
- `Wave`

Suggested uses:
- UI labels
- Folder naming
- Report enrichment
- Per-title configuration routing

## Local Overrides
Optional override file:
- `%APPDATA%\XeCLI\titleids.local.csv`

Use it for:
- Custom homebrew Title IDs
- Unreleased builds
- Project-local corrections

The local override file follows the same CSV layout that XeCLI already understands.

## Best Practices
- Treat the bundled CSV as the canonical shipped dataset.
- Preserve Media ID when available; it helps distinguish variants.
- Keep your own tool logic tolerant of missing optional fields.
- Do not hardcode machine-specific paths in integrations; discover the repo or published `Assets` folder relative to the executable when possible.

## FAQ

### Is the database downloaded at runtime
No. The core database ships with the repo and with the publish output.

### Can I use the database without using XeCLI
Yes. The CSV and TXT files are plain local assets and are intended to be reusable.

### Can I add my own titles
Yes. Use `%APPDATA%\XeCLI\titleids.local.csv`.

### Should I parse the TXT or the CSV
Use the CSV whenever possible. It carries more metadata.


