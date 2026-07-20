[![Support XeCLI on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/xecli)

# XeCLI-XellFetch

XeCLI-XellFetch is the companion payload bundle used by XeCLI's integrated XeLL, NAND, and keyvault workflows.

It includes:

- the custom `xell.bin` payload that shows `Waiting for XeCLI connection`
- the `XellLaunch` helper XEX used to chain-load the payload from a running dashboard
- the `QuickBoot` content assets used to build a dashboard shortcut package

The name reflects its role in the XeLL workflow: it provides the payload and helper assets that XeCLI uses to fetch NAND and keyvault data over the network.

Main XeCLI repo: [https://github.com/SaveEditors/xecli](https://github.com/SaveEditors/xecli)
Standalone repo: [https://github.com/SaveEditors/XeCLI-XellFetch](https://github.com/SaveEditors/XeCLI-XellFetch)

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
    config-launcher.ini.example
    config-flash.ini.example
tools/
  build-quickboot.ps1
source/
  README.md
  BUILDING.md
  COMPONENT-NOTICES.md
  LICENSE.GPL-2.0.txt
  NEWLIB-NOTICES.txt
  SOURCE-MANIFEST.json
  build.sh
  build-metadata/
  dependencies/
  xell/
SHA256SUMS.txt
```

The main XeCLI repository carries the GPLv3 X360 package builder and its complete corresponding source under `third_party/X360`.

## Direct Helper Launch

1. Copy `launch/XellLaunch/default.xex` and `payload/xell.bin` into the same folder on the console.
2. A common layout is `Hdd1:\XellLaunch\default.xex` and `Hdd1:\XellLaunch\xell.bin`.
3. Launch `default.xex` from your dashboard, file manager, or another homebrew loader.
4. The payload displays `Waiting for XeCLI connection` when it is loaded.

The helper looks for `xell.bin` beside itself first. If you move the helper, keep `xell.bin` adjacent.

## HTTP Endpoints

> [!WARNING]
> XeLL serves these endpoints over unencrypted HTTP without authentication. Use them only on a trusted private LAN, never expose or port-forward the console to the internet, and disconnect untrusted devices while exporting data. NAND images, CPU keys, fuse data, and keyvault files are sensitive console secrets; store them securely and remove unnecessary copies.

Useful endpoints exposed by this payload family:

- `/rawflash` and `/FLASH`: NAND dump stream
- `/KV`, `/KVRAW`, and `/KVRAW2`: keyvault export stream
- `/XECLI_STATUS` and `/xecli_status`: plain-text status and heartbeat
- `/XECLI_SYNC` and `/xecli_sync`: XeCLI job and status synchronization
- `/reboot` and `/REBOOT`: generic reboot
- `/XECLI_DONE`, `/xecli_done`, `/XECLI_REBOOT`, `/xecli_reboot`: completion reboot paths used by XeCLI

If the console gets a DHCP lease, browse to `http://<console-ip>/` and use the endpoints above from a trusted computer on the same private network. Reboot endpoints act immediately, so do not invoke them while an export is in progress.

## Optional QuickBoot Shortcut

The `launch/QuickBoot` folder is included so you can build a dashboard content package without XeCLI.

Typical flow:

1. Put the helper where you want it on the console, for example `Hdd1:\XellLaunch\default.xex`.
2. Pick the matching config example:
   `launch/QuickBoot/config-launcher.ini.example` for a helper on HDD or USB
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

## Notes

- The normal helper workflow packages the built payload and launcher assets; it does not rebuild XeLL.
- `source/` contains the complete XeLL source snapshot, XeCLI modifications, exact LibXenon and fat-xenon revisions, linked Newlib source, build metadata, GPLv2 terms, and an exact file manifest.
- A new build can vary with its host toolchain and archive implementation. The shipped payload is checksum-locked by `SHA256SUMS.txt`, and the source package states its reproducibility scope without claiming an arbitrary host will produce byte-identical output.
- QuickBoot package creation uses the source-built X360 library version `1.0.0.42`, SHA-256 `F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9`. It is a host-side package builder and is never installed on the console.
- `tools/build-quickboot.ps1` resolves the library from the main repository by default. A standalone checkout can pass `-X360DllPath` pointing to the verified `X360.dll` built from `third_party/X360`.
- Complete X360 source, GPLv3 terms, upstream provenance, XeCLI modifications, deterministic build instructions, and the exact source manifest are kept together under `third_party/X360`.
- Full authorization, source/binary status, and component responsibility are recorded in the repository-level `THIRD-PARTY-NOTICES.md`.
