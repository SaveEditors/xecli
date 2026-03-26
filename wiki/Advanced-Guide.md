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

It resolves the active Title ID from the console and can layer a path-based fallback name over the bundled database entry when the live XEX is a dashboard replacement or homebrew shell.

## Session and Hardware Controls
XeCLI also exposes a lightweight hardware/session layer that is useful in tooling and operator workflows:

```powershell
rgh signin state
rgh led set --preset quadrant1
rgh fan set --speed 55 --channel both
rgh smc version
```

Use cases:

- prove that the correct profile is signed in before save or content work
- surface visible LED state changes during scripted workflows
- dispatch fan changes from automation without opening a GUI tool
- probe SMC details when the installed plugin stack supports it

Read [Hardware-and-System.md](Hardware-and-System.md) for the operator-level details and caveats.

## Memory Work
### Read and verify before you write
Typical sequence:

```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh mem peek --addr 0x30000000 --type u32
rgh mem search --addr 0x30000000 --size 0x1000 --pattern DEADBEEF
```

### Write with type discipline
```powershell
rgh mem poke --addr 0x82000000 --type u32 --value 0x12345678
rgh mem poke --addr 0x82000000 --type float --value 1337
rgh mem poke --addr 0x82000000 --type string --value "XeCLI"
```

Do not treat `poke` as a blind trainer primitive. Read the target first, confirm endianness, then write.

### Freeze writes
```powershell
rgh mem search --addr 0x82000000 --size 0x4000 --pattern 00000000 --freeze --freeze-type u32 --freeze-value 1 --freeze-count 10
```

Use this for controlled short tests. Infinite freeze loops are available, but that is not a good default during early analysis.

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

Unload is intentionally gated because live module unload can wedge the console if the target rejects it.

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
rgh debug watch
```

`debug watch` is the quickest way to confirm that the console is actually emitting debug lifecycle events instead of silently stalling.

### Breakpoints
```powershell
rgh debug break add --addr 0x82001000
rgh debug databreak add --addr 0x82100000 --size 4 --type write
```

Use code breakpoints to catch known execution sites and data breakpoints to identify writers to hot structures.

## XEX, Ghidra, and IDA Work
### Dump the actual executable
Use `xex dump` for a real XEX image, not a raw module dump:

```powershell
rgh xex dump --out .\title.xex
```

### String triage
```powershell
rgh xex strings --running --unicode --min 6
```

### Ghidra headless pipeline
Ghidra is external and documented in the CLI as `(Free)`. XeCLI does not currently pin a supported Ghidra version in the docs, but it can install the `XEXLoaderWV` helper after you configure the tool path.

```powershell
rgh ghidra config --path "C:\Tools\ghidra" --java "C:\Java"
rgh ghidra install-loader
rgh ghidra analyze --in .\title.xex
rgh ghidra decompile --in .\title.xex --out .\decomp
rgh ghidra verify --dir .\decomp
```

`ghidra verify` exists because a decompile folder full of placeholder stubs is worse than no result at all if you do not catch it.

### IDA headless pipeline
IDA support is pinned to `IDA Pro 9.1.250226` with the maintained [SaveEditors/idaxex](https://github.com/SaveEditors/idaxex/releases/tag/0.42b-compat) `0.42b-compat` line. XeCLI does not bundle IDA Pro, but it can install the pinned loader helper after you configure the tool path.

```powershell
rgh ida config --path "C:\Program Files\IDA Professional 9.1" --python python
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

Read [Reverse-Engineering.md](Reverse-Engineering.md) for the current support matrix, official download links, helper-loader install notes, and the pinned IDA baseline.

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
