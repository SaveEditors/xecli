# CLI Help Output

This page captures the current built-in help screens from the shipped `rgh` binary and explains how the shortcut groups map to the canonical command tree.

## How to Read the Help Tree
Use the help system in this order:

1. `rgh help`
2. `rgh <group> help`
3. `rgh <group> <command> --help`

Examples:

```powershell
rgh help
rgh modules help
rgh modules load --help
rgh mem poke --help
rgh ghidra decompile --help
```

## Root Help
Current `rgh help` output:

```text
USAGE:
    rgh [OPTIONS] <COMMAND>

EXAMPLES:
    rgh status
    rgh title
    rgh modules list
    rgh mem hexdump --addr 0x30000000 --size 0x40
    rgh ghidra decompile --running --out .\decomp

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    status          Show a compact console status snapshot
    profiles        List profiles and signed-in users
    title           Resolve the active title or look up a Title ID in the local database
    target          Show or set the default target
    ping            Ping the current console
    reboot          Reboot the console (cold by default)
    launch          Launch a XEX with optional arguments
    install         Install rgh for the current user or add it to the machine PATH
    start           Discover consoles and set the default target
    connect         Set or select the default target
    scan            Scan the network for consoles
    screenshot      Capture a live screenshot
    xbdm            XBDM commands
    modules         Shortcut for `rgh xbdm modules`
    module          Alias of `rgh modules` and `rgh xbdm modules`
    mem             Shortcut for `rgh xbdm mem`
    xex             Shortcut for `rgh xbdm xex`
    fs              Shortcut for `rgh xbdm fs`
    threads         Shortcut for `rgh xbdm threads`
    debug           Shortcut for `rgh xbdm debug`
    jrpc2           JRPC2 (XDRPC-style) commands
    notify          Send an on-screen notification
    notify-icons    Manage notify icon presets
    ftp             FTP commands (alternate access)
    save            Profile and save-data helpers over FTP
    content         Installed content management over FTP
    plugin          DashLaunch plugin management
    god             ISO to Games on Demand conversion
    ghidra          Ghidra headless helpers
```

## Shortcut Map
These shortcut groups are not separate implementations. They are direct operator-facing shortcuts for the canonical XBDM branches.

| Shortcut | Canonical form | Meaning |
| --- | --- | --- |
| `rgh modules ...` | `rgh xbdm modules ...` | Loaded module list, info, dump, load, unload, and pending verification |
| `rgh module ...` | `rgh modules ...` | Singular alias for the same module branch |
| `rgh mem ...` | `rgh xbdm mem ...` | Memory dump, hexdump, peek, poke, watch, strings, and search |
| `rgh xex ...` | `rgh xbdm xex ...` | Running-XEX dump, launch, strings, and Ghidra-backed decompile |
| `rgh fs ...` | `rgh xbdm fs ...` | XBDM file-system list, get, put, cat, remove, mkdir, and move |
| `rgh threads ...` | `rgh xbdm threads ...` | Thread list, context, suspend, and resume |
| `rgh debug ...` | `rgh xbdm debug ...` | Break, go, watch, code breakpoints, and data breakpoints |

## Alias Notes
Several commands also expose quality-of-life aliases. The important ones are:

| Primary command | Alias | Notes |
| --- | --- | --- |
| `rgh start` | `rgh s` | Quick discovery entry point |
| `rgh connect` | `rgh c` | Quick target selection |
| `rgh scan` | `rgh discover` | Explicit discovery wording |
| `rgh screenshot` | `rgh shot` | Short capture form |
| `rgh launch` | `rgh run` | XEX launch alias |
| `rgh notify` | `rgh xnotify` | Console notification alias |
| `rgh modules unload` | `rgh modules remove` | Same unload operation |
| `rgh modules pending` | `rgh modules verify` | Same pending-op verification |
| `rgh mem find` | `rgh mem search` | Same memory-search operation |
| `rgh mem peek` | `rgh mem read` | Same memory-read operation |
| `rgh mem poke` | `rgh mem write` | Same memory-write operation |
| `rgh debug watch` | `rgh debug events` | Same live-debug stream |
| `rgh debug break remove` | `rgh debug break del` | Same breakpoint removal |
| `rgh debug databreak remove` | `rgh debug databreak del` | Same data-breakpoint removal |
| `rgh ftp list` | `rgh ftp ls` | Same FTP listing |
| `rgh ftp put` | `rgh ftp push` | Same upload workflow in docs/examples |
| `rgh save inject` | `rgh save push` / `rgh save put` | Same save-upload workflow |
| `rgh xex decompile` | `rgh xex decode` | Same Ghidra-backed export |

