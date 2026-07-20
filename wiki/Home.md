# XeCLI Wiki

<p align="center">
  <img src="assets/xecli-wordmark.png" alt="XeCLI logo" width="260">
</p>

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for live console work. Use `rgh.exe` in the terminal or `XeTerminal.exe` for the Windows GUI. This wiki is the primary reference for the shipped command surface, runtime metadata formats, and public release notes.

These pages track the released build so the public documentation stays aligned with what users can actually run.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

Both Windows packages ship `XeTerminal.exe`. The setup package adds Start menu and uninstall integration; the portable package runs directly from its extracted folder without making those changes.

XeCLI includes two avatar browsing workflows:

- `rgh avatar choose` for a terminal game/item picker
- `rgh avatar browse` for a Windows picker with search, filters, multi-select, and install

`rgh avatar install` is the direct path for explicit one-item or full-title installs. `--remote` switches the same workflow to the hosted `Avatar-Item-Collection` repository with local caching.

XeCLI provides native XeLL workflows through `rgh xell boot`, `rgh xell info`, `rgh xell kv export`, and `rgh xell nand dump`. `XeCLI-XellFetch` supplies the payload model and helper assets for the managed launch and backup flow.

XeTerminal provides scrolling, quick actions, command history search, debugger address mapping, and session diagnostics for RGH/JTAG work.

Local content workflows are built in as well. `rgh con`, `rgh profile`, and `rgh xdbf` cover pulled CON/profile/GPD files directly, including rehash/resign, raw `Account` or GPD extraction, achievement and setting edits, and avatar color edits inside profile packages.

## Start Here

Use these pages first:

- [Beginner Guide](Beginner-Guide) for package verification, installation, portable use, and the first console connection.
- [Releases](Releases) for the current release highlights and the full patch-notes archive across all public versions.
- [XeCLI-XellFetch](XeCLI-XellFetch) for the payload model, guided XeLL launch, keyvault export, and NAND backup with integrity checks.
- [FTP and File Transfer](FTP-and-File-Transfer) for saved FTP targets and the full `rgh ftp ...` workflow.
- [Avatar Item Collection](Avatar-Item-Collection) for local or hosted avatar downloads, browsing, and install planning.
- [Integrations](Integrations) for automation tools, scripts, and companion tools.
- [Advanced Guide](Advanced-Guide) for live debugging, memory inspection, XEX dumping, and reverse-engineering workflows.
- [Reverse Engineering](Reverse-Engineering) for Ghidra and IDA headless support, requirements, and helper-loader install notes.
- [Troubleshooting](Troubleshooting) for support bundles and common recovery paths.

## Install or Run Portable

XeCLI v2.0.0 publishes two self-contained x64 packages. Windows 11 x64 is recommended. On Windows 10, Microsoft's .NET 10 support is limited to eligible Enterprise/LTSC releases: versions 1607, 1809, and 21H2. The installer minimum remains build 14393 (Windows 10 version 1607), but that build floor alone does not expand Microsoft's supported edition list:

- `XeCLI-2.0.0-win-x64-setup.exe` for a guided install, Start menu entry, optional desktop shortcut, optional PATH registration, and Windows uninstall integration
- `XeCLI-2.0.0-win-x64.zip` for direct portable use with no setup changes by default

Verify either package against the matching `.sha256` sidecar from the same GitHub release before running it:

```powershell
$packagePath = ".\XeCLI-2.0.0-win-x64-setup.exe"
$expectedHash = ((Get-Content "${packagePath}.sha256" -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) { throw "SHA-256 mismatch. Do not run this package." }
"SHA-256 match: $actualHash"
```

The v2.0.0 packages are not Authenticode-signed, so Windows may show **Unknown Publisher** or Microsoft Defender SmartScreen. Proceed only after downloading from the official release and verifying the checksum. If setup changes PATH, open a new terminal before using `rgh`.

The portable package keeps state under `UserData` beside the executables while `xecli.portable` is present. `XECLI_HOME` overrides that location. Portable use does not register commands unless you explicitly run `rgh install`; that CLI workflow can copy a release package for the current user or all users and optionally configure command access, but it does not create the setup wizard's shortcuts or Windows uninstall entry. Update a portable copy by checking the new ZIP against its `.sha256` sidecar, extracting it into a new empty folder, and, after closing the old copy, copying only `UserData` when you want to retain portable state.

