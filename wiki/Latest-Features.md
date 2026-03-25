# Latest Features

This page is the high-level product and release overview for XeCLI. It highlights the current workflow pillars, the newest public release, and the full release timeline so the wiki surfaces the entire shipped feature arc instead of a single point release.

## Current Release Focus: v1.0.7

XeCLI v1.0.7 pairs the automated NAND workflow with full Spanish localization, the new Inno Setup installer (dark themed, branded, Ko-fi friendly), and the canonical GitHub wiki so operators read the latest docs without the obsolete HTML site.

`rgh nand dump` still launches or re-attaches to XeLL, stages the helper and linker assets the backup path needs, performs the reference and verification reads, packages the result, and reboots automatically only after a verified success.

If you want the stripped boot package instead of the full desktop bundle, the standalone `XeCLI-XeLL` companion package can serve as the minimal XeLL-side payload bundle for the same workflow.

Read [XeCLI-XeLL](XeCLI-XeLL) for the payload model, workflow boundaries, safety notes, and verification behavior.

## Current Workflow Pillars

### XeCLI-XeLL Workflow

XeCLI has a native XeLL workflow instead of treating NAND and key backup as an external handoff. `rgh xell ...` detects whether the console is on the dashboard or already in XeLL, asks before the first automatic launch into XeLL, and then works against the XeLL HTTP service directly.

Use:

```powershell
rgh xell boot
rgh xell info
rgh xell kv export
rgh nand dump
```

The backup path is intentionally read-only. `rgh nand dump` takes a reference dump, then either reboots back into XeLL or performs the verification read in the same XeLL session when the active custom payload would otherwise route through the dashboard and introduce drift. After a verified match, XeCLI validates the copied output, SHA-256 manifest, and final zip archive before it sends the final reboot token. `rgh xell kv export` packages the keyvault with `CPUKEY.txt`, `KV+CPU KEY.txt`, optional fuse text, and a verified archive.

XeCLI supports both the older `/rawflash` style and the current XeLL Reloaded endpoint set `/FLASH`, `/FUSE`, `/KV`, `/KVRAW`, `/KVRAW2`, `/LOG`, and `/REBOOT`.

Read [XeCLI-XeLL](XeCLI-XeLL) for the full workflow, payload model, and safety boundaries.

### Local CON, Profile, and GPD Workflows

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

### FTP, Console Content, and Device Work

XeCLI includes a practical FTP workflow instead of treating FTP as an afterthought. The current command set covers saved targets, browsing, search, file pull and push, directory creation, rename/move, delete, and file printing.

Use:

```powershell
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
rgh ftp list --path /Hdd1/
rgh ftp find --path /Hdd1/ --name default.xex
rgh ftp get --path /Hdd1/launch.ini --out .\launch.ini
rgh ftp put --in .\plugin.xex --path /Hdd1/Plugins/plugin.xex
```

That file-transfer surface sits beside `rgh fs`, `rgh content`, `rgh save`, `rgh plugin`, `rgh homebrew`, `rgh ogxbox`, and the broader Fatman storage workflow.

Read [FTP and File Transfer](FTP-and-File-Transfer), [Homebrew and USB](Homebrew-and-USB), [Original Xbox Compatibility](Original-Xbox-Compatibility), and [FATX Manager](FATX-Manager) for the full storage and deployment surface.

### Avatar Downloader and Installer

XeCLI can work from a local `Avatar-Item-Collection` corpus or the hosted GitHub-backed collection. `--remote` turns the avatar commands into a downloader-backed workflow with local caching and the same ownership patch and install path used by local packages.

Use:

```powershell
rgh avatar games --remote --search "Black Ops"
rgh avatar browse --remote
rgh avatar install --remote --titleid 415608C3 --all --current-user
```

Read [Avatar Item Collection](Avatar-Item-Collection) for the full command surface.

### Console Operations and Automation

XeCLI works well from terminal agents and scripted workflows because the command surface is explicit and many commands expose `--json`. That makes it a good fit for repeatable console automation, file collection, screenshots, and reverse-engineering pipelines.

Use:

```powershell
rgh status --json
rgh title --json
rgh screenshot --out .\screen.bmp
rgh xex dump --out .\title.xex
```

Read [Integrations](Integrations) for guidance on agent and external-tool usage.

### Live Debugging and Reverse Engineering

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

Read [Advanced Guide](Advanced-Guide) for the broader workflow guidance.

## Release Timeline

This is the short-form patch-notes index for the full public release line. The detailed archive lives in [Releases](Releases).

| Release | Primary Themes |
| --- | --- |
| `v1.0.7` | Spanish localization, dark Inno Setup installer, Ko-fi support, and the canonical GitHub wiki migration |
| `v1.0.6` | Automated NAND dumping, managed XeLL staging, verification gating, and safe reboot control |
| `v1.0.5` | Local CON/profile/XDBF workflows, thread metadata fallback, installer hardening, and release validation |
| `v1.0.4` | IDA support, Ghidra helper-loader polish, and broad Fatman/FATX expansion |
| `v1.0.3` | Original Xbox compatibility staging and install workflows |
| `v1.0.2` | Installer split, homebrew package staging, and install-flow polish |
| `v1.0.1` | Avatar workflows, hardware controls, and early installer/homebrew integration |
| `v1.0.0` | Initial public CLI release with discovery, status, debugging, FTP, and analysis workflows |

## v1.0.7 Spanish Localization, Installer, and Wiki

- Spanish translations now cover the CLI, installer prompts, and the WinForms avatar browser, with `UiLanguage` in `config.json`, `--lang`, and `XECLI_LANG` fallbacks.
- The Inno Setup release is dark-themed, uses the XeCLI logo, features Ko-fi support, and enforces the installer wizard layout you already verified.
- The GitHub wiki is now the only documentation surface; the HTML wiki was removed, and `wiki/*.md` is the maintained source.

## Patch Notes Guidance

- Use [Releases](Releases) for the canonical patch-notes archive.
- Use [Release Notes v1.0.7](Release-Notes-v1.0.7) for the detailed breakdown of the newest release.
- Patch notes cover shipped product behavior and important fixes. They do not include README-only or wiki-only maintenance updates.
