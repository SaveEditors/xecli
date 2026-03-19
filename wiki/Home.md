# XeCLI Wiki

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for XBDM, JRPC2, FTP, XEX dumping, memory inspection, and automation. This wiki is the primary reference for the `rgh` CLI, bundled metadata, and release workflows.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

## Documentation Index

### Getting Started
| Page | Purpose |
| --- | --- |
| [Beginner Guide](Beginner-Guide.md) | Safe first-run workflow: discovery, connect, status, and basic operations |
| [Commands Reference](Commands.md) | Full command-by-command reference with examples |
| [CLI Help Output](CLI-Help.md) | Exact built-in `rgh help` output and top-level branch help screens |
| [XNotify](XNotify.md) | Notification usage, icon IDs, and direct integration notes |
| [Troubleshooting](Troubleshooting.md) | Failure cases, common console/plugin issues, and recovery paths |

### Developer Reference
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
| [FAQ](FAQ.md) | Short answers to repeated operator and developer questions |

## Recommended Reading Paths

### New operator path
1. [Beginner Guide](Beginner-Guide.md)
2. [Commands Reference](Commands.md)
3. [XNotify](XNotify.md)
4. [CLI Help Output](CLI-Help.md)
5. [Troubleshooting](Troubleshooting.md)

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
- Launch, reboot, and console notification workflows

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

### Analysis and packaging
- Running XEX dump and string extraction
- Ghidra headless analysis and decompile export
- ISO to Games on Demand conversion with watchdog mode
- Bundled Title ID metadata for richer output and external tool reuse

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
rgh start
rgh ping
rgh status
rgh title
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

Only source builds require a local .NET 10 SDK/runtime.
