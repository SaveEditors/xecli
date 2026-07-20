# Releases

XeCLI release highlights and shipped change history for the stable v2.0.0 release and prior public versions.

Use `rgh changelog` or `rgh release-notes` to read the built-in v2.0.0 entry and archived v1.1.0 entry without network access. `--latest` shows v2.0.0, `--version` selects a built-in entry, and `--json` returns machine-readable output. This page remains the complete archive for every public version.

## Current Release Focus

`v2.0.0` is a hardening and usability release for RGH/JTAG operators. It combines stricter command-result handling, live debugging and address tools, a production XeTerminal workspace, safer FTP transfers, configurable preferences, validated hardware status, and self-contained Windows packaging.

The current release uses a self-contained `win-x64` installer and portable ZIP; XeCLI does not publish a 32-bit artifact. Windows 11 x64 is recommended. On Windows 10, Microsoft's .NET 10 support is limited to eligible Enterprise/LTSC releases: versions 1607, 1809, and 21H2. The installer minimum remains build 14393 (Windows 10 version 1607), but that build floor alone does not expand Microsoft's supported edition list. XeTerminal ships in both packages with Start menu integration in the installer, optional desktop shortcut and PATH registration, and direct no-argument launch from the portable folder. The packages are not Authenticode-signed, so users should expect an Unknown Publisher or SmartScreen warning and verify the matching `.sha256` sidecar before running either package.

## Release Index

