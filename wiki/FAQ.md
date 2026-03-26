# FAQ

## Is the project name `XeCLI` or `rgh`
Both, by design.

- Repository and product name: `XeCLI`
- Installed command: `rgh`

## Does the release require .NET to be installed separately
No, not for the published `win-x64` release zip.

The release package is self-contained. Source builds still require the .NET 10 SDK/runtime.

## How should I install XeCLI
Run:

```powershell
.\XeCLI-1.0.8-setup-win-x64.exe
```

That is the normal Windows install path.

The installer can choose the install scope, ask for the install directory, register `rgh` for new terminals, set the initial UI language, and then offer to connect to a detected console.

After installation, open a new terminal and use:

```powershell
rgh --help
rgh language
```

## Does the installer add `rgh` to PATH
It can.

During install, XeCLI asks whether you want `rgh` available in new terminals. You can accept or skip that step.

## Can XeCLI stage dashboards to USB or install them directly to the console
Yes.

Use the dedicated `homebrew` branch:

```powershell
rgh homebrew install aurora --usb E:
rgh homebrew install all --usb E: --auto-confirm
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

With `--usb`, XeCLI stages the packages onto a USB drive or folder. Without `--usb`, XeCLI connects to the console, detects only `Hdd1`, `Usb0`, `Usb1`, and `Usb2`, installs to the selected drive, and asks whether it should generate a new `launch.ini`, merge plugin entries into the existing one, or leave `launch.ini` alone. XeCLI asks before staging or installing unless you use `--auto-confirm` or run the command non-interactively.

By default, temporary extraction now happens on the target drive instead of `%LOCALAPPDATA%`, which avoids the common "not enough space on disk" failure when staging larger packages.

Read `Homebrew and USB` for the full package workflow.

## Does XeCLI fetch Title ID metadata from the internet
No. The bundled Title ID database ships with the repository and with the published release.

## Can I use the Title ID database in my own tools
Yes. That is a supported use case. Read the bundled CSV/TXT files directly or use `rgh title --json`.

## Where should I start if I am new to XeCLI
Use these pages in order:

1. `Beginner Guide`
2. `Commands Reference`
3. `XNotify`
4. `CLI Help Output`
5. `Troubleshooting`

## Why did the installer not offer to connect to my console
That prompt only appears when XeCLI actually detects a console after install.

If it does not appear:

- the console may not be reachable yet
- discovery may not have found a target fast enough
- or you may have used a scripted install path

Manual fallback:

```powershell
rgh start
rgh status
```

## Does XeCLI include console-side plugins
Yes. The source tree and release package ship the common console-side `.xex` files XeCLI expects, including `xbdm.xex`, `XDRPC.xex`, and `JRPC2.xex`.

Those files are provided so users can deploy the exact plugin versions XeCLI was built and tested against.

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

For the full icon list and usage notes, read `XNotify`.

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

Read `Hardware and System Controls` for the operational details.

## Why does `status --quick` show skipped fields
Because those fields were intentionally skipped. The current release distinguishes skipped from unknown so the operator can tell the difference between a deliberate fast path and a failed probe.

## Why does `threads list` show image and address columns instead of thread names
Because many retail, JTAG, and plugin-backed XBDM targets do not expose usable thread-name pointers.

XeCLI now resolves the thread start routine address, maps it back to a loaded image when possible, and shows `Image`, `Start Addr.`, and `End Addr.` in the thread table. `End Addr.` is the containing image end boundary, not a reconstructed function end.

## Can module load require a reboot
Yes. Some live module loads do not complete as a clean hot-load on every console or plugin stack. Use `--reboot-expected` and verify with `rgh modules pending`.

## Is module unload safe
Not universally. That is why `modules unload` requires `--force`.

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
Yes. Add entries to:

- `%APPDATA%\XeCLI\titleids.local.csv`

## Does XeCLI require Ghidra or IDA
No. Reverse-engineering tooling is optional.

- Ghidra is only required for the `ghidra` and `xex decompile` workflows.
- IDA Pro is only required for the `ida` and `xex ida-decompile` workflows.
- XeCLI does not bundle either tool.
- Ghidra is documented in the CLI as `(Free)`.
- IDA support is pinned to `IDA Pro 9.1.250226` with the maintained `SaveEditors/idaxex 0.42b-compat` line.

## Does XeCLI replace Aurora, Neighborhood, or XeXMenu completely
No single tool replaces every scene workflow perfectly. The goal of XeCLI is to cover the high-value terminal-first workflows cleanly enough that you do not need to bounce between small one-off utilities for status, dumps, memory work, saves, content, notifications, screenshots, and scripted automation.

## How can I support the project
Use the Ko-fi link in the README or the support button in the published wiki:

- [ko-fi.com/saveeditors](https://ko-fi.com/saveeditors)
