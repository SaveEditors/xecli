# Beginner Guide

This guide covers the safe path from a fresh install to a working live-console session, including the Windows launcher, FTP checks, and avatar browsing.

## 1. Know the Minimum Requirements
On the PC side:

- Windows
- no extra runtime if you are using the published `win-x64` release package
- the .NET 10 SDK selected by `global.json` only if you are building or running XeCLI from source
- Network access to the console
- Optional: `XeTerminal.exe` if you want the guided Windows interface

On the console side:

- XBDM enabled
- JRPC2 if you want RPC, temps, CPU key, notifications, or Title ID reads
- An active console FTP service (Aurora, FreestyleDash, DashLaunch, or similar homebrew) if you want FTP-backed save, content, or file workflows
- The avatar browser works from the same install once the collection is configured

XeCLI does not publish third-party console plugin binaries. Use plugin files from sources you trust and are allowed to use. If you want the homebrew installer to copy them during staging, place your console-side plugin files in a `ConsoleDependencies` folder beside `rgh.exe` before running `rgh homebrew install`.

## 2. Run the Installer
The recommended guided install uses the published setup executable. XeCLI v2.0.0 also provides a portable ZIP for users who do not want Start menu, uninstall, or PATH changes. Both `win-x64` packages are self-contained and do not require a separate .NET installation. Windows 11 x64 is recommended. On Windows 10, Microsoft's .NET 10 support is limited to eligible Enterprise/LTSC releases: versions 1607, 1809, and 21H2. The installer minimum remains build 14393 (Windows 10 version 1607), but that build floor alone does not expand Microsoft's supported edition list.

### Verify the download

Download the package and its matching `.sha256` sidecar from the same GitHub release. This PowerShell example verifies the setup executable; replace the package name with `XeCLI-2.0.0-win-x64.zip` to verify the portable archive:

```powershell
$packagePath = ".\XeCLI-2.0.0-win-x64-setup.exe"
$expectedHash = ((Get-Content "${packagePath}.sha256" -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) { throw "SHA-256 mismatch. Do not run this package." }
"SHA-256 verified: $actualHash"
```

XeCLI v2.0.0 packages are not Authenticode-signed. Windows can therefore show **Unknown Publisher** or a Microsoft Defender SmartScreen warning. That warning is expected for this release, but it is not an integrity check: confirm that the package came from the official GitHub release and that its SHA-256 value matches before choosing **More info > Run anyway**.

### Use the setup wizard

After verification, launch:

```powershell
.\XeCLI-2.0.0-win-x64-setup.exe
```

The installer walks through:

- install scope: current user or all users
- install directory selection (defaults to `%LOCALAPPDATA%\Programs\XeCLI` for current user or `%ProgramFiles%\XeCLI` for all users)
- optional PATH registration for new terminals
- XeCLI Start menu shortcut creation
- optional XeCLI desktop shortcut creation

Setup installs XeCLI locally and can register `rgh` on PATH. It does not scan for, connect to, or run commands on a console. Discovery remains an explicit CLI action.

If you enable PATH registration, close any terminal that was already open before setup and open a new one before using `rgh`; an existing process will not receive the updated PATH. If you skip PATH registration, run `rgh.exe` from the installation directory you selected.

```powershell
rgh --help
rgh language
```

If you prefer the GUI launcher, open `XeTerminal.exe` from the Start menu. If you selected the installer shortcut task, the same launcher is also available on the desktop. The GUI path gives you the same core command surface with a more guided workflow, including quick FTP checks and avatar browsing.

### Upgrade or relocate an installed copy

Run a newer setup package against the same installation directory for an in-place upgrade. Existing language and first-run settings are preserved. Setup blocks an upgrade when Windows reports an existing XeCLI installation in another or ambiguous location, or when the existing installation's ownership manifest is missing and its files cannot be upgraded safely.

For either relocation or ownership-manifest error:

1. Uninstall the existing XeCLI entry through Windows while retaining its settings.
2. Run the new setup package and select the desired installation directory.
3. Open a new terminal if PATH registration changed.

The v1.x uninstaller may leave its `.xecli-install` marker in an otherwise empty program folder. v2.0.0 setup verifies and removes that exact legacy marker automatically. If any other file remains, setup leaves the folder untouched and asks you to select an empty dedicated directory.

