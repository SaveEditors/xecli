# Troubleshooting

This page covers the failures most likely to matter during real RGH/JTAG use.

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

If `status --quick` works and full `status` is slow, the problem is usually in optional JRPC or user/drive probes rather than XBDM itself.

## `rgh title` shows a generic system title
That can be correct at the Title ID level while still being unhelpful to an operator.

XeCLI tries to improve that by using the running XEX path as a fallback display name. If you still need a custom name, add an entry to:

- `%APPDATA%\XeCLI\titleids.local.csv`

## JRPC-backed fields show unavailable or skipped
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

Do not treat a disconnect as proof of success unless the post-reboot state confirms it.

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

A healthy BMP capture should produce a non-trivial file with dimensions matching the decoded frame-buffer metadata emitted by the command.

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

If the output contains widespread bad-instruction placeholders, the problem is usually with import/loader quality rather than a terminal formatting issue.

## FTP-backed commands behave differently from XBDM-backed ones
That is expected. They hit different console services.

If FTP commands fail while XBDM commands succeed:

- verify the FTP service is enabled
- verify username, password, and port
- verify the path format you are using

## The installer did not prompt the way I expected
Use `rgh install` from a normal interactive terminal.

The full installer wizard is intentionally skipped when:

- input is redirected
- you passed explicit install flags such as `--path`, `--machine`, or `--no-path`
- you are running a scripted or quiet install

Manual fallback:

```powershell
rgh install
```

If you want a specific location without the wizard:

```powershell
rgh install --path C:\Tools\XeCLI
```

If the post-install console prompt did not appear, XeCLI either did not detect a console fast enough or you skipped it. Use:

```powershell
rgh start
rgh connect <console-ip>
rgh status
```

## `rgh.exe` says .NET is missing
The published `win-x64` release should not require a separate .NET install. If you see a runtime-missing error:

- you are probably using an older framework-dependent build
- or you are running a source build instead of the packaged release

Use the latest release archive from GitHub Releases. The release folder should contain:

- `rgh.exe`
- bundled runtime files beside it
- `ConsoleDependencies/`
- `Assets/`
- `ghidra_scripts/`

If you are intentionally running from source, install the .NET 10 SDK/runtime and use:

```powershell
dotnet run --project src/Xbox360.Remote.Cli -- --help
```
