# Commands Reference

This page documents the command surface of XeCLI and explains when to use each command group.

## Command Model
XeCLI is organized by operational area:
- Core console status and targeting
- XBDM-backed inspection and control
- JRPC2-backed RPC helpers
- FTP-backed file access
- Ghidra-assisted XEX analysis
- ISO to Games on Demand conversion

## Common Options

### XBDM-backed commands
| Option | Description |
| --- | --- |
| `--ip <IP>` | Console IP. Uses the saved default target if omitted. |
| `--port <PORT>` | XBDM port. Default: `730`. |
| `--timeout <MS>` | Socket timeout in milliseconds. Default: `5000`. |
| `--json` | Emit JSON output when supported. |

### Discovery commands
| Option | Description |
| --- | --- |
| `--ports <PORTS>` | Comma-separated ports to probe. Default: `730,731`. |
| `--timeout <MS>` | TCP discovery timeout in milliseconds. Default: `400`. |
| `--no-nap` | Disable NAP broadcast discovery. |
| `--no-tcp` | Disable TCP scanning. |
| `--json` | Emit JSON output. |

### FTP commands
| Option | Description |
| --- | --- |
| `--ip <IP>` | Console IP. Falls back to the saved target. |
| `--port <PORT>` | FTP port. Default: `21`. |
| `--user <USER>` | FTP username. Default: `xboxftp`. |
| `--pass <PASS>` | FTP password. Default: `xboxftp`. |
| `--timeout <MS>` | FTP timeout in milliseconds. Default: `5000`. |
| `--json` | Emit JSON output when supported. |

## Core Commands

### status
Purpose:
Returns a console snapshot with connection, title, temperature, platform, and sign-in information.

Syntax:
```bash
rgh status [--quick] [--no-jrpc] [--no-drives] [--no-users] [--json]
```

Use it when:
- You want to confirm the active title
- You want to confirm JRPC2 is available
- You want a fast health check before debugging

### profiles
Purpose:
Shows signed-in users and profile information gathered from XBDM, FTP, and F3 where available.

Syntax:
```bash
rgh profiles [--no-ftp] [--no-f3] [--json]
```

### title
Purpose:
Looks up a Title ID in the bundled Title ID database.

Syntax:
```bash
rgh title <TITLEID> [MEDIAID] [--json]
```

### target
Purpose:
Shows or updates the saved default target.

Syntax:
```bash
rgh target [--set <IP>] [--port <PORT>] [--clear]
```

### ping
Purpose:
Verifies that XBDM is responsive.

Syntax:
```bash
rgh ping
```

### reboot
Purpose:
Requests a cold reboot or title reboot.

Syntax:
```bash
rgh reboot [--title]
```

### install
Purpose:
Installs or removes the command shim.

Syntax:
```bash
rgh install [--uninstall] [--path <DIR>]
```

## Discovery Commands

### start
Purpose:
Discovers consoles and interactively sets the default target.

Syntax:
```bash
rgh start [--ports <PORTS>] [--timeout <MS>] [--no-nap] [--no-tcp] [--json]
```

### connect
Purpose:
Sets the default target by discovery index or IP.

Syntax:
```bash
rgh connect [target] [--ports <PORTS>] [--timeout <MS>] [--no-nap] [--no-tcp]
```

### scan
Purpose:
Performs discovery without changing the saved default target.

Syntax:
```bash
rgh scan [--ports <PORTS>] [--timeout <MS>] [--no-nap] [--no-tcp] [--json]
```

## XBDM Console Access

### xbdm info
```bash
rgh xbdm info [--json]
```

### xbdm raw
```bash
rgh xbdm raw --cmd "<command>"
```

### screenshot
```bash
rgh screenshot --out <FILE> [--format <bmp|raw>] [--force]
```

### xbdm screenshot
```bash
rgh xbdm screenshot --out <FILE> [--format <bmp|raw>] [--force]
```

## Modules

### modules list
```bash
rgh modules list [--sections] [--json]
```

### modules info
```bash
rgh modules info --name <MODULE> [--sections] [--json]
```