The v2.0.0 uninstaller retains settings under `%APPDATA%\XeCLI`, cache and the default command log under `%LOCALAPPDATA%\XeCLI`, and default XeTerminal captures under the Windows Pictures folder at `XeCLI\Captures`. Configured capture/log directories and any `XECLI_HOME` directory are also left untouched. Remove retained data manually only when you no longer need it.

### Use the portable ZIP

Extract `XeCLI-2.0.0-win-x64.zip` and run `rgh.exe` or `XeTerminal.exe` from its single root folder. The included `xecli.portable` marker makes XeCLI store configuration, cache, command logs, and default captures under `UserData` beside the executables. Portable mode makes no installer, shortcut, or PATH changes unless you explicitly run `rgh install`.

To update a portable copy, verify and extract the new ZIP to a new empty folder. Close the old copy, then copy only its `UserData` folder into the new package if you want to retain portable settings and history. Keep the previous folder until the new copy starts successfully; do not extract a new release over an older program folder.

`XECLI_HOME` takes precedence over portable mode. If that environment variable is set, XeCLI stores application state in the directory it names instead of package-local `UserData`; clear it when you want the extracted folder to remain fully self-contained.

From a verified portable folder, `rgh install` can copy the release into a current-user or all-users installation and optionally register the command:

```powershell
.\rgh.exe install
.\rgh.exe install --path "$env:LOCALAPPDATA\Programs\XeCLI" --no-path
```

The CLI-driven installer does not create the setup wizard's Start menu shortcut or Windows uninstall entry. Its `--uninstall` option removes command registration but does not delete the installed folder or retained user data. See [Commands Reference](Commands#rgh-install) for current-user, all-users, source, path, quiet, and machine-PATH options.

## 3. Discover or Set a Target Manually
To discover and select a target, use the explicit discovery path:

```powershell
rgh start
```

Direct targeting:

```powershell
rgh target --set <console-ip>
rgh connect <console-ip>
```

Use `rgh connect <console-ip>` when the console address changed or when XeCLI prints `Console connection failed`, `Console connection timed out`, or `Console connection did not complete` after discovery or setup.

If you use FTP features, set that target too:

```powershell
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
```

FTP-backed commands need the console's FTP service to be running. If FTP is not running, XBDM-backed `rgh fs ...` workflows can still work.

Read [FTP-and-File-Transfer.md](FTP-and-File-Transfer) if you want the full FTP command set and the guidance on when to use FTP instead of `rgh fs`.

## 4. Confirm the Console Is Reachable
```powershell
rgh ping
rgh status
rgh status --quick
```

After you explicitly select a target, use these commands to verify reachability or change the target.

If reachability fails after moving the console or changing networks, run:

```powershell
rgh connect <console-ip>
rgh status --quick
```

## 5. Resolve the Active Title
```powershell
rgh title
rgh title --json
```

`rgh title` with no arguments resolves the active Title ID from the connected console. XeCLI uses a configured user Title Database or a trusted path-based name such as `Aurora` when available; otherwise it keeps the raw `0xXXXXXXXX` ID. Configure an optional CSV/TXT file under XeTerminal **Settings > Paths**.

## 6. Inspect Live State
Modules:

```powershell
rgh modules list
rgh modules info --name Aurora.xex
```

Memory:

```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh mem peek --addr 0x30000000 --type u32
```

Screenshot:

```powershell
rgh screenshot --out .\screen.png
```

Avatar library:

```powershell
rgh avatar browse --remote
rgh avatar choose --search "Black Ops"
rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user
```

Use `rgh avatar browse` when you want a Windows picker with search, filters, and multi-select. Use `rgh avatar choose` when you want terminal-first selection.

## 7. Use FTP-Backed Workflows
Browse files:

```powershell
rgh ftp list --path /Hdd1/
```

List saves for a title:

```powershell
rgh save list --titleid FFFE07D1 --device Hdd1
```

List installed content:

```powershell
rgh content list --device Hdd1 --show-types
```

List DashLaunch plugins:

```powershell
rgh plugin list
```

Read [FTP-and-File-Transfer.md](FTP-and-File-Transfer) for the broader file and storage workflow.

