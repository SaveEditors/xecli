# Troubleshooting

This page covers the failures most likely to matter during real RGH/JTAG use.

## Check local XeCLI health
Use `rgh health` when you want to check local XeCLI settings before connecting to a console or before creating a support bundle:

```powershell
rgh health
rgh health --json
```

The command stays local and offline. It does not contact the console. It checks config parsing, saved target/profile data, and the local command log.

Saved target endpoints are redacted by default. Use `rgh health --include-private` only when you need full endpoint text in local output.

Warning statuses are informational and still exit 0. If the command reports a failure, fix the shown local issue first.

If support still needs more context, run `rgh support bundle`, `rgh diagnostics bundle --support`, or `rgh diag bundle` after reviewing the health output.

## Support asks for a support bundle
Use the support command when you need one local package with version, settings, health summary, and command-log context:

```powershell
rgh support bundle
rgh diagnostics bundle --support
rgh diag bundle
rgh support bundle --out .\xecli-support.zip
rgh diagnostics bundle --support --out .\xecli-support.zip
rgh diagnostics bundle --out .\xecli-diagnostics.zip
rgh diagnostics bundle --folder --out .\xecli-diagnostics
```

`rgh diag bundle` is the same command through the short alias. `rgh diagnostics bundle` stays available for compatibility, and `--support` switches that tree onto the support bundle layout.

The bundle includes the support or diagnostics report, `version.json`, redacted `health.json` and configuration data, command-log exports, `release-check.json` for the local package-integrity summary, and `manifest.json`.

The bundle redacts local paths, IP addresses, console IDs, and known secrets. Review the files before sharing because command history can still show workflow context.

## `rgh mem peek`, `rgh mem hexdump`, and `rgh mem dump` disagree
Expected behavior in the current release is that small reads line up across all three commands.

Checks:

- re-read the exact same address and size without changing the title state
- confirm the region is still mapped with `rgh mem regions`
- compare a static buffer before testing a fast-moving game structure

Useful commands:

```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh mem peek --addr 0x30000000 --type u32
rgh mem dump --addr 0x30000000 --size 0x40 --out .\mem.bin
```

If the same small region still disagrees across those commands, treat it as target-side instability or a transport/plugin problem rather than normal CLI behavior.

## `rgh ping` or `rgh status` hangs too long
Expected behavior in the current release is fast failure, not long silent hangs.

Checks:

- confirm the console IP is still reachable
- confirm XBDM is listening on the expected port
- run `rgh target` to verify the saved IP and port

Useful commands:

```powershell
rgh target
rgh ping
rgh status --quick
```

If `status --quick` works and full `status` is slow, the extra time is usually in the FTP 21 check or the optional JRPC2 or user/drive checks rather than XBDM itself.

## Console connection failed, timed out, or did not complete
These messages mean XeCLI could not finish setting or verifying the console target:

- `Console connection failed`
- `Console connection timed out`
- `Console connection did not complete`

Checks:

- confirm the console is powered on and on the same network as the PC
- confirm XBDM is enabled and listening on the expected port
- run `rgh target` to see the saved target
- if the console IP changed, run `rgh connect <console-ip>` before retrying

Useful commands:

```powershell
rgh target
rgh start
rgh connect <console-ip>
rgh ping
rgh status --quick
```

If `rgh start` finds the console but `rgh connect <console-ip>` still fails, check the console IP, firewall, and plugin state before trying slower commands such as full `rgh status`.

## `rgh title` shows a generic system title
That can be correct at the Title ID level while still being unhelpful to an operator.

XeCLI tries to improve that by using the running XEX path as a fallback display name. If you still need a custom name, select a `.csv` or `.txt` file under XeTerminal **Settings > Paths**, set `XECLI_TITLE_DATABASE`, or add `titleids.csv` to the active XeCLI configuration directory. That directory is `%APPDATA%\XeCLI` when installed, `UserData` beside the executables in portable mode, or the directory selected by `XECLI_HOME`. Existing `titleids.local.csv` files continue to work as a legacy fallback.

## `rgh launch` is acknowledged but the title does not change
Plain `rgh launch` confirms only that XBDM accepted the request. Use `--wait` when the active XEX must be proven:

```powershell
rgh launch Hdd1:\Games\Example\default.xex --verify --wait
```

