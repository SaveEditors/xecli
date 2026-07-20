# XeCLI Support

## Start with the Documentation

The [XeCLI Wiki](https://github.com/SaveEditors/xecli/wiki) covers installation, console setup, commands, FTP prerequisites, integrations, and recovery steps. For common failures, start with [Troubleshooting](https://github.com/SaveEditors/xecli/wiki/Troubleshooting).

Useful local checks:

```powershell
rgh --version
rgh health
rgh ftp check
```

`rgh health` checks local configuration without contacting a console. Use `rgh ftp check` only when the issue involves FTP access.

## Open an Issue

Use [GitHub Issues](https://github.com/SaveEditors/xecli/issues) for reproducible defects, documentation corrections, and focused feature requests. Before opening an issue, search existing reports and retry with the current public release.

For a defect, include:

- XeCLI version and Installer or Portable ZIP
- Windows version and the relevant console plugin, dashboard, or external-tool version
- The exact command or XeTerminal action
- Expected behavior, observed behavior, and sanitized error output
- The smallest reliable sequence that reproduces the problem

For additional context, create a local bundle with `rgh support bundle`. XeCLI redacts known sensitive fields, but you must still review the archive before attaching it because recent command context may be relevant.

## Protect Private Data

Do not post passwords, access tokens, keyvaults, CPU keys, NAND images, console identifiers, gamertags, account/profile data, save files, memory dumps, or private workstation paths in public issues. Replace IP addresses and other identifiers with neutral placeholders where possible.

Security concerns require a private report. Follow [SECURITY.md](SECURITY.md) instead of opening a public issue with technical details.

Support covers XeCLI and its documented integration behavior. Questions specific to a third-party dashboard, console plugin, or external analysis tool may also require that project's documentation or support channel.

## Support Development

XeCLI is open source and support is community-maintained. If the project is useful to you, you can support continued development on [Ko-fi](https://ko-fi.com/xecli).
