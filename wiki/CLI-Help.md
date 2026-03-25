# CLI Help Output

This page captures the current built-in help screens from the shipped `rgh` binary and explains how the shortcut groups map to the canonical command tree.

Use this page when you need:

- the exact built-in help wording from the shipped binary
- the shortcut and alias map
- a quick reminder of what each top-level branch exposes

Use [Commands.md](Commands.md) when you need task-oriented examples and expected outputs. Use [XNotify.md](XNotify.md) when you need icon IDs, notification usage, and integration notes.

For first-time setup from the release package, start with `.\rgh.exe install` from the extracted release folder. After installation, use `rgh install` for maintenance or reinstall flows, `rgh homebrew install ...` for normal dashboard/homebrew staging, and `rgh ogxbox install ...` for Original Xbox compatibility files that belong on `HddX:\Compatibility`. The installer can copy XeCLI to a chosen folder, register `rgh`, silently scan for consoles, and offer to run `status` on a detected target.

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
    rgh notify XeCLI connected 14
    rgh smc version
    rgh fan set --speed 55 --channel both
    rgh led set --preset quadrant1
    rgh tray open
    rgh nand dump
    rgh nand dump --single
    rgh nand dump --output nand_backup.bin
    rgh xell boot
    rgh xell info
    rgh xell kv export
    rgh popup show --title XeCLI --body Connected to console --preset none
    rgh avatar games --search Black Ops
    rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 
--current-user
    rgh avatar browse --remote
    rgh homebrew install aurora --usb E:
    rgh homebrew install all --usb E: --auto-confirm
    rgh ogxbox install hacked --include-fixer --usb E:
    rgh ghidra decompile --running --out .\decomp
    rgh ida check
    rgh ida decompile --running --out .\ida-decomp

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    status          Show a compact console status snapshot                      
    profiles        List profiles and signed-in users                           
    title           Resolve the active title or look up a Title ID              
    target          Show or set the default target                              
    ping            Ping the current console                                    
    reboot          Reboot the console (cold by default)                        
    shutdown        Power off the console                                       
    launch          Launch a XEX with optional arguments                        
    install         Launch the XeCLI installer                                  
    start           Discover consoles and set the default target                
    connect         Set or select the default target                            
    scan            Scan the network for consoles                               
    screenshot      Capture a live screenshot                                   
    homebrew        Download public homebrew packages to USB, a folder, or a    
                    detected console drive                                      
    ogxbox          Original Xbox compatibility pack installer for              
                    HddX:\Compatibility                                         
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
    notify-icons    Browse XNotify icons and manage preset aliases              
    smc             System Management Controller helpers                        
    fan             Fan speed helpers                                           
    led             Ring-of-light LED helpers                                   
    signin          Signed-in user helpers                                      
    tray            Disc tray helpers                                           
    xell            XeLL Reloaded helpers with guided auto-launch when needed   
    nand            XeLL-backed NAND dumping and verification                   
    popup           Trainer-style console popup helpers                         
    spoof           Private title-aware spoof helpers. BO2 supports local GT,   
                    local XUID, and remote spoofing                             
    ftp             FTP commands (alternate access)                             
    save            Profile and save-data helpers over FTP                      
    content         Installed content management over FTP                       
    avatar          Avatar item library and install helpers                     
    plugin          DashLaunch plugin management                                
    god             ISO to Games on Demand conversion                           
    ghidra          Ghidra headless helpers                                     
    ida             IDA headless helpers                                        
