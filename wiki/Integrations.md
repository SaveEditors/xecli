# Integrations

This page is for people who want to build other tools around XeCLI or reuse its command and file formats.

## Integration Modes
There are three useful ways to integrate with XeCLI:

1. Call the CLI directly.
2. Parse its JSON output.
3. Read operator-configured metadata files directly.

Use the smallest integration surface that solves the problem.

## Automation Tools
XeCLI is designed to work as a process-oriented automation layer. An external tool can invoke `rgh`, inspect the exit code, consume structured output, and retain redacted reports or transcripts without embedding XBDM, JRPC2, FTP, FATX, XEX, or package-handling code.

There is no separate automation service to configure. The command line is the integration boundary, and the caller receives only the access already available to the local XeCLI process and its selected console target.

### Automation Surface

| Need | XeCLI surface | Automation contract |
| --- | --- | --- |
| Run repeatable command files | `rgh script run`, `rgh script replay` | Validate with `--dry-run`, explicitly allow live commands, and retain a redacted transcript |
| Select and validate targets | `rgh target-saved`, `rgh health`, `rgh network` | Use a named target instead of discovery and check local state before contacting a console |
| Create structured snapshots | `rgh report`, `rgh report-profile` | Emit Markdown, HTML, CSV, or JSON; compare saved reports offline |
| Read live state | `status`, `profiles`, `title`, `signin`, `modules`, `mem`, `content`, and other commands with `--json` | Parse JSON and use the process exit code instead of scraping terminal tables |
| Transfer and compare files | `rgh ftp`, `rgh fs`, `rgh save`, `rgh content` | Preview supported writes with `--dry-run`; use hashes, diffs, resume, and transfer summaries where available |
| Capture and compare memory state | `rgh mem map`, `mem compare`, `mem dump-compare`, `mem-bookmarks` | Save machine-readable snapshots and perform comparisons or bookmark work offline |
| Automate reverse engineering | `rgh xex`, `rgh ghidra`, `rgh ida` | Build analysis bundles, run headless tools, verify output, and exchange symbol sidecars |
| Plan storage changes | `rgh xtaf patch plan`, `rgh xtaf patch apply` | Generate a patch manifest, preview it, and apply only when the baseline still matches |
| Preserve an audit trail | `rgh log`, `rgh diagnostics`, `rgh support` | Export local history or create redacted diagnostic artifacts |
| Configure shells | `rgh completion` | Emit completion scripts for PowerShell, Bash, or Zsh to standard output |
| Check release packages | `rgh release check` | Maintainer-only validation of a publish directory, archive, and checksum metadata |

### Recommended Execution Contract

For reliable automation:

1. Select an explicit saved target instead of relying on discovery.
2. Run `rgh health --json` and `rgh target-saved validate --json` before a live workflow.
3. Prefer `--json`, CSV, or a documented output file over terminal-table parsing.
4. Preview supported write operations with `--dry-run`.
5. Require an explicit operator decision before allowing live, destructive, reboot, or confirmation-gated commands.
6. Use completion options such as `--wait` when the workflow must confirm an observed state change instead of only receiving a request acknowledgement.
7. Retain redacted transcripts, reports, or exported logs when the workflow needs an audit trail.

Do not place keyvaults, credentials, CPU keys, unredacted reports, or other private console data in shared command files or logs. Keep those files local and pass private values only where the selected workflow explicitly requires them.

### Command Files and Transcripts

`rgh script run` reads one XeCLI command per line. A dry run validates and classifies the file without executing its child commands. Live or destructive entries remain blocked unless the caller deliberately adds `--allow-live`.

```powershell
rgh script run .\workflow.txt --dry-run
rgh script run .\workflow.txt --allow-live --transcript-out .\workflow-transcript.json
rgh script replay .\workflow-transcript.json --dry-run
```

The transcript is structured and redacted. Replay is offline and never reruns the saved child commands. The compatibility alias `rgh batch` reaches the same command tree, but new integrations should use `rgh script`.

### Targets, Preflight, and Reports

Named targets and report profiles make repeated jobs deterministic:

```powershell
rgh target-saved validate --json
rgh target-saved use home --json
rgh health --json
rgh status --json

rgh report-profile add operator-review --format json --include-modules --include-threads
rgh report --report-profile operator-review --out .\console-report.json
rgh report --left .\report-before.json --right .\report-after.json --format json
```

Reports redact console and network identifiers by default. Add `--include-private` only for output that remains under the operator's control.

### Previewable Write Workflows

Several write-capable command families expose planning or dry-run modes suitable for a review gate:

```powershell
rgh ftp put --path /Hdd1/Plugins --in .\Plugins --recursive --dry-run --json
rgh ftp sync --direction upload --path /Hdd1/Plugins --in .\Plugins --recursive --dry-run --json
rgh trainer apply --file .\trainer.json --dry-run
rgh avatar install --contentid <CONTENT-ID> --current-user --dry-run
rgh xtaf patch apply --patch .\xtaf-patch.json --image .\hdd.img --dry-run --json
```

