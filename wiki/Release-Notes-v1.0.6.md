# XeCLI v1.0.6 Release Notes

XeCLI v1.0.6 makes XeLL-backed NAND dumping a first-class automated workflow instead of a manual multi-step handoff.

## Automated NAND Dumping

`rgh nand dump` is now the PC-side entry point for the full backup path. It launches or re-attaches to XeLL, stages the helper/linker assets the workflow needs, downloads the reference dump, performs the verification pass, packages the output, and reboots the console after the backup is verified.

That means the operator does not need to break the flow into separate console-side steps unless they want to.

## Managed Staging

The release package carries the helper/linker assets that the automated dump path expects, so the user does not have to gather them separately before starting the backup.

If you prefer the minimal boot package, the standalone `Xell-NoN` companion package can be used as the stripped XeLL-side bootstrap for the same automated NAND flow.

## Verification and Reboot

The verification path remains read-only.

- XeCLI still takes a reference dump first.
- On the stock helper path, XeCLI requests a XeLL reboot and compares the next dump byte-for-byte.
- On the managed XeCLI custom payload path, XeCLI can keep the console inside the same XeLL session for the verification pass when the console would otherwise bounce through the dashboard and introduce drift.
- If the verification dump does not match, XeCLI retries before failing hard.
- If the backup verifies, XeCLI writes the manifest, packages the archive, and only then sends the final reboot token that schedules the return to the dashboard.

`--single` and `--no-verify` skip the repeated-dump loop, but XeCLI still validates the copied output, manifest, and archive. Auto-reboot stays disabled in those modes.

## Safety Notes

- Do not power off or disconnect the console during the dump.
- Keep the PC connected to the console until the workflow finishes.
- Treat any failed verification as a stop condition, not as a backup you should flash back.

## Related Docs

- [XeLL and NAND Backups](XeLL-and-NAND-Backups.md)
- [Commands Reference](Commands.md)
- [Latest Features](Latest-Features.md)
