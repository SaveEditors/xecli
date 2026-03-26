# XeCLI v1.0.8 Release Notes

XeCLI v1.0.8 focuses on the local disk side of the toolkit, the release packaging split between XeCLI and `XeCLI-XellFetch`, final installer polish for the Windows package, and the supported IDA loader baseline used by the reverse-engineering workflow.

## XTAF Command-Surface Promotion

- `rgh xtaf` is now the primary FATX/XTAF command family in XeCLI.
- `rgh fatman` and `rgh fatx` remain supported as compatibility aliases, so existing scripts do not break.
- The public docs now frame XTAF around the workflows that matter most: physical-disk access, FATX/XTAF header scan, manual offset-open, metadata backup/restore, safe repair, and raw partition export.

## IDA Loader Baseline

- The supported IDA workflow remains pinned to `IDA Pro 9.1.250226`.
- XeCLI now documents and targets the maintained [`SaveEditors/idaxex`](https://github.com/SaveEditors/idaxex/releases/tag/0.42b-compat) `0.42b-compat` line instead of the older upstream release path.
- The supported loader bundle is published from the SaveEditors fork so the XeCLI docs and the maintained IDA loader line stay aligned.
- Public reverse-engineering docs were updated to reflect the SaveEditors `idaxex` support baseline.

## Packaging Split

- XeCLI and `XeCLI-XellFetch` now ship as clearly separated release lines.
- The full desktop CLI release remains in the XeCLI repo.
- The standalone XeLL payload bundle remains in the dedicated `XeCLI-XellFetch` repo for operators who want the payload without the rest of the desktop workflow.

## Installer Polish

- The installer keeps the bundled .NET runtime prerequisite flow so a normal Windows host can install XeCLI without manually fetching the runtime first.
- The CLI-side `rgh install` flow and first-run setup prompts were removed so the setup executable is the only install path.
- The installer now owns the initial language selection and persists it by calling the shipped `rgh language --set ...` command after install.
- The welcome page and summary copy were cleaned up so the setup flow reads like a normal public release instead of a staging build.
- Release packaging continues to validate the self-contained CLI layout before the installer is built.

## Verification

- `dotnet build .\decompiled\rgh.csproj -c Release`
- `dotnet .\decompiled\bin\Release\net10.0-windows\rgh.dll xtaf --help`
- `dotnet .\decompiled\bin\Release\net10.0-windows\rgh.dll fatman --help`
- `dotnet .\decompiled\bin\Release\net10.0-windows\rgh.dll xtaf devices --json`
- `dotnet .\decompiled\bin\Release\net10.0-windows\rgh.dll xtaf scan --image .\src\Xbox360.Remote.Cli\Assets\empty_live.bin --json`
- `dotnet build .\src\Xbox360.Remote.Cli\Xbox360.Remote.Cli.csproj -c Release`
- `dotnet .\src\Xbox360.Remote.Cli\bin\Release\net10.0-windows\rgh.dll ida install-loader --path <IDA_DIR> --archive .\idaxex-0.42b-compat_ida91.zip`
- `scripts\build-installer.ps1 -Version 1.0.8 -VerifyInstaller`

## Related Docs

- [Releases](Releases)
- [XTAF / FATX Manager](FATX-Manager)
- [Reverse Engineering](Reverse-Engineering)
- [XeCLI-XellFetch](XeCLI-XellFetch)
