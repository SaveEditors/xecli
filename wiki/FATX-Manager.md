# XTAF

XTAF is XeCLI's FATX recovery, extraction, repair, and Windows-first disk-management layer for local Xbox 360 disks and images. The primary command name is `rgh xtaf`; `rgh fatman` and `rgh fatx` remain as compatibility aliases. It supports image-backed and raw-disk reads, exports, directory creation, file injection, rename/move, delete operations, retail-layout FATX formatting, low-level metadata backup/restore, and safe chain-map repair.

Use it when you want to:

- detect FATX-capable disks or image sources
- inspect Windows physical disks directly
- inspect partitions and volume details
- browse directories and locate entries
- search by name or path
- recover data from `.img` and `.bin` sources
- create FATX directories inside an image
- write host files into a FATX image
- rename or move FATX entries inside an image
- remove FATX files or directories inside an image
- format one partition or a full Xbox 360 retail layout
- back up and restore low-level metadata regions
- check for orphaned or invalid FATX chain-map state
- repair safe FATX allocation issues
- dump partitions to a host directory
- print small files in the terminal
- export selected files or directory trees to the host

## Current Command Surface

```powershell
rgh xtaf devices
rgh xtaf disks
rgh xtaf partitions
rgh xtaf scan
rgh xtaf info
rgh xtaf list
rgh xtaf find
rgh xtaf cat
rgh xtaf get
rgh xtaf mkdir
rgh xtaf put
rgh xtaf mv
rgh xtaf rm
rgh xtaf format
rgh xtaf check
rgh xtaf repair
rgh xtaf metadata backup
rgh xtaf metadata restore
rgh xtaf extract
rgh xtaf dump
```

`rgh fatman` and `rgh fatx` are compatibility aliases for the same command group.

## Manual Open by Offset

For nonstandard, partial, or dev images, XTAF can open a volume directly from a byte offset instead of relying on the normal layout detector.

- `--offset <VALUE>` opens a FATX/XTAF volume at a manual byte offset
- `--length <VALUE>` limits that manual open to a specific byte range
- both values accept decimal bytes or `0x`-prefixed hex
- `--length` only works when `--offset` is also supplied
- `rgh xtaf scan` finds plausible FATX/XTAF header offsets before you open one manually

This is the right workflow when you already know a likely FATX/XTAF header location and want to inspect it directly.

## Current Scope

Today XTAF can:

- read local Xbox 360 FATX images or disks
- enumerate Windows physical disks for direct `--disk` access
- show partition and volume metadata
- list directories and find entries
- recover and dump `.img` / `.bin` sources
- export files or directory trees to the host
- create FATX directories in an image
- write host files into a FATX image
- rename or move FATX entries in an image
- remove FATX files or directories in an image
- format individual FATX partitions or a retail Xbox 360 layout
- back up disk-prefix and partition-header metadata regions
- restore low-level metadata regions from an XTAF metadata manifest
- scan and repair safe chain-map issues such as orphaned allocations

It does not claim to:

- mount a virtual filesystem
- expose a true driver-backed Windows mount
- rewrite arbitrary security sectors beyond the metadata regions XTAF explicitly backs up and restores

## Example Workflows

### Inspect a disk
```powershell
rgh xtaf disks
rgh xtaf partitions --disk 2
rgh xtaf info --disk 2 --partition Content
rgh xtaf metadata backup --disk 2 --out .\xtaf-meta
rgh xtaf check --disk 2 --partition Content
rgh xtaf repair --disk 2 --partition Content --auto-confirm
rgh xtaf format --disk 2 --auto-confirm
```

### Inspect a disk image
```powershell
rgh xtaf devices
rgh xtaf partitions --image .\Hdd1.img
rgh xtaf info --image .\Hdd1.img --partition Hdd1
```

### Probe a nonstandard image by offset
```powershell
rgh xtaf scan --image .\Unknown.img
rgh xtaf info --image .\Unknown.img --offset 0xB6600000
rgh xtaf list --image .\Unknown.img --offset 0xB6600000 --path /
rgh xtaf dump --image .\Unknown.img --offset 0xB6600000 --length 0x10000000 --out .\partition-dump
```

### Browse a path
```powershell
rgh xtaf list --image .\Hdd1.img --partition Hdd1 --path Content\0000000000000000
rgh xtaf find --image .\Hdd1.img --partition Hdd1 --query launch.ini
```

### Mutate an image
```powershell
rgh xtaf mkdir --image .\Hdd1.img --path \XeCLI
rgh xtaf put --image .\Hdd1.img --path \XeCLI\readme.txt --in .\readme.txt --overwrite
rgh xtaf mv --image .\Hdd1.img --path \XeCLI\readme.txt --to \XeCLI\readme-old.txt
rgh xtaf rm --image .\Hdd1.img --path \XeCLI --recursive
```

### Format and repair
```powershell
rgh xtaf format --image .\blank-hdd.img --auto-confirm
rgh xtaf check --image .\Hdd1.img --partition Content
rgh xtaf repair --image .\Hdd1.img --partition Content --auto-confirm
rgh xtaf metadata backup --image .\Hdd1.img --out .\xtaf-meta
rgh xtaf metadata restore --image .\Hdd1.img --manifest .\xtaf-meta\fatman-metadata.json --auto-confirm
```

### Export a file or folder
```powershell
rgh xtaf cat --image .\Hdd1.img --partition Hdd1 --path launch.ini
rgh xtaf get --image .\Hdd1.img --partition Hdd1 --path launch.ini --out .\launch.ini
rgh xtaf extract --image .\Hdd1.img --partition Hdd1 --path Content\0000000000000000 --out .\content
rgh xtaf dump --image .\Hdd1.img --out .\recovery
```

## Notes

XTAF is separate from the live-console `fs`, `ftp`, `save`, and `content` workflows.
Those branches operate on the console over XBDM or FTP. FATX is for local disks and images.

Validation note:

- the runtime has been verified against a synthetic FATX fixture image
- the retail-layout formatter path has been verified against a large sparse 20 GB test image, including `partitions`, `list`, `mkdir`, `put`, `cat`, `check`, `repair`, and metadata backup/restore
- manual-open and scan flows were also validated against a nonstandard AMPED HDD image, where XTAF surfaced real `XTAF` offsets and opened the compatibility volume by bounded offset
- image-backed `mkdir`, `put`, `mv`, `rm`, `list`, and `cat` were also validated against a synthetic FATX image in local testing

## Related Pages

- [Commands Reference](Commands.md)
- [CLI Help Output](CLI-Help.md)
- [Home](Home.md)
- [Documentation Standards](Standards.md)
