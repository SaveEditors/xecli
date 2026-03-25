# Original Xbox Compatibility

This page documents the `rgh ogxbox` workflow for installing original Xbox backwards-compatibility files onto an Xbox 360 hard drive or staging them to USB/a folder first.

Use it when you want to:

- prepare one of the three public XeFu compatibility sets
- install the compatibility files directly to `HddX:\Compatibility`
- stage the files to a USB drive or folder before copying them manually
- optionally stage the HDD Compatibility Partition Fixer when `HddX` does not exist yet

## Command Surface

```powershell
rgh ogxbox list
rgh ogxbox install hacked --usb E:
rgh ogxbox install hud --include-fixer --usb E:
rgh ogxbox install retail
```

If `rgh` is not registered yet and you are still in the extracted release folder:

```powershell
.\rgh.exe ogxbox install hacked --usb E:
```

## XeFu Set Choices

### `hacked`
- best default choice for a modded console
- removes the stock whitelist and restriction checks
- includes the standard emulator files plus extra compatibility-oriented files
- requires an exploited console

Direct link:
- [Hacked XeFu Pack](https://consolemods.org/wiki/images/9/9d/Hacked_Xefu_Pack.zip)

### `hud`
- same hacked compatibility pack, but keeps the full Xbox 360 guide available while you are inside original Xbox titles
- useful if you want guide access or game chat while playing
- uses more resources, so some games may run worse or behave less reliably than the standard hacked pack
- requires an exploited console

Direct link:
- [Hacked XeFu Pack with HUD](https://consolemods.org/wiki/images/c/c4/Hacked_Xefu_Pack_with_HUD.zip)

### `retail`
- closest to stock Microsoft behavior
- keeps the official compatibility restrictions and whitelist behavior
- useful if you want the original retail emulator files without the hacked compatibility changes

Direct link:
- [Unmodified Retail XeFu Pack](https://consolemods.org/wiki/images/2/28/Unmodified_Retail_Xefu_Pack.zip)

## HDD Compatibility Partition Fixer

Some third-party or manually prepared hard drives do not have the `HddX` partition required for original Xbox compatibility files.

XeCLI can also stage or install the public partition fixer:

- [HDD Compatibility Partition Fixer](https://consolemods.org/wiki/images/b/b2/Hdd_compat_partition_fixer_v1.zip)

Use `--include-fixer` when:

- `HddX` is missing
- you are preparing a clean USB/folder package for someone else
- you want the fixer staged beside the chosen XeFu set for convenience

The fixer still needs to be run on the console. XeCLI does not create `HddX` remotely by itself.

## Local Staging

Use `--usb` when you want a host-side staging target instead of an immediate console install.

Accepted targets:

- removable USB drive letter like `E:`
- removable-drive selection number from the prompt
- normal folder path

Examples:

```powershell
rgh ogxbox install hacked --usb E:
rgh ogxbox install hud --include-fixer --usb F:
rgh ogxbox install retail --usb A:\Builds\OGXbox
```

XeCLI stages:

- `Compatibility\`
- `HddCompatibilityPartitionFixer\` when `--include-fixer` is used
- `XeCLI-OriginalXbox-Compatibility.txt` with the next-step instructions

## Direct Console Install

If you omit `--usb`, XeCLI installs straight to the console over FTP.

Direct console behavior:

- XeCLI checks for `/HddX`
- if `HddX` exists, XeCLI uploads the selected XeFu set to `/HddX/Compatibility`
- if `HddX` is missing and `--include-fixer` is not used, XeCLI stops and tells you to rerun with `--include-fixer`
- if `HddX` is missing and `--include-fixer` is used, XeCLI stages the fixer to `Hdd1` when available, otherwise to `Usb0`, `Usb1`, or `Usb2`

Example:

```powershell
rgh ogxbox install hacked
rgh ogxbox install hacked --include-fixer
```

## Example Output

### Local staging

```text
SUCCESS Original Xbox compatibility install complete
Hacked XeFu Pack -> E:\Compatibility

Field                Value
Mode                 host
XeFu Set             Hacked XeFu Pack
Target               E:\
Compatibility Path   E:\Compatibility
Compatibility Files  Installed
Fixer Included       Yes
Fixer Path           E:\HddCompatibilityPartitionFixer
Files                23
Size                 31.42 MB
```

### Direct console install

```text
SUCCESS Original Xbox compatibility install complete
Hacked XeFu Pack -> /HddX/Compatibility

Field                Value
Mode                 console
XeFu Set             Hacked XeFu Pack
Target               192.168.1.186
Compatibility Path   /HddX/Compatibility
Compatibility Files  Installed
Fixer Included       No
Files                23
Size                 31.42 MB
```

## Notes

- The official ConsoleMods guidance still recommends creating `HddX` first, then copying the emulator files into `HddX:\Compatibility`.
- Stealth-server plugins can interfere with original Xbox game startup. If a game fails to boot, temporarily disable those plugins and test again.
- Some original Xbox titles run better with the console output set to `480p` instead of `720p`.
