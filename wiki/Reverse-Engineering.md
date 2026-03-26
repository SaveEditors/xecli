# Reverse Engineering

## External Tooling

XeCLI does not bundle Ghidra, IDA Pro, Java, or XEX loader plugins. Reverse-engineering workflows depend on external installs plus the helper loaders that XeCLI can install after you point it at those tools.

- Official Ghidra site: [Ghidra](https://ghidra-sre.org/)
- Official Ghidra releases: [NSA Ghidra Releases](https://github.com/NationalSecurityAgency/ghidra/releases)
- Official IDA Pro site: [IDA Pro](https://hex-rays.com/ida-pro)
- Official Hex-Rays download/account portal: [My Hex-Rays](https://my.hex-rays.com/)
- Official open-source IDA SDK: [ida-sdk](https://github.com/HexRaysSA/ida-sdk)
- Official Ghidra XEX loader project used by XeCLI: [XEXLoaderWV](https://github.com/zeroKilo/XEXLoaderWV)
- Community Xbox reverse-engineering references: [emoose/xbox-reversing](https://github.com/emoose/xbox-reversing/tree/master)

## Ghidra Support

### Current command surface

- `rgh ghidra config`
- `rgh ghidra install-loader`
- `rgh ghidra analyze`
- `rgh ghidra decompile`
- `rgh ghidra verify`
- `rgh xex decompile`

### What XeCLI does for Ghidra

- Treats Ghidra as an external headless backend.
- Stores the Ghidra install path, `JAVA_HOME`, and default project path.
- Pulls `.xex` files from a local path, FTP path, or the current running title.
- Ships the headless helper script in `src/Xbox360.Remote.Cli/ghidra_scripts/`.
- Detects `XEXLoaderWV.jar` automatically for `.xex` imports.
- Can install `XEXLoaderWV` into the configured Ghidra install with `rgh ghidra install-loader`.
- Verifies decompile output with `rgh ghidra verify`.

### Ghidra requirements

- Ghidra is external and not shipped by XeCLI.
- Ghidra is documented as `(Free)` in the CLI. XeCLI does not currently pin a tested Ghidra version in the docs.
- Java must be available for the configured Ghidra runtime.
- `XEXLoaderWV` is required for clean `.xex` imports.
- After `rgh ghidra config --path <dir>`, XeCLI can install the loader helper for you with `rgh ghidra install-loader`.

## IDA Support

### Supported baseline

- Supported IDA build: `IDA Pro 9.1.250226`
- Supported XEX loader build: [`SaveEditors/idaxex 0.42b-compat`](https://github.com/SaveEditors/idaxex/releases/tag/0.42b-compat)
- Do not claim support for `idaxex 0.43` or `IDA 9.2` until that exact combination is validated in XeCLI.

### Current command surface

- `rgh ida config`
- `rgh ida check`
- `rgh ida install-loader`
- `rgh ida analyze`
- `rgh ida decompile`
- `rgh ida verify`
- `rgh xex ida-decompile`

### What XeCLI does for IDA

- Treats IDA Pro as an external headless backend.
- Stores the IDA install path, Python command, `IDAUSR`, and preferred backend.
- Validates the configured runtime with `rgh ida check`.
- Installs the pinned `SaveEditors idaxex 0.42b-compat` loader set with `rgh ida install-loader`.
- Ships the helper scripts in `src/Xbox360.Remote.Cli/ida_scripts/`.
- Imports raw `.xex` files through `idat.exe` batch mode.
- Uses `idalib` for database-backed decompilation when available.
- Supports one-shot console-driven decompilation through `rgh xex ida-decompile`.
- Verifies exported C output with `rgh ida verify`.

### IDA requirements

- IDA Pro is external and not shipped by XeCLI.
- XeCLI documents `IDA Pro 9.1.250226` as the supported baseline.
- A valid local IDA install and license are required for `idat` and `idalib`.
- `SaveEditors idaxex 0.42b-compat` must be present for `.xex` loading.
- Python is required for `idalib` workflows.
- After `rgh ida config --path <dir>`, XeCLI can install the supported loader helper for you with `rgh ida install-loader`.

## Validation Notes

These workflows were validated against a live console on **March 22, 2026**:

- `rgh ida check`
- `rgh ida install-loader`
- `rgh ghidra install-loader`
- `rgh ida analyze --ftp-path /Hdd1/Aurora/Aurora.xex --out-db ...`
- `rgh ida decompile --in ...Aurora-cli.i64 --backend idalib --max 1`
- `rgh ida verify --dir ...`
- `rgh xex ida-decompile --ftp-path /Hdd1/Aurora/Aurora.xex --out ... --max 1 --out-db ... --keep-db`

Observed live IDA import/decompile results for Aurora:

- imported database: `Aurora-cli.i64`
- segments: `7`
- functions: `40129`
- one-function `idalib` export completed successfully
- one-shot `xex ida-decompile` completed successfully with the auto-selected `idalib` backend after batch import