Setup upgrades must use the existing installation directory. If setup reports another or ambiguous location, or a missing ownership manifest, uninstall the existing copy while retaining settings and then reinstall into the desired directory. See the [Beginner Guide](Beginner-Guide) for the complete workflow and [Troubleshooting](Troubleshooting) for recovery steps.

## Documentation Index

### Getting Started
| Page | Purpose |
| --- | --- |
| [Releases](Releases) | Canonical patch-notes archive and current release-highlights page for all public XeCLI releases |
| [Beginner Guide](Beginner-Guide) | Safe first-run workflow: install, discovery, connect, status, and basic operations |
| [XeCLI-XellFetch](XeCLI-XellFetch) | Shipped XeLL payload workflow, HTTP endpoint inspection, keyvault export, and NAND backup with byte-for-byte comparison |
| [FTP and File Transfer](FTP-and-File-Transfer) | FTP requirements, saved targets, browse/find/get/put workflows, and when to use FTP instead of `rgh fs` |
| [Commands Reference](Commands) | Full command-by-command reference with examples |
| [CLI Help Output](CLI-Help) | Exhaustive generated help for every root command and supported shortcut tree |
| [Troubleshooting](Troubleshooting) | Diagnostics bundles, failure cases, common console/plugin issues, and recovery paths |
| [Hardware and System Controls](Hardware-and-System) | Sign-in state, ring-light LEDs, fan commands, and direct SMC telemetry |
| [XNotify](XNotify) | Notification usage, icon IDs, and direct integration notes |
| [Homebrew and USB](Homebrew-and-USB) | USB/folder staging or direct console installs for Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch |
| [Original Xbox Compatibility](Original-Xbox-Compatibility) | XeFu pack selection, HddX targeting, and optional HDD Compatibility Partition Fixer staging |
| [XTAF](FATX-Manager) | FATX/XTAF image and storage recovery with canonical `rgh xtaf` plus `rgh fatman` and `rgh fatx` aliases |
| [Avatar Item Collection](Avatar-Item-Collection) | Local and hosted avatar browsing, layout, and install model |

### Technical Reference
| Page | Purpose |
| --- | --- |
| [Advanced Guide](Advanced-Guide) | Reverse engineering, memory workflows, debugger control, and automation usage |
| [Reverse Engineering](Reverse-Engineering) | Ghidra and IDA headless support, external requirements, and helper-loader install flows |
| [Frameworks and Architecture](Frameworks) | Command architecture, transport layers, and design decisions |
| [Documentation Standards](Standards) | Maintainer guidance for keeping public documentation accurate and consistent |
| [Contributing](Contributing) | Public contribution expectations and privacy requirements |

### Data and Integrations
| Page | Purpose |
| --- | --- |
| [Integrations](Integrations) | Reusing XeCLI from scripts, automation tools, and external tools |
| [Title ID Database](Title-ID-Database) | Optional user metadata, source precedence, formats, and raw-ID fallback |
| [FAQ](FAQ) | Short answers to common setup and usage questions |

## Recommended Reading Paths

### New user path
1. [Beginner Guide](Beginner-Guide)
2. [Releases](Releases)
3. [XeCLI-XellFetch](XeCLI-XellFetch)
4. [Commands Reference](Commands)
5. [Hardware and System Controls](Hardware-and-System)
6. [XNotify](XNotify)
7. [CLI Help Output](CLI-Help)
8. [Troubleshooting](Troubleshooting)
9. [Homebrew and USB](Homebrew-and-USB)
10. [Original Xbox Compatibility](Original-Xbox-Compatibility)
11. [XTAF](FATX-Manager)

### Reverse-engineering path
1. [Advanced Guide](Advanced-Guide)
2. [Frameworks and Architecture](Frameworks)
3. [Commands Reference](Commands)
4. [Title ID Database](Title-ID-Database)

## Capability Map

