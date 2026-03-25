# Xell-NoN

Xell-NoN is a small standalone package for the XeCLI NAND-over-network payload.

It includes:

- the current custom `xell.bin` payload that keeps the on-screen `Waiting for XeCLI connection` prompt
- the `XellLaunch` helper XEX used to chain-load that payload from a running dashboard
- the `QuickBoot` content assets used to build a dashboard shortcut package

XeCLI uses this payload as part of its automated NAND dump flow. If you want the full PC-side workflow, verification loop, packaging, and reboot handling, use the main XeCLI repo instead.

Main XeCLI repo: [https://github.com/SaveEditors/xecli](https://github.com/SaveEditors/xecli)

## Layout

```text
payload/
  xell.bin
launch/
  XellLaunch/
    default.xex
    upstream-readme.txt
  QuickBoot/
    default.xex
    X360.dll
    config-launcher.ini.example
    config-flash.ini.example
tools/
  build-quickboot.ps1
docs/
  XeCLI_CUSTOMIZATION.md
  xell-preview-waiting.svg
SHA256SUMS.txt
```

## Why this payload

This package carries the same custom waiting-screen payload XeCLI v1.0.6 now ships by default.

- `Assets\XellLaunch\default.xex` is the helper XeCLI stages on-console.
- `Assets\QuickBoot\default.xex` and `Assets\QuickBoot\X360.dll` are the content assets XeCLI uses for dashboard shortcut generation.
- `Assets\XellLaunch\xell.bin` and `payload\xell.bin` should match the same live-tested custom build hash.

## Direct helper launch

1. Copy `launch/XellLaunch/default.xex` and `payload/xell.bin` into the same folder on the console.
2. A common layout is `Hdd1:\XellLaunch\default.xex` and `Hdd1:\XellLaunch\xell.bin`.
3. Launch `default.xex` from your dashboard, file manager, or another homebrew loader.
4. The payload should settle on the XeCLI splash and show `Waiting for XeCLI connection`.

The helper looks for `xell.bin` beside itself first. If you move the helper, keep `xell.bin` adjacent.

## HTTP endpoints

Useful endpoints exposed by this payload family:

- `/rawflash` and `/FLASH`: NAND dump stream
- `/XECLI_STATUS` and `/xecli_status`: plain-text status and heartbeat
- `/reboot` and `/REBOOT`: generic reboot
- `/XECLI_DONE`, `/xecli_done`, `/XECLI_REBOOT`, `/xecli_reboot`: completion reboot paths used by XeCLI

If the console gets a DHCP lease, browse to `http://<console-ip>/` and use the endpoints above directly.

## Optional QuickBoot shortcut

The `launch/QuickBoot` folder is included so you can build a dashboard content package without XeCLI.

Typical flow:

1. Put the helper where you want it on the console, for example `Hdd1:\XellLaunch\default.xex`.
2. Pick the matching config example:
   `launch/QuickBoot/config-launcher.ini.example` for a helper on HDD/USB
   `launch/QuickBoot/config-flash.ini.example` for a flash-resident `\lhelper.xex`
3. Build the package:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-quickboot.ps1 `
  -ConfigFile .\launch\QuickBoot\config-launcher.ini.example `
  -OutputFile .\XeLLLaunch.live
```

4. Upload the resulting package to:

```text
Hdd1:\Content\0000000000000000\C0DE9999\00007000\XeLLLaunch
```

The script uses the same content metadata XeCLI uses for its QuickBoot shortcut staging.

## Notes

- This package does not rebuild XeLL from source. It packages the already-built binaries present in this workspace.
- `docs/XeCLI_CUSTOMIZATION.md` documents the intended UI/behavior of the custom payload.
- `docs/xell-preview-waiting.svg` is a static preview of the waiting screen styling.
