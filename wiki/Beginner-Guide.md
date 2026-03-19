<p align="center">
  <img src="../assets/readme/xecli-logo.jpg" alt="XeCLI logo" width="280">
</p>

# Beginner Guide

This guide covers the safe, practical path from a fresh install to a working live-console session.

## 1. Know the Minimum Requirements
On the PC side:

- Windows
- no extra runtime if you are using the published `win-x64` release package
- .NET 10 only if you are building or running XeCLI from source
- Network access to the console

On the console side:

- XBDM enabled
- JRPC2 if you want RPC, temps, CPU key, notifications, or Title ID reads
- FTP service if you want FTP-backed save, content, or file workflows

## 2. Start the CLI
If you are using a release build:

```powershell
rgh
```

The published release is self-contained. Extract it, run `rgh.exe`, and XeCLI can install the PATH entry for you. You do not need to install .NET separately for that path.

On first interactive launch, XeCLI can offer to add itself to the machine PATH. Accept that if you want `rgh` available everywhere.

## 3. Discover or Set a Target
Interactive discovery:

```powershell
rgh start
```

Direct targeting:

```powershell
rgh target --set <console-ip>
```

If you use FTP features, set that target too:

```powershell
rgh ftp target --set <console-ip> --user <ftp-user> --pass <ftp-pass>
```

## 4. Confirm the Console Is Reachable
```powershell
rgh ping
rgh status
rgh status --quick
```

Use `status --quick` when you want a fast response and do not need JRPC-backed fields.

## 5. Resolve the Active Title
```powershell
rgh title
rgh title --json
```

`rgh title` with no arguments resolves the active title from the connected console. If the bundled database only knows the base system Title ID, XeCLI can still show a better path-based title name such as `Aurora`.

## 6. Inspect Live State
Modules:

```powershell
rgh modules list
rgh modules info --name Aurora.xex
```

Memory:

```powershell
rgh mem hexdump --addr 0x30000000 --size 0x40
rgh mem peek --addr 0x30000000 --type u32
```

Screenshot:

```powershell
rgh screenshot --out .\screen.bmp
```

## 7. Use FTP-Backed Workflows
Browse files:

```powershell
rgh ftp list --path /Hdd1/
```

List saves for a title:

```powershell
rgh save list --titleid FFFE07D1 --device Hdd1
```

List installed content:

```powershell
rgh content list --device Hdd1 --show-types
```

List DashLaunch plugins:

```powershell
rgh plugin list
```

## 8. Launch and Notify
Launch a XEX:

```powershell
rgh launch Hdd1:\Aurora\Aurora.xex --titleid FFFE07D1
```

Send a notification:

```powershell
rgh notify "XeCLI connected"
```

## 9. Avoid the Common Mistakes
Do not start with dangerous commands until the basic path is stable.

Start with:

- `status`
- `title`
- `modules list`
- `ftp list`
- `screenshot`

Delay these until you know the target is stable:

- `mem poke`
- `mem search --freeze`
- `modules unload --force`
- `modules load --system`
- deletion commands

## 10. Know Where to Go Next
- [Commands Reference](Commands.md) for full command coverage
- [Advanced Guide](Advanced-Guide.md) for reverse-engineering and automation workflows
- [Troubleshooting](Troubleshooting.md) when the console or plugin stack does not behave as expected