Some active titles deny the directory check used by `--verify`. For a recovery path you already trust, retry once with `--no-verify --wait`. XeCLI still requires the active XEX to change and exits nonzero if XBDM acknowledges the request but the title ignores it.

If the known recovery launch is also ignored, use a verified reboot instead of repeating `magicboot` requests:

```powershell
rgh reboot --yes --wait
```

## JRPC2-backed fields show unavailable or skipped
Possible reasons:

- you used `status --quick`
- JRPC2 is not deployed or enabled on the console yet
- JRPC2 is deployed but not healthy

Useful checks:

```powershell
rgh jrpc2 title-id
rgh jrpc2 dashboard
rgh jrpc2 temps
```

## `modules load` disconnects the console
This is the main reason the pending-verification flow exists.

Use:

```powershell
rgh modules load --path Hdd:\HvP2.xex --system --reboot-expected
```

Then after reboot:

```powershell
rgh modules pending
```

Do not treat a disconnect as success unless the post-reboot state confirms it.

## `modules unload` is rejected
XeCLI requires `--force` because unload can destabilize the target.

Use:

```powershell
rgh modules unload --name HvP2.xex --force
```

If the module handle cannot be resolved:

- confirm the module is actually loaded with `rgh modules list`
- try unloading by explicit handle if you already know it

## Screenshot output is wrong or empty
Check:

- the console is running a visible title or shell
- the frame buffer format is being decoded into the selected output format
- the output file size is reasonable
- if the image is correct except for a small right-edge strip, use `--crop-right` or `--crop-right-percent` for an explicit trim override

A healthy screenshot capture should produce a non-trivial `.png` or `.bmp` file with dimensions matching the decoded frame-buffer metadata emitted by the command.

## Save or content commands return too much data
Use more specific filters:

```powershell
rgh save list --titleid 415608C3 --profile E00012AA8D7879B4
rgh content list --titleid 415608C3
```

Dashboard content trees can be very large if you query them broadly.

## Ghidra decompile output contains placeholder stubs
Run:

```powershell
rgh ghidra verify --dir .\decomp
```

If the output contains widespread bad-instruction placeholders, the problem is usually with import or loader quality rather than a terminal formatting issue.

## FTP-backed commands behave differently from XBDM-backed ones
That is expected. They hit different console services.

If FTP commands fail while XBDM commands succeed:

- verify the FTP service is enabled
- verify username, password, and port
- verify the path format you are using

Read [FTP and File Transfer](FTP-and-File-Transfer) for supported FTP sources, the port 21 listener requirement, and the common signs that the console only has XBDM or JRPC2 available.

## The installer did not prompt the way I expected
Use the published setup executable when you expect the Windows wizard, Start menu entry, optional desktop shortcut, and Windows uninstall integration:

```powershell
.\XeCLI-2.0.0-win-x64-setup.exe
```

`rgh install` is also supported, but it is a separate CLI-driven workflow that copies a verified portable release and configures command access. It does not reproduce the setup wizard or create its shortcuts and uninstall entry. Its prompts are skipped when input is redirected, `--quiet` is used, or scope/path options already define a noninteractive plan.

The setup executable suppresses wizard pages only when launched with Inno Setup silent-install arguments such as `/SILENT` or `/VERYSILENT`. For a normal interactive install, rerun the verified setup package without those arguments. Setup does not scan for consoles. Discover or select a target explicitly after setup:

```powershell
rgh start
rgh connect <console-ip>
rgh status
```

If setup added XeCLI to PATH, run those commands from a newly opened terminal. If PATH registration was skipped, run `rgh.exe` from the installation directory.

## Windows shows Unknown Publisher or a SmartScreen warning

XeCLI v2.0.0 packages are not Authenticode-signed, so Windows may identify the publisher as unknown. Confirm that the package came from the official GitHub release and verify the matching `.sha256` sidecar before allowing it to run:

```powershell
$packagePath = ".\XeCLI-2.0.0-win-x64-setup.exe"
$expectedHash = ((Get-Content "${packagePath}.sha256" -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) { throw "SHA-256 mismatch. Do not run this package." }
"SHA-256 verified: $actualHash"
```

Use the same command with `XeCLI-2.0.0-win-x64.zip` when checking the portable package. A matching checksum verifies the downloaded bytes; it does not create a publisher signature.

## Setup blocks an upgrade, relocation, or ownership check

