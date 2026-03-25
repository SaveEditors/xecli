# Title ID Database

This page documents the bundled Title ID metadata that ships with XeCLI and explains how it can be reused by other tooling.

## Files
Bundled database files:

- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

Optional local extension file:

- `%APPDATA%\XeCLI\titleids.local.csv`

The bundled files are part of the repository and part of the published release. XeCLI does not fetch them from the internet at runtime.

## What the Database Is Used For
XeCLI uses the Title ID database to:

- resolve raw Title IDs into readable names
- enrich content listings
- label active-title output
- attach region/media metadata to lookups
- help normalize title-aware avatar item browsing in `Avatar-Item-Collection`

Other tools can use the same files to:

- enrich dashboards
- label save editors
- annotate dump folders
- match media-specific variants

## Current Behavior
`rgh title` combines live title resolution with the bundled database:

```powershell
rgh title
rgh title 415608C3
rgh title 415608C3 2B7302D6
```

If the live Title ID maps to a generic system entry but the running XEX path clearly indicates a dashboard replacement or homebrew shell, XeCLI can surface a better display name while still showing the database entry separately.

## Data Shape
The CSV contains fields such as:

- Title ID
- Media ID
- Name
- Serial
- Type
- Region
- XEX CRC when available
- Wave metadata when available

The TXT file is used as an additional bundled source for matching entries not present in the CSV set.

## Load Order
XeCLI loads Title ID metadata in this order:

1. `xbox360_gamelist.csv`
2. `xbox360_titleids.txt`
3. `%APPDATA%\XeCLI\titleids.local.csv`

That means local overrides can extend the bundled data without replacing it.

## Local Overrides
Use the local override file when you want to add:

- private homebrew entries
- private builds
- scene tools not present in the bundled set
- corrected names or media-specific annotations

The local override file should follow the same CSV layout XeCLI already understands.

## Using the Database Outside XeCLI
External tools can read the database directly. A few common use cases:

- display Title ID names in a launcher
- annotate `content list` results with richer metadata
- attach names to save extraction folders
- feed game names into reporting or automation pipelines

If you are building another tool, prefer reading the shipped asset files from the release or repository rather than inventing another metadata source.

## Practical Examples
Resolve a live title through XeCLI:

```powershell
rgh title --json
```

Use the CSV directly in another tool:

- load `xbox360_gamelist.csv`
- normalize the Title ID to 8-digit hex
- join by Media ID when available

## What This Database Is Not
It is not:

- a runtime online service
- a replacement for your own private overrides
- a perfect universal source for every homebrew build ever made

That is why XeCLI supports local additive overrides instead of pretending the bundled set is the end of the story.
