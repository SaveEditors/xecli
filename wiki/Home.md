# XeCLI Wiki

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for XBDM, JRPC2, FTP, XEX dumping, memory inspection, and automation. This wiki is the primary reference for the `rgh` CLI, bundled metadata, and release workflows.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

Avatar workflows in the shipped release now support both:

- `rgh avatar choose` for a terminal game/item picker
- `rgh avatar browse` for a Windows picker with search, filters, multi-select, and install

`rgh avatar install` remains the direct path for explicit one-item or full-title installs. `--remote` switches the same workflow to the hosted `Avatar-Item-Collection` repository with local caching.

## Documentation Index

### Getting Started
| Page | Purpose |
| --- | --- |
| [Beginner Guide](Beginner-Guide.md) | Safe first-run workflow: install, discovery, connect, status, and basic operations |
| [Commands Reference](Commands.md) | Full command-by-command reference with examples |
| [CLI Help Output](CLI-Help.md) | Exact built-in `rgh help` output and top-level branch help screens |
| [Hardware and System Controls](Hardware-and-System.md) | Sign-in state, ring-light LEDs, fan commands, and SMC version notes |
| [XNotify](XNotify.md) | Notification usage, icon IDs, and direct integration notes |
| [Homebrew and USB](Homebrew-and-USB.md) | USB/folder staging or direct console installs for Aurora, DashLaunch, XeXMenu, and Freestyle Dash |
| [Avatar Item Collection](Avatar-Item-Collection.md) | Local and hosted avatar corpus naming, browser flows, layout, and install model |
| [Troubleshooting](Troubleshooting.md) | Failure cases, common console/plugin issues, and recovery paths |

### Technical Reference
| Page | Purpose |
| --- | --- |
| [Advanced Guide](Advanced-Guide.md) | Reverse engineering, memory workflows, Ghidra flows, and automation usage |
| [Frameworks and Internals](Frameworks.md) | Command architecture, transport layers, and internal design decisions |
| [Documentation Standards](Standards.md) | Structure, conventions, and maintenance rules for this wiki |
| [Contributing](Contributing.md) | Contribution expectations for code, docs, validation, and release prep |

### Data and Integrations
| Page | Purpose |
| --- | --- |
| [Integrations](Integrations.md) | Reusing XeCLI from scripts and external tools |
| [Title ID Database](Title-ID-Database.md) | Bundled metadata files and how other tools can consume them |
| [FAQ](FAQ.md) | Short answers to common setup and usage questions |

## Recommended Reading Paths

### New user path
1. [Beginner Guide](Beginner-Guide.md)
2. [Commands Reference](Commands.md)
3. [Hardware and System Controls](Hardware-and-System.md)
4. [XNotify](XNotify.md)
5. [CLI Help Output](CLI-Help.md)
6. [Troubleshooting](Troubleshooting.md)
7. [Homebrew and USB](Homebrew-and-USB.md)

### Reverse-engineering path
1. [Advanced Guide](Advanced-Guide.md)
2. [Frameworks and Internals](Frameworks.md)
3. [Commands Reference](Commands.md)
4. [Title ID Database](Title-ID-Database.md)

### Contributor path
1. [Contributing](Contributing.md)
2. [Documentation Standards](Standards.md)
3. [Frameworks and Internals](Frameworks.md)
4. [CLI Help Output](CLI-Help.md)

## Capability Map

### Console operations
- Discovery, target persistence, and quick connection workflows
- Status, title resolution, and profile visibility
- Sign-in state, ring-of-light LED control, manual fan commands, and SMC version probing
- Launch, reboot, and console notification workflows
- Terminal and Windows avatar browsing, remote-hosted downloads, and console-side avatar item installs

### Live inspection and debugging
- Module list, info, dump, load, unload, and pending verification
- Memory dump, hexdump, peek, poke, watch, strings, and search
- Thread list, context, suspend, and resume
- Debug stop/go, breakpoints, databreaks, and event watch

### Storage and content
- XBDM file-system operations
- FTP-based browsing, transfer, and content discovery
- Save extraction and injection
- DashLaunch plugin slot management
- USB/folder staging or direct console installs with `rgh homebrew install aurora|dashlaunch|xexmenu|fsd|all`
- Avatar Item Collection cataloging, remote browsing, search, dry-run planning, progress-driven installs, and install execution

### Analysis and packaging
- Running XEX dump and string extraction
- Ghidra headless analysis and decompile export
- ISO to Games on Demand conversion with watchdog mode
- Bundled Title ID metadata for richer output and external tool reuse
- Avatar item catalog reuse for external launchers, installers, and companion tools

## Scope Boundaries
XeCLI is strong in live-console workflows. It does not currently claim to be:

- a NAND flasher
- a XeBuild replacement
- a glitch-chip programmer
- a full trace debugger

Current out-of-scope areas:

- NAND read/write
- image building or dashboard patching
- glitch timing programming
- trace/step debugging

## Important Paths

### Bundled assets
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`
- `src/Xbox360.Remote.Cli/ghidra_scripts/DecompileAllToC.java`

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
rgh ping
rgh status
rgh title
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
- `ConsoleDependencies/`, `Assets/`, and `ghidra_scripts/` ship in the same release package
- `xbdm.xex`, `XDRPC.xex`, and `JRPC2.xex` are also exposed at the repo root for direct download/reference
- `rgh install` can copy the release to a chosen install folder, register `rgh`, and offer immediate console discovery after setup

Only source builds require a local .NET 10 SDK/runtime.
