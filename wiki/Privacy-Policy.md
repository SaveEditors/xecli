# XeCLI Privacy Policy

Last updated: March 30, 2026

This Privacy Policy explains how XeCLI handles information when you use the XeCLI desktop application, the XeTerminal interface, the `rgh` command-line tools, and related public releases published by SaveEditors.

XeCLI is primarily a local desktop tool. Core features do not require you to create a XeCLI account or use a mandatory hosted XeCLI service.

## 1. Information XeCLI Processes

Depending on the features you use, XeCLI may process:

- target console IP addresses, ports, and connection settings
- local file paths you choose for uploads, downloads, dumps, and exports
- console metadata such as title name, title ID, loaded XEX name, motherboard, dashboard, temperatures, drive inventory, modules, or plugin state
- transfer logs, command output, and operator-selected settings
- optional integration metadata needed for features such as Discord Rich Presence

## 2. Local Storage

XeCLI stores configuration and local runtime state on your system so the application can preserve preferences and operate correctly.

Common local storage locations include:

- `%APPDATA%\XeCLI\config.json`
- `%LOCALAPPDATA%\XeCLI\cache`

Depending on how you use the product, XeCLI may also create local output files such as:

- screenshots
- logs
- downloaded console files
- exported saves, profiles, key material, or dumps
- temporary transfer artifacts

Those files remain under your control on your own system unless you choose to move or share them.

## 3. Console and Network Communication

When you connect XeCLI to a console, XeCLI communicates directly with that console or related local-network services using the protocols required for the selected feature, such as XBDM, FTP, or XeLL HTTP endpoints.

That communication may include:

- connection and authentication attempts supported by the target environment
- file listings, file transfers, and directory metadata
- status, title, module, temperature, and storage queries
- command execution requests initiated by the operator

XeCLI does not require a SaveEditors-operated cloud relay for normal console operations.

## 4. Discord Rich Presence

If you enable Discord Rich Presence, XeCLI may send limited status information to the local Discord desktop client through Discord's native local IPC interface.

Discord Rich Presence is optional and is not active unless it is configured and enabled by the operator.

Depending on configuration, that status may include:

- whether XeCLI is connected to a console
- the current title or title name
- the target address or a generalized session state
- other runtime presence text configured by the application

Discord processes that data under Discord's own terms and privacy practices. SaveEditors does not control Discord's handling of information once it is transmitted to Discord.

- Discord Privacy Policy: https://discord.com/privacy
- Discord Terms of Service: https://discord.com/terms

## 5. GitHub and External Downloads

If you use features that access GitHub releases, metadata feeds, or other external content sources, your system may connect directly to those third-party services to retrieve requested content or documentation.

Those services may receive standard network information such as your IP address, user agent, and request metadata as part of the normal web request process.

## 6. Data Sharing

SaveEditors does not sell XeCLI user data.

XeCLI is designed to operate locally. Information is only transmitted to third parties when required by the feature you choose to use, such as:

- Discord Rich Presence
- GitHub-hosted release or metadata downloads
- other operator-initiated integrations

## 7. Data Retention

XeCLI retains local settings, cached content, and output files on your device until you remove them, replace them, or uninstall the application.

Retention of third-party data handled by services such as Discord or GitHub is governed by those services, not by SaveEditors.

## 8. Security

XeCLI is a technical operator tool and should be used on systems you trust and manage. You are responsible for:

- protecting your local machine and user profile
- protecting exported dumps, keys, saves, and downloaded files
- reviewing commands before execution
- securing any target systems and local network environment you choose to connect

No software can guarantee complete security in every environment.

## 9. Children's Privacy

XeCLI is not directed to children under 13, and SaveEditors does not knowingly collect personal information from children through XeCLI.

## 10. Changes to This Policy

This Privacy Policy may be updated from time to time. The published version on the official XeCLI documentation surface is the current version.

Continued use of XeCLI after an update means you accept the revised policy.

## 11. Contact

Project and privacy contact:

- Repository: https://github.com/SaveEditors/xecli
- Issues: https://github.com/SaveEditors/xecli/issues