## 8. Install or Stage Homebrew
XeCLI can either stage the common public dashboards and tools to a USB drive or folder, or install them directly onto a detected console drive.

```powershell
rgh homebrew install aurora --usb E:\XeCLI-Stage
rgh homebrew install all --usb E:\XeCLI-Stage
rgh homebrew install aurora --device Hdd1 --ini-mode merge
rgh homebrew install xm360 --usb E:\XeCLI-Stage
rgh homebrew install timefixer --usb E:\XeCLI-Stage
rgh homebrew install simple360 --usb E:\XeCLI-Stage
rgh homebrew install xelllaunch --usb E:\XeCLI-Stage
```

USB or folder staging:

- downloads the selected public package archives
- creates or reuses package folders under the staging path, replacing matching files while leaving unrelated files in place
- copies user-supplied files from `ConsoleDependencies\` into `Plugins\` when that folder is present
- replaces the staging folder's `launch.ini` when Aurora is selected or already present
- prompts before staging or installing; non-interactive use is refused unless `--auto-confirm` is explicitly supplied
- supports Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch

If no removable USB drive is visible, use a normal folder path instead:

```powershell
rgh homebrew install all --usb E:\XeCLI-Stage
```

Use a dedicated staging folder rather than a drive root so existing USB content remains easy to review.

Direct console install:

- detects only `Hdd1`, `Usb0`, `Usb1`, and `Usb2` from the live console over FTP
- asks which detected drive you want to install to
- asks whether XeCLI should generate a new `launch.ini`, merge plugin entries into the existing file, or leave it unchanged
- uploads the selected dashboards/tools and any user-supplied `ConsoleDependencies` plugins directly onto the selected console drive
- stops on an existing destination-file conflict instead of silently replacing console files

Read [Homebrew-and-USB.md](Homebrew-and-USB) for the full package workflow.

### Original Xbox compatibility
If you want original Xbox backwards compatibility on a modded console, use the dedicated `ogxbox` workflow instead of the normal homebrew installer:

```powershell
rgh ogxbox list
rgh ogxbox install hacked --usb E:
rgh ogxbox install hud --include-fixer
```

Use `hacked` as the default choice for the widest compatibility on an exploited console. Use `hud` only if you specifically want the 360 guide available inside original Xbox titles and can accept the extra performance cost. Use `retail` when you want the stock Microsoft emulator behavior.

Read [Original-Xbox-Compatibility.md](Original-Xbox-Compatibility) for the XeFu set differences and `HddX:\Compatibility` workflow.

## 9. Launch and Notify
Launch a XEX:

```powershell
rgh launch Hdd1:\Aurora\Aurora.xex --titleid FFFE07D1 --verify --wait
```

Send a notification:

```powershell
rgh notify "XeCLI connected" 14
```

## 10. Check Session and Hardware State
Signed-in user:

```powershell
rgh signin state
```

Ring light:

```powershell
rgh led set --preset quadrant1
rgh led state
```

If you want the hardware/session details explained in depth, read [Hardware-and-System.md](Hardware-and-System).

## 11. Avoid the Common Mistakes
Do not start with dangerous commands until the basic path is stable.

Start with:

- `status`
- `title`
- `modules list`
- `ftp list`
- `screenshot`
- `signin state`
- `homebrew install aurora --usb E:\XeCLI-Stage`
- `ogxbox install hacked --include-fixer --usb E:`
- `led set --preset quadrant1`

Delay these until you know the target is stable:

- `mem poke`
- `mem search --freeze`
- `modules unload --force`
- `modules load --system`
- `fan set`
- deletion commands

## 12. Know Where to Go Next
- [Commands Reference](Commands) for full command coverage
- [Homebrew and USB](Homebrew-and-USB) for USB/folder staging and direct console installs
- [Original Xbox Compatibility](Original-Xbox-Compatibility) for XeFu pack installs and HddX setup
- [Hardware and System Controls](Hardware-and-System) for sign-in, LED, fan, and SMC behavior
- [XNotify](XNotify) for icon IDs and notification usage
- [Advanced Guide](Advanced-Guide) for reverse-engineering and automation workflows
- [Troubleshooting](Troubleshooting) when the console or plugin stack does not behave as expected