### modules dump
```bash
rgh modules dump --name <MODULE> --out <FILE>
rgh modules dump --all --dir <DIR>
```

Use modules commands when:
- You need the live module base and size
- You are validating a patch target
- You want a memory-resident copy of a loaded image

## Memory Commands

### mem dump
```bash
rgh mem dump --addr <HEX|DEC> --size <HEX|DEC> --out <FILE>
```

### mem hexdump
```bash
rgh mem hexdump --addr <HEX|DEC> --size <HEX|DEC>
```

### mem regions
```bash
rgh mem regions [--json]
```

### mem peek
```bash
rgh mem peek --addr <ADDR> --type <TYPE> [--len <N>] [--le]
```

Supported types:
`u8`, `u16`, `u32`, `u64`, `s8`, `s16`, `s32`, `s64`, `f32`, `f64`, `ascii`

### mem poke
```bash
rgh mem poke --addr <ADDR> --type <TYPE> --value <VALUE> [--le]
```

Supported types:
`u8`, `u16`, `u32`, `u64`, `s8`, `s16`, `s32`, `s64`, `f32`, `f64`, `ascii`, `hex`

### mem watch
```bash
rgh mem watch --addr <ADDR> --size <SIZE> [--interval <MS>] [--count <N>] [--clear]
```

### mem strings
```bash
rgh mem strings --addr <ADDR> --size <SIZE> [--min <N>] [--max <N>] [--json]
```

### mem find
```bash
rgh mem find --addr <ADDR> --size <SIZE> --pattern <HEX> | --ascii <TEXT> [--chunk <SIZE>] [--max <N>] [--json]
```

Use memory commands when:
- You already know the address range
- You are validating offsets or patches
- You want to extract structures or string blocks from live memory

## XEX Commands

### xex dump
```bash
rgh xex dump --out <FILE> [--path <XEX>]
```

### xex strings
```bash
rgh xex strings --in <FILE> | --ftp-path <PATH> | --running [--min <N>] [--max <N>] [--unicode] [--out <FILE>] [--json]
```

### xex decompile
```bash
rgh xex decompile --in <FILE> --out <DIR> [--max <N>] [--func-timeout <SEC>] [--timeout <SEC>] [--project <NAME>] [--projects <DIR>] [--path <DIR>] [--java <DIR>] [--loader <NAME>] [--delete-project] [--overwrite] [--script-path <DIR>]
```

Use XEX commands when:
- You want a real executable image
- You are preparing a sample for Ghidra
- You need string extraction without opening a GUI

## Thread and Debug Commands

### threads list
```bash
rgh threads list [--no-names] [--json]
```

### threads context
```bash
rgh threads context --id <THREAD>
```

### threads suspend
```bash
rgh threads suspend --id <THREAD>
```

### threads resume
```bash
rgh threads resume --id <THREAD>
```

### debug stop
```bash
rgh debug stop
```

### debug go
```bash
rgh debug go
```

### debug break add/remove
```bash
rgh debug break add --addr <ADDR>
rgh debug break remove --addr <ADDR>
```

### debug databreak add/remove
```bash
rgh debug databreak add --addr <ADDR> [--size <BYTES>] [--type <write|read|exec|rw>]
rgh debug databreak remove --addr <ADDR> [--size <BYTES>] [--type <write|read|exec|rw>]
```

## File System Commands

### fs list
```bash
rgh fs list --path <DIR> [--json]
```

### fs get
```bash
rgh fs get --path <FILE> --out <FILE>
```

### fs put
```bash
rgh fs put --path <FILE> --in <FILE>
```

### fs cat
```bash
rgh fs cat --path <FILE> [--max <BYTES>] [--hex] [--encoding <utf8|ascii>]
```

### fs rm
```bash
rgh fs rm --path <PATH>
```

### fs mkdir
```bash
rgh fs mkdir --path <DIR>
```

### fs mv
```bash
rgh fs mv --from <PATH> --to <PATH>
```

## JRPC2 Commands

