# Homebrew and USB

This page covers the `rgh homebrew install` workflow in XeCLI.

For the separate Original Xbox compatibility workflow that targets `HddX:\Compatibility`, use [Original Xbox Compatibility](Original-Xbox-Compatibility.md).

Use it when you want to:

- stage Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, or XellLaunch onto a USB drive or folder
- install those packages directly onto `Hdd1`, `Usb0`, `Usb1`, or `Usb2` on the console
- keep Original Xbox compatibility separate from normal dashboard/homebrew staging so `HddX:\Compatibility` installs are handled through `rgh ogxbox install ...`
- prepare a clean homebrew layout without manually hunting archives
- reuse the bundled `xbdm.xex`, `JRPC2.xex`, and `XDRPC.xex` files in the same package

## Package Mode

XeCLI now exposes package staging through the `homebrew` command group:

```powershell
rgh homebrew list
rgh homebrew install aurora --usb E:
rgh homebrew install aurora --device Hdd1 --ini-mode merge
rgh homebrew install all --device Hdd1 --ini-mode generated --auto-confirm
rgh homebrew install dashlaunch --usb E:
rgh homebrew install xexmenu --usb E:
rgh homebrew install fsd --usb E:
rgh homebrew install xm360 --usb E:
rgh homebrew install timefixer --usb E:
rgh homebrew install simple360 --usb E:
rgh homebrew install xelllaunch --usb E:
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
- `xm360`
- `timefixer`
- `simple360`
- `xelllaunch`
- `all`

## Target Selection

### USB or folder staging
Use `--usb` when you want a host-side staging target.

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
rgh homebrew install xm360 --usb E:
rgh homebrew install timefixer --usb E:
rgh homebrew install simple360 --usb E:
rgh homebrew install xelllaunch --usb E:
```

### Direct console install
If you omit `--usb`, XeCLI switches into direct console install mode.

It connects to the console over FTP, probes only these supported install roots:

- `Hdd1`
- `Usb0`
- `Usb1`
- `Usb2`

It does not offer `System`, `SysExt`, `HddX`, or other system roots in this workflow.

If more than one supported install root is present, XeCLI asks which one to use. You can also pass it explicitly:

```powershell
rgh homebrew install aurora --device Hdd1
rgh homebrew install all --device Usb0 --auto-confirm
```

Use `--device` only with direct console install. Do not combine it with `--usb`.

## Download and Cache Behavior

XeCLI downloads package archives into the local cache:

- `%LOCALAPPDATA%\XeCLI\cache\packages\archives`

It then extracts them into a staging area, copies the payload into the selected USB/folder target or onto the console, and reuses cached archives on later installs.

Useful flags:

```powershell
rgh homebrew install aurora --usb E: --force-download
rgh homebrew install all --usb E: --cache D:\XeCLI-Cache --auto-confirm
```

- `--force-download` ignores the cached archive and fetches it again
- `--cache <DIR>` moves both the archive cache and staging root to a different location
- `--auto-confirm` skips the confirmation step before package install

## Confirmation Behavior

XeCLI asks before staging or installing a package unless `--auto-confirm` is supplied or the command is running in a non-interactive context.

## What XeCLI Places on the Target

For each selected package, XeCLI creates a clean package folder on the target:

- `Aurora\`
- `DashLaunch\`
- `XeXMenu\`
- `FreestyleDash\`

It also copies the bundled console-side plugin files into:

- `Plugins\`

When you choose `generated` launch.ini mode, XeCLI also writes:

- `launch.ini`

The generated `launch.ini` sets Aurora as the default path and points the plugin slots at the bundled `Plugins\` directory.

Direct console install uses these console paths:

- `/<Device>/Aurora`
- `/<Device>/DashLaunch`
- `/<Device>/XeXMenu`
- `/<Device>/FreestyleDash`
- `/<Device>/Plugins`

Default `launch.ini` path for console installs:

- `/Hdd1/launch.ini`

Override it with:

```powershell
rgh homebrew install dashlaunch --device Hdd1 --ini /Hdd1/launch.dev.ini --ini-mode merge
```

## launch.ini Modes

Direct console install supports three launch.ini modes:

- `generated`
  - write a fresh XeCLI `launch.ini`
  - useful when you want Aurora + the bundled plugins as a clean baseline
- `merge`
  - keep the existing file
  - add or update plugin entries for the bundled `xbdm.xex`, `JRPC2.xex`, and `XDRPC.xex`
  - preserve the current `launch.ini` content before writing the merged plugin entries when the target path supports a backup copy
- `skip`
  - install only the homebrew files and bundled plugins
  - leave `launch.ini` untouched

## Example Output

```text
rgh homebrew install all --usb E: --auto-confirm
Extracting Aurora 0.7b.2...
Extracting DashLaunch 3.21...
Download XeXMenu 1.2 downloading: 55%
Extracting XeXMenu 1.2...
Extracting Freestyle Dash 3...
Extracting XM360...
Extracting TimeFixer...
Extracting Simple 360 NAND Flasher...
Extracting XellLaunch...
SUCCESS Homebrew install complete
4 package(s) 326 MB -> E:\
```

That example is abbreviated. The current package catalog also includes XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch.

```text
rgh homebrew install dashlaunch --device Hdd1 --ini-mode merge --auto-confirm
Extracting DashLaunch 3.21...
SUCCESS Console homebrew install complete
1 package(s) 2.52 MB -> Hdd1 on 192.168.1.186

Package            Console Path       Files   Size
DashLaunch 3.21    /Hdd1/DashLaunch   8       2.52 MB

Bundled plugins copied to /Hdd1/Plugins
Updated existing launch.ini plugin entries /Hdd1/launch.ini
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
- XM360
  - `https://consolemods.org/wiki/images/5/5f/XM360.7z`
  - `https://consolemods.org/wiki/File:XM360.7z`
- TimeFixer
  - `https://github.com/DerfJagged/TimeFixer/releases/download/v1/TimeFixer_by_Derf.zip`
- Simple 360 NAND Flasher
  - `https://consolemods.org/wiki/images/f/ff/Simple_360_NAND_Flasher.7z`
  - `https://consolemods.org/wiki/File:Simple_360_NAND_Flasher.7z`
- XellLaunch
  - `https://consolemods.org/wiki/images/4/41/XellLaunch.7z`
  - `https://consolemods.org/wiki/File:XellLaunch.7z`

XeCLI resolves MediaWiki file pages to the raw archive automatically when a page URL is provided instead of a direct file URL.

## Notes

- `rgh install` is the XeCLI installer
- `rgh homebrew install ...` is the package workflow for both local staging and direct console install
- `rgh ogxbox install ...` is the Original Xbox compatibility workflow for XeFu packs and the optional HDD Compatibility Partition Fixer
- use `--usb` for local staging
- omit `--usb` for direct console install
- direct console install only targets `Hdd1`, `Usb0`, `Usb1`, or `Usb2`
- installs prompt for confirmation unless `--auto-confirm` is supplied or the command is running non-interactively
