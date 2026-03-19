# Contributing

This page defines the expected contribution model for XeCLI.

## What Good Contributions Look Like
Good contributions improve one of these areas:

- live-console reliability
- command clarity
- output quality
- documentation accuracy
- release quality
- integration surface for external tools

Every contribution should leave the command surface clearer, safer, or easier to automate.

## Code Expectations

### Behavior
- Do not silently change command meaning.
- Do not widen the scope of a dangerous command without an explicit flag or confirmation path.
- Prefer explicit subcommands over overloaded positional behavior.

### Reliability
- Commands that should fail fast must not hang for long periods.
- Console-disconnect behavior must be handled honestly.
- If a command can trigger a reboot or disconnect, document that and verify post-state where possible.

### Output
- Human-readable output should stay clean and scannable.
- JSON output should remain stable enough for scripts.
- Help text must explain the real operator workflow, not internal implementation details.

## Documentation Expectations
Any command or behavior change should update the relevant docs in the same change set.

Minimum docs coverage:

- `README.md` for user-visible feature additions
- `wiki/Commands.md` for new commands or flags
- `wiki/CLI-Help.md` when help output changes
- `wiki/Frameworks.md` when internal behavior meaningfully changes

## Validation Expectations

### Prefer live validation when possible
For console-facing features, verify on real hardware or against the actual target environment where available.

Examples:

- `status` returns promptly and shows the expected fields
- `title` resolves the active title correctly
- `modules load` and `modules unload` verify against real module state
- `save inject` round-trips cleanly
- screenshot output is viewable and not corrupted

### If something cannot be verified
State exactly what was and was not proven.

Unacceptable:

- implying a feature is verified when it only builds

Acceptable:

- `command compiles, but live verification was blocked by missing JRPC2`

## Release Contributions
Release-facing work should include:

- current screenshots
- current README links
- clean release archive contents
- no machine-specific local paths in docs
- no leftover dumps or test artifacts in the repo

## Documentation Contribution Model
The wiki should read like a real developer-owned knowledge base.

That means:

- grouped page navigation
- operator path and developer path
- subsystem explanation
- standards for future documentation updates

## Current Constraints
At the time of writing, the source-tree wiki content exists and Pages is live, but GitHub’s separate Wiki backend may still require first-page initialization on GitHub itself before `.wiki.git` is writable.

Treat that as an infrastructure constraint, not a reason to lower the quality of the wiki content.
