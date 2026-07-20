# Building a Windows Release

XeCLI releases are built by `tools/build-release.ps1`. The script publishes the CLI/GUI payload and XeTerminal launcher for `win-x64`, creates a portable stage with `xecli.portable`, writes an exact SHA-256 file manifest, probes executable output, builds a deterministic single-root ZIP, and validates the ZIP, release summary, checksums, and metadata sidecars against the staged body.

The version is always explicit and must match every project. There is no development-version fallback.

## Prerequisites

- PowerShell 7.1 or later; Windows PowerShell 5.1 is not supported
- the .NET SDK selected by `global.json`
- a writable NuGet package cache containing the locked dependencies, or network access to restore them
- Inno Setup 6 when `-BuildInstaller` is requested
- SignTool, a certificate in a Windows personal certificate store, and a timestamp service when signed promotion is selected

Pass tool paths as parameters when they are not available through the command environment. The script does not depend on workstation-specific paths.

## Local unsigned package

Use local mode for repeatable development packaging and installer checks:

```powershell
.\tools\build-release.ps1 `
  -Version 2.0.0 `
  -Mode Local `
  -DotNetPath dotnet `
  -PackagesPath <package-cache>
```

Add `-BuildInstaller -IsccPath <iscc-path>` to compile the installer. Add `-DryRun` to validate inputs and planned output locations without publishing.

Local mode verifies that XeCLI-produced executables and the optional installer are unsigned. Its manifest records `ReleaseKind` as `local-unsigned`, and the resulting artifacts are not eligible for promotion.

## Promotion policy

Promotion mode fails closed. Every promotion requires a clean worktree, a complete git revision, and `.github/release-policy.json` with `provenanceStatus` set to `resolved`. The status may be resolved only after every shipped path is covered by `THIRD-PARTY-NOTICES.md`; it must not be changed merely to exercise the release script.

Promotion also requires `-BuildInstaller`, the exact SDK version pinned by `global.json`, and exactly one explicit artifact policy:

- `-RequireSigning` produces `ReleaseKind: promotion-signed`.
- `-AllowUnsignedPromotion` produces `ReleaseKind: public-unsigned`.

The switches are mutually exclusive. Omitting both, passing both, passing signing inputs with unsigned promotion, or attempting to promote `local-unsigned` fails before publishing. There is no automatic downgrade from signed to unsigned output.

## Public unsigned promotion

Use public unsigned promotion when no paid or identity-verified signing certificate will be used:

```powershell
.\tools\build-release.ps1 `
  -Version 2.0.0 `
  -Mode Promotion `
  -AllowUnsignedPromotion `
  -DotNetPath dotnet `
  -PackagesPath <package-cache> `
  -BuildInstaller `
  -IsccPath <iscc-path>
```

Do not pass `-SignToolPath`, `-CertificateThumbprint`, or `-TimestampUrl` in this mode. The script verifies that the staged binaries and installer are unsigned.

> **WARNING: Publisher is Unknown. Windows SmartScreen may warn or block this unsigned public release. Verify the SHA256 inventory before running any artifact.**

The warning, `Publisher: Unknown`, `ReleaseKind: public-unsigned`, version, git revision, provenance state, portable marker/body gates, installer status, and complete staged SHA-256 inventory are written to the manifest, release summary, and each artifact's `.release.json` metadata sidecar. The installer and portable body include the project license, only the third-party notices required by shipped components, payload checksums, and exact matching-tag source directions. Complete corresponding source remains in the public release tag instead of adding a 22 MiB development tree to every end-user package.

## Signed promotion

Signed promotion requires `-RequireSigning`, a matching certificate, SignTool, and a timestamp URL:

```powershell
.\tools\build-release.ps1 `
  -Version 2.0.0 `
  -Mode Promotion `
  -RequireSigning `
  -SignToolPath <signtool-path> `
  -CertificateThumbprint <certificate-thumbprint> `
  -TimestampUrl <timestamp-url> `
  -DotNetPath dotnet `
  -PackagesPath <package-cache> `
  -BuildInstaller `
  -IsccPath <iscc-path>
