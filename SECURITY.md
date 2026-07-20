# Security Policy

## Supported Versions

| Release line | Security updates |
| --- | --- |
| `2.0.x` | Supported |
| `1.x` and earlier | Not supported |

Security fixes are made against the current stable `2.0.x` line. Older releases are retained for historical reference, but users should reproduce a report on the latest stable release before submitting it. Backports are not guaranteed.

## Report a Vulnerability Privately

Use GitHub's private [Report a vulnerability](https://github.com/SaveEditors/xecli/security/advisories/new) form. This keeps the report and any follow-up discussion private until a fix and disclosure plan are ready. Do not disclose vulnerability details, exploit instructions, sensitive output, or attachments in a public issue.

Include the following in a private report when relevant:

- The affected XeCLI version and whether it came from the installer, portable ZIP, or a source build
- The affected command or XeTerminal workflow and the security impact
- Sanitized reproduction steps and the expected and observed behavior
- Relevant error text, package checksum, or a minimal sanitized file that does not contain user or console data

Never submit keyvaults, CPU keys, credentials, access tokens, signing material, private profile/save data, NAND images, or unreviewed memory dumps and support bundles. Review every attachment before sharing it.

## Scope

Security reports may include unsafe file handling, unintended disclosure, command or archive injection, path traversal, integrity-check bypasses, or a vulnerability in a bundled XeCLI component. General usage questions and non-security defects belong in the normal [support process](SUPPORT.md).

XeCLI v2.0.0 packages are intentionally unsigned. An **Unknown Publisher** or SmartScreen warning by itself is expected behavior, not a security vulnerability. Always download from the official Releases page and verify the published SHA-256 checksum.

Please allow maintainers time to confirm the report before publishing details. Coordinated disclosure helps protect users while a correction is prepared.
