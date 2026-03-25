# XeCLI Wiki

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for XBDM, JRPC2, FTP, XTAF, XeLL-backed backups, XEX dumping, memory inspection, and automation. This GitHub wiki is the primary reference for the `rgh` CLI, bundled metadata, and release workflows.

These pages are maintained directly in the repository's Markdown wiki source so the public documentation stays aligned with the shipped command surface.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

Avatar workflows in the shipped release now support both:

- `rgh avatar choose` for a terminal game/item picker
- `rgh avatar browse` for a Windows picker with search, filters, multi-select, and install

`rgh avatar install` remains the direct path for explicit one-item or full-title installs. `--remote` switches the same workflow to the hosted `Avatar-Item-Collection` repository with local caching.

Native XeLL workflows are also built into the shipped CLI now. `rgh xell ...` and `rgh nand dump` handle guided XeLL launch, keyvault export, and verified read-only NAND backup without depending on an external flasher workflow.

v1.0.8 builds on that by promoting `rgh xtaf` as the primary FATX/XTAF surface, separating `XeCLI-XellFetch` into its own standalone release path, and polishing the Windows installer and wiki for a cleaner public release.

Local content workflows are built in now as well. `rgh con`, `rgh profile`, and `rgh xdbf` cover pulled CON/profile/GPD files directly, including rehash/resign, raw `Account` or GPD extraction, achievement and setting edits, and avatar color edits inside profile packages.

## Latest Features

The current features worth surfacing first are:

- [Releases](Releases) for the full patch-notes archive across all public versions.
- [Latest Features](Latest-Features) for the current workflow highlights and release timeline.
- [v1.0.8 Release Notes](Release-Notes-v1.0.8) for the XTAF command-surface promotion, installer polish, and release packaging split.
- [XeCLI-XellFetch](XeCLI-XellFetch) for the payload model, guided XeLL launch, keyvault export, and verified NAND backup.
- [FTP and File Transfer](FTP-and-File-Transfer) for saved FTP targets and the full `rgh ftp ...` workflow.
- [Avatar Item Collection](Avatar-Item-Collection) for local or hosted avatar downloads, browsing, and install planning.
- [Integrations](Integrations) for terminal-agent, script-driven, and companion-tool workflows.
- [Advanced Guide](Advanced-Guide) for live debugging, memory inspection, XEX dumping, and reverse-engineering workflows.
- [Reverse Engineering](Reverse-Engineering) for Ghidra and IDA headless support, requirements, and helper-loader install notes.

## Documentation Index

### Getting Started
| Page | Purpose |
| --- | --- |
| [Latest Features](Latest-Features) | Current workflow highlights plus the full release timeline and feature map |
| [Releases](Releases) | Canonical patch-notes archive for all public XeCLI releases |
| [v1.0.8 Release Notes](Release-Notes-v1.0.8) | XTAF promotion, installer polish, and the standalone XeCLI-XellFetch packaging split |
| [Beginner Guide](Beginner-Guide) | Safe first-run workflow: install, discovery, connect, status, and basic operations |
| [XeCLI-XellFetch](XeCLI-XellFetch) | Managed XeLL payload workflow, HTTP endpoint inspection, keyvault export, and verified read-only NAND backup |
| [FTP and File Transfer](FTP-and-File-Transfer) | Saved FTP targets, browse/find/get/put workflows, and when to use FTP instead of `rgh fs` |
| [Commands Reference](Commands) | Full command-by-command reference with examples |
| [CLI Help Output](CLI-Help) | Exact built-in `rgh help` output and top-level branch help screens |
| [Hardware and System Controls](Hardware-and-System) | Sign-in state, ring-light LEDs, fan commands, and SMC version notes |
| [Remote Spoofing](Remote-Spoofing) | In-game gamertag, XUID, and remote-slot spoofing for supported titles; BO2 local GT, local XUID, and remote spoofing are supported as in-title memory writes |
| [XNotify](XNotify) | Notification usage, icon IDs, and direct integration notes |
| [Homebrew and USB](Homebrew-and-USB) | USB/folder staging or direct console installs for Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch |
| [Original Xbox Compatibility](Original-Xbox-Compatibility) | XeFu pack selection, HddX targeting, and optional HDD Compatibility Partition Fixer staging |
| [XTAF](FATX-Manager) | FATX/XTAF image and storage recovery with `rgh xtaf`, plus `rgh fatman` / `rgh fatx` compatibility aliases |
| [Avatar Item Collection](Avatar-Item-Collection) | Local and hosted avatar corpus naming, browser flows, layout, and install model |
| [Troubleshooting](Troubleshooting) | Failure cases, common console/plugin issues, and recovery paths |

