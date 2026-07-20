# Contributing to XeCLI

XeCLI contributions should improve shipped behavior, operator clarity, release quality, or documentation accuracy.

## Build from Source

Prerequisites:

- x64 Windows 11, or an eligible Windows 10 Enterprise/LTSC release supported by .NET 10 (21H2, 1809, or 1607); Windows 11 is recommended
- PowerShell 7.1 or later for release tooling
- the .NET 10 SDK selected by `global.json`

From the repository root:

```powershell
dotnet restore XeCLI.sln --locked-mode --runtime win-x64 --force-evaluate
dotnet build XeCLI.sln -c Release --no-restore
dotnet run --project src/Xbox360.Remote.Cli -- --help
```

Use the public release packages when you only want to run XeCLI; source builds are intended for development.

## Core Expectations

- Keep the public command surface explicit and stable.
- Document user-visible behavior changes in the same change set.
- Prefer copy-paste-ready examples and exact technical wording.
- State clearly what was validated and what was not.

## Documentation Policy

The Markdown wiki under `wiki/*.md` is the canonical user documentation surface.

- Update the relevant wiki pages whenever shipped behavior changes.
- Do not add or maintain parallel `.html` wiki pages.
- Keep the wiki organized by user task, subsystem, and release history.
- Treat the GitHub wiki as the maintained public reference.

## Release Notes Policy

Patch notes should describe shipped product behavior.

- Include new features, important fixes, and operator-relevant behavior changes.
- Exclude README-only edits, wiki-only edits, formatting cleanups, and documentation housekeeping.
- Keep release notes professional and product-focused.
- Keep release notes, documentation, PR summaries, and commit messages focused on shipped behavior and user impact.

## Privacy and Artifact Hygiene

Never commit or publish private verification artifacts that could leak personal or machine-specific data.

Do not commit:

- dumps
- captures
- incidental screenshots unless they are intentional release assets
- temp appdata or temp localappdata directories
- machine-specific paths, IPs, serials, console IDs, gamertags, or user data
- debug logs, crash dumps, packet captures, or trace exports unless they are explicitly sanitized and intentionally tracked

Review the staged diff before every commit.

## Validation

For user-visible changes, validate the actual shipped path where possible.

- Build the release configuration.
- Check help output when commands, descriptions, or flags change.
- Update the relevant wiki pages and release notes when product behavior changes.
- Be explicit about any gaps in live verification.

## Pull Requests

- Keep each change focused and explain its user-visible effect.
- Include documentation and release-note updates when behavior changes.
- Confirm that Release configuration builds and that affected help output is accurate.
- Do not include generated release packages, local configuration, console data, or workstation-specific files.
- Complete the pull-request template and respond to review feedback before merge.

Additional contributor guidance lives in:

- `wiki/Contributing.md`
- `wiki/Standards.md`
