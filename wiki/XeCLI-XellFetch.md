# XeCLI-XellFetch

This page documents the shipped `XeCLI-XellFetch` workflow layer for managed XeLL bootstrap, endpoint detection, keyvault export, and read-only NAND backup handling.

XeCLI treats Xell, NAND, and KV as integrated scope in the main product line, with a defined PC-side workflow that launches or re-attaches to XeLL, inspects the active endpoint surface, exports keys, compares dump outputs, and reboots under operator control after the workflow completes. The shipped command surface below is the supported user-facing entry point.

## What XeCLI-XellFetch Covers

`XeCLI-XellFetch` is the operator-facing name for the XeLL payload and workflow model used by these shipped commands:

- `rgh xell boot`
- `rgh xell info`
- `rgh xell kv export`
- `rgh xell nand dump`

There is no separate root-level nand dump command in the shipped tree; NAND dump lives under `rgh xell nand dump`.

That surface covers:

- guided transition from dashboard to XeLL
- XeLL HTTP endpoint inspection and compatibility detection
- packaged keyvault export with CPU key capture
- read-only NAND backup
- managed reboot control after success
- optional use of the `XeCLI-XellFetch` payload bundle as the supporting asset set for the integrated XeCLI workflow

## Command Surface

```powershell
rgh xell boot
rgh xell info
rgh xell kv export
rgh xell kv export --raw
rgh xell nand dump
rgh xell nand dump --single
```

## Guided XeLL Launch

XeCLI does not assume the console is already in XeLL.

- It first detects whether the console is on the dashboard or already in XeLL.
- If the console is already in XeLL, the command attaches to the detected XeLL IP and continues.
- If the console is on the dashboard, XeCLI asks for confirmation before the first automatic transition into XeLL.
- When available, XeCLI prefers the XellLaunch shortcut path. `--force-xell` skips that preference and forces the direct reboot path.
- If the session is non-interactive, or if automatic launch is not available, XeCLI prints the manual eject-button fallback instead of rebooting without confirmation.
- The `XeCLI-XellFetch` repo remains the supporting payload bundle for the integrated XeCLI path, while the command surface itself lives in XeCLI and is presented here as the shipped operator workflow.

## Payload and Staging Model

The managed workflow can stage and use the helper/linker assets needed by the automated dump path instead of requiring the operator to assemble them manually before starting.

In v2.0.0, the shipped workflow keeps one operator model:

- the full XeCLI desktop package, where `rgh xell boot`, `rgh xell info`, `rgh xell kv export`, and `rgh xell nand dump` manage the XeLL-side handoff inside the main CLI

The point of the payload layer is consistency. XeCLI and the active XeLL-side components report the same state transitions for dump start, comparison, and completion instead of leaving that coordination to manual timing.

All XeLL-integrated commands wait up to 30 seconds for the XeLL HTTP server on port 80. XeCLI probes the last dashboard IP first. When subnet scanning is enabled, it then probes that IPv4 subnet and the well-known XeLL fallback addresses `192.168.1.99` and `192.168.88.99`.

> [!WARNING]
> XeLL HTTP is unencrypted and unauthenticated. Use these workflows only on a trusted private LAN, never port-forward the console or expose it to the internet, and disconnect untrusted devices while exporting data. NAND images, CPU keys, fuse data, and keyvault files are sensitive console secrets; store them securely and remove unnecessary copies.

## Endpoint Compatibility

XeCLI supports both older and current XeLL Reloaded HTTP layouts.

- Flash dump: `/rawflash` and `/FLASH`
- CPU key and fuse text: `/cpukey.txt` and `/FUSE`
- Keyvault: `/KV`
- Raw keyvault: `/KVRAW` and `/KVRAW2`
- Startup log: `/log` and `/LOG`
- Reboot: `/reboot` and `/REBOOT`

Use `rgh xell info` when you want to see which of those endpoints the active XeLL build exposes.

