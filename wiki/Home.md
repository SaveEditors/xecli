# XeCLI Wiki

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for XBDM, JRPC2, FTP, Fatman, XeLL-backed backups, XEX dumping, memory inspection, and automation. This wiki is the primary reference for the `rgh` CLI, bundled metadata, and release workflows.

The local source tree is the canonical behavior model for these pages. If a feature is documented here, it should match the shipped release and the current repo state unless a page says otherwise.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

Avatar workflows in the shipped release now support both:

- `rgh avatar choose` for a terminal game/item picker
- `rgh avatar browse` for a Windows picker with search, filters, multi-select, and install

`rgh avatar install` remains the direct path for explicit one-item or full-title installs. `--remote` switches the same workflow to the hosted `Avatar-Item-Collection` repository with local caching.

Native XeLL workflows are also built into the shipped CLI now. `rgh xell ...` and `rgh nand dump` handle guided XeLL launch, keyvault export, and verified read-only NAND backup without depending on an external flasher workflow.

v1.0.6 expands that into a one-command PC workflow for automated NAND dumping, with the release notes documenting the packaging and safety model in more detail.

Local content workflows are built in now as well. `rgh con`, `rgh profile`, and `rgh xdbf` cover pulled CON/profile/GPD files directly, including rehash/resign, raw `Account` or GPD extraction, achievement and setting edits, and avatar color edits inside profile packages.

## Latest Features

The current features worth surfacing first are:

- [XeLL and NAND Backups](XeLL-and-NAND-Backups.md) for guided XeLL launch, keyvault export, and verified NAND backup.
- [FTP and File Transfer](FTP-and-File-Transfer.md) for saved FTP targets and the full `rgh ftp ...` workflow.
- [Avatar Item Collection](Avatar-Item-Collection.md) for local or hosted avatar downloads, browsing, and install planning.
- [Integrations](Integrations.md) for Claude, Codex, and other terminal-agent or script-driven workflows.
- [Advanced Guide](Advanced-Guide.md) for live debugging, memory inspection, XEX dumping, and reverse-engineering workflows.
- [Reverse Engineering](Reverse-Engineering.md) for Ghidra and IDA headless support, requirements, and helper-loader install notes.
- [Latest Features](Latest-Features.md) for a concise overview page that links these workflows together.

## Documentation Index

### Getting Started
| Page | Purpose |
| --- | --- |
| [Latest Features](Latest-Features.md) | Current standout workflows to surface in the README, sidebar, and docs landing pages |
| [v1.0.6 Release Notes](Release-Notes-v1.0.6.md) | Automated NAND dumping, managed staging, verification behavior, and the optional Xell-NoN companion package |
| [Beginner Guide](Beginner-Guide.md) | Safe first-run workflow: install, discovery, connect, status, and basic operations |
| [XeLL and NAND Backups](XeLL-and-NAND-Backups.md) | Guided XeLL launch, HTTP endpoint inspection, keyvault export, and verified read-only NAND backup |
| [FTP and File Transfer](FTP-and-File-Transfer.md) | Saved FTP targets, browse/find/get/put workflows, and when to use FTP instead of `rgh fs` |
| [Commands Reference](Commands.md) | Full command-by-command reference with examples |
| [CLI Help Output](CLI-Help.md) | Exact built-in `rgh help` output and top-level branch help screens |
| [Hardware and System Controls](Hardware-and-System.md) | Sign-in state, ring-light LEDs, fan commands, and SMC version notes |
| [Remote Spoofing](Remote-Spoofing.md) | In-game gamertag, XUID, and remote-slot spoofing for supported titles; BO2 local GT, local XUID, and remote spoofing are supported as in-title memory writes |
| [XNotify](XNotify.md) | Notification usage, icon IDs, and direct integration notes |
| [Homebrew and USB](Homebrew-and-USB.md) | USB/folder staging or direct console installs for Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch |
| [Original Xbox Compatibility](Original-Xbox-Compatibility.md) | XeFu pack selection, HddX targeting, and optional HDD Compatibility Partition Fixer staging |
| [Fatman](FATX-Manager.md) | Read-only FATX image and storage recovery with `rgh fatman` or the `rgh fatx` alias |
| [Avatar Item Collection](Avatar-Item-Collection.md) | Local and hosted avatar corpus naming, browser flows, layout, and install model |
| [Troubleshooting](Troubleshooting.md) | Failure cases, common console/plugin issues, and recovery paths |

