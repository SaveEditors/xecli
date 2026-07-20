# Title ID Database

XeCLI does not bundle or download a title or game database. Every command remains usable without one: when no trusted name is available, XeCLI displays the raw hexadecimal Title ID, such as `0x415608C3`.

An operator can supply an optional UTF-8 CSV or TXT file to add friendly names and media metadata. Configure it in **XeTerminal > Settings > Paths > Title Database file**, or use the CLI and environment behaviors below.

## Default Paths

The default user file is `titleids.csv` in the XeCLI configuration directory:

- installed: `%APPDATA%\XeCLI\titleids.csv`
- portable: `<XeCLI folder>\UserData\titleids.csv`
- custom `XECLI_HOME`: `<XECLI_HOME>\titleids.csv`

XeCLI automatically uses the default file when it exists. Relative paths saved in `config.json` or supplied through `XECLI_TITLE_DATABASE` resolve from the same configuration directory, so a relative portable setting moves with `UserData`.

Existing `%APPDATA%\XeCLI\titleids.local.csv` files remain compatible. When no newer source is configured and no default `titleids.csv` exists, XeCLI loads the legacy file. Opening Settings shows that legacy path; saving records it in `config.json`, or you can move/rename it to the new default path.

## Source Precedence

XeCLI selects one user source in this order:

1. `rgh title --database <PATH>` for that title command
2. `XECLI_TITLE_DATABASE`
3. `TitleDatabasePath` saved in `config.json` by XeTerminal Settings
4. the default `titleids.csv`, when present
5. legacy `titleids.local.csv`, when present

The local live-discovery cache is loaded first at `titleids.discovered.csv`. Entries from the selected user file are then applied by Title ID. If a user file contains a Title ID, all lower-priority cached rows for that ID are replaced, while unrelated cached IDs remain available.

Within one source, multiple Media ID variants for a Title ID are retained. A later row with the same Title ID and Media ID replaces the earlier row and emits a duplicate warning. This makes the last duplicate deterministic without allowing lower-priority names to leak back into results.

## CSV Format

The recommended header is:

```csv
Title ID,Name,Media ID,Serial,Type,Region,XEX CRC,Wave
415608C3,Example Game,2B7302D6,AB-123,Game,Region Free,1234ABCD,10
```

Only Title ID and Name are normally needed. Header names are case-insensitive and punctuation-insensitive; `Game Name,Title ID,...` is also accepted for compatibility. Headerless `Name,Title ID` and `Title ID,Name` two-column files work as well.

CSV parsing supports quoted commas, escaped quotes, UTF-8 BOMs, blank lines, and comment rows beginning with `#`, `;`, or `//`.

## TXT Format

TXT files accept tilde, tab, or equals separators. The Title ID can be on either side:

```text
415608C3~Example Game
Another Title=58410957
```

Blank lines and lines beginning with `#`, `;`, or `//` are ignored.

## Warnings and Fallbacks

Malformed rows do not prevent startup or discard valid rows. Text output from `rgh title` prints line-specific warnings, and JSON output includes them under `Database.Warnings`. Invalid Media IDs are ignored while the rest of the row is retained. At most 100 detailed warnings are retained, followed by a suppression summary.

For a raw lookup, these remain successful with no database configured:

```powershell
rgh title 415608C3
rgh title 415608C3 --json
rgh title 415608C3 --database C:\Data\my-titleids.csv
```

Free-form name search requires available user or cached metadata. A name with no match still returns the normal no-match result.

## Live Name Cache

When live status or active-title discovery returns a non-generic `.xex` filename, XeCLI can persist that name in `titleids.discovered.csv` under the configuration directory. Writes are local and atomic. Generic executable names such as `default.xex`, `dash.xex`, and `game.xex` are not cached.

The cache has a deliberate limit: an FTP content folder exposes a Title ID but not a trustworthy title name, so content inventory alone is not persisted as named metadata. In that case XeCLI keeps the raw hexadecimal fallback until package metadata, a non-generic live executable name, or a user entry supplies a name.

User entries always override this cache. XeCLI never silently downloads title metadata, and the generated cache is never part of the repository or release package.

## External Tools

External tools can read the operator's chosen CSV/TXT file directly or call `rgh title --json`. The JSON `Database` object reports the effective source kind/path, user/cache entry counts, and parse warnings so integrations can expose the same status without guessing which file XeCLI used.
