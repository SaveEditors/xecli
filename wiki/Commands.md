# Commands Reference

This page documents the XeCLI command surface by workflow area. Command examples use the installed terminal command `rgh`.

The `Example output` blocks on this page are representative results from the shipped CLI. Exact spacing, colors, and secondary detail lines can vary by flags, console state, and whether JSON mode is enabled.

## Find by Workflow
Use this table when you know the job you need done but not the exact command namespace yet.

| Workflow | Start here |
| --- | --- |
| Check if the console is up | `rgh ping`, `rgh status`, `rgh title` |
| Review recent console contacts | `rgh network recent`, `rgh network doctor` |
| Check local XeCLI setup without a console connection | `rgh health` |
| Find and connect to a console | `rgh start`, `rgh scan`, `rgh connect`, `rgh target` |
| Read or dump live memory | `rgh mem dump`, `rgh mem hexdump`, `rgh mem map`, `rgh mem peek` |
| Change live memory | `rgh mem poke`, `rgh mem search --freeze` |
| Apply trainer helper files | `rgh trainer apply` |
| Compare saved memory map captures | `rgh mem diff`, `rgh mem compare` |
| Compare saved binary memory dumps | `rgh mem dump-diff`, `rgh mem dump-compare` |
| Group saved memory captures into a session | `rgh mem session create`, `rgh mem session list`, `rgh mem session show`, `rgh mem session remove` |
| Store offline memory labels | `rgh mem-bookmarks add`, `rgh mem-bookmarks rename`, `rgh mem-bookmarks show`, `rgh mem-bookmarks export`, `rgh mem-bookmarks list`, `rgh mem-bookmarks remove` |
| Move IDA or Ghidra function names into offline bookmarks | `rgh ida export-symbols`, `rgh ghidra export-symbols`, `rgh mem-bookmarks import` |
| Wrap external Xtaf-CLI mount and recovery workflows | `rgh xtaf-cli config`, `rgh xtaf-cli status`, `rgh xtaf-cli install`, `rgh xtaf-cli run` |
| Run command files or review a saved transcript offline | `rgh script run`, `rgh script replay <transcript> --dry-run` |
| Export the local command log | `rgh log export --format csv --out <FILE>` |
| Read release history | `rgh changelog`, `rgh release-notes` |
| Validate a local release build | `rgh release check` |
| Confirm the dashboard FTP listener | `rgh ftp check`, `rgh ftp doctor`, `rgh status --quick` |
| Work with loaded modules | `rgh modules list`, `rgh modules info`, `rgh modules address`, `rgh modules dump`, `rgh modules load`, `rgh modules unload` |
| Inspect threads or break execution | `rgh threads ...`, `rgh debug ...` |
| Pull or compare files from the console | `rgh fs get`, `rgh ftp get`, `rgh ftp hash`, `rgh ftp diff`, `rgh xex dump`, `rgh screenshot` |
| Push files to the console | `rgh fs put`, `rgh ftp put`, `rgh save inject`, `rgh plugin enable` |
| Manage saves | `rgh save list`, `rgh save extract`, `rgh save inject` |
| Inspect or edit local CON/profile/GPD files | `rgh con ...`, `rgh profile ...`, `rgh profile titles add`, `rgh xdbf ...` |
| Manage title content or DashLaunch plugins | `rgh content ...`, `rgh plugin ...` |
| Back up NAND or export XeLL keys | `rgh xell boot`, `rgh xell info`, `rgh xell kv export`, `rgh xell nand dump` |
| Inspect local XTAF/FATX disks or images | `rgh xtaf devices`, `rgh xtaf partitions`, `rgh xtaf scan`, `rgh xtaf info`, `rgh xtaf list`, `rgh xtaf find`, `rgh xtaf cat`, `rgh xtaf get`, `rgh xtaf extract`, `rgh xtaf dump` |
| Stage Original Xbox compatibility packs | `rgh ogxbox list`, `rgh ogxbox install hacked|hud|retail` |
| Browse or install avatar items from the local or hosted collection | `rgh avatar library ...`, `rgh avatar cache ...`, `rgh avatar games`, `rgh avatar items`, `rgh avatar choose`, `rgh avatar browse`, `rgh avatar install` |
| Install or open the local CLI UI | `rgh install`, `rgh terminal`, `rgh shell` |
| Validate saved target profiles without connecting | `rgh target-saved validate` |
| Send visible console messages | `rgh notify`, `rgh notify-icons`, `rgh jrpc2 notify` |
| Analyze XEX files | `rgh xex info`, `rgh xex header`, `rgh xex strings`, `rgh xex map`, `rgh xex decompile`, `rgh xex ida-decompile`, `rgh ghidra ...`, `rgh ida ...` |
| Convert retail ISOs | `rgh god info`, `rgh god build`, `rgh god watch` |
| Create a local support bundle | `rgh support bundle`, `rgh diagnostics bundle --support`, `rgh diagnostics bundle`, `rgh diag bundle` |

## Command Model
XeCLI is organized into a few major namespaces:

- Core commands: status, title, targeting, launch, reboot
- Live-console commands: modules, memory, threads, debug, screenshot, and file-system access when the console is reachable through XBDM
- Session and console helpers: CPU key, temps, Title ID, dashboard, notifications, and generic RPC when JRPC2 is available
- XeLL-integrated backup workflows: the shipped `rgh xell boot`, `rgh xell info`, `rgh xell kv export`, and `rgh xell nand dump` commands are documented in [XeCLI-XellFetch](XeCLI-XellFetch)
- File-transfer commands: file access, saves, content, and plugin management when the console FTP service is running; use `rgh ftp check` or `rgh ftp doctor` to check port 21 first and confirm whether a compatible dashboard FTP service is running; see [FTP and File Transfer](FTP-and-File-Transfer) for requirements and failure signs
- Offline memory map captures: saved `rgh mem map --json` snapshots and `rgh mem diff` / `rgh mem compare` comparisons
- Offline memory dump captures: saved binary `rgh mem dump` files and `rgh mem dump-diff` / `rgh mem dump-compare` comparisons
- Offline memory sessions: local manifests that group saved map, dump, and search captures and let `rgh mem diff` / `rgh mem dump-diff` resolve `@session:label` references
- Offline memory bookmarks: stored address labels with exact `--addr`, `--module`, and `--rva` values saved locally; you can rename them, inspect one bookmark at a time, or export the full store to JSON
- IDA symbol sidecars: function-name export from IDA and import into offline bookmarks through a small JSON file
- Local content commands: CON package inspection, profile editing, and raw XDBF/GPD record access
- XTAF manager: local disk or image inspection, export, mutation, metadata, and repair workflows
- External Xtaf-CLI wrapper: optional mount, recovery, trim, and custom-volume workflows without duplicating the built-in XTAF branch
- Analysis commands: XEX, Ghidra, IDA, metadata
- Packaging commands: Games on Demand conversion and watchdog mode
- Health and diagnostics commands: offline local checks and redacted local support bundles

Shortcut equivalence and branch aliases:

- `rgh modules` = `rgh xbdm modules`
- `rgh module` = alias of `rgh modules`
- `rgh module list` = `rgh modules list`
- `rgh module info` = `rgh modules info`
- `rgh module address` = `rgh modules address`
- `rgh module dump` = `rgh modules dump`
- `rgh module load` = `rgh modules load`
- `rgh module unload` = `rgh modules unload`
- `rgh module pending` = `rgh modules pending`
- `rgh mem` = `rgh xbdm mem`
- `rgh mem session` = offline memory session branch
- `rgh mem-bookmarks` = offline memory bookmark branch
- `rgh bookmarks` = alias of `rgh mem-bookmarks`
- `rgh memmarks` = alias of `rgh mem-bookmarks`
- `rgh xex` = `rgh xbdm xex`
- `rgh xex launch` (alias: `rgh xex run`) = `rgh xbdm xex launch`
- `rgh fs` = `rgh xbdm fs`
- `rgh threads` = `rgh xbdm threads`
- `rgh debug` = `rgh xbdm debug`
- `rgh xtaf` = primary FATX/XTAF manager for disks and images
- `rgh fatman` = compatibility alias of `rgh xtaf`
- `rgh fatx` = compatibility alias of `rgh xtaf`
- `rgh diag` = alias of `rgh diagnostics`

High-traffic aliases:

- `rgh s` = `rgh start`
- `rgh c` = `rgh connect`
- `rgh discover` = `rgh scan`
- `rgh shot` = `rgh screenshot`
- `rgh run` = `rgh launch`
- `rgh xnotify` = `rgh notify`
- `rgh mem search` = `rgh mem find`
- `rgh mem read` = `rgh mem peek`
- `rgh mem write` = `rgh mem poke`
- `rgh debug address-check` = `rgh debug addr` / `rgh debug resolve`
- `rgh module remove` = `rgh modules unload`
- `rgh module verify` = `rgh modules pending`
- `rgh avatar apply` = `rgh avatar install`
- `rgh avatar cache` = avatar cache status and refresh helpers
- `rgh script` = newline-delimited local CLI command file playback
- `rgh xtaf` = primary FATX/XTAF manager
- `rgh fatman` = compatibility alias of `rgh xtaf`
- `rgh fatx` = compatibility alias of `rgh xtaf`

## Release History Commands
`rgh changelog` and `rgh release-notes` read the built-in release catalog locally. They do not need network access.

Use:

- `--latest` to show only the newest release entry
- `--version <TAG>` to pin a specific release
- `--json` to emit machine-readable output

## Release Check Commands
`rgh release check` validates the integrity and structure of a local portable release directory. It requires the empty `xecli.portable` marker, verifies every file size and SHA-256 value declared by `release-manifest.json`, checks the declared version, runtime, and release kind, and requires a matching ZIP checksum when you pass `--zip`. ZIP validation also requires one named root and compares every archived file byte-for-byte with the selected directory.

```powershell
rgh release check --publish-dir .\artifacts\release\stage\XeCLI-2.0.0-win-x64
rgh release check --publish-dir .\artifacts\release\stage\XeCLI-2.0.0-win-x64 --zip .\artifacts\release\XeCLI-2.0.0-win-x64.zip
rgh release check --publish-dir .\artifacts\release\stage\XeCLI-2.0.0-win-x64 --json
```

Options:

| Option | Description |
| --- | --- |
| `--publish-dir <DIR>` | Release publish directory to validate. Default: `out\win-x64`. |
| `--project <FILE>` | Project file used for the app-version comparison. Default: `src\Xbox360.Remote.Cli\Xbox360.Remote.Cli.csproj`. |
| `--zip <FILE>` | Optional release zip to validate against its SHA256 sidecar. |
| `--hash <FILE>` | Optional SHA256 sidecar file for `--zip`. Defaults to `<zip>.sha256`. |
| `--json` | Emit JSON output. |

Example output:

```text
Release Check
Check             Status   Detail
Release manifest   ok       Loaded release-manifest.json
Portable marker    ok       Loaded empty xecli.portable
Manifest body      ok       Validated staged files byte-for-byte
Project version    ok       AppVersion matches 2.0.0
Git revision       ok       Git revision matches <repo-revision>
Release hash       ok       SHA256 matches XeCLI-2.0.0-win-x64.zip
Release zip body   ok       Validated archived files byte-for-byte
SUCCESS Release check passed
```

This integrity check does not create or require an Authenticode signature. XeCLI v2.0.0 packages are unsigned, so Windows may identify the publisher as unknown. When checking a downloaded setup executable or ZIP, also compare it with the `.sha256` sidecar from the same GitHub release.

## Shell Completion
`rgh completion` generates offline shell-completion output locally. It writes the completion script to stdout for Bash, Zsh, and PowerShell, so you can redirect or pipe it into the right completion path for a manual install. PowerShell accepts the `powershell`, `pwsh`, and `ps` aliases. Fish is not supported, and shells outside the supported set are rejected with a clear error.

If you ask for a shell outside that set, XeCLI exits with a clear error and does not emit a script.

For exhaustive generated help covering every root command and its supported subcommands, including shortcut roots, see [CLI Help Output](CLI-Help). Run `rgh <command> --help` in the installed v2.0.0 build for the authoritative option list for that command.

## Common Options
### XBDM-backed commands
| Option | Description |
| --- | --- |
| `--ip <IP>` | Console IP. Uses the active console target if omitted. |
| `--port <PORT>` | XBDM TCP port. Default: `730`. |
| `--timeout <MS>` | Socket timeout in milliseconds. |
| `--json` | Emit JSON output when supported. |

