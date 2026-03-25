# XeCLI

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for live console work with XBDM, JRPC2, FTP, XeLL-backed NAND dumping, XEX tooling, memory inspection, content workflows, reverse-engineering helpers, and release packaging.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

[![Support XeCLI on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/saveeditors)

## What XeCLI Covers

- Live console status, title resolution, module inspection, memory inspection, thread control, screenshots, and debug helpers
- File-system and FTP workflows for browsing, transfer, search, save handling, plugins, and console content staging
- XeLL-backed keyvault export, CPU key capture, startup-log capture, and verified read-only NAND backup
- Local content tooling for CON, profile, GPD, and XDBF inspection and mutation
- Reverse-engineering helpers for Ghidra and IDA, plus XEX dump, decompile, and analysis flows
- Homebrew, dashboard, compatibility-pack, and USB staging workflows

## v1.0.6 Highlights

- Added the automated PC-side NAND workflow with `rgh nand dump`
- Added managed staging for the helper/linker assets used by the XeLL dump flow
- Added the `Xell-NoN` companion package for the standalone payload/bootstrap path
- Added verified-success reboot gating so the console only leaves XeLL after the PC confirms the dump is valid
- Added same-session verification fallback for consoles that ignore XeLL reboot during the second pass

**Safety note:** Auto-reboot is disabled for `--single` and `--no-verify` so the console does not leave XeLL before the operator sees that verification was skipped.

## Quick Start

```powershell
dotnet .\rgh.dll --help
dotnet .\rgh.dll nand dump --ip 192.168.1.186 --yes
```

## Documentation

- [GitHub Wiki](https://github.com/SaveEditors/xecli/wiki)
- [Docs landing page](https://saveeditors.github.io/xecli/)
- [Wiki home](https://saveeditors.github.io/xecli/wiki/Home.html)
- [Latest Features](https://saveeditors.github.io/xecli/wiki/Latest-Features.html)
- [XeLL and NAND Backups](https://saveeditors.github.io/xecli/wiki/XeLL-and-NAND-Backups.html)
- [Commands Reference](https://saveeditors.github.io/xecli/wiki/Commands.html)
- [CLI Help Output](https://saveeditors.github.io/xecli/wiki/CLI-Help.html)
- [Beginner Guide](https://saveeditors.github.io/xecli/wiki/Beginner-Guide.html)
- [Advanced Guide](https://saveeditors.github.io/xecli/wiki/Advanced-Guide.html)
- [Reverse Engineering](https://saveeditors.github.io/xecli/wiki/Reverse-Engineering.html)
- [Integrations](https://saveeditors.github.io/xecli/wiki/Integrations.html)
- [Troubleshooting](https://saveeditors.github.io/xecli/wiki/Troubleshooting.html)
- [Release Notes v1.0.6](https://saveeditors.github.io/xecli/wiki/Release-Notes-v1.0.6.html)
- [All Releases](https://saveeditors.github.io/xecli/wiki/Releases.html)
- [GitHub Releases](https://github.com/SaveEditors/xecli/releases)

## Tooling Notes

Ghidra is an external `(Free)` dependency. XeCLI's supported Ghidra XEX import path uses the maintained [SaveEditors/XEXLoaderWV](https://github.com/SaveEditors/XEXLoaderWV) fork.

The IDA workflow is pinned to `IDA Pro 9.1.250226` with `idaxex 0.42b`.

## Companion Package

The standalone `Xell-NoN` companion bundle lives in [Xell-NoN](Xell-NoN) and is also published at [github.com/SaveEditors/Xell-NoN](https://github.com/SaveEditors/Xell-NoN). It packages the custom `xell.bin`, `XellLaunch`, and `QuickBoot` assets for operators who want the XeLL-side payload without the full XeCLI desktop workflow.

## Release

- [Latest Release](https://github.com/SaveEditors/xecli/releases/latest)
- [Full Release Archive](https://saveeditors.github.io/xecli/wiki/Releases.html)

## Screenshots

![XeCLI help](assets/readme/rgh-help.png)

![XeCLI status](assets/readme/rgh-status.png)
