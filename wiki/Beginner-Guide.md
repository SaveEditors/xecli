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

Direct plugin downloads from the XeCLI repo:

- [Download `xbdm.xex`](https://github.com/SaveEditors/xecli/raw/main/xbdm.xex)
- [Download `XDRPC.xex`](https://github.com/SaveEditors/xecli/raw/main/XDRPC.xex)
- [Download `JRPC2.xex`](https://github.com/SaveEditors/xecli/raw/main/JRPC2.xex)

Use these if you want the exact plugin files bundled with XeCLI instead of sourcing them separately.

## 2. Run the Installer
If you are using the published release build:

```powershell
.\rgh.exe install
```

The published release is self-contained. You do not need to install .NET separately for that path.

Run that command from the extracted release folder that contains `rgh.exe`.

The installer now walks through:

- install scope: current user or all users
- install directory selection
- PATH registration for new terminals

After setup finishes, XeCLI silently scans for consoles. If one console is detected, it can ask whether to connect immediately. If multiple consoles are detected, it lists them and asks you to choose one. When you confirm, XeCLI runs `rgh status` on the selected console.

After installation completes, open a new terminal and use:

```powershell
rgh --help
rgh status
```

## 3. Discover or Set a Target Manually
If you skipped the post-install connection prompt, use the manual discovery path:

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

If you accepted the installer’s connect prompt, XeCLI already ran `rgh status` for the selected console. Use these commands any time you want to recheck reachability or switch back to a manual verification flow.

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
rgh notify "XeCLI connected" 14
```

## 9. Check Session and Hardware State
Signed-in user:

```powershell
rgh signin state
```

Ring light:

```powershell
rgh led set --preset quadrant1
rgh led state
```

If you want the hardware/session details explained in depth, read [Hardware-and-System.md](Hardware-and-System.md).

## 10. Avoid the Common Mistakes
Do not start with dangerous commands until the basic path is stable.

Start with:

- `status`
- `title`
- `modules list`
- `ftp list`
- `screenshot`
- `signin state`
- `led set --preset quadrant1`

Delay these until you know the target is stable:

- `mem poke`
- `mem search --freeze`
- `modules unload --force`
- `modules load --system`
- `fan set`
- deletion commands

## 11. Know Where to Go Next
- [Commands Reference](Commands.md) for full command coverage
- [Hardware and System Controls](Hardware-and-System.md) for sign-in, LED, fan, and SMC behavior
- [XNotify](XNotify.md) for icon IDs and notification usage
- [Advanced Guide](Advanced-Guide.md) for reverse-engineering and automation workflows
- [Troubleshooting](Troubleshooting.md) when the console or plugin stack does not behave as expected
