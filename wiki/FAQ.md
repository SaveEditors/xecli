# FAQ

## Is the project name `XeCLI` or `rgh`
Both, by design.

- Repository and product name: `XeCLI`
- Installed command: `rgh`

## Does XeCLI fetch Title ID metadata from the internet
No. The bundled Title ID database ships with the repository and with the published release.

## Can I use the Title ID database in my own tools
Yes. That is a supported use case. Read the bundled CSV/TXT files directly or use `rgh title --json`.

## Does XeCLI include console-side plugins
Partially. The source tree and release package now ship common console-side `.xex` dependency files in `ConsoleDependencies/`, including `xbdm.xex`, `XDRPC.xex`, and `JRPC2.xex`.

That does not mean XeCLI installs or enables them automatically. Console-side services such as XBDM, JRPC2, and FTP still need to be installed and configured on the target console.

## Why does `rgh title` work with no arguments
Because the command is designed to resolve the active title by default when no Title ID is supplied.

## Why does `status --quick` show skipped fields
Because those fields were intentionally skipped. The current release distinguishes skipped from unknown so the operator can tell the difference between a deliberate fast path and a failed probe.

## Can module load require a reboot
Yes. Some live module loads do not complete as a clean hot-load on every console or plugin stack. Use `--reboot-expected` and verify with `rgh modules pending`.

## Is module unload safe
Not universally. That is why `modules unload` requires `--force`.

## Does XeCLI support scripting
Yes. Prefer commands with `--json` when building scripts or companion tools.

## Can I extend the metadata set
Yes. Add entries to:

- `%APPDATA%\XeCLI\titleids.local.csv`

## Does XeCLI require Ghidra
No. Ghidra is only required for the `ghidra` and `xex decompile` workflows.

## Does XeCLI replace Aurora, Neighborhood, or XeXMenu completely
No single tool replaces every scene workflow perfectly. The goal of XeCLI is to cover the high-value terminal-first workflows cleanly enough that you do not need to bounce between small one-off utilities for status, dumps, memory work, saves, content, notifications, screenshots, and scripted automation.
