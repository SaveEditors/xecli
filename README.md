# XeCLI

XeCLI is a terminal-first Xbox 360 RGH/JTAG toolkit for live console work with XBDM, JRPC2, FTP, XeLL-backed NAND dumping, XEX tooling, memory inspection, and automation.

v1.0.6 adds the automated PC-side NAND workflow. `rgh nand dump` now handles managed XeLL launch, helper/linker staging, verification, packaging, and verified-success reboot control from one command.

**Disclaimer:** Auto-reboot is disabled for `--single` or `--no-verify` to prevent data loss. Keep the PC connected until XeCLI confirms the dump is verified.

## Quick Start

```powershell
dotnet .\rgh.dll nand dump --ip 192.168.1.186 --yes
```

## Documentation

- [Docs landing page](https://saveeditors.github.io/xecli/)
- [Wiki home](https://saveeditors.github.io/xecli/wiki/Home.html)
- [XeLL and NAND Backups](https://saveeditors.github.io/xecli/wiki/XeLL-and-NAND-Backups.html)
- [Commands Reference](https://saveeditors.github.io/xecli/wiki/Commands.html)
- [Release Notes v1.0.6](https://saveeditors.github.io/xecli/wiki/Release-Notes-v1.0.6.html)
- [All Releases](https://saveeditors.github.io/xecli/wiki/Releases.html)
- [GitHub Releases](https://github.com/SaveEditors/xecli/releases)

## Companion Package

The standalone `Xell-NoN` companion bundle lives in [Xell-NoN](Xell-NoN) and is also published at [github.com/SaveEditors/Xell-NoN](https://github.com/SaveEditors/Xell-NoN). It packages the custom `xell.bin`, `XellLaunch`, and `QuickBoot` assets for operators who want the XeLL-side payload without the full XeCLI desktop workflow.
