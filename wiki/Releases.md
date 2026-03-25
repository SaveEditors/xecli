# Releases

This page is the full public XeCLI patch-notes history. The README only summarizes the latest two releases and links here for the complete release archive.

## Release Index

| Release | Focus | Links |
| --- | --- | --- |
| `v1.0.6` | Automated NAND dumping, managed XeLL/Xell-NoN staging, verified packaging, and safe reboot control | [Release notes](#v106-automated-nand-dumping-and-xell-non) |
| `v1.0.5` | Local content workflows, thread metadata fallback, installer fixes, and release packaging | [Release notes](#v105-local-content-and-release-polish) |
| `v1.0.4` | IDA debugger/decompiler support and the expanded Fatman/FATX workflow | [Release notes](#v104-ida-and-fatman-expansion) |
| `v1.0.3` | Original Xbox compatibility staging and install workflows | [Release notes](#v103-original-xbox-compatibility-update) |
| `v1.0.2` | Installer split and homebrew package staging | [Release notes](#v102-installer-and-homebrew-update) |
| `v1.0.1` | Avatar library/install workflows and hardware controls | [Release notes](#v101-avatar-update) |
| `v1.0.0` | Initial public XeCLI release | [Release notes](#v100-initial-release) |

Published GitHub releases: [All releases](https://github.com/SaveEditors/xecli/releases)

## v1.0.6 Automated NAND Dumping and Xell-NoN

- Added `rgh nand dump` as a one-command PC workflow for managed XeLL launch, NAND download, verification, packaging, and verified-success reboot control.
- Added managed helper/linker staging so the workflow can provision `XellLaunch`, `QuickBoot`, and the custom `xell.bin` payload without requiring the operator to gather them separately.
- Added the standalone `Xell-NoN` companion package so the XeLL payload stack can be used outside the full XeCLI desktop flow when needed.
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
- Added release publish, verification, packaging, and zip smoke-test scripts so the `win-x64` archive is validated as a fresh install before release.

## v1.0.4 IDA and Fatman Expansion

- Added headless IDA Pro `9.1.250226` support with `rgh ida config`, `rgh ida check`, `rgh ida install-loader`, `rgh ida analyze`, `rgh ida decompile`, `rgh ida verify`, and `rgh xex ida-decompile`.
- Clarified the Ghidra path as an external `(Free)` dependency and added helper-loader install flows through `rgh ghidra install-loader`.
- Expanded Fatman into XeCLI's FATX image and storage workflow, with `rgh fatman` as the primary name and `rgh fatx` retained as the alias.
- Added FATX/XTAF discovery, partition inspection, scan, info, list, find, cat, get, extract, dump, mkdir, put, mv, and rm flows.
- Added Windows-first physical disk support, manual-open by `--offset` / `--length`, metadata backup/restore, and safe chain-map `check` / `repair` workflows.

## v1.0.3 Original Xbox Compatibility Update

- Added `rgh ogxbox list` and `rgh ogxbox install <hacked|hud|retail>` so XeCLI can stage or install the three public XeFu compatibility packs with user-facing descriptions instead of leaving that setup manual.
- Added optional HDD Compatibility Partition Fixer staging and direct console deployment support, including `HddX:\Compatibility` targeting for the compatibility files and automatic placement of the fixer on a writable console drive when needed.

## v1.0.2 Installer and Homebrew Update

- Split first-time setup and package staging cleanly so `rgh install` remains the XeCLI installer and `rgh homebrew install <package>` handles Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch staging.
- Added `rgh ogxbox list` and `rgh ogxbox install <hacked|hud|retail>` for Original Xbox compatibility packs, with optional HDD Compatibility Partition Fixer staging and direct `HddX:\Compatibility` install support.
- Added a dedicated homebrew package catalog with cached downloads, archive extraction, generated `launch.ini`, bundled plugin copies, staging to removable USB drives or normal folders, and an explicit install confirmation prompt unless `--auto-confirm` is supplied.
- Kept silent backward compatibility for older `rgh install aurora` style calls by redirecting them to the new homebrew path without exposing that legacy syntax in public help.
- Fixed installer summaries and follow-up instructions so installs without PATH enabled now point users to the installed `rgh.exe` directly instead of telling them to run a missing `rgh` command.

## v1.0.1 Avatar Update

- Added hosted and local `Avatar-Item-Collection` support with `rgh avatar games`, `rgh avatar items`, `rgh avatar choose`, `rgh avatar browse`, `rgh avatar install`, and `rgh avatar apply`.
- Added terminal and Windows picker flows for avatar item selection, with current-user ownership patching, cached downloads, and multi-item progress bars.
- Added hardware and session controls including sign-in state, LED presets, fan commands, tray control, shutdown, native popup messages, and title-aware spoof helpers.
- Added a dedicated `rgh homebrew install` workflow for staging Aurora, DashLaunch, XeXMenu, Freestyle Dash, XM360, TimeFixer, Simple 360 NAND Flasher, and XellLaunch onto USB drives or staging folders, with archive caching, progress bars, generated `launch.ini`, bundled plugin copies, and confirm-before-install behavior.
- Reworked `rgh install` into a real installer flow with install-path selection, PATH registration, clearer first-time setup, and post-install console discovery prompts.
- Fixed current-user command registration so PATH-based installs resolve directly to `rgh.exe` instead of relying on a fragile wrapper-first path.
- Added direct repo-root copies of `xbdm.xex`, `XDRPC.xex`, and `JRPC2.xex`, and linked them from the Beginner Guide for separate download/use.

## v1.0.0 Initial Release

- Shipped the first public XeCLI release with the `rgh` command, XBDM discovery, console status, module inspection, memory inspection, thread control, and debug helpers.
- Added JRPC2 helpers for Title ID, temperatures, notifications, CPU key, motherboard, and dashboard queries, plus FTP-backed file, save, content, and plugin workflows.
- Added XEX dumping, string extraction, Ghidra headless integration, ISO to GOD conversion, and the bundled Title ID database in the release package.
