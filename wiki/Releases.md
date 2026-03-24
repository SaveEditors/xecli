# Releases

This page keeps the recent public XeCLI release history concise and links back to the published GitHub releases. The README points here instead of carrying a long embedded changelog.

## Latest Two Releases

| Release | Focus | Links |
| --- | --- | --- |
| `v1.0.5` | Local CON/profile/XDBF workflows, native profile editing, and pulled-container repair | [Release notes](#v105-local-content-and-profile-workflows) · [GitHub release](https://github.com/SaveEditors/xecli/releases/tag/v1.0.5) |
| `v1.0.4` | IDA debugger/decompiler support and the expanded Fatman/FATX workflow | [Release notes](#v104-ida-and-fatman-expansion) · [GitHub release](https://github.com/SaveEditors/xecli/releases/tag/v1.0.4) |

Older releases: [All releases](https://github.com/SaveEditors/xecli/releases)

## v1.0.5 Local Content and Profile Workflows

- Added native local-content branches with `rgh con`, `rgh profile`, and `rgh xdbf` for pulled Xbox 360 packages and GPD/XDBF files.
- Added package metadata, verification, rehash, resign, FATX magic-name, and FATX-path workflows directly inside `rgh con`.
- Added decoded profile operations for account inspection, raw `Account` extraction, embedded GPD list/extract, achievements, settings, and avatar-color edits.
- Added `rgh profile titles add` so XeCLI can repair or refresh one dashboard title record directly, using embedded title GPD data when it exists.
- Kept profile mutations inside XeCLI, writing back to the local container, refreshing STFS hashes, and preserving a verifiable CON result.
- Carried the detailed workflow docs into the wiki instead of bloating the README.

## v1.0.4 IDA and Fatman Expansion

- Added headless IDA Pro `9.1.250226` support with `rgh ida config`, `rgh ida check`, `rgh ida install-loader`, `rgh ida analyze`, `rgh ida decompile`, `rgh ida verify`, and `rgh xex ida-decompile`.
- Clarified the Ghidra path as an external `(Free)` dependency and added helper-loader install flows through `rgh ghidra install-loader`.
- Expanded Fatman into XeCLI's FATX image and storage workflow, with `rgh fatman` as the primary name and `rgh fatx` retained as the alias.
- Added FATX/XTAF discovery, partition inspection, scan, info, list, find, cat, get, extract, dump, mkdir, put, mv, and rm flows.
- Added Windows-first physical disk support, manual-open by `--offset` / `--length`, metadata backup/restore, and safe chain-map `check` / `repair` workflows.
- Added dedicated reverse-engineering and Fatman documentation across the wiki and published docs site.
