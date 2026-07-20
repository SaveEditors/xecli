# XeCLI v2.0.0 Updates

XeCLI v2.0.0 brings the command-line toolkit and XeTerminal desktop workspace together in one stable, self-contained Windows release for Xbox 360 RGH/JTAG workflows. This page describes user-facing changes from the public v1.1.0 release.

## Commands, Reports, and Automation

- Added `rgh report` to save a redacted snapshot of console state as Markdown, HTML, CSV, or JSON. Two saved snapshots can also be compared offline.
- Added `rgh report-profile` to reuse report settings and `rgh target-saved` to maintain named console targets.
- Added `rgh script run` for repeatable, one-command-per-line command files. Dry runs validate without execution, transcripts are redacted, and `rgh script replay` reviews saved transcripts offline.
- Added `rgh log list` and `rgh log export` to review or export XeCLI's local command history; these are not Xbox or XBDM system logs.
- Added `rgh release-notes` (`rgh changelog`) to read the release history bundled with XeCLI without accessing GitHub or another online service.
- Added shell-completion output for PowerShell, Bash, and Zsh through `rgh completion`.
- Added `rgh health`, redacted diagnostics, and curated support bundles that can be created without contacting a console.
- Added optional operator-supplied Title Database lookup while keeping discovered title data local to the user's machine.

## Debugging and Analysis

- Added module/RVA/Ghidra address translation through the module command families and integrated that resolution with code and data breakpoint creation.
- Added memory sessions for saved map, dump, and search captures; offline map and dump comparisons; and importable/exportable memory bookmarks.
- Added flat JSON trainer workflows with explicit live addresses and a dry-run preview.
- Expanded XEX tooling with strings, headers, address maps, dumps, and portable analysis bundles.
- Expanded Ghidra and IDA helpers with loader setup, headless analysis, decompilation, output checks, and symbol-sidecar export that can feed memory bookmarks.

## Files, Targets, and Local Content

- Added network diagnostics, recent-target history, named saved targets, and clearer recovery guidance when a configured console is unavailable.
- Added safer FTP replacement review with source and destination details and replace, skip, apply-to-all, or cancel choices.
- Added FTP sync and single-file resume workflows, remote hashing, local-to-remote diff, recursive transfer summaries, upload dry runs, and optional SHA-256, SHA-1, or MD5 verification after transfers.
- Added the optional `rgh xtaf-cli` integration for configuring, installing, and invoking the external SaveEditors Xtaf-CLI; promoted `rgh xtaf` as the canonical built-in FATX surface while retaining compatibility aliases.
- Maintained local CON, profile, and GPD/XDBF inspection and editing alongside save, installed-content, plugin, avatar, XTAF/FATX, and GOD workflows.
- When a CON or profile mutation must preserve a console signature, XeCLI requires `--keyvault` with the operator's own decrypted keyvault. Read-only inspection does not require it, and keyvault files should never be shared.

## XeLL and Hardware

- Reworked `rgh xell boot` and `rgh xell info` around managed XellLaunch or QuickBoot launch paths and clearer XeLL service inspection.
- Expanded `rgh xell kv export` with packaged keyvault and CPU-key output plus optional repeated comparison.
- Moved NAND backup to `rgh xell nand dump` and expanded it with repeated dump comparison, integrity manifests, integrity-checked archives, and guarded return-to-dashboard behavior.
- Added validated SMC and temperature reporting that rejects incomplete, sentinel, and implausible values.

## XeTerminal Workspace

- Expanded connection and session presentation with console identity, temperatures, traffic, storage, detected drives, and plugin/module inventory.
- Expanded the local and remote file workspace with coordinated XBDM and FTP browsing, transfer review, and status feedback.
- Added settings for console defaults, reconnect behavior, FTP, captures, logs, external tools, language, appearance, and local preferences.
- Added quick actions with `Ctrl+K`, command-history search with `Ctrl+R`, Up/Down history navigation, and 19 coordinated themes with Cyan as the default.

## Reliability Fixes Since v1.1.0

- XBDM operations now require the expected status, response type, payload, and binary length. Rejected, empty, truncated, or malformed replies are reported as failures instead of successful results.
- XBDM TCP connection, greeting, read, and multi-response paths now use explicit deadlines and size limits, preventing stalled or unbounded responses from hanging a command indefinitely.
- FTP downloads are written to temporary files and checked for the expected size before replacing the destination. Uploads use a temporary remote name and move into place only after the transfer completes, so incomplete transfers do not appear under the requested final name.
- XBDM and FTP path arguments now reject quotes, control characters, invalid roots, and unsafe traversal before a command is sent or a local destination is selected.
- Configuration, bookmarks, profiles, target history, and other saved JSON state now use atomic replacement so an interrupted write does not leave a partially written file in place.
- SMC and JRPC2 hardware readouts now reject invalid version sentinels, incomplete payloads, and temperatures outside the supported range instead of displaying them as valid console data.
- Installer and CLI-driven upgrades now use owned-file manifests and recoverable transactions. An interrupted update can restore the prior application files, while unrelated destination content and user-owned state remain outside the update set.

## Compatibility Changes from v1.1.0

- The unsupported `rgh spoof` command family is not included in v2.0.0. XeCLI does not provide gamertag, XUID, or remote spoofing.
- NAND backup moved from `rgh nand dump` to `rgh xell nand dump`, keeping XeLL launch, service checks, transfer, comparison, packaging, and dashboard return under one command branch.
- `rgh xtaf` is the canonical built-in FATX command in v2.0.0. The existing `rgh fatman` and `rgh fatx` names remain available as compatibility aliases.
- The bundled title list from v1.1.0 is no longer shipped. Title lookup can use an optional database supplied by the operator, and discovered title data remains local.

## Installer, Portable Use, and Data

- v2.0.0 targets self-contained x64 Windows packages, with Windows 11 recommended. On Windows 10, Microsoft supports .NET 10 only on eligible Enterprise and LTSC releases (21H2, 1809, and 1607); the installer requires build 14393 or later. No 32-bit package is published.
- Both the installer and portable ZIP are self-contained `win-x64` packages containing `rgh.exe` and `XeTerminal.exe`; no separate .NET installation is required.
- The installer creates the XeCLI Start menu entry automatically. XeCLI desktop shortcut creation and adding `rgh` to `PATH` are optional.
- Installer upgrades preserve settings and stop before writing when a destination is portable, contains unrelated files, or cannot be identified safely. The CLI-driven `rgh install` path verifies the portable package, accepts only a dedicated install folder, updates XeCLI application files without removing user content, and switches the copied release to installed-mode storage.
- After a v1.x uninstall, setup recognizes and removes the old installer's lone `.xecli-install` marker so v2.0.0 can reuse that now-empty directory without weakening protection for unrelated files.
- Installed mode uses `%APPDATA%\XeCLI` for configuration, `%LOCALAPPDATA%\XeCLI` for cache and the default command log, and the Windows Pictures folder for default XeTerminal captures. Uninstall retains these, any configured capture or log directories, and any `XECLI_HOME` directory for manual removal.
- Portable mode performs no installation or `PATH` changes and stores configuration, cache, logs, and default captures under package-local `UserData`. In either mode, `XECLI_HOME` overrides the default state roots with the directory it names.
- Public v2.0.0 packages are intentionally unsigned. Windows may show **Unknown Publisher** or a SmartScreen warning; verify each download against its published `.sha256` file before running it.

See the [XeCLI Wiki](https://github.com/SaveEditors/xecli/wiki) for setup, exhaustive command documentation, console requirements, and troubleshooting.
