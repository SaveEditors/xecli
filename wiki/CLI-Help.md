# CLI Help

XeCLI v2.0.0 uses the `rgh` command. This page is generated from the Release executable and lists every canonical command path without duplicating shortcut trees.

## Usage

Use `--help` at any level of the command tree:

```powershell
rgh --help
rgh <command> --help
rgh <group> <command> --help
```

Commands that connect to a console use the current target. Start with `rgh start`, `rgh connect`, or `rgh target --help` when a target has not been configured.

## Root commands

| Command | Description |
| --- | --- |
| `rgh status` | Show a compact console status snapshot |
| `rgh profiles` | List profiles and signed-in users |
| `rgh title` | Resolve the active title, look up a raw Title ID, or search an optional user Title Database. MEDIAID prefers a matching row when available |
| `rgh report` | Generate or compare console reports as Markdown, HTML, CSV, or JSON |
| `rgh report-profile` | Store and select saved report presets |
| `rgh health` | Run offline local health checks without connecting to a console |
| `rgh script` | Run command files or review structured transcripts offline |
| `rgh mem-bookmarks` | Manage offline memory address bookmarks |
| `rgh log` | Inspect the local append-only command log cache |
| `rgh diagnostics` | Collect local support diagnostics without connecting to a console |
| `rgh support` | Collect a redacted support bundle without connecting to a console |
| `rgh release-notes` | Show the built-in release catalog without network access |
| `rgh completion` | Emit shell completion scripts to stdout |
| `rgh release` | Validate local release artifacts before publish |
| `rgh trainer` | Apply trainer JSON memory writes |
| `rgh target` | Show or set the default console target |
| `rgh language` | Show or change the saved UI language |
| `rgh ping` | Probe XBDM reachability on the current console |
| `rgh reboot` | Reboot the console (cold by default) |
| `rgh shutdown` | Power off the console |
| `rgh launch` | Launch a XEX with optional arguments |
| `rgh install` | Install a verified portable release or manage command registration |
| `rgh start` | Discover consoles and set the default target |
| `rgh connect` | Set or select the default target |
| `rgh scan` | Scan the network for consoles |
| `rgh screenshot` | Capture a live screenshot |
| `rgh network` | Inspect target, profile, FTP configuration, and recent console history without connecting |
| `rgh terminal` | Open the XeTerminal desktop interface |
| `rgh homebrew` | Download public homebrew packages to USB, a folder, or a detected console drive |
| `rgh ogxbox` | Original Xbox compatibility pack installer for HddX:\Compatibility |
| `rgh xtaf` | XTAF is XeCLI's FATX image and physical-disk manager (aliases: fatman, fatx) |
| `rgh xtaf-cli` | Optional SaveEditors Xtaf-CLI wrapper for mount, recovery, trim, and volume workflows |
| `rgh xbdm` | XBDM commands |
| `rgh modules` | Shortcut for `rgh xbdm modules` |
| `rgh module` | Alias of `rgh modules` and `rgh xbdm modules` |
| `rgh mem` | Shortcut for `rgh xbdm mem` |
| `rgh xex` | Shortcut for `rgh xbdm xex` |
| `rgh fs` | Shortcut for `rgh xbdm fs` |
| `rgh threads` | Shortcut for `rgh xbdm threads` |
| `rgh debug` | Shortcut for `rgh xbdm debug` |
| `rgh jrpc2` | JRPC2 commands |
| `rgh notify` | Send an on-screen notification |
| `rgh notify-icons` | Browse XNotify icons and manage preset aliases |
| `rgh smc` | System Management Controller helpers |
| `rgh fan` | Fan speed helpers |
| `rgh led` | Ring-of-light LED helpers |
| `rgh signin` | Signed-in user helpers |
| `rgh tray` | Disc tray helpers |
| `rgh xell` | XeLL Reloaded helpers with guided auto-launch when needed |
| `rgh popup` | Trainer-style console popup helpers |
| `rgh ftp` | FTP commands (alternate access). Requires an active console FTP service |
| `rgh target-saved` | Store and select saved target profiles |
| `rgh save` | Profile and save-data helpers over FTP |
| `rgh content` | Installed content management over FTP |
| `rgh con` | Inspect and repair local Xbox 360 content packages |
| `rgh profile` | Inspect and edit local Xbox 360 profile packages |
| `rgh xdbf` | Inspect and extract records from local GPD/XDBF files |
| `rgh avatar` | Avatar item library and install helpers |
| `rgh plugin` | DashLaunch plugin management |
| `rgh god` | ISO to Games on Demand conversion |
| `rgh ghidra` | Ghidra headless helpers (Free, external install required) |
| `rgh ida` | IDA Pro headless helpers (IDA 9.3 supported; legacy 9.1 archive compatibility available, external install required) |

## Shortcut map

These roots remain supported. Their command trees are documented once under the canonical XBDM paths.

| Shortcut | Canonical path |
| --- | --- |
| `rgh modules` | `rgh xbdm modules` |
| `rgh module` | `rgh xbdm modules` |
| `rgh mem` | `rgh xbdm mem` |
| `rgh xex` | `rgh xbdm xex` |
| `rgh fs` | `rgh xbdm fs` |
| `rgh threads` | `rgh xbdm threads` |
| `rgh debug` | `rgh xbdm debug` |

## Canonical command reference

Each section below is captured from the matching `--help` invocation with terminal color removed.

### `rgh status`

```text
DESCRIPTION:
Show a compact console status snapshot

USAGE:
    rgh status [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --quick                    Skip JRPC2, drive, and user checks for a fast snapshot
        --no-jrpc                  Skip JRPC2 queries (temps, CPU key, dashboard, title id)
        --no-drives                Skip drive and USB size reporting
        --no-users                 Skip user list and signed-in detection
```

### `rgh profiles`

```text
DESCRIPTION:
List profiles and signed-in users

USAGE:
    rgh profiles [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --no-ftp                   Skip FTP profile scan
        --no-f3                    Skip F3 profile lookup (HTTP 9999)
```

### `rgh title`

```text
DESCRIPTION:
Resolve the active title, look up a raw Title ID, or search an optional user Title Database. MEDIAID prefers a matching row when available

USAGE:
    rgh title [TITLEID] [MEDIAID] [OPTIONS]

ARGUMENTS:
    [TITLEID]    Title ID (hex, with or without 0x), or a title name search when not hex. Omit it or pass 'active' to resolve the current title
    [MEDIAID]    Optional media ID (hex). Prefers a matching row when available, but still shows all title-ID matches

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --active                   Resolve the currently active title from the connected console
        --database <PATH>          Use a user-supplied CSV or TXT Title Database for this lookup. Overrides settings and XECLI_TITLE_DATABASE
```

### `rgh report`

```text
DESCRIPTION:
Generate or compare console reports as Markdown, HTML, CSV, or JSON

USAGE:
    rgh report [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --report-profile <NAME>    Apply a saved report profile by name
        --out <FILE>               Write the report to a file instead of stdout
        --format <FORMAT>          Report format: md, html, csv, or json. Default: md
        --include-modules          Include loaded module inventory
        --include-threads          Include thread inventory
        --include-memory           Include XBDM memory region map
        --include-private          Keep console and network identifiers visible instead of redacting them
        --since <DATE>             Include only modules at or after the given UTC date/time
        --limit <N>                Limit the number of modules after filtering
        --status <VALUE>           Only include optional report sections when the console execution state matches
        --diff                     Suppress timestamps and volatile duration fields for stable diff output
        --left <FILE>              Compare this saved report file against --right
        --right <FILE>             Compare this saved report file against --left
```

### `rgh report-profile`

```text
DESCRIPTION:
Store and select saved report presets

USAGE:
    rgh report-profile [OPTIONS] <COMMAND>

EXAMPLES:
    rgh report-profile add operator-review --format json --include-modules
    rgh report-profile list
    rgh report-profile show operator-review
    rgh report-profile remove operator-review

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    add <NAME>       Add or update a saved report profile
    list             List saved report profiles
    show <NAME>      Show one saved report profile
    remove <NAME>    Remove a saved report profile
```

#### `rgh report-profile add`

```text
DESCRIPTION:
Add or update a saved report profile

USAGE:
    rgh report-profile add <NAME> [OPTIONS]

ARGUMENTS:
    <NAME>    Profile name

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --format <FORMAT>          Report format: md, html, csv, or json
        --include-modules          Include loaded module inventory
        --include-threads          Include thread inventory
        --include-memory           Include XBDM memory region map
        --include-private          Keep console and network identifiers visible instead of redacting them
        --since <DATE>             Include only modules at or after the given UTC date/time
        --limit <N>                Limit the number of modules after filtering
        --status <VALUE>           Only include optional report sections when the console execution state matches
        --diff                     Suppress timestamps and volatile duration fields for stable diff output
```

#### `rgh report-profile list`

```text
DESCRIPTION:
List saved report profiles

USAGE:
    rgh report-profile list [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show saved endpoints in full
```

#### `rgh report-profile show`

```text
DESCRIPTION:
Show one saved report profile

USAGE:
    rgh report-profile show <NAME> [OPTIONS]

ARGUMENTS:
    <NAME>

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show saved endpoints in full
```

#### `rgh report-profile remove`

```text
DESCRIPTION:
Remove a saved report profile

USAGE:
    rgh report-profile remove <NAME> [OPTIONS]

ARGUMENTS:
    <NAME>

OPTIONS:
    -h, --help    Prints help information
        --json    Emit JSON output
```

### `rgh health`

```text
DESCRIPTION:
Run offline local health checks without connecting to a console

USAGE:
    rgh health [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show saved target endpoints in full, including the port
```

### `rgh script`

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
    run <FILE>             Run newline-delimited local CLI commands from a file
    replay <TRANSCRIPT>    Replay a structured batch transcript offline for review without executing commands
```

#### `rgh script run`

```text
DESCRIPTION:
Run newline-delimited local CLI commands from a file

USAGE:
    rgh script run <FILE> [OPTIONS]

ARGUMENTS:
    <FILE>    File containing one CLI command per line

OPTIONS:
    -h, --help              Prints help information
        --dry-run           Validate and print the batch without running any commands
        --allow-live        Allow live or destructive commands instead of blocking them
        --transcript-out    Write a structured redacted transcript artifact to the given file
```

#### `rgh script replay`

```text
DESCRIPTION:
Replay a structured batch transcript offline for review without executing commands

USAGE:
    rgh script replay <TRANSCRIPT> [OPTIONS]

ARGUMENTS:
    <TRANSCRIPT>    Structured batch transcript artifact to review offline

OPTIONS:
    -h, --help       Prints help information
        --dry-run    Review the transcript offline without executing child commands
```

### `rgh mem-bookmarks`

```text
DESCRIPTION:
Manage offline memory address bookmarks

