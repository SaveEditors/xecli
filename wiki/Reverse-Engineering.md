# Reverse Engineering

## External Tooling

XeCLI does not bundle Ghidra, IDA Pro, Java, or XEX loader plugins. Reverse-engineering workflows depend on external installs plus the helper loaders that XeCLI can install after you point it at those tools.

- Official Ghidra site: [Ghidra](https://ghidra-sre.org/)
- Official Ghidra releases: [NSA Ghidra Releases](https://github.com/NationalSecurityAgency/ghidra/releases)
- Official IDA Pro site: [IDA Pro](https://hex-rays.com/ida-pro)
- Official Hex-Rays download/account portal: [My Hex-Rays](https://my.hex-rays.com/)
- Official open-source IDA SDK: [ida-sdk](https://github.com/HexRaysSA/ida-sdk)
- Maintained Ghidra XEX loader supported by XeCLI: [SaveEditors/XEXLoaderWV](https://github.com/SaveEditors/XEXLoaderWV)
- Upstream XEXLoaderWV project: [zeroKilo/XEXLoaderWV](https://github.com/zeroKilo/XEXLoaderWV)
- Community Xbox reverse-engineering references: [emoose/xbox-reversing](https://github.com/emoose/xbox-reversing/tree/master)

## Ghidra Support

### Ghidra command surface

- `rgh ghidra config`
- `rgh ghidra install-loader`
- `rgh ghidra analyze`
- `rgh ghidra decompile`
- `rgh ghidra export-symbols`
- `rgh ghidra verify`
- `rgh xex decompile`
- `rgh xex map`
- `rgh xex bundle`

### What XeCLI does for Ghidra

- Treats Ghidra as an external headless tool.
- Stores the Ghidra install path, `JAVA_HOME`, and default project path.
- Pulls `.xex` files from a local path, FTP path, or the current running title.
- Ships the Ghidra headless helper script used by `rgh ghidra analyze` and `rgh ghidra decompile`.
- Detects `XEXLoaderWV.jar` automatically for `.xex` imports.
- Can install `XEXLoaderWV` into the configured Ghidra install with `rgh ghidra install-loader`.
- Verifies decompile output with `rgh ghidra verify`.
- Maps Ghidra addresses back to XEX load addresses with `rgh xex map --file .\default.xex --ghidra-base 0x82000000 --ghidra 0x82001234`.
- Exports an offline XEX analysis bundle with `rgh xex bundle` so the source metadata, address map, and optional symbol sidecar can be reloaded and validated without another analysis pass.

## XEX Metadata

### XEX metadata command surface

- `rgh xex info`
- `rgh xex header`
- `rgh xex map`
- `rgh xex bundle`

### What XeCLI does for XEX metadata

- Inspects local XEX metadata offline from a file on disk.
- Treats `rgh xex header` as the same metadata view as `rgh xex info`.
- Maps a Ghidra address to the matching XEX load address offline from a local XEX file.
- Packages XEX metadata, optional address-map data, and an optional symbol sidecar into a round-trip bundle.
- Uses concise file references in the default text output.
- Supports `--json` when you want machine-readable output.
- The JSON shape is intended to stay stable for scripting. Hex-valued fields are emitted as `0x`-prefixed strings, and private path-like strings are redacted or reduced to safe leaf names rather than echoed verbatim.

### Ghidra requirements

- Ghidra is external and not shipped by XeCLI.
- Ghidra is documented as `(Free)` in the CLI. XeCLI pins the loader integration to `SaveEditors/XEXLoaderWV` tag/version `13.0.0`, asset `ghidra_12.0.4_PUBLIC_20260325_XEXLoaderWV.zip`, SHA-256 `498B9C2A2430585CC49A13DB33603B6A46CFE84B157985F9BE2C4360F917FA5A`.
- Java must be available for the configured Ghidra runtime.
- `XEXLoaderWV` is required for clean `.xex` imports.
- After `rgh ghidra config --path <dir>`, XeCLI can install the loader helper for you with `rgh ghidra install-loader`.

## IDA Support

### Supported baseline

- Supported IDA build: `IDA Pro 9.3`
- Supported XEX loader build: `SaveEditors/idaxex` tag `ida-pro-9.3-saveeditors-1`, archive SHA-256 `1734277D26FF4985F15931B6855368312EC11D619D53D2C104F9F7C5753BBD7C`.
- The supported baseline is `IDA Pro 9.3`. Legacy support requires `IDA Pro 9.1.250226` and `emoose/idaxex` tag `0.42b`, asset `idaxex+xex1tool-0.42b_ida91.zip`, SHA-256 `49F7C519C4A0BF7E90AF3411554B00591C0C628676E1CBA62A7EAA6216BDCD89`. Other IDA or loader combinations are outside the documented baseline.

### IDA command surface

- `rgh ida config`
- `rgh ida check`
- `rgh ida install-loader`
- `rgh ida analyze`
- `rgh ida decompile`
- `rgh ida export-symbols`
- `rgh ida verify`
- `rgh xex ida-decompile`

### What XeCLI does for IDA

- Treats IDA Pro as an external headless tool.
- Stores the IDA install path, Python command, `IDAUSR`, and preferred execution path.
- Validates the configured runtime with `rgh ida check`.
- Installs the pinned IDA-compatible XEX loader set with `rgh ida install-loader`.
- Ships the IDA helper scripts used by `rgh ida analyze` and `rgh ida decompile`.
- Imports raw `.xex` files through `idat.exe` batch mode.
- Uses `idalib` for database-backed decompilation when available.
- Supports one-shot console-driven decompilation through `rgh xex ida-decompile`.
- Verifies exported C output with `rgh ida verify`.
- Exports function names to a small shared symbol sidecar JSON with `rgh ida export-symbols` or `rgh ghidra export-symbols`, then imports those names into offline bookmarks with `rgh mem-bookmarks import`.

### Symbol sidecars

The symbol sidecar slice is intentionally small and user-facing:

- `version`
- `module`
- `symbols`

Each entry in `symbols` carries `name`, `RVA`, and `type`.

This workflow is for function names only. It does not cover comments or PDB data, and there is no comments/PDB import-back workflow yet. `rgh mem-bookmarks import --file .\symbols.json` accepts either IDA or Ghidra sidecars when the bookmarks use exact-address or module/RVA style coordinates.

### IDA requirements

- IDA Pro is external and not shipped by XeCLI.
- XeCLI documents `IDA Pro 9.3` as the supported baseline, with legacy `IDA Pro 9.1.250226` archives still supported for compatible installs.
- A valid local IDA install and license are required for `idat` and `idalib`.
- The supported IDA-compatible XEX loader must be present for `.xex` loading.
- Python is required for `idalib` workflows.
- After `rgh ida config --path <dir>`, XeCLI can install the supported loader helper for you with `rgh ida install-loader`.
- Built-in archives are pinned and authenticated. A custom `--archive` or `--url` requires `--sha256`, and every installed loader, TIL, and legacy `xex1tool.exe` payload is checked against the manifest for the detected IDA build.