| Release | Focus | Links |
| --- | --- | --- |
| `v2.0.0` | Stable XeTerminal workspace, strict command results, live address mapping, safer transfers, settings, and portable packaging | [Release notes](#v200-stable-release) |
| `v1.1.0` | XeTerminal beta, Discord Rich Presence, and universal installer packaging | [Release notes](#archived-v110-xeterminal-beta-and-release-polish) |
| `v1.0.9` | Critical debugging fixes, XeTerminal beta, and universal installer packaging | [Release notes](#v109-critical-debugging-bug-fixes) |
| `v1.0.8` | XTAF promotion, standalone XeCLI-XellFetch packaging, and installer polish | [Release notes](#v108-xtaf-packaging-and-installer-polish) |
| `v1.0.7` | Spanish localization, dark Inno Setup installer, Ko-fi support, and the canonical GitHub wiki migration | [Release notes](#v107-spanish-localization-inno-setup-and-wiki) |
| `v1.0.6` | Automated NAND dumping, managed XeLL/XeCLI-XellFetch staging, integrity-checked packaging, and safe reboot control | [Release notes](#v106-automated-nand-dumping-and-xecli-xellfetch) |
| `v1.0.5` | Local content workflows, thread metadata fallback, installer fixes, and release packaging | [Release notes](#v105-local-content-and-release-polish) |
| `v1.0.4` | IDA debugger/decompiler support and the expanded XTAF/FATX workflow | [Release notes](#v104-ida-and-xtaf-expansion) |
| `v1.0.3` | Original Xbox compatibility staging and install workflows | [Release notes](#v103-original-xbox-compatibility-update) |
| `v1.0.2` | Installer split and homebrew package staging | [Release notes](#v102-installer-and-homebrew-update) |
| `v1.0.1` | Avatar library/install workflows and hardware controls | [Release notes](#v101-avatar-update) |
| `v1.0.0` | Initial public XeCLI release | [Release notes](#v100-initial-release) |

Published GitHub releases: [All releases](https://github.com/SaveEditors/xecli/releases)

Every v1.x entry below is archived release history. It is retained for reference and is not current installation guidance.

## v2.0.0 Stable Release

The changes below use the public v1.1.0 release as the baseline.

### Highlights

- Expanded the command surface to 62 root entries, with complete built-in help and a task-oriented command reference in this Wiki.
- Promoted XeTerminal from the v1.1.0 beta interface to the primary desktop workspace, with coordinated console state, inventory, storage, traffic, local and remote file browsing, quick actions, history search, settings, and 19 themes.
- Consolidated the Windows release into self-contained x64 installer and portable packages containing both `rgh.exe` and `XeTerminal.exe`.

### New and Expanded Workflows

- Added reports and saved report profiles, named targets and recent-target history, command-file execution, redacted transcripts, local command-log export, shell completion, offline health checks, diagnostics, and support bundles.
- Added module/RVA/Ghidra address translation, breakpoint address resolution, memory sessions and bookmarks, map and dump comparison, trainer workflows, and expanded XEX inspection.
- Expanded Ghidra and IDA integration with loader setup, headless analysis, decompilation, symbol export, output checks, and portable XEX analysis bundles.
- Added FTP conflict review, sync, single-file resume, remote hashing, local-to-remote comparison, recursive summaries, upload previews, and optional post-transfer checksums.
- Reworked XeLL launch and service inspection around managed XellLaunch or QuickBoot paths, expanded packaged keyvault export, and moved the expanded NAND comparison and packaging workflow to `rgh xell nand dump`.
- Added optional operator-supplied Title Database support and maintained local CON, profile, GPD/XDBF, save, content, plugin, avatar, XTAF/FATX, and GOD workflows.

### Reliability and Safety Fixes

- XBDM operations now require the expected status, response type, payload, and binary length. Rejected, empty, truncated, or malformed replies no longer appear as successful results.
- XBDM connection, greeting, read, and multi-response paths now use explicit deadlines and size limits.
- FTP downloads use temporary local files and size checks before replacement; uploads stage under a temporary remote name and move into place only after completion.
- XBDM and FTP path arguments reject quotes, control characters, invalid roots, and unsafe traversal before an operation begins.
- Configuration, bookmarks, profiles, target history, and other saved JSON state use atomic replacement so interrupted writes do not leave partial files in place.
- SMC and JRPC2 hardware readouts reject invalid version sentinels, incomplete payloads, and temperatures outside the supported range.
- Installer and CLI-driven upgrades use owned-file manifests and recoverable transactions while leaving unrelated files and user-owned state outside the update set.

### Compatibility and Packaging Changes

- The unsupported `rgh spoof` command family is not included in v2.0.0.
- NAND backup moved from `rgh nand dump` to `rgh xell nand dump`.
- `rgh xtaf` is the canonical built-in FATX command; `rgh fatman` and `rgh fatx` remain compatibility aliases.
- The bundled title list from v1.1.0 is no longer shipped. Operators can configure their own optional Title Database.
- v2.0.0 is published for x64 Windows only; there is no v2.0.0 x86 package.
- The installer creates the XeCLI Start menu entry, with optional desktop shortcut and `PATH` registration. Portable mode makes no installation or `PATH` changes and keeps default state under package-local `UserData`.
- Packages include SHA-256 sidecars and release manifests. They are not Authenticode-signed, so Windows may display Unknown Publisher or SmartScreen warnings.
- Same-directory upgrades preserve installer choices. Uninstall retains settings, cache, command logs, captures, configured data directories, and any `XECLI_HOME` directory for manual removal.

## Archived v1.1.0 XeTerminal Beta and Release Polish

This archived release is kept for reference only.

- Promoted `XeTerminal.exe` as the beta custom interface shipped alongside `rgh`, with the live shell, session, status, traffic, storage, inventory, and FTP/file surfaces integrated into the release build.
- Added native Discord Rich Presence updates for connected and disconnected console state, using the installed Discord desktop client instead of a helper process.
- Fixed screenshot output so normal `.png` saves are usable by default, the corrupted right-edge strip is automatically trimmed on the affected frame-buffer path, and XeTerminal names screenshots from the active title.
- Improved XeTerminal UI fit, session rendering, selection behavior, language switching, console/status presentation, storage rendering, and screenshot capture behavior across the then-current beta surface.
- Shipped XeTerminal directly in the installer with Start menu integration, optional desktop shortcut creation, and direct launcher support for installed and portable builds.

## v1.0.9 Critical Debugging Bug Fixes

- Fixed the false-zero small-read path so `rgh mem peek`, `rgh mem hexdump`, and small `rgh mem dump` reads agree on the same live memory instead of silently trusting bad `getmemex` results.
- Added write verification for `rgh mem poke` and freeze loops so XeCLI reads the target bytes back and fails if the write is rejected or the readback does not match.
- Hardened debugger-control commands so `rgh debug stop`, `rgh debug go`, breakpoints, and data breakpoints only print success after XBDM returns an accepted response.
- Corrected the `rgh debug databreak --type rw` mapping so it emits a real read/write data breakpoint instead of silently degrading to read-only.
- Added the beta `XeTerminal.exe` launcher so installed and portable builds can open the custom terminal UI directly, with Start menu registration, optional desktop shortcut support, and no-argument launch behavior.
- Added self-contained portable packages for both `win-x64` and `win-x86`, with SHA-256 files for each portable zip and a universal setup installer retained for the installer workflow.

## v1.0.8 XTAF, Packaging, and Installer Polish

- Promoted `rgh xtaf` to the primary FATX/XTAF command family while keeping `rgh fatman` and `rgh fatx` as compatibility aliases.
- Highlighted the XTAF workflows that matter in practice: physical-disk inspection, FATX/XTAF header scan, manual `--offset` / `--length` opens, metadata backup/restore, safe chain-map repair, and raw partition export.
- Separated the desktop XeCLI release and the standalone `XeCLI-XellFetch` payload release so they ship from their own repos with clearer operator expectations.
- Polished the Windows installer flow with the bundled .NET runtime prerequisite path, corrected welcome-page ordering, installer-owned language selection, and updated release-facing copy.

## v1.0.7 Spanish Localization, Inno Setup, and Wiki

- Added Spanish help, installer, and avatar strings so Spanish operators can navigate the CLI, wrapper, and WinForms browser without a manual translation layer.
- Released a dark-themed installer with the XeCLI logo, brand art, Ko-fi support, optional `PATH` setup, and clean install, upgrade, and uninstall behavior.
- Centralized user documentation in the GitHub wiki for easier navigation and maintenance.

## v1.0.6 Automated NAND Dumping and XeCLI-XellFetch

- Added `rgh xell nand dump` as a one-command PC workflow for managed XeLL launch, NAND download, integrity checks, packaging, and reboot control gated on successful completion.
- Added managed helper/linker staging so the workflow can provision `XellLaunch`, `QuickBoot`, and the custom `xell.bin` payload without requiring the operator to gather them separately.
- Added standalone `XeCLI-XellFetch` repo support so the XeLL payload stack can be used outside the full XeCLI desktop flow when needed.
- Added explicit verification gating so auto-reboot only happens after XeCLI confirms the dump, manifest, and archive are valid.
- Added safe same-session verification fallback when the console ignores XeLL reboot during the second-pass dump.
- Added improved XeLL payload status sync so the console UI and the PC-side command flow report the same NAND dump and completion states.

## v1.0.5 Local Content and Release Polish

- Added native local-content branches with `rgh con`, `rgh profile`, and `rgh xdbf` for pulled Xbox 360 packages and GPD/XDBF files.
- Added package metadata, verification, rehash, resign, FATX magic-name, and FATX-path workflows directly inside `rgh con`.
- Added decoded profile operations for account inspection, raw `Account` extraction, embedded GPD list/extract, achievements, settings, and avatar-color edits.
- Added `rgh profile titles add` so XeCLI can repair or refresh one dashboard title record directly, using embedded title GPD data when it exists.
- Kept profile mutations inside XeCLI, writing back to the local container, refreshing STFS hashes, and preserving a verifiable CON result.
- Added thread list image metadata so `rgh threads list` can show `Image`, `Start Addr.`, and `End Addr.` fallback data when real thread names are not exposed by the console.
- Improved console connection error guidance so failed or incomplete target setup is easier to diagnose from the CLI.
- Hardened `rgh install --source` validation so the installer accepts published self-contained release layouts and rejects framework-dependent build output.
- Fixed the first-run installer prompt so only a bare `rgh` launch offers setup, while normal commands like `rgh status` and `rgh threads list` run directly.

## v1.0.4 IDA and XTAF Expansion

- Added headless IDA Pro `9.1.250226` support with `rgh ida config`, `rgh ida check`, `rgh ida install-loader`, `rgh ida analyze`, `rgh ida decompile`, `rgh ida verify`, and `rgh xex ida-decompile`.
- Clarified the Ghidra path as an external `(Free)` dependency and added helper-loader install flows through `rgh ghidra install-loader`.
- Expanded XeCLI's FATX image and storage workflow, now surfaced as `rgh xtaf` with `rgh fatman` and `rgh fatx` kept as compatibility aliases.
- Added FATX/XTAF discovery, partition inspection, scan, info, list, find, cat, get, extract, dump, mkdir, put, mv, and rm flows.
- Added Windows-first physical disk support, manual-open by `--offset` / `--length`, metadata backup/restore, and safe chain-map `check` / `repair` workflows.

## v1.0.3 Original Xbox Compatibility Update

- Added `rgh ogxbox list` and `rgh ogxbox install <hacked|hud|retail>` so XeCLI can stage or install the three public XeFu compatibility packs with user-facing descriptions instead of leaving that setup manual.
- Added optional HDD Compatibility Partition Fixer staging and direct console transfer support, including `HddX:\Compatibility` targeting for the compatibility files and automatic placement of the fixer on a writable console drive when needed.

## v1.0.2 Installer and Homebrew Update

- Split first-time setup and package staging cleanly so `rgh install` remains the XeCLI installer and `rgh homebrew install <package>` handles Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch staging.
- Added `rgh ogxbox list` and `rgh ogxbox install <hacked|hud|retail>` for Original Xbox compatibility packs, with optional HDD Compatibility Partition Fixer staging and direct `HddX:\Compatibility` install support.
- Added a dedicated homebrew package catalog with cached downloads, archive extraction, generated `launch.ini`, optional user-supplied plugin copies, staging to removable USB drives or normal folders, and an explicit install confirmation prompt unless `--auto-confirm` is supplied.
- Kept silent backward compatibility for older `rgh install aurora` style calls by redirecting them to the new homebrew path without exposing that legacy syntax in public help.
- Fixed installer summaries and follow-up instructions so installs without PATH enabled now point users to the installed `rgh.exe` directly instead of telling them to run a missing `rgh` command.

## v1.0.1 Avatar Update

- Added hosted and local `Avatar-Item-Collection` support with `rgh avatar games`, `rgh avatar items`, `rgh avatar choose`, `rgh avatar browse`, `rgh avatar install`, and `rgh avatar apply`.
- Added terminal and Windows picker flows for avatar item selection, with current-user ownership patching, cached downloads, and multi-item progress bars.
- Added hardware and session controls including sign-in state, LED presets, fan commands, tray control, shutdown, and native popup messages.
- Added a dedicated `rgh homebrew install` workflow for staging Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch onto USB drives or staging folders, with archive caching, progress bars, generated `launch.ini`, optional user-supplied plugin copies, and confirm-before-install behavior.
- Reworked `rgh install` into a local installer flow with install-path selection, PATH registration, clearer first-time setup, and explicit post-install discovery through `rgh start`.
- Fixed current-user command registration so PATH-based installs resolve directly to `rgh.exe` instead of relying on a fragile wrapper-first path.
- Clarified that console-side plugin binaries are user-supplied and are copied only when a local `ConsoleDependencies` folder is present.

## v1.0.0 Initial Release

- Shipped the first public XeCLI release with the `rgh` command, XBDM discovery, console status, module inspection, memory inspection, thread control, and debug helpers.
- Added JRPC2 helpers for Title ID, temperatures, notifications, CPU key, motherboard, and dashboard queries, plus FTP-backed file, save, content, and plugin workflows.
- Added XEX dumping, string extraction, Ghidra headless integration, ISO to GOD conversion, and local Title ID lookup support.
