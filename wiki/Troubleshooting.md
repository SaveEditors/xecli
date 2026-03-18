# Troubleshooting

This page covers the failure cases most users hit during setup, live debugging, file access, and XEX analysis.

## Discovery Finds Nothing
Possible causes:
- The console is not on the same LAN
- XBDM is not running
- Discovery traffic is blocked

What to do:
```bash
rgh scan
rgh target --set <console-ip>
rgh ping
```

If direct targeting works but discovery does not, the problem is usually broadcast visibility rather than XeCLI itself.

## XBDM Connect Fails
What to check:
- Correct IP
- Correct port
- XBDM enabled
- No local firewall rule blocking outbound traffic

Useful command:
```bash
rgh ping
```

## Status Is Slow
`rgh status` can be slower when it gathers drive, user, FTP, and JRPC2 data.

Use:
```bash
rgh status --quick
```

Or selectively skip sources:
```bash
rgh status --no-jrpc --no-users --no-drives
```

## JRPC2 Commands Fail
If `jrpc2` commands fail while XBDM commands work, the issue is usually plugin availability rather than network transport.

What to do:
- Confirm JRPC2 is installed and active on the console
- Re-run `rgh status` and check the JRPC2 field

## FTP Lists the Wrong Directory
Some FTP implementations on Xbox 360 setups return unexpected root listings.

Try:
```bash
rgh ftp list --path "/Hdd1/"
```

If needed, use full normalized paths consistently.

## `xex dump` Works but Ghidra Results Are Poor
Possible causes:
- Wrong loader setup in Ghidra
- Reused stale project state
- You imported the wrong file

What to do:
```bash
rgh ghidra config --path "<ghidra-dir>" --java "<java-home>"
rgh ghidra decompile --in .\\title.xex --out .\\decomp --delete-project
rgh ghidra verify --dir .\\decomp
```

## Decompiled Output Contains Placeholder Functions
If decompiled files contain placeholder output such as bad-instruction stubs, verify:
- You are analyzing a real XEX, not a memory dump
- Ghidra has the loader support you expect
- The import was clean

Use:
```bash
rgh ghidra verify --dir .\\decomp
```

## Screenshots Look Corrupted
Try:
```bash
rgh screenshot --out .\\screen.bmp --format bmp
```

If corruption remains:
- Capture from a stable screen, not a transition
- Retry after returning to a simpler UI screen

## `mem find` Is Unreliable on Large Ranges
Large live searches can stress XBDM responses.

Better workflow:
```bash
rgh mem dump --addr 0x82000000 --size 0x400000 --out .\\range.bin
```

Then search offline with your preferred tooling.

## Title Does Not Resolve to a Name
Possible causes:
- The title is custom or homebrew
- The bundled database lacks that entry
- Media-specific matching is needed

What to do:
- Run `rgh title <title-id>`
- Add an override entry to `%APPDATA%\XeCLI\titleids.local.csv`
- Check the bundled CSV for matching media IDs

## Release Packaging Questions
If you are preparing a public release:
- Keep documentation limited to `README.md` and `wiki/`
- Keep bundled assets in `src/Xbox360.Remote.Cli/Assets`
- Avoid machine-specific IPs and local drive paths in docs or examples
- Prefer placeholders like `<console-ip>`, `<ftp-user>`, and `<output-dir>`