### jrpc2 cpu-key
```bash
rgh jrpc2 cpu-key
```

### jrpc2 temps
```bash
rgh jrpc2 temps [--sensor <cpu|gpu|edram|motherboard>]
```

### jrpc2 title-id
```bash
rgh jrpc2 title-id
```

### jrpc2 dashboard
```bash
rgh jrpc2 dashboard
```

### jrpc2 motherboard
```bash
rgh jrpc2 motherboard
```

### jrpc2 resolve
```bash
rgh jrpc2 resolve --module <NAME> --ordinal <N>
```

### jrpc2 notify
```bash
rgh jrpc2 notify --message <TEXT> [--logo <ID>] [--icon <NAME>]
```

### jrpc2 call
```bash
rgh jrpc2 call --ret <TYPE> [--addr <ADDR> | --module <NAME> --ordinal <N>] [--arg <TYPE:VALUE> ...] [--system] [--vm]
```

Return types:
`int`, `uint`, `float`, `string`, `byte`, `u64`, `void`

Argument forms:
`int:123`, `u32:0xDEADBEEF`, `float:1.5`, `string:hello`, `bytes:DEADBEEF`

## Notification Commands

### notify
```bash
rgh notify --message <TEXT> [--logo <ID>] [--icon <NAME>]
```

### notify-icons
```bash
rgh notify-icons list
rgh notify-icons add --name <NAME> --logo <ID>
rgh notify-icons remove --name <NAME>
```

## FTP Commands

### ftp target
```bash
rgh ftp target [--set <IP>] [--port <PORT>] [--user <USER>] [--pass <PASS>] [--clear]
```

### ftp list
```bash
rgh ftp list --path <DIR>
```

### ftp find
```bash
rgh ftp find --path <DIR> --name <PATTERN> [--depth <N>] [--max <N>] [--regex]
```

### ftp get
```bash
rgh ftp get --path <REMOTE> --out <FILE>
```

### ftp put
```bash
rgh ftp put --path <REMOTE> --in <FILE>
```

### ftp cat
```bash
rgh ftp cat --path <REMOTE> [--max <BYTES>] [--encoding <utf8|ascii>]
```

### ftp rm, mkdir, mv
```bash
rgh ftp rm --path <PATH>
rgh ftp mkdir --path <DIR>
rgh ftp mv --from <PATH> --to <PATH>
```

## GOD Commands

### god info
```bash
rgh god info <ISO> [--json]
```

### god build
```bash
rgh god build <ISO> <DEST> [--trim <end|none>] [--threads <N>] [--title <NAME>]
```

### god watch
```bash
rgh god watch <WATCH> --dest <DIR> [--recursive] [--settle <SEC>] [--poll <MS>] [--timeout <SEC>] [--retries <N>] [--delete-source] [--move-done <DIR>] [--move-failed <DIR>] [--once]
```

## Ghidra Commands

### ghidra config
```bash
rgh ghidra config [--path <DIR>] [--java <DIR>] [--projects <DIR>] [--clear]
```

### ghidra analyze
```bash
rgh ghidra analyze --in <FILE> [--project <NAME>] [--projects <DIR>] [--path <DIR>] [--java <DIR>] [--loader <NAME>] [--timeout <SEC>] [--delete-project] [--overwrite]
rgh ghidra analyze --ftp-path <PATH> | --running [--timeout <SEC>]
```

### ghidra decompile
```bash
rgh ghidra decompile --in <FILE> --out <DIR> [--max <N>] [--func-timeout <SEC>] [--project <NAME>] [--projects <DIR>] [--path <DIR>] [--java <DIR>] [--timeout <SEC>] [--delete-project] [--overwrite] [--script-path <DIR>]
rgh ghidra decompile --ftp-path <PATH> | --running --out <DIR> [--max <N>] [--func-timeout <SEC>] [--timeout <SEC>]
```

### ghidra verify
```bash
rgh ghidra verify --dir <DIR> [--pattern <REGEX>] [--ext <EXT>] [--max <N>] [--json]
```