Setup upgrades an existing XeCLI copy only in the same installation directory. It stops before installing new files when Windows reports another or ambiguous XeCLI location, when the existing copy is missing the ownership manifest needed for a safe upgrade, when the selected folder is a portable package, or when an unrecognized destination already contains files.

Recovery:

1. Uninstall the existing XeCLI entry through Windows. The v2.0.0 uninstaller retains settings, cache, command logs, and default captures.
2. Rerun the new setup package and select the desired installation directory.
3. Open a new terminal if PATH registration changed.

The v1.x uninstaller can leave `.xecli-install` as the sole item in its old program folder. v2.0.0 setup verifies and removes that exact legacy marker automatically. If the folder contains anything else, setup does not modify it; use an empty dedicated directory instead.

Retained settings remain under `%APPDATA%\XeCLI`, cache and the default command log remain under `%LOCALAPPDATA%\XeCLI`, and default XeTerminal captures remain under the Windows Pictures folder at `XeCLI\Captures`. Configured capture/log directories and any `XECLI_HOME` directory are also untouched. Delete retained data manually only when you no longer need it.

Do not point setup at an extracted portable package. Choose an empty dedicated installation folder instead. To update portable XeCLI, verify and extract the new ZIP into a new empty folder, close the old copy, and copy only the old `UserData` folder when you want to retain portable state.

## `rgh` is not found after installation

PATH registration is optional and applies only to new processes. Close any terminal that was already open before setup and open a new one. If PATH registration was skipped, run the executable from the selected installation directory or reinstall with PATH enabled. The default setup directory is `%LOCALAPPDATA%\Programs\XeCLI` for a current-user install and `%ProgramFiles%\XeCLI` for an all-users install.

For a CLI-driven current-user install, `rgh install --no-path` creates a per-user command shim without adding the installation directory itself to PATH. All-users `--no-path` leaves PATH unchanged. See [Commands Reference](Commands#rgh-install) for complete `rgh install` behavior.

## `rgh.exe` says .NET is missing
The published `win-x64` portable release should not require a separate .NET install. If you see a runtime-missing error:

- you are probably using an older framework-dependent build
- or you are running a source build instead of the packaged release

Use the latest release archive from GitHub Releases. The release folder should contain:

- `rgh.exe` and `XeTerminal.exe`
- bundled runtime files beside it
- `Assets/`
- `ghidra_scripts/` and `ida_scripts/`
- `LICENSE`, required third-party notices/licenses, matching-tag source directions under `PROVENANCE/`, and integrity manifests
- the empty `xecli.portable` marker in the portable ZIP only

`ConsoleDependencies/` is not supplied in the release. It appears only if you add permitted console-side plugin files for homebrew staging.

If you are intentionally running from source, install the .NET 10 SDK selected by `global.json` and use:

```powershell
dotnet run --project src/Xbox360.Remote.Cli -- --help
```

## The portable release is not the same as the installer
The zip releases are the portable builds. The setup executable is the installer.

The portable ZIP contains an `xecli.portable` marker beside the executables. While that marker is present, XeCLI stores configuration, cache, command logs, and default captures under the package-local `UserData` folder. Keep the marker with the executables when moving the folder.

`XECLI_HOME` takes precedence over the marker. If it is set, XeCLI stores application state in that custom directory instead of package-local `UserData`.

Portable use makes no installation changes by default. You can explicitly run `.\rgh.exe install` from the extracted release to copy it into a current-user or all-users installation and configure command access. `rgh install --uninstall` removes that CLI registration but does not delete the installed folder or user data.

Use the setup executable when you want:

- PATH registration
- XeCLI Start menu integration
- optional XeCLI desktop shortcut creation
- uninstall integration
- the installer-owned language selection step

Install and same-directory upgrade keep existing `UiLanguage` and `PathPromptHandled` settings. If `config.json` is malformed, setup leaves it untouched instead of replacing the file.

Uninstall always retains settings under `%APPDATA%\XeCLI`, cache and the default command log under `%LOCALAPPDATA%\XeCLI`, and default XeTerminal captures under the Windows Pictures folder at `XeCLI\Captures`. Configured capture/log directories and any `XECLI_HOME` directory are also retained. Remove these locations manually only when you no longer need them.

Use the zip release when you want:

- a portable folder you can run directly
- no installation changes on the machine
- manual control over where the files live

XeCLI v2.0.0 publishes one supported portable package for 64-bit Windows: `XeCLI-2.0.0-win-x64.zip`.