### FTP-backed commands
| Option | Description |
| --- | --- |
| `--ip <IP>` | Console IP. Uses the active console target if omitted. |
| `--port <PORT>` | FTP port. Default: `21`. |
| `--user <USER>` | FTP username. Default: `xboxftp`. |
| `--pass <PASS>` | FTP password. Default: `xboxftp`. |
| `--timeout <MS>` | FTP timeout in milliseconds. |
| `--json` | Emit JSON output when supported. |

### Discovery options
| Option | Description |
| --- | --- |
| `--ports <PORTS>` | Comma-separated TCP ports to check. Default: `730,731`. |
| `--timeout <MS>` | Discovery timeout. Default: `400`. |
| `--no-nap` | Disable NAP broadcast discovery. |
| `--no-tcp` | Disable TCP scan discovery. |
| `--json` | Emit discovery results as JSON. |

### XeLL-integrated commands
| Option | Description |
| --- | --- |
| `--ip <IP>` | Console IP. Uses the active console target if omitted. |
| `--port <PORT>` | XBDM TCP port used while the console is still on the dashboard. Default: `730`. |
| `--timeout <MS>` | Socket timeout in milliseconds. |
| `--json` | Emit JSON output when supported. |
| `--force-xell` | See [XeCLI-XellFetch](XeCLI-XellFetch) for the integrated XeLL reboot target. |

Notes:

- XeCLI always asks for confirmation before the first automatic reboot into XeLL.
- If the shell is non-interactive or XeLL cannot be launched automatically, XeCLI prints the manual eject-button fallback instead of guessing.
- Once XeLL is requested, XeCLI waits up to 30 seconds for the XeLL HTTP service on port 80 and then uses the detected XeLL IP.

## Health and Diagnostics Commands
### `rgh health`
Run local XeCLI health checks without connecting to a console.

```powershell
rgh health
rgh health --json
rgh health --include-private
```

Checks:

- config file presence and parse status
- saved target and saved target profile status
- local command-log readability and malformed line count

By default, the saved default target and saved target-profile endpoints are redacted. Use `--include-private` only when you want full endpoint text in local terminal or JSON output.

Warning statuses are informational and still exit 0. If a check fails, `rgh health` exits with a non-zero code. For support, run the suggested `rgh support bundle` command after reviewing the local result.

Example output:

```text
XeCLI Health
Check           Status   Detail
Config          ok       Config file is present and parses.
Target profile   ok       Current profile home; profiles 2.
Command log     ok       12 entries; 0 malformed line(s).

Support: run rgh support bundle to collect a redacted offline bundle.
```

## Script Commands
`rgh script run` reads newline-delimited CLI commands from a file. Use `--dry-run` to validate the file without executing commands, `--transcript-out <FILE>` to save a structured redacted transcript, and `--allow-live` only when you intentionally want live or destructive commands to run. `rgh script replay <transcript> --dry-run` reviews a saved transcript offline and never executes its child commands.

### `rgh script --help`
```text
DESCRIPTION:
Run command files or review structured transcripts offline

USAGE:
    rgh script [OPTIONS] <COMMAND>

EXAMPLES:
    rgh script run .\batch.txt --dry-run
    rgh script run .\batch.txt --transcript-out .\batch-transcript.json
    rgh script replay .\batch-transcript.json --dry-run
    rgh batch run .\batch.txt --allow-live

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    run <FILE>              Run newline-delimited local CLI commands from a file
    replay <TRANSCRIPT>     Replay a structured batch transcript offline for review without executing commands
```

### `rgh script run --help`
```text
DESCRIPTION:
Run newline-delimited local CLI commands from a file

USAGE:
    rgh script run <FILE> [OPTIONS]

ARGUMENTS:
    <FILE>    File containing one CLI command per line

OPTIONS:
    -h, --help          Prints help information
        --dry-run       Validate and print the batch without running any
                        commands
        --allow-live    Allow live or destructive commands instead of blocking
                        them
        --transcript-out <FILE>
                        Write a structured redacted transcript artifact
```

Example usage:

```powershell
rgh script run .\batch.txt --dry-run
rgh script run .\batch.txt --transcript-out .\batch-transcript.json
```

Notes:

- `--dry-run` prints a redacted transcript and does not execute commands.
- `--allow-live` lifts the live and destructive command guard.
- `--transcript-out <FILE>` records command classifications and redacted results for later review.
- `batch` remains a compatibility alias for the same command tree.

### `rgh script replay`
Review a structured transcript created by `rgh script run --transcript-out` without rerunning any child command.

```powershell
rgh script replay .\batch-transcript.json --dry-run
rgh batch replay .\batch-transcript.json --dry-run
```

`--dry-run` is required. The review validates the transcript, renders each saved entry and its recorded status, and remains entirely offline.

### `rgh report`
Generate a console report in markdown, HTML, CSV, or JSON. Use `--report-profile` to pull from a saved report preset and `--target-profile` when you want to pull from a saved target profile instead of the current console target. Use `--since`, `--limit`, and `--status` to shape the module section, and `--include-private` when you need the full identifiers in local output. `--left` and `--right` compare two saved report files and emit a markdown table or JSON diff.

```powershell
rgh report --out .\console-report.md
rgh report --format csv --include-modules --include-threads --include-memory --out .\console-report.csv
rgh report --format html --include-modules --include-threads --out .\console-report.html
rgh report --format json --include-private --out .\console-report.json
rgh report --report-profile operator-review --out .\console-report.json
rgh report --left .\report-left.json --right .\report-right.json --format json
```

Notes:

- By default, console and network identifiers are redacted in report output, including CSV.
- Add `--include-private` when you need the full identifiers in local output or JSON.
- Modules, threads, and memory regions are included only when you opt in.
- `--since`, `--limit`, and `--status` apply to the module inventory when it is included.
- `--report-profile` loads a saved report preset from local storage.
- `--target-profile` uses a saved target profile by name instead of the current live target.
- Offline diff currently compares saved Markdown, CSV, or JSON report files and reports added, removed, and changed rows.

### `rgh report-profile`
Store and inspect saved report presets locally. Use `rgh report-profile add <name> ...` to save a preset, `list` to review presets, `show <name>` to inspect one preset, and `remove <name>` to delete one.

Example output:

```text
XeCLI Console Report
Generated UTC: 2026-05-15T12:30:00.0000000Z
Collection: 84 ms
Target: redacted:730
```

### `rgh support bundle`
Create a redacted local support package without connecting to a console. Use `rgh diagnostics bundle --support` if you want the support layout through the diagnostics tree. The short alias is `rgh diag bundle`.

```powershell
rgh support bundle
rgh support bundle --out .\xecli-support.zip
rgh diag bundle
rgh diagnostics bundle --support --out .\xecli-support.zip
rgh diagnostics bundle
rgh diagnostics bundle --out .\xecli-diagnostics.zip
rgh diagnostics bundle --folder --out .\xecli-diagnostics
rgh diag bundle --json
```

Options:

| Option | Description |
| --- | --- |
| `--out <PATH>` | Output zip file or folder path. Default: `xecli-support-<timestamp>.zip` for `rgh support bundle` or `xecli-diagnostics-<timestamp>.zip` for `rgh diagnostics bundle`. |
| `--folder` | Write an unpacked diagnostics folder instead of a zip archive. |
| `--force` | Overwrite an existing zip file or replace an existing diagnostics folder. |
| `--json` | Emit JSON output. |
| `--support` | Write the support-facing bundle layout through `rgh diagnostics bundle`. |

The bundle includes redacted command-log exports, health summaries, version metadata, and a local package-integrity summary when a source checkout is available.

Bundle contents include a redacted machine-readable health summary for support, plus these files:

- `support-report.md` or `diagnostics-report.md`
- `health.json`
- `version.json`
- `config-redacted.json`
- `command-log.jsonl`
- `command-log.csv`
- `release-check.json`
- `manifest.json`

The bundle redacts local paths, IP addresses, console IDs, and known secrets. Review the files before sharing because command history can still show recent command context.

Example output:

```text
SUCCESS Support bundle created
Output: xecli-support-20260508-123000.zip
Format: zip
Files: 8
Command log entries: 12
```

## Log Commands
### `rgh log list`
Inspect the local append-only command log cache. Entries are shown newest-first after malformed lines are skipped.

```powershell
rgh log list
rgh log list --limit 1 --json
rgh log list --limit 1 --csv
rgh log list --limit 0 --json
```

Options:

| Option | Description |
| --- | --- |
| `--limit <N>` | Maximum entries to show. Default: all. `0` returns no entries. |
| `--json` | Emit JSON output. |
| `--csv` | Emit CSV to stdout. |

`--limit 1` returns only the newest valid entry. `--limit 0` returns an empty JSON array or only the CSV header row.

### `rgh log export`
Export every valid local command-log entry to a CSV file. CSV is the only supported export format.

```powershell
rgh log export --format csv --out .\command-log.csv
```

Both `--format csv` and `--out <FILE>` are required. XeCLI creates the parent directory when possible and reports a failure if the destination cannot be written.

## Core Commands
### `rgh status`
Console summary view with XBDM identity, running XEX, JRPC2-backed fields, drive layout, and sign-in information.

```powershell
rgh status
rgh status --quick
rgh status --json
rgh status --no-drives --no-users
```

Notes:

- `--quick` uses the saved config and skips slower JRPC2, drive, and sign-in checks.
- `status` attempts to check the FTP 21 endpoint as best-effort; FTP reachability does not block the report or prevent console state reporting.
- Skipped fields are rendered as skipped instead of unknown.
- When a pending module load or unload is being tracked across a reboot, `status` can surface that state.

FTP-backed commands require an active console FTP service (Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin). If FTP is not running, use XBDM-backed `rgh fs ...` commands instead; XBDM/JRPC2 commands can still work even when FTP is unavailable.

Example output:

```text
Status
IP               <console-ip>
Execution State  start
Title ID         0xFFFE07D1
Title Name       Aurora
Signed In        Yes
Gamertag         ExampleUser
```

### `rgh profiles`
Enumerates sign-in and profile information gathered from XBDM, JRPC2, FTP, and F3 when available.

```powershell
rgh profiles
rgh profiles --json
rgh profiles --no-ftp
rgh profiles --no-f3
```

Example output:

```text
Signed-In Users
User 0   ExampleUser   state=SignedInToLive   xuid=0x<xuid>

Profiles (FTP)
72 profile containers found across Hdd1 and Usb devices
```

### `rgh title`
Resolve the active title, display any raw hexadecimal Title ID, or search an optional user-supplied Title Database by name. If the input is not hex, XeCLI normalizes it and searches available user/cache metadata, with exact normalized matches first and partial/fuzzy matches after that. A `MEDIAID` refines the preferred row when one exists, but XeCLI still shows every match for that Title ID.

```powershell
rgh title
rgh title --json
rgh title 415608C3
rgh title "Call of Duty 2"
rgh title 415608C3 2B7302D6
rgh title 415608C3 --database C:\Data\titleids.csv
rgh title active
```

Example output:

```text
Active Title
Title ID     0xFFFE07D1
Title Name   Aurora
Media ID     unknown
Source       running xex path + optional user metadata
```

When you pass both `TITLEID` and `MEDIAID`, XeCLI keeps the full title-id match set and uses the media ID only to choose the summary row when it can. A stale media ID does not collapse the lookup to zero rows.

In JSON mode, free-form name searches use `Query` for the raw text and set `TitleId` to the matched hex Title ID when the search finds a result. `TitleId` is null only when the search has no match or when the input was not a Title ID lookup.

`--database <PATH>` overrides `XECLI_TITLE_DATABASE` and saved Settings for this command. Without that option, XeCLI uses the environment variable, saved config, `titleids.csv` in the active XeCLI configuration directory, or the legacy `titleids.local.csv` compatibility path in that order. The configuration directory is `%APPDATA%\XeCLI` when installed, package-local `UserData` in portable mode, or the directory selected by `XECLI_HOME`. Malformed rows produce `Database.Warnings` without preventing valid rows or raw-ID lookup. XeCLI never downloads title metadata.

### `rgh target`
Show, set, or clear the saved default XBDM target.

```powershell
rgh target
rgh target --set <console-ip>
rgh target --set <console-ip> --port 730
rgh target --clear
```