```

Avatar browsing in the shipped build now has both a terminal path and a Windows picker. Use `rgh avatar choose` for terminal selection, `rgh avatar browse` for the Windows picker, and `rgh avatar install` for direct one-item or full-title installs. Add `--remote` to use the hosted `Avatar-Item-Collection` repo with local caching and progress bars.

Original Xbox compatibility is intentionally separate from normal homebrew installs. Use `rgh ogxbox install hacked|hud|retail` for the three public XeFu sets, and add `--include-fixer` when you also want the HDD Compatibility Partition Fixer staged.

`rgh xell ...` and `rgh nand ...` are native XeCLI branches. They prompt before the first automatic dashboard-to-XeLL transition, attach to XeLL's HTTP service once it comes up, and keep all backup operations read-only.

## Fatman
Fatman is XeCLI's read-only FATX manager for local Xbox 360 disks and images. The current release cut focuses on image recovery, partition dumping, inspection, search, and export, not on write, mount, format, or repair operations.

Current command surface:

```text
rgh fatman devices
rgh fatman partitions
rgh fatman scan
rgh fatman info
rgh fatman list
rgh fatman find
rgh fatman cat
rgh fatman get
rgh fatman extract
rgh fatman dump
```

`rgh fatx` is the documented alias for the same command group.

Manual-open workflow:

- use `--offset` to open a FATX/XTAF volume directly from a byte offset
- use `--length` with `--offset` when you want to clamp the manual partition size
- both values accept decimal bytes or `0x`-prefixed hex
- use `rgh fatman scan` first when you need candidate offsets for a nonstandard image

```powershell
rgh fatman scan --image .\Unknown.img
rgh fatman info --image .\Unknown.img --offset 0xB6600000
rgh fatman list --image .\Unknown.img --offset 0xB6600000 --path /
rgh fatman dump --image .\Unknown.img --offset 0xB6600000 --length 0x10000000 --out .\partition-dump
```

What it does:

- detect FATX-capable disks or image sources
- inspect partitions and volume details
- browse directories and locate entries
- search by name or path
- print small files in the terminal
- export selected files or directory trees to the host
- recover data from `.img` and `.bin` sources

Validation note:

- the runtime has been verified against a synthetic FATX fixture image
- manual-open and scan flows were also validated against a nonstandard AMPED HDD image, where Fatman surfaced real `XTAF` offsets and opened the compatibility volume by bounded offset

What it does not do in the first cut:

- write files back to FATX volumes
- format or repartition disks
- mount a virtual filesystem
- repair damaged volumes
- modify security sectors or low-level disk metadata

## Shortcut Map
These shortcut groups are not separate implementations. They are direct shortcuts for the canonical XBDM branches.

| Shortcut | Canonical form | Meaning |
| --- | --- | --- |
| `rgh modules ...` | `rgh xbdm modules ...` | Loaded module list, info, dump, load, unload, and pending verification |
| `rgh module ...` | `rgh modules ...` | Singular alias for the same module branch |
| `rgh mem ...` | `rgh xbdm mem ...` | Memory dump, hexdump, peek, poke, watch, strings, and search |
| `rgh xex ...` | `rgh xbdm xex ...` | Running-XEX dump, launch, strings, and Ghidra/IDA-backed decompile |
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
| `rgh avatar install` | `rgh avatar apply` | Same avatar patch-and-install workflow |
| `rgh xex decompile` | `rgh xex decode` | Same Ghidra-backed export |
| `rgh xex ida-decompile` | `rgh xbdm xex ida-decompile` | Same IDA-backed export |

## Representative Runtime Results
The built-in help screens tell you how to call a command. They do not show what success or failure usually looks like in terminal use. The examples below are representative results from the shipped CLI.

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

rgh signin state
Signed In   Yes
State       Signed in locally
Gamertag    ExampleUser
XUID        0x<xuid>

rgh led set --preset quadrant1
SUCCESS Ring light updated
TL=green, TR=off, BL=off, BR=off

rgh fan set --speed 50 --channel both
SUCCESS Fan command sent
50% requested for both

rgh tray open
SUCCESS Disc tray opened

rgh popup show --title "XeCLI" --body "Connected" --preset question
SUCCESS Popup requested
Title="XeCLI" Preset=question Buttons=1

rgh avatar choose --remote --titleid 58410A5D --all --current-user --overwrite
Avatar download 3 item(s) file 2/3 | Destination Arcade - 0000020800069131C14650A158410A5D: 21%
Current user: ExampleUser | XUID: 0x<xuid> | State: Signed in to Xbox Live
SUCCESS Avatar install complete
3 item(s)  512 KB -> /Hdd1/Content/0000000000000000/58410A5D/0000020800060102C383304058410A5D via FTP

rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user
Current user: ExampleUser | XUID: 0x<xuid> | State: Signed in to Xbox Live
SUCCESS Avatar install complete
1 item(s)  116 KB -> /Hdd1/Content/0000000000000000/415608C3/00009000/000000080DF3B242CAE65A52415608C3 via XBDM

rgh avatar install --contentid 0000020800060102C383304058410A5D --current-user
Current user: ExampleUser | XUID: 0x<xuid> | State: Signed in to Xbox Live
SUCCESS Avatar install complete
1 item(s)  112 KB -> /Hdd1/Content/0000000000000000/58410A5D/0000020800060102C383304058410A5D via FTP

rgh spoof gt
Game            Call of Duty: Black Ops II
Gamertag        ExampleTag
Address         0x841E1B30

gamertag remains a compatibility alias for `gt`.

rgh spoof xuid
Game            Call of Duty: Black Ops II
XUID            <xuid>
Stored          <stored-xuid>

rgh spoof xuid set --value <xuid>
SUCCESS XUID spoof applied
Game            Call of Duty: Black Ops II
XUID            <xuid>
Stored          <stored-xuid>

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

### `rgh avatar help`
```text
DESCRIPTION:
Avatar item library and install helpers

