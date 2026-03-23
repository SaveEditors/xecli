# Latest Features

This page highlights the current XeCLI workflows that are most worth surfacing in the README, the wiki sidebar, and the docs landing page.

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

## Claude, Codex, and Terminal Agents

XeCLI works well from Claude, Codex, and other terminal agents because the command surface is explicit and many commands expose `--json`. That makes it a good fit for repeatable console automation, file collection, screenshots, and reverse-engineering pipelines.

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

Ghidra is external and `(Free)`. IDA Pro `9.1.250226` is external and required for the IDA debugger/decompiler path. After you configure the tool path, XeCLI can install the supported XEX loader helpers with `rgh ghidra install-loader` and `rgh ida install-loader`.

Read [Advanced-Guide.md](Advanced-Guide.md) for the broader workflow guidance.