Example output:

```text
Default target
IP      <console-ip>
Port    730
```

If the console IP changed, or discovery found the right console but you still need to pick it manually, use `rgh connect <console-ip>` so XeCLI updates the target and verifies the connection path before you retry commands such as `rgh status`.

### `rgh target-saved`
Store and select saved target profiles.

```powershell
rgh target-saved list
rgh target-saved list --csv
rgh target-saved list --json
rgh target-saved show home
rgh target-saved show home --json
rgh target-saved add home --ip <console-ip> --port 730
rgh target-saved validate
rgh target-saved validate --json
rgh target-saved use home
rgh target-saved current
rgh target-saved current --json
```

Notes:

- profile names are visible in list, show, and current output; endpoint text is redacted by default
- `rgh target-saved list --csv` emits a stable `Name,Target,Current` header and keeps endpoints redacted unless you add `--include-private`
- `rgh target-saved list --json`, `show --json`, `current --json`, and `use --json` keep the endpoint redacted unless you add `--include-private`
- add `--include-private` when you need the full endpoint text in local output or JSON
- `validate` checks the saved profile file locally, reports malformed or inconsistent entries, keeps endpoints redacted by default, and exits nonzero when it finds an issue
- `show` is also available as `get`; `use` as `select`; `current` as `state`
- `use` sets the current target profile and updates the saved default target
- `current` shows the effective target that active console commands use
- `rgh target-saved remove` refuses to remove the currently selected profile unless you pass `--force`
- forced removal clears the saved target when it points at the removed profile

Example output:

```text
Target Profile home
Name     home
Target   redacted:730
Current  yes
```

### `rgh ping`
Fast XBDM connectivity check.

```powershell
rgh ping
```

Example output:

```text
OK 21 ms
```

### `rgh reboot`
Request a cold reboot or title restart. Add `--wait` when automation must prove a process change or an XBDM disconnect followed by a responsive reconnect.

```powershell
rgh reboot
rgh reboot --title
rgh reboot --yes --wait
rgh reboot --title --yes --wait --wait-timeout 60
```

Request-only output confirms the XBDM acknowledgement, not reboot completion:

```text
SUCCESS Cold reboot requested
```

With `--wait`, XeCLI stops execution before the request and requires either a changed process ID or an XBDM disconnect followed by a responsive reconnect. It exits nonzero when neither transition is observed. If an acknowledged request is ignored, XeCLI attempts to resume execution before returning the failure.

### `rgh shutdown`
Power off the console through the JRPC power path.

```powershell
rgh shutdown
rgh shutdown --notify
```

Example output:

```text
SUCCESS Shutdown requested
power-off command sent to console
```

### `rgh launch`
Launch a XEX with optional directory, arguments, title hint, and notification. Use `--wait` for a completion contract instead of request acknowledgement alone.

```powershell
rgh launch Hdd1:\Aurora\Aurora.xex --verify --wait
rgh launch Hdd1:\Aurora\Aurora.xex --titleid FFFE07D1 --wait
rgh launch --xex Hdd1:\Aurora\Aurora.xex --args "debug=1"
rgh launch --xex Hdd1:\Aurora\Aurora.xex --dry-run
rgh launch Hdd1:\Aurora\Aurora.xex --no-verify --wait
```

Plain launch output deliberately says that the request was sent:

```text
WARNING Launch request sent
Target verified before request: Hdd1:\Aurora\Aurora.xex
```

`--wait` captures the current active XEX, stops execution, sends `magicboot`, and then requires the requested active XEX plus observable transition evidence. Evidence can be a changed process ID, an XBDM disconnect and reconnect, or a changed active-XEX path. A same-title relaunch remains unverified when XBDM exposes no process or transport transition. On timeout XeCLI exits nonzero and attempts to resume execution. `--no-verify` is intended only for a known recovery path when the current title denies directory enumeration.

### `rgh language`
`rgh language` shows or changes the saved UI language XeCLI uses when no `--lang` override or `XECLI_LANG` environment variable is supplied.

```powershell
rgh language
rgh language --set es
rgh language --set en
rgh language --clear
```

Example output:

```text
Field               Value
Saved UI language   Español (es)
Effective language  Español (es)
CLI override        Use --lang en|es for one command
```

For Windows installation and PATH registration, use the published setup executable. `rgh language` is only for the saved CLI language preference.

### `rgh install`
Use the published setup executable for the standard Windows wizard, Start menu shortcut, optional desktop shortcut, and Windows uninstall entry. Use `rgh install` from a published self-contained release folder when you want the CLI-driven copy and command-registration workflow.

```powershell
rgh install
rgh install --source .\XeCLI-2.0.0-win-x64 --path "$env:LOCALAPPDATA\Programs\XeCLI"
rgh install --source .\XeCLI-2.0.0-win-x64 --path "$env:LOCALAPPDATA\Programs\XeCLI" --no-path
rgh install --source .\XeCLI-2.0.0-win-x64 --path "$env:LOCALAPPDATA\Programs\XeCLI" --quiet
rgh install --machine --source .\XeCLI-2.0.0-win-x64 --path "C:\Program Files\XeCLI"
rgh install --uninstall
rgh install --machine --uninstall --path "C:\Program Files\XeCLI"
rgh install --machine-path --source .\XeCLI-2.0.0-win-x64 --quiet
```

Behavior and options:

- The default scope is the current user and the default directory is `%LOCALAPPDATA%\Programs\XeCLI`.
- `--machine` installs for all users under `%ProgramFiles%\XeCLI` by default and requires administrator approval.
- `--source <DIR>` selects the published self-contained release folder to copy; `--path <DIR>` selects the destination.
- PATH registration is optional. After any PATH change, close the current terminal and open a new one before running `rgh`.
- For a current-user install, `--no-path` leaves the install directory out of the user PATH and creates a per-user `rgh.cmd` shim in the WindowsApps command directory. For an all-users install, `--no-path` leaves PATH unchanged, so use the full installed executable path.
- `--machine-path` is a legacy elevated operation that adds the selected `--source` directory to the machine PATH without copying the release. If `--source` is omitted, it uses the directory containing the running `rgh.exe`. It may be combined with `--source` and `--quiet`, but not with `--machine`, `--uninstall`, `--path`, or `--no-path`.
- `--quiet` suppresses non-error output and is appropriate for a fully specified, non-interactive install.
- `--uninstall` removes CLI command registration for the selected scope. It does not delete the installation directory or user data.

### `rgh terminal`
Open the interactive XeTerminal Windows interface from the CLI. `rgh shell` is an alias.

```powershell
rgh terminal
rgh terminal --profile home
rgh terminal --ip <console-ip> --port 730
rgh terminal --theme cyan
rgh terminal --background .\background.jpg --no-telemetry
rgh shell
```

The command uses the selected target profile or saved target unless you provide `--ip`. It does not perform network discovery. Auto-connect follows the saved XeTerminal preference. `--theme` selects a shipped theme, `--background` selects an optional image, and `--no-telemetry` disables live polling for that session. Values below 100 for `--opacity` currently remain fully opaque to avoid rendering problems. Because this command opens an interactive Windows GUI, `--json` is not supported.

### `rgh homebrew install`
Download one or more public homebrew packages onto a USB drive, staging folder, or detected console drive.

```powershell
rgh homebrew list
rgh homebrew list --csv
rgh homebrew install aurora --usb E:\XeCLI-Stage
rgh homebrew install all --usb E:\XeCLI-Stage
rgh homebrew install aurora --device Hdd1 --ini-mode merge
rgh homebrew install all --device Hdd1 --ini-mode generated
rgh homebrew install dashlaunch --usb E:\UsbStage --force-download
rgh homebrew install xm360 --usb E:\XeCLI-Stage
rgh homebrew install timefixer --usb E:\XeCLI-Stage
rgh homebrew install simple360 --usb E:\XeCLI-Stage
rgh homebrew install xelllaunch --usb E:\XeCLI-Stage
```

Example output:

```text
SUCCESS Homebrew install complete
1 package(s) 38.69 MB -> E:\XeCLI-Stage

Package            Installed To         Files   Size
Aurora 0.7b.2      E:\XeCLI-Stage\Aurora 167     38.69 MB

Generated E:\XeCLI-Stage\launch.ini
```

Local staging merges into existing package folders, replaces matching file paths, and leaves unrelated files in place. Use a dedicated staging folder instead of a drive root. The current package catalog also includes XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch.

Native read-only backup flows no longer depend on Simple 360 NAND Flasher. XeCLI treats [XeCLI-XellFetch](XeCLI-XellFetch) as the integrated v2.0.0 XeLL workflow target for `rgh xell boot`, `rgh xell info`, `rgh xell kv export`, and `rgh xell nand dump`.

Console install example output:

```text
SUCCESS Console homebrew install complete
1 package(s) 2.52 MB -> Hdd1 on <console-ip>

Package            Console Path       Files   Size
DashLaunch 3.21    /Hdd1/DashLaunch   8       2.52 MB

User-supplied plugins copied to /Hdd1/Plugins
Updated existing launch.ini plugin entries /Hdd1/launch.ini
```

Important options:

- `--usb <TARGET>` accepts a drive letter, removable-drive selection number, or folder path
- omit `--usb` to install directly onto the console through FTP
- `--device <DEVICE>` targets a detected console drive such as `Hdd1`, `Usb0`, `Usb1`, or `Usb2`
- `--ini-mode <generated|merge|skip>` chooses whether XeCLI creates a fresh `launch.ini`, updates plugin entries in the existing file, or leaves it alone
- `--ini <PATH>` overrides the console-side `launch.ini` path when using direct console install
- `--cache <DIR>` moves archive and staging storage to a different directory
- `--force-download` refreshes cached archives
- `--auto-confirm` explicitly bypasses the confirmation prompt; non-interactive installs refuse to continue without it
- `--json` emits machine-readable package install output

### `rgh ogxbox install`
Stage or install one of the public Original Xbox XeFu compatibility sets.

```powershell
rgh ogxbox list
rgh ogxbox install hacked --usb E:
rgh ogxbox install hud --include-fixer --usb E:
rgh ogxbox install retail
```

Set choices:

- `hacked`
  - best default choice for modded consoles
  - removes stock whitelist/restriction checks and targets wider compatibility
- `hud`
  - same hacked base, but keeps the full Xbox 360 guide enabled during original Xbox titles
  - can cost performance or compatibility in some games
- `retail`
  - stock-style emulator files
  - closest to the original Microsoft behavior

Example output:

```text
SUCCESS Original Xbox compatibility install complete
Hacked XeFu Pack -> /HddX/Compatibility

Field                Value
Mode                 console
XeFu Set             Hacked XeFu Pack
Target               <console-ip>
Compatibility Path   /HddX/Compatibility
Compatibility Files  Installed
Fixer Included       No
Files                23
Size                 31.42 MB
```

Important options:

- `--usb <TARGET>` stages to a removable USB drive or folder instead of installing directly to the console
- omit `--usb` to connect over FTP and target `HddX:\Compatibility`
- `--include-fixer` also stages or installs HDD Compatibility Partition Fixer so you can create `HddX` on drives that do not have it yet
- `--cache <DIR>` moves archive and staging storage to a different directory
- `--force-download` refreshes cached archives
- `--auto-confirm` skips the confirmation prompt
- `--json` emits machine-readable install output

If `HddX` is missing and `--include-fixer` is not used, XeCLI stops before writing anything and tells you to rerun with the fixer included.

## XeLL and Backup Commands
The shipped managed XeLL launch, keyvault export, and verified NAND backup flow is documented separately in [XeCLI-XellFetch](XeCLI-XellFetch). That workflow is read-only and describes the XeLL HTTP service layouts, the `--force-xell` behavior, and the verification steps used by the commands.

## XTAF
XTAF is XeCLI's FATX manager for local Xbox 360 disks and images. The current surface covers image and physical-disk recovery, partition dumping, inspection, search, export, write, format, and safe repair workflows. `rgh xtaf` is canonical; `rgh fatman` and `rgh fatx` remain compatibility aliases.

### Current command surface
```powershell
rgh xtaf devices
rgh xtaf disks
rgh xtaf partitions
rgh xtaf scan
rgh xtaf info
rgh xtaf list
rgh xtaf find
rgh xtaf cat
rgh xtaf get
rgh xtaf mkdir
rgh xtaf put
rgh xtaf mv
rgh xtaf rm
rgh xtaf format
rgh xtaf check
rgh xtaf repair
rgh xtaf metadata backup
rgh xtaf metadata restore
rgh xtaf patch plan
rgh xtaf patch apply
rgh xtaf extract
rgh xtaf dump
```

