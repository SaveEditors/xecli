# XeCLI-XellFetch

This page documents the `XeCLI-XellFetch` workflow layer: XeCLI's managed XeLL bootstrap, endpoint detection, keyvault export path, and verified read-only NAND dump flow.

XeCLI does not treat XeLL as a loose external handoff. It uses a defined PC-side workflow that can launch or re-attach to XeLL, inspect the active endpoint surface, export keys, drive repeated dump verification, and only reboot automatically after the safety checks pass.

## What XeCLI-XellFetch Covers

`XeCLI-XellFetch` is the operator-facing name for the XeLL payload and workflow model used by:

- `rgh xell boot`
- `rgh xell info`
- `rgh xell kv export`
- `rgh nand dump`

That surface covers:

- guided transition from dashboard to XeLL
- XeLL HTTP endpoint inspection and compatibility detection
- packaged keyvault export with CPU key capture
- verified read-only NAND backup
- managed reboot control after success
- optional use of the standalone `XeCLI-XellFetch` payload bundle outside the full desktop package

## Command Surface

```powershell
rgh xell boot
rgh xell info
rgh xell kv export
rgh xell kv export --raw
rgh nand dump
rgh nand dump --single
```

## Guided XeLL Launch

XeCLI does not assume the console is already in XeLL.

- It first detects whether the console is on the dashboard or already in XeLL.
- If the console is already in XeLL, the command attaches to the detected XeLL IP and continues.
- If the console is on the dashboard, XeCLI asks for confirmation before the first automatic transition into XeLL.
- When available, XeCLI prefers the XellLaunch shortcut path. `--force-xell` skips that preference and forces the direct reboot path.
- If the session is non-interactive, or if automatic launch is not available, XeCLI prints the manual eject-button fallback instead of rebooting without confirmation.
- The standalone `XeCLI-XellFetch` repo can be used when you want the same custom XeLL-side bootstrap without the broader desktop bundle.

## Payload and Staging Model

The managed workflow can stage and use the helper/linker assets needed by the automated dump path instead of requiring the operator to assemble them manually before starting.

In practical terms, the release line now supports two operator models:

- the full XeCLI desktop package, where `rgh nand dump` manages the XeLL-side handoff itself
- the standalone `XeCLI-XellFetch` package, where you want the payload stack available separately for the same XeCLI-managed XeLL tasks

The point of the payload layer is consistency. XeCLI and the active XeLL-side components report the same state transitions for dump start, verification, and completion instead of leaving that coordination to manual timing.

All XeLL-backed commands wait up to 30 seconds for the XeLL HTTP server on port 80. If the dashboard IP changes after the reboot, XeCLI also scans the local subnet and common XeLL fallback addresses such as `192.168.88.99`.

## Endpoint Compatibility

XeCLI supports both older and current XeLL Reloaded HTTP layouts.

- Flash dump: `/rawflash` and `/FLASH`
- CPU key and fuse text: `/cpukey.txt` and `/FUSE`
- Keyvault: `/KV`
- Raw keyvault: `/KVRAW` and `/KVRAW2`
- Startup log: `/log` and `/LOG`
- Reboot: `/reboot` and `/REBOOT`

Use `rgh xell info` when you want to see which of those endpoints the active XeLL build exposes.

## Keyvault Export

`rgh xell kv export` downloads the XeLL keyvault output, verifies repeated reads by default, and refuses to finish unless a CPU key is also available.

```powershell
rgh xell kv export
rgh xell kv export --output .\kv_backup.bin
rgh xell kv export --raw
```

Default mode exports the decrypted keyvault from `/KV`. `--raw` switches to the raw keyvault block.

By default, XeCLI performs a second same-session keyvault read and compares it byte-for-byte before it reports success or allows the payload to leave XeLL. `--single` and `--no-verify` skip that verification loop and intentionally disable auto-reboot.

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

The final zip is verified by reopening it and comparing every entry against the source files byte-for-byte.

## Verified NAND Dumping

`rgh nand dump` is the high-safety backup path. It does not write anything to NAND and, in v1.0.6, is the single PC-side command that drives the automated NAND workflow.

```powershell
rgh nand dump
rgh nand dump --output .\nand_backup.bin
rgh nand dump --single
rgh nand dump --force-xell
```

Default verification flow:

1. Detect the current mode and prompt before the first automatic XeLL launch.
2. Wait for XeLL HTTP and download the reference dump.
3. Attempt a second verification dump.
4. On stock helper paths, XeCLI requests a XeLL reboot and waits for XeLL to come back before downloading the verification dump.
5. On the managed custom payload path, XeCLI can keep the console inside the same XeLL session for the verification dump when dashboard round-trips would otherwise introduce NAND drift.
6. Compare the verification dump byte-for-byte against the reference dump.
7. If the verification dump differs, retry the second dump up to three more times.
8. If a match is found, copy the verified dump to the final output path and compare that copy again.
9. Write a SHA-256 manifest, build a zip archive, and verify the archive contents byte-for-byte.

The release now stages the helper/linker assets that the automated path expects, so the operator does not need to assemble the PC-side tooling manually before starting the dump.

Successful output can include:

- the verified NAND image
- `*.cpukey.txt` when XeLL exposed the CPU key
- `*.startup-log.txt` when XeLL exposed the startup log
- `*.sha256.txt`
- a verified `.zip`

When the verified path completes, XeCLI prints `NAND verified — safe to flash`.

`--single` and `--no-verify` skip the repeated second-dump loop, but XeCLI still validates the copied output, manifest, and zip archive. Auto-reboot stays disabled in those modes so the console does not leave XeLL before the operator can see that verification was skipped.

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
- If NAND verification never produces matching dumps, XeCLI stops with a hard failure and tells you not to flash the files.

## Recommended Sequence

```powershell
rgh xell info
rgh xell kv export --output .\kv_backup.bin
rgh nand dump --output .\nand_backup.bin
```

This gives you an inspected XeLL session, a packaged KV plus CPU key set, and a verified NAND backup set through the same `XeCLI-XellFetch` workflow.
