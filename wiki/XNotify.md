# XNotify

XNotify is the Xbox 360 on-screen notification system used for sign-in prompts, achievements, storage warnings, party/chat events, and other shell-level status messages.

XeCLI exposes XNotify through the `rgh notify`, `rgh notify-icons`, and `rgh jrpc2 notify` commands so you can send operator messages directly to the console without opening a separate tool.

At the practical level, every XNotify workflow comes down to three inputs:

- message text
- icon/logo identifier
- a healthy notification-capable JRPC/XDRPC path on the console

## What XeCLI Supports

XeCLI supports:

- direct message sends with a numeric icon ID
- direct message sends with a built-in icon name
- reusable icon presets for operator shortcuts
- the same icon mapping through both `notify` and `jrpc2 notify`

Examples:

```powershell
rgh notify "XeCLI connected"
rgh notify "XeCLI connected" 14
rgh notify --message "XeCLI connected" --logo 14
rgh notify --message "Build finished" --icon success
rgh jrpc2 notify "Module load complete" 27
rgh notify-icons list
rgh notify-icons add --name success --logo 14
```

Representative output:

```text
SUCCESS Notification sent
message="XeCLI connected" logo=14 (Flashing happy face)
```

## Why XNotify Matters

In real RGH/JTAG workflows, XNotify is useful for:

- confirming that a long-running action finished while you are looking at the console
- validating that JRPC2 is alive and responding
- confirming trainer, module, save, or plugin operations without manually checking files first
- building custom tooling that gives visible operator feedback during scripted workflows

## What XNotify Actually Is

XNotify is the shell popup system built into the Xbox 360 dashboard environment.

It is the same notification family used for:

- sign-in and sign-out messages
- achievement and avatar award toasts
- storage and controller warnings
- party/chat prompts
- download and update notices

That is why the icon list contains shell-event names instead of generic app icons. XeCLI is not inventing a new notification framework. It is exposing the console’s existing one in a terminal-friendly way.

## Command Surface

### `rgh notify`
Operator-facing notification command.

Examples:

```powershell
rgh notify "XeCLI connected"
rgh notify "XeCLI connected" 14
rgh notify --message "XeCLI connected" --logo 14
rgh notify --message "Upload complete" --icon success
```

Notes:

- the second positional argument can be a numeric icon ID
- `--logo` accepts decimal or `0x` hex
- `--icon` resolves a named preset from your local config
- if no icon is provided, XeCLI uses its default notification path

### `rgh notify-icons list`
Shows the built-in icon catalog and any saved presets.

Examples:

```powershell
rgh notify-icons list
rgh notify-icons list --json
```

### `rgh notify-icons show`
Resolves one icon by name or number.

Examples:

```powershell
rgh notify-icons show 14
rgh notify-icons show achievement
rgh notify-icons show avatar-award
```

### `rgh notify-icons add`
Creates a reusable local alias.

Examples:

```powershell
rgh notify-icons add --name success --logo 14
rgh notify-icons add --name achievement --logo 27
rgh notify-icons add --name avatar --logo 60
```

### `rgh notify-icons remove`
Removes a saved alias.

Examples:

```powershell
rgh notify-icons remove --name success
```

### `rgh jrpc2 notify`
Lower-level JRPC2-backed notification send. Use this if you want to stay inside the `jrpc2` namespace for scripting or RPC-focused workflows.

Examples:

```powershell
rgh jrpc2 notify "XeCLI connected"
rgh jrpc2 notify "XeCLI connected" 14
```

## Numeric Icon Usage

The most direct form is:

```powershell
rgh notify "XeCLI connected" 14
```

That means:

- message text = `XeCLI connected`
- logo/icon id = `14`

This is the fastest operator form when you already know the ID you want.

## Direct RPC Usage Notes

If you are building tooling outside XeCLI, the lower-level shape is still:

- message text
- icon/logo ID
- dispatch through the console notification RPC path

XeCLI already normalizes the useful operator forms:

```powershell
rgh notify "XeCLI connected" 14
rgh jrpc2 notify "XeCLI connected" 14
rgh notify --message "XeCLI connected" --logo 14
```

That keeps users away from raw command framing while still matching the way other JRPC/XDRPC-capable tools think about XNotify.

## Built-In Icon Reference

Common operator-friendly icons:

| ID | Name | Notes |
| --- | --- | --- |
| `0` | Xbox logo | Default Xbox sphere/logo |
| `4` | Flashing Xbox logo | Strong generic system notification |
| `12` | Download | Good for transfer status |
| `14` | Flashing happy face | Best default success icon |
| `15` | Flashing frowning face | Error or warning |
| `16` | Flashing double-sided hammer | Tooling / patch / work icon |
| `27` | Achievement unlocked | Completion or unlock events |
| `34` | Flashing Xbox console | Console/system-level operation |
| `38` | Flashing chat icon | Communication or session notice |
| `42` | Blank | Message with effectively no icon |
| `55` | Downloaded | Completed transfer state |
| `59` | Gamer picture unlocked | Profile/player-oriented status |
| `60` | Avatar award unlocked | Avatar/profile event |
| `65` | Flashing chat symbol | Chat/event style notice |
| `76` | Updating | Update/install/progress completion |

Broader built-in catalog:

| ID | Name |
| --- | --- |
| `0` | Xbox logo |
| `1` | New message logo |
| `2` | Friend request logo |
| `3` | New message |
| `4` | Flashing Xbox logo |
| `5` | Gamertag sent you a message |
| `6` | Gamertag signed out |
| `7` | Gamertag signed in |
| `8` | Gamertag signed into Xbox Live |
| `9` | Gamertag signed in offline |
| `10` | Gamertag wants to chat |
| `11` | Disconnected from Xbox Live |
| `12` | Download |
| `13` | Flashing music symbol |
| `14` | Flashing happy face |
| `15` | Flashing frowning face |
| `16` | Flashing double-sided hammer |
| `17` | Gamertag wants to chat 2 |
| `18` | Please reinsert memory unit |
| `19` | Please reconnect controller |
| `20` | Gamertag has joined chat |
| `21` | Gamertag has left chat |
| `22` | Game invite sent |
| `23` | Flash logo |
| `24` | Page sent to |
| `25` | Reserved / unknown 25 |
| `26` | Reserved / unknown 26 |
| `27` | Achievement unlocked |
| `28` | Reserved / unknown 28 |
| `29` | Gamertag wants to talk in video Kinect |
| `30` | Video chat invite sent |
| `31` | Ready to play |
| `32` | Cannot download X |
| `33` | Download stopped for X |
| `34` | Flashing Xbox console |
| `35` | X sent you a game message |
| `36` | Device full |
| `37` | Reserved / unknown 37 |
| `38` | Flashing chat icon |
| `39` | Achievements unlocked |
| `40` | X has sent you a nudge |
| `41` | Messenger disconnected |
| `42` | Blank |
| `43` | Cannot sign in messenger |
| `44` | Missed messenger conversation |
| `45` | Family timer X time remaining |
| `46` | Disconnected Xbox Live 11 minutes remaining |
| `47` | Kinect health effects |
| `48` | Reserved / unknown 48 |
| `49` | Gamertag wants you to join an Xbox Live party |
| `50` | Party invite sent |
| `51` | Game invite sent to Xbox Live party |
| `52` | Kicked from Xbox Live party |
| `53` | Nulled |
| `54` | Disconnected Xbox Live party |
| `55` | Downloaded |
| `56` | Cannot connect Xbox Live party |
| `57` | Gamertag has joined Xbox Live party |
| `58` | Gamertag has left Xbox Live party |
| `59` | Gamer picture unlocked |
| `60` | Avatar award unlocked |
| `61` | Joined Xbox Live party |
| `62` | Please reinsert USB storage device |
| `63` | Player muted |
| `64` | Player unmuted |
| `65` | Flashing chat symbol |
| `76` | Updating |

## Suggested Operator Patterns

Use a small predictable icon set instead of choosing a random logo each time.

Recommended mapping:

- `14` for success
- `15` for failure
- `12` or `55` for transfer/download events
- `16` for patch/tool actions
- `27` for major completion milestones
- `42` for plain-text messages when you want the icon hidden

Examples:

```powershell
rgh notify "Save upload complete" 14
rgh notify "Transfer failed" 15
rgh notify "Downloading title update" 12
rgh notify "Patch applied" 16
rgh notify "Decompile finished" 27
```

## Direct Integration Notes

XeCLI is a command-line wrapper around the same practical workflow you would use in another JRPC/XDRPC-capable tool: send message text plus a numeric icon ID to the console’s notification path.

If you are building your own tool:

- keep the icon ID mapping local so your UI can show readable names
- expose both numeric and named icon selection
- use visible completion/failure notifications for long-running actions
- do not assume every console/plugin stack has the same RPC health

The important practical contract is:

- message text
- icon/logo ID
- working JRPC2/XDRPC-style notification call path

## Troubleshooting

If notifications do not appear:

- confirm JRPC2 is loaded and healthy
- confirm your console is signed into a shell/session where notifications are visible
- test with a simple known icon first, such as `14`
- test the lower-level path directly with `rgh jrpc2 notify`

Useful checks:

```powershell
rgh notify "XeCLI connected" 14
rgh jrpc2 notify "XeCLI connected" 14
rgh notify-icons show 14
rgh notify-icons list
```