### Manual open by offset
- use `--offset` to open a FATX/XTAF volume directly from a byte offset
- use `--length` with `--offset` when you want to clamp the manual partition size
- both values accept decimal bytes or `0x`-prefixed hex
- use `rgh xtaf scan` first when you need candidate offsets for a nonstandard image

```powershell
rgh xtaf scan --image .\Unknown.img
rgh xtaf info --image .\Unknown.img --offset 0xB6600000
rgh xtaf list --image .\Unknown.img --offset 0xB6600000 --path /
rgh xtaf dump --image .\Unknown.img --offset 0xB6600000 --length 0x10000000 --out .\partition-dump
```

### Metadata patch workflow
`rgh xtaf patch plan` compares two FATX metadata backups and writes a patch manifest for the metadata regions that changed. `rgh xtaf patch apply` consumes that manifest and applies the metadata changes only after the current source still matches the baseline SHA-256 captured by the plan.

This is a FATX metadata workflow, not a generic binary patch engine. It only works against the metadata regions XTAF backs up.

- use `--dry-run` on `patch apply` to preview the plan without writing
- use `--json` on either command when you want machine-readable output

```powershell
rgh xtaf patch plan --from .\before --to .\after --out .\xtaf-patch.json
rgh xtaf patch apply --patch .\xtaf-patch.json --image .\hdd.img --dry-run
rgh xtaf patch apply --patch .\xtaf-patch.json --image .\hdd.img --json
```

### What it will do
- detect FATX-capable disks or image sources
- inspect Windows physical disks directly
- inspect partitions and volume details
- browse directories and locate entries
- search by name or path
- recover data from `.img` and `.bin` sources
- create FATX directories in an image or writable physical disk
- write host files into an image or writable physical disk
- rename or move FATX entries in an image or writable physical disk
- remove FATX files or directories in an image or writable physical disk
- format individual FATX partitions or a retail Xbox 360 layout
- back up and restore low-level metadata regions
- compare metadata backups, generate patch manifests, and apply them only after baseline SHA-256 checks pass
- check for orphaned or invalid FATX chain-map state
- repair safe FATX allocation issues
- print small files in the terminal
- dump partitions to a host directory
- export selected files or directory trees to the host

Physical-disk access through `--disk` is Windows-only. Physical writes and formatting require an elevated terminal, and Windows can deny access to mounted, in-use, locked, or policy-protected devices. XeCLI does not bypass those denials or dismount devices. Image writes require normal filesystem write permission. `--dry-run` remains read-only, and destructive operations require confirmation unless `--auto-confirm` is supplied.

### Current boundaries
- XeCLI does not mount a virtual filesystem.
- XeCLI does not expose a driver-backed Windows mount.
- XeCLI does not rewrite arbitrary security sectors beyond the metadata regions XTAF explicitly backs up and restores.
- XeCLI does not act as a generic binary patch engine.

### Example output
```text
XTAF Sources
0  Disk 0  932.51 GB  Xbox 360 HDD
1  Disk 1   32.00 GB  USB Flash Drive

Selected source: Disk 0 / Hdd1
Partitions:
Hdd1  419.41 GB  FATX
HddX    1.00 GB  FATX
```

## Discovery Commands
### `rgh start`
`rgh start` explicitly discovers consoles and selects a default target. Installation is local-only and never launches this command.

```powershell
rgh start
rgh start --json
```

Example output:

```text
Detected Consoles
#1  <console-ip>  Jtag  0037XXXXXXXX190
Default target updated to <console-ip>
```

### `rgh connect`
rgh connect is the manual follow-up when you already know the discovery result you want to target.

```powershell
rgh connect 1
rgh connect <console-ip>
```

Use `rgh connect <console-ip>` when the console address changed or after discovery or setup reports `Console connection failed`, `Console connection timed out`, or `Console connection did not complete`.

Example output:

```text
SUCCESS Target updated
<console-ip>:730
```

### `rgh scan`
`rgh scan` is the explicit discovery-only path. The installer never scans for consoles.

```powershell
rgh scan
rgh scan --json
```

Example output:

```text
Detected Consoles
#1  <console-ip>  Jtag  0037XXXXXXXX190  tcp+nap
```

## Screenshot Commands
### `rgh screenshot`
```powershell
rgh screenshot --out .\screen.png
rgh screenshot --out .\screen.png --force
```

The command emits decoded frame-buffer metadata after a successful capture. `.png`, `.bmp`, and `.raw` outputs are supported.

Example output:

```text
Screenshot saved: .\screen.png
Width  1024
Height 576
Pitch  4096
Format 0x00000012
```

## XBDM Root Commands
### `rgh xbdm info`
```powershell
rgh xbdm info
rgh xbdm info --json
```

Example output:

```text
XBDM Info
Debug Name    Jtag
DM Version    2.0.21076.11
Reported by XBDM  Natelix
```

### `rgh xbdm raw`
```powershell
rgh xbdm raw --cmd "modules"
rgh xbdm raw --cmd "dirlist name=Hdd:\\"
```

Example output:

```text
(202) multiline response follows
name="xam.xex" base=0x82000000 size=0x001C0000
name="Aurora.xex" base=0x90F00000 size=0x00480000
```

### `rgh xbdm screenshot`
```powershell
rgh xbdm screenshot --out .\screen.png
```

Example output:

```text
Screenshot saved: .\screen.png
```

## Module Commands
When a command accepts a module name, the exact full module path is the safest form. Unique leaf or suffix matches work when unambiguous; ambiguous leaf names are rejected, and the CLI will ask you to use the full path or a more specific name.

### `rgh modules list`
```powershell
rgh modules list
rgh modules list --json
rgh modules list --sections
```

Example output:

```text
Modules
xboxkrnl.exe   0x80010000   0x005A0000
xam.xex        0x82000000   0x001C0000
Aurora.xex     0x90F00000   0x00480000
```

### `rgh modules info`
```powershell
rgh modules info --name Aurora.xex
rgh modules info --name xam.xex --sections
```

Example output:

```text
Module Info
Name         Aurora.xex
Base         0x90F00000
Size         0x00480000
Path         \Device\Harddisk0\Partition1\Aurora\Aurora.xex
```

### `rgh modules address`
Resolve a module-relative or Ghidra address against the live base of a loaded module.

```powershell
rgh modules address --name default.xex --rva 0x1234
rgh modules address --name default.xex --ghidra 0x82001234 --ghidra-base 0x82000000
rgh modules address --name default.xex --rva 0x1234 --sections
```

Use either `--rva` or `--ghidra`, not both. `--ghidra-base` is required with `--ghidra`. Exact full module paths are safest when more than one loaded module has the same leaf name.

### `rgh modules dump`
```powershell
rgh modules dump --name xam.xex --out .\xam.bin
rgh modules dump --all --dir .\modules
```

Example output:

```text
SUCCESS Module dump complete
xam.xex -> .\xam.bin
```

### `rgh modules load`
```powershell
rgh modules load --path Hdd:\HvP2.xex
rgh modules load --path Hdd:\HvP2.xex --system
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
rgh modules load --path Hdd:\HvP2.xex --dry-run
```

Important options:

- `--flags <N>` kernel load flags, default `8`
- `--system` runs the load on a system thread instead of the default title thread
- `--reboot-expected` persists pending verification if the console disconnects as part of the load
- `--notify` sends the default console success notification

Example output:

```text
SUCCESS Module loaded
HvP2.xex at 0x91340000
```

### `rgh modules unload`
```powershell
rgh modules unload --name HvP2.xex --force
rgh modules unload --handle 0x91340000 --force
rgh modules unload --name HvP2.xex --force --notify
```

Important options:

- `--force` is required
- `--allow-critical` is additionally required for known system and boot-plugin modules, including XBDM, JRPC2, XDRPC, xbGuard, XAM, and the kernel
- `--skip-mark` disables the sysdll unload marker write at `handle+0x40`
- `--dry-run` reports the planned target without connecting or modifying memory

Use a disposable title module for lifecycle testing. A protected module can satisfy the unload RPC and disappear from the immediate module list while still freezing the dashboard. XeCLI therefore waits briefly and requires a separate XBDM connection with a non-empty debug-monitor version before reporting a live unload as successful.

Example output:

```text
SUCCESS Module unloaded
0x91340000 HvP2.xex
```

### `rgh modules pending`
```powershell
rgh modules pending
```

Use this after:

```powershell
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
```

Example output:

```text
SUCCESS Pending module load verified
HvP2.xex at 0x91340000
```

## Memory Commands
`rgh mem map` captures the current live region layout, and `--json` saves a snapshot that `rgh mem diff` and `rgh mem compare` can compare later. Use these commands to review region and module changes in human or JSON form.

### `rgh mem map`
```powershell
rgh mem map
rgh mem map --json
```

Example output:

```text
Memory Map
0x80000000  0x02000000  System
0x82000000  0x01000000  Title
```

Use `--json` to save a capture for `rgh mem diff` and `rgh mem compare`.

### `rgh mem diff`
`rgh mem diff` and `rgh mem compare` work on saved `rgh mem map --json` captures, not raw live bytes. Use them to compare two offline memory map snapshots and review the region and module changes in human or JSON form.
```powershell
rgh mem diff --left .\left-map.json --right .\right-map.json
rgh mem compare --left .\left-map.json --right .\right-map.json --json
```

Example output:

```text
Memory Map Diff
Field    Left  Right  Delta
Regions   12    13     +1
Modules   7     8      +1
```

The human view lists added, removed, and changed regions and modules after the summary table. `--json` emits a structured diff payload for scripts.

Session references can replace raw paths when you have a saved manifest:

```powershell
rgh mem session create --name lab --capture map:baseline=.\left-map.json --capture map:current=.\right-map.json
rgh mem diff --left @lab:baseline --right @lab:current
```

### `rgh mem dump-diff`
```powershell
rgh mem dump-diff --left .\left.bin --right .\right.bin
rgh mem dump-compare --left .\left.bin --right .\right.bin --json
```

Example output:

```text
Memory Dump Compare
Field            Left       Right      Notes
File             left.bin   right.bin  compared
Size             3 bytes    3 bytes    same size
Differing bytes  1          1          byte-level compare
First difference 0x00000002 0x00000002 offset
Byte at offset   0x30       0x31       left vs right
```

`rgh mem dump-diff` and `rgh mem dump-compare` work on saved binary `rgh mem dump` files, not raw live bytes. Use them to compare two offline memory dumps and review the first differing offset, the bytes at that offset, and the total differing-byte count in human or JSON form. `--json` emits a structured payload with the left and right file names and sizes plus a summary block.

Session references also work here:

```powershell
rgh mem session create --name lab --capture dump:before=.\left.bin --capture dump:after=.\right.bin
rgh mem dump-diff --left @lab:before --right @lab:after
```

### `rgh mem session`
`rgh mem session` stores local manifests that group saved memory map, dump, and search captures. The manifests stay offline and do not contact a console.

```powershell
rgh mem session create --name lab --capture map:baseline=.\left-map.json --capture map:current=.\right-map.json --capture dump:before=.\left.bin
rgh mem session list
rgh mem session show --name lab
rgh mem session remove --name lab
```

Use `@session:label` with `rgh mem diff` and `rgh mem dump-diff` when you want to point at a stored capture without retyping the file path.

### `rgh mem dump`
```powershell
rgh mem dump --addr 0x82000000 --size 0x20000 --out .\mem.bin
rgh mem dump --module default.xex --rva 0x1234 --size 0x40 --out .\mem.bin
rgh mem dump --module default.xex --ghidra 0x82001234 --ghidra-base 0x82000000 --size 0x40 --out .\mem.bin
```

Example output:

```text
SUCCESS Memory dump complete
0x82000000 length=0x00020000 -> .\mem.bin
```

Use `--addr` by itself for an explicit live address, or use `--module` with `--rva` for module-relative lookups. Pair `--module` with `--ghidra` and `--ghidra-base` when translating a Ghidra address back to the live console address space.

### `rgh mem hexdump`
```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh mem hexdump --module default.xex --rva 0x1234 --size 0x40
rgh mem hexdump --module default.xex --ghidra 0x82001234 --ghidra-base 0x82000000 --size 0x40
```

