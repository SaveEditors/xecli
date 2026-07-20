# Advanced Guide

This page focuses on the workflows where XeCLI is strongest: live inspection, module work, memory editing, debugger control, XEX extraction, and automation.

## Pick the Right Data Source
Use the transport that matches the task:

- XBDM for live control, memory, threads, screenshots, and module metadata
- JRPC2 for Title ID, CPU key, temperatures, notifications, and generic RPC
- FTP for bulk file transfer, content trees, save files, and DashLaunch config edits

Trying to force one transport into every job is where most fragile automation starts.

## Active Title and Metadata
`rgh title` is now useful as an operator tool, not just a raw lookup helper:

```powershell
rgh title
rgh title --json
```

It resolves the active Title ID from the console, applies an optional user Title Database, and can use/cache a non-generic live XEX filename for a dashboard replacement or homebrew shell. Without a name source, output remains the raw hexadecimal ID.

## Session and Hardware Controls
XeCLI also exposes a lightweight hardware/session layer that is useful in tooling and operator workflows:

```powershell
rgh signin state
rgh led set --preset quadrant1
rgh fan set --speed 55 --channel both
rgh smc version
rgh smc temps
```

Use cases:

- prove that the correct profile is signed in before save or content work
- surface visible LED state changes during scripted workflows
- dispatch fan changes from automation without opening a GUI tool
- read SMC version and fixed-point temperature details when the installed plugin stack supports it

Read [Hardware-and-System.md](Hardware-and-System) for the operator-level details and caveats.

## Memory Work
### Capture the live memory map
```powershell
rgh mem map
rgh mem map --json
```

Use this to capture the current live region layout. Save the JSON form when you want a snapshot that can be compared later with `rgh mem diff` or `rgh mem compare`.

### Read and verify before you write
Typical sequence:

```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh mem peek --addr 0x30000000 --type u32
rgh mem search --addr 0x30000000 --size 0x1000 --pattern DEADBEEF
rgh mem search --addr 0x30000000 --size 0x1000 --type uint32 --value 0x12345678
```

### Write with type discipline
```powershell
rgh mem poke --addr 0x82000000 --type u32 --value 0x12345678
rgh mem poke --addr 0x82000000 --type float --value 1337
rgh mem poke --addr 0x82000000 --type string --value "XeCLI"
```

Do not treat `poke` as a blind trainer primitive. Read the target first, confirm endianness, then write.

In the current release, `mem poke` verifies the write by reading the target bytes back. If the console rejects the write or the readback differs, XeCLI fails instead of printing a misleading success line.

`rgh mem peek`, `rgh mem poke`, `rgh mem hexdump`, `rgh mem dump`, `rgh mem search`, and `rgh debug address-check` now share the same live-address resolver, so module/RVA and Ghidra input can be translated before the console call is made.

`rgh mem search` uses the same value encoding as `rgh mem poke` and freeze writes. Values are big-endian by default; add `--little-endian` for little-endian targets. Common aliases include `int32`, `uint32`, and `float32`. For signed types, hex can use two's-complement form, such as `--type int32 --value 0xFFFFFFFF` to search for `-1` bytes.

Use `--le` on `rgh mem peek` and `rgh mem poke` when the target value should be read or written as little-endian. Use `--clear` on `rgh mem watch` when you want the screen cleared between updates. Use `--hit` to choose which match to freeze when `--freeze-all` is not used, and `--freeze-interval` to control freeze write timing.

### Freeze writes
```powershell
rgh mem search --addr 0x82000000 --size 0x4000 --pattern 00000000 --freeze --freeze-type u32 --freeze-value 1 --freeze-count 10
```

Use this for controlled short tests. Infinite freeze loops are available, but that is not a good default during early analysis.

Freeze writes now use the same verified-write path as `mem poke`, which makes short freeze passes safer during console-backed testing.

## Trainer Schema Version 1
`rgh trainer apply` is the structured trainer path for a flat live-memory file. Use it when you want a repeatable write set from a file instead of hand-entered pokes.

