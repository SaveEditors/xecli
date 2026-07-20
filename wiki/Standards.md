# Documentation Quality Guidelines

This is a documentation-maintainer reference. End users should begin with the [Wiki Home](Home), [Beginner Guide](Beginner-Guide), or [Commands Reference](Commands). Contributors should use these guidelines together with [Contributing](Contributing).

## Goals

Documentation should be:

- specific
- copy-paste friendly
- honest about prerequisites and limits
- organized by user task and subsystem
- consistent with the released `rgh` command surface
- careful with privacy-sensitive console and machine details

## Page Structure

### Home page

`Home.md` should act as a user index.

It should contain:

- grouped page navigation
- reading paths for new users and reverse engineers
- a capability summary
- clear scope boundaries

### Command pages

Command documentation should include:

- command name
- what it does
- important options
- realistic examples
- warnings when a command can destabilize the console or modify state

### Help-output page

`CLI-Help.md` should match the help text shipped with the current public binary.

Regenerate it from a fresh Release build; do not edit the generated page by hand:

```powershell
pwsh ./tools/update-cli-help.ps1 -ExecutablePath ./src/Xbox360.Remote.Cli/bin/Release/net10.0-windows/rgh.exe
```

It should be refreshed when:

- top-level commands change
- help descriptions change
- aliases are added or removed
- examples in command help change

### Architecture and reference pages

Reference pages should explain:

- how command groups fit together
- how transport layers are used
- what is and is not guaranteed
- where user-facing state is stored

### Release pages

`Releases.md` is the public release-history and current-highlights page.

Release notes should focus on:

- shipped features
- important fixes
- behavior changes that matter to users
- compatibility or requirement changes

They should not focus on wording cleanup, page upkeep, or process notes.

## Style Rules

### Wording

- Prefer exact technical wording over broad claims.
- Do not claim a feature works if it was not checked.
- Do not hide dangerous behavior under vague names.
- Prefer direct statements like `requires XBDM` or `uses FTP`.

### Examples

- Use `rgh` in examples.
- Keep examples realistic and copy-paste ready.
- Use placeholder values only when they are environment-specific.

### Paths and naming

- Prefer Windows-style console paths where that matches real console usage.
- Use `XeCLI` for the project and `rgh` for the command.
- Treat `wiki/*.md` as the maintained source for the GitHub wiki.
- GitHub wiki pages are the supported public wiki surface.

## Privacy

Before publishing logs, screenshots, reports, or command output, remove:

- IP addresses
- console IDs and serials
- XUIDs and gamertags when they identify a person
- machine-specific paths
- tokens and keys
- live network details

Do not publish debug dumps, crash dumps, packet data, or machine-specific temporary output unless it is intentionally sanitized and useful to users.

## Blocked Features

When a feature depends on something outside XeCLI, state the blocker clearly.

Examples:

- console-side plugin missing
- XBDM service unavailable
- Ghidra not installed

## Related Pages

- [Contributing](Contributing)
- [Wiki Home](Home)
- [Commands Reference](Commands)