Reboot endpoints act immediately. Do not call them manually while an export or comparison is running.

## Keyvault Export

`rgh xell kv export` downloads the XeLL keyvault output, performs an output comparison by default, and refuses to finish unless a CPU key is also available.

```powershell
rgh xell kv export
rgh xell kv export --output .\kv_backup.bin
rgh xell kv export --raw
```

Default mode exports the decrypted keyvault from `/KV`. `--raw` switches to the raw keyvault block.

By default, XeCLI performs a second same-session keyvault read and compares it byte-for-byte before it reports success or allows the payload to leave XeLL. `--single` and `--no-verify` skip that comparison step and disable auto-reboot.

Host-side output includes:

- your selected KV filename
- `KV+CPU KEY.txt`
- a sibling `*.CPUKEY.txt`
- optional `*.fuses.txt`
- `*.sha256.txt`
- a `.zip` archive

Inside the archive, XeCLI uses stable names:

- `KV.bin` or `KV_RAW.bin`
- `CPUKEY.txt`
- `KV+CPU KEY.txt`
- `fuses.txt` when available
- `manifest.sha256.txt`

The final zip is checked by reopening it and comparing every entry against the source files byte-for-byte.

## NAND Dumping

`rgh xell nand dump` is the read-only backup path. It does not write anything to NAND and is the PC-side command that drives the automated NAND workflow.

```powershell
rgh xell nand dump
rgh xell nand dump --output .\nand_backup.bin
rgh xell nand dump --single
rgh xell nand dump --force-xell
```

Default comparison flow:

1. Detect the current mode and prompt before the first automatic XeLL launch.
2. Wait for XeLL HTTP and download the reference dump.
3. Attempt a second comparison dump.
4. On stock helper paths, XeCLI requests a XeLL reboot and waits for XeLL to come back before downloading the comparison dump.
5. On the managed custom payload path, XeCLI can keep the console inside the same XeLL session for the comparison dump when dashboard round-trips would otherwise introduce NAND drift.
6. Compare the comparison dump byte-for-byte against the reference dump.
7. If the comparison dump differs, retry the second dump up to three more times.
8. If a match is found, copy the matched dump to the final output path and compare that copy again.
9. Write a SHA-256 manifest, build a zip archive, and verify the archive contents byte-for-byte.

The workflow stages the helper/linker assets that the automated path expects, so the operator does not need to assemble the PC-side tooling manually before starting the dump.

Successful output can include:

- the NAND image produced by the workflow
- `*.cpukey.txt` when XeLL exposed the CPU key
- `*.startup-log.txt` when XeLL exposed the startup log
- `*.sha256.txt`
- a `.zip`

When the comparison path completes, XeCLI prints a completion message and the operator should review the resulting files before any flash workflow.

`--single` and `--no-verify` skip the repeated second-dump loop, but XeCLI still validates the copied output, manifest, and zip archive. Auto-reboot stays disabled in those modes so the console does not leave XeLL before the operator can review the output.

## Safety Boundaries

`XeCLI-XellFetch` is intentionally a read-only workflow surface.

It does not claim to be:

- a NAND flashing path
- a XeBuild replacement
- a dashboard patching flow
- a glitch-chip programming tool

The shipped path is for inspection, export, verification, and packaging.

## Failure Behavior

XeCLI fails closed for backup work.

- If no XeLL HTTP server appears within 30 seconds, the command stops and tells you to check Ethernet and boot XeLL manually.
- If the keyvault export does not include a CPU key, XeCLI aborts rather than packaging an incomplete set.
- If NAND comparison never produces matching dumps, XeCLI stops with a hard failure and tells you not to flash the files.

## Recommended Sequence

```powershell
rgh xell boot
rgh xell info
rgh xell kv export --output .\kv_backup.bin
rgh xell nand dump --output .\nand_backup.bin
```

This gives you an inspected XeLL session, a packaged KV plus CPU key set, and a NAND backup set through the same `XeCLI-XellFetch` workflow.
