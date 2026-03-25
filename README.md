# XeCLI

v1.0.6 turns NAND dumping into a PC-side one-command workflow. `rgh nand dump` now drives the managed XeLL launch, helper/linker staging, repeated verification, packaging, and the automatic reboot after a verified success. The release notes are in [wiki/Release-Notes-v1.0.6.md](wiki/Release-Notes-v1.0.6.md).

**Disclaimer:** Auto-reboot is disabled for `--single` or `--no-verify` flags to prevent data loss. Always ensure your PC stays connected during the dump.

## Quick Start

```powershell
dotnet .\rgh.dll nand dump --ip 192.168.1.186 --yes
```

Useful docs:

- [wiki/Home.md](wiki/Home.md)
- [wiki/XeLL-and-NAND-Backups.md](wiki/XeLL-and-NAND-Backups.md)
- [wiki/Commands.md](wiki/Commands.md)

The standalone `Xell-NoN` companion bundle lives in [Xell-NoN](Xell-NoN) and is also published at [github.com/SaveEditors/Xell-NoN](https://github.com/SaveEditors/Xell-NoN). It packages the custom `xell.bin`, `XellLaunch`, and `QuickBoot` assets for operators who want the XeLL-side payload without the full XeCLI desktop workflow.
