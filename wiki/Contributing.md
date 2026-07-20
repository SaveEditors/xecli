# Contributing

This is a contributor and maintainer guide, not an end-user setup page. For product usage, start with the [Beginner Guide](Beginner-Guide) or [Commands Reference](Commands). Contributors should also follow the [Documentation Standards](Standards).

## Good Contributions

Good changes improve one of these areas:

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
- If a command can trigger a reboot or disconnect, document that and check the post-state where possible.

### Output

- Human-readable output should stay clean and scannable.
- JSON output should remain stable enough for scripts.
- Help text must describe what the user can do, not hidden implementation details.

## Documentation Expectations

Any command or behavior change should update the relevant public docs in the same change set.

Minimum docs coverage:

- `README.md` for user-visible feature additions
- `wiki/Commands.md` for new commands or flags
- `wiki/CLI-Help.md` when help output changes
- `wiki/Frameworks.md` when user-visible behavior meaningfully changes
- `wiki/Releases.md` when shipped product behavior changes in a release-facing way

### Regenerate CLI help

Build the Release CLI, then generate the exhaustive reference from that executable:

```powershell
dotnet build ./src/Xbox360.Remote.Cli/Xbox360.Remote.Cli.csproj --configuration Release --no-restore
pwsh ./tools/update-cli-help.ps1 -ExecutablePath ./src/Xbox360.Remote.Cli/bin/Release/net10.0-windows/rgh.exe
```

The generator discovers canonical commands, omits duplicate shortcut trees, and validates every emitted section against `--help`. Do not edit `CLI-Help.md` by hand.

## Verification

### Prefer live checks when practical

For console-facing features, check behavior on a live target or other realistic setup where available.

Examples:

- `status` returns promptly and shows the expected fields
- `title` resolves the active title correctly
- `modules load` and `modules unload` match the reported module state
- `save inject` preserves data on a round trip
- screenshot output is viewable and not corrupted

### If something cannot be confirmed

Say exactly what was confirmed and what was left unverified.

Unacceptable:

- implying a feature was confirmed when it only built

Acceptable:

- `command builds, but live confirmation was blocked by missing JRPC2`

## Privacy

Submitted changes should not include:

- local machine paths that reveal a local setup
- IP addresses
- console IDs or serials
- personal account data
- tokens or keys
- unredacted screenshots or logs

## Related Pages

- [Documentation Standards](Standards)
- [Wiki Home](Home)
- [Repository](https://github.com/SaveEditors/xecli)
