# XeCLI

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for live console work with XBDM, JRPC2, FTP, XeLL-backed NAND dumping, XEX tooling, memory inspection, content workflows, reverse-engineering helpers, and release packaging.

The repository and product name are `XeCLI`. The installed terminal command is `rgh`.

[![Support XeCLI on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/xecli)

## What XeCLI Covers

- Live console status, title resolution, module inspection, memory inspection, thread control, screenshots, and debug helpers
- File-system and FTP workflows for browsing, transfer, search, save handling, plugins, and console content staging
- XeLL-backed keyvault export, CPU key capture, startup-log capture, and verified read-only NAND backup
- Local XTAF/FATX disk and image workflows with Windows disk enumeration, header scan, manual offset/length open, metadata backup/restore, safe repair, and raw partition export
- Local content tooling for CON, profile, GPD, and XDBF inspection and mutation
- Reverse-engineering helpers for Ghidra and IDA, plus XEX dump, decompile, and analysis flows
- Homebrew, dashboard, compatibility-pack, and USB staging workflows

## v1.0.9 Highlights

- Fixed false-zero live memory reads so `rgh mem peek`, `rgh mem hexdump`, and small memory dumps agree on the same target data
- Added verified memory writes so `rgh mem poke` and freeze loops fail on rejected or mismatched writes instead of printing false success
- Hardened debugger control so `rgh debug stop`, `rgh debug go`, breakpoints, and data breakpoints only report success when XBDM accepts the command
- Added self-contained portable release assets for both `win-x64` and `win-x86`, plus a universal setup installer

## Quick Start

```powershell
.\XeCLI-v1.0.9-setup.exe
rgh --help
rgh language --set es
rgh nand dump --ip 192.168.1.186 --yes
```

Portable zip users can run `rgh.exe` directly from the extracted `win-x64` or `win-x86` release folder. The installer exists to register `rgh`, add the selected install to PATH, and persist the initial UI language selection on supported Windows installations.

## Documentation

- [GitHub Wiki](https://github.com/SaveEditors/xecli/wiki)
- [Wiki Home](https://github.com/SaveEditors/xecli/wiki/Home)
- [XeCLI-XellFetch](https://github.com/SaveEditors/xecli/wiki/XeCLI-XellFetch)
- [XTAF / FATX Manager](https://github.com/SaveEditors/xecli/wiki/FATX-Manager)
- [Commands Reference](https://github.com/SaveEditors/xecli/wiki/Commands)
- [CLI Help Output](https://github.com/SaveEditors/xecli/wiki/CLI-Help)
- [Beginner Guide](https://github.com/SaveEditors/xecli/wiki/Beginner-Guide)
- [Advanced Guide](https://github.com/SaveEditors/xecli/wiki/Advanced-Guide)
- [Reverse Engineering](https://github.com/SaveEditors/xecli/wiki/Reverse-Engineering)
- [Integrations](https://github.com/SaveEditors/xecli/wiki/Integrations)
- [Troubleshooting](https://github.com/SaveEditors/xecli/wiki/Troubleshooting)
- [All Releases](https://github.com/SaveEditors/xecli/wiki/Releases)
- [GitHub Releases](https://github.com/SaveEditors/xecli/releases)

## Tooling Notes

Ghidra is an external `(Free)` dependency. XeCLI's supported Ghidra XEX import path uses the maintained [SaveEditors/XEXLoaderWV](https://github.com/SaveEditors/XEXLoaderWV) fork.

The IDA workflow is pinned to `IDA Pro 9.1.250226` with `idaxex 0.42b`.

## Why XTAF Matters

XeCLI's `rgh xtaf` surface is not limited to browsing normal Xbox 360 HDD images. The standout workflows are:

- Direct Windows physical-disk inspection alongside normal `.img` and `.bin` inputs
- FATX/XTAF header scan plus manual `--offset` and bounded `--length` opens for unusual or partial images
- Retail-layout FATX formatting for blank or recovered Xbox 360 HDD images
- Low-level metadata backup and restore for disk-prefix and partition-header regions before risky work
- Safe chain-map audit and repair for orphaned allocation issues
- Raw partition dump and tree extraction workflows for recovery or analysis

`rgh fatman` and `rgh fatx` still work as compatibility aliases, but `rgh xtaf` is now the primary disk and image command surface.

## Standalone Payload Repo

The standalone `XeCLI-XellFetch` repo is published at [github.com/SaveEditors/XeCLI-XellFetch](https://github.com/SaveEditors/XeCLI-XellFetch). It packages the custom `xell.bin`, `XellLaunch`, and `QuickBoot` assets for operators who want the XeLL-side payload with or without the full XeCLI desktop workflow. XeCLI integrates with that workflow, but the standalone bundle is released from its own repo.

## Release

- [Latest Release](https://github.com/SaveEditors/xecli/releases/latest)
- [Full Release Archive](https://github.com/SaveEditors/xecli/wiki/Releases)

## Screenshots

![XeCLI help](assets/readme/rgh-help.png)

![XeCLI status](assets/readme/rgh-status.png)