## Representative Runtime Results
The built-in help screens tell you how to call a command. They do not show what success or failure usually looks like in terminal use. The examples below are representative operator-facing results from the shipped CLI.

### Core console checks
```text
rgh ping
OK 21 ms

rgh title
Active Title
Title ID     0xFFFE07D1
Title Name   Aurora
Media ID     unknown
```

### Live inspection
```text
rgh modules list
Modules
xboxkrnl.exe   0x80010000   0x005A0000
xam.xex        0x82000000   0x001C0000
Aurora.xex     0x90F00000   0x00480000

rgh mem hexdump --addr 0x30000000 --size 0x10
0x30000000 DE AD BE EF 01 02 03 04 05 06 07 08 09 0A 0B 0C  ................
```

### Transfer and content workflows
```text
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini
SUCCESS FTP download complete
/Hdd1/launch.ini -> .\launch.ini (12.49 KB)

rgh save extract --titleid 415608C3 --out .\saves
SUCCESS Save extract complete
3 file(s)  8.2 MB -> .\saves\Grand Theft Auto V (0x415608C3)
```

### Notifications and analysis
```text
rgh notify "XeCLI connected" 14
SUCCESS Notification sent
message="XeCLI connected" logo=14 (Flashing happy face)

rgh ghidra decompile --running --out .\decomp
SUCCESS Ghidra decompile complete
200 function files written to .\decomp
```

## Top-Level Help Screens

### `rgh status help`
```text
DESCRIPTION:
Show a compact console status snapshot

USAGE:
    rgh status [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --ip <IP>         Console IP address. If omitted, uses the last
                          connected console
        --port <PORT>     TCP port (default: 730)
        --timeout <MS>    Socket timeout in milliseconds (default: 5000)
        --json            Emit JSON output
        --quick           Skip JRPC2, drive, and user checks for a fast snapshot
        --no-jrpc         Skip JRPC2 queries (temps, CPU key, dashboard, title
                          id)
        --no-drives       Skip drive and USB size reporting
        --no-users        Skip user list and signed-in detection
```

### `rgh profiles help`
```text
DESCRIPTION:
List profiles and signed-in users

USAGE:
    rgh profiles [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --ip <IP>         Console IP address. If omitted, uses the last
                          connected console
        --port <PORT>     TCP port (default: 730)
        --timeout <MS>    Socket timeout in milliseconds (default: 5000)
        --json            Emit JSON output
        --no-ftp          Skip FTP profile scan
        --no-f3           Skip F3 profile lookup (HTTP 9999)
```

### `rgh title help`
```text
DESCRIPTION:
Resolve the active title or look up a Title ID in the local database

USAGE:
    rgh title [TITLEID] [MEDIAID] [OPTIONS]

ARGUMENTS:
    [TITLEID]    Title ID (hex, with or without 0x). Omit it or pass 'active' to
                 resolve the current title
    [MEDIAID]    Optional media ID (hex)

OPTIONS:
    -h, --help            Prints help information
        --ip <IP>         Console IP address. If omitted, uses the last
                          connected console
        --port <PORT>     TCP port (default: 730)
        --timeout <MS>    Socket timeout in milliseconds (default: 5000)
        --json            Emit JSON output
        --active          Resolve the currently active title from the connected
                          console
```

### `rgh target help`
```text
DESCRIPTION:
Show or set the default target

USAGE:
    rgh target [OPTIONS]

OPTIONS:
    -h, --help           Prints help information
        --set <IP>       Set the default console IP
        --port <PORT>    Set the default port (default: 730)
        --clear          Clear the saved target
```

### `rgh ping help`
```text
DESCRIPTION:
Ping the current console

USAGE:
    rgh ping [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --ip <IP>         Console IP address. If omitted, uses the last
                          connected console
        --port <PORT>     TCP port (default: 730)
        --timeout <MS>    Socket timeout in milliseconds (default: 5000)
        --json            Emit JSON output
```

### `rgh reboot help`
```text
DESCRIPTION:
Reboot the console (cold by default)

USAGE:
    rgh reboot [OPTIONS]

OPTIONS:
    -h, --help                  Prints help information
        --ip <IP>               Console IP address. If omitted, uses the last
                                connected console
        --port <PORT>           TCP port (default: 730)
        --timeout <MS>          Socket timeout in milliseconds (default: 5000)
        --json                  Emit JSON output
        --title                 Restart the current title instead of a cold
                                reboot
        --notify                Send a default success notification to the
                                console
        --notify-icon <NAME>    Notification icon preset name
        --notify-logo <ID>      Notification logo id (decimal or 0x hex)
```

