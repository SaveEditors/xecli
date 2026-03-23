# Fatman

Fatman is XeCLI's FATX recovery, extraction, repair, and Windows-first disk-management layer for local Xbox 360 disks and images. It supports image-backed and raw-disk reads, exports, directory creation, file injection, rename/move, delete operations, retail-layout FATX formatting, low-level metadata backup/restore, and safe chain-map repair.

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
rgh fatman devices
rgh fatman disks
rgh fatman partitions
rgh fatman scan
rgh fatman info
rgh fatman list
rgh fatman find
rgh fatman cat
rgh fatman get
rgh fatman mkdir
rgh fatman put
rgh fatman mv
rgh fatman rm
rgh fatman format
rgh fatman check
rgh fatman repair
rgh fatman metadata backup
rgh fatman metadata restore
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

## Current Scope

Today Fatman can:

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
- restore low-level metadata regions from a Fatman manifest
- scan and repair safe chain-map issues such as orphaned allocations

It does not claim to:

- mount a virtual filesystem
- expose a true driver-backed Windows mount
- rewrite arbitrary security sectors beyond the metadata regions Fatman explicitly backs up and restores

## Example Workflows

### Inspect a disk
```powershell
rgh fatman disks
rgh fatman partitions --disk 2
rgh fatman info --disk 2 --partition Content
rgh fatman metadata backup --disk 2 --out .\fatman-meta
rgh fatman check --disk 2 --partition Content
rgh fatman repair --disk 2 --partition Content --auto-confirm
rgh fatman format --disk 2 --auto-confirm
```

### Inspect a disk image
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

### Mutate an image
```powershell
rgh fatman mkdir --image .\Hdd1.img --path \XeCLI
rgh fatman put --image .\Hdd1.img --path \XeCLI\readme.txt --in .\readme.txt --overwrite
rgh fatman mv --image .\Hdd1.img --path \XeCLI\readme.txt --to \XeCLI\readme-old.txt
rgh fatman rm --image .\Hdd1.img --path \XeCLI --recursive
```

### Format and repair
```powershell
rgh fatman format --image .\blank-hdd.img --auto-confirm
rgh fatman check --image .\Hdd1.img --partition Content
rgh fatman repair --image .\Hdd1.img --partition Content --auto-confirm
rgh fatman metadata backup --image .\Hdd1.img --out .\fatman-meta
rgh fatman metadata restore --image .\Hdd1.img --manifest .\fatman-meta\fatman-metadata.json --auto-confirm
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
- the retail-layout formatter path has been verified against a large sparse 20 GB test image, including `partitions`, `list`, `mkdir`, `put`, `cat`, `check`, `repair`, and metadata backup/restore
- manual-open and scan flows were also validated against a nonstandard AMPED HDD image, where Fatman surfaced real `XTAF` offsets and opened the compatibility volume by bounded offset
- image-backed `mkdir`, `put`, `mv`, `rm`, `list`, and `cat` were also validated against a synthetic FATX image in local testing

## Related Pages

- [Commands Reference](Commands.md)
- [CLI Help Output](CLI-Help.md)
- [Home](Home.md)
- [Documentation Standards](Standards.md)
