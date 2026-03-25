# Latest Features

This page highlights the current XeCLI workflows that are most worth surfacing in the README, the wiki sidebar, and the docs landing page.

## v1.0.6 Automated NAND Dumping

XeCLI v1.0.6 turns NAND dumping into a PC-side one-command workflow. `rgh nand dump` launches or re-attaches to XeLL, stages the helper/linker assets the backup path needs, performs the reference and verification reads, packages the result, and reboots automatically only after a verified success.

If you want the stripped boot package instead of the full desktop bundle, the standalone `Xell-NoN` companion package can serve as the minimal XeLL-side bootstrap for the same workflow.

Read [XeLL and NAND Backups](XeLL-and-NAND-Backups.md) for the full flow, safety notes, and verification model.

## Native XeLL Backup Workflows

XeCLI now has a native XeLL workflow instead of treating NAND and key backup as an external afterthought. `rgh xell ...` detects whether the console is on the dashboard or already in XeLL, asks before the first automatic launch into XeLL, and then works against the XeLL HTTP service directly.

Use:

```powershell
rgh xell boot
rgh xell info
rgh xell kv export
rgh nand dump
```

The backup path is intentionally read-only. `rgh nand dump` takes a reference dump, then either reboots back into XeLL or performs the verification read in the same XeLL session when the active custom payload would otherwise route through the dashboard and introduce drift. After a verified match, XeCLI validates the copied output, SHA-256 manifest, and final zip archive before it sends the final reboot token. `rgh xell kv export` packages the keyvault with `CPUKEY.txt`, `KV+CPU KEY.txt`, optional fuse text, and a verified archive.

XeCLI supports both the older `/rawflash` style and the current XeLL Reloaded endpoint set `/FLASH`, `/FUSE`, `/KV`, `/KVRAW`, `/KVRAW2`, `/LOG`, and `/REBOOT`.

Read [XeLL and NAND Backups](XeLL-and-NAND-Backups.md) for the full workflow and safety model.

## Local CON, Profile, and GPD Workflows

XeCLI now has a practical local-content surface for pulled Xbox 360 containers instead of forcing that work into external tools. `rgh con` handles package metadata, verification, rehash, resign, magic-name, and FATX-path derivation. `rgh profile` handles decoded profile editing. `rgh xdbf` handles raw record inspection when you need to drop down to the GPD/XDBF level.

Use:

```powershell
rgh con info .\E00012AA8D7879B4.con
rgh con verify .\E00012AA8D7879B4.con
rgh profile info .\E00012AA8D7879B4.con
rgh profile account extract .\E00012AA8D7879B4.con .\Account
rgh profile gpd list .\E00012AA8D7879B4.con
rgh profile achievements unlock .\E00012AA8D7879B4.con --titleid 415607E7 --achievementid 0x00000004
rgh profile settings set .\E00012AA8D7879B4.con 0x10040006 1337
rgh profile avatar-colors set .\E00012AA8D7879B4.con --hair 0xFF112233
rgh xdbf sync-status .\FFFE07D1.gpd
```

The profile path includes targeted extraction for the raw `Account` payload and for embedded dashboard/title GPD files, so you no longer need a full package extract just to inspect `FFFE07D1.gpd` or one title record set. Mutating profile commands write back into the local container, refresh STFS hashes, and leave the CON verifiable.

This surface has been validated against real pulled profile containers rather than only synthetic fixtures.

## FTP and File Work

XeCLI includes a practical FTP workflow instead of treating FTP as an afterthought. The current command set covers saved targets, browsing, search, file pull and push, directory creation, rename/move, delete, and file printing.

Use:

```powershell
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
rgh ftp list --path /Hdd1/
rgh ftp find --path /Hdd1/ --name default.xex
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini
rgh ftp put --in .\plugin.xex --path /Hdd1/Plugins/plugin.xex
```

Read [FTP-and-File-Transfer.md](FTP-and-File-Transfer.md) for the full workflow.

## Avatar Downloader and Installer

XeCLI can work from a local `Avatar-Item-Collection` corpus or the hosted GitHub-backed collection. `--remote` turns the avatar commands into a downloader-backed workflow with local caching and the same ownership patch and install path used by local packages.

Use:

```powershell
rgh avatar games --remote --search "Black Ops"
rgh avatar browse --remote
rgh avatar install --remote --titleid 415608C3 --all --current-user
```

Read [Avatar-Item-Collection.md](Avatar-Item-Collection.md) for the full command surface.

## Terminal Agents and Automation

XeCLI works well from terminal agents and scripted workflows because the command surface is explicit and many commands expose `--json`. That makes it a good fit for repeatable console automation, file collection, screenshots, and reverse-engineering pipelines.

Use:

```powershell
rgh status --json
rgh title --json
rgh screenshot --out .\screen.bmp
rgh xex dump --out .\title.xex
```

Read [Integrations.md](Integrations.md) for guidance on agent and external-tool usage.

## Live Debugging and Reverse Engineering

XeCLI remains strongest in live console inspection and reverse-engineering handoff:

- modules
- memory reads, writes, and freeze-assisted searches
- threads and execution control
- screenshots
- XEX dump and strings
- Ghidra headless analysis and verification
- IDA Pro 9.1.250226 headless import, decompile, and verification

Use:

```powershell
rgh modules list
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh debug watch
rgh ghidra decompile --running --out .\decomp
rgh xex ida-decompile --running --out .\ida-decomp --max 10
```

`threads list` now falls back to loaded-image metadata when XBDM does not publish real thread names. The table exposes `Image`, `Start Addr.`, and `End Addr.` so the operator still gets usable execution context.

Ghidra is external and `(Free)`. IDA Pro `9.1.250226` is external and required for the IDA debugger/decompiler path. After you configure the tool path, XeCLI can install the supported XEX loader helpers with `rgh ghidra install-loader` and `rgh ida install-loader`.

Read [Advanced-Guide.md](Advanced-Guide.md) for the broader workflow guidance.