### Technical Reference
| Page | Purpose |
| --- | --- |
| [Advanced Guide](Advanced-Guide) | Reverse engineering, memory workflows, debugger control, and automation usage |
| [Reverse Engineering](Reverse-Engineering) | Ghidra and IDA headless support, external requirements, and helper-loader install flows |
| [Frameworks and Architecture](Frameworks) | Command architecture, transport layers, and design decisions |
| [Documentation Standards](Standards) | Structure, conventions, and maintenance rules for this wiki |
| [Contributing](Contributing) | Contribution expectations for code, docs, validation, and release prep |

### Data and Integrations
| Page | Purpose |
| --- | --- |
| [Integrations](Integrations) | Reusing XeCLI from scripts, terminal-agent sessions, and external tools |
| [Title ID Database](Title-ID-Database) | Bundled metadata files and how other tools can consume them |
| [FAQ](FAQ) | Short answers to common setup and usage questions |

## Recommended Reading Paths

### New user path
1. [Beginner Guide](Beginner-Guide)
2. [Latest Features](Latest-Features)
3. [XeCLI-XellFetch](XeCLI-XellFetch)
4. [Commands Reference](Commands)
5. [Hardware and System Controls](Hardware-and-System)
6. [Remote Spoofing](Remote-Spoofing)
7. [XNotify](XNotify)
8. [CLI Help Output](CLI-Help)
9. [Troubleshooting](Troubleshooting)
10. [Homebrew and USB](Homebrew-and-USB)
11. [Original Xbox Compatibility](Original-Xbox-Compatibility)
12. [XTAF](FATX-Manager)

### Reverse-engineering path
1. [Advanced Guide](Advanced-Guide)
2. [Frameworks and Architecture](Frameworks)
3. [Commands Reference](Commands)
4. [Title ID Database](Title-ID-Database)

### Contributor path
1. [Contributing](Contributing)
2. [Documentation Standards](Standards)
3. [Releases](Releases)
4. [Frameworks and Architecture](Frameworks)
5. [CLI Help Output](CLI-Help)

## Capability Map

### Console operations
- Discovery, target persistence, and quick connection workflows
- Status, title resolution, profile visibility, and pulled-profile editing
- Sign-in state, ring-of-light LED control, manual fan commands, and SMC version probing
- Launch, reboot, and console notification workflows
- Guided XeLL launch, XeLL HTTP endpoint inspection, keyvault export, and verified read-only NAND backup
- v1.0.8 adds XTAF-first disk workflows, cleaner standalone payload packaging, and installer polish on top of the verified NAND dump flow
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
- XTAF device discovery, partition inspection, header scan, manual offset-open for strange images, retail-layout format, metadata backup/restore, safe repair, directory export, and raw partition dump workflows
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
- Command-based reuse from scripts, terminal agents, and companion tools
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
- direct Windows mount-driver workflows

That means the current `xtaf` surface focuses on disk and image analysis, repair, and export rather than a driver-backed mount model.

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
.\XeCLI-1.0.8-setup-win-x64.exe
rgh --help
rgh language
rgh start
rgh homebrew install all --usb E: --auto-confirm
rgh homebrew install aurora --device Hdd1 --ini-mode merge
rgh ogxbox list
rgh ogxbox install hacked --include-fixer --usb E:
rgh xtaf devices
rgh xtaf partitions --image .\hdd.img
rgh xtaf scan --image .\hdd.img
rgh xtaf list --image .\hdd.img --partition Content --path /
rgh xtaf get --image .\hdd.img --partition Content --path /launch.ini --out .\launch.ini
rgh xtaf cat --image .\hdd.img --partition Content --path /launch.ini
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

## Release and Documentation Entry Points
- [Repository](https://github.com/SaveEditors/xecli)
- [GitHub Wiki](https://github.com/SaveEditors/xecli/wiki)
- [Latest Release](https://github.com/SaveEditors/xecli/releases/latest)

## Release Packaging
The published Windows release is a self-contained `win-x64` package.

That means:

- `rgh.exe` runs without a separate .NET install
- runtime files ship beside the executable in the release folder
- `ConsoleDependencies/`, `Assets/`, `ghidra_scripts/`, and `ida_scripts/` ship in the same release package
- `xbdm.xex`, `XDRPC.xex`, and `JRPC2.xex` are also exposed at the repo root for direct download/reference
- the published setup executable can register `rgh`, set the initial UI language, and offer immediate console discovery after setup

Only source builds require a local .NET 10 SDK/runtime.