Example output:

```text
0x30000000 DE AD BE EF 01 02 03 04 05 06 07 08 09 0A 0B 0C  ................
0x30000010 10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1D 1E 1F  ................
```

Use `--addr` by itself for an explicit live address, or use `--module` with `--rva` for module-relative lookups. Pair `--module` with `--ghidra` and `--ghidra-base` when translating a Ghidra address back to the live console address space.

### `rgh mem regions`
```powershell
rgh mem regions
rgh mem regions --json
```

Example output:

```text
Memory Regions
0x80000000  0x02000000  protect=0x00000020
0x82000000  0x01000000  protect=0x00000004
```

### `rgh mem peek`
```powershell
rgh mem peek --addr 0x82000000 --type u32
rgh mem peek --addr 0x82000000 --type ascii --len 32
rgh mem peek --addr 0x82000000 --type u32 --le
rgh mem peek --module default.xex --rva 0x1234 --type u32
rgh mem peek --module default.xex --ghidra 0x80001234 --ghidra-base 0x80000000 --type u32
```

Example output:

```text
0x82000000 = 0x12345678 (u32)
```

For small reads, `peek` prefers the reliable XBDM read path before falling back to `getmemex`, matching `hexdump` and small `dump` behavior on live titles. Use `--le` when the value should be interpreted as little-endian.

### `rgh mem poke`
```powershell
rgh mem poke --addr 0x82000000 --type u32 --value 0x12345678
rgh mem poke --addr 0x82000000 --type float --value 1337
rgh mem poke --addr 0x82000000 --type string --value "XeCLI"
rgh mem poke --addr 0x82000000 --type u32 --value 0x12345678 --le
rgh mem poke --module default.xex --rva 0x1234 --type u32 --value 0x12345678
rgh mem poke --module default.xex --ghidra 0x80001234 --ghidra-base 0x80000000 --type u32 --value 0x12345678
```

Common type aliases include:

- `u8`, `u16`, `u32`, `u64`
- `s8`, `s16`, `s32`, `s64`
- `f32`, `f64`
- `int32`, `uint32`, `float32`
- `byte`, `int`, `uint`, `float`
- `ascii`, `string`, `hex`, `bytes`

Example output:

```text
Wrote and verified 0x82000000 (4 byte(s))
```

`poke` now reads the target bytes back and fails if XBDM rejects the write or the readback does not match. Use `--le` when the value should be written as little-endian.

### `rgh mem watch`
```powershell
rgh mem watch --addr 0x82000000 --size 0x40
rgh mem watch --addr 0x82000000 --size 0x40 --interval 100 --count 10
rgh mem watch --addr 0x82000000 --size 0x40 --clear
```

Example output:

```text
watch 1  0x82000000  78 56 34 12 ...
watch 2  0x82000000  79 56 34 12 ...
```

Use `--clear` to clear the screen between updates.

### `rgh mem strings`
```powershell
rgh mem strings --addr 0x82000000 --size 0x20000 --min 6
rgh mem strings --addr 0x82000000 --size 0x20000 --json
rgh mem strings --addr 0x82000000 --size 0x20000 --out .\strings.txt
rgh mem strings --addr 0x82000000 --size 0x20000 --json --out .\strings.json
```

Example output:

```text
0x82014020  ascii    xam.xex
0x82014210  ascii    XeKeysExecute
```

`--out` writes a text file by default. Use a `.json` suffix to write JSON output, and `--json` still emits JSON to stdout.

### `rgh mem search`
Alias of `rgh mem find`.

```powershell
rgh mem search --addr 0x30000000 --size 0x1000 --pattern DEADBEEF
rgh mem search --addr 0x82000000 --size 0x20000 --ascii "xam.xex"
rgh mem search --addr 0x82000000 --size 0x20000 --type uint32 --value 0x12345678
rgh mem search --addr 0x82000000 --size 0x20000 --type int64 --value 0x0123456789ABCDEF --little-endian
rgh mem search --addr 0x82000000 --size 0x20000 --type string --value "XeCLI"
rgh mem search --addr 0x82000000 --size 0x20000 --type utf16 --value "XeCLI"
rgh mem search --addr 0x82000000 --size 0x20000 --type float32 --value 1337 --little-endian
rgh mem search --addr 0x82000000 --size 0x20000 --type int32 --value 0xFFFFFFFF
rgh mem search --addr 0x82000000 --size 0x20000 --pattern 00000000 --hit 2
rgh mem search --addr 0x82000000 --size 0x20000 --pattern 00000000 --out .\hits.json
rgh mem search --addr 0x82000000 --size 0x20000 --pattern 00000000 --freeze --freeze-type u32 --freeze-value 305419896 --freeze-interval 100
```

Typed search uses the same value encoding as `rgh mem poke` and freeze writes. Values are big-endian by default. Add `--little-endian` when the numeric target bytes are little-endian. `string` searches use UTF-8 byte sequences, and `utf16` searches use UTF-16LE byte sequences.

Common type aliases include `int32`, `int64`, `uint32`, `float32`, `string`, and `utf16`. For signed types, hex values can use two's-complement form: `--type int32 --value 0xFFFFFFFF` searches for the bytes that represent `-1`.

Freeze options:

```powershell
rgh mem search --addr 0x82000000 --size 0x20000 --pattern 00000000 --freeze --freeze-type u32 --freeze-value 305419896
rgh mem search --addr 0x82000000 --size 0x20000 --pattern 00000000 --freeze --freeze-all --freeze-count 5
```

Example output:

```text
Hits
0x82000120
0x820004A8
0x820019F0
```

Freeze writes use the same verified-write path as `rgh mem poke`, so rejected writes and mismatched readback now fail instead of silently continuing.
Use `--hit` to choose the 1-based hit index to freeze when `--freeze-all` is not used. Use `--freeze-interval` to control how often freeze writes repeat.

## Memory Bookmarks
`rgh mem-bookmarks` stores offline memory labels. It saves the values you pass exactly as entered and does not resolve live addresses or infer missing coordinates.

For function-name sidecars, use `rgh ida export-symbols` or `rgh ghidra export-symbols` to write the shared JSON file, then `rgh mem-bookmarks import` to load those names into offline bookmarks. The imported entries stay exact-address or module/RVA based.

Group aliases:

- `rgh bookmarks`
- `rgh memmarks`

Command aliases:

- `rgh mem-bookmarks list` = `rgh mem-bookmarks ls`
- `rgh mem-bookmarks remove` = `rgh mem-bookmarks rm` / `rgh mem-bookmarks del`

### `rgh mem-bookmarks add`
```powershell
rgh mem-bookmarks add --name BossRoom --addr 0x82001234
rgh mem-bookmarks add --name MainLoop --module Aurora.xex --rva 0x1234
rgh mem-bookmarks add --name xam-note --addr 0x8200A000 --note "dashboard hook"
rgh mem-bookmarks add --name MainLoop --module Aurora.xex --rva 0x1234 --json
```

Use `--addr` for an explicit live address, `--module` for a stored module label, and `--rva` with `--module` for a module-relative label. `--rva` is not accepted without `--module`.

`--json` emits a machine-readable bookmark envelope.

### `rgh mem-bookmarks rename`
```powershell
rgh mem-bookmarks rename --name BossRoom --to BossRoomFinal
rgh mem-bookmarks mv --name BossRoom --to BossRoomFinal
rgh mem-bookmarks rename --name BossRoom --to BossRoomFinal --json
```

`rename` updates the stored bookmark name without changing its saved coordinates or note. The new name must be unique.
`mv` is an alias for `rename`.

### `rgh mem-bookmarks list`
```powershell
rgh mem-bookmarks list
rgh mem-bookmarks list --json
rgh mem-bookmarks ls
```

`list` shows the saved offline labels. When the store is empty, the plain output prints `No memory bookmarks saved.` instead of staying silent. `--json` returns the full bookmark list for scripts or export, and an empty store becomes `[]`.

### `rgh mem-bookmarks show`
```powershell
rgh mem-bookmarks show --name BossRoom
rgh mem-bookmarks jump --name BossRoom
rgh mem-bookmarks show --name BossRoom --json
```

`show` prints the stored bookmark details for one exact name. It does not resolve live addresses or do partial matching.
`jump` is an alias for `show`.

### `rgh mem-bookmarks export`
```powershell
rgh mem-bookmarks export --out .\memory-bookmarks.json
rgh mem-bookmarks export --out .\memory-bookmarks.json --json
```

`export` writes the current offline bookmark store to a JSON file. Use it when you want a local backup or a handoff file for another machine.

### `rgh mem-bookmarks import`
```powershell
rgh mem-bookmarks import --file .\symbols.json
rgh mem-bookmarks import --file .\symbols.json --json
```

`import` reads a symbol sidecar JSON file from either `rgh ida export-symbols` or `rgh ghidra export-symbols` and turns each exported function name into an offline bookmark entry. The current sidecar schema uses `version`, `module`, and `symbols`, with each symbol carrying `name`, `RVA`, and `type`.

This slice is function-name only. It does not import comments or PDB data, and there is no comments/PDB import-back path yet. It expects exact-address or module/RVA style coordinates.

### `rgh mem-bookmarks remove`
```powershell
rgh mem-bookmarks remove --name BossRoom
rgh mem-bookmarks rm --name BossRoom
rgh mem-bookmarks del --name BossRoom
rgh mem-bookmarks remove --name BossRoom --json
```

`remove` matches the bookmark name exactly. It does not do partial matches or live lookup.
`--json` returns the removed bookmark name.

## Trainer Helper
`rgh trainer` applies trainer schema version 1 flat live-memory entries from a file. This schema works with explicit live addresses only. It does not resolve module names, RVAs, or Ghidra addresses, and it does not follow pointer chains, build freeze profiles, or manage rollback data.

### `rgh trainer apply`
```powershell
rgh trainer apply --file .\trainer.json --dry-run
rgh trainer apply --file .\trainer.json
```

Use `--dry-run` to preview the write plan without changing memory.

Each trainer entry is a flat object with these fields:

- `address`: the live console address to write
- `value`: the typed value to write
- `littleEndian`: set to `true` when the value should be written little-endian
- `enabled`: set to `false` to keep an entry in the file without applying it
- `note`: short operator text for context

The file stays intentionally flat. Module-relative lookups, Ghidra-to-live translation, freeze loops, pointer chains, and rollback files are not part of `rgh trainer apply` today. `rgh trainer run` is an alias of `rgh trainer apply`.

## Symbol Sidecars
### `rgh ida export-symbols`
```powershell
rgh ida export-symbols --in .\title.i64 --out .\symbols.json
rgh ida export-symbols --in .\title.i64 --out .\symbols.json --json
```

`export-symbols` writes function names from an IDA database into a sidecar JSON file. The exported file is meant for offline bookmark import, not for a full analysis handoff.

### `rgh ghidra export-symbols`
```powershell
rgh ghidra export-symbols --in .\title.xex --out .\symbols.json
rgh ghidra export-symbols --in .\title.xex --out .\symbols.json --json
```

`export-symbols` writes function names from a Ghidra database into the same sidecar JSON file shape used by IDA. The exported file is meant for offline bookmark import, not for a full analysis handoff.

The user-facing schema is:

- `version`
- `module`
- `symbols`

Each item in `symbols` includes `name`, `RVA`, and `type`. The command keeps this slice narrow: function names only, with no comments or PDB export.

## XEX Commands
### `rgh xex info`
```powershell
rgh xex info --file <file.xex>
rgh xex info --file <file.xex> --json
rgh xex info --file <file.xex> --json --out .\xex-info.json
```

`rgh xex header` is an alias for `rgh xex info`.

This command inspects local XEX metadata offline. It reads the file on disk and does not need a live console connection.

By default, the text output uses concise file references. Use `--json` for machine-readable output, and add `--out` to write that JSON to a file while still printing it to the console.

`--json` uses a stable field contract with hex values rendered as `0x`-prefixed strings. The `File` field stays leaf-only, and private path-like header strings such as `BoundingPath` are redacted or omitted instead of echoing local filesystem paths.

### `rgh xex dump`
```powershell
rgh xex dump --out .\title.xex
rgh xex dump --path Hdd1:\Aurora\Aurora.xex --out .\aurora.xex
```

Example output:

