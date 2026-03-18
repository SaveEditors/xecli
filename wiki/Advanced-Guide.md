# Advanced Guide

This guide focuses on the workflows XeCLI is strongest at: live inspection, reverse engineering support, repeatable dumps, and tool integration.

## Choosing the Right Workflow

### Use `status` when you need context
Start with:
```bash
rgh status
```

This gives you the target state before you start reading memory, dumping modules, or sending RPC calls.

### Use `modules` when you want the live memory image of a loaded module
```bash
rgh modules list
rgh modules info --name xam.xex --sections
rgh modules dump --name xam.xex --out .\\xam.bin
```

This is useful for:
- Mapping what is actually loaded right now
- Inspecting base addresses and sizes
- Pulling a module image from memory for low-level comparison work

### Use `xex dump` when you need a real XEX file
```bash
rgh xex dump --out .\\title.xex
```

This is the correct starting point for:
- Ghidra import
- Static string extraction
- Repeated archival of dashboard or title executables

Do not confuse raw module memory dumps with full XEX files. They serve different purposes.

## Memory Work

### Reading Known Addresses
```bash
rgh mem peek --addr 0x82000000 --type u32
rgh mem hexdump --addr 0x82000000 --size 0x100
```

### Writing Values
```bash
rgh mem poke --addr 0x82000000 --type u32 --value 0x12345678
```

Use `mem poke` carefully. It is appropriate for controlled experiments, trainer development, and patch validation, but a bad write can crash the title or destabilize the session.

### Watching Live Changes
```bash
rgh mem watch --addr 0x82000000 --size 0x40 --interval 250
```

This is useful for:
- Watching state changes in a small region
- Verifying a patch lands where expected
- Tracking counters, flags, and title state

### Searching Memory
For short ranges:
```bash
rgh mem find --addr 0x82000000 --size 0x20000 --ascii "XAM"
```

For larger investigations, dump first and search offline:
```bash
rgh mem dump --addr 0x82000000 --size 0x400000 --out .\\range.bin
```

That workflow is usually more reliable than trying to brute-force very large live searches over XBDM.

## Thread and Debug Work

List threads:
```bash
rgh threads list
```

Inspect context:
```bash
rgh threads context --id <thread-id>
```

Control execution:
```bash
rgh debug stop
rgh debug go
```

Breakpoints:
```bash
rgh debug break add --addr 0x82001000
rgh debug break remove --addr 0x82001000
```

Data breakpoints:
```bash
rgh debug databreak add --addr 0x82100000 --size 4 --type write
```

Use this set when you are validating a patch site, watching a hot structure, or trying to understand where a value is modified.

## XEX and Ghidra

### Dump, Then Analyze
```bash
rgh xex dump --out .\\title.xex
rgh ghidra config --path "<ghidra-dir>" --java "<java-home>"
rgh ghidra analyze --in .\\title.xex
rgh ghidra decompile --in .\\title.xex --out .\\decomp
```

### Validate Decompiled Output
```bash
rgh ghidra verify --dir .\\decomp
```

This helps catch placeholder output caused by bad imports, wrong loader behavior, or unsupported instructions.

### Extract Strings Quickly
```bash
rgh xex strings --in .\\title.xex --unicode --min 6
```

This is often the fastest first pass when you are trying to identify subsystems, paths, feature names, or hardcoded messages.

## Using the Bundled Title ID Database
The bundled database is useful beyond the `title` command. It can be used to:
- Resolve the current Title ID in status output
- Annotate dumps and reports
- Label captured screenshots or artifacts
- Feed metadata into launchers, save editors, dashboards, or trainers

See:
- [Title ID Database](Title-ID-Database.md)
- [Integrations](Integrations.md)

## Automation Patterns

### Human-readable status logs
```bash
rgh status --json
```

### Module inventory
```bash
rgh modules list --json
```

### Title metadata lookup in scripts
```bash
rgh title 4D5307E6 --json
```

### FTP indexing
```bash
rgh ftp find --path "/Hdd1/" --name "*.xex" --max 500 --json
```

These outputs are designed to be consumed by other tools, not just read in a terminal.


