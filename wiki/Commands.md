# Commands Reference

This page documents the XeCLI command surface by workflow area. Command examples use the installed terminal command `rgh`.

The `Example output` blocks on this page are representative results from the shipped CLI. Exact spacing, colors, and secondary detail lines can vary by flags, console state, and whether JSON mode is enabled.

## Find by Workflow
Use this table when you know the job you need done but not the exact command namespace yet.

| Workflow | Start here |
| --- | --- |
| Check if the console is up | `rgh ping`, `rgh status`, `rgh title` |
| Find and connect to a console | `rgh start`, `rgh scan`, `rgh connect`, `rgh target` |
| Read or dump live memory | `rgh mem dump`, `rgh mem hexdump`, `rgh mem peek` |
| Change live memory | `rgh mem poke`, `rgh mem search --freeze` |
| Work with loaded modules | `rgh modules list`, `rgh modules info`, `rgh modules dump`, `rgh modules load`, `rgh modules unload` |
| Inspect threads or break execution | `rgh threads ...`, `rgh debug ...` |
| Pull files from the console | `rgh fs get`, `rgh ftp get`, `rgh xex dump`, `rgh screenshot` |
| Push files to the console | `rgh fs put`, `rgh ftp put`, `rgh save inject`, `rgh plugin enable` |
| Manage saves | `rgh save list`, `rgh save extract`, `rgh save inject` |
| Manage title content or DashLaunch plugins | `rgh content ...`, `rgh plugin ...` |
| Browse or install avatar items from the local or hosted collection | `rgh avatar library ...`, `rgh avatar games`, `rgh avatar items`, `rgh avatar choose`, `rgh avatar browse`, `rgh avatar install` |
| Send visible console messages | `rgh notify`, `rgh notify-icons`, `rgh jrpc2 notify` |
| Analyze XEX files | `rgh xex strings`, `rgh xex decompile`, `rgh ghidra ...` |
| Convert retail ISOs | `rgh god info`, `rgh god build`, `rgh god watch` |

## Command Model
XeCLI is organized into a few major namespaces:

- Core commands: status, title, targeting, launch, reboot
- XBDM-backed commands: modules, memory, threads, debug, screenshot, file system
- JRPC2-backed commands: CPU key, temps, Title ID, dashboard, notifications, generic RPC
- FTP-backed commands: file access, saves, content, and plugin management
- Analysis commands: XEX, Ghidra, metadata
- Packaging commands: Games on Demand conversion and watchdog mode

Shortcut equivalence:

- `rgh modules` = `rgh xbdm modules`
- `rgh module` = alias of `rgh modules`
- `rgh mem` = `rgh xbdm mem`
- `rgh xex` = `rgh xbdm xex`
- `rgh fs` = `rgh xbdm fs`
- `rgh threads` = `rgh xbdm threads`
- `rgh debug` = `rgh xbdm debug`

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
- `rgh module remove` = `rgh modules unload`
- `rgh module verify` = `rgh modules pending`
- `rgh avatar apply` = `rgh avatar install`

For the exact top-level help screen and the exact help output of every top-level command group, see [CLI-Help.md](CLI-Help.md).

## Common Options
### XBDM-backed commands
| Option | Description |
| --- | --- |
| `--ip <IP>` | Console IP. Uses the saved target if omitted. |
| `--port <PORT>` | XBDM TCP port. Default: `730`. |
| `--timeout <MS>` | Socket timeout in milliseconds. |
| `--json` | Emit JSON output when supported. |

### FTP-backed commands
| Option | Description |
| --- | --- |
| `--ip <IP>` | Console IP. Uses the saved target if omitted. |
| `--port <PORT>` | FTP port. Default: `21`. |
| `--user <USER>` | FTP username. Default: `xboxftp`. |
| `--pass <PASS>` | FTP password. Default: `xboxftp`. |
| `--timeout <MS>` | FTP timeout in milliseconds. |
| `--json` | Emit JSON output when supported. |

### Discovery commands
| Option | Description |
| --- | --- |
| `--ports <PORTS>` | Comma-separated TCP ports to probe. Default: `730,731`. |
| `--timeout <MS>` | Discovery timeout. Default: `400`. |
| `--no-nap` | Disable NAP broadcast discovery. |
| `--no-tcp` | Disable TCP scan discovery. |
| `--json` | Emit discovery results as JSON. |

## Core Commands
### `rgh status`
Console summary view with XBDM identity, running XEX, JRPC-backed fields, drive layout, and sign-in information.

```powershell
rgh status
rgh status --quick
rgh status --json
rgh status --no-drives --no-users
```

Notes:

- `--quick` skips slower JRPC, drive, and sign-in probes.
- Skipped fields are rendered as skipped instead of unknown.
- When a pending module load or unload is being tracked across a reboot, `status` can surface that state.