```powershell
rgh trainer apply --file .\trainer.json --dry-run
rgh trainer apply --file .\trainer.json
```

`--dry-run` previews the plan and leaves memory untouched.

Each entry in the trainer file is a flat object with these fields:

- `address`: the live console address to write
- `value`: the typed value to write for that entry
- `littleEndian`: set to `true` when the value should be written little-endian
- `enabled`: set to `false` to keep an entry in the file without applying it
- `note`: short operator text for context

Trainer schema version 1 uses live addresses only. It does not resolve module names, RVAs, or Ghidra addresses, and it does not follow pointer chains, build freeze profiles, or manage rollback files. Freeze loops are also out of scope for this schema. `rgh trainer run` is an alias of `rgh trainer apply`.

## Module Work
### Safe module queries
```powershell
rgh modules list
rgh modules info --name Aurora.xex
rgh modules dump --name xam.xex --out .\xam.bin
```

### Unload workflow
```powershell
rgh modules unload --name HvP2.xex --force
```

Unload is intentionally gated because live module unload can wedge the console even when the RPC appears to complete. Use a disposable title module for lifecycle testing. Known system and boot-plugin modules are blocked unless both `--force` and `--allow-critical` are supplied, and XeCLI requires a fresh XBDM health check before it reports success.

### Reboot-expected load workflow
Some modules or plugin stacks do not complete as a clean hot-load. XeCLI supports a tracked reboot-expected flow:

```powershell
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
```

Then, after the console comes back:

```powershell
rgh modules pending
```

This is better than pretending a disconnect was success.

## Thread and Debug Work
### Thread control
```powershell
rgh threads list
rgh threads context --id 0xFB000008
rgh threads suspend --id 0xFB000008
rgh threads resume --id 0xFB000008
```

`threads list` now favors image-backed start metadata over fake names. On targets where XBDM does not expose real thread names, the table shows the containing image plus `Start Addr.` and `End Addr.` columns instead. `End Addr.` is the image end boundary for the resolved start address.

### Execution control and events
```powershell
rgh debug stop
rgh debug go
rgh debug watch --duration 5
rgh debug watch --duration 5 --require-event
```

`debug watch` is the quickest way to confirm that the console is actually emitting debug lifecycle events instead of silently stalling. Use `--duration` to set the watch length; `--timeout` only affects the XBDM connection and I/O timeout. Add `--require-event` when the test expects at least one notification and a quiet stream must fail instead of counting as success.

`debug stop` and `debug go` now require accepted XBDM responses. A rejected control command is reported as a failure, not as a silent success.

### Address checks
```powershell
rgh debug address-check --module default.xex --rva 0x1234
```

`debug address-check` uses the same live-address resolver as the memory commands, so module/RVA or Ghidra input is translated before the console sees it. The command also answers to `rgh debug addr` and `rgh debug resolve` when you want a shorter form.

### Breakpoints
```powershell
rgh debug break add --addr 0x82001000
rgh debug databreak add --addr 0x82100000 --size 4 --type write
```

Use code breakpoints to catch known execution sites and data breakpoints to identify writers to hot structures.

`debug databreak --type rw` now maps to a true read/write data breakpoint. Breakpoint and databreak commands also fail fast when XBDM rejects the request.

## XEX, Ghidra, and IDA Work
### Dump the actual executable
Use `xex dump` for a real XEX image, not a raw module dump:

```powershell
rgh xex dump --out .\title.xex
```

### String triage
```powershell
rgh xex strings --file .\title.xex --unicode --min-length 6 --limit 50
```

### Map a Ghidra address
```powershell
rgh xex map --file .\default.xex --ghidra-base 0x82000000 --ghidra 0x82001234
```

This runs offline on a local XEX file and maps the Ghidra address to the matching XEX load address.

Use this translation:

`GhidraAddress - GhidraBase + LiveModuleBase`

For example, if Ghidra shows `0x82001234`, the Ghidra base is `0x82000000`, and the live module base is `0x90000000`, the live address is `0x90001234`.