### Technical Reference
| Page | Purpose |
| --- | --- |
| [Advanced Guide](Advanced-Guide.md) | Reverse engineering, memory workflows, debugger control, and automation usage |
| [Reverse Engineering](Reverse-Engineering.md) | Ghidra and IDA headless support, external requirements, and helper-loader install flows |
| [Frameworks and Architecture](Frameworks.md) | Command architecture, transport layers, and design decisions |
| [Documentation Standards](Standards.md) | Structure, conventions, and maintenance rules for this wiki |
| [Contributing](Contributing.md) | Contribution expectations for code, docs, validation, and release prep |

### Data and Integrations
| Page | Purpose |
| --- | --- |
| [Integrations](Integrations.md) | Reusing XeCLI from scripts, Claude/Codex sessions, and external tools |
| [Title ID Database](Title-ID-Database.md) | Bundled metadata files and how other tools can consume them |
| [FAQ](FAQ.md) | Short answers to common setup and usage questions |

## Recommended Reading Paths

### New user path
1. [Beginner Guide](Beginner-Guide.md)
2. [Latest Features](Latest-Features.md)
3. [XeLL and NAND Backups](XeLL-and-NAND-Backups.md)
4. [Commands Reference](Commands.md)
5. [Hardware and System Controls](Hardware-and-System.md)
6. [Remote Spoofing](Remote-Spoofing.md)
7. [XNotify](XNotify.md)
8. [CLI Help Output](CLI-Help.md)
9. [Troubleshooting](Troubleshooting.md)
10. [Homebrew and USB](Homebrew-and-USB.md)
11. [Original Xbox Compatibility](Original-Xbox-Compatibility.md)
12. [Fatman](FATX-Manager.md)

### Reverse-engineering path
1. [Advanced Guide](Advanced-Guide.md)
2. [Frameworks and Architecture](Frameworks.md)
3. [Commands Reference](Commands.md)
4. [Title ID Database](Title-ID-Database.md)

### Contributor path
1. [Contributing](Contributing.md)
2. [Documentation Standards](Standards.md)
3. [Frameworks and Architecture](Frameworks.md)
4. [CLI Help Output](CLI-Help.md)

## Capability Map

### Console operations
- Discovery, target persistence, and quick connection workflows
- Status, title resolution, profile visibility, and pulled-profile editing
- Sign-in state, ring-of-light LED control, manual fan commands, and SMC version probing
- Launch, reboot, and console notification workflows
- Guided XeLL launch, XeLL HTTP endpoint inspection, keyvault export, and verified read-only NAND backup
- v1.0.6 automated NAND dumping from the PC, including managed helper/linker staging and automatic reboot on verified success
- Title-aware gamertag, XUID, and remote-player spoofing for supported games, with BO2 documented as a title-local spoof flow rather than a signed-in account change
- Terminal and Windows avatar browsing, remote-hosted downloads, and console-side avatar item installs

### Live inspection and debugging
- Module list, info, dump, load, unload, and pending verification
- Memory dump, hexdump, peek, poke, watch, strings, and search
- Thread list, context, suspend, and resume
- Debug stop/go, breakpoints, databreaks, and event watch