USAGE:
    rgh mem-bookmarks [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    add       Add a memory bookmark
    rename    Rename a memory bookmark
    list      List memory bookmarks
    show      Show a stored memory bookmark
    export    Export memory bookmarks to a JSON file
    import    Import memory bookmarks from a symbol sidecar
    remove    Remove a memory bookmark
```

#### `rgh mem-bookmarks add`

```text
DESCRIPTION:
Add a memory bookmark

USAGE:
    rgh mem-bookmarks add [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --name <NAME>        Bookmark name
        --addr <ADDR>        Live runtime address to store as entered
        --module <MODULE>    Module name or path to store as entered
        --rva <ADDR>         Relative virtual address to store with --module
        --note <TEXT>        Optional note
        --json               Output JSON
```

#### `rgh mem-bookmarks rename`

```text
DESCRIPTION:
Rename a memory bookmark

USAGE:
    rgh mem-bookmarks rename [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --name <NAME>    Current bookmark name
        --to <NAME>      New bookmark name
        --json           Output JSON
```

#### `rgh mem-bookmarks list`

```text
DESCRIPTION:
List memory bookmarks

USAGE:
    rgh mem-bookmarks list [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

#### `rgh mem-bookmarks show`

```text
DESCRIPTION:
Show a stored memory bookmark

USAGE:
    rgh mem-bookmarks show [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --name <NAME>    Bookmark name
        --json           Output JSON
```

#### `rgh mem-bookmarks export`

```text
DESCRIPTION:
Export memory bookmarks to a JSON file

USAGE:
    rgh mem-bookmarks export [OPTIONS]

OPTIONS:
    -h, --help          Prints help information
        --out <FILE>    Output file path
        --json          Output JSON
```

#### `rgh mem-bookmarks import`

```text
DESCRIPTION:
Import memory bookmarks from a symbol sidecar

USAGE:
    rgh mem-bookmarks import [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --file <FILE>    Path to a symbol sidecar JSON file
        --json           Output JSON
```

#### `rgh mem-bookmarks remove`

```text
DESCRIPTION:
Remove a memory bookmark

USAGE:
    rgh mem-bookmarks remove [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --name <NAME>    Bookmark name
        --json           Output JSON
```

### `rgh log`

```text
DESCRIPTION:
Inspect the local append-only command log cache

USAGE:
    rgh log [OPTIONS] <COMMAND>

EXAMPLES:
    rgh log list
    rgh log list --limit 1 --json
    rgh log export --format csv --out .\command-log.csv

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list      List captured command log entries
    export    Export captured command log entries
```

#### `rgh log list`

```text
DESCRIPTION:
List captured command log entries

USAGE:
    rgh log list [OPTIONS]

OPTIONS:
    -h, --help         Prints help information
        --limit <N>    Maximum entries to show (default: all, 0 = no entries)
        --json         Output JSON
        --csv          Output CSV to stdout
```

#### `rgh log export`

```text
DESCRIPTION:
Export captured command log entries

USAGE:
    rgh log export [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --format <FORMAT>    Export format. Only csv is supported
        --out <FILE>         Output file path
```

### `rgh diagnostics`

```text
DESCRIPTION:
Collect local support diagnostics without connecting to a console

USAGE:
    rgh diagnostics [OPTIONS] <COMMAND>

EXAMPLES:
    rgh diagnostics bundle
    rgh diagnostics bundle --folder --out .\xecli-diagnostics

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    bundle    Create a redacted local diagnostics bundle
```

#### `rgh diagnostics bundle`

```text
DESCRIPTION:
Create a redacted local diagnostics bundle

USAGE:
    rgh diagnostics bundle [OPTIONS]

OPTIONS:
    -h, --help          Prints help information
        --out <PATH>    Output zip file or folder path. Default: xecli-diagnostics-<timestamp>.zip or xecli-support-<timestamp>.zip in the current directory
        --folder        Write an unpacked bundle folder instead of a zip archive
        --force         Overwrite an existing zip file or replace an existing bundle folder
        --json          Emit JSON output
        --support       Write the support-facing bundle layout and wording
```

### `rgh support`

```text
DESCRIPTION:
Collect a redacted support bundle without connecting to a console

USAGE:
    rgh support [OPTIONS] <COMMAND>

EXAMPLES:
    rgh support bundle
    rgh support bundle --folder --out .\xecli-support

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    bundle    Create a curated redacted support bundle
```

#### `rgh support bundle`

```text
DESCRIPTION:
Create a curated redacted support bundle

USAGE:
    rgh support bundle [OPTIONS]

OPTIONS:
    -h, --help          Prints help information
        --out <PATH>    Output zip file or folder path. Default: xecli-diagnostics-<timestamp>.zip or xecli-support-<timestamp>.zip in the current directory
        --folder        Write an unpacked bundle folder instead of a zip archive
        --force         Overwrite an existing zip file or replace an existing bundle folder
        --json          Emit JSON output
```

### `rgh release-notes`

```text
DESCRIPTION:
Show the built-in release catalog without network access

USAGE:
    rgh release-notes [OPTIONS]

OPTIONS:
    -h, --help                 Prints help information
        --latest               Show the latest built-in release notes
        --version <VERSION>    Show a specific built-in release version, such as v2.0.0
        --json                 Output JSON
```

### `rgh completion`

```text
DESCRIPTION:
Emit shell completion scripts to stdout

USAGE:
    rgh completion <SHELL> [OPTIONS]

ARGUMENTS:
    <SHELL>    Shell to generate completion for: powershell, pwsh, ps, bash, or zsh

OPTIONS:
    -h, --help    Prints help information
```

### `rgh release`

```text
DESCRIPTION:
Validate local release artifacts before publish

USAGE:
    rgh release [OPTIONS] <COMMAND>

EXAMPLES:
    rgh release check --publish-dir .\out\win-x64
    rgh release check --publish-dir .\out\win-x64 --zip .\out\release\XeCLI-2.0.0-win-x64.zip

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    check    Validate a publish directory, release zip, and checksum metadata locally
```

#### `rgh release check`

```text
DESCRIPTION:
Validate a publish directory, release zip, and checksum metadata locally

USAGE:
    rgh release check [OPTIONS]

OPTIONS:
    -h, --help                            Prints help information
        --publish-dir <DIR>               Published release directory to validate
        --project <FILE>                  Project file used for AppVersion comparison
        --zip <FILE>                      Release zip to validate against its SHA256 sidecar; required with --promotion
        --hash <FILE>                     SHA256 sidecar for --zip; required with --promotion and otherwise defaults to <zip>.sha256
        --summary <FILE>                  Release summary whose artifact sidecars must be validated; required with --promotion
        --expected-release-kind <KIND>    Require an exact recognized ReleaseKind
        --promotion                       Apply fail-closed public promotion policy checks
        --json                            Emit JSON output
```

### `rgh trainer`

```text
DESCRIPTION:
Apply trainer JSON memory writes

USAGE:
    rgh trainer [OPTIONS] <COMMAND>

EXAMPLES:
    rgh trainer apply --file .\trainer.json --dry-run
    rgh trainer apply --file .\trainer.json

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    apply    Apply a trainer JSON file to live memory
```

#### `rgh trainer apply`

```text
DESCRIPTION:
Apply a trainer JSON file to live memory

USAGE:
    rgh trainer apply [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --file <FILE>              Trainer JSON file to apply
        --dry-run                  Validate the trainer file and show what would be written without connecting
```

### `rgh target`

```text
DESCRIPTION:
Show or set the default console target

USAGE:
    rgh target [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --set <IP>       Set the default console IP
        --port <PORT>    Set the default port (default: 730)
        --clear          Clear the saved target
```

### `rgh language`

```text
DESCRIPTION:
Show or change the saved UI language

USAGE:
    rgh language [OPTIONS]

OPTIONS:
    -h, --help          Prints help information
        --set <LANG>    Save the default UI language (en or es)
        --clear         Clear the saved UI language and fall back to --lang, XECLI_LANG, or the system language
```

### `rgh ping`

```text
DESCRIPTION:
Probe XBDM reachability on the current console

USAGE:
    rgh ping [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

### `rgh reboot`

```text
DESCRIPTION:
Reboot the console (cold by default)

USAGE:
    rgh reboot [OPTIONS]

OPTIONS:
                                   DEFAULT
    -h, --help                                Prints help information
        --ip <IP>                             Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>               Use a saved target profile by name
        --port <PORT>                         TCP port (default: 730)
        --timeout <MS>                        XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                                Emit JSON output
        --no-reconnect                        Disable automatic XBDM reconnect attempts
        --dry-run                             Show the reboot request without connecting or writing anything
        --yes                                 Skip the reboot confirmation prompt
        --title                               Restart the current title instead of a cold reboot
        --wait                                Stop execution and require a changed process or XBDM disconnect and responsive reconnect
        --wait-timeout <SEC>       120        Maximum seconds to wait for a verified reboot transition (default: 120)
        --notify                              Send a default success notification to the console
        --notify-icon <NAME>                  Notification icon preset name
        --notify-logo <ID>                    Notification logo id (decimal or 0x hex)
```

### `rgh shutdown`

```text
DESCRIPTION:
Power off the console

USAGE:
    rgh shutdown [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --dry-run                  Show the shutdown request without connecting or writing anything
        --yes                      Skip the shutdown confirmation prompt
        --notify                   Send a default success notification to the console before shutdown
        --notify-icon <NAME>       Notification icon preset name
        --notify-logo <ID>         Notification logo id (decimal or 0x hex)
```

### `rgh launch`

```text
DESCRIPTION:
Launch a XEX with optional arguments

USAGE:
    rgh launch [XEX] [OPTIONS]

ARGUMENTS:
    [XEX]    XEX path to launch, for example Hdd1:\Aurora\Aurora.xex

OPTIONS:
                                   DEFAULT
    -h, --help                                Prints help information
        --ip <IP>                             Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>               Use a saved target profile by name
        --port <PORT>                         TCP port (default: 730)
        --timeout <MS>                        XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                                Emit JSON output
        --no-reconnect                        Disable automatic XBDM reconnect attempts
        --xex <PATH>                          XEX path to launch, for example Hdd1:\Aurora\Aurora.xex
        --directory <DIR>                     Working directory passed to XBDM. Defaults to the XEX folder
        --args <TEXT>                         Command-line arguments passed to the XEX
        --titleid <TITLEID>                   Optional Title ID for display/logging
        --dry-run                             Show the generated XBDM command without executing it
        --verify                              Require remote path verification before sending the launch request
        --no-verify                           Skip remote path verification for a known recovery path
        --wait                                Stop execution and require the requested active XEX plus observable transition evidence
        --wait-timeout <SEC>       45         Maximum seconds to wait for a verified launch transition (default: 45)
        --notify                              Send a default success notification to the console
        --notify-icon <NAME>                  Notification icon preset name
        --notify-logo <ID>                    Notification logo id (decimal or 0x hex)
```

### `rgh install`

```text
DESCRIPTION:
Install a verified portable release or manage command registration

USAGE:
    rgh install [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --machine         Install for all users (administrator approval required)
        --uninstall       Remove the command registration. With --machine, remove the all-users PATH entry
        --machine-path    Legacy path-only install for the current executable directory (admin required)
        --path <DIR>      Install directory for XeCLI
        --source <DIR>    Source release directory containing rgh.exe and its runtime files
        --no-path         Do not add the install directory to PATH
        --quiet           Suppress non-error install output
```

### `rgh start`

```text
DESCRIPTION:
Discover consoles and set the default target

USAGE:
    rgh start [OPTIONS]

OPTIONS:
    -h, --help             Prints help information
        --ports <PORTS>    Comma-separated ports to probe. Default: 730,731
        --timeout <MS>     TCP timeout in milliseconds (default: 400)
        --no-nap           Disable NAP broadcast discovery
        --no-tcp           Disable TCP scan discovery
        --json             Emit JSON output
```

### `rgh connect`

```text
DESCRIPTION:
Set or select the default target

USAGE:
    rgh connect [target] [OPTIONS]

ARGUMENTS:
    [target]    Console IP or discovery index

OPTIONS:
    -h, --help             Prints help information
        --ports <PORTS>    Comma-separated ports to probe. Default: 730,731
        --timeout <MS>     TCP timeout in milliseconds (default: 400)
        --no-nap           Disable NAP broadcast discovery
        --no-tcp           Disable TCP scan discovery
        --json             Emit JSON output
```

### `rgh scan`

```text
DESCRIPTION:
Scan the network for consoles

USAGE:
    rgh scan [OPTIONS]

OPTIONS:
    -h, --help             Prints help information
        --ports <PORTS>    Comma-separated ports to probe. Default: 730,731
        --timeout <MS>     TCP timeout in milliseconds (default: 400)
        --no-nap           Disable NAP broadcast discovery
        --no-tcp           Disable TCP scan discovery
        --json             Emit JSON output
```

### `rgh screenshot`

```text
DESCRIPTION:
Capture a live screenshot

USAGE:
    rgh screenshot [OPTIONS]

OPTIONS:
    -h, --help                        Prints help information
        --ip <IP>                     Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>       Use a saved target profile by name
        --port <PORT>                 TCP port (default: 730)
        --timeout <MS>                XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                        Emit JSON output
        --no-reconnect                Disable automatic XBDM reconnect attempts
        --out <FILE>                  Output file path (.bmp, .png, or .raw)
        --format <FORMAT>             Output format: bmp, png, or raw
        --raw                         Force raw output (same as --format raw)
        --force                       Overwrite the output file if it exists
        --decode <MODE>               Force decode mode: auto, linear, tiled-v1, tiled-v2, tiled-v3, tiled-xenia
        --endianness <MODE>           Force endianness: auto, none, swap8-16, swap8-32, swap16-32
        --order <ORDER>               Force 32bpp channel order: auto, bgra, rgba, argb, abgr, bgrx, rgbx, xrgb, xbgr
        --dump-variants               Dump all decode variants to a folder next to the output file
        --crop-right <PX>             Crop N pixels from the right edge (default: 0)
        --crop-right-percent <PCT>    Crop a percentage from the right edge (0-50)
        --xenia-bank-xor <N>          Force Xenia tile bank XOR (0-1) when using tiled-xenia
        --xenia-pipe-xor <N>          Force Xenia tile pipe XOR (0-3) when using tiled-xenia
```

### `rgh network`

```text
DESCRIPTION:
Inspect target, profile, FTP configuration, and recent console history without connecting

USAGE:
    rgh network [OPTIONS] <COMMAND>

EXAMPLES:
    rgh network doctor
    rgh network recent

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    doctor    Summarize target, profile, and FTP settings without probing the network
    recent    List recently seen console endpoints from local history
```

#### `rgh network doctor`

```text
DESCRIPTION:
Summarize target, profile, and FTP settings without probing the network

USAGE:
    rgh network doctor [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show saved target endpoints in full, including the port
```

#### `rgh network recent`

```text
DESCRIPTION:
List recently seen console endpoints from local history

USAGE:
    rgh network recent [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --limit <N>          Maximum entries to show (default: 5)
        --json               Emit JSON output
        --include-private    Show console endpoints in full, including the port
```

### `rgh terminal`

```text
DESCRIPTION:
Open the XeTerminal desktop interface

USAGE:
    rgh terminal [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --opacity <PERCENT>        Window opacity percentage (55-100)
        --background <PATH>        Optional background image path for the terminal shell
        --theme <NAME>             Terminal theme: matrix, cyan, amber, mono, carbon, ruby, xenon, violet, aurora, steel, sunset, enterprise, operations, debugger, command, signal, graphite, alpine,
                                   or console
        --no-telemetry             Disable live telemetry polling in the terminal shell
```

### `rgh homebrew`

```text
DESCRIPTION:
Download public homebrew packages to USB, a folder, or a detected console drive

USAGE:
    rgh homebrew [OPTIONS] <COMMAND>

EXAMPLES:
    rgh homebrew list
    rgh homebrew list --csv
    rgh homebrew install aurora --usb E:\XeCLI-Stage
    rgh homebrew install all --usb E:\XeCLI-Stage
    rgh homebrew install aurora --device Hdd1 --ini-mode merge

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list                 List the built-in package catalog
    install <PACKAGE>    Download one or more public homebrew packages to USB, a folder, or the console
```

#### `rgh homebrew list`

```text
DESCRIPTION:
List the built-in package catalog

USAGE:
    rgh homebrew list [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Emit machine-readable output
        --csv     Emit comma-separated values to stdout
```

#### `rgh homebrew install`

```text
DESCRIPTION:
Download one or more public homebrew packages to USB, a folder, or the console

USAGE:
    rgh homebrew install <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Package to install or stage: aurora, dashlaunch, xexmenu, fsd, xm360, timefixer, simple360, xelllaunch, or all

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --usb <TARGET>             Stage to a removable USB drive, drive letter, selection number, or host folder path
        --device <DEVICE>          Install directly to a detected console device such as Hdd1, Usb0, Usb1, or Usb2
        --ini-mode <MODE>          Console install launch.ini behavior: generated, merge, or skip
        --ini <PATH>               Console launch.ini path (default: /Hdd1/launch.ini)
        --cache <DIR>              Package download cache directory
        --force-download           Redownload archives even when they already exist in the cache
        --auto-confirm             Skip confirmation prompts
```

### `rgh ogxbox`

```text
DESCRIPTION:
Original Xbox compatibility pack installer for HddX:\Compatibility

USAGE:
    rgh ogxbox [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ogxbox list
    rgh ogxbox install hacked --usb E:\XeCLI-Stage
    rgh ogxbox install hud --include-fixer

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list             List the built-in XeFu compatibility sets and partition fixer
    install <SET>    Stage or install an Original Xbox compatibility set
```

#### `rgh ogxbox list`

```text
DESCRIPTION:
List the built-in XeFu compatibility sets and partition fixer

USAGE:
    rgh ogxbox list [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Emit machine-readable output
```

#### `rgh ogxbox install`

```text
DESCRIPTION:
Stage or install an Original Xbox compatibility set

USAGE:
    rgh ogxbox install <SET> [OPTIONS]

ARGUMENTS:
    <SET>    Compatibility set to install: hacked, hud, or retail

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --usb <TARGET>             Stage to a removable USB drive, drive letter, selection number, or host folder path
        --include-fixer            Also stage or install HDD Compatibility Partition Fixer
        --cache <DIR>              Download cache directory
        --force-download           Redownload archives even when they already exist in the cache
        --auto-confirm             Skip confirmation prompts
```

### `rgh xtaf`

```text
DESCRIPTION:
XTAF is XeCLI's FATX image and physical-disk manager (aliases: fatman, fatx)

USAGE:
    rgh xtaf [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xtaf devices
    rgh xtaf disks
    rgh xtaf partitions --image .\hdd.img
    rgh xtaf partitions --disk 2
    rgh xtaf scan --image .\hdd.img
    rgh xtaf info --image .\hdd.img --partition Content
    rgh xtaf info --image .\hdd.img --offset 0xB6600000
    rgh xtaf list --image .\hdd.img --partition Content --path /
    rgh xtaf list --image .\hdd.img --offset 0xB6600000 --path /
    rgh xtaf mkdir --image .\hdd.img --partition Content --path /XeCLI
    rgh xtaf put --image .\hdd.img --partition Content --path /XeCLI/readme.txt --in .\readme.txt --overwrite
    rgh xtaf mv --image .\hdd.img --partition Content --path /XeCLI/readme.txt --to /XeCLI/readme-old.txt
    rgh xtaf rm --image .\hdd.img --partition Content --path /XeCLI --recursive
    rgh xtaf format --disk 2 --dry-run
    rgh xtaf check --image .\hdd.img --partition Content
    rgh xtaf repair --image .\hdd.img --partition Content
    rgh xtaf metadata backup --image .\hdd.img --out .\xtaf-meta
    rgh xtaf metadata restore --manifest .\xtaf-meta\fatman-metadata.json --image .\hdd.img --dry-run
    rgh xtaf patch plan --from .\xtaf-meta-a --to .\xtaf-meta-b --out .\xtaf-patch.json
    rgh xtaf patch apply --patch .\xtaf-patch.json --image .\hdd.img --dry-run
    rgh xtaf extract --image .\hdd.img --partition Content --path /Content --out .\Content
    rgh xtaf dump --image .\hdd.img --offset 0xB6600000 --length 0x10000000 --out .\partition-dump
    rgh xtaf dump --image .\hdd.img --out .\xtaf-dump

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    devices       List host storage devices visible to XTAF
    disks         List Windows physical disks that XTAF can open directly
    partitions    Detect partitions inside a raw Xbox 360 HDD image or physical disk
    scan          Scan an image or physical disk for plausible FATX/XTAF volume headers
    info          Inspect one detected FATX partition
    list          List entries inside a FATX partition
    find          Search a FATX partition for matching paths
    get           Extract a single file from a FATX partition
    cat           Print a text file from a FATX partition
    mkdir         Create a directory in an image or writable physical disk
    put           Write a host file to an image or writable physical disk
    mv            Move or rename a FATX entry
    rm            Remove a FATX entry from an image or writable physical disk
    format        Format one or more FATX partitions inside an image or physical disk
    check         Scan a FATX partition for orphaned and invalid chain-map state
    repair        Repair safe FATX chain-map issues such as orphaned allocations
    extract       Extract a directory tree from a FATX partition
    dump          Dump one or more raw FATX partitions to host files
    patch         Plan and apply FATX metadata patches between metadata backup directories
    metadata      Back up or restore low-level FATX and partition metadata regions
```

#### `rgh xtaf devices`

```text
DESCRIPTION:
List host storage devices visible to XTAF

USAGE:
    rgh xtaf devices [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Emit machine-readable JSON output
```

#### `rgh xtaf disks`

```text
DESCRIPTION:
List Windows physical disks that XTAF can open directly

USAGE:
    rgh xtaf disks [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Emit machine-readable JSON output
```

#### `rgh xtaf partitions`

```text
DESCRIPTION:
Detect partitions inside a raw Xbox 360 HDD image or physical disk

USAGE:
    rgh xtaf partitions [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --json                  Emit machine-readable JSON output
        --image <FILE>          Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>    Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>        Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>        Limit the manually opened partition to a byte length (decimal or 0x hex)
```

#### `rgh xtaf scan`

```text
DESCRIPTION:
Scan an image or physical disk for plausible FATX/XTAF volume headers

USAGE:
    rgh xtaf scan [OPTIONS]

OPTIONS:
                                DEFAULT
    -h, --help                             Prints help information
        --json                             Emit machine-readable JSON output
        --image <FILE>                     Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>               Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>                   Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>                   Limit the manually opened partition to a byte length (decimal or 0x hex)
        --limit <COUNT>         32         Maximum number of plausible FATX/XTAF header candidates to return
```

#### `rgh xtaf info`

```text
DESCRIPTION:
Inspect one detected FATX partition

USAGE:
    rgh xtaf info [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
```

#### `rgh xtaf list`

```text
DESCRIPTION:
List entries inside a FATX partition

USAGE:
    rgh xtaf list [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
```

#### `rgh xtaf find`

```text
DESCRIPTION:
Search a FATX partition for matching paths

USAGE:
    rgh xtaf find [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
        --query <TEXT>              Case-insensitive text to match in entry names or full FATX paths
```

#### `rgh xtaf get`

```text
DESCRIPTION:
Extract a single file from a FATX partition

USAGE:
    rgh xtaf get [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
        --out <PATH>                Host output path
```

#### `rgh xtaf cat`

```text
DESCRIPTION:
Print a text file from a FATX partition

USAGE:
    rgh xtaf cat [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
```

#### `rgh xtaf mkdir`

```text
DESCRIPTION:
Create a directory in an image or writable physical disk

USAGE:
    rgh xtaf mkdir [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
```

#### `rgh xtaf put`

```text
DESCRIPTION:
Write a host file to an image or writable physical disk

USAGE:
    rgh xtaf put [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
        --out <PATH>                Host output path
        --in <FILE>                 Host file to write into the selected FATX source
        --overwrite                 Replace an existing FATX file at the target path
```

#### `rgh xtaf mv`

```text
DESCRIPTION:
Move or rename a FATX entry

USAGE:
    rgh xtaf mv [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
        --to <FATXPATH>             Destination FATX path
        --overwrite                 Replace an existing destination file if it already exists
```

#### `rgh xtaf rm`

```text
DESCRIPTION:
Remove a FATX entry from an image or writable physical disk

USAGE:
    rgh xtaf rm [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
        --recursive                 Delete directory contents recursively
```

#### `rgh xtaf format`

```text
DESCRIPTION:
Format one or more FATX partitions inside an image or physical disk

USAGE:
    rgh xtaf format [OPTIONS]

OPTIONS:
    -h, --help                           Prints help information
        --json                           Emit machine-readable JSON output
        --image <FILE>                   Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>             Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>                 Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>                 Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>         Partition name or zero-based partition index
        --label <TEXT>                   Volume label to apply when formatting a single selected partition
        --full-zero                      Zero the full target partition instead of performing a quick FATX initialization
        --sectors-per-cluster <COUNT>    Override the FATX sectors-per-cluster value (must be a power of two)
        --auto-confirm                   Skip the destructive-action confirmation prompt
        --dry-run                        Show the partitions and format options that would be used without writing anything
```

#### `rgh xtaf check`

```text
DESCRIPTION:
Scan a FATX partition for orphaned and invalid chain-map state

USAGE:
    rgh xtaf check [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
```

#### `rgh xtaf repair`

```text
DESCRIPTION:
Repair safe FATX chain-map issues such as orphaned allocations

USAGE:
    rgh xtaf repair [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --auto-confirm              Skip the destructive-action confirmation prompt when applying repairs
```

#### `rgh xtaf extract`

```text
DESCRIPTION:
Extract a directory tree from a FATX partition

USAGE:
    rgh xtaf extract [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --path <FATXPATH>           Path inside the selected FATX partition
        --out <PATH>                Host output path
```

#### `rgh xtaf dump`

```text
DESCRIPTION:
Dump one or more raw FATX partitions to host files

USAGE:
    rgh xtaf dump [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --json                      Emit machine-readable JSON output
        --image <FILE>              Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>        Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>            Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>            Limit the manually opened partition to a byte length (decimal or 0x hex)
        --partition <NAME|INDEX>    Partition name or zero-based partition index
        --out <PATH>                Directory to receive dumped partition images
```

#### `rgh xtaf patch`

```text
DESCRIPTION:
Plan and apply FATX metadata patches between metadata backup directories

USAGE:
    rgh xtaf patch [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    plan     Compare two metadata backup directories and write a patch manifest
    apply    Apply a metadata patch manifest to an image or physical disk
```

##### `rgh xtaf patch plan`

```text
DESCRIPTION:
Compare two metadata backup directories and write a patch manifest

USAGE:
    rgh xtaf patch plan [OPTIONS]

OPTIONS:
    -h, --help          Prints help information
        --json          Emit machine-readable JSON output
        --from <DIR>    Source metadata backup directory to use as the baseline
        --to <DIR>      Target metadata backup directory to use as the patch source
        --out <FILE>    Write the patch manifest to this file and store payload files beside it
```

##### `rgh xtaf patch apply`

```text
DESCRIPTION:
Apply a metadata patch manifest to an image or physical disk

USAGE:
    rgh xtaf patch apply [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --json                  Emit machine-readable JSON output
        --patch <FILE>          Patch manifest produced by fatman patch plan
        --image <FILE>          Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>    Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --dry-run               Show the patch plan without writing to the destination
        --auto-confirm          Skip the destructive-action confirmation prompt
```

#### `rgh xtaf metadata`

```text
DESCRIPTION:
Back up or restore low-level FATX and partition metadata regions

USAGE:
    rgh xtaf metadata [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    backup     Back up disk-prefix and partition-header metadata regions
    restore    Restore metadata regions from a prior XTAF metadata backup
```

##### `rgh xtaf metadata backup`

```text
DESCRIPTION:
Back up disk-prefix and partition-header metadata regions

USAGE:
    rgh xtaf metadata backup [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --json                  Emit machine-readable JSON output
        --image <FILE>          Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>    Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>        Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>        Limit the manually opened partition to a byte length (decimal or 0x hex)
        --out <DIR>             Directory to receive metadata backup files and the manifest
```

##### `rgh xtaf metadata restore`

```text
DESCRIPTION:
Restore metadata regions from a prior XTAF metadata backup

USAGE:
    rgh xtaf metadata restore [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --json                  Emit machine-readable JSON output
        --image <FILE>          Path to a raw Xbox 360 HDD image (.img / .bin)
        --disk <NUMBER|PATH>    Windows physical disk number or device path (for example 2 or \\.\PhysicalDrive2)
        --offset <VALUE>        Open a partition manually at a byte offset (decimal or 0x hex)
        --length <VALUE>        Limit the manually opened partition to a byte length (decimal or 0x hex)
        --manifest <FILE>       Manifest produced by fatman metadata backup
        --auto-confirm          Skip the destructive-action confirmation prompt
        --dry-run               Show the restore plan without opening the destination for write access or applying changes
```

### `rgh xtaf-cli`

```text
DESCRIPTION:
Optional SaveEditors Xtaf-CLI wrapper for mount, recovery, trim, and volume workflows

USAGE:
    rgh xtaf-cli [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xtaf-cli status
    rgh xtaf-cli config --path .\tools\Xtaf-CLI
    rgh xtaf-cli install
    rgh xtaf-cli run --arg --help

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    config     Store or display the configured Xtaf-CLI path
    status     Show whether Xtaf-CLI is configured and ready
    install    Download and install pinned SaveEditors Xtaf-CLI 1.0.0
    run        Run the configured Xtaf-CLI executable with raw pass-through arguments
```

#### `rgh xtaf-cli config`

```text
DESCRIPTION:
Store or display the configured Xtaf-CLI path

USAGE:
    rgh xtaf-cli config [OPTIONS]

OPTIONS:
    -h, --help          Prints help information
        --path <DIR>    Xtaf-CLI install directory or executable path override
        --clear         Clear the stored Xtaf-CLI path
```

#### `rgh xtaf-cli status`

```text
DESCRIPTION:
Show whether Xtaf-CLI is configured and ready

USAGE:
    rgh xtaf-cli status [OPTIONS]

OPTIONS:
    -h, --help             Prints help information
        --path <DIR>       Xtaf-CLI install directory or executable path override
        --json             Emit machine-readable output
        --require-mount    Require WinFsp mount support in addition to the Xtaf-CLI executable
```

#### `rgh xtaf-cli install`

```text
DESCRIPTION:
Download and install pinned SaveEditors Xtaf-CLI 1.0.0

USAGE:
    rgh xtaf-cli install [OPTIONS]

OPTIONS:
    -h, --help              Prints help information
        --path <DIR>        Install directory override
        --archive <FILE>    Use a local Xtaf-CLI archive. Requires --sha256
        --url <URL>         Download Xtaf-CLI from an explicit URL. Requires --sha256
        --sha256 <HASH>     Required SHA256 for a custom --archive or --url source
```

#### `rgh xtaf-cli run`

```text
DESCRIPTION:
Run the configured Xtaf-CLI executable with raw pass-through arguments

USAGE:
    rgh xtaf-cli run [OPTIONS]

OPTIONS:
    -h, --help          Prints help information
        --path <DIR>    Xtaf-CLI install directory or executable path override
        --arg <ARG>     Argument to pass through to the Xtaf-CLI executable. Repeat for each raw argument
```

### `rgh xbdm`

```text
DESCRIPTION:
XBDM commands

USAGE:
    rgh xbdm [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xbdm mem diff --left .\left.json --right .\right.json
    rgh xbdm mem dump-diff --left .\left.bin --right .\right.bin

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    info          Show XBDM console info
    raw           Send a raw XBDM command
    screenshot    Capture a screenshot via XBDM
    modules       Manage loaded modules through XBDM and kernel RPC
    mem           Inspect, search, and modify live memory
    xex           Dump, launch, and analyze XEX images
    fs            Browse and transfer files over XBDM
    threads       Inspect and control threads
    debug         Break, resume, and watch live debug state
```

#### `rgh xbdm info`

```text
DESCRIPTION:
Show XBDM console info

USAGE:
    rgh xbdm info [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

#### `rgh xbdm raw`

```text
DESCRIPTION:
Send a raw XBDM command

USAGE:
    rgh xbdm raw [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --cmd <COMMAND>
```

#### `rgh xbdm screenshot`

```text
DESCRIPTION:
Capture a screenshot via XBDM

USAGE:
    rgh xbdm screenshot [OPTIONS]

OPTIONS:
    -h, --help                        Prints help information
        --ip <IP>                     Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>       Use a saved target profile by name
        --port <PORT>                 TCP port (default: 730)
        --timeout <MS>                XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                        Emit JSON output
        --no-reconnect                Disable automatic XBDM reconnect attempts
        --out <FILE>                  Output file path (.bmp, .png, or .raw)
        --format <FORMAT>             Output format: bmp, png, or raw
        --raw                         Force raw output (same as --format raw)
        --force                       Overwrite the output file if it exists
        --decode <MODE>               Force decode mode: auto, linear, tiled-v1, tiled-v2, tiled-v3, tiled-xenia
        --endianness <MODE>           Force endianness: auto, none, swap8-16, swap8-32, swap16-32
        --order <ORDER>               Force 32bpp channel order: auto, bgra, rgba, argb, abgr, bgrx, rgbx, xrgb, xbgr
        --dump-variants               Dump all decode variants to a folder next to the output file
        --crop-right <PX>             Crop N pixels from the right edge (default: 0)
        --crop-right-percent <PCT>    Crop a percentage from the right edge (0-50)
        --xenia-bank-xor <N>          Force Xenia tile bank XOR (0-1) when using tiled-xenia
        --xenia-pipe-xor <N>          Force Xenia tile pipe XOR (0-3) when using tiled-xenia
```

#### `rgh xbdm modules`

```text
DESCRIPTION:
Manage loaded modules through XBDM and kernel RPC

USAGE:
    rgh xbdm modules [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List loaded modules
    info       Show module details
    address    Translate module RVAs or Ghidra addresses to live runtime addresses
    dump       Dump a module from memory
    load       Load a module via kernel RPC
    unload     Unload a module via kernel RPC
    pending    Verify a pending module operation after reboot
```

##### `rgh xbdm modules list`

```text
DESCRIPTION:
List loaded modules

USAGE:
    rgh xbdm modules list [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --sections                 Include section details
```

##### `rgh xbdm modules info`

```text
DESCRIPTION:
Show module details

USAGE:
    rgh xbdm modules info [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --name <MODULE>            Module name, e.g. default.xex or xam.xex
        --sections                 Include section details
```

##### `rgh xbdm modules address`

```text
DESCRIPTION:
Translate module RVAs or Ghidra addresses to live runtime addresses

USAGE:
    rgh xbdm modules address [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --name <MODULE>            Module name, e.g. default.xex or xam.xex
        --rva <ADDR>               Relative virtual address to translate from this module base
        --ghidra <ADDR>            Address copied from Ghidra
        --ghidra-base <ADDR>       Image base shown by Ghidra or the XEX loader
        --sections                 Include section details
```

##### `rgh xbdm modules dump`

```text
DESCRIPTION:
Dump a module from memory

USAGE:
    rgh xbdm modules dump [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --name <MODULE>            Module name, e.g. default.xex or xam.xex
        --out <FILE>               Output file path
        --all                      Dump all modules to a directory
        --dir <DIR>                Output directory for --all
        --session-delay <MS>       Delay between isolated module transfer sessions (default: 1500 ms; range: 250-10000)
        --readable-only            With --all, skip modules whose full memory span is not reported readable by XBDM
```

##### `rgh xbdm modules load`

```text
DESCRIPTION:
Load a module via kernel RPC

USAGE:
    rgh xbdm modules load [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <PATH>              Remote XEX path to load, e.g. Hdd:\XDRPC.xex
        --flags <N>                Kernel load flags (default 8)
        --system                   Run the load RPC on a system thread instead of the default title thread
        --reboot-expected          Treat a console disconnect/reboot as an expected part of the load and persist pending verification
        --dry-run                  Show the resolved call without writing anything
        --notify                   Send a default success notification to the console
        --notify-icon <NAME>       Notification icon preset name
        --notify-logo <ID>         Notification logo id (decimal or 0x hex)
```

##### `rgh xbdm modules unload`

```text
DESCRIPTION:
Unload a module via kernel RPC

USAGE:
    rgh xbdm modules unload [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --name <MODULE>            Loaded module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --handle <HANDLE>          Explicit module handle as hex or decimal
        --skip-mark                Do not set the sysdll unload marker at handle+0x40 before unloading
        --dry-run                  Show the resolved unload target without writing anything
        --force                    Required. Module unload can wedge the console if the target rejects live unload
        --allow-critical           Also allow unloading a known system or boot-plugin module. Requires --force
        --notify                   Send a default success notification to the console
        --notify-icon <NAME>       Notification icon preset name
        --notify-logo <ID>         Notification logo id (decimal or 0x hex)
```

##### `rgh xbdm modules pending`

```text
DESCRIPTION:
Verify a pending module operation after reboot

USAGE:
    rgh xbdm modules pending [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

#### `rgh xbdm mem`

```text
DESCRIPTION:
Inspect, search, and modify live memory

USAGE:
    rgh xbdm mem [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xbdm mem diff --left .\left.json --right .\right.json
    rgh xbdm mem dump-diff --left .\left.bin --right .\right.bin
    rgh xbdm mem session list

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    dump         Dump a memory range to a file
    hexdump      Hex dump a memory range
    regions      List live memory regions
    map          Render the live memory map
    diff         Compare two saved memory map JSON captures
    dump-diff    Compare two saved memory dump files
    session      Manage offline memory capture sessions
    peek         Read a value from memory
    poke         Write a value to memory
    watch        Stream memory changes
    strings      Extract strings from memory
    find         Search memory for bytes, ASCII, or a typed value
```

##### `rgh xbdm mem dump`

```text
DESCRIPTION:
Dump a memory range to a file

USAGE:
    rgh xbdm mem dump [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>              Absolute console address to dump
        --module <NAME>            Loaded module name used with --rva or --ghidra. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <RVA>                Module-relative virtual address
        --ghidra <ADDR>            Address copied from Ghidra for the selected module
        --ghidra-base <ADDR>       Image base configured in Ghidra
        --size <SIZE>
        --out <FILE>
```

##### `rgh xbdm mem hexdump`

```text
DESCRIPTION:
Hex dump a memory range

USAGE:
    rgh xbdm mem hexdump [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>              Absolute console address to read
        --module <NAME>            Loaded module name used with --rva or --ghidra. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <RVA>                Module-relative virtual address
        --ghidra <ADDR>            Address copied from Ghidra for the selected module
        --ghidra-base <ADDR>       Image base configured in Ghidra
        --size <SIZE>
```

##### `rgh xbdm mem regions`

```text
DESCRIPTION:
List live memory regions

USAGE:
    rgh xbdm mem regions [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

##### `rgh xbdm mem map`

```text
DESCRIPTION:
Render the live memory map

USAGE:
    rgh xbdm mem map [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

##### `rgh xbdm mem diff`

```text
DESCRIPTION:
Compare two saved memory map JSON captures

USAGE:
    rgh xbdm mem diff [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --left <FILE>     Left memory map JSON capture produced by `rgh mem map --json`
        --right <FILE>    Right memory map JSON capture produced by `rgh mem map --json`
        --json            Output JSON
```

##### `rgh xbdm mem dump-diff`

```text
DESCRIPTION:
Compare two saved memory dump files

USAGE:
    rgh xbdm mem dump-diff [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --left <FILE>     Left memory dump file to compare
        --right <FILE>    Right memory dump file to compare
        --json            Output JSON
```

##### `rgh xbdm mem session`

```text
DESCRIPTION:
Manage offline memory capture sessions

USAGE:
    rgh xbdm mem session [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xbdm mem session create --name lab --capture map:baseline=.\left.json
    rgh xbdm mem session show --name lab

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    create    Create a named memory session manifest
    list      List saved memory sessions
    show      Show a saved memory session manifest
    remove    Remove a saved memory session manifest
```

###### `rgh xbdm mem session create`

```text
DESCRIPTION:
Create a named memory session manifest

USAGE:
    rgh xbdm mem session create [OPTIONS]

OPTIONS:
    -h, --help              Prints help information
        --name <NAME>       Session name
        --note <TEXT>       Optional note for the session
        --capture <SPEC>    Capture spec in the form <label>=<path> or <kind>:<label>=<path>. Repeat for each stored capture
        --json              Output JSON
```

###### `rgh xbdm mem session list`

```text
DESCRIPTION:
List saved memory sessions

USAGE:
    rgh xbdm mem session list [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

###### `rgh xbdm mem session show`

```text
DESCRIPTION:
Show a saved memory session manifest

USAGE:
    rgh xbdm mem session show [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --name <NAME>    Session name
        --json           Output JSON
```

###### `rgh xbdm mem session remove`

```text
DESCRIPTION:
Remove a saved memory session manifest

USAGE:
    rgh xbdm mem session remove [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --name <NAME>    Session name
        --json           Output JSON
```

##### `rgh xbdm mem peek`

```text
DESCRIPTION:
Read a value from memory

USAGE:
    rgh xbdm mem peek [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>
        --module <MODULE>          Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               RVA relative to --module
        --ghidra <ADDR>            Ghidra address to translate through --module and --ghidra-base
        --ghidra-base <ADDR>       Image base used by Ghidra when passing --ghidra
        --type <TYPE>              u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii plus byte/int/float/string aliases
        --len <N>                  Length for ascii reads (default 32)
        --le                       Interpret as little-endian (default is big-endian)
```

##### `rgh xbdm mem poke`

```text
DESCRIPTION:
Write a value to memory

USAGE:
    rgh xbdm mem poke [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>
        --module <MODULE>          Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               RVA relative to --module
        --ghidra <ADDR>            Ghidra address to translate through --module and --ghidra-base
        --ghidra-base <ADDR>       Image base used by Ghidra when passing --ghidra
        --type <TYPE>              u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii|hex plus byte/int/float/string/bytes aliases
        --value <VALUE>
        --le                       Write little-endian (default is big-endian)
```

##### `rgh xbdm mem watch`

```text
DESCRIPTION:
Stream memory changes

USAGE:
    rgh xbdm mem watch [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>
        --module <MODULE>          Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               RVA relative to --module
        --ghidra <ADDR>            Ghidra address to translate through --module and --ghidra-base
        --ghidra-base <ADDR>       Image base used by Ghidra when passing --ghidra
        --size <SIZE>
        --interval <MS>            Poll interval in milliseconds (default 500)
        --count <N>                Number of iterations (default 0 = infinite)
        --clear                    Clear the screen between updates
```

##### `rgh xbdm mem strings`

```text
DESCRIPTION:
Extract strings from memory

USAGE:
    rgh xbdm mem strings [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>
        --module <MODULE>          Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               RVA relative to --module
        --ghidra <ADDR>            Ghidra address to translate through --module and --ghidra-base
        --ghidra-base <ADDR>       Image base used by Ghidra when passing --ghidra
        --size <SIZE>
        --min <N>                  Minimum string length (default 4)
        --max <N>                  Maximum strings to return (default 200)
        --out <FILE>               Write output to a file (.json for JSON, otherwise text)
```

##### `rgh xbdm mem find`

```text
DESCRIPTION:
Search memory for bytes, ASCII, or a typed value

USAGE:
    rgh xbdm mem find [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>
        --module <MODULE>          Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               RVA relative to --module
        --ghidra <ADDR>            Ghidra address to translate through --module and --ghidra-base
        --ghidra-base <ADDR>       Image base used by Ghidra when passing --ghidra
        --size <SIZE>
        --chunk <SIZE>             Read chunk size (hex or dec, default: 0x4000)
        --pattern <HEX>            Hex pattern, e.g. DEADBEEF or 0xDE AD BE EF
        --ascii <TEXT>             ASCII text pattern
        --type <TYPE>              Typed value search: u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|int64|ascii|string|utf16|hex plus aliases
        --value <VALUE>            Value for typed search. Used with --type
        --little-endian            Build typed search and freeze values as little-endian (default is big-endian)
        --max <N>                  Maximum matches to return (default 20)
        --out <FILE>               Write the hit list to a file (.json for JSON, otherwise text)
        --freeze                   Continuously rewrite a value to the selected hit(s). Requires --freeze-type and --freeze-value
        --freeze-type <TYPE>       Value type for freeze writes: int|uint|float|string|bytes|u32|f32|ascii|hex, etc
        --freeze-value <VALUE>     Value to freeze to the selected hit(s)
        --freeze-interval <MS>     Freeze write interval in milliseconds (default 250)
        --freeze-count <N>         Number of freeze write passes (default 0 = until Ctrl+C)
        --freeze-all               Freeze all hits instead of just one selected hit
        --hit <N>                  1-based hit index to freeze when --freeze-all is not used (default 1)
```

#### `rgh xbdm xex`

```text
DESCRIPTION:
Dump, launch, and analyze XEX images

USAGE:
    rgh xbdm xex [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xex info --file .\default.xex
    rgh xex map --file .\default.xex --ghidra-base 0x82000000 --ghidra 0x82001234

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    dump             Dump the active XEX image
    launch           Launch a XEX with optional arguments
    strings          Extract strings from a local binary/XEX, FTP path, or running title
    info             Inspect a local XEX header
    map              Map a Ghidra address to a local XEX address
    decompile        Decompile a XEX to C via Ghidra
    ida-decompile    Decompile a XEX to C via IDA Pro
```

##### `rgh xbdm xex dump`

```text
DESCRIPTION:
Dump the active XEX image

USAGE:
    rgh xbdm xex dump [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <XEX>               Explicit XEX path. If omitted, uses the running title
        --out <FILE>               Output file path
```

##### `rgh xbdm xex launch`

```text
DESCRIPTION:
Launch a XEX with optional arguments

USAGE:
    rgh xbdm xex launch [XEX] [OPTIONS]

ARGUMENTS:
    [XEX]    XEX path to launch, for example Hdd1:\Aurora\Aurora.xex

OPTIONS:
                                   DEFAULT
    -h, --help                                Prints help information
        --ip <IP>                             Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>               Use a saved target profile by name
        --port <PORT>                         TCP port (default: 730)
        --timeout <MS>                        XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                                Emit JSON output
        --no-reconnect                        Disable automatic XBDM reconnect attempts
        --xex <PATH>                          XEX path to launch, for example Hdd1:\Aurora\Aurora.xex
        --directory <DIR>                     Working directory passed to XBDM. Defaults to the XEX folder
        --args <TEXT>                         Command-line arguments passed to the XEX
        --titleid <TITLEID>                   Optional Title ID for display/logging
        --dry-run                             Show the generated XBDM command without executing it
        --verify                              Require remote path verification before sending the launch request
        --no-verify                           Skip remote path verification for a known recovery path
        --wait                                Stop execution and require the requested active XEX plus observable transition evidence
        --wait-timeout <SEC>       45         Maximum seconds to wait for a verified launch transition (default: 45)
        --notify                              Send a default success notification to the console
        --notify-icon <NAME>                  Notification icon preset name
        --notify-logo <ID>                    Notification logo id (decimal or 0x hex)
```

##### `rgh xbdm xex strings`

```text
DESCRIPTION:
Extract strings from a local binary/XEX, FTP path, or running title

USAGE:
    rgh xbdm xex strings [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --file <FILE>        Input file to scan
        --in <FILE>          Legacy alias for --file
        --ftp-path <PATH>    Fetch the XEX via FTP before scanning (e.g. /Hdd1/Aurora/Aurora.xex)
        --running            Use the running title XEX (resolved via XBDM + FTP)
        --min-length <N>     Minimum string length (default: 4)
        --min <N>            Legacy alias for --min-length
        --limit <N>          Maximum strings to return (default: 200)
        --max <N>            Legacy alias for --limit
        --unicode            Include UTF-16 (LE/BE) strings
        --out <FILE>         Write output to a text file
        --json               Output JSON
```

##### `rgh xbdm xex info`

```text
DESCRIPTION:
Inspect a local XEX header

USAGE:
    rgh xbdm xex info [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --file <FILE>    Input XEX file to inspect
        --json           Output JSON
        --out <FILE>     Write JSON output to a file
```

##### `rgh xbdm xex map`

```text
DESCRIPTION:
Map a Ghidra address to a local XEX address

USAGE:
    rgh xbdm xex map [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --file <FILE>           Input XEX file to map against
        --ghidra-base <ADDR>    Base address used when importing in Ghidra
        --ghidra <ADDR>         Address copied from Ghidra
        --json                  Output JSON
```

##### `rgh xbdm xex decompile`

```text
DESCRIPTION:
Decompile a XEX to C via Ghidra

USAGE:
    rgh xbdm xex decompile [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --in <FILE>             Input file to analyze (XEX)
        --ftp-path <PATH>       Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex)
        --running               Use the running title XEX (resolved via XBDM + FTP)
        --out <DIR>             Output directory for decompiled C files
        --max <N>               Maximum number of functions to decompile (default: all)
        --func-timeout <SEC>    Decompile timeout per function (seconds, default: 5000)
        --project <NAME>        Project name (default: <file>_headless)
        --projects <DIR>        Project root directory override
        --path <DIR>            Ghidra install directory override
        --java <DIR>            JAVA_HOME override
        --loader <NAME>         Explicit loader name
        --timeout <SEC>         Analysis timeout per file (seconds, default: 5000)
        --delete-project        Delete a project created by this run after analysis completes
        --overwrite             Overwrite existing file in project
        --script-path <DIR>     Override script path (default: ghidra_scripts next to rgh.exe)
```

##### `rgh xbdm xex ida-decompile`

```text
DESCRIPTION:
Decompile a XEX to C via IDA Pro

USAGE:
    rgh xbdm xex ida-decompile [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --in <FILE>          Input XEX or database path
        --ftp-path <PATH>    Fetch the XEX via FTP before decompiling
        --running            Use the running title XEX resolved over XBDM + FTP
        --out <DIR>          Output directory for C files
        --max <N>            Maximum number of functions to decompile (default: all)
        --backend <NAME>     Backend: auto, batch, or idalib
        --out-db <FILE>      Database path to create or reuse for raw XEX input
        --overwrite-db       Overwrite the database when importing a raw XEX
        --keep-db            Keep the generated database even when using a temporary cache path
        --path <DIR>         IDA install directory override
        --python <EXE>       Python executable override
        --user <DIR>         IDA user directory override
```

#### `rgh xbdm fs`

```text
DESCRIPTION:
Browse and transfer files over XBDM

USAGE:
    rgh xbdm fs [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list     List files in a directory
    get      Download a file from the console
    put      Upload a file to the console
    cat      Print a file from the console
    rm       Delete a file on the console
    mkdir    Create a directory on the console
    mv       Move or rename a file on the console
```

##### `rgh xbdm fs list`

```text
DESCRIPTION:
List files in a directory

USAGE:
    rgh xbdm fs list [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <DIR>
```

##### `rgh xbdm fs get`

```text
DESCRIPTION:
Download a file from the console

USAGE:
    rgh xbdm fs get [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <FILE>
        --out <FILE>
```

##### `rgh xbdm fs put`

```text
DESCRIPTION:
Upload a file to the console

USAGE:
    rgh xbdm fs put [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <FILE>
        --in <FILE>
```

##### `rgh xbdm fs cat`

```text
DESCRIPTION:
Print a file from the console

USAGE:
    rgh xbdm fs cat [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <FILE>
        --max <BYTES>              Maximum bytes to display (default 65536)
        --hex                      Render as hex instead of text
        --encoding <ENC>           Text encoding: utf8|ascii (default utf8)
```

##### `rgh xbdm fs rm`

```text
DESCRIPTION:
Delete a file on the console

USAGE:
    rgh xbdm fs rm [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <PATH>
```

##### `rgh xbdm fs mkdir`

```text
DESCRIPTION:
Create a directory on the console

USAGE:
    rgh xbdm fs mkdir [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --path <PATH>
```

##### `rgh xbdm fs mv`

```text
DESCRIPTION:
Move or rename a file on the console

USAGE:
    rgh xbdm fs mv [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --from <PATH>
        --to <PATH>
```

#### `rgh xbdm threads`

```text
DESCRIPTION:
Inspect and control threads

USAGE:
    rgh xbdm threads [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List threads
    context    Read thread registers
    suspend    Suspend a thread
    resume     Resume a thread
```

##### `rgh xbdm threads list`

```text
DESCRIPTION:
List threads

USAGE:
    rgh xbdm threads list [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --no-names                 Compatibility flag. Ignored by the current thread list output; thread list now uses start-address metadata instead of thread names
```

##### `rgh xbdm threads context`

```text
DESCRIPTION:
Read thread registers

USAGE:
    rgh xbdm threads context [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --id <ID>                  Thread ID in hex or decimal
```

##### `rgh xbdm threads suspend`

```text
DESCRIPTION:
Suspend a thread

USAGE:
    rgh xbdm threads suspend [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --id <ID>                  Thread ID in hex or decimal
```

##### `rgh xbdm threads resume`

```text
DESCRIPTION:
Resume a thread

USAGE:
    rgh xbdm threads resume [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --id <ID>                  Thread ID in hex or decimal
```

#### `rgh xbdm debug`

```text
DESCRIPTION:
Break, resume, and watch live debug state

USAGE:
    rgh xbdm debug [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xbdm debug address-check --module default.xex --rva 0x1234
    rgh xbdm debug break add --module default.xex --rva 0x1234
    rgh xbdm debug databreak add --module default.xex --ghidra 0x82001234 --ghidra-base 0x82000000

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    address-check    Resolve and verify a live debug address
    stop             Break execution
    go               Resume execution
    watch            Stream live XBDM debug notifications
    break            Code breakpoints
    databreak        Data breakpoints
```

##### `rgh xbdm debug address-check`

```text
DESCRIPTION:
Resolve and verify a live debug address

USAGE:
    rgh xbdm debug address-check [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>              Live console address. Use this when you already have the XBDM address
        --module <MODULE>          Live console module name. Exact full module path is safest; unique leaf or suffix names are accepted when unambiguous
        --rva <ADDR>               Relative virtual address inside the live console module
        --ghidra <ADDR>            Ghidra-translated address for the selected module
        --ghidra-base <ADDR>       Ghidra image base used to translate addresses back to the live console
        --sections                 Include section rows for the resolved live console module
```

##### `rgh xbdm debug stop`

```text
DESCRIPTION:
Break execution

USAGE:
    rgh xbdm debug stop [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

##### `rgh xbdm debug go`

```text
DESCRIPTION:
Resume execution

USAGE:
    rgh xbdm debug go [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

##### `rgh xbdm debug watch`

```text
DESCRIPTION:
Stream live XBDM debug notifications

USAGE:
    rgh xbdm debug watch [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --duration <SEC>           Total watch runtime in seconds (default: 30)
        --max <N>                  Maximum notifications to print before exiting
        --raw                      Print raw notify lines instead of parsed summaries
        --stopon-fce               Ask XBDM to stop on first-chance exceptions
        --name <NAME>              Debugger session name (default: XeCLI)
        --out <FILE>               Write debug events to a JSONL log file
        --context                  Capture thread register context for break, databreak, and exception events when thread id is present
        --require-event            Fail if the watch window ends without receiving at least one notification
```

##### `rgh xbdm debug break`

```text
DESCRIPTION:
Code breakpoints

USAGE:
    rgh xbdm debug break [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    add         Add a code breakpoint
    remove      Remove a code breakpoint
    clearall    Clear all code and data breakpoints
```

###### `rgh xbdm debug break add`

```text
DESCRIPTION:
Add a code breakpoint

USAGE:
    rgh xbdm debug break add [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>              Live runtime address. Use this when you already have the XBDM address
        --module <MODULE>          Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               Relative virtual address inside --module
        --ghidra <ADDR>            Address copied from Ghidra
        --ghidra-base <ADDR>       Image base shown by Ghidra or the XEX loader
```

###### `rgh xbdm debug break remove`

```text
DESCRIPTION:
Remove a code breakpoint

USAGE:
    rgh xbdm debug break remove [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>              Live runtime address. Use this when you already have the XBDM address
        --module <MODULE>          Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               Relative virtual address inside --module
        --ghidra <ADDR>            Address copied from Ghidra
        --ghidra-base <ADDR>       Image base shown by Ghidra or the XEX loader
```

###### `rgh xbdm debug break clearall`

```text
DESCRIPTION:
Clear all code and data breakpoints

USAGE:
    rgh xbdm debug break clearall [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

##### `rgh xbdm debug databreak`

```text
DESCRIPTION:
Data breakpoints

USAGE:
    rgh xbdm debug databreak [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    add       Add a data breakpoint
    remove    Remove a data breakpoint
```

###### `rgh xbdm debug databreak add`

```text
DESCRIPTION:
Add a data breakpoint

USAGE:
    rgh xbdm debug databreak add [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>              Live runtime address. Use this when you already have the XBDM address
        --module <MODULE>          Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               Relative virtual address inside --module
        --ghidra <ADDR>            Address copied from Ghidra
        --ghidra-base <ADDR>       Image base shown by Ghidra or the XEX loader
        --size <SIZE>              Size in bytes (default 4)
        --type <TYPE>              write|read|exec|rw (default write)
        --force                    Allow a direct address or a range outside a known compatible module section
```

###### `rgh xbdm debug databreak remove`

```text
DESCRIPTION:
Remove a data breakpoint

USAGE:
    rgh xbdm debug databreak remove [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>              Live runtime address. Use this when you already have the XBDM address
        --module <MODULE>          Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --rva <ADDR>               Relative virtual address inside --module
        --ghidra <ADDR>            Address copied from Ghidra
        --ghidra-base <ADDR>       Image base shown by Ghidra or the XEX loader
        --size <SIZE>              Size in bytes (default 4)
        --type <TYPE>              write|read|exec|rw (default write)
```

### `rgh jrpc2`

```text
DESCRIPTION:
JRPC2 commands

USAGE:
    rgh jrpc2 [OPTIONS] <COMMAND>

EXAMPLES:
    rgh jrpc2 temps
    rgh jrpc2 notify "XeCLI connected" 14

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    cpu-key        Read the CPU key
    temps          Read temperature sensors
    title-id       Read the current Title ID
    dashboard      Read the dashboard version
    motherboard    Read motherboard type
    resolve        Resolve a function by module/ordinal
    notify         Send a notification via JRPC2
    call           Call a function with RPC
```

#### `rgh jrpc2 cpu-key`

```text
DESCRIPTION:
Read the CPU key

USAGE:
    rgh jrpc2 cpu-key [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

#### `rgh jrpc2 temps`

```text
DESCRIPTION:
Read temperature sensors

USAGE:
    rgh jrpc2 temps [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --sensor <SENSOR>          cpu|gpu|edram|motherboard
```

#### `rgh jrpc2 title-id`

```text
DESCRIPTION:
Read the current Title ID

USAGE:
    rgh jrpc2 title-id [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

#### `rgh jrpc2 dashboard`

```text
DESCRIPTION:
Read the dashboard version

USAGE:
    rgh jrpc2 dashboard [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

#### `rgh jrpc2 motherboard`

```text
DESCRIPTION:
Read motherboard type

USAGE:
    rgh jrpc2 motherboard [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

#### `rgh jrpc2 resolve`

```text
DESCRIPTION:
Resolve a function by module/ordinal

USAGE:
    rgh jrpc2 resolve [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --module <MODULE>          Module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --ordinal <ORD>
```

#### `rgh jrpc2 notify`

```text
DESCRIPTION:
Send a notification via JRPC2

USAGE:
    rgh jrpc2 notify [message] [logo] [OPTIONS]

ARGUMENTS:
    [message]    Notification text
    [logo]       Optional icon id or built-in icon name

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --logo <ID>                Notification logo id or built-in icon name
        --icon <NAME>              Notification icon preset name from config
        --message <TEXT>           Notification text
        --position <POS>           Notification position (top|bottom|center|left|right|top-left|top-right|bottom-left|bottom-right)
```

#### `rgh jrpc2 call`

```text
DESCRIPTION:
Call a function with RPC

USAGE:
    rgh jrpc2 call [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --addr <ADDR>
        --module <MODULE>          Module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous
        --ordinal <ORD>
        --ret <TYPE>
        --arg <ARG>
        --vm
        --system
```

### `rgh notify`

```text
DESCRIPTION:
Send an on-screen notification

USAGE:
    rgh notify [message] [logo] [OPTIONS]

ARGUMENTS:
    [message]    Notification text
    [logo]       Optional icon id or built-in icon name

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --message <TEXT>           Notification text
        --logo <ID>                Notification logo id or built-in icon name
        --icon <NAME>              Notification icon preset name from config
        --position <POS>           Notification position (top|bottom|center|left|right|top-left|top-right|bottom-left|bottom-right)
```

### `rgh notify-icons`

```text
DESCRIPTION:
Browse XNotify icons and manage preset aliases

USAGE:
    rgh notify-icons [OPTIONS] <COMMAND>

EXAMPLES:
    rgh notify-icons list
    rgh notify-icons show 14
    rgh notify-icons add --name success --logo 14

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list            List built-in XNotify icons and preset aliases
    show <value>    Resolve an icon id, built-in name, or preset alias
    add             Add an icon preset
    remove          Remove an icon preset
```

#### `rgh notify-icons list`

```text
DESCRIPTION:
List built-in XNotify icons and preset aliases

USAGE:
    rgh notify-icons list [OPTIONS]

OPTIONS:
    -h, --help             Prints help information
        --builtins-only    Show only the built-in XNotify icon catalog
        --presets-only     Show only user-defined preset aliases from config
```

#### `rgh notify-icons show`

```text
DESCRIPTION:
Resolve an icon id, built-in name, or preset alias

USAGE:
    rgh notify-icons show <value> [OPTIONS]

ARGUMENTS:
    <value>    A built-in icon name, preset alias, decimal id, or 0x hex id

OPTIONS:
    -h, --help    Prints help information
```

#### `rgh notify-icons add`

```text
DESCRIPTION:
Add an icon preset

USAGE:
    rgh notify-icons add [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --name <NAME>
        --logo <ID>
```

#### `rgh notify-icons remove`

```text
DESCRIPTION:
Remove an icon preset

USAGE:
    rgh notify-icons remove [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --name <NAME>
```

### `rgh smc`

```text
DESCRIPTION:
System Management Controller helpers

USAGE:
    rgh smc [OPTIONS] <COMMAND>

EXAMPLES:
    rgh smc version
    rgh smc temps

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    version    Probe the console SMC version
    temps      Read SMC temperature sensors with fixed-point precision
```

#### `rgh smc version`

```text
DESCRIPTION:
Probe the console SMC version

USAGE:
    rgh smc version [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

#### `rgh smc temps`

```text
DESCRIPTION:
Read SMC temperature sensors with fixed-point precision

USAGE:
    rgh smc temps [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

### `rgh fan`

```text
DESCRIPTION:
Fan speed helpers

USAGE:
    rgh fan [OPTIONS] <COMMAND>

EXAMPLES:
    rgh fan set --speed 55 --channel both
    rgh fan show

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    set     Send a manual fan speed command
    show    Show the last XeCLI-applied manual fan setting
```

#### `rgh fan set`

```text
DESCRIPTION:
Send a manual fan speed command

USAGE:
    rgh fan set [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --speed <PERCENT>          Manual fan speed percentage (10-100)
        --channel <CHANNEL>        primary|secondary|both (default: both)
        --notify                   Send a default success notification to the console
        --notify-icon <NAME>       Notification icon preset name
        --notify-logo <ID>         Notification logo id (decimal or 0x hex)
```

#### `rgh fan show`

```text
DESCRIPTION:
Show the last XeCLI-applied manual fan setting

USAGE:
    rgh fan show [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Emit JSON output
```

### `rgh led`

```text
DESCRIPTION:
Ring-of-light LED helpers

USAGE:
    rgh led [OPTIONS] <COMMAND>

EXAMPLES:
    rgh led set --preset quadrant1
    rgh led set --tl green --tr off --bl off --br off

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    set      Set the ring-of-light LEDs
    state    Show the last XeCLI-applied ring-light state
```

#### `rgh led set`

```text
DESCRIPTION:
Set the ring-of-light LEDs

USAGE:
    rgh led set [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --preset <NAME>            all-green|all-red|all-orange|all-off|quadrant1|quadrant2|quadrant3|quadrant4
        --tl <COLOR>               Top-left color: off|green|red|orange
        --tr <COLOR>               Top-right color: off|green|red|orange
        --bl <COLOR>               Bottom-left color: off|green|red|orange
        --br <COLOR>               Bottom-right color: off|green|red|orange
        --notify                   Send a default success notification to the console
        --notify-icon <NAME>       Notification icon preset name
        --notify-logo <ID>         Notification logo id (decimal or 0x hex)
```

#### `rgh led state`

```text
DESCRIPTION:
Show the last XeCLI-applied ring-light state

USAGE:
    rgh led state [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Emit JSON output
```

### `rgh signin`

```text
DESCRIPTION:
Signed-in user helpers

USAGE:
    rgh signin [OPTIONS] <COMMAND>

EXAMPLES:
    rgh signin state

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    state    Read the active sign-in state, gamertag, and XUID
```

#### `rgh signin state`

```text
DESCRIPTION:
Read the active sign-in state, gamertag, and XUID

USAGE:
    rgh signin state [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
```

### `rgh tray`

```text
DESCRIPTION:
Disc tray helpers

USAGE:
    rgh tray [OPTIONS] <COMMAND>

EXAMPLES:
    rgh tray open
    rgh tray close

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    open     Open the disc tray
    close    Close the disc tray
```

#### `rgh tray open`

```text
DESCRIPTION:
Open the disc tray

USAGE:
    rgh tray open [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --dry-run                  Show the tray action without connecting or writing anything
        --yes                      Skip the tray action confirmation prompt
        --notify                   Send a default success notification to the console
        --notify-icon <NAME>       Notification icon preset name
        --notify-logo <ID>         Notification logo id (decimal or 0x hex)
```

#### `rgh tray close`

```text
DESCRIPTION:
Close the disc tray

USAGE:
    rgh tray close [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --dry-run                  Show the tray action without connecting or writing anything
        --yes                      Skip the tray action confirmation prompt
        --notify                   Send a default success notification to the console
        --notify-icon <NAME>       Notification icon preset name
        --notify-logo <ID>         Notification logo id (decimal or 0x hex)
```

### `rgh xell`

```text
DESCRIPTION:
XeLL Reloaded helpers with guided auto-launch when needed

USAGE:
    rgh xell [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xell boot
    rgh xell boot --quickboot
    rgh xell boot --quickboot --quickboot-target flash
    rgh xell info
    rgh xell kv export
    rgh xell kv export --raw
    rgh xell kv export --single
    rgh xell nand dump
    rgh xell nand dump --single
    rgh xell nand dump --output .\nand_backup.bin

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    boot    Launch XeLL Reloaded, or confirm that XeLL is already running
    info    Inspect the available XeLL HTTP services, endpoints, and detected CPU key
    kv      XeLL keyvault export helpers with optional repeated verification
    nand    XeLL-backed NAND dumping and verification
```

#### `rgh xell boot`

```text
DESCRIPTION:
Launch XeLL Reloaded, or confirm that XeLL is already running

USAGE:
    rgh xell boot [OPTIONS]

OPTIONS:
    -h, --help                       Prints help information
        --ip <IP>                    Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>      Use a saved target profile by name
        --port <PORT>                TCP port (default: 730)
        --timeout <MS>               XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                       Emit JSON output
        --no-reconnect               Disable automatic XBDM reconnect attempts
        --force-xell                 Force the direct XeLL reboot path instead of the XellLaunch shortcut when available
        --yes                        Skip the interactive XeLL launch confirmation prompt
        --launcher <PATH>            Remote XEX path to use as the XeLL launch helper, for example Hdd1:\XellLaunch\default.xex
        --stage-launcher <FILE>      Upload a local XeLL launch helper XEX before booting. Defaults to Hdd1:\XellLaunch\default.xex when --launcher is omitted
        --stage-xell-bin <FILE>      Upload a local XeLL binary as xell.bin beside the launch helper before booting
        --quickboot                  Build, upload, and launch a QuickBoot XeLL launcher, and also install a dashboard shortcut when possible
        --quickboot-target <MODE>    QuickBoot target: launcher (default) or flash
        --allow-usb                  Allow XeLL launch even when removable USB storage is attached. Disabled by default because some consoles hang at Fat mount uda0
```

#### `rgh xell info`

```text
DESCRIPTION:
Inspect the available XeLL HTTP services, endpoints, and detected CPU key

USAGE:
    rgh xell info [OPTIONS]

OPTIONS:
    -h, --help                       Prints help information
        --ip <IP>                    Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>      Use a saved target profile by name
        --port <PORT>                TCP port (default: 730)
        --timeout <MS>               XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                       Emit JSON output
        --no-reconnect               Disable automatic XBDM reconnect attempts
        --force-xell                 Force the direct XeLL reboot path instead of the XellLaunch shortcut when available
        --yes                        Skip the interactive XeLL launch confirmation prompt
        --launcher <PATH>            Remote XEX path to use as the XeLL launch helper, for example Hdd1:\XellLaunch\default.xex
        --stage-launcher <FILE>      Upload a local XeLL launch helper XEX before booting. Defaults to Hdd1:\XellLaunch\default.xex when --launcher is omitted
        --stage-xell-bin <FILE>      Upload a local XeLL binary as xell.bin beside the launch helper before booting
        --quickboot                  Build, upload, and launch a QuickBoot XeLL launcher, and also install a dashboard shortcut when possible
        --quickboot-target <MODE>    QuickBoot target: launcher (default) or flash
        --allow-usb                  Allow XeLL launch even when removable USB storage is attached. Disabled by default because some consoles hang at Fat mount uda0
```

#### `rgh xell kv`

```text
DESCRIPTION:
XeLL keyvault export helpers with optional repeated verification

USAGE:
    rgh xell kv [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xell kv export
    rgh xell kv export --output kv_backup.bin
    rgh xell kv export --raw
    rgh xell kv export --single

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    export    Export the keyvault, CPU key text, and a packaged verified backup set
```

##### `rgh xell kv export`

```text
DESCRIPTION:
Export the keyvault, CPU key text, and a packaged verified backup set

USAGE:
    rgh xell kv export [OPTIONS]

OPTIONS:
    -h, --help                       Prints help information
        --ip <IP>                    Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>      Use a saved target profile by name
        --port <PORT>                TCP port (default: 730)
        --timeout <MS>               XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                       Emit JSON output
        --no-reconnect               Disable automatic XBDM reconnect attempts
        --force-xell                 Force the direct XeLL reboot path instead of the XellLaunch shortcut when available
        --yes                        Skip the interactive XeLL launch confirmation prompt
        --launcher <PATH>            Remote XEX path to use as the XeLL launch helper, for example Hdd1:\XellLaunch\default.xex
        --stage-launcher <FILE>      Upload a local XeLL launch helper XEX before booting. Defaults to Hdd1:\XellLaunch\default.xex when --launcher is omitted
        --stage-xell-bin <FILE>      Upload a local XeLL binary as xell.bin beside the launch helper before booting
        --quickboot                  Build, upload, and launch a QuickBoot XeLL launcher, and also install a dashboard shortcut when possible
        --quickboot-target <MODE>    QuickBoot target: launcher (default) or flash
        --allow-usb                  Allow XeLL launch even when removable USB storage is attached. Disabled by default because some consoles hang at Fat mount uda0
        --overwrite                  Allow existing output files and archive to be replaced
        --output <FILE>              Output keyvault filename. Defaults to kv_yyyyMMdd_HHmmss.bin
        --raw                        Export the raw keyvault block instead of the decrypted KV
        --single                     Take one keyvault export only and skip byte-for-byte verification
        --no-verify                  Skip the repeated keyvault verification loop
        --dry-run                    Preview the export plan without connecting to the console
```

#### `rgh xell nand`

```text
DESCRIPTION:
XeLL-backed NAND dumping and verification

USAGE:
    rgh xell nand [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xell nand dump
    rgh xell nand dump --single
    rgh xell nand dump --quickboot --yes
    rgh xell nand dump --output nand_backup.bin
    rgh xell nand dump --force-xell

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    dump    Boot XeLL Reloaded, download the flash dump, verify repeated dumps, and package a safe backup
```

##### `rgh xell nand dump`

```text
DESCRIPTION:
Boot XeLL Reloaded, download the flash dump, verify repeated dumps, and package a safe backup

USAGE:
    rgh xell nand dump [OPTIONS]

OPTIONS:
    -h, --help                       Prints help information
        --ip <IP>                    Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>      Use a saved target profile by name
        --port <PORT>                TCP port (default: 730)
        --timeout <MS>               XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                       Emit JSON output
        --no-reconnect               Disable automatic XBDM reconnect attempts
        --force-xell                 Force the direct XeLL reboot path instead of the XellLaunch shortcut when available
        --yes                        Skip the interactive XeLL launch confirmation prompt
        --launcher <PATH>            Remote XEX path to use as the XeLL launch helper, for example Hdd1:\XellLaunch\default.xex
        --stage-launcher <FILE>      Upload a local XeLL launch helper XEX before booting. Defaults to Hdd1:\XellLaunch\default.xex when --launcher is omitted
        --stage-xell-bin <FILE>      Upload a local XeLL binary as xell.bin beside the launch helper before booting
        --quickboot                  Build, upload, and launch a QuickBoot XeLL launcher, and also install a dashboard shortcut when possible
        --quickboot-target <MODE>    QuickBoot target: launcher (default) or flash
        --allow-usb                  Allow XeLL launch even when removable USB storage is attached. Disabled by default because some consoles hang at Fat mount uda0
        --single                     Take one dump only and skip byte-for-byte verification
        --overwrite                  Allow existing output files and archive to be replaced
        --output <FILE>              Output NAND filename. Defaults to nand_backup_yyyyMMdd_HHmmss.bin
        --no-verify                  Skip the second-dump verification loop
        --dry-run                    Preview the dump plan without connecting to the console
```

### `rgh popup`

```text
DESCRIPTION:
Trainer-style console popup helpers

USAGE:
    rgh popup [OPTIONS] <COMMAND>

EXAMPLES:
    rgh popup show --title XeCLI --body "Connected to console"

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    show    Show a native Xbox 360 popup
```

#### `rgh popup show`

```text
DESCRIPTION:
Show a native Xbox 360 popup

USAGE:
    rgh popup show [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              TCP port (default: 730)
        --timeout <MS>             XBDM connection/read stall timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --no-reconnect             Disable automatic XBDM reconnect attempts
        --title <TEXT>             Popup title text
        --body <TEXT>              Popup body text
        --button <TEXT>            Button label. Repeat to add multiple buttons
        --focus <INDEX>            Focused button index (default: 0)
        --preset <NAME>            Popup icon preset: none|error|warning|question (default: none)
        --style <ID>               Raw popup style id. Overrides --preset when provided
```

### `rgh ftp`

```text
DESCRIPTION:
FTP commands (alternate access). Requires an active console FTP service

USAGE:
    rgh ftp [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ftp list --path /Hdd1/
    rgh ftp check
    rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
    rgh ftp hash --path /Hdd1/XeCLI/stage2/scratch.bin --algorithm sha256
    rgh ftp diff --path /Hdd1/XeCLI/stage2/scratch.bin --local .\scratch.bin --algorithm sha256
    rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini --verify-hash sha256
    rgh ftp put --path /Hdd1/launch.ini --in .\launch.ini --verify-hash sha256
    rgh ftp sync --direction upload --path /Hdd1/Plugins --in .\Plugins --recursive --dry-run
    rgh ftp sync --direction download --path /Hdd1/Content --in .\ContentBackup --recursive

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    check     Probe port 21 to confirm whether a compatible dashboard FTP service is running
    target    Show or set FTP target
    list      List files via FTP
    find      Find files via FTP
    get       Download a file via FTP
    put       Upload a file via FTP
    sync      Synchronize files via FTP
    cat       Print a file via FTP
    hash      Hash a remote file via FTP
    diff      Compare a remote file with a local file via FTP
    rm        Delete a file via FTP
    mkdir     Create a directory via FTP
    mv        Move or rename a file via FTP
```

#### `rgh ftp check`

```text
DESCRIPTION:
Probe port 21 to confirm whether a compatible dashboard FTP service is running

USAGE:
    rgh ftp check [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
```

#### `rgh ftp target`

```text
DESCRIPTION:
Show or set FTP target

USAGE:
    rgh ftp target [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --set <IP>       Set the default FTP IP
        --port <PORT>    Set the default FTP port (default: 21)
        --user <USER>    Set the default FTP username
        --pass <PASS>    Set the default FTP password
        --clear          Clear saved FTP target settings
```

#### `rgh ftp list`

```text
DESCRIPTION:
List files via FTP

USAGE:
    rgh ftp list [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --path <PATH>              Remote directory path (default: /)
```

#### `rgh ftp find`

```text
DESCRIPTION:
Find files via FTP

USAGE:
    rgh ftp find [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --path <PATH>              Root path to search (default: /)
        --name <PATTERN>           File or folder name pattern (supports * and ? wildcards)
        --regex                    Interpret --name as a regular expression
        --depth <N>                Maximum recursion depth (default: 6)
        --max <N>                  Maximum results to return (default: 200)
```

#### `rgh ftp get`

```text
DESCRIPTION:
Download a file via FTP

USAGE:
    rgh ftp get [OPTIONS]

OPTIONS:
    -h, --help                       Prints help information
        --ip <IP>                    Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>      Use a saved target profile by name
        --port <PORT>                FTP port (default: 21)
        --user <USER>                FTP username (default: xboxftp)
        --pass <PASS>                FTP password (default: xboxftp)
        --timeout <MS>               FTP timeout in milliseconds (default: 5000)
        --json                       Emit JSON output
        --path <PATH>                Remote file or directory path
        --out <PATH>                 Local output file or directory
        --recursive                  Download directory trees recursively
        --overwrite                  Overwrite local files that already exist
        --resume                     Resume partial single-file downloads
        --verify-hash <ALGORITHM>    Verify with sha256, sha1, or md5
```

#### `rgh ftp put`

```text
DESCRIPTION:
Upload a file via FTP

USAGE:
    rgh ftp put [OPTIONS]

OPTIONS:
    -h, --help                       Prints help information
        --ip <IP>                    Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>      Use a saved target profile by name
        --port <PORT>                FTP port (default: 21)
        --user <USER>                FTP username (default: xboxftp)
        --pass <PASS>                FTP password (default: xboxftp)
        --timeout <MS>               FTP timeout in milliseconds (default: 5000)
        --json                       Emit JSON output
        --path <PATH>                Remote destination file or directory
        --in <PATH>                  Local input file or directory
        --recursive                  Upload directory trees recursively
        --overwrite                  Overwrite remote files that already exist
        --dry-run                    Show upload plan without writing remote files
        --resume                     Resume partial single-file uploads
        --verify-hash <ALGORITHM>    Verify with sha256, sha1, or md5
```

#### `rgh ftp sync`

```text
DESCRIPTION:
Synchronize files via FTP

USAGE:
    rgh ftp sync [OPTIONS]

OPTIONS:
    -h, --help                       Prints help information
        --ip <IP>                    Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>      Use a saved target profile by name
        --port <PORT>                FTP port (default: 21)
        --user <USER>                FTP username (default: xboxftp)
        --pass <PASS>                FTP password (default: xboxftp)
        --timeout <MS>               FTP timeout in milliseconds (default: 5000)
        --json                       Emit JSON output
        --direction <DIRECTION>      upload|download
        --path <PATH>                Remote source or destination path
        --in <PATH>                  Local input or output path
        --recursive                  Transfer directory trees recursively
        --dry-run                    Show the sync plan without transferring files
        --resume                     Resume partial single-file transfers
        --verify-hash <ALGORITHM>    Verify with sha256, sha1, or md5
```

#### `rgh ftp cat`

```text
DESCRIPTION:
Print a file via FTP

USAGE:
    rgh ftp cat [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --path <PATH>
        --max <BYTES>              Maximum bytes to read (default: 65536)
        --encoding <ENC>           ascii|utf8|utf-8 (default: utf8)
```

#### `rgh ftp hash`

```text
DESCRIPTION:
Hash a remote file via FTP

USAGE:
    rgh ftp hash [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --path <PATH>              Remote FTP file path. Use --remote as an alias
        --remote <PATH>            Alias for --path
        --algorithm <ALGORITHM>    Hash algorithm: sha256, sha1, or md5
```

#### `rgh ftp diff`

```text
DESCRIPTION:
Compare a remote file with a local file via FTP

USAGE:
    rgh ftp diff [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --path <PATH>              Remote FTP file path
        --local <FILE>             Local file to compare
        --algorithm <ALGORITHM>    Hash algorithm: sha256, sha1, or md5
```

#### `rgh ftp rm`

```text
DESCRIPTION:
Delete a file via FTP

USAGE:
    rgh ftp rm [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --path <PATH>
```

#### `rgh ftp mkdir`

```text
DESCRIPTION:
Create a directory via FTP

USAGE:
    rgh ftp mkdir [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --path <PATH>
```

#### `rgh ftp mv`

```text
DESCRIPTION:
Move or rename a file via FTP

USAGE:
    rgh ftp mv [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --from <PATH>
        --to <PATH>
```

### `rgh target-saved`

```text
DESCRIPTION:
Store and select saved target profiles

USAGE:
    rgh target-saved [OPTIONS] <COMMAND>

EXAMPLES:
    rgh target-profile list
    rgh target profile list
    rgh target-profile list --csv
    rgh target-profile add home --ip <console-ip> --port 730
    rgh target-profile use home
    rgh target-profile validate
    rgh target-profile current

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    add <NAME>       Add or update a saved target profile
    list             List saved target profiles
    show <NAME>      Show one saved target profile
    validate         Validate saved target profiles and report any issues
    use <NAME>       Set the current target profile and update the saved default target
    current          Show the effective target for the current profile
    remove <NAME>    Remove a saved target profile
```

#### `rgh target-saved add`

```text
DESCRIPTION:
Add or update a saved target profile

USAGE:
    rgh target-saved add <NAME> [OPTIONS]

ARGUMENTS:
    <NAME>    Profile name

OPTIONS:
    -h, --help               Prints help information
        --ip <IP>            Target console host or IP
        --port <PORT>        Target port (default: 730)
        --ftp-port <PORT>    Optional FTP port for this profile
        --ftp-user <USER>    Optional FTP username for this profile
        --ftp-pass <PASS>    Optional FTP password for this profile
        --json               Emit JSON output
        --include-private    Show the target endpoint in full, including the port
```

#### `rgh target-saved list`

```text
DESCRIPTION:
List saved target profiles

USAGE:
    rgh target-saved list [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --csv                Output CSV to stdout
        --include-private    Show the target endpoint in full, including the port
```

#### `rgh target-saved show`

```text
DESCRIPTION:
Show one saved target profile

USAGE:
    rgh target-saved show <NAME> [OPTIONS]

ARGUMENTS:
    <NAME>

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show the target endpoint in full, including the port
```

#### `rgh target-saved validate`

```text
DESCRIPTION:
Validate saved target profiles and report any issues

USAGE:
    rgh target-saved validate [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show the target endpoint in full, including the port
```

#### `rgh target-saved use`

```text
DESCRIPTION:
Set the current target profile and update the saved default target

USAGE:
    rgh target-saved use <NAME> [OPTIONS]

ARGUMENTS:
    <NAME>

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show the target endpoint in full, including the port
```

#### `rgh target-saved current`

```text
DESCRIPTION:
Show the effective target for the current profile

USAGE:
    rgh target-saved current [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --json               Emit JSON output
        --include-private    Show the target endpoint in full, including the port
```

#### `rgh target-saved remove`

```text
DESCRIPTION:
Remove a saved target profile

USAGE:
    rgh target-saved remove <NAME> [OPTIONS]

ARGUMENTS:
    <NAME>

OPTIONS:
    -h, --help     Prints help information
        --force
        --json     Emit JSON output
```

### `rgh save`

```text
DESCRIPTION:
Profile and save-data helpers over FTP

USAGE:
    rgh save [OPTIONS] <COMMAND>

EXAMPLES:
    rgh save list --titleid FFFE07D1 --device Hdd1
    rgh save extract --titleid 415608C3 --out .\saves

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List save files for a title
    extract    Extract save files for a title
    inject     Upload save files for a title
```

#### `rgh save list`

```text
DESCRIPTION:
List save files for a title

USAGE:
    rgh save list [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --titleid <TITLEID>        Title ID in hex (for example 4D530805)
        --profile <PROFILE>        Restrict to one 16-character profile ID
        --device <ROOTS>           Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0
```

#### `rgh save extract`

```text
DESCRIPTION:
Extract save files for a title

USAGE:
    rgh save extract [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --titleid <TITLEID>        Title ID in hex (for example 4D530805)
        --profile <PROFILE>        Restrict to one 16-character profile ID
        --out <DIR>                Destination directory
        --overwrite                Overwrite files that already exist
        --device <ROOTS>           Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0
```

#### `rgh save inject`

```text
DESCRIPTION:
Upload save files for a title

USAGE:
    rgh save inject [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --ip <IP>                   Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>     Use a saved target profile by name
        --port <PORT>               FTP port (default: 21)
        --user <USER>               FTP username (default: xboxftp)
        --pass <PASS>               FTP password (default: xboxftp)
        --timeout <MS>              FTP timeout in milliseconds (default: 5000)
        --json                      Emit JSON output
        --titleid <TITLEID>         Title ID in hex (for example 4D530805)
        --profile <PROFILE>         Destination 16-character profile ID
        --device <ROOT>             Destination storage root, for example Hdd1 or Usb0 (default: Hdd1)
        --in <PATH>                 Local file or directory to upload
        --remote-path <RELATIVE>    Relative save path to use when --in points to a single file
        --overwrite                 Overwrite remote files that already exist
        --dry-run                   Show the remote paths that would be uploaded without writing anything
```

### `rgh content`

```text
DESCRIPTION:
Installed content management over FTP

USAGE:
    rgh content [OPTIONS] <COMMAND>

EXAMPLES:
    rgh content list --device Hdd1 --show-types
    rgh content browse .\profile.con
    rgh content browse .\FFFE07D1.gpd

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list             List installed titles and content types
    delete           Delete installed content for a title
    browse <FILE>    Browse a local profile package or dashboard GPD
```

#### `rgh content list`

```text
DESCRIPTION:
List installed titles and content types

USAGE:
    rgh content list [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --device <ROOTS>           Comma-separated content roots, for example Hdd1 or Hdd1,Usb0 (default: Hdd1,Usb0,Usb1,HddX)
        --titleid <TITLEID>        Restrict to one Title ID
        --show-types               Include content-type breakdowns per title
```

#### `rgh content delete`

```text
DESCRIPTION:
Delete installed content for a title

USAGE:
    rgh content delete [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --titleid <TITLEID>        Title ID to remove
        --device <ROOTS>           Comma-separated content roots, for example Hdd1 or Hdd1,Usb0
        --yes                      Delete without interactive confirmation
```

#### `rgh content browse`

```text
DESCRIPTION:
Browse a local profile package or dashboard GPD

USAGE:
    rgh content browse <FILE> [OPTIONS]

ARGUMENTS:
    <FILE>    Path to a profile package or dashboard GPD file

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

### `rgh con`

```text
DESCRIPTION:
Inspect and repair local Xbox 360 content packages

USAGE:
    rgh con [OPTIONS] <COMMAND>

EXAMPLES:
    rgh con info .\E000000000000000.con
    rgh con verify .\E000000000000000.con
    rgh con rehash .\E000000000000000.con --keyvault .\KV.bin
    rgh con resign .\E000000000000000.con --keyvault .\KV.bin

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    info <PACKAGE>          Show package metadata and derived FATX values
    verify <PACKAGE>        Verify a CON signature when supported
    rehash <PACKAGE>        Save package headers and refresh STFS hashes
    resign <PACKAGE>        Re-sign a CON package and verify the result
    magic-name <PACKAGE>    Print the package's FATX magic filename
    fatx-path <PACKAGE>     Print the destination FATX content path
```

#### `rgh con info`

```text
DESCRIPTION:
Show package metadata and derived FATX values

USAGE:
    rgh con info <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the CON/LIVE/PIRS package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

#### `rgh con verify`

```text
DESCRIPTION:
Verify a CON signature when supported

USAGE:
    rgh con verify <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the CON/LIVE/PIRS package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

#### `rgh con rehash`

```text
DESCRIPTION:
Save package headers and refresh STFS hashes

USAGE:
    rgh con rehash <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the STFS package

OPTIONS:
    -h, --help               Prints help information
        --keyvault <FILE>    Your decrypted Xbox 360 keyvault, required when rehashing a CON package
        --json               Output JSON
```

#### `rgh con resign`

```text
DESCRIPTION:
Re-sign a CON package and verify the result

USAGE:
    rgh con resign <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the CON package

OPTIONS:
    -h, --help               Prints help information
        --keyvault <FILE>    Your decrypted Xbox 360 keyvault, required to re-sign the CON package
        --json               Output JSON
```

#### `rgh con magic-name`

```text
DESCRIPTION:
Print the package's FATX magic filename

USAGE:
    rgh con magic-name <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the CON/LIVE/PIRS package

OPTIONS:
    -h, --help    Prints help information
```

#### `rgh con fatx-path`

```text
DESCRIPTION:
Print the destination FATX content path

USAGE:
    rgh con fatx-path <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the CON/LIVE/PIRS package

OPTIONS:
    -h, --help        Prints help information
        --fix-name    Replace the leaf filename with the package's magic FATX name when available
```

### `rgh profile`

```text
DESCRIPTION:
Inspect and edit local Xbox 360 profile packages

USAGE:
    rgh profile [OPTIONS] <COMMAND>

EXAMPLES:
    rgh profile info .\E000000000000000.con
    rgh profile extract .\E000000000000000.con .\profile-files
    rgh profile account show .\E000000000000000.con
    rgh profile account extract .\E000000000000000.con .\Account
    rgh profile account set-gamertag .\E000000000000000.con ExampleTag --keyvault .\KV.bin
    rgh profile gpd list .\E000000000000000.con
    rgh profile gpd extract .\E000000000000000.con .\FFFE07D1.gpd --dashboard
    rgh profile titles list .\E000000000000000.con
    rgh profile titles add .\E000000000000000.con --titleid 415607E7 --keyvault .\KV.bin
    rgh profile achievements list .\E000000000000000.con --titleid 4D530805
    rgh profile achievements unlock .\E000000000000000.con --titleid 4D530805 --achievementid 0x00000001 --keyvault .\KV.bin
    rgh profile settings get .\E000000000000000.con 0x10040006
    rgh profile settings set .\E000000000000000.con 0x10040006 1337 --keyvault .\KV.bin
    rgh profile avatar-colors get .\E000000000000000.con
    rgh profile avatar-colors set .\E000000000000000.con --hair 0xFF22150D --keyvault .\KV.bin

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    info <PACKAGE>                Show profile package contents and summary information
    extract <PACKAGE> <OUTDIR>    Extract files from a profile package
    account                       Inspect and update account data inside a profile package
    gpd                           List and extract embedded dashboard and title GPD files
    titles                        Inspect title records inside a profile package
    achievements                  Inspect and edit achievement data inside a profile package
    settings                      Inspect and edit profile settings stored in the dashboard GPD
    avatar-colors                 Inspect and edit avatar color values stored in the dashboard profile setting blob
```

#### `rgh profile info`

```text
DESCRIPTION:
Show profile package contents and summary information

USAGE:
    rgh profile info <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

#### `rgh profile extract`

```text
DESCRIPTION:
Extract files from a profile package

USAGE:
    rgh profile extract <PACKAGE> <OUTDIR> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package
    <OUTDIR>     Destination directory

OPTIONS:
    -h, --help         Prints help information
        --overwrite    Overwrite files that already exist
        --json         Output JSON
```

#### `rgh profile account`

```text
DESCRIPTION:
Inspect and update account data inside a profile package

USAGE:
    rgh profile account [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    show <PACKAGE>                       Show decoded profile account information
    extract <PACKAGE> <OUT>              Extract the raw Account payload from a profile package
    set-gamertag <PACKAGE> <GAMERTAG>    Update the profile account gamertag
```

##### `rgh profile account show`

```text
DESCRIPTION:
Show decoded profile account information

USAGE:
    rgh profile account show <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

##### `rgh profile account extract`

```text
DESCRIPTION:
Extract the raw Account payload from a profile package

USAGE:
    rgh profile account extract <PACKAGE> <OUT> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package
    <OUT>        Path to write the raw Account payload

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

##### `rgh profile account set-gamertag`

```text
DESCRIPTION:
Update the profile account gamertag

USAGE:
    rgh profile account set-gamertag <PACKAGE> <GAMERTAG> [OPTIONS]

ARGUMENTS:
    <PACKAGE>     Path to the profile package
    <GAMERTAG>    Replacement gamertag

OPTIONS:
    -h, --help               Prints help information
        --keyvault <FILE>    Your decrypted Xbox 360 keyvault, required when the profile is a CON package
        --json               Output JSON
```

#### `rgh profile gpd`

```text
DESCRIPTION:
List and extract embedded dashboard and title GPD files

USAGE:
    rgh profile gpd [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list <PACKAGE>             List embedded dashboard and title GPD files
    extract <PACKAGE> <OUT>    Extract one dashboard or title GPD from the profile package
```

##### `rgh profile gpd list`

```text
DESCRIPTION:
List embedded dashboard and title GPD files

USAGE:
    rgh profile gpd list <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

##### `rgh profile gpd extract`

```text
DESCRIPTION:
Extract one dashboard or title GPD from the profile package

USAGE:
    rgh profile gpd extract <PACKAGE> <OUT> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package
    <OUT>        Path to write the selected GPD

OPTIONS:
    -h, --help                 Prints help information
        --dashboard            Extract the dashboard GPD (FFFE07D1.gpd)
        --titleid <TITLEID>    Extract a specific title GPD by title ID
        --json                 Output JSON
```

#### `rgh profile titles`

```text
DESCRIPTION:
Inspect title records inside a profile package

USAGE:
    rgh profile titles [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list <PACKAGE>    List title records from the dashboard GPD
    add <PACKAGE>     Insert or refresh one dashboard title record
```

##### `rgh profile titles list`

```text
DESCRIPTION:
List title records from the dashboard GPD

USAGE:
    rgh profile titles list <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
        --csv     Output CSV
```

##### `rgh profile titles add`

```text
DESCRIPTION:
Insert or refresh one dashboard title record

USAGE:
    rgh profile titles add <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help                             Prints help information
        --keyvault <FILE>                  Your decrypted Xbox 360 keyvault, required when the profile is a CON package
        --titleid <TITLEID>                Title ID in hex, for example 415607E7
        --name <NAME>                      Optional title name override
        --achievements-possible <COUNT>    Optional possible-achievement count. Defaults to the embedded title GPD when available
        --credit-possible <COUNT>          Optional possible gamerscore total. Defaults to the embedded title GPD when available
        --last-loaded <TIMESTAMP>          Optional ISO-8601 UTC timestamp to store as the last-loaded time
        --replace                          Replace an existing dashboard title record instead of failing
        --json                             Output JSON
```

#### `rgh profile achievements`

```text
DESCRIPTION:
Inspect and edit achievement data inside a profile package

USAGE:
    rgh profile achievements [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list <PACKAGE>      List achievements for one title GPD
    unlock <PACKAGE>    Unlock one profile achievement and update title totals
    lock <PACKAGE>      Lock one profile achievement and update title totals
```

##### `rgh profile achievements list`

```text
DESCRIPTION:
List achievements for one title GPD

USAGE:
    rgh profile achievements list <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help                 Prints help information
        --titleid <TITLEID>    Title ID in hex, for example 4D530805
        --json                 Output JSON
```

##### `rgh profile achievements unlock`

```text
DESCRIPTION:
Unlock one profile achievement and update title totals

USAGE:
    rgh profile achievements unlock <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help                             Prints help information
        --keyvault <FILE>                  Your decrypted Xbox 360 keyvault, required when the profile is a CON package
        --titleid <TITLEID>                Title ID in hex, for example 4D530805
        --achievementid <ACHIEVEMENTID>    Achievement ID in decimal or hex
        --online                           Mark the achievement as unlocked online
        --time <TIMESTAMP>                 Optional ISO-8601 UTC timestamp for online unlocks
        --json                             Output JSON
```

##### `rgh profile achievements lock`

```text
DESCRIPTION:
Lock one profile achievement and update title totals

USAGE:
    rgh profile achievements lock <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help                             Prints help information
        --keyvault <FILE>                  Your decrypted Xbox 360 keyvault, required when the profile is a CON package
        --titleid <TITLEID>                Title ID in hex, for example 4D530805
        --achievementid <ACHIEVEMENTID>    Achievement ID in decimal or hex
        --json                             Output JSON
```

#### `rgh profile settings`

```text
DESCRIPTION:
Inspect and edit profile settings stored in the dashboard GPD

USAGE:
    rgh profile settings [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list <PACKAGE>                       List profile settings
    get <PACKAGE> <SETTINGID>            Read a single profile setting
    set <PACKAGE> <SETTINGID> <VALUE>    Create or update a single profile setting
```

##### `rgh profile settings list`

```text
DESCRIPTION:
List profile settings

USAGE:
    rgh profile settings list <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

##### `rgh profile settings get`

```text
DESCRIPTION:
Read a single profile setting

USAGE:
    rgh profile settings get <PACKAGE> <SETTINGID> [OPTIONS]

ARGUMENTS:
    <PACKAGE>      Path to the profile package
    <SETTINGID>    Setting ID in hex, for example 0x10040006

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

##### `rgh profile settings set`

```text
DESCRIPTION:
Create or update a single profile setting

USAGE:
    rgh profile settings set <PACKAGE> <SETTINGID> <VALUE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>      Path to the profile package
    <SETTINGID>    Setting ID in hex, for example 0x10040006
    <VALUE>        New setting value

OPTIONS:
    -h, --help                    Prints help information
        --keyvault <FILE>         Your decrypted Xbox 360 keyvault, required when the profile is a CON package
        --type <TYPE>             Setting type for missing records: int32, int64, double, unicode, float, binary, datetime, or null
        --binary-flags <FLAGS>    Optional binary/unicode flags value
        --json                    Output JSON
```

#### `rgh profile avatar-colors`

```text
DESCRIPTION:
Inspect and edit avatar color values stored in the dashboard profile setting blob

USAGE:
    rgh profile avatar-colors [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    get <PACKAGE>    Show avatar ARGB color values
    set <PACKAGE>    Update one or more avatar ARGB color values
```

##### `rgh profile avatar-colors get`

```text
DESCRIPTION:
Show avatar ARGB color values

USAGE:
    rgh profile avatar-colors get <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

##### `rgh profile avatar-colors set`

```text
DESCRIPTION:
Update one or more avatar ARGB color values

USAGE:
    rgh profile avatar-colors set <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Path to the profile package

OPTIONS:
    -h, --help                   Prints help information
        --keyvault <FILE>        Your decrypted Xbox 360 keyvault, required when the profile is a CON package
        --skin <ARGB>            ARGB color for the avatar skin
        --hair <ARGB>            ARGB color for the avatar hair
        --lip <ARGB>             ARGB color for the avatar lips
        --eye <ARGB>             ARGB color for the avatar eyes
        --eyebrow <ARGB>         ARGB color for the avatar eyebrows
        --eyeshadow <ARGB>       ARGB color for the avatar eye shadow
        --facial-hair <ARGB>     ARGB color for the avatar facial hair
        --face-paint <ARGB>      ARGB color for the primary face paint slot
        --face-paint-2 <ARGB>    ARGB color for the secondary face paint slot
        --json                   Output JSON
```

### `rgh xdbf`

```text
DESCRIPTION:
Inspect and extract records from local GPD/XDBF files

USAGE:
    rgh xdbf [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xdbf list .\415608C3.gpd --show-sync
    rgh xdbf get .\415608C3.gpd strings 0x0000000000008000 --out .\title-name.bin
    rgh xdbf extract .\415608C3.gpd .\records
    rgh xdbf sync-status .\415608C3.gpd

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list <FILE>                    List records in a GPD/XDBF file
    get <FILE> <NAMESPACE> <ID>    Read a single record from a GPD/XDBF file
    extract <FILE> <OUTDIR>        Extract records to a directory
    sync-status <FILE>             Show records that are pending sync
```

#### `rgh xdbf list`

```text
DESCRIPTION:
List records in a GPD/XDBF file

USAGE:
    rgh xdbf list <FILE> [OPTIONS]

ARGUMENTS:
    <FILE>    Path to the GPD/XDBF file

OPTIONS:
    -h, --help                     Prints help information
        --namespace <NAMESPACE>    Optional namespace filter: achievements, images, settings, titles, strings, avatar, or the numeric namespace ID
        --origin <ORIGIN>          Sync record origin: profile or pec (default: profile)
        --show-sync                Include pending-sync status for each record
        --json                     Output JSON
```

#### `rgh xdbf get`

```text
DESCRIPTION:
Read a single record from a GPD/XDBF file

USAGE:
    rgh xdbf get <FILE> <NAMESPACE> <ID> [OPTIONS]

ARGUMENTS:
    <FILE>         Path to the GPD/XDBF file
    <NAMESPACE>    Namespace name or numeric ID
    <ID>           Record ID, in decimal or hex

OPTIONS:
    -h, --help               Prints help information
        --origin <ORIGIN>    Sync record origin: profile or pec (default: profile)
        --out <FILE>         Write the record payload to a file instead of rendering a hex preview
        --json               Output JSON
```

#### `rgh xdbf extract`

```text
DESCRIPTION:
Extract records to a directory

USAGE:
    rgh xdbf extract <FILE> <OUTDIR> [OPTIONS]

ARGUMENTS:
    <FILE>      Path to the GPD/XDBF file
    <OUTDIR>    Directory to write extracted records into

OPTIONS:
    -h, --help                     Prints help information
        --namespace <NAMESPACE>    Optional namespace filter: achievements, images, settings, titles, strings, avatar, or the numeric namespace ID
        --origin <ORIGIN>          Sync record origin: profile or pec (default: profile)
```

#### `rgh xdbf sync-status`

```text
DESCRIPTION:
Show records that are pending sync

USAGE:
    rgh xdbf sync-status <FILE> [OPTIONS]

ARGUMENTS:
    <FILE>    Path to the GPD/XDBF file

OPTIONS:
    -h, --help                     Prints help information
        --namespace <NAMESPACE>    Optional namespace filter: achievements, images, settings, titles, strings, avatar, or the numeric namespace ID
        --origin <ORIGIN>          Sync record origin: profile or pec (default: profile)
        --json                     Output JSON
```

### `rgh avatar`

```text
DESCRIPTION:
Avatar item library and install helpers

USAGE:
    rgh avatar [OPTIONS] <COMMAND>

EXAMPLES:
    rgh avatar library show
    rgh avatar cache status
    rgh avatar cache refresh
    rgh avatar games --search "Black Ops"
    rgh avatar items --titleid 415608C3 --limit 10
    rgh avatar choose --search "Black Ops"
    rgh avatar browse --remote
    rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user
    rgh avatar apply --titleid 415608C3 --all --current-user

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    library    Show or set avatar collection paths
    cache      Inspect and refresh avatar index caches
    games      List games with available avatar items
    items      List avatar items from the collection
    choose     Interactively choose a game and avatar items in the terminal
    browse     Browse avatar items in a Windows picker and install selected entries
    install    Patch avatar items for a user and install them to the console
```

#### `rgh avatar library`

```text
DESCRIPTION:
Show or set avatar collection paths

USAGE:
    rgh avatar library [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    show    Show the current avatar library and cache paths
    set     Set the default avatar library and cache paths
```

##### `rgh avatar library show`

```text
DESCRIPTION:
Show the current avatar library and cache paths

USAGE:
    rgh avatar library show [OPTIONS]

OPTIONS:
    -h, --help    Prints help information
        --json    Emit JSON output
```

##### `rgh avatar library set`

```text
DESCRIPTION:
Set the default avatar library and cache paths

USAGE:
    rgh avatar library set [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --path <DIR>                Set the default Avatar-Item-Collection root
        --cache <PATH>              Set the avatar index cache file or directory
        --manifest-url <URL>        Set the hosted avatar manifest URL
        --title-map-url <URL>       Set the hosted avatar title map URL
        --content-base-url <URL>    Set the hosted avatar content base URL
        --download-cache <DIR>      Set the cache directory for downloaded avatar packages
        --clear                     Clear saved avatar library settings
```

#### `rgh avatar cache`

```text
DESCRIPTION:
Inspect and refresh avatar index caches

USAGE:
    rgh avatar cache [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    status     Show the current avatar cache state
    refresh    Refresh the local or remote avatar cache
```

##### `rgh avatar cache status`

```text
DESCRIPTION:
Show the current avatar cache state

USAGE:
    rgh avatar cache status [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --library <DIR>             Avatar item library root. Defaults to config or Avatar-Item-Collection
        --cache <PATH>              Avatar index cache file or directory
        --remote                    Use the hosted avatar library instead of the local corpus
        --manifest-url <URL>        Override the hosted avatar manifest URL
        --title-map-url <URL>       Override the hosted avatar title map URL
        --content-base-url <URL>    Override the hosted avatar content base URL
        --download-cache <DIR>      Directory used to cache downloaded avatar packages
        --json                      Emit JSON output
```

##### `rgh avatar cache refresh`

```text
DESCRIPTION:
Refresh the local or remote avatar cache

USAGE:
    rgh avatar cache refresh [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --library <DIR>             Avatar item library root. Defaults to config or Avatar-Item-Collection
        --cache <PATH>              Avatar index cache file or directory
        --remote                    Use the hosted avatar library instead of the local corpus
        --manifest-url <URL>        Override the hosted avatar manifest URL
        --title-map-url <URL>       Override the hosted avatar title map URL
        --content-base-url <URL>    Override the hosted avatar content base URL
        --download-cache <DIR>      Directory used to cache downloaded avatar packages
        --json                      Emit JSON output
```

#### `rgh avatar games`

```text
DESCRIPTION:
List games with available avatar items

USAGE:
    rgh avatar games [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --library <DIR>             Avatar item library root. Defaults to config or Avatar-Item-Collection
        --cache <PATH>              Avatar index cache file or directory
        --remote                    Use the hosted avatar library instead of the local corpus
        --manifest-url <URL>        Override the hosted avatar manifest URL
        --title-map-url <URL>       Override the hosted avatar title map URL
        --content-base-url <URL>    Override the hosted avatar content base URL
        --download-cache <DIR>      Directory used to cache downloaded avatar packages
        --json                      Emit JSON output
        --search <TEXT>             Filter titles by name, publisher, or title id
        --limit <N>                 Maximum titles to show (default: 100, 0 = no limit)
        --no-cache                  Force a fresh library scan instead of using the cache
```

#### `rgh avatar items`

```text
DESCRIPTION:
List avatar items from the collection

USAGE:
    rgh avatar items [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --library <DIR>             Avatar item library root. Defaults to config or Avatar-Item-Collection
        --cache <PATH>              Avatar index cache file or directory
        --remote                    Use the hosted avatar library instead of the local corpus
        --manifest-url <URL>        Override the hosted avatar manifest URL
        --title-map-url <URL>       Override the hosted avatar title map URL
        --content-base-url <URL>    Override the hosted avatar content base URL
        --download-cache <DIR>      Directory used to cache downloaded avatar packages
        --json                      Emit JSON output
        --titleid <TITLEID>         Restrict items to a Title ID
        --game <TEXT>               Restrict items to a game name match
        --search <TEXT>             Search by item name, game name, or content id
        --publisher <TEXT>          Restrict items to one publisher
        --tag <TEXT>                Restrict items to one derived tag
        --limit <N>                 Maximum items to show (default: 100, 0 = no limit)
        --no-cache                  Force a fresh library scan instead of using the cache
```

#### `rgh avatar choose`

```text
DESCRIPTION:
Interactively choose a game and avatar items in the terminal

USAGE:
    rgh avatar choose [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --library <DIR>             Avatar item library root. Defaults to config or Avatar-Item-Collection
        --cache <PATH>              Avatar index cache file or directory
        --remote                    Use the hosted avatar library instead of the local corpus
        --manifest-url <URL>        Override the hosted avatar manifest URL
        --title-map-url <URL>       Override the hosted avatar title map URL
        --content-base-url <URL>    Override the hosted avatar content base URL
        --download-cache <DIR>      Directory used to cache downloaded avatar packages
        --json                      Emit JSON output
        --ip <IP>                   Console IP address. If omitted, uses the last connected console
        --port <PORT>               FTP port (default: 21)
        --user <USER>               FTP username (default: xboxftp)
        --pass <PASS>               FTP password (default: xboxftp)
        --timeout <MS>              FTP timeout in milliseconds (default: 5000)
        --titleid <TITLEID>         Install all items for one title when paired with --all
        --contentid <CONTENTID>     Install one specific avatar item
        --all                       Install all items for the selected title
        --device <ROOT>             Console storage root (default: Hdd1)
        --xuid <XUID>               Explicit target XUID. Defaults to the current signed-in user
        --gamertag <NAME>           Label shown in local output when --xuid is provided
        --current-user              Use the current signed-in user explicitly
        --xbdm-port <PORT>          XBDM port used for current-user resolution (default: saved target port or 730)
        --working <DIR>             Working directory for patched temporary files
        --overwrite                 Overwrite remote files that already exist
        --dry-run                   Show the install plan without uploading anything
        --search <TEXT>             Initial item search text
        --game <TEXT>               Initial game/title filter text
        --publisher <TEXT>          Initial publisher filter
        --tag <TEXT>                Initial derived tag filter
        --limit <N>                 Maximum titles/items to preload (default: 100, 0 = no limit)
        --no-cache                  Force a fresh library scan instead of using the cache
        --show-sensitive            Show raw gamertag, XUID, and source path details in the browser
```

#### `rgh avatar browse`

```text
DESCRIPTION:
Browse avatar items in a Windows picker and install selected entries

USAGE:
    rgh avatar browse [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --library <DIR>             Avatar item library root. Defaults to config or Avatar-Item-Collection
        --cache <PATH>              Avatar index cache file or directory
        --remote                    Use the hosted avatar library instead of the local corpus
        --manifest-url <URL>        Override the hosted avatar manifest URL
        --title-map-url <URL>       Override the hosted avatar title map URL
        --content-base-url <URL>    Override the hosted avatar content base URL
        --download-cache <DIR>      Directory used to cache downloaded avatar packages
        --json                      Emit JSON output
        --ip <IP>                   Console IP address. If omitted, uses the last connected console
        --port <PORT>               FTP port (default: 21)
        --user <USER>               FTP username (default: xboxftp)
        --pass <PASS>               FTP password (default: xboxftp)
        --timeout <MS>              FTP timeout in milliseconds (default: 5000)
        --titleid <TITLEID>         Install all items for one title when paired with --all
        --contentid <CONTENTID>     Install one specific avatar item
        --all                       Install all items for the selected title
        --device <ROOT>             Console storage root (default: Hdd1)
        --xuid <XUID>               Explicit target XUID. Defaults to the current signed-in user
        --gamertag <NAME>           Label shown in local output when --xuid is provided
        --current-user              Use the current signed-in user explicitly
        --xbdm-port <PORT>          XBDM port used for current-user resolution (default: saved target port or 730)
        --working <DIR>             Working directory for patched temporary files
        --overwrite                 Overwrite remote files that already exist
        --dry-run                   Show the install plan without uploading anything
        --search <TEXT>             Initial item search text
        --game <TEXT>               Initial game/title filter text
        --publisher <TEXT>          Initial publisher filter
        --tag <TEXT>                Initial derived tag filter
        --limit <N>                 Maximum titles/items to preload (default: 100, 0 = no limit)
        --no-cache                  Force a fresh library scan instead of using the cache
        --show-sensitive            Show raw gamertag, XUID, and source path details in the browser
```

#### `rgh avatar install`

```text
DESCRIPTION:
Patch avatar items for a user and install them to the console

USAGE:
    rgh avatar install [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --library <DIR>             Avatar item library root. Defaults to config or Avatar-Item-Collection
        --cache <PATH>              Avatar index cache file or directory
        --remote                    Use the hosted avatar library instead of the local corpus
        --manifest-url <URL>        Override the hosted avatar manifest URL
        --title-map-url <URL>       Override the hosted avatar title map URL
        --content-base-url <URL>    Override the hosted avatar content base URL
        --download-cache <DIR>      Directory used to cache downloaded avatar packages
        --json                      Emit JSON output
        --ip <IP>                   Console IP address. If omitted, uses the last connected console
        --port <PORT>               FTP port (default: 21)
        --user <USER>               FTP username (default: xboxftp)
        --pass <PASS>               FTP password (default: xboxftp)
        --timeout <MS>              FTP timeout in milliseconds (default: 5000)
        --titleid <TITLEID>         Install all items for one title when paired with --all
        --contentid <CONTENTID>     Install one specific avatar item
        --all                       Install all items for the selected title
        --device <ROOT>             Console storage root (default: Hdd1)
        --xuid <XUID>               Explicit target XUID. Defaults to the current signed-in user
        --gamertag <NAME>           Label shown in local output when --xuid is provided
        --current-user              Use the current signed-in user explicitly
        --xbdm-port <PORT>          XBDM port used for current-user resolution (default: saved target port or 730)
        --working <DIR>             Working directory for patched temporary files
        --overwrite                 Overwrite remote files that already exist
        --dry-run                   Show the install plan without uploading anything
```

### `rgh plugin`

```text
DESCRIPTION:
DashLaunch plugin management

USAGE:
    rgh plugin [OPTIONS] <COMMAND>

EXAMPLES:
    rgh plugin list
    rgh plugin enable --slot 5 --path Hdd:\XDRPC.xex --backup

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List configured DashLaunch plugins
    enable     Set a DashLaunch plugin slot
    disable    Clear a DashLaunch plugin slot
```

#### `rgh plugin list`

```text
DESCRIPTION:
List configured DashLaunch plugins

USAGE:
    rgh plugin list [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --ini <PATH>               DashLaunch config path (default: /Hdd1/launch.ini)
```

#### `rgh plugin enable`

```text
DESCRIPTION:
Set a DashLaunch plugin slot

USAGE:
    rgh plugin enable [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --slot <N>                 Plugin slot number, 1-5
        --path <PATH>              Plugin XEX path
        --ini <PATH>               DashLaunch config path (default: /Hdd1/launch.ini)
        --backup                   Create a .bak copy before writing
```

#### `rgh plugin disable`

```text
DESCRIPTION:
Clear a DashLaunch plugin slot

USAGE:
    rgh plugin disable [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last connected console
        --target-profile <NAME>    Use a saved target profile by name
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --slot <N>                 Plugin slot number, 1-5
        --ini <PATH>               DashLaunch config path (default: /Hdd1/launch.ini)
        --backup                   Create a .bak copy before writing
```

### `rgh god`

```text
DESCRIPTION:
ISO to Games on Demand conversion

USAGE:
    rgh god [OPTIONS] <COMMAND>

EXAMPLES:
    rgh god info .\game.iso
    rgh god watch .\incoming --dest .\god --once

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    info <ISO>            Inspect an ISO and show title metadata
    build <ISO> <DEST>    Convert an ISO into GOD parts
    watch <WATCH>         Watch a folder and auto-convert ISOs
```

#### `rgh god info`

```text
DESCRIPTION:
Inspect an ISO and show title metadata

USAGE:
    rgh god info <ISO> [OPTIONS]

ARGUMENTS:
    <ISO>    Path to the ISO image

OPTIONS:
    -h, --help    Prints help information
        --json    Output JSON
```

#### `rgh god build`

```text
DESCRIPTION:
Convert an ISO into GOD parts

USAGE:
    rgh god build <ISO> <DEST> [OPTIONS]

ARGUMENTS:
    <ISO>     Path to the ISO image
    <DEST>    Output folder for the GOD package

OPTIONS:
    -h, --help            Prints help information
        --trim <MODE>     Trim unused space: end or none (default: end)
    -j, --threads <N>     Parallel workers for part files (default: 1)
        --title <NAME>    Override the package display title
```

#### `rgh god watch`

```text
DESCRIPTION:
Watch a folder and auto-convert ISOs

USAGE:
    rgh god watch <WATCH> [OPTIONS]

ARGUMENTS:
    <WATCH>    Directory to watch for new ISO files

OPTIONS:
    -h, --help                 Prints help information
        --dest <DIR>           Output root directory for GOD packages
        --trim <MODE>          Trim unused space: end or none (default: end)
    -j, --threads <N>          Parallel workers for part files (default: 1)
        --title <NAME>         Override the package display title for all conversions
        --ext <LIST>           Comma-separated extensions to include (default: iso)
        --recursive            Watch subdirectories recursively
        --settle <SECONDS>     Seconds a file must remain unchanged before converting (default: 8)
        --poll <MS>            Poll interval in milliseconds for stability checks (default: 500)
        --timeout <SECONDS>    Max seconds to wait for a file to become stable (0 = no timeout)
        --retries <N>          Retry failed conversions (default: 2)
        --delete-source        Delete ISO files after a successful conversion
        --move-done <DIR>      Move ISO files to this folder after successful conversion
        --move-failed <DIR>    Move ISO files to this folder after failed conversion
        --once                 Process existing ISOs once and exit
```

### `rgh ghidra`

```text
DESCRIPTION:
Ghidra headless helpers (Free, external install required)

USAGE:
    rgh ghidra [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ghidra config --path C:\Tools\ghidra --java C:\Java
    rgh ghidra install-loader
    rgh ghidra export-symbols --in .\game.xex --out .\symbols.json
    rgh ghidra decompile --running --out .\decomp

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    config            Configure Ghidra paths
    install-loader    Download or install XEXLoaderWV into the configured Ghidra install
    analyze           Run headless analysis
    export-symbols    Export function entry symbols to a sidecar JSON file
    decompile         Decompile a module or XEX
    verify            Verify decompiler output for bad-instruction placeholders
```

#### `rgh ghidra config`

```text
DESCRIPTION:
Configure Ghidra paths

USAGE:
    rgh ghidra config [OPTIONS]

OPTIONS:
    -h, --help              Prints help information
        --path <DIR>        Ghidra install directory (contains support/analyzeHeadless.bat)
        --java <DIR>        JAVA_HOME to use for Ghidra
        --projects <DIR>    Default Ghidra projects directory
        --clear             Clear stored Ghidra settings
```

#### `rgh ghidra install-loader`

```text
DESCRIPTION:
Download or install XEXLoaderWV into the configured Ghidra install

USAGE:
    rgh ghidra install-loader [OPTIONS]

OPTIONS:
    -h, --help              Prints help information
        --path <DIR>        Ghidra install directory override
        --archive <FILE>    Use a local XEXLoaderWV archive. Requires --sha256
        --url <URL>         Download XEXLoaderWV from an explicit URL. Requires --sha256
        --sha256 <HASH>     Required SHA256 for a custom --archive or --url source
```

#### `rgh ghidra analyze`

```text
DESCRIPTION:
Run headless analysis

USAGE:
    rgh ghidra analyze [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --in <FILE>          Input file to analyze (XEX)
        --ftp-path <PATH>    Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex)
        --running            Use the running title XEX (resolved via XBDM + FTP)
        --project <NAME>     Project name (default: <file>_headless)
        --projects <DIR>     Project root directory override
        --path <DIR>         Ghidra install directory override
        --java <DIR>         JAVA_HOME override
        --loader <NAME>      Explicit loader name
        --timeout <SEC>      Analysis timeout per file (seconds, default: 5000)
        --delete-project     Delete a project created by this run after analysis completes
        --overwrite          Overwrite existing file in project
```

#### `rgh ghidra export-symbols`

```text
DESCRIPTION:
Export function entry symbols to a sidecar JSON file

USAGE:
    rgh ghidra export-symbols [OPTIONS]

OPTIONS:
    -h, --help                 Prints help information
        --in <FILE>            Input file to analyze (XEX)
        --ftp-path <PATH>      Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex)
        --running              Use the running title XEX (resolved via XBDM + FTP)
        --out <FILE>           Output symbol sidecar JSON file
        --project <NAME>       Project name (default: <file>_headless)
        --projects <DIR>       Project root directory override
        --path <DIR>           Ghidra install directory override
        --java <DIR>           JAVA_HOME override
        --loader <NAME>        Explicit loader name
        --timeout <SEC>        Analysis timeout per file (seconds, default: 5000)
        --delete-project       Delete a project created by this run after analysis completes
        --overwrite            Overwrite existing file in project
        --script-path <DIR>    Override script path (default: ghidra_scripts next to rgh.exe)
```

#### `rgh ghidra decompile`

```text
DESCRIPTION:
Decompile a module or XEX

USAGE:
    rgh ghidra decompile [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --in <FILE>             Input file to analyze (XEX)
        --ftp-path <PATH>       Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex)
        --running               Use the running title XEX (resolved via XBDM + FTP)
        --out <DIR>             Output directory for decompiled C files
        --max <N>               Maximum number of functions to decompile (default: all)
        --func-timeout <SEC>    Decompile timeout per function (seconds, default: 5000)
        --project <NAME>        Project name (default: <file>_headless)
        --projects <DIR>        Project root directory override
        --path <DIR>            Ghidra install directory override
        --java <DIR>            JAVA_HOME override
        --loader <NAME>         Explicit loader name
        --timeout <SEC>         Analysis timeout per file (seconds, default: 5000)
        --delete-project        Delete a project created by this run after analysis completes
        --overwrite             Overwrite existing file in project
        --script-path <DIR>     Override script path (default: ghidra_scripts next to rgh.exe)
```

#### `rgh ghidra verify`

```text
DESCRIPTION:
Verify decompiler output for bad-instruction placeholders

USAGE:
    rgh ghidra verify [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --dir <DIR>          Directory containing decompiled output to verify
        --pattern <REGEX>    Regex pattern to flag (default: baddata|Bad instruction)
        --ext <EXT>          File extension to scan (default: .c)
        --max <N>            Maximum matching files to display (default: 25)
        --json               Output JSON
```

### `rgh ida`

```text
DESCRIPTION:
IDA Pro headless helpers (IDA 9.3 supported; legacy 9.1 archive compatibility available, external install required)

USAGE:
    rgh ida [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ida config --path "C:\Program Files\IDA Professional 9.3" --python python
    rgh ida install-loader
    rgh ida decompile --running --out .\ida-decomp --max 25

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    config            Configure IDA install, python, and backend paths
    check             Verify the configured IDA environment
    install-loader    Download or install the supported loader set into IDA
    analyze           Import a XEX into an IDA database headlessly
    decompile         Decompile a XEX or IDA database to C
    export-symbols    Export function entry symbols from an IDA database or XEX
    verify            Verify IDA decompiler output for obvious failures
```

#### `rgh ida config`

```text
DESCRIPTION:
Configure IDA install, python, and backend paths

USAGE:
    rgh ida config [OPTIONS]

OPTIONS:
    -h, --help              Prints help information
        --path <DIR>        IDA install directory (must contain idat.exe)
        --python <EXE>      Python executable or command used for idalib helpers
        --user <DIR>        IDA user directory override for headless runs
        --backend <NAME>    Preferred backend: auto, batch, or idalib
        --clear             Clear stored IDA settings
```

#### `rgh ida check`

```text
DESCRIPTION:
Verify the configured IDA environment

USAGE:
    rgh ida check [OPTIONS]

OPTIONS:
    -h, --help              Prints help information
        --path <DIR>        IDA install directory override
        --python <EXE>      Python executable override
        --user <DIR>        IDA user directory override
        --backend <NAME>    Preferred backend override
        --json              Emit JSON
```

#### `rgh ida install-loader`

```text
DESCRIPTION:
Download or install the supported loader set into IDA

USAGE:
    rgh ida install-loader [OPTIONS]

OPTIONS:
    -h, --help              Prints help information
        --path <DIR>        IDA install directory override
        --archive <FILE>    Use a local idaxex archive. Requires --sha256; every installed payload is checked against the detected IDA build manifest
        --url <URL>         Download idaxex from an explicit URL. Requires --sha256; every installed payload is checked against the detected IDA build manifest
        --sha256 <HASH>     Required SHA256 for a custom --archive or --url source
```

#### `rgh ida analyze`

```text
DESCRIPTION:
Import a XEX into an IDA database headlessly

USAGE:
    rgh ida analyze [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --in <FILE>          Input file to analyze (XEX or existing database)
        --ftp-path <PATH>    Fetch the XEX via FTP before analysis
        --running            Use the running title XEX resolved over XBDM + FTP
        --out-db <FILE>      Output database path (.i64)
        --overwrite          Overwrite an existing database
        --path <DIR>         IDA install directory override
        --python <EXE>       Python executable override
        --user <DIR>         IDA user directory override
```

#### `rgh ida decompile`

```text
DESCRIPTION:
Decompile a XEX or IDA database to C

USAGE:
    rgh ida decompile [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --in <FILE>          Input XEX or database path
        --ftp-path <PATH>    Fetch the XEX via FTP before decompiling
        --running            Use the running title XEX resolved over XBDM + FTP
        --out <DIR>          Output directory for C files
        --max <N>            Maximum number of functions to decompile (default: all)
        --backend <NAME>     Backend: auto, batch, or idalib
        --out-db <FILE>      Database path to create or reuse for raw XEX input
        --overwrite-db       Overwrite the database when importing a raw XEX
        --keep-db            Keep the generated database even when using a temporary cache path
        --path <DIR>         IDA install directory override
        --python <EXE>       Python executable override
        --user <DIR>         IDA user directory override
```

#### `rgh ida export-symbols`

```text
DESCRIPTION:
Export function entry symbols from an IDA database or XEX

USAGE:
    rgh ida export-symbols [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --in <FILE>       Input IDA database or XEX
        --out <FILE>      Output symbol sidecar JSON file
        --path <DIR>      IDA install directory override
        --python <EXE>    Python executable override
        --user <DIR>      IDA user directory override
```

#### `rgh ida verify`

```text
DESCRIPTION:
Verify IDA decompiler output for obvious failures

USAGE:
    rgh ida verify [OPTIONS]

OPTIONS:
    -h, --help               Prints help information
        --dir <DIR>          Directory containing IDA decompiler output to verify
        --pattern <REGEX>    Regex pattern to flag (default: could not decompile|BAD)
        --ext <EXT>          File extension to scan (default: .c)
        --max <N>            Maximum matching files to display (default: 25)
        --json               Emit JSON
```
