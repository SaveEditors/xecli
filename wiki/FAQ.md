# FAQ

## Is the project name `XeCLI` or `rgh`
Both, by design.

- Repository and product name: `XeCLI`
- Installed command: `rgh`

## Does the release require .NET to be installed separately
No, not for the published `win-x64` release zip.

The release package is self-contained. Source builds still require the .NET 10 SDK/runtime.

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

## Does XeCLI include console-side plugins
Partially. The source tree and release package now ship common console-side `.xex` dependency files in `ConsoleDependencies/`, including `xbdm.xex`, `XDRPC.xex`, and `JRPC2.xex`.

That does not mean XeCLI installs or enables them automatically. Console-side services such as XBDM, JRPC2, and FTP still need to be installed and configured on the target console.

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

## Does XeCLI require Ghidra
No. Ghidra is only required for the `ghidra` and `xex decompile` workflows.

## Does XeCLI replace Aurora, Neighborhood, or XeXMenu completely
No single tool replaces every scene workflow perfectly. The goal of XeCLI is to cover the high-value terminal-first workflows cleanly enough that you do not need to bounce between small one-off utilities for status, dumps, memory work, saves, content, notifications, screenshots, and scripted automation.

## How can I support the project
Use the Ko-fi link in the README or the support card in the published wiki:

- [ko-fi.com/saveeditors](https://ko-fi.com/saveeditors)