Example output:

```text
Status
IP               192.168.1.186
Execution State  start
Title ID         0xFFFE07D1
Title Name       Aurora
Signed In        Yes
Gamertag         Diamond KSG
```

### `rgh profiles`
Enumerates sign-in and profile information gathered from XBDM, JRPC/XAM, FTP, and F3 when available.

```powershell
rgh profiles
rgh profiles --json
rgh profiles --no-ftp
rgh profiles --no-f3
```

Example output:

```text
Signed-In Users
User 0   Diamond KSG   state=SignedInToLive   xuid=0x5D83300C00000900

Profiles (FTP)
72 profile containers found across Hdd1 and Usb devices
```

### `rgh title`
Resolve the active title or look up a specific Title ID from the bundled database.

```powershell
rgh title
rgh title --json
rgh title 415608C3
rgh title 415608C3 2B7302D6
rgh title active
```

Example output:

```text
Active Title
Title ID     0xFFFE07D1
Title Name   Aurora
Media ID     unknown
Source       running xex path + bundled database
```

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
IP      192.168.1.186
Port    730
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
Cold reboot or title reboot.

```powershell
rgh reboot
rgh reboot --title
rgh reboot --notify
```

Example output:

```text
SUCCESS Reboot requested
cold reboot command sent to console
```

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
Launch a XEX with optional directory, arguments, title hint, and notification.

```powershell
rgh launch Hdd1:\Aurora\Aurora.xex
rgh launch Hdd1:\Aurora\Aurora.xex --titleid FFFE07D1
rgh launch --xex Hdd1:\Aurora\Aurora.xex --args "debug=1"
rgh launch --xex Hdd1:\Aurora\Aurora.xex --dry-run
```

Example output:

```text
SUCCESS Launch requested
Hdd1:\Aurora\Aurora.xex
Target title: Aurora (0xFFFE07D1)
Arguments: debug=1
```

### `rgh install`
`rgh install` is the XeCLI installer. It chooses an install location and optionally registers `rgh` for new terminals.

For a first-time install from the release package, run it from the extracted folder as:

```powershell
.\rgh.exe install
```

```powershell
rgh install
rgh install --path C:\Tools\XeCLI
rgh install --machine
rgh install --uninstall
```

Example output:

```text
XeCLI Installer
1. Current user
2. All users
Choose an install scope [1]:
Install directory: C:\Users\You\AppData\Local\Programs\XeCLI
Add rgh to PATH for new terminals? [Y/n]:
Continue with installation? [Y/n]:

Created by Pew - Se7ensins
SUCCESS Install complete
Scope              Current user
Install Directory  C:\Users\You\AppData\Local\Programs\XeCLI
Command            rgh
PATH               User PATH updated
Next Step          Open a new terminal and run rgh --help.
Jtag at 192.168.1.186 was detected, would you like to connect now? [y/N]:
```

For dashboard and homebrew package staging, use `rgh homebrew install ...`. That keeps the installer flow and the USB package workflow separate in both help and docs.

### `rgh homebrew install`
Download one or more public homebrew packages onto a USB drive, staging folder, or detected console drive.

```powershell
rgh homebrew list
rgh homebrew install aurora --usb E:
rgh homebrew install all --usb E: --auto-confirm
rgh homebrew install aurora --device Hdd1 --ini-mode merge
rgh homebrew install all --device Hdd1 --ini-mode generated --auto-confirm
rgh homebrew install dashlaunch --usb A:\UsbStage --force-download
```

Example output:

```text
SUCCESS Homebrew install complete
4 package(s) 326 MB -> E:\

Package            Installed To         Files   Size
Aurora 0.7b.2      E:\Aurora            167     38.69 MB
DashLaunch 3.21    E:\DashLaunch          8      2.52 MB
XeXMenu 1.2        E:\XeXMenu             1    203.37 MB
Freestyle Dash 3   E:\FreestyleDash      26     81.43 MB

Generated E:\launch.ini
Copied bundled console plugins into E:\Plugins
```

Console install example output:

