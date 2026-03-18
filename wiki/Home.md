# XeCLI Wiki

This wiki is the full operator and developer reference for XeCLI. It is written for three audiences:
- Console operators who want fast, reliable workflows for live RGH/JTAG work
- Reverse engineers who want to dump, inspect, and decompile XEXs and modules
- Tool developers who want to reuse XeCLI data, outputs, and bundled assets in their own projects

## Start Here
- [Beginner Guide](Beginner-Guide.md)
- [Commands Reference](Commands.md)
- [Advanced Guide](Advanced-Guide.md)

## Deep Reference
- [Frameworks and Internals](Frameworks.md)
- [Title ID Database](Title-ID-Database.md)
- [Integrations](Integrations.md)
- [Troubleshooting](Troubleshooting.md)
- [FAQ](FAQ.md)

## What XeCLI Is
XeCLI is a terminal-first Xbox 360 RGH/JTAG toolchain built around live console access. It focuses on:
- Discovery and connection management
- XBDM status, file system, module, memory, thread, and debug operations
- JRPC2-backed RPC, notifications, title metadata, and sensor reads
- FTP-backed file workflows and XEX pulling
- Ghidra headless analysis and decompile export
- ISO to Games on Demand conversion
- A bundled Title ID database that ships with the repo and publish output

## What XeCLI Is Good At
- Daily console operations without opening GUI tools
- Repeatable scripts using `--json`
- Pulling XEXs and modules for static analysis
- Reading and modifying live memory
- Quick operational visibility into console state
- Reusing XeCLI's bundled game metadata in other tools

## What XeCLI Does Not Currently Cover
- NAND flashing
- Dashboard patching
- Glitch chip programming or timing profiles
- Full stepping or trace-based debugger workflows
- Symbol-aware decompilation

## Bundled Assets
XeCLI ships with local data files and helper assets in the repo. The important point is that these files are included with the project and publish output.

Title ID files:
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

At runtime, published builds read:
- `Assets/xbox360_gamelist.csv`
- `Assets/xbox360_titleids.txt`

These are local files. XeCLI does not fetch them from the internet at runtime.

## Configuration
- Main config: `%APPDATA%\XeCLI\config.json`
- Cache: `%LOCALAPPDATA%\XeCLI\cache`
- Local Title ID extension file: `%APPDATA%\XeCLI\titleids.local.csv`

## Recommended Reading Order
If you are new:
1. [Beginner Guide](Beginner-Guide.md)
2. [Commands Reference](Commands.md)
3. [Troubleshooting](Troubleshooting.md)

If you are reverse engineering:
1. [Advanced Guide](Advanced-Guide.md)
2. [Frameworks and Internals](Frameworks.md)
3. [Title ID Database](Title-ID-Database.md)

If you are integrating XeCLI into another tool:
1. [Integrations](Integrations.md)
2. [Title ID Database](Title-ID-Database.md)
3. [Commands Reference](Commands.md)


