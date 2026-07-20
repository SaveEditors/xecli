# XeCLI Privacy Policy

Last updated: July 20, 2026

This Privacy Policy explains how XeCLI handles information when you use the XeCLI desktop application, the XeTerminal interface, the `rgh` command-line tools, and related public releases published by SaveEditors.

XeCLI is primarily a local desktop tool. Core features do not require you to create a XeCLI account or use a mandatory hosted XeCLI service.

## 1. Information XeCLI Processes

Depending on the features you use, XeCLI may process:

- target console IP addresses, ports, and connection settings
- FTP usernames and passwords when you provide or save them
- local file paths you choose for uploads, downloads, dumps, and exports
- console metadata such as title name, title ID, loaded XEX name, motherboard, dashboard, temperatures, drive inventory, modules, or plugin state
- transfer logs, command output, and operator-selected settings
- optional integration metadata needed for features such as Discord Rich Presence

## 2. Local Storage

XeCLI stores configuration and local runtime state on your system so the application can preserve preferences and operate correctly.

Common local storage locations include:

- `%APPDATA%\XeCLI\config.json`
- `%APPDATA%\XeCLI\target-profiles.json` when named target profiles are saved
- `%LOCALAPPDATA%\XeCLI\cache`
- `%LOCALAPPDATA%\XeCLI\cache\logging\command-log.jsonl` for the default installed command log
- `%LOCALAPPDATA%\XeCLI\cache\recent-console-history.json` for recently contacted endpoints
- the Windows Pictures folder at `XeCLI\Captures` for default installed XeTerminal captures
- package-local `UserData` for portable configuration, cache, command logs, and default captures
- the directory named by `XECLI_HOME` when that override is set

Depending on how you use the product, XeCLI may also create local output files such as:

- screenshots
- logs
- diagnostics bundles
- downloaded console files
- exported saves, profiles, key material, or dumps
- temporary transfer files

Those files remain under your control on your own system unless you choose to move or share them.

Saved FTP passwords in `config.json` and `target-profiles.json` are readable text and are not stored in Windows Credential Manager. Portable mode keeps the corresponding files under `UserData`; `XECLI_HOME` places them under the directory it names. Protect the active storage directory with your operating-system account and do not place it in a shared or publicly synchronized location.

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

That status may include:

- whether XeCLI is connected to a console
- the console motherboard family
- the dashboard version
- the current title or title name

Discord processes that data under Discord's own terms and privacy practices. SaveEditors does not control Discord's handling of information once it is transmitted to Discord.

- Discord Privacy Policy: https://discord.com/privacy
- Discord Terms of Service: https://discord.com/terms

## 5. GitHub and External Downloads

If you use features that access GitHub releases, metadata feeds, or other external content sources, your system may connect directly to those third-party services to retrieve requested content or documentation.

Those services may receive standard network information such as your IP address, client details, and request metadata as part of the normal web request process.

## 6. Data Sharing

SaveEditors does not collect or sell XeCLI user data.

XeCLI is designed to operate locally. Information is only transmitted to third parties when required by the feature you choose to use, such as:

- Discord Rich Presence
- GitHub-hosted release or metadata downloads
- other operator-initiated integrations

Diagnostics bundles are local output files. XeCLI redacts local paths, IP addresses, console IDs, and known secrets in the bundle, but you should review the files before sharing them because command history can still show workflow context.

XeCLI redacts secret command options such as `--pass` from its own command log. Your terminal or shell may independently retain the original command line, and exported logs remain wherever you saved them.

## 7. Data Retention

The uninstaller retains settings under `%APPDATA%\XeCLI`, cache and the default command log under `%LOCALAPPDATA%\XeCLI`, and default XeTerminal captures under the Windows Pictures folder at `XeCLI\Captures`. Configured capture/log directories and any `XECLI_HOME` directory are not removed.

Files saved to locations you selected outside those default folders are also retained. For a portable release, application state remains under `UserData` in the extracted folder until you delete it or the portable folder yourself.

Run `rgh ftp target --clear` to remove the saved default FTP port, username, and password. Credentials saved in a named profile remain until that profile is removed with `rgh target profile remove <name>`. These commands do not clear terminal history, command-log exports, diagnostics bundles, or other copies; manage those files and your shell history separately.

Retention of third-party data handled by services such as Discord or GitHub is governed by those services, not by SaveEditors.

## 8. Security

XeCLI is a technical operator tool and should be used on systems you trust and manage. You are responsible for:

- protecting your local machine and user profile
- protecting exported dumps, keys, saves, and downloaded files
- protecting configuration files that contain saved FTP credentials
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

Do not include passwords, keys, dumps, unredacted diagnostics, or other sensitive data in a public issue. Remove secrets and review attachments before posting.