```text
SUCCESS XEX dump complete
\Device\Harddisk0\Partition1\Aurora\Aurora.xex -> .\aurora.xex
```

### `rgh xex strings`
```powershell
rgh xex strings --file .\title.xex --unicode --min-length 6 --limit 50
rgh xex strings --ftp-path /Hdd1/Aurora/Aurora.xex --out .\strings.txt
rgh xex strings --file .\title.xex --json --limit 50
```

Example output:

```text
XEX Strings
0x00000000  ascii    XEX2
0x0005B8A0  utf16le  Aurora
```

`--json` emits a documented envelope with `File`, `MinLength`, `Limit`, `Unicode`, `Count`, and `Strings`. Each string entry carries `Offset`, `Kind`, `Length`, and `Text`.

### `rgh xex map`
```powershell
rgh xex map --file .\default.xex --ghidra-base 0x82000000 --ghidra 0x82001234
rgh xex map --file .\default.xex --ghidra-base 0x82000000 --ghidra 0x82001234 --json
```

This command runs offline against a local XEX file. It maps a Ghidra address back to the XEX load address using the Ghidra image base you provide.

Use this translation:

`GhidraAddress - GhidraBase + LiveModuleBase`

For example, if Ghidra shows `0x82001234`, the Ghidra base is `0x82000000`, and the live module base is `0x90000000`, the live address is `0x90001234`.

Use it when Ghidra shows an address from an imported XEX and you need the matching runtime-style address for notes, breakpoints, or memory lookup. It does not require a console connection.

If you already have a connected console and are reasoning from the live module base, use the live module metadata or memory path instead of the offline mapper. The offline mapper is for local file analysis, not for pulling a module base from the console.

### `rgh xex bundle`
```powershell
rgh xex bundle --file .\default.xex --out .\xex-bundle.zip
rgh xex bundle --file .\default.xex --ghidra-base 0x82000000 --ghidra 0x82001234 --symbols .\symbols.json --out .\xex-bundle --folder
```

This command packages a local XEX analysis bundle for offline round-trip use. It writes the source metadata, an address-map sidecar when you provide `--ghidra-base` and `--ghidra`, and an optional imported symbol sidecar when you provide `--symbols`.

The bundle manifest is validated immediately after it is written so the command can catch schema or file-shape mistakes offline. Use `--folder` if you want an unpacked bundle directory instead of a zip archive.

### `rgh debug address-check`
```powershell
rgh debug address-check --module default.xex --rva 0x1234
rgh debug address-check --module default.xex --ghidra 0x82001234 --ghidra-base 0x82000000
```

This command uses the same live-address resolver as the memory commands, then checks the result against the loaded module layout and reports the matching section when one exists. It also reports section gaps explicitly instead of hiding them behind a generic no-match result.

Use `--module` with `--rva` for module-relative lookups, or pair `--module` with `--ghidra` and `--ghidra-base` when translating a Ghidra address back to the live console address space.

`--json` emits a machine-readable address-check result.
`rgh debug addr` and `rgh debug resolve` are aliases of this command.

### `rgh xex decompile`
```powershell
rgh xex decompile --in .\title.xex --out .\decomp
rgh xex decompile --running --out .\decomp --max 200
```

Example output:

```text
SUCCESS Ghidra decompile complete
200 function files written to .\decomp
```

## File-System Commands
### XBDM-backed
```powershell
rgh fs list --path Hdd:\
rgh fs get --path Hdd:\launch.ini --out .\launch.ini
rgh fs put --path Hdd:\launch.ini --in .\launch.ini
rgh fs cat --path Hdd:\launch.ini
rgh fs rm --path Hdd:\temp\old.txt
rgh fs mkdir --path Hdd:\temp\newdir
rgh fs mv --from Hdd:\old.txt --to Hdd:\new.txt
```

Example output:

```text
Directory
Aurora           Dir
launch.ini       File   12794
```

### FTP-backed
Target management:

```powershell
rgh ftp target
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
rgh ftp target --clear
```

```powershell
rgh ftp list --path /Hdd1/
rgh ftp find --path /Hdd1/ --name *.xex
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini --verify-hash sha256
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini --resume
rgh ftp get --path /Hdd1/Content/415608C3 --out .\ContentBackup --recursive
rgh ftp put --path /Hdd1/launch.ini --in .\launch.ini --verify-hash sha256
rgh ftp put --path /Hdd1/launch.ini --in .\launch.ini --resume
rgh ftp put --path /Hdd1/Plugins --in .\Plugins --recursive --dry-run
rgh ftp sync --direction upload --path /Hdd1/Plugins --in .\Plugins --recursive --dry-run
rgh ftp sync --direction download --path /Hdd1/Content/415608C3 --in .\ContentBackup --recursive
rgh ftp hash --path /Hdd1/launch.ini --algorithm sha256
rgh ftp diff --path /Hdd1/launch.ini --local .\launch.ini --algorithm sha256
rgh ftp cat --path /Hdd1/launch.ini
rgh ftp rm --path /Hdd1/temp/old.txt
rgh ftp mkdir --path /Hdd1/newdir
rgh ftp mv --from /Hdd1/old.txt --to /Hdd1/new.txt
```

Example output:

```text
SUCCESS FTP upload complete
total=1 planned=1 skipped=0 transferred=1 failed=0 bytes=12.49 KB
1 file(s) -> /Hdd1/launch.ini
```

Single-file `ftp get --resume` and `ftp put --resume` continue partial transfers where supported. Recursive `ftp get` and `ftp put` report total files, planned files, skipped files, transferred files, failed files, and bytes. Existing destination files are skipped unless `--overwrite` is used.

The `ftp sync` flow supports resumable single-file transfers in both directions, and directory-tree sync requires `--recursive`.

`rgh ftp hash` calculates `sha256`, `sha1`, or `md5` for one remote file; `--remote` is an alias for `--path`. `rgh ftp diff` compares one remote file with one local file using the selected algorithm and exits nonzero when the hashes differ. Both commands require an active console FTP service and may download the remote file when the server cannot calculate the requested hash directly.

For a fast `ftp check`, use `rgh ftp check` or `rgh ftp doctor`. It checks port 21 and helps confirm whether a compatible dashboard FTP service is running before you browse or transfer files.

With `--json`, recursive transfer output includes the same summary plus one `Files` entry per file. File entries include status values such as `planned`, `transferred`, `skipped`, or `failed`; local paths and error text are reduced so private host path details are not exposed.

`ftp put --dry-run` previews an upload without writing remote files. The preview reports the same summary fields and lists the files that would upload or skip.

## Thread and Debug Commands
### Threads
```powershell
rgh threads list
rgh threads context --id 0xFB000008
rgh threads suspend --id 0xFB000008
rgh threads resume --id 0xFB000008
```

Example output:

```text
Threads
#  ID         Image         Start Addr.  End Addr.    Stack Base  Stack Limit
1  0xFB000008 xboxkrnl.exe  0x8012E3C0   0x801F0000   0x7A020000  0x7A010000
2  0xF9000004 Aurora.xex    0x82458C90   0x82BF0000   0x700A0000  0x70060000
```

`threads list` now resolves the thread start routine address and, when that address lands inside a loaded image, also shows the containing image name and end boundary. `End Addr.` is the containing image end boundary, not a recovered function end.

`rgh threads list --json` includes `ImageName`, `StartAddress`, and `EndAddress` for the same reason.

### Debug control
```powershell
rgh debug stop
rgh debug go
rgh debug watch --duration 5
rgh debug watch --duration 5 --require-event
```

`debug watch` uses `--duration` to control the watch length. `--timeout` still applies to the XBDM connection and I/O timeout. Add `--require-event` when the workflow intentionally triggers a debug notification and a quiet event stream should be treated as a failure.

Example output:

```text
execution stopped
execution started
```

`debug stop` and `debug go` now fail when XBDM rejects the command. They no longer print success on non-OK responses.

### Breakpoints
```powershell
rgh debug break add --addr 0x82001000
rgh debug break remove --addr 0x82001000
rgh debug break clearall
```

Example output:

```text
SUCCESS Breakpoint added
0x82001000
```

Breakpoint add, remove, and clear operations now require an accepted XBDM response before XeCLI reports success.

### Data breakpoints
```powershell
rgh debug databreak add --addr 0x82100000 --size 4 --type write
rgh debug databreak add --addr 0x82100000 --size 4 --type rw
rgh debug databreak remove --addr 0x82100000 --size 4 --type write
```

Example output:

```text
SUCCESS Data breakpoint added
addr=0x82100000 size=4 type=write
```

`--type rw` is accepted as the `readwrite` alias. Data breakpoint add and remove operations now fail when XBDM rejects the request.

## JRPC2 Commands
```powershell
rgh jrpc2 cpu-key
rgh jrpc2 temps
rgh jrpc2 temps --sensor gpu
rgh jrpc2 title-id
rgh jrpc2 dashboard
rgh jrpc2 motherboard
rgh jrpc2 resolve --module xam.xex --ordinal 526
rgh jrpc2 notify --message "XeCLI"
rgh jrpc2 call --module xam.xex --ordinal 526 --ret int --arg int:0
```

Example output:

```text
CPU Key     587AC7...
Dashboard   17559
Motherboard Trinity
```

## Notification Commands
```powershell
rgh notify "Success :)"
rgh notify "XeCLI connected" 14
rgh notify --message "XeCLI connected" --icon info
rgh notify-icons list
rgh notify-icons show 14
rgh notify-icons add --name success --logo 0x24
rgh notify-icons remove --name success
```

For the full XNotify explanation, icon IDs, direct numeric usage, and integration notes, see [XNotify](XNotify).

Example output:

```text
SUCCESS Notification sent
message="XeCLI connected" logo=14 (Flashing happy face)
```

### `rgh notify-icons show`
```powershell
rgh notify-icons show 14
rgh notify-icons show success
rgh notify-icons show achievement
```

Example output:

```text
Input     14
Logo ID   14
Label     Flashing happy face
Notes     Common success/smiley icon
```

## Hardware and Session Commands
### `rgh signin state`
```powershell
rgh signin state
rgh signin state --json
```

Example output:

```text
Signed In   Yes
State       Signed in locally
Gamertag    ExampleUser
XUID        0x<xuid>
Slot        0
```

### `rgh led set`
```powershell
rgh led set --preset quadrant1
rgh led set --preset all-green --notify
rgh led set --tl green --tr off --bl off --br off
```

Example output:

```text
SUCCESS Ring light updated
TL=green, TR=off, BL=off, BR=off
```

### `rgh led state`
```powershell
rgh led state
rgh led state --json
```

Example output:

```text
Source        Last XeCLI-applied ring-light state
Preset        quadrant1
Top Left      green
Top Right     off
Bottom Left   off
Bottom Right  off
```

### `rgh fan set`
```powershell
rgh fan set --speed 55 --channel both
rgh fan set --speed 45 --channel primary
rgh fan set --speed 60 --channel secondary --notify
```

Example output:

```text
SUCCESS Fan command sent
55% requested for both
```

### `rgh fan show`
```powershell
rgh fan show
rgh fan show --json
```

Example output:

```text
Source   Last XeCLI-applied manual setting
Speed    50%
Channel  both
```

### `rgh smc version`
```powershell
rgh smc version
rgh smc version --json
```

Example output:

```text
SUCCESS SMC version
2.3
```

### `rgh smc temps`
Read the four SMC temperature sensors at their native 8.8 fixed-point precision.

```powershell
rgh smc temps
rgh smc temps --json
```

Example output:

```text
Sensor       Temperature
CPU          80.50 C
GPU          79.25 C
EDRAM        71.75 C
Motherboard  45.50 C
```

The JSON response includes the raw sensor words, `ScratchRestored: true`, and `Operation: "ReadOnlyProbe"` so automation can validate both the readings and transaction safety.

Read [Hardware and System Controls](Hardware-and-System) for the deeper operational notes around sign-in state, LED presets, fan command behavior, and SMC availability.

### `rgh tray open` / `rgh tray close`
Open or close the physical disc tray through the JRPC2 path.

```powershell
rgh tray open
rgh tray close
```

Example output:

```text
SUCCESS Disc tray opened
```

```text
SUCCESS Disc tray closed
```

### `rgh popup show`
Show a native Xbox 360 popup. This is the trainer-style popup path, not a standard XNotify toast.

