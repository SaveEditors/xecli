# Contributing to XeCLI

XeCLI contributions should improve shipped behavior, operator clarity, release quality, or documentation accuracy.

## Core Expectations

- Keep the public command surface explicit and stable.
- Document user-visible behavior changes in the same change set.
- Prefer copy-paste-ready examples and exact technical wording.
- State clearly what was validated and what was not.

## Documentation Policy

The Markdown wiki under `wiki/*.md` is the canonical user documentation surface.

- Update the relevant wiki pages whenever shipped behavior changes.
- Do not add or maintain parallel `.html` wiki pages.
- Keep the wiki organized by workflow, subsystem, and release history.
- Treat the GitHub wiki as the maintained public reference.

## Release Notes Policy

Patch notes should describe shipped product behavior.

- Include new features, important fixes, and operator-relevant behavior changes.
- Exclude README-only edits, wiki-only edits, formatting cleanups, and documentation housekeeping.
- Keep release notes professional and product-focused.
- Do not mention internal drafting tools, assistants, or generation workflows in release notes, documentation, PR summaries, or commit messages.

## Privacy and Artifact Hygiene

Never commit or publish local test material that could leak personal or machine-specific data.

Do not commit:

- dumps
- captures
- screenshots from local testing unless they are intentional release assets
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

Additional contributor guidance lives in:

- `wiki/Contributing.md`
- `wiki/Standards.md`