### `rgh launch help`
```text
DESCRIPTION:
Launch a XEX with optional arguments

USAGE:
    rgh launch [XEX] [OPTIONS]

ARGUMENTS:
    [XEX]    XEX path to launch, for example Hdd1:\Aurora\Aurora.xex

OPTIONS:
    -h, --help                  Prints help information
        --ip <IP>               Console IP address. If omitted, uses the last
                                connected console
        --port <PORT>           TCP port (default: 730)
        --timeout <MS>          Socket timeout in milliseconds (default: 5000)
        --json                  Emit JSON output
        --xex <PATH>            XEX path to launch, for example
                                Hdd1:\Aurora\Aurora.xex
        --directory <DIR>       Working directory passed to XBDM. Defaults to
                                the XEX folder
        --args <TEXT>           Command-line arguments passed to the XEX
        --titleid <TITLEID>     Optional Title ID for display/logging
        --dry-run               Show the generated XBDM command without
                                executing it
        --notify                Send a default success notification to the
                                console
        --notify-icon <NAME>    Notification icon preset name
        --notify-logo <ID>      Notification logo id (decimal or 0x hex)
```

### `rgh install help`
```text
DESCRIPTION:
Install rgh for the current user or add it to the machine PATH

USAGE:
    rgh install [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --uninstall       Remove the current-user shim or, with --machine-path,
                          remove the machine PATH entry
        --machine-path    Add the rgh.exe directory to the machine PATH (admin
                          required)
        --path <DIR>      Override the rgh.exe directory (defaults to the
                          current executable folder)
        --quiet           Suppress non-error install output
```

### Discovery Group
`rgh start help`, `rgh connect help`, and `rgh scan help`:

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

`connect` changes the usage line to `rgh connect [target] [OPTIONS]` and accepts a console IP or discovery index.

### `rgh screenshot help`
```text
DESCRIPTION:
Capture a live screenshot

USAGE:
    rgh screenshot [OPTIONS]

OPTIONS:
    -h, --help                        Prints help information
        --ip <IP>                     Console IP address. If omitted, uses the
                                      last connected console
        --port <PORT>                 TCP port (default: 730)
        --timeout <MS>                Socket timeout in milliseconds (default:
                                      5000)
        --json                        Emit JSON output
        --out <FILE>                  Output file path (.bmp or .raw)
        --format <FORMAT>             Output format: bmp or raw
        --raw                         Force raw output (same as --format raw)
        --force                       Overwrite the output file if it exists
        --decode <MODE>               Force decode mode: auto, linear, tiled-v1,
                                      tiled-v2, tiled-v3, tiled-xenia
        --endianness <MODE>           Force endianness: auto, none, swap8-16,
                                      swap8-32, swap16-32
        --order <ORDER>               Force 32bpp channel order: auto, bgra,
                                      rgba, argb, abgr, bgrx, rgbx, xrgb, xbgr
        --dump-variants               Dump all decode variants to a folder next
                                      to the output file
        --crop-right <PX>             Crop N pixels from the right edge
                                      (default: 0)
        --crop-right-percent <PCT>    Crop a percentage from the right edge
                                      (0-50)
        --xenia-bank-xor <N>          Force Xenia tile bank XOR (0-1) when using
                                      tiled-xenia
        --xenia-pipe-xor <N>          Force Xenia tile pipe XOR (0-3) when using
                                      tiled-xenia
```

### `rgh xbdm help`
```text
DESCRIPTION:
XBDM commands

USAGE:
    rgh xbdm [OPTIONS] <COMMAND>

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

### `rgh modules help`
```text
DESCRIPTION:
Shortcut for `rgh xbdm modules`

USAGE:
    rgh modules [OPTIONS] <COMMAND>

EXAMPLES:
    rgh modules list
    rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List loaded modules
    info       Show module details
    dump       Dump a module from memory
    load       Load a module via kernel RPC
    unload     Unload a module via kernel RPC
    pending    Verify a pending module operation after reboot
```

### `rgh module help`
```text
DESCRIPTION:
Alias of `rgh modules` and `rgh xbdm modules`

USAGE:
    rgh module [OPTIONS] <COMMAND>

EXAMPLES:
    rgh module info --name Aurora.xex

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List loaded modules
    info       Show module details
    dump       Dump a module from memory
    load       Load a module via kernel RPC
    unload     Unload a module via kernel RPC
    pending    Verify a pending module operation after reboot
```

### `rgh mem help`
```text
DESCRIPTION:
Shortcut for `rgh xbdm mem`

