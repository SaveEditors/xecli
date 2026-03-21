# Documentation Standards

This page defines how XeCLI documentation should be structured and maintained.

## Goals
The wiki should read like engineering documentation, not release marketing.

Documentation should be:

- specific
- copy-paste friendly
- honest about prerequisites and limitations
- organized by workflow and subsystem
- consistent with the live `rgh` command surface

## Structural Rules

### Home page
`Home.md` must act as a documentation index, not a wall of prose.

It should contain:

- grouped page navigation
- reading paths for operators, reverse engineers, and contributors
- a capability summary
- clear scope boundaries

### Commands pages
Command documentation must include:

- the command name
- what it does
- the important options
- one or more realistic examples
- warnings when the command can destabilize the console or modify state

### Help-output page
`CLI-Help.md` should capture the actual built-in help output from the current shipped binary.

It must be updated whenever:

- top-level commands change
- help descriptions change
- aliases are added or removed
- example lines in the command app are changed

### Internal/reference pages
Internal pages should explain:

- how the CLI is structured
- how the transport layers fit together
- what is and is not guaranteed
- where persistent state is stored

## Style Rules

### Wording
- Prefer exact technical wording over broad language.
- Do not claim a feature works if it was not verified.
- Do not hide dangerous behavior under vague names.
- Prefer direct statements like `requires XBDM` or `uses FTP`.

### Examples
- Use `rgh` in examples, not internal executable paths.
- Keep examples realistic and copy-paste ready.
- Use placeholder values only when they are truly environment-specific.

### Paths and naming
- Prefer repository-relative paths in docs about the repo.
- Prefer Windows-style console paths where that matches the real console workflow.
- Use `XeCLI` for the project and `rgh` for the command.

## Maintenance Rules

### When command behavior changes
Update all of:

- `README.md`
- `wiki/Commands.md`
- `wiki/CLI-Help.md`
- any affected workflow page

### When a new subsystem is added
Add or update:

- an entry from `Home.md`
- the relevant command reference section
- internal design notes in `Frameworks.md`
- integration notes if the feature is reusable by external tools

### When a feature is blocked by external services
State the blocker clearly.

Examples:

- console-side plugin missing
- XBDM service unavailable
- Ghidra not installed

## Release Readiness Checklist

- README screenshots match the current shipped build
- release zip exists and is attached to the current release
- wiki links in README resolve
- Pages landing page resolves
- command reference matches the live binary
- source repo does not contain dumps, screenshots from testing, or build output except intended repo assets
