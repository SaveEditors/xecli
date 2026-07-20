# FAQ

## Is the project name `XeCLI` or `rgh`
Both, by design.

- Repository and product name: `XeCLI`
- Installed command: `rgh`

## Does the release require .NET to be installed separately
No, not for the published installer or portable ZIP.

Both release paths are self-contained. Source builds require the .NET 10 SDK selected by `global.json`.

## How should I install XeCLI
The guided install uses `XeCLI-2.0.0-win-x64-setup.exe`. The no-install option is `XeCLI-2.0.0-win-x64.zip`; extract it and run `rgh.exe` or `XeTerminal.exe` from the single root folder. Both v2.0.0 packages are self-contained and do not require a separate .NET installation. Windows 11 x64 is recommended. On Windows 10, Microsoft's .NET 10 support is limited to eligible Enterprise/LTSC releases: versions 1607, 1809, and 21H2. The installer minimum remains build 14393 (Windows 10 version 1607), but that build floor alone does not expand Microsoft's supported edition list.

Verify the downloaded package against its matching `.sha256` sidecar from the same GitHub release:

```powershell
$packagePath = ".\XeCLI-2.0.0-win-x64-setup.exe"
$expectedHash = ((Get-Content "${packagePath}.sha256" -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) { throw "SHA-256 mismatch. Do not run this package." }
"SHA-256 verified: $actualHash"
```

XeCLI v2.0.0 is not Authenticode-signed, so Windows can show **Unknown Publisher** or Microsoft Defender SmartScreen. Proceed only when the package came from the official release and its SHA-256 value matches. Then launch the verified setup executable:

```powershell
.\XeCLI-2.0.0-win-x64-setup.exe
```

The installer can choose the install scope and directory, optionally add `rgh` to PATH for new terminals, create the Start menu entry, optionally create a desktop shortcut, and set the initial UI language. The default directory is `%LOCALAPPDATA%\Programs\XeCLI` for current user or `%ProgramFiles%\XeCLI` for all users. Setup does not discover or connect to consoles.

If PATH registration was enabled, open a new terminal before using `rgh`. If it was skipped, run `rgh.exe` from the selected installation directory.

```powershell
rgh --help
rgh language
```

## Does the installer add `rgh` to PATH
It can.

During setup, XeCLI asks whether you want `rgh` available through PATH. You can accept or skip that step. Close existing terminals and open a new one after any PATH change because running processes retain their previous environment.

## What is the difference between setup, portable use, and `rgh install`

- Setup provides the guided installer, Start menu entry, optional desktop shortcut, optional PATH registration, and Windows uninstall integration.
- Portable use makes no installation changes by default. The `xecli.portable` marker stores application state under `UserData` beside the executables.
- `XECLI_HOME` overrides installed and portable state locations. Clear it when you want package-local portable state.
- Running `.\rgh.exe install` from a verified portable release starts the CLI-driven copy and command-registration workflow for the current user or all users. It does not create setup shortcuts or a Windows uninstall entry.
- `rgh install --uninstall` removes CLI command registration; it does not delete the installed folder or user data.