USAGE:
    rgh mem [OPTIONS] <COMMAND>

EXAMPLES:
    rgh mem hexdump --addr 0x30000000 --size 0x40
    rgh mem search --addr 0x30000000 --size 0x1000 --pattern DEADBEEF

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    dump       Dump a memory range to a file
    hexdump    Hex dump a memory range
    regions    List memory regions
    peek       Read a value from memory
    poke       Write a value to memory
    watch      Stream memory changes
    strings    Extract strings from memory
    find       Search memory for a pattern
```

### `rgh xex help`
```text
DESCRIPTION:
Shortcut for `rgh xbdm xex`

USAGE:
    rgh xex [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xex dump --out .\title.xex
    rgh xex strings --running --unicode --min 6

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    dump         Dump the active XEX image
    launch       Launch a XEX with optional arguments
    strings      Extract strings from a XEX (local/FTP/running)
    decompile    Decompile a XEX to C via Ghidra
```

### `rgh fs help`
```text
DESCRIPTION:
Shortcut for `rgh xbdm fs`

USAGE:
    rgh fs [OPTIONS] <COMMAND>

EXAMPLES:
    rgh fs list --path Hdd:\

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

### `rgh threads help`
```text
DESCRIPTION:
Shortcut for `rgh xbdm threads`

USAGE:
    rgh threads [OPTIONS] <COMMAND>

EXAMPLES:
    rgh threads list
    rgh threads context --id 0xFB000008

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List threads
    context    Read thread registers
    suspend    Suspend a thread
    resume     Resume a thread
```

### `rgh debug help`
```text
DESCRIPTION:
Shortcut for `rgh xbdm debug`

USAGE:
    rgh debug [OPTIONS] <COMMAND>

EXAMPLES:
    rgh debug stop
    rgh debug watch

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    stop         Break execution
    go           Resume execution
    watch        Stream live XBDM debug notifications
    break        Code breakpoints
    databreak    Data breakpoints
```

### `rgh jrpc2 help`
```text
DESCRIPTION:
JRPC2 (XDRPC-style) commands

USAGE:
    rgh jrpc2 [OPTIONS] <COMMAND>

EXAMPLES:
    rgh jrpc2 temps
    rgh jrpc2 notify --message XeCLI

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

### `rgh notify help`
```text
DESCRIPTION:
Send an on-screen notification

USAGE:
    rgh notify [message] [OPTIONS]

ARGUMENTS:
    [message]    Notification text

OPTIONS:
    -h, --help              Prints help information
        --ip <IP>           Console IP address. If omitted, uses the last
                            connected console
        --port <PORT>       TCP port (default: 730)
        --timeout <MS>      Socket timeout in milliseconds (default: 5000)
        --json              Emit JSON output
        --message <TEXT>    Notification text
        --logo <ID>         Notification logo id (decimal or 0x hex)
        --icon <NAME>       Notification icon preset name
```

### `rgh notify-icons help`
```text
DESCRIPTION:
Manage notify icon presets

USAGE:
    rgh notify-icons [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list      List icon presets
    add       Add an icon preset
    remove    Remove an icon preset
```

### `rgh ftp help`
```text
DESCRIPTION:
FTP commands (alternate access)

USAGE:
    rgh ftp [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ftp list --path /Hdd1/
    rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    target    Show or set FTP target
    list      List files via FTP
    find      Find files via FTP
    get       Download a file via FTP
    put       Upload a file via FTP
    cat       Print a file via FTP
    rm        Delete a file via FTP
    mkdir     Create a directory via FTP
    mv        Move or rename a file via FTP
```

### `rgh save help`
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

### `rgh content help`
```text
DESCRIPTION:
Installed content management over FTP

USAGE:
    rgh content [OPTIONS] <COMMAND>

EXAMPLES:
    rgh content list --device Hdd1 --show-types

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list      List installed titles and content types
    delete    Delete installed content for a title
```

### `rgh plugin help`
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

### `rgh god help`
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

### `rgh ghidra help`
```text
DESCRIPTION:
Ghidra headless helpers

USAGE:
    rgh ghidra [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ghidra config --path C:\Tools\ghidra --java C:\Java
    rgh ghidra decompile --running --out .\decomp

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    config       Configure Ghidra paths
    analyze      Run headless analysis
    decompile    Decompile a module or XEX
    verify       Verify decompiler output for bad-instruction placeholders
```

## Where the Full Examples Live
Use this page for the real help screens. Use [Commands.md](Commands.md) for task-oriented examples and command-by-command usage patterns.
