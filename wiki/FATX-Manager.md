# Fatman

Fatman is XeCLI's read-only FATX recovery and extraction manager for local Xbox 360 disks and images. The first release cut focuses on image recovery, partition dumping, inspection, and export, not on write, mount, format, or repair operations.

Use it when you want to:

- detect FATX-capable disks or image sources
- inspect partitions and volume details
- browse directories and locate entries
- search by name or path
- recover data from `.img` and `.bin` sources
- dump partitions to a host directory
- print small files in the terminal
- export selected files or directory trees to the host

## Planned Command Surface

```powershell
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

## Manual Open by Offset

For nonstandard, partial, or dev images, Fatman can open a volume directly from a byte offset instead of relying on the normal layout detector.

- `--offset <VALUE>` opens a FATX/XTAF volume at a manual byte offset
- `--length <VALUE>` limits that manual open to a specific byte range
- both values accept decimal bytes or `0x`-prefixed hex
- `--length` only works when `--offset` is also supplied
- `rgh fatman scan` finds plausible FATX/XTAF header offsets before you open one manually

This is the right workflow when you already know a likely FATX/XTAF header location and want to inspect it directly.

## Read-Only Scope

This first cut is intentionally narrow:

- read local Xbox 360 FATX images or disks
- show partition and volume metadata
- list directories and find entries
- recover and dump `.img` / `.bin` sources
- export files or directory trees to the host

It does not claim to:

- write files back to FATX volumes
- format or repartition disks
- mount a virtual filesystem
- repair damaged volumes
- modify security sectors or low-level disk metadata

## Example Workflows

### Inspect a disk
```powershell
rgh fatman devices
rgh fatman partitions --image .\Hdd1.img
rgh fatman info --image .\Hdd1.img --partition Hdd1
```

### Probe a nonstandard image by offset
```powershell
rgh fatman scan --image .\Unknown.img
rgh fatman info --image .\Unknown.img --offset 0xB6600000
rgh fatman list --image .\Unknown.img --offset 0xB6600000 --path /
rgh fatman dump --image .\Unknown.img --offset 0xB6600000 --length 0x10000000 --out .\partition-dump
```

### Browse a path
```powershell
rgh fatman list --image .\Hdd1.img --partition Hdd1 --path Content\0000000000000000
rgh fatman find --image .\Hdd1.img --partition Hdd1 --query launch.ini
```

### Export a file or folder
```powershell
rgh fatman cat --image .\Hdd1.img --partition Hdd1 --path launch.ini
rgh fatman get --image .\Hdd1.img --partition Hdd1 --path launch.ini --out .\launch.ini
rgh fatman extract --image .\Hdd1.img --partition Hdd1 --path Content\0000000000000000 --out .\content
rgh fatman dump --image .\Hdd1.img --out .\recovery
```

## Notes

Fatman is separate from the live-console `fs`, `ftp`, `save`, and `content` workflows.
Those branches operate on the console over XBDM or FTP. FATX is for local disks and images.

Validation note:

- the runtime has been verified against a synthetic FATX fixture image
- manual-open and scan flows were also validated against a nonstandard AMPED HDD image, where Fatman surfaced real `XTAF` offsets and opened the compatibility volume by bounded offset

## Related Pages

- [Commands Reference](Commands.md)
- [CLI Help Output](CLI-Help.md)
- [Home](Home.md)
- [Documentation Standards](Standards.md)
