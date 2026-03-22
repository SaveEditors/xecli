# FTP and File Transfer

This page documents XeCLI's FTP workflow and how it fits with the XBDM file-system commands.

## Why FTP Matters In XeCLI

FTP is the practical path for many day-to-day tasks:

- browse storage quickly
- search for files by name
- pull configs, saves, and content metadata
- push plugins or payloads
- rename or reorganize files
- support save, content, and plugin workflows that already depend on FTP

## Save The FTP Target

Set the default FTP target once:

```powershell
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
```

Inspect or clear it later:

```powershell
rgh ftp target
rgh ftp target --clear
```

XeCLI stores the saved FTP target in the normal per-user config so later commands can omit repeated connection flags.

## Core FTP Commands

```powershell
rgh ftp list --path /Hdd1/
rgh ftp find --path /Hdd1/ --name default.xex
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini
rgh ftp put --in .\plugin.xex --path /Hdd1/Plugins/plugin.xex
rgh ftp cat --path /Hdd1/launch.ini
rgh ftp mkdir --path /Hdd1/Tools
rgh ftp mv --from /Hdd1/old.xex --to /Hdd1/new.xex
rgh ftp rm --path /Hdd1/temp.bin
```

## Use FTP For Storage-Oriented Jobs

Prefer FTP when the job is mostly about files on disk:

- save extraction and injection
- installed content inventory
- plugin edits
- package staging
- pulling configs or logs

XeCLI's other storage-oriented commands build on the same FTP path:

```powershell
rgh save list --titleid FFFE07D1 --device Hdd1
rgh save extract --titleid 415608C3 --out .\saves
rgh content list --device Hdd1 --show-types
rgh plugin list
```

## When To Use XBDM FS Instead

Use `rgh fs ...` when you specifically want the XBDM transport instead of FTP:

```powershell
rgh fs list --path Hdd:\
rgh fs get --path Hdd:\launch.ini --out .\launch.ini
rgh fs put --in .\patch.bin --path Hdd:\patch.bin
```

That path is useful when FTP is unavailable or when you want the XBDM side of the session for consistency with other live-debug operations.

## Related Pages

- [Beginner-Guide.md](Beginner-Guide.md)
- [Commands.md](Commands.md)
- [Integrations.md](Integrations.md)
