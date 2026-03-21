# Homebrew and USB

This page covers the USB-side package workflow in XeCLI.

Use it when you want to:

- stage Aurora, DashLaunch, XeXMenu, or Freestyle Dash onto a USB drive
- prepare a clean USB layout without manually hunting archives
- reuse the bundled `xbdm.xex`, `JRPC2.xex`, and `XDRPC.xex` files in the same package

## Package Mode

XeCLI now exposes package staging through the `homebrew` command group:

```powershell
rgh homebrew list
rgh homebrew install aurora --usb E:
rgh homebrew install dashlaunch --usb E:
rgh homebrew install xexmenu --usb E:
rgh homebrew install fsd --usb E:
rgh homebrew install all --usb E: --auto-confirm
```

If you are still in the extracted release folder and `rgh` is not registered yet, use:

```powershell
.\rgh.exe homebrew install aurora --usb E:
```

Supported package IDs:

- `aurora`
- `dashlaunch`
- `xexmenu`
- `fsd`
- `all`

## USB Target Selection

`--usb` accepts:

- a drive letter like `E:`
- a drive root like `E:\`
- a removable-drive selection number from the prompt
- a normal folder path for staging or testing

Examples:

```powershell
rgh homebrew install aurora --usb E:
rgh homebrew install all --usb F: --auto-confirm
rgh homebrew install dashlaunch --usb A:\Builds\UsbStage
```

If you omit `--usb`, XeCLI looks for removable drives and prompts for one. If no removable drives are visible, it stops and asks you to provide `--usb <drive-or-path>`.

## Download and Cache Behavior

XeCLI downloads package archives into the local cache:

- `%LOCALAPPDATA%\XeCLI\cache\packages\archives`

It then extracts them into a staging area on the target drive by default, copies the payload into the selected USB or folder target, and reuses cached archives on later installs.

Useful flags:

```powershell
rgh homebrew install aurora --usb E: --force-download
rgh homebrew install all --usb E: --cache D:\XeCLI-Cache --auto-confirm
```

- `--force-download` ignores the cached archive and fetches it again
- `--cache <DIR>` moves both the archive cache and staging root to a different location
- `--auto-confirm` skips the confirmation step before package install

## What XeCLI Places on the Target

For each selected package, XeCLI creates a clean package folder on the target:

- `Aurora\`
- `DashLaunch\`
- `XeXMenu\`
- `FreestyleDash\`

It also copies the bundled console-side plugin files into:

- `Plugins\`

When Aurora is part of the install, XeCLI also writes:

- `launch.ini`

The generated `launch.ini` sets Aurora as the default path and points the plugin slots at the bundled `Plugins\` directory.

## Example Output

```text
rgh homebrew install all --usb E: --auto-confirm
Extracting Aurora 0.7b.2...
Extracting DashLaunch 3.21...
Download XeXMenu 1.2 downloading: 55%
Extracting XeXMenu 1.2...
Extracting Freestyle Dash 3...
SUCCESS Homebrew install complete
4 package(s) 326 MB -> E:\
```

## Package Sources

The current build uses these public sources:

- Aurora 0.7b.2
  - `http://phoenix.xboxunity.net/downloads/Aurora%200.7b.2%20-%20Release%20Package.rar`
  - mirror: `https://consolemods.org/wiki/images/d/dd/Aurora_0.7b.2_-_Release_Package.rar`
- DashLaunch 3.21
  - `https://consolemods.org/wiki/File:DashLaunch_v3.21.7z`
- XeXMenu 1.2
  - `https://consolemods.org/wiki/images/5/5c/XeXmenu_1.2.7z`
- Freestyle Dash 3
  - `https://consolemods.org/wiki/images/a/a0/Fsd3.zip`
  - fallback: `https://consolemods.org/wiki/images/7/76/TeamFSD.Freestyle3.0.775.7z`

XeCLI resolves MediaWiki file pages to the raw archive automatically when a page URL is provided instead of a direct file URL.

## Notes

- `rgh install` is the XeCLI installer
- `rgh homebrew install ...` is the package staging path
- package-mode is local USB or folder staging only
- package installs do not push these dashboards onto the console HDD yet