```

The script stops before publishing if a required signing input is absent. It verifies Authenticode signatures after signing and records `ReleaseKind` as `promotion-signed`. Signed promotion remains subject to the same clean-tree and resolved-provenance gates as public unsigned promotion.

## Outputs

By default, generated files are written under `artifacts/release`:

- `stage/XeCLI-<version>-win-x64/` - validated portable stage
- `XeCLI-<version>-win-x64.zip` - deterministic portable archive
- `XeCLI-<version>-win-x64.zip.sha256` - archive checksum
- `XeCLI-<version>-win-x64.zip.release.json` - archive policy, inventory, warning, and artifact metadata
- `release-summary.json` - complete release policy and artifact summary
- `XeCLI-<version>-win-x64-setup.exe` - installer; required for promotion
- `XeCLI-<version>-win-x64-setup.exe.sha256` - installer checksum; required for promotion
- `XeCLI-<version>-win-x64-setup.exe.release.json` - installer policy and exact embedded application-payload inventory metadata; required for promotion

The portable `release-manifest.json` covers every staged file except the manifest itself. Promotion validates every first-party console payload recorded in `XeCLI-XellFetch/SHA256SUMS.txt`, the complete XeLL corresponding-source inventory, and the complete X360 GPL source manifest. The source-built `Assets/QuickBoot/X360.dll` is pinned to SHA-256 `F90691B92A91FFA941BB111039DE031907A82A510C777231FE9C6D356DA437A9`. Standard `.sha256` files contain only the artifact digest and filename. The portable metadata sidecar carries the exact portable inventory; the installer sidecar carries the exact files embedded for installation. Both also record version, revision, installer status, portable gates, publisher state, and the SmartScreen warning.

`rgh release check` rejects unknown release kinds and promotion policy, `local-unsigned` promotion, unresolved public provenance, missing version or revision evidence, altered inventories, noncanonical sidecar names, warning removal, and artifact or ZIP body mismatches.

## Release qualification

Build a promotion only from the final clean commit. Before publication, that exact commit must pass the complete automated test suite, command/help generation checks, documentation and leak checks, release-contract validation, and the supported Windows install lifecycle. The lifecycle must cover portable launch and state, fresh current-user and all-users setup, PATH selected and skipped, same-directory upgrade, migration from the latest v1 release, safe rejection of portable and unrelated nonempty destinations, uninstall, and retained user data.

The reproducible public qualification path starts with the repository-pinned SDK and release builder:

```powershell
dotnet restore .\XeCLI.sln --locked-mode --runtime win-x64 --force-evaluate
dotnet build .\XeCLI.sln --configuration Release --no-restore
.\tools\build-release.ps1 `
  -Version 2.0.0 `
  -Mode Local `
  -DotNetPath dotnet `
  -PackagesPath <package-cache> `
  -BuildInstaller `
  -IsccPath <iscc-path>
```

After qualification passes, repeat the build from the reviewed clean commit with the explicit public unsigned policy shown above. Then validate the complete promotion output with the staged executable and exact artifact paths:

```powershell
.\artifacts\release\stage\XeCLI-2.0.0-win-x64\rgh.exe release check `
  --publish-dir .\artifacts\release\stage\XeCLI-2.0.0-win-x64 `
  --project .\src\Xbox360.Remote.Cli\Xbox360.Remote.Cli.csproj `
  --zip .\artifacts\release\XeCLI-2.0.0-win-x64.zip `
  --hash .\artifacts\release\XeCLI-2.0.0-win-x64.zip.sha256 `
  --summary .\artifacts\release\release-summary.json `
  --expected-release-kind public-unsigned `
  --promotion `
  --json
```

Do not reuse packages or qualification evidence from another revision. Retain the final commit ID, resolved SDK version, release kind, `release-summary.json`, artifact sidecars, SHA-256 files, and install-lifecycle result with the release review materials. These are the evidence that the reviewed commit and publication files match.

## Publication set

Promotion produces exactly seven files for the GitHub release:

1. `XeCLI-<version>-win-x64.zip`
2. `XeCLI-<version>-win-x64.zip.sha256`
3. `XeCLI-<version>-win-x64.zip.release.json`
4. `XeCLI-<version>-win-x64-setup.exe`
5. `XeCLI-<version>-win-x64-setup.exe.sha256`
6. `XeCLI-<version>-win-x64-setup.exe.release.json`
7. `release-summary.json`

Do not upload staging directories, logs, local configuration, test output, or packages from earlier revisions. GitHub's automatically generated source archives are separate from this seven-file allowlist.

## Manual review and authorization

Stop before any remote mutation. A maintainer must review the final README and screenshot, user-facing updates, wiki, installer flow, portable layout, unsigned-publisher warning, checksums, metadata, and seven-file publication set. Publishing requires explicit authorization for the exact commit and release version.

Private vulnerability reporting must be enabled before `SECURITY.md` goes live. Publish the matching wiki revision with the main repository so README links do not lead to older guidance.

Legacy tags and releases are not deleted, moved, or rewritten as part of a normal release. If repository-history replacement or another exceptional cutover is required, verify a complete recovery backup first and obtain separate explicit authorization for the exact ref changes.

## Publish and verify

Create tag `v<version>` from the reviewed commit and prepare the GitHub release as a draft. Upload only the seven approved files, then download them into a new directory and verify their SHA-256 values and release metadata independently before publishing the draft.

After publication, verify the tag resolves to the reviewed commit, the release and wiki links work, the installer and portable ZIP download successfully, and both checksum examples in the README match the downloaded files. Repeat a clean installer and portable smoke check using the downloaded artifacts rather than local build output.

If upload or verification fails, keep the release as a draft and correct the complete set from the same commit. If any part has already become public, stop further changes and either complete the exact reviewed publication or perform an explicitly authorized rollback; do not mix revisions or replace individual assets silently.

## Installer data behavior

Install and upgrade preserve existing `UiLanguage` and `PathPromptHandled` values. A malformed configuration file is left untouched for recovery. Uninstall retains settings under `%APPDATA%\XeCLI`, cache and the default command log under `%LOCALAPPDATA%\XeCLI`, and default XeTerminal captures under the Windows Pictures folder at `XeCLI\Captures`. Configured capture/log directories and any `XECLI_HOME` directory are also retained.