```text
SUCCESS Console homebrew install complete
1 package(s) 2.52 MB -> Hdd1 on 192.168.1.186

Package            Console Path       Files   Size
DashLaunch 3.21    /Hdd1/DashLaunch   8       2.52 MB

Bundled plugins copied to /Hdd1/Plugins
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
- `--auto-confirm` skips the confirmation prompt before staging
- `--json` emits machine-readable package install output

## Discovery Commands
### `rgh start`
rgh start is the manual version of the discovery flow that the installer can launch immediately after setup.

```powershell
rgh start
rgh start --json
```

Example output:

```text
Detected Consoles
#1  192.168.1.186  Jtag  0037XXXXXXXX190
Default target updated to 192.168.1.186
```

### `rgh connect`
rgh connect is the manual follow-up when you already know the discovery result you want to target.

```powershell
rgh connect 1
rgh connect <console-ip>
```

Example output:

```text
SUCCESS Target updated
192.168.1.186:730
```

### `rgh scan`
rgh scan is the discovery-only path. The installer uses a quieter variant of this flow after setup and only prompts when a console is found.

```powershell
rgh scan
rgh scan --json
```

Example output:

```text
Detected Consoles
#1  192.168.1.186  Jtag  0037XXXXXXXX190  tcp+nap
```

## Screenshot Commands
### `rgh screenshot`
```powershell
rgh screenshot --out .\screen.bmp
rgh screenshot --out .\screen.bmp --force
```

The command emits decoded frame-buffer metadata after a successful capture.

Example output:

```text
SUCCESS Screenshot captured
1024x576  pitch=4096  format=0x00000012
.\screen.bmp
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
Flavor        Natelx did, of course
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
rgh xbdm screenshot --out .\screen.bmp
```

Example output:

```text
SUCCESS Screenshot captured
.\screen.bmp
```

## Module Commands
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
- `--skip-mark` disables the sysdll unload marker write at `handle+0x40`
- `--dry-run` resolves the target without modifying memory

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
### `rgh mem dump`
```powershell
rgh mem dump --addr 0x82000000 --size 0x20000 --out .\mem.bin
```

Example output:

```text
SUCCESS Memory dump complete
0x82000000 length=0x00020000 -> .\mem.bin
```

### `rgh mem hexdump`
```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
```

Example output:

```text
0x30000000 DE AD BE EF 01 02 03 04 05 06 07 08 09 0A 0B 0C  ................
0x30000010 10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1D 1E 1F  ................
```

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
```

Example output:

```text
0x82000000 = 0x12345678 (u32)
```

### `rgh mem poke`
```powershell
rgh mem poke --addr 0x82000000 --type u32 --value 0x12345678
rgh mem poke --addr 0x82000000 --type float --value 1337
rgh mem poke --addr 0x82000000 --type string --value "XeCLI"
```

Type aliases include:

- `u8`, `u16`, `u32`, `u64`
- `s8`, `s16`, `s32`, `s64`
- `f32`, `f64`
- `byte`, `int`, `uint`, `float`
- `ascii`, `string`, `hex`, `bytes`

Example output:

```text
SUCCESS Memory write complete
0x82000000 <= 0x12345678 (u32)
```

### `rgh mem watch`
```powershell
rgh mem watch --addr 0x82000000 --size 0x40
rgh mem watch --addr 0x82000000 --size 0x40 --interval 100 --count 10
```

Example output:

```text
watch 1  0x82000000  78 56 34 12 ...
watch 2  0x82000000  79 56 34 12 ...
```

### `rgh mem strings`
```powershell
rgh mem strings --addr 0x82000000 --size 0x20000 --min 6
rgh mem strings --addr 0x82000000 --size 0x20000 --json
```

Example output:

```text
0x82014020  ascii    xam.xex
0x82014210  ascii    XeKeysExecute
```

### `rgh mem search`
Alias of `rgh mem find`.

```powershell
rgh mem search --addr 0x30000000 --size 0x1000 --pattern DEADBEEF
rgh mem search --addr 0x82000000 --size 0x20000 --ascii "xam.xex"
rgh mem search --addr 0x82000000 --size 0x20000 --pattern 00000000 --out .\hits.json
```

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

## XEX Commands
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
rgh xex strings --running --unicode --min 6
rgh xex strings --ftp-path /Hdd1/Aurora/Aurora.xex --out .\strings.txt
rgh xex strings --in .\title.xex --json
```

Example output:

```text
XEX Strings
0x00000000  ascii    XEX2
0x0005B8A0  utf16le  Aurora
```

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
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini
rgh ftp put --path /Hdd1/launch.ini --in .\launch.ini
rgh ftp cat --path /Hdd1/launch.ini
rgh ftp rm --path /Hdd1/temp/old.txt
rgh ftp mkdir --path /Hdd1/newdir
rgh ftp mv --from /Hdd1/old.txt --to /Hdd1/new.txt
```

Example output:

```text
SUCCESS FTP upload complete
.\launch.ini -> /Hdd1/launch.ini (12.49 KB)
```

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
0xFB000008  priority=100  state=Running
0xFB000009  priority=100  state=Waiting
```

### Debug control
```powershell
rgh debug stop
rgh debug go
rgh debug watch
```

Example output:

```text
execution stopped
execution started
```

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

### Data breakpoints
```powershell
rgh debug databreak add --addr 0x82100000 --size 4 --type write
rgh debug databreak remove --addr 0x82100000 --size 4 --type write
```

Example output:

```text
SUCCESS Data breakpoint added
addr=0x82100000 size=4 type=write
```

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

For the full XNotify explanation, icon IDs, direct numeric usage, and integration notes, see [XNotify.md](XNotify.md).

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
Gamertag    Diamond KSG
XUID        0x5D83300C00000900
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
50% requested for both
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

Read [Hardware-and-System.md](Hardware-and-System.md) for the deeper operational notes around sign-in state, LED presets, fan command behavior, and SMC version availability.

### `rgh tray open` / `rgh tray close`
Open or close the physical disc tray through the JRPC/XAM path.

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
rgh popup show --title "Raw Style" --body "Testing" --style 3
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

### `rgh spoof gamertag`
Reads or writes the current in-game gamertag for supported titles. The simple form is positional.

```powershell
rgh spoof gamertag
rgh spoof gamertag ExampleTag
rgh spoof gt set --value ExampleTag --notify
```

Example output:

```text
Game            Call of Duty: Black Ops II
Gamertag        Diamond KSG
Address         0x841E1B30
```

Write example:

```text
SUCCESS Gamertag spoof applied
XeCliTmp
```

### `rgh spoof xuid`
Reads or writes the current in-game XUID for supported titles.

```powershell
rgh spoof xuid
rgh spoof xuid 5D83300C00000900
rgh spoof xuid set --value 5D83300C00000900
```

Example output:

```text
Game            Call of Duty: Black Ops II
XUID            5D83300C00000900
Stored          000900000C30835D
Binary Addr     0x841E1B50
Text Addr       0x841E1B58
```

### `rgh spoof remote`
Overwrites remote-player text slots for supported titles. The simple positional form applies the same text to every supported slot in the current title.

```powershell
rgh spoof remote XeCliRemote
rgh spoof remote list
rgh spoof remote apply --slot 1 --text HostName
rgh spoof remote apply --all --text XeCLI-{slot}
```

Example output:

```text
SUCCESS Remote spoof applied
Call of Duty: Black Ops II => XeCliRemote

Slot  Verified       Address
1     XeCliRemote    0x841E1B3C
2     XeCliRemote    0x841E7334
...
12    XeCliRemote    0x8421E2E4
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
- If FTP is unavailable, install can fall back to XBDM upload and XBDM verification automatically.
- `--remote` switches the browser/install flow to the hosted manifest and content cache.
- `rgh avatar choose --remote --titleid 58410A5D --all --current-user --overwrite` was validated live against `192.168.1.186`.

### `rgh avatar library show`
```powershell
rgh avatar library show
rgh avatar library show --json
```

Example output:

```text
Mode                local
Configured Library  auto
Effective Library   A:\Downloads\12\em\Avatar-Item-Collection
Configured Cache    default
Effective Cache     C:\Users\B\AppData\Roaming\XeCLI\avatar-index.v3.json
Manifest URL        https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/avatar-manifest.json
Content Base URL    https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/
```

### `rgh avatar library set`
```powershell
rgh avatar library set --path A:\Downloads\12\em\Avatar-Item-Collection
rgh avatar library set --cache C:\Users\B\AppData\Roaming\XeCLI
rgh avatar library set --clear
```

Example output:

```text
SUCCESS Avatar library settings updated
Library: A:\Downloads\12\em\Avatar-Item-Collection
Cache:   C:\Users\B\AppData\Roaming\XeCLI\avatar-index.v3.json
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
Current user: Diamond KSG | XUID: 0x5D83300C00000900 | State: Signed in to Xbox Live
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
- the picker shares the same hosted/local backend and download cache as the CLI commands.

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
- When FTP is closed on the console, XeCLI can fall back to XBDM upload and XBDM verification automatically.
- Multi-item installs emit batch progress plus per-item transfer bars.

Validated install proofs:

- Standard layout:
  - source item:
    - `A:\Downloads\12\em\Avatar-Item-Collection\415608C3\00009000\000000080DF3B242CAE65A52415608C3`
  - verified remote path:
    - `/Hdd1/Content/0000000000000000/415608C3/00009000/000000080DF3B242CAE65A52415608C3`
- Root layout:
  - source item:
    - `A:\Downloads\12\em\Avatar-Item-Collection\58410A5D\0000020800060102C383304058410A5D`
  - verified remote path:
    - `/Hdd1/Content/0000000000000000/58410A5D/0000020800060102C383304058410A5D`

Example output:

```text
Current user: Diamond KSG | XUID: 0x5D83300C00000900 | State: Signed in to Xbox Live
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

## Ghidra Commands
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
rgh xex strings --in .\title.xex --unicode --min 6
rgh ghidra decompile --in .\title.xex --out .\decomp
```

### Verify a reboot-expected module load
```powershell
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
rgh modules pending
```
