# Commands Reference

This page documents the XeCLI command surface by workflow area. Command examples use the installed terminal command `rgh`.

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

### `rgh profiles`
Enumerates sign-in and profile information gathered from XBDM, JRPC/XAM, FTP, and F3 when available.

```powershell
rgh profiles
rgh profiles --json
rgh profiles --no-ftp
rgh profiles --no-f3
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

### `rgh target`
Show, set, or clear the saved default XBDM target.

```powershell
rgh target
rgh target --set <console-ip>
rgh target --set <console-ip> --port 730
rgh target --clear
```

### `rgh ping`
Fast XBDM connectivity check.

```powershell
rgh ping
```

### `rgh reboot`
Cold reboot or title reboot.

```powershell
rgh reboot
rgh reboot --title
rgh reboot --notify
```

### `rgh launch`
Launch a XEX with optional directory, arguments, title hint, and notification.

```powershell
rgh launch Hdd1:\Aurora\Aurora.xex
rgh launch Hdd1:\Aurora\Aurora.xex --titleid FFFE07D1
rgh launch --xex Hdd1:\Aurora\Aurora.xex --args "debug=1"
rgh launch --xex Hdd1:\Aurora\Aurora.xex --dry-run
```

### `rgh install`
Install or remove the command shim, or add/remove the machine PATH entry.

```powershell
rgh install
rgh install --machine-path
rgh install --uninstall
```

## Discovery Commands
### `rgh start`
```powershell
rgh start
rgh start --json
```

### `rgh connect`
```powershell
rgh connect 1
rgh connect <console-ip>
```

### `rgh scan`
```powershell
rgh scan
rgh scan --json
```

## Screenshot Commands
### `rgh screenshot`
```powershell
rgh screenshot --out .\screen.bmp
rgh screenshot --out .\screen.bmp --force
```

The command emits decoded frame-buffer metadata after a successful capture.

## XBDM Root Commands
### `rgh xbdm info`
```powershell
rgh xbdm info
rgh xbdm info --json
```

### `rgh xbdm raw`
```powershell
rgh xbdm raw --cmd "modules"
rgh xbdm raw --cmd "dirlist name=Hdd:\\"
```

### `rgh xbdm screenshot`
```powershell
rgh xbdm screenshot --out .\screen.bmp
```

## Module Commands
### `rgh modules list`
```powershell
rgh modules list
rgh modules list --json
rgh modules list --sections
```

### `rgh modules info`
```powershell
rgh modules info --name Aurora.xex
rgh modules info --name xam.xex --sections
```

### `rgh modules dump`
```powershell
rgh modules dump --name xam.xex --out .\xam.bin
rgh modules dump --all --dir .\modules
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

### `rgh modules pending`
```powershell
rgh modules pending
```

Use this after:

```powershell
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
```

## Memory Commands
### `rgh mem dump`
```powershell
rgh mem dump --addr 0x82000000 --size 0x20000 --out .\mem.bin
```

### `rgh mem hexdump`
```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
```

### `rgh mem regions`
```powershell
rgh mem regions
rgh mem regions --json
```

### `rgh mem peek`
```powershell
rgh mem peek --addr 0x82000000 --type u32
rgh mem peek --addr 0x82000000 --type ascii --len 32
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

### `rgh mem watch`
```powershell
rgh mem watch --addr 0x82000000 --size 0x40
rgh mem watch --addr 0x82000000 --size 0x40 --interval 100 --count 10
```

### `rgh mem strings`
```powershell
rgh mem strings --addr 0x82000000 --size 0x20000 --min 6
rgh mem strings --addr 0x82000000 --size 0x20000 --json
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

## XEX Commands
### `rgh xex dump`
```powershell
rgh xex dump --out .\title.xex
rgh xex dump --path Hdd1:\Aurora\Aurora.xex --out .\aurora.xex
```