### Storage and content
- XBDM file-system operations
- FTP-based browsing, transfer, search, and content discovery
- Local CON/profile/GPD inspection and editing with `rgh con`, `rgh profile`, and `rgh xdbf`
- Fatman device discovery, partition inspection, directory browsing, read-only search, and file export
- Save extraction and injection
- DashLaunch plugin slot management
- XeLL-backed read-only NAND backup, keyvault export, CPU key capture, startup-log capture, and packaged SHA-256 manifests
- USB/folder staging or direct console installs with `rgh homebrew install aurora|dashlaunch|xexmenu|fsd|xm360|timefixer|simple360|xelllaunch|all`
- Original Xbox compatibility staging or direct `HddX:\Compatibility` installs with `rgh ogxbox install hacked|hud|retail`, plus optional HDD Compatibility Partition Fixer staging
- Avatar Item Collection cataloging, remote browsing, search, dry-run planning, progress-driven installs, and install execution

### Analysis and packaging
- Running XEX dump and string extraction
- Ghidra headless analysis and decompile export
- IDA Pro 9.1.250226 headless import, decompile, and verification workflows
- ISO to Games on Demand conversion with watchdog mode
- Bundled Title ID metadata for richer output and external tool reuse
- Command-based reuse from scripts, Claude/Codex, and companion tools
- Avatar item catalog reuse for external launchers, installers, and companion tools

## Scope Boundaries
XeCLI is strong in live-console workflows. It does not currently claim to be:

- a NAND flasher
- a XeBuild replacement
- a glitch-chip programmer
- a full trace debugger

Current out-of-scope areas:

- NAND write or flash operations
- image building or dashboard patching
- glitch timing programming
- trace/step debugging
- Fatman write, format, mount, repartition, or repair workflows

## Important Paths

### Bundled assets
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`
- `src/Xbox360.Remote.Cli/ghidra_scripts/DecompileAllToC.java`
- `src/Xbox360.Remote.Cli/ida_scripts/`

### Runtime state
- `%APPDATA%\XeCLI\config.json`
- `%LOCALAPPDATA%\XeCLI\cache`
- `%APPDATA%\XeCLI\titleids.local.csv`

## Common Starting Commands
```powershell
.\rgh.exe install
rgh --help
rgh start
rgh homebrew install all --usb E: --auto-confirm
rgh homebrew install aurora --device Hdd1 --ini-mode merge
rgh ogxbox list
rgh ogxbox install hacked --include-fixer --usb E:
rgh fatman devices
rgh fatman partitions --image .\hdd.img
rgh fatman scan --image .\hdd.img
rgh fatman list --image .\hdd.img --partition Content --path /
rgh fatman get --image .\hdd.img --partition Content --path /launch.ini --out .\launch.ini
rgh fatman cat --image .\hdd.img --partition Content --path /launch.ini
rgh ping
rgh status
rgh title
rgh xell info
rgh xell kv export
rgh nand dump
rgh con info .\E00012AA8D7879B4.con
rgh profile info .\E00012AA8D7879B4.con
rgh profile gpd list .\E00012AA8D7879B4.con
rgh xdbf list .\FFFE07D1.gpd --show-sync
rgh avatar library show
rgh avatar games --search Black Ops
rgh avatar items --titleid 415608C3 --limit 10
rgh avatar choose --search Black Ops --current-user
rgh avatar browse --remote
rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user
rgh modules list
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh screenshot --out .\screen.bmp
```

## Release and Docs Entry Points
- [Repository](https://github.com/SaveEditors/xecli)
- [Latest Release](https://github.com/SaveEditors/xecli/releases/latest)
- [Docs Landing Page](https://saveeditors.github.io/xecli/)
- [Published Wiki Home](https://saveeditors.github.io/xecli/wiki/Home.html)

## Release Packaging
The published Windows release is a self-contained `win-x64` package.

That means:

- `rgh.exe` runs without a separate .NET install
- runtime files ship beside the executable in the release folder
- `ConsoleDependencies/`, `Assets/`, `ghidra_scripts/`, and `ida_scripts/` ship in the same release package
- `xbdm.xex`, `XDRPC.xex`, and `JRPC2.xex` are also exposed at the repo root for direct download/reference
- `rgh install` can copy the release to a chosen install folder, register `rgh`, and offer immediate console discovery after setup

Only source builds require a local .NET 10 SDK/runtime.