See [Commands Reference](Commands#rgh-install) for `--source`, `--path`, `--machine`, `--no-path`, `--machine-path`, `--quiet`, and uninstall behavior.

## How do I upgrade or move an installed copy

Run the newer setup package against the existing installation directory for an in-place upgrade. If setup reports a different or ambiguous existing location, or says the ownership manifest is missing, uninstall the existing Windows entry while retaining its settings and then rerun setup for the desired location.

After a v1.x uninstall, setup can reuse the old program folder when its only remaining item is the exact legacy `.xecli-install` marker; setup verifies and removes that marker before extracting v2 files. Any other nonempty destination remains blocked.

The v2.0.0 uninstaller retains settings under `%APPDATA%\XeCLI`, cache and the default command log under `%LOCALAPPDATA%\XeCLI`, and default XeTerminal captures under the Windows Pictures folder at `XeCLI\Captures`. Configured capture/log directories and any `XECLI_HOME` directory are also retained. Open a new terminal if the reinstall changes PATH.

For a portable update, verify and extract the new ZIP into a new empty folder. After closing the old copy, copy only its `UserData` folder into the new package if you want to retain portable state. Do not extract the new program files over the old folder.

## How do I create a support bundle
Run:

```powershell
rgh support bundle
rgh diagnostics bundle --support
rgh diagnostics bundle
rgh diag bundle
```

By default, XeCLI creates `xecli-support-<timestamp>.zip` in the current directory for `rgh support bundle`, and `xecli-diagnostics-<timestamp>.zip` for `rgh diagnostics bundle`. Use `--out <PATH>` to choose a destination, or add `--folder` if support asks for an unpacked folder.

Review the bundle before sharing it. XeCLI redacts local paths, IP addresses, console IDs, and known secrets, but command history can still show recent command context. `rgh diagnostics bundle --support` produces the same support layout through the diagnostics tree.

## Can XeCLI stage dashboards to USB or install them directly to the console
Yes.

Use the dedicated `homebrew` branch:

```powershell
rgh homebrew install aurora --usb E:\XeCLI-Stage
rgh homebrew install all --usb E:\XeCLI-Stage
rgh homebrew install aurora --device Hdd1 --ini-mode merge
```

Supported package IDs are:

- `aurora`
- `dashlaunch`
- `xexmenu`
- `fsd`
- `xm360`
- `timefixer`
- `simple360`
- `xelllaunch`
- `all`

With `--usb`, XeCLI stages the packages onto a USB drive or folder. Without `--usb`, XeCLI connects to the console, detects only `Hdd1`, `Usb0`, `Usb1`, and `Usb2`, installs to the selected drive, and asks whether it should generate a new `launch.ini`, merge plugin entries into the existing one, or leave `launch.ini` alone. XeCLI asks before staging or installing unless you use `--auto-confirm`. In a non-interactive context, the command refuses to continue unless that bypass is supplied explicitly.

By default, temporary extraction now happens on the target drive instead of `%LOCALAPPDATA%`, which avoids the common "not enough space on disk" failure when staging larger packages.

Read [Homebrew and USB](Homebrew-and-USB) for the full package workflow.

## Does XeCLI fetch Title ID metadata from the internet
No. XeCLI neither bundles nor downloads a title database. Use an optional local CSV/TXT file, or rely on raw hexadecimal Title IDs and the small local cache of trusted names observed from live executable paths.

## Can I use the Title ID database in my own tools
Yes. Read your operator-supplied CSV/TXT file directly or use `rgh title --json`, whose `Database` object reports source and warning status.

## Where should I start if I am new to XeCLI
Use these pages in order:

1. [Beginner Guide](Beginner-Guide)
2. [Commands Reference](Commands)
3. [XNotify](XNotify)
4. [CLI Help Output](CLI-Help)
5. [Troubleshooting](Troubleshooting)

## Why did the installer not connect to my console

Console discovery is deliberately separate from installation. Run it explicitly after setup:

```powershell
rgh start
rgh connect <console-ip>
rgh status
```

If discovery or connection reports `Console connection failed`, `Console connection timed out`, or `Console connection did not complete`, confirm the console IP and run `rgh connect <console-ip>` before retrying `rgh status`.

## Does XeCLI include console-side plugins
No. XeCLI does not publish or supply third-party console-side plugin binaries. Use plugin files from sources you trust and are allowed to use. If you want XeCLI to copy your files during homebrew staging or console installation, place them in a `ConsoleDependencies` folder beside `rgh.exe` before running `rgh homebrew install`.

## Why does `rgh title` work with no arguments
Because the command is designed to resolve the active title by default when no Title ID is supplied.

## How do I send a notification with a specific icon
Use a numeric logo ID or a saved preset.

Examples:

```powershell
rgh notify "XeCLI connected" 14
rgh notify --message "Patch applied" --logo 16
rgh notify-icons add --name success --logo 14
rgh notify --message "Transfer complete" --icon success
```

For the full icon list and usage notes, read [XNotify](XNotify).

## How do I check who is signed in
Use:

```powershell
rgh signin state
```

That command isolates the active session state, gamertag, XUID, and slot without forcing you to scan the full `status` output.

## Can XeCLI control the ring light
Yes.

Examples:

```powershell
rgh led set --preset quadrant1
rgh led set --preset all-green
rgh led state
```

`led state` shows the last XeCLI-applied ring-light state.

## Can XeCLI control fan speed
XeCLI exposes manual fan command dispatch and a cached `fan show` view.

Examples:

```powershell
rgh fan set --speed 55 --channel both
rgh fan show
```

Read [Hardware and System Controls](Hardware-and-System) for the operational details.

## Why does `status --quick` show skipped fields
Because those fields were intentionally skipped. The current release distinguishes skipped from unknown so you can tell the difference between a deliberate fast path and a missing response.

## Why does `threads list` show image and address columns instead of thread names
Because many retail, JTAG, and plugin-backed XBDM targets do not expose usable thread-name pointers.

XeCLI now resolves the thread start routine address, maps it back to a loaded image when possible, and shows `Image`, `Start Addr.`, and `End Addr.` in the thread table. `End Addr.` is the containing image end boundary, not a reconstructed function end.

## Can module load require a reboot
Yes. Some live module loads do not complete as a clean hot-load on every console or plugin stack. Use `--reboot-expected` and verify with `rgh modules pending`.

## Is module unload safe
Not universally. That is why `modules unload` requires `--force`, blocks known system and boot-plugin modules behind the additional `--allow-critical` flag, and verifies a fresh XBDM session before reporting success. Prefer a disposable title module; unloading XBDM, JRPC2, XDRPC, XAM, the kernel, or another boot plugin can freeze the dashboard even after a nominal RPC result.

## Does XeCLI support scripting
Yes. Prefer commands with `--json` when building scripts or companion tools.

## Which commands are best for external dashboards or companion apps
Start with:

- `rgh status --json`
- `rgh title --json`
- `rgh modules list --json`
- `rgh profiles --json`
- `rgh scan --json`
- `rgh ghidra verify --json`

## Can I extend the metadata set
Yes. Select a CSV/TXT file in XeTerminal **Settings > Paths**, set `XECLI_TITLE_DATABASE`, or place `titleids.csv` in the active XeCLI configuration directory: `%APPDATA%\XeCLI` when installed, `UserData` beside the executables in portable mode, or the directory selected by `XECLI_HOME`. Existing `titleids.local.csv` files remain a compatibility fallback.

## Does XeCLI require Ghidra or IDA
No. Reverse-engineering tooling is optional.

- Ghidra is only required for the `ghidra` and `xex decompile` workflows.
- IDA Pro is only required for the `ida` and `xex ida-decompile` workflows.
- XeCLI does not bundle either tool.
- Ghidra is documented in the CLI as `(Free)`.
- IDA support is documented against `IDA Pro 9.3` with the SaveEditors `idaxex 9.3` loader, and legacy `IDA Pro 9.1.250226` archives remain supported for compatible installs.

## Does XeCLI replace Aurora, Neighborhood, or XeXMenu completely
No single tool replaces every scene workflow perfectly. The goal of XeCLI is to cover the high-value terminal-first workflows cleanly enough that you do not need to bounce between small one-off utilities for status, dumps, memory work, saves, content, notifications, screenshots, and scripted automation.

## How can I support the project
Use the project Ko-fi page:

- [ko-fi.com/xecli](https://ko-fi.com/xecli)
