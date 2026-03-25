[![Support XeCLI on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/saveeditors)

# XeCLI-XellFetch

XeCLI-XellFetch is the standalone companion package for XeCLI's custom XeLL payload and launcher bundle.

It is called `Fetch` because it fetches NAND and keyvault data over the network.

It includes:

- the current custom `xell.bin` payload that keeps the on-screen `Waiting for XeCLI connection` prompt
- the `XellLaunch` helper XEX used to chain-load that payload from a running dashboard
- the `QuickBoot` content assets used to build a dashboard shortcut package

XeCLI uses this payload as part of its automated XeLL workflows, including verified NAND dumping and verified keyvault export. You can also use this package on its own without XeCLI by launching the helper directly and pulling the exposed HTTP endpoints from your own tooling.

Main XeCLI repo: [https://github.com/SaveEditors/xecli](https://github.com/SaveEditors/xecli)
Standalone companion repo: [https://github.com/SaveEditors/XeCLI-XellFetch](https://github.com/SaveEditors/XeCLI-XellFetch)

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
  XellFetch_CUSTOMIZATION.md
  xell-preview-waiting.svg
SHA256SUMS.txt
```

## Why this payload

This package carries the same custom waiting-screen payload XeCLI ships for its managed XeLL workflows.

- It can be launched and queried without XeCLI.
- It keeps the job-aware waiting, transfer, verification, success, and failure states used by the main workflow.
- It ships the same helper and QuickBoot assets XeCLI uses for dashboard-side launch.

- `launch\XellLaunch\default.xex` is the helper used to stage the payload from a running dashboard.
- `launch\QuickBoot\default.xex` and `launch\QuickBoot\X360.dll` are the content assets used for dashboard shortcut generation.
- `payload\xell.bin` should match the same live-tested custom build hash XeCLI ships in `Assets\XellLaunch\xell.bin`.

## Direct helper launch

1. Copy `launch/XellLaunch/default.xex` and `payload/xell.bin` into the same folder on the console.
2. A common layout is `Hdd1:\XellLaunch\default.xex` and `Hdd1:\XellLaunch\xell.bin`.
3. Launch `default.xex` from your dashboard, file manager, or another homebrew loader.
4. The payload should settle on the XeCLI splash and show `Waiting for XeCLI connection`.

The helper looks for `xell.bin` beside itself first. If you move the helper, keep `xell.bin` adjacent.

## HTTP endpoints

Useful endpoints exposed by this payload family:

- `/rawflash` and `/FLASH`: NAND dump stream
- `/KV`, `/KVRAW`, and `/KVRAW2`: keyvault export stream
- `/XECLI_STATUS` and `/xecli_status`: plain-text status and heartbeat
- `/XECLI_SYNC` and `/xecli_sync`: XeCLI job/status synchronization
- `/reboot` and `/REBOOT`: generic reboot
- `/XECLI_DONE`, `/xecli_done`, `/XECLI_REBOOT`, `/xecli_reboot`: completion reboot paths used by XeCLI

If the console gets a DHCP lease, browse to `http://<console-ip>/` and use the endpoints above directly from a browser, `curl`, PowerShell, or your own host tool.

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
- `docs/XellFetch_CUSTOMIZATION.md` documents the intended UI/behavior of the custom payload.
- `docs/xell-preview-waiting.svg` is a static preview of the waiting screen styling.