A successful preview is not permission to perform the write. The caller should present the plan and require an explicit decision before removing `--dry-run` or adding an automatic-confirmation option.

### Completion and Observed State Changes

Some live commands distinguish an accepted request from an observed completion. Use the completion form when later steps depend on the result:

```powershell
rgh launch --xex Hdd1:\Aurora\Aurora.xex --wait
rgh reboot --wait
```

Avoid interactive flows such as `rgh start`, `rgh avatar choose`, or confirmation-gated destructive commands in unattended jobs. Prefer explicit targets and arguments, and stop the workflow on any non-zero exit code.

## Use the CLI Directly
XeCLI is useful as an orchestration layer when your tool needs live console interaction but you do not want to duplicate transport code.

Good examples:

- launchers that need title and module metadata
- dashboards that need status snapshots
- overlays that need sign-in/session state
- operator panels that need LED or notification actions
- save tools that need extraction and injection
- reverse-engineering helpers that need XEX pulls or Ghidra/IDA automation
- storage tools that want SaveEditors Xtaf-CLI as an optional external mount or recovery surface

Typical calls:

```powershell
rgh status --json
rgh title --json
rgh signin state --json
rgh modules list --json
rgh content list --json
```

For external Xtaf-CLI workflows, use the wrapper commands instead of duplicating FATX logic:

```powershell
rgh xtaf-cli status
rgh xtaf-cli status --require-mount
rgh xtaf-cli install
rgh xtaf-cli run --arg=--help
```

## Parse JSON Output
Prefer `--json` when you need stable machine-readable output instead of screen scraping terminal tables.

Common commands with useful JSON output:

- `status`
- `profiles`
- `title`
- `scan`
- `modules list`
- `modules info`
- `mem regions`
- `mem strings`
- `content list`
- `signin state`
- `ghidra verify`
- `ida check`
- `ida verify`

Example:

```powershell
rgh status --json > status.json
rgh title --json > title.json
rgh signin state --json > signin.json
rgh modules list --json > modules.json
```

## Reuse the Title ID Database
XeCLI does not ship a title database. External tools can consume the same operator-supplied CSV/TXT file selected in XeTerminal Settings, named by `XECLI_TITLE_DATABASE`, or stored as `titleids.csv` in the active XeCLI configuration directory. That directory is `%APPDATA%\XeCLI` when installed, package-local `UserData` in portable mode, or the directory selected by `XECLI_HOME`.

That file can be reused to:

- resolve Title IDs
- annotate content inventory
- label saves and dumps
- enrich UI views with media and region data

There is no runtime fetch. `rgh title --json` also exposes `Database.SourceKind`, `SourcePath`, entry counts, and line-specific warnings.

## Reuse the Avatar Item Collection
XeCLI also expects an avatar corpus named `Avatar-Item-Collection`.

That corpus is intended to be reused by external tools that need:

- offline avatar item browsing
- title-aware filtering
- item metadata indexing
- pack-level download or cache orchestration
- avatar install workflows without depending on an online marketplace

The hosted collection lives at:

- [SaveEditors/Avatar-Item-Collection](https://github.com/SaveEditors/Avatar-Item-Collection)

Remote consumers should treat the manifest/title-map pair as the primary entry point:

- `avatar-manifest.json`
- `avatar-title-map.json`

The current XeCLI remote defaults resolve to the same hosted repo and keep a local download cache so repeated installs do not re-fetch the same pack.

The recommended external-tool pattern is:

1. index the local collection once
2. expose title-based and text-based filters in your own UI
3. download or cache only the packs your workflow needs
4. hand install or patch data to XeCLI or a companion installer layer

Use `Avatar-Item-Collection` as the public-facing corpus name in your own tooling and integrations.

Typical CLI entry points:

```powershell
rgh avatar library show --json
rgh avatar games --json
rgh avatar items --titleid 415608C3 --json
rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user
```

When the target console's FTP service is not running, `rgh avatar install` can fall back to XBDM upload and XBDM-side verification.

## Use XeCLI as a Console Worker
Example pattern:

```powershell
rgh xex dump --out .\title.xex
rgh screenshot --out .\screen.bmp
rgh save extract --titleid 415608C3 --out .\saves
```

This works well when your application wants the files that XeCLI can pull rather than embedding transport logic itself.

This is also a good fit when you want XeCLI’s live progress handling and clear error messages without rewriting FTP/XBDM transport code yourself.

## Ghidra and IDA Integration Patterns
If your tool needs decompile output but you do not want to own the reverse-engineering automation layer yourself, XeCLI can drive either supported path.

### Ghidra path

If you want XeCLI to handle Ghidra import, decompile, and verification:

```powershell
rgh ghidra decompile --in .\title.xex --out .\decomp
rgh ghidra verify --dir .\decomp --json
```

This keeps import, decompile, and verification logic in one place.

### IDA path

If you want XeCLI to handle the IDA import/decompile path instead:

```powershell
rgh ida analyze --in .\title.xex --out-db .\title.i64 --overwrite
rgh ida decompile --in .\title.i64 --out .\ida-decomp --backend idalib --max 50
rgh ida verify --dir .\ida-decomp --json
```

For a one-shot XEX-driven IDA path, use:

```powershell
rgh xex ida-decompile --in .\title.xex --out .\ida-decomp --out-db .\title.i64 --keep-db
```

Use the Ghidra path when you want the `(Free)` external tool or a looser environment requirement. Use the IDA path when you want the documented `IDA Pro 9.3` plus `SaveEditors idaxex 9.3` workflow, with legacy `IDA Pro 9.1.250226` archive compatibility still available.

### Symbol sidecar path

If you only need to move function names out of IDA or Ghidra and into offline bookmarks, use the sidecar path:

```powershell
rgh ida export-symbols --in .\title.i64 --out .\symbols.json
rgh ghidra export-symbols --in .\title.xex --out .\symbols.json
rgh mem-bookmarks import --file .\symbols.json
```

The exported JSON keeps the user-facing `version`, `module`, and `symbols` shape, with `name`, `RVA`, and `type` on each symbol entry. `rgh mem-bookmarks import --file .\symbols.json` accepts either IDA or Ghidra sidecars. This slice is function-name only and stays on the exact-address or module/RVA workflow; it does not include comments or PDB data.

## Notification Integration Pattern
If your app needs visible console-side feedback, use XeCLI as the notification layer rather than hardcoding icon IDs in multiple places.

Examples:

```powershell
rgh notify "Build finished" 14
rgh notify "Patch applied" 16
rgh notify "Download complete" 55
rgh led set --preset quadrant1
```

For custom frontends, a practical pattern is:

1. keep your own message text in the host app
2. call XeCLI with a known icon ID or preset
3. show the same event in your host UI and on the console

Read [XNotify.md](XNotify) for the icon reference and direct usage guidance.

## Config and Override Files
The active XeCLI configuration directory is `%APPDATA%\XeCLI` when installed, package-local `UserData` in portable mode, or the directory selected by `XECLI_HOME`. It can contain:

- `config.json`
- `titleids.csv` (optional user metadata)
- `titleids.discovered.csv` (XeCLI-generated live-name cache)
- `titleids.local.csv` (legacy compatibility)

If you build another tool around XeCLI, do not hardcode machine-specific paths. Resolve config and asset paths relative to the XeCLI executable or the active configuration directory.

If you ship XeCLI beside another tool, prefer resolving:

- executable folder for optional `ConsoleDependencies/` and shipped `Assets/`, `ghidra_scripts/`, and `ida_scripts/`
- the active configuration directory for runtime overrides
- the configured `titleids.csv`/TXT path for user-specific metadata

## When to Reuse XeCLI Instead of Rewriting It
Use XeCLI directly when:

- you want stable command names
- you need live XBDM/JRPC2/FTP coordination
- you want consistent metadata parsing and Ghidra/IDA flows
- you need a tool that can still be used manually in terminal

Reimplement only if:

- you need a library API instead of a process boundary
- you need your own transport and retry behavior
- your application cannot tolerate CLI process orchestration

## Recommended External-Tool Entry Points

| Need | Recommended XeCLI entry point |
| --- | --- |
| Live status card | `rgh status --json` |
| Active title badge | `rgh title --json` |
| Signed-in user badge | `rgh signin state --json` |
| Notification banner on console | `rgh notify` |
| Session/hardware indicator | `rgh led set` |
| File pull/push | `rgh ftp ...` or `rgh fs ...` |
| Command-file workflow | `rgh script run <FILE> --dry-run`, followed by an explicitly approved `--allow-live` run |
| Offline transcript review | `rgh script replay <TRANSCRIPT> --dry-run` |
| Saved target preflight | `rgh target-saved validate --json`, `rgh target-saved use <NAME> --json`, `rgh health --json` |
| Structured console report | `rgh report --format json`, optionally with `rgh report-profile` |
| Offline report comparison | `rgh report --left <FILE> --right <FILE> --format json` |
| Transfer preview | `rgh ftp put ... --dry-run --json` or `rgh ftp sync ... --dry-run --json` |
| Memory snapshot comparison | `rgh mem map --json`, `rgh mem compare`, or `rgh mem dump-compare --json` |
| Symbol/bookmark exchange | `rgh ida export-symbols` or `rgh ghidra export-symbols`, then `rgh mem-bookmarks import` |
| Storage patch review | `rgh xtaf patch plan`, then `rgh xtaf patch apply ... --dry-run --json` |
| Audit and support artifacts | `rgh log export`, `rgh diagnostics bundle`, or `rgh support bundle` |
| Shell completion | `rgh completion powershell`, `rgh completion bash`, or `rgh completion zsh` |
| Running-XEX acquisition | `rgh xex dump` |
| Decompile pipeline | `rgh ghidra decompile` + `rgh ghidra verify --json` or `rgh ida decompile` + `rgh ida verify --json` |
| Save backup/import | `rgh save extract` / `rgh save inject` |