```powershell
rgh popup show --title "XeCLI" --body "Connected to console"
rgh popup show --title "Warning" --body "Module reload required" --preset warning
rgh popup show --title "Question" --body "Continue?" --preset question
rgh popup show --title "System Message" --body "Operation complete" --style 3
```

Preset values:

- `none`
- `error`
- `warning`
- `question`

Example output:

```text
SUCCESS Popup requested
Title="XeCLI" Preset=warning Buttons=1
```

## Save Commands
### `rgh save list`
```powershell
rgh save list --titleid FFFE07D1 --device Hdd1
rgh save list --titleid 415608C3 --profile E00012AA8D7879B4
```

Example output:

```text
Save Files
/Hdd1/Content/E00012AA8D7879B4/415608C3/00010000/savegame.svg
```

### `rgh save extract`
```powershell
rgh save extract --titleid 415608C3 --out .\saves
rgh save extract --titleid 415608C3 --profile E00012AA8D7879B4 --device Hdd1 --overwrite
```

Example output:

```text
SUCCESS Save extract complete
3 file(s)  8.2 MB -> .\saves\Grand Theft Auto V (0x415608C3)
```

### `rgh save inject`
```powershell
rgh save inject --titleid 415608C3 --in .\saves --device Hdd1
rgh save inject --titleid 415608C3 --profile E00012AA8D7879B4 --in .\save.bin --overwrite
```

Example output:

```text
SUCCESS Save inject complete
1 file(s)  128 KB -> /Hdd1/Content/E00012AA8D7879B4/415608C3
```

## Content Commands
### `rgh content list`
```powershell
rgh content list
rgh content list --device Hdd1 --show-types
rgh content list --titleid 415608C3
```

Example output:

```text
Installed Content
415608C3  Grand Theft Auto V  Game
415608C3  Grand Theft Auto V  Title Update
```

### `rgh content delete`
```powershell
rgh content delete --titleid 415608C3 --type "Title Update"
```

Treat delete operations as destructive.

Example output:

```text
SUCCESS Content delete complete
Title Update for 0x415608C3 removed
```

### `rgh content browse`
```powershell
rgh content browse .\profile.con
rgh content browse .\FFFE07D1.gpd
```

Use `browse` to inspect a local profile package or dashboard GPD file.

Example output:

```text
Content Files
Alpha.bin  2 B  1
Zeta.bin   1 B  1
```

## Local Content Commands
These commands operate on local files on the PC, not live console memory. A typical workflow is:

1. Use `rgh profiles --json` or `rgh content list` to locate the container on the console.
2. Pull it locally with `rgh ftp get` or `rgh fs get`.
3. Inspect or edit it with `rgh con`, `rgh profile`, and `rgh xdbf`.
4. Validate the result with `rgh con verify` before copying it back anywhere else.

Read-only inspection does not require signing material. Mutating a CON package requires the operator's own decrypted Xbox 360 keyvault through `--keyvault <FILE>` so XeCLI can refresh STFS hashes and preserve a valid CON signature. XeCLI never bundles or supplies a keyvault. Treat your keyvault as sensitive, keep it in a private local location, and never publish or share it.

### `rgh con`
Use `con` for package-level metadata, verification, rehash, resign, and FATX-path derivation.

```powershell
$keyvaultPath = "D:\Secure\decrypted-keyvault.bin"
rgh con info .\E00012AA8D7879B4.con
rgh con verify .\E00012AA8D7879B4.con
rgh con rehash .\E00012AA8D7879B4.con --keyvault $keyvaultPath
rgh con resign .\E00012AA8D7879B4-copy.con --keyvault $keyvaultPath
rgh con magic-name .\E00012AA8D7879B4.con
rgh con fatx-path .\E00012AA8D7879B4.con --fix-name
```

Example output:

```text
Path             .\E00012AA8D7879B4.con
Signature Type   Console
Content Type     Profile
STFS             valid
FATX Path        Content\E00012AA8D7879B4\FFFE07D1\00010000\E00012AA8D7879B4
```

### `rgh profile`
Use `profile` for decoded profile/package workflows: package summary, account extraction, embedded GPD extraction, title records, achievements, settings, and avatar colors.

`rgh profiles` and `rgh profile` are different:

- `rgh profiles` discovers live console profiles over XBDM/JRPC2/FTP/F3
- `rgh profile` works on one local pulled profile container such as `E000xxxxxxxxxxxx.con`

```powershell
$keyvaultPath = "D:\Secure\decrypted-keyvault.bin"
rgh profile info .\E00012AA8D7879B4.con
rgh profile extract .\E00012AA8D7879B4.con .\profile-files
rgh profile account show .\E00012AA8D7879B4.con
rgh profile account extract .\E00012AA8D7879B4.con .\Account
rgh profile account set-gamertag .\E00012AA8D7879B4.con ExampleUser --keyvault $keyvaultPath
rgh profile gpd list .\E00012AA8D7879B4.con
rgh profile gpd extract .\E00012AA8D7879B4.con .\FFFE07D1.gpd --dashboard
rgh profile gpd extract .\E00012AA8D7879B4.con .\415607E7.gpd --titleid 415607E7
rgh profile titles list .\E00012AA8D7879B4.con
rgh profile titles add .\E00012AA8D7879B4.con --titleid 415607E7 --name "Example Game" --keyvault $keyvaultPath
rgh profile achievements list .\E00012AA8D7879B4.con --titleid 415607E7
rgh profile achievements unlock .\E00012AA8D7879B4.con --titleid 415607E7 --achievementid 0x00000004 --keyvault $keyvaultPath
rgh profile achievements lock .\E00012AA8D7879B4.con --titleid 415607E7 --achievementid 0x00000004 --keyvault $keyvaultPath
rgh profile settings list .\E00012AA8D7879B4.con
rgh profile settings get .\E00012AA8D7879B4.con 0x10040006
rgh profile settings set .\E00012AA8D7879B4.con 0x10040006 1337 --keyvault $keyvaultPath
rgh profile avatar-colors get .\E00012AA8D7879B4.con
rgh profile avatar-colors set .\E00012AA8D7879B4.con --hair 0xFF112233 --face-paint 0xFF556677 --keyvault $keyvaultPath
rgh profile titles list .\E00012AA8D7879B4.con --csv
```

Notes:

- `profile account extract` exports the raw `Account` payload without needing a full package extract.
- `profile gpd list` surfaces the dashboard GPD plus each embedded title GPD found in the container.
- `profile gpd extract` is the targeted path when you only want `FFFE07D1.gpd` or one game GPD.
- `profile titles add` creates or refreshes one dashboard title record. Add `--replace` when that Title ID already exists; possible achievement and gamerscore totals are derived from an embedded title GPD when available or can be supplied explicitly.
- `profile titles list --csv` emits the same title inventory as a stable CSV stream for scripts and spreadsheets.
- `profile settings set` can update existing records or create missing records when `--type` is supplied.
- `profile avatar-colors` edits the dashboard avatar blob stored in setting `0x63E80044`.
- Every mutation example above includes `--keyvault` because the sample input is a CON profile. The operator must supply and protect that decrypted keyvault; XeCLI never bundles one.
- Mutating commands are safest on a disposable copy until you are comfortable with the exact workflow.

Example output:

```text
Profile Package
Profile ID      0xE00003608D3F513F
Signature       Console
Gamertag        ExampleUser
Dashboard GPD   present
Account         present
Titles          3
Gamerscore      50
```

### `rgh xdbf`
Use `xdbf` when you want raw record-level access to a pulled `*.gpd` or other XDBF-backed file.

```powershell
rgh xdbf list .\FFFE07D1.gpd --show-sync
rgh xdbf get .\FFFE07D1.gpd settings 0x0000000010040006
rgh xdbf get .\FFFE07D1.gpd settings 0x0000000010040006 --out .\gamerscore-setting.bin
rgh xdbf extract .\415607E7.gpd .\records
rgh xdbf sync-status .\415607E7.gpd
```

Notes:

- `--origin profile|pec` controls how sync records are interpreted for profile-backed versus PEC-backed files.
- `--show-sync` and `sync-status` are useful when you need to understand which records are dirty before or after a profile edit.
- `xdbf` is the low-level inspection path; prefer `profile ...` when XeCLI already exposes a higher-level decoded command.

Example output:

```text
XDBF Records
Namespace    Settings (0x0003)
ID           0x0000000010040006
Size         24
Offset       0x0000091C
Pending      yes
```

## Avatar Commands
The avatar surface now has three operator paths:

- `rgh avatar choose` for a terminal picker
- `rgh avatar browse` for a Windows picker
- `rgh avatar install` for direct one-item or full-title installs

All three use the same local or hosted `Avatar-Item-Collection` index, ownership patching, caching, and upload pipeline.

Validated behavior:

- `rgh avatar install` patches ownership for the target XUID before upload.
- `rgh avatar apply` is an alias of `rgh avatar install`.
- Standard avatar content layouts under `00009000` are supported.
- Irregular title-root payload layouts are also supported.
- If the console FTP service is not running, install can fall back to XBDM upload and XBDM verification automatically.
- `--remote` switches the browser/install flow to the hosted manifest and content cache.
- If a remote item does not show up, confirm the title ID and source, then retry with a single item first before using `--all` or `--overwrite`.

### `rgh avatar library show`
```powershell
rgh avatar library show
rgh avatar library show --json
```

Example output:

```text
Mode                local
Configured Library  auto
Effective Library   <avatar-library-root>
Configured Cache    default
Effective Cache     <config-dir>\avatar-index.v5.json
Manifest URL        https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/avatar-manifest.json
Content Base URL    https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/
```

### `rgh avatar library set`
```powershell
rgh avatar library set --path "$env:USERPROFILE\Downloads\Avatar-Item-Collection"
rgh avatar library set --cache "$env:APPDATA\XeCLI"
rgh avatar library set --clear
```

Example output:

```text
SUCCESS Avatar library settings updated
Library: <avatar-library-root>
Cache:   <config-dir>\avatar-index.v5.json
```

### `rgh avatar games`
```powershell
rgh avatar games
rgh avatar games --search "Black Ops"
rgh avatar games --remote --search "Black Ops"
rgh avatar games --limit 20
rgh avatar games --no-cache
```

Example output:

```text
Avatar Games
0x415608C3   COD: Black Ops II   37 items   7.44 MB   Activision
```

### `rgh avatar items`
```powershell
rgh avatar items --titleid 415608C3 --limit 10
rgh avatar items --search hoodie
rgh avatar items --titleid 58410A5D --limit 5
rgh avatar items --remote --titleid 415608C3 --limit 10
```

Example output:

```text
Avatar Items
0x415608C3   COD: Black Ops II   COD: Black Ops II Logo Shirt White - Female   000000080DF3B242CAE65A52415608C3   00009000   116 KB
0x58410A5D   Destination Arcade   A cool hoodie                                  0000020800060102C383304058410A5D   root      112 KB
```

### `rgh avatar choose`
```powershell
rgh avatar choose --search "Black Ops" --current-user
rgh avatar choose --remote --titleid 58410A5D --all --current-user --overwrite
```

What it does:

- resolves one title interactively or from `--titleid`
- lets you select items in terminal, unless `--all` is set
- hands the chosen set to the same install pipeline used by `rgh avatar install`

Example output:

```text
Avatar download 3 item(s) file 2/3 | Destination Arcade - 0000020800069131C14650A158410A5D: 21%
Current user: ExampleUser | XUID: 0x<xuid> | State: Signed in to Xbox Live
SUCCESS Avatar install complete
3 item(s)  512 KB -> /Hdd1/Content/0000000000000000/58410A5D/0000020800060102C383304058410A5D via FTP
```

### `rgh avatar browse`
```powershell
rgh avatar browse --remote
rgh avatar browse --remote --titleid 415608C3 --search hoodie
```

What it does:

- opens the Windows avatar picker
- shows title search, item search, tag filtering, multi-select, select-all, and clear
- displays the current signed-in user when that information can be resolved
- installs the selected set through the same pipeline used by `rgh avatar install`

Notes:

- `browse` is Windows-only.
- `gui` is an alias of `browse`.
- the picker shares the same hosted/local content source and download cache as the CLI commands.

### `rgh avatar install`
```powershell
rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user
rgh avatar install --contentid 0000020800060102C383304058410A5D --current-user
rgh avatar install --titleid 415608C3 --all --current-user
rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user --dry-run
rgh avatar install --remote --titleid 415608C3 --all --current-user
```