### Console operations
- Discovery, target persistence, and quick connection workflows
- Status, title resolution, profile visibility, and pulled-profile editing
- Sign-in state, ring-of-light LED control, manual fan commands, and direct SMC telemetry
- Launch, reboot, and console notification workflows
- Guided XeLL launch, HTTP endpoint inspection, keyvault export, and NAND backup with byte-for-byte comparison
- XeTerminal scrolling, quick actions, command history search, live module address mapping, and session diagnostics
- Terminal and Windows avatar browsing, remote-hosted downloads, and console-side avatar item installs
- Offline local health checks with `rgh health`

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
- XeLL-integrated read-only NAND backup, keyvault export, CPU key capture, startup-log capture, and packaged SHA-256 manifests
- USB/folder staging or direct console installs with `rgh homebrew install aurora|dashlaunch|xexmenu|fsd|xm360|timefixer|simple360|xelllaunch|all`
- Original Xbox compatibility staging or direct `HddX:\Compatibility` installs with `rgh ogxbox install hacked|hud|retail`, plus optional HDD Compatibility Partition Fixer staging
- Avatar Item Collection cataloging, remote browsing, search, dry-run planning, progress-driven installs, and install execution
- Offline local health checks with `rgh health`, plus redacted local diagnostics bundles with `rgh diagnostics bundle` or `rgh diag bundle`

### Analysis and packaging
- Running XEX dump and string extraction
- Ghidra headless analysis and decompile export
- IDA Pro headless import, decompile, and verification workflows
- ISO to Games on Demand conversion with watchdog mode
- Optional user Title ID metadata with raw hexadecimal fallback
- Command-based reuse from scripts, automation tools, and companion tools
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
- `ghidra_scripts/DecompileAllToC.java`
- `ghidra_scripts/ExportSymbols.java`
- `ida_scripts/`

### Runtime state
- `%APPDATA%\XeCLI\config.json`
- `%LOCALAPPDATA%\XeCLI\cache`
- `%LOCALAPPDATA%\XeCLI\cache\logging\command-log.jsonl` (default installed command log)
- the Windows Pictures folder at `XeCLI\Captures` (default installed XeTerminal captures)
- `%APPDATA%\XeCLI\titleids.csv` (optional user file)
- `%APPDATA%\XeCLI\titleids.discovered.csv` (generated live-name cache)
- `UserData` beside the executables when `xecli.portable` is present
- the directory named by `XECLI_HOME` when that override is set

## Common Starting Commands
```powershell
.\XeCLI-2.0.0-win-x64-setup.exe
rgh --help
rgh health
rgh language
rgh start
rgh homebrew install all --usb E:\XeCLI-Stage
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
rgh diagnostics bundle
rgh xell boot
rgh xell info
rgh xell kv export
rgh xell nand dump
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
rgh screenshot --out .\screen.png
```

## Release and Documentation Entry Points
- [Repository](https://github.com/SaveEditors/xecli)
- [GitHub Wiki](https://github.com/SaveEditors/xecli/wiki)
- [Latest Release](https://github.com/SaveEditors/xecli/releases/latest)
- [Support XeCLI](https://ko-fi.com/xecli)

## Legal
- [Terms of Service](Terms-of-Service)
- [Privacy Policy](Privacy-Policy)

## Release Packaging
The published Windows release uses a self-contained `win-x64` installer and portable ZIP. XeCLI v2.0.0 supports 64-bit Windows; no 32-bit v2.0.0 artifact is published.

That means:

- `rgh.exe` runs without a separate .NET install in the portable package
- runtime files ship beside the executable in each release folder
- `Assets/`, `ghidra_scripts/`, and `ida_scripts/` ship in the same release package
- the project license, required third-party notices, matching-tag source directions, and integrity manifests ship with the package
- complete corresponding source remains available from the exact public release tag instead of being installed with the application
- optional console plugin files can be supplied by placing them in `ConsoleDependencies/` beside `rgh.exe`
- the portable ZIP ships with a matching `.sha256` file
- each portable ZIP extracts into one named XeCLI root folder instead of spilling files into the current directory
- each setup executable ships with a matching `.sha256` file and can optionally register `rgh` on PATH and set the initial UI language
- the setup and portable packages are unsigned, so Windows may identify the publisher as unknown until the user verifies and permits the package

Only source builds require the .NET 10 SDK selected by `global.json`.