`rgh xex map` does not need a console connection. It only uses the local XEX file plus the Ghidra base you provide. If you are working from a live module base on a connected console, use the runtime module metadata or live memory workflow instead of the offline mapper.

### Ghidra headless pipeline
Ghidra is external and documented in the CLI as `(Free)`. XeCLI installs the pinned `SaveEditors/XEXLoaderWV` 13.0.0 loader asset after you configure the tool path; custom archives or URLs require an explicit SHA-256.

```powershell
rgh ghidra config --path "C:\Tools\ghidra" --java "C:\Java"
rgh ghidra install-loader
rgh ghidra analyze --in .\title.xex
rgh ghidra decompile --in .\title.xex --out .\decomp
rgh ghidra verify --dir .\decomp
```

`ghidra verify` exists because a decompile folder full of placeholder stubs is worse than no result at all if you do not catch it.

### IDA headless pipeline
IDA support is documented against `IDA Pro 9.3` with the SaveEditors `idaxex 9.3` loader, and legacy `IDA Pro 9.1.250226` archives remain supported for compatible installs. XeCLI does not bundle IDA Pro, but it can install the loader helper after you configure the tool path.

```powershell
rgh ida config --path "C:\Program Files\IDA Professional 9.3" --python python
rgh ida install-loader
rgh ida check
rgh ida analyze --ftp-path /Hdd1/Aurora/Aurora.xex --out-db .\Aurora.i64 --overwrite
rgh ida decompile --in .\Aurora.i64 --out .\ida-decomp --backend idalib --max 50
rgh ida verify --dir .\ida-decomp
```

### One-shot live console path
If you want a single command that pulls the running or remote XEX, builds a database, and exports C:

```powershell
rgh xex ida-decompile --ftp-path /Hdd1/Aurora/Aurora.xex --out .\ida-decomp --max 50 --out-db .\Aurora.i64 --keep-db
```

### Symbol sidecar export
If you want the function names from an IDA or Ghidra database without carrying extra analysis metadata:

```powershell
rgh ida export-symbols --in .\Aurora.i64 --out .\symbols.json
rgh ghidra export-symbols --in .\Aurora.xex --out .\symbols.json
rgh mem-bookmarks import --file .\symbols.json
```

The sidecar JSON uses `version`, `module`, and `symbols`, and each symbol carries `name`, `RVA`, and `type`. `rgh mem-bookmarks import --file .\symbols.json` accepts either IDA or Ghidra sidecars. This is function-name only, and it stays on the exact-address or module/RVA workflow. It does not export comments or PDB data in this path, and there is no comments/PDB import-back workflow yet.

Read [Reverse-Engineering.md](Reverse-Engineering) for the current support matrix, official download links, helper-loader install notes, and the pinned IDA baseline.

## Save, Content, and Plugin Operations
Use FTP-backed commands when the job is about the storage layout instead of the live memory image:

```powershell
rgh save extract --titleid 415608C3 --out .\saves
rgh content list --device Hdd1 --show-types
rgh plugin list
rgh plugin enable --slot 5 --path Hdd:\XDRPC.xex --backup
```

Treat content deletion and plugin edits as operational changes, not exploratory commands.

## Automation Guidance
### Good automation targets
- `status --json`
- `title --json`
- `modules list --json`
- `content list --json`
- `ghidra verify --json`

### Bad automation targets
- interactive selection flows
- destructive commands without explicit targeting
- reboot-expected module loads without follow-up verification

### Example pattern
```powershell
rgh status --json > status.json
rgh title --json > title.json
rgh modules list --json > modules.json
```

## When to Stop and Reassess
Stop and reassess instead of brute-forcing the console when:

- a module load repeatedly knocks the box off LAN
- XBDM stays up but JRPC2 calls become ambiguous
- a memory write target does not retain values across reads
- decompile output collapses into placeholder stubs

XeCLI is most effective when it makes unstable workflows explicit instead of hiding them.