### `rgh xex strings`
```powershell
rgh xex strings --running --unicode --min 6
rgh xex strings --ftp-path /Hdd1/Aurora/Aurora.xex --out .\strings.txt
rgh xex strings --in .\title.xex --json
```

### `rgh xex decompile`
```powershell
rgh xex decompile --in .\title.xex --out .\decomp
rgh xex decompile --running --out .\decomp --max 200
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

## Thread and Debug Commands
### Threads
```powershell
rgh threads list
rgh threads context --id 0xFB000008
rgh threads suspend --id 0xFB000008
rgh threads resume --id 0xFB000008
```

### Debug control
```powershell
rgh debug stop
rgh debug go
rgh debug watch
```

### Breakpoints
```powershell
rgh debug break add --addr 0x82001000
rgh debug break remove --addr 0x82001000
rgh debug break clearall
```

### Data breakpoints
```powershell
rgh debug databreak add --addr 0x82100000 --size 4 --type write
rgh debug databreak remove --addr 0x82100000 --size 4 --type write
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

## Notification Commands
```powershell
rgh notify "Success :)"
rgh notify --message "XeCLI connected" --icon info
rgh notify-icons list
rgh notify-icons add --name success --logo 0x24
rgh notify-icons remove --name success
```

## Save Commands
### `rgh save list`
```powershell
rgh save list --titleid FFFE07D1 --device Hdd1
rgh save list --titleid 415608C3 --profile E00012AA8D7879B4
```

### `rgh save extract`
```powershell
rgh save extract --titleid 415608C3 --out .\saves
rgh save extract --titleid 415608C3 --profile E00012AA8D7879B4 --device Hdd1 --overwrite
```

### `rgh save inject`
```powershell
rgh save inject --titleid 415608C3 --in .\saves --device Hdd1
rgh save inject --titleid 415608C3 --profile E00012AA8D7879B4 --in .\save.bin --overwrite
```

## Content Commands
### `rgh content list`
```powershell
rgh content list
rgh content list --device Hdd1 --show-types
rgh content list --titleid 415608C3
```

### `rgh content delete`
```powershell
rgh content delete --titleid 415608C3 --type "Title Update"
```

Treat delete operations as destructive.

## Plugin Commands
### `rgh plugin list`
```powershell
rgh plugin list
```

### `rgh plugin enable`
```powershell
rgh plugin enable --slot 5 --path Hdd:\XDRPC.xex
rgh plugin enable --slot 5 --path Hdd:\XDRPC.xex --backup
```

### `rgh plugin disable`
```powershell
rgh plugin disable --slot 5
```

These commands edit `launch.ini` over FTP. Back up first when changing a live configuration.

## GOD Commands
### `rgh god info`
```powershell
rgh god info .\game.iso
```

### `rgh god build`
```powershell
rgh god build .\game.iso .\god
rgh god build .\game.iso .\god --trim end --threads 2
```

### `rgh god watch`
```powershell
rgh god watch .\incoming --dest .\god
rgh god watch .\incoming --dest .\god --recursive --move-done .\done --move-failed .\failed
rgh god watch .\incoming --dest .\god --once
```

The watchdog waits for file stability before starting conversion and can process a directory once and exit for automation use.

## Ghidra Commands
### `rgh ghidra config`
```powershell
rgh ghidra config --path "C:\Tools\ghidra" --java "C:\Java"
```

### `rgh ghidra analyze`
```powershell
rgh ghidra analyze --in .\title.xex
rgh ghidra analyze --running
rgh ghidra analyze --ftp-path /Hdd1/Aurora/Aurora.xex
```

### `rgh ghidra decompile`
```powershell
rgh ghidra decompile --in .\title.xex --out .\decomp
rgh ghidra decompile --running --out .\decomp --max 200
```

### `rgh ghidra verify`
```powershell
rgh ghidra verify --dir .\decomp
rgh ghidra verify --dir .\decomp --json
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
