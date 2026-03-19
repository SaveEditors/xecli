# XeCLI Wiki

This wiki is the operator and developer reference for XeCLI. It is organized so a new user can get productive quickly without hiding the deeper features that make the project useful for live RGH/JTAG work.

The repository and product name are `XeCLI`. The terminal command is `rgh`.

## Read This First
If you are new to the tool:

1. [Beginner Guide](Beginner-Guide.md)
2. [Commands Reference](Commands.md)
3. [CLI Help Output](CLI-Help.md)
4. [Troubleshooting](Troubleshooting.md)

If you are using XeCLI for reverse engineering or tool development:

1. [Advanced Guide](Advanced-Guide.md)
2. [Frameworks and Internals](Frameworks.md)
3. [Integrations](Integrations.md)
4. [Title ID Database](Title-ID-Database.md)

## What XeCLI Covers Well
Daily console operations:

- Discovery and target selection
- Fast status and health checks
- Title lookup and active-title resolution
- Launch, reboot, and notification workflows

Live debugging and inspection:

- Module list, info, dump, load, unload, and post-reboot verification
- Memory dump, hexdump, peek, poke, watch, string extraction, and pattern search
- Thread enumeration, register context, suspend, and resume
- Breakpoints, data breakpoints, and live debug-event watch

File and content workflows:

- XBDM file-system commands
- FTP-backed browsing, transfer, and search
- Save extraction and injection
- Installed-content inventory
- DashLaunch plugin slot management

Analysis workflows:

- XEX dump and string extraction
- Ghidra headless analysis and decompile export
- Decompile verification
- Bundled Title ID data for metadata enrichment

Packaging workflows:

- ISO to Games on Demand conversion
- Folder watchdog for unattended conversions

## What XeCLI Does Not Try to Pretend It Covers
XeCLI is strong in live-console workflows. It is not currently a NAND flasher, XeBuild replacement, or glitch-chip programming suite.

Not covered in the current release:

- NAND read/write
- Image building or dashboard patching
- Glitch timing programming
- Full stepping and trace-based debugging

## Release-Ready Expectations
The project is intended to be publishable as a clean GitHub repository:

- Source under `src/`
- Docs under `README.md` and `wiki/`
- Bundled assets stored in the repo
- No runtime dumps, screenshots, or machine-specific paths committed

The release archive should contain the CLI executable plus its bundled assets, not a source-tree publish dump.

## Important Paths
Bundled metadata and scripts:

- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`
- `src/Xbox360.Remote.Cli/ghidra_scripts/DecompileAllToC.java`

Runtime config:

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

For the exact built-in help screens users will see in terminal, including the current `rgh help` output and every top-level help page, use [CLI Help Output](CLI-Help.md).

## Related Pages
- [Beginner Guide](Beginner-Guide.md)
- [Commands Reference](Commands.md)
- [CLI Help Output](CLI-Help.md)
- [Advanced Guide](Advanced-Guide.md)
- [Frameworks and Internals](Frameworks.md)
- [Title ID Database](Title-ID-Database.md)
- [Integrations](Integrations.md)
- [Troubleshooting](Troubleshooting.md)
- [FAQ](FAQ.md)