Notes:

- Use `--current-user` or `--xuid <XUID>` for ownership targeting.
- `--gamertag <NAME>` is only a local output label when `--xuid` is explicit.
- `--overwrite` replaces an existing remote item.
- `--device` defaults to `Hdd1`.
- `--dry-run` prints a preflight summary with title, category, profile, and cache state and does not check the console.
- When the console FTP service is not running, XeCLI can fall back to XBDM upload and XBDM verification automatically.
- Multi-item installs emit batch progress plus per-item transfer bars.

### `rgh avatar cache`
```powershell
rgh avatar cache status
rgh avatar cache refresh
rgh avatar cache status --remote
```

Notes:

- `status` reports the current local or remote avatar cache state without probing the console.
- `refresh` rebuilds the cache from the local collection or hosted manifest.
- both commands accept the same library, cache, and remote URL overrides as the other avatar library commands.

Install verification:

- Standard layout:
  - source item:
    - `<local-source-item-path>`
  - verified remote path:
    - `/Hdd1/Content/0000000000000000/415608C3/00009000/000000080DF3B242CAE65A52415608C3`
- Root layout:
  - source item:
    - `<local-source-item-path>`
  - verified remote path:
    - `/Hdd1/Content/0000000000000000/58410A5D/0000020800060102C383304058410A5D`

Example output:

```text
Current user: ExampleUser | XUID: 0x<xuid> | State: Signed in to Xbox Live
SUCCESS Avatar install complete
1 item(s)  116 KB -> /Hdd1/Content/0000000000000000/415608C3/00009000/000000080DF3B242CAE65A52415608C3 via XBDM
```

### `rgh avatar apply`
`rgh avatar apply` is an alias of `rgh avatar install`.

```powershell
rgh avatar apply --contentid 000000080DF3B242CAE65A52415608C3 --current-user
rgh avatar apply --titleid 415608C3 --all --current-user
```

## Plugin Commands
### `rgh plugin list`
```powershell
rgh plugin list
```

Example output:

```text
DashLaunch Plugins
slot1  Hdd:\xbdm.xex
slot5  Hdd:\XDRPC.xex
```

### `rgh plugin enable`
```powershell
rgh plugin enable --slot 5 --path Hdd:\XDRPC.xex
rgh plugin enable --slot 5 --path Hdd:\XDRPC.xex --backup
```

Example output:

```text
SUCCESS Plugin enabled
slot 5 -> Hdd:\XDRPC.xex
```

### `rgh plugin disable`
```powershell
rgh plugin disable --slot 5
```

These commands edit `launch.ini` over FTP. Back up first when changing a live configuration.

Example output:

```text
SUCCESS Plugin disabled
slot 5 cleared
```

## GOD Commands
### `rgh god info`
```powershell
rgh god info .\game.iso
```

Example output:

```text
ISO Info
Title ID   415608C3
Name       Grand Theft Auto V
Media      DVD1
```

### `rgh god build`
```powershell
rgh god build .\game.iso .\god
rgh god build .\game.iso .\god --trim end --threads 2
```

Example output:

```text
SUCCESS GoD build complete
output written to .\god\415608C3
```

### `rgh god watch`
```powershell
rgh god watch .\incoming --dest .\god
rgh god watch .\incoming --dest .\god --recursive --move-done .\done --move-failed .\failed
rgh god watch .\incoming --dest .\god --once
```

The watchdog waits for file stability before starting conversion and can process a directory once and exit for automation use.

Example output:

```text
Watching .\incoming
queued   game.iso
done     game.iso -> .\god\415608C3
```

## Xtaf-CLI Commands
SaveEditors Xtaf-CLI is an optional external tool for mount, recovery, trim, and custom-volume workflows. XeCLI does not duplicate its FATX engine. Use `rgh xtaf-cli config` to store the path, `rgh xtaf-cli status` to inspect readiness, `rgh xtaf-cli install` to fetch the pinned 1.0.0 Windows release, and `rgh xtaf-cli run --arg ...` to pass raw arguments through to the configured executable.

WinFsp 2.x is only required for mount-oriented workflows. `rgh xtaf-cli status` reports core CLI readiness separately from mount readiness. Add `--require-mount` when a script must fail unless WinFsp mount support is available.

### `rgh xtaf-cli config`
```powershell
rgh xtaf-cli config --path ".\tools\Xtaf-CLI"
rgh xtaf-cli config
```

### `rgh xtaf-cli status`
```powershell
rgh xtaf-cli status
rgh xtaf-cli status --json
rgh xtaf-cli status --require-mount --json
```

### `rgh xtaf-cli install`
```powershell
rgh xtaf-cli install
rgh xtaf-cli install --path ".\tools\Xtaf-CLI"
rgh xtaf-cli install --archive .\Xtaf-CLI-v1.0.0-windows.zip --sha256 D2C561074E55A61CC5D4DA9D3D7B9865EEC7886D2DA90BE49BB7AA8ED02B2E73
rgh xtaf-cli install --url <custom-url> --sha256 <64-hex-sha256>
```

The built-in source is `SaveEditors/Xtaf-CLI` tag `v1.0`, asset `Xtaf-CLI-v1.0.0-windows.zip`, authenticated by SHA-256 `D2C561074E55A61CC5D4DA9D3D7B9865EEC7886D2DA90BE49BB7AA8ED02B2E73`. Custom archives and URLs are accepted only with an explicit `--sha256`.

### `rgh xtaf-cli run`
```powershell
rgh xtaf-cli run --arg=--help
```

Use `--arg=<value>` once per token when the value starts with a dash, or `--arg <value>` for ordinary values. XeCLI passes the exact argument list through to Xtaf-CLI. If the argument set is mount-oriented and WinFsp is missing, XeCLI stops early with a clear installation reminder instead of launching a failing mount attempt.

## Ghidra Commands
Ghidra is external and documented in the CLI as `(Free)`. XeCLI does not bundle Ghidra or Java. After `rgh ghidra config --path <dir>`, XeCLI installs the pinned `SaveEditors/XEXLoaderWV` 13.0.0 loader asset into that Ghidra install with `rgh ghidra install-loader`.

### `rgh ghidra config`
```powershell
rgh ghidra config --path "C:\Tools\ghidra" --java "C:\Java"
```

Example output:

```text
SUCCESS Ghidra config updated
path=C:\Tools\ghidra
java=C:\Java
```

### `rgh ghidra install-loader`
```powershell
rgh ghidra install-loader
rgh ghidra install-loader --archive .\ghidra_12.0.4_PUBLIC_20260325_XEXLoaderWV.zip --sha256 498B9C2A2430585CC49A13DB33603B6A46CFE84B157985F9BE2C4360F917FA5A
rgh ghidra install-loader --url <custom-url> --sha256 <64-hex-sha256>
```

The built-in source is owner/repository `SaveEditors/XEXLoaderWV`, tag/version `13.0.0`, filename `ghidra_12.0.4_PUBLIC_20260325_XEXLoaderWV.zip`, and SHA-256 `498B9C2A2430585CC49A13DB33603B6A46CFE84B157985F9BE2C4360F917FA5A`. Custom sources require an explicit hash.

Example output:

```text
SUCCESS Ghidra XEX loader installed
XEXLoaderWV -> C:\Tools\ghidra\Ghidra\Extensions\XEXLoaderWV\lib\XEXLoaderWV.jar
```

### `rgh ghidra analyze`
```powershell
rgh ghidra analyze --in .\title.xex
rgh ghidra analyze --running
rgh ghidra analyze --ftp-path /Hdd1/Aurora/Aurora.xex
```

Example output:

```text
SUCCESS Ghidra analysis complete
project=title  loader=xex
```

### `rgh ghidra decompile`
```powershell
rgh ghidra decompile --in .\title.xex --out .\decomp
rgh ghidra decompile --running --out .\decomp --max 200
```

Example output:

```text
SUCCESS Ghidra decompile complete
200 function files written to .\decomp
```

### `rgh ghidra verify`
```powershell
rgh ghidra verify --dir .\decomp
rgh ghidra verify --dir .\decomp --json
```

Example output:

```text
No flagged files found.
```

## IDA Commands
IDA is external and not bundled by XeCLI. The supported baseline is `IDA Pro 9.3` with `SaveEditors/idaxex` tag `ida-pro-9.3-saveeditors-1`. Legacy support is specifically `IDA Pro 9.1.250226` with `emoose/idaxex` tag `0.42b`, including its authenticated `xex1tool.exe`. Other IDA or loader combinations are outside the supported baseline.

### `rgh ida config`
```powershell
rgh ida config --path "C:\Program Files\IDA Professional 9.3" --python python
rgh ida config
```

Example output:

```text
SUCCESS IDA settings updated
Stored IDA install, python, and analysis settings were saved.
```

### `rgh ida check`
```powershell
rgh ida check
rgh ida check --json
```

Example output:

```text
Install              C:\Program Files\IDA Professional 9.3
Batch EXE            C:\Program Files\IDA Professional 9.3\idat.exe
IDA build            9.3 (supported)
Loader               compatible / ida93 (supported)
TIL files            present
idalib import        ok
```

### `rgh ida install-loader`
```powershell
rgh ida install-loader
rgh ida install-loader --archive .\ida-pro-9.3-saveeditors-1.zip --sha256 1734277D26FF4985F15931B6855368312EC11D619D53D2C104F9F7C5753BBD7C
rgh ida install-loader --url <custom-url> --sha256 <64-hex-sha256>
```

The 9.3 archive SHA-256 is `1734277D26FF4985F15931B6855368312EC11D619D53D2C104F9F7C5753BBD7C`. The legacy `idaxex+xex1tool-0.42b_ida91.zip` archive SHA-256 is `49F7C519C4A0BF7E90AF3411554B00591C0C628676E1CBA62A7EAA6216BDCD89`. Custom sources require `--sha256`, and installation verifies every copied loader, TIL, and `xex1tool` payload against the manifest for the detected IDA build.

Example output:

```text
SUCCESS IDA loader installed
Compatible loader -> C:\Program Files\IDA Professional 9.3\loaders\xex-loader.dll
```

### `rgh ida analyze`
```powershell
rgh ida analyze --in .\title.xex --out-db .\title.i64 --overwrite
rgh ida analyze --ftp-path /Hdd1/Aurora/Aurora.xex --out-db .\Aurora.i64 --overwrite
```

`rgh ida analyze` imports through the IDA batch executable and writes the generated `.i64` database to the path you choose with `--out-db`.

Example output:

```text
SUCCESS IDA analysis complete
.\Aurora.i64 segments 7 functions 40129
```

### `rgh ida decompile`
```powershell
rgh ida decompile --in .\Aurora.i64 --out .\ida-decomp --max 50
rgh ida decompile --running --out .\ida-decomp --out-db .\Aurora.i64 --keep-db
```

`rgh ida decompile` accepts `--backend auto|batch|idalib`. `batch` keeps the file-based IDA path, while `idalib` is used when the configured Python runtime can import IDA's library-backed helpers.

Example output:

```text
SUCCESS IDA decompile complete
1 file(s) core idalib output .\ida-decomp
```

### `rgh ida verify`
```powershell
rgh ida verify --dir .\ida-decomp
rgh ida verify --dir .\ida-decomp --json
```

Example output:

```text
No flagged files found.
```

### `rgh xex ida-decompile`
```powershell
rgh xex ida-decompile --ftp-path /Hdd1/Aurora/Aurora.xex --out .\ida-decomp --max 50 --out-db .\Aurora.i64 --keep-db
```

## Practical Workflows
### Fast console health check
```powershell
rgh ping
rgh status --quick
rgh title
```

### Pull a running title for analysis
```powershell
rgh xex dump --out .\title.xex
rgh xex strings --file .\title.xex --unicode --min-length 6 --limit 50
rgh xex map --file .\title.xex --ghidra-base 0x82000000 --ghidra 0x82001234
rgh ghidra decompile --in .\title.xex --out .\decomp
rgh xex ida-decompile --in .\title.xex --out .\ida-decomp --max 50 --out-db .\title.i64 --keep-db
```

### Verify a reboot-expected module load
```powershell
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
rgh modules pending
```

### Capture a verified NAND backup
See [XeCLI-XellFetch](XeCLI-XellFetch) for the managed XeLL launch, keyvault export, and verified NAND backup target that used to be shown here.