USAGE:
    rgh avatar [OPTIONS] <COMMAND>

EXAMPLES:
    rgh avatar library show
    rgh avatar games --search Black Ops
    rgh avatar items --titleid 415608C3 --limit 10
    rgh avatar choose --search Black Ops
    rgh avatar browse --remote
    rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 
--current-user
    rgh avatar apply --titleid 415608C3 --all --current-user

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    library    Show or set avatar collection paths
    games      List games with available avatar items
    items      List avatar items from the collection
    choose     Interactively choose a game and avatar items in the terminal
    browse     Browse avatar items in a Windows picker and install selected
               entries
    install    Patch avatar items for a user and install them to the console
```

### `rgh avatar choose help`
```text
DESCRIPTION:
Interactively choose a game and avatar items in the terminal

USAGE:
    rgh avatar choose [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --remote                    Use the hosted avatar library instead of the local corpus
        --titleid <TITLEID>         Install all items for one title when paired with --all
        --all                       Install all items for the selected title
        --current-user              Use the current signed-in user explicitly
        --search <TEXT>             Initial item search text
        --game <TEXT>               Initial game/title filter text
```

### `rgh avatar browse help`
```text
DESCRIPTION:
Browse avatar items in a Windows picker and install selected entries

USAGE:
    rgh avatar browse [OPTIONS]

OPTIONS:
    -h, --help                      Prints help information
        --remote                    Use the hosted avatar library instead of the local corpus
        --titleid <TITLEID>         Preload one title or install all items for that title when paired with --all
        --search <TEXT>             Initial item search text
        --game <TEXT>               Initial game/title filter text
        --tag <TEXT>                Initial derived tag filter
        --current-user              Use the current signed-in user explicitly
```

### `rgh avatar install help`
```text
DESCRIPTION:
Patch avatar items for a user and install them to the console

USAGE:
    rgh avatar install [OPTIONS]

OPTIONS:
    -h, --help                     Prints help information
        --ip <IP>                  Console IP address. If omitted, uses the last
                                   connected console
        --port <PORT>              FTP port (default: 21)
        --user <USER>              FTP username (default: xboxftp)
        --pass <PASS>              FTP password (default: xboxftp)
        --timeout <MS>             FTP timeout in milliseconds (default: 5000)
        --json                     Emit JSON output
        --library <DIR>            Avatar item library root. Defaults to config
                                   or Avatar-Item-Collection
        --cache <PATH>             Avatar index cache file or directory
        --titleid <TITLEID>        Install all items for one title when paired
                                   with --all
        --contentid <CONTENTID>    Install one specific avatar item
        --all                      Install all items for the selected title
        --device <ROOT>            Console storage root (default: Hdd1)
        --xuid <XUID>              Explicit target XUID. Defaults to the current
                                   signed-in user
        --gamertag <NAME>          Label shown in local output when --xuid is
                                   provided
        --current-user             Use the current signed-in user explicitly
        --xbdm-port <PORT>         XBDM port used for current-user resolution
                                   (default: saved target port or 730)
        --working <DIR>            Working directory for patched temporary files
        --overwrite                Overwrite remote files that already exist
        --dry-run                  Show the install plan without uploading
                                   anything
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
Launch the XeCLI installer

USAGE:
    rgh install [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --machine         Install for all users (administrator approval
                          required)
        --uninstall       Remove the command registration. With --machine,
                          remove the all-users PATH entry
        --machine-path    Legacy path-only install for the current executable
                          directory (admin required)
        --path <DIR>      Install directory for XeCLI
        --source <DIR>    Source release directory containing rgh.exe and its
                          runtime files
        --no-path         Do not add the install directory to PATH
        --quiet           Suppress non-error install output
```

### `rgh homebrew help`
```text
DESCRIPTION:
Download public homebrew packages to USB, a folder, or the console

USAGE:
    rgh homebrew [OPTIONS] <COMMAND>

EXAMPLES:
    rgh homebrew list
    rgh homebrew install aurora --usb E:
    rgh homebrew install xm360 --usb E:
    rgh homebrew install all --usb E: --auto-confirm

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List the built-in package catalog
    install    Download one or more public homebrew packages to USB, a folder, or the console
```

### `rgh homebrew install help`
```text
DESCRIPTION:
Download one or more public homebrew packages to USB, a folder, or the console

USAGE:
    rgh homebrew install <PACKAGE> [OPTIONS]

ARGUMENTS:
    <PACKAGE>    Package to stage: aurora, dashlaunch, xexmenu, fsd, xm360,
                 timefixer, simple360, xelllaunch, or all

OPTIONS:
    -h, --help            Prints help information
        --usb <TARGET>    USB drive, drive letter, selection number, or folder
                          path
        --cache <DIR>     Package download cache directory
        --force-download  Redownload archives even when they already exist in
                          the cache
        --auto-confirm    Skip confirmation prompts
        --json            Emit machine-readable output
```

### `rgh ogxbox help`
```text
DESCRIPTION:
Original Xbox compatibility pack installer for HddX:\Compatibility

USAGE:
    rgh ogxbox [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ogxbox list
    rgh ogxbox install hacked --usb E:
    rgh ogxbox install hud --include-fixer

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list       List the built-in XeFu compatibility sets and partition fixer
    install    Stage or install an Original Xbox compatibility set
```

### `rgh ogxbox install help`
```text
DESCRIPTION:
Stage or install an Original Xbox compatibility set

USAGE:
    rgh ogxbox install <SET> [OPTIONS]

ARGUMENTS:
    <SET>    Compatibility set to install: hacked, hud, or retail

OPTIONS:
    -h, --help             Prints help information
        --ip <IP>          Console IP address. If omitted, uses the last target
        --port <PORT>      FTP port (default: 21)
        --user <USER>      FTP username (default: xboxftp)
        --pass <PASS>      FTP password (default: xboxftp)
        --timeout <MS>     FTP timeout in milliseconds
        --usb <TARGET>     Stage to a removable USB drive or folder instead of
                           installing directly to the console
        --include-fixer    Also stage or install HDD Compatibility Partition Fixer
        --cache <DIR>      Download cache directory
        --force-download   Redownload archives even when cached
        --auto-confirm     Skip confirmation prompts
        --json             Emit machine-readable output
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

The interactive installer uses a quieter discovery pass after setup. If a console is found, it can ask whether to connect now and then run `rgh status` on the selected target.

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
    rgh xex ida-decompile --running --out .\ida-decomp --max 10

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    dump         Dump the active XEX image
    launch       Launch a XEX with optional arguments
    strings      Extract strings from a XEX (local/FTP/running)
    decompile    Decompile a XEX to C via Ghidra
    ida-decompile    Decompile a XEX to C via IDA Pro
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
Send an on-screen notification with a raw icon id, built-in icon name, or preset alias

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
Browse the built-in XNotify icon catalog and manage preset aliases

USAGE:
    rgh notify-icons [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    list      List built-in XNotify icons and preset aliases
    show      Resolve an icon id, built-in name, or preset alias
    add       Add an icon preset
    remove    Remove an icon preset
```

### `rgh smc help`
```text
DESCRIPTION:
System Management Controller helpers

USAGE:
    rgh smc [OPTIONS] <COMMAND>

EXAMPLES:
    rgh smc version

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    version    Read the console SMC version
```

### `rgh fan help`
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
    set     Apply a manual fan speed
    show    Show the last XeCLI-applied manual fan setting
```

### `rgh led help`
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

### `rgh signin help`
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

### `rgh tray help`
```text
DESCRIPTION:
Disc tray helpers

USAGE:
    rgh tray [OPTIONS] <COMMAND>

EXAMPLES:
    rgh tray open
    rgh tray close

COMMANDS:
    open     Open the disc tray
    close    Close the disc tray
```

### `rgh popup help`
```text
DESCRIPTION:
Trainer-style console popup helpers

USAGE:
    rgh popup [OPTIONS] <COMMAND>

EXAMPLES:
    rgh popup show --title XeCLI --body Connected to console

COMMANDS:
    show    Show a native Xbox 360 popup
```

### `rgh spoof help`
```text
DESCRIPTION:
Private title-aware spoof helpers

USAGE:
    rgh spoof [OPTIONS] <COMMAND>

COMMANDS:
    gt        Spoof the current in-game gamertag for supported titles
    xuid      Spoof the current in-game XUID for supported titles
    remote    Remote-player text spoofing for supported titles
    reset     Restore the in-game identity from the signed-in user or explicit values
```

BO2 remote spoof slots are documented as 2-12 in the wiki because the first lobby slot is reserved in the current supported profile.

Current BO2 behavior is intentionally split:
- `rgh spoof gt` is stable and keeps BO2's local name and XUID state aligned.
- `rgh spoof remote` is stable for BO2 slots 2-12.
- `rgh spoof xuid show` can read BO2's visible XUID surfaces.
- `rgh spoof xuid set` exits safely on BO2 because the deeper account/profile spoof path is still disabled.

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
Ghidra headless helpers (Free, external install required)

USAGE:
    rgh ghidra [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ghidra config --path C:\Tools\ghidra --java C:\Java
    rgh ghidra install-loader
    rgh ghidra decompile --running --out .\decomp

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    config       Configure Ghidra paths
    install-loader    Download or install XEXLoaderWV into the configured Ghidra install
    analyze      Run headless analysis
    decompile    Decompile a module or XEX
    verify       Verify decompiler output for bad-instruction placeholders
```

### `rgh ida help`
```text
DESCRIPTION:
IDA Pro headless helpers (IDA Pro 9.1.250226 required, external install required)

USAGE:
    rgh ida [OPTIONS] <COMMAND>

EXAMPLES:
    rgh ida config --path C:\Program Files\IDA Professional 9.1 --python python
    rgh ida install-loader
    rgh ida decompile --running --out .\ida-decomp --max 25

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    config            Configure IDA install, python, and backend paths
    check             Verify the configured IDA 9.1 + idaxex 0.42b environment
    install-loader    Download or install the supported idaxex 0.42b loader set into IDA
    analyze           Import a XEX into an IDA database headlessly
    decompile         Decompile a XEX or IDA database to C
    verify            Verify IDA decompiler output for obvious failures
```

### `rgh xell help`
```text
DESCRIPTION:
XeLL Reloaded helpers with guided auto-launch when needed

USAGE:
    rgh xell [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xell boot
    rgh xell info
    rgh xell kv export
    rgh xell kv export --raw

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    boot    Launch XeLL Reloaded, or confirm that XeLL is already running
    info    Inspect the available XeLL HTTP services, endpoints, and detected
            CPU key
    kv      XeLL keyvault export helpers
```

### `rgh xell kv help`
```text
DESCRIPTION:
XeLL keyvault export helpers

USAGE:
    rgh xell kv [OPTIONS] <COMMAND>

EXAMPLES:
    rgh xell kv export
    rgh xell kv export --output kv_backup.bin
    rgh xell kv export --raw

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    export    Export the keyvault, CPU key text, and a packaged verified backup
              set
```

### `rgh nand help`
```text
DESCRIPTION:
XeLL-backed NAND dumping and verification

USAGE:
    rgh nand [OPTIONS] <COMMAND>

EXAMPLES:
    rgh nand dump
    rgh nand dump --single
    rgh nand dump --output nand_backup.bin
    rgh nand dump --force-xell

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    dump    Boot XeLL Reloaded, download the flash dump, verify repeated dumps,
            and package a safe backup
```

### `rgh xell boot help`
```text
DESCRIPTION:
Launch XeLL Reloaded, or confirm that XeLL is already running

USAGE:
    rgh xell boot [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --ip <IP>         Console IP address. If omitted, uses the last
                          connected console
        --port <PORT>     TCP port (default: 730)
        --timeout <MS>    Socket timeout in milliseconds (default: 5000)
        --json            Emit JSON output
        --force-xell      Force the direct XeLL reboot path instead of the
                          XellLaunch shortcut when available
```

### `rgh xell info help`
```text
DESCRIPTION:
Inspect the available XeLL HTTP services, endpoints, and detected CPU key

USAGE:
    rgh xell info [OPTIONS]

OPTIONS:
    -h, --help            Prints help information
        --ip <IP>         Console IP address. If omitted, uses the last
                          connected console
        --port <PORT>     TCP port (default: 730)
        --timeout <MS>    Socket timeout in milliseconds (default: 5000)
        --json            Emit JSON output
        --force-xell      Force the direct XeLL reboot path instead of the
                          XellLaunch shortcut when available
```

### `rgh xell kv export help`
```text
DESCRIPTION:
Export the keyvault, CPU key text, and a packaged verified backup set

USAGE:
    rgh xell kv export [OPTIONS]

OPTIONS:
    -h, --help             Prints help information
        --ip <IP>          Console IP address. If omitted, uses the last
                           connected console
        --port <PORT>      TCP port (default: 730)
        --timeout <MS>     Socket timeout in milliseconds (default: 5000)
        --json             Emit JSON output
        --force-xell       Force the direct XeLL reboot path instead of the
                           XellLaunch shortcut when available
        --output <FILE>    Output keyvault filename. Defaults to
                           kv_yyyyMMdd_HHmmss.bin
        --raw              Export the raw keyvault block instead of the
                           decrypted KV
```

### `rgh nand dump help`
```text
DESCRIPTION:
Boot XeLL Reloaded, download the flash dump, verify repeated dumps, and package
a safe backup

USAGE:
    rgh nand dump [OPTIONS]

OPTIONS:
    -h, --help             Prints help information
        --ip <IP>          Console IP address. If omitted, uses the last
                           connected console
        --port <PORT>      TCP port (default: 730)
        --timeout <MS>     Socket timeout in milliseconds (default: 5000)
        --json             Emit JSON output
        --force-xell       Force the direct XeLL reboot path instead of the
                           XellLaunch shortcut when available
        --single           Take one dump only and skip byte-for-byte
                           verification
        --output <FILE>    Output NAND filename. Defaults to
                           nand_backup_yyyyMMdd_HHmmss.bin
        --no-verify        Skip the second-dump verification loop
```

## Where the Full Examples Live
Use this page for the real help screens. Use [Commands.md](Commands.md) for task-oriented examples and command-by-command usage patterns.
