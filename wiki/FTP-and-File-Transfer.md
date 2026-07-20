# FTP and File Transfer

This page documents XeCLI's FTP workflow and how it fits with the XBDM file-system commands.

FTP commands require an active console FTP service, such as Aurora, Freestyle Dash/FSD, XexMenu, or a DashLaunch FTP plugin. When FTP is not running, XBDM-backed `rgh fs ...` workflows can still work, and XBDM or JRPC2 status commands may still show the console as reachable.

If you only need to confirm the listener, run `rgh ftp check` or `rgh ftp doctor` first. It checks port 21 and helps confirm whether a compatible dashboard FTP service is running.

If XeCLI can see the active title and it is not a known FTP-capable dashboard or plugin, the FTP commands will refuse before opening a session. That warning is deliberate: XBDM can still work in games even when FTP is not present. Run `rgh ftp check` or `rgh ftp doctor` to check port 21 before you retry.

## FTP Requirements

Common FTP sources on an Xbox 360 RGH/JTAG console include Aurora, Freestyle Dash/FSD, XexMenu, and the DashLaunch FTP plugin. The stock NXE dashboard and normal game sessions usually do not provide an FTP listener.

If FTP is unavailable while the console remains reachable through XBDM or JRPC2:

1. Boot a dashboard or launcher that provides FTP, or confirm that the DashLaunch FTP plugin is loaded.
2. Run `rgh ftp check` or `rgh ftp doctor` and confirm that port 21 answers.
3. Verify the saved IP address, port, username, and password with `rgh ftp target`.
4. Confirm that Windows Firewall and the local network are not blocking port 21.

The GUI can show the console as connected while its FTP surface remains unavailable. That means the XBDM connection succeeded but an FTP service was not detected.

## Why FTP Matters In XeCLI

FTP is the practical path for many day-to-day tasks:

- browse storage quickly
- search for files by name
- pull configs, saves, and content metadata
- push plugins or payloads
- rename or reorganize files
- support save, content, and plugin workflows that already depend on FTP

## Save The FTP Target

Save an FTP target once:

```powershell
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
```

Inspect or clear it later:

```powershell
rgh ftp target
rgh ftp target --clear
```

XeCLI stores the saved FTP target in the normal per-user config so later commands can omit repeated connection flags. That saved target can differ from the live endpoint that full `rgh status` reports.

Run `rgh ftp target` with no arguments when you want to see the effective FTP target and the saved defaults side by side. That output makes it easy to confirm whether the active profile or the saved config is supplying the connection details.

### Credential storage and clearing

Saved FTP passwords are stored as readable text in XeCLI's local JSON configuration, not in Windows Credential Manager. Installed mode uses `%APPDATA%\XeCLI\config.json`; portable mode uses `UserData\config.json`; `XECLI_HOME` uses the directory it names. Named target profiles can also contain FTP credentials in `target-profiles.json` in the same configuration directory. Protect these files with your Windows account and do not place them in a shared or synchronized folder.

`rgh ftp target --clear` removes the saved default FTP port, username, and password from `config.json`. It does not remove credentials stored in named profiles; remove an unused profile separately:

```powershell
rgh target profile remove <name>
```

XeCLI redacts `--pass` values from its command log, but your shell may retain the original command line. Clear sensitive entries with your shell's own history controls, and review command logs, exported logs, and diagnostics before sharing them. Clearing an FTP target does not erase shell history or previously exported files.

FTP does not encrypt credentials or transferred data. Use it only on a trusted private network, and do not expose the console's FTP service through router port forwarding.

## Core FTP Commands

```powershell
rgh ftp check
rgh ftp list --path /Hdd1/
rgh ftp find --path /Hdd1/ --name default.xex
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini --verify-hash sha256
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini --resume
rgh ftp get --path /Hdd1/Content/415608C3 --out .\ContentBackup --recursive
rgh ftp put --in .\plugin.xex --path /Hdd1/Plugins/plugin.xex --verify-hash sha256
rgh ftp put --in .\plugin.xex --path /Hdd1/Plugins/plugin.xex --resume
rgh ftp put --in .\Plugins --path /Hdd1/Plugins --recursive --dry-run
rgh ftp sync --direction upload --path /Hdd1/Plugins --in .\Plugins --recursive --dry-run
rgh ftp sync --direction download --path /Hdd1/Content/415608C3 --in .\ContentBackup --recursive
rgh ftp cat --path /Hdd1/launch.ini
rgh ftp mkdir --path /Hdd1/Tools
rgh ftp mv --from /Hdd1/old.xex --to /Hdd1/new.xex
rgh ftp rm --path /Hdd1/temp.bin
```

Add `--verify-hash sha256|sha1|md5` to `ftp get` and `ftp put` when you want a post-transfer checksum comparison.

Single-file `ftp get --resume` and `ftp put --resume` continue partial transfers where supported. The `ftp sync` flow supports resumable single-file transfers in both directions, and directory-tree sync requires `--recursive`. When resume finds that the destination is already complete, `ftp sync` warns that existing files were skipped instead of rewriting them.

## Recursive Transfer Reporting

Use `--recursive` when downloading or uploading a directory tree. Recursive `ftp get` and `ftp put` report the transfer as a batch, including total files, planned files, skipped files, transferred files, failed files, and total bytes. Existing destination files are skipped unless you pass `--overwrite`.

```text
SUCCESS FTP upload complete
total=8 planned=6 skipped=2 transferred=6 failed=0 bytes=24.18 MB
6 file(s) -> /Hdd1/Plugins
```

JSON output uses the same summary fields and includes a `Files` array with one entry per file. Each entry includes its relative path, local path, remote path, size, status, optional error text, and optional verification result. XeCLI reduces private host-path details, but always review output before sharing it.

For upload planning, add `--dry-run` to preview the recursive transfer without writing remote files:

```powershell
rgh ftp put --in .\Plugins --path /Hdd1/Plugins --recursive --dry-run
rgh ftp put --in .\Plugins --path /Hdd1/Plugins --recursive --dry-run --json
```

The upload preview uses the same summary shape, with planned uploads and skipped existing files shown before any data is sent.

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

That path is useful when the console FTP service is not running or when you want the XBDM side of the session for consistency with other live-debug operations.

## Related Pages

- [Beginner-Guide.md](Beginner-Guide)
- [Commands.md](Commands)
- [Integrations.md](Integrations)
