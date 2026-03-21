# Hardware and System Controls

XeCLI exposes a small set of hardware and session controls that are useful during daily RGH/JTAG work:

- sign-in state inspection
- ring-of-light LED control
- manual fan command dispatch
- SMC version probing
- disc tray open/close
- trainer-style message-box popups

This page explains what those commands do, how they are intended to be used, and what their current practical limits are.

## Command Overview

```powershell
rgh signin state
rgh led set --preset quadrant1
rgh led state
rgh fan set --speed 55 --channel both
rgh fan show
rgh smc version
rgh tray open
rgh popup show --title "XeCLI" --body "Connected"
```

## Signed-In Session State
### `rgh signin state`

Reads the active XAM sign-in slot and resolves:

- whether a user is signed in
- the sign-in state
- the current gamertag
- the current XUID
- the user slot

Examples:

```powershell
rgh signin state
rgh signin state --json
```

Representative output:

```text
Signed In   Yes
State       Signed in locally
Gamertag    Diamond KSG
XUID        0x5D83300C00000900
Slot        0
```

Sign-in state meanings:

| Value | Meaning |
| --- | --- |
| `0` | Not signed in |
| `1` | Signed in locally |
| `2` | Signed in to Xbox Live |

This command is the cleanest way to answer the common questions:

- is anyone signed in right now?
- which profile is active?
- what XUID is attached to the current session?

## Ring-of-Light LED Control
### `rgh led set`

Sets the four ring-of-light quadrants through the console’s RPC path.

You can use:

- a preset
- explicit per-quadrant colors
- optional success notification flags

Examples:

```powershell
rgh led set --preset quadrant1
rgh led set --preset all-green
rgh led set --tl green --tr off --bl off --br off
rgh led set --preset all-orange --notify
```

Supported colors:

- `off`
- `green`
- `red`
- `orange`

Built-in presets:

- `all-green`
- `all-red`
- `all-orange`
- `all-off`
- `quadrant1`
- `quadrant2`
- `quadrant3`
- `quadrant4`
- `player1`
- `player2`
- `player3`
- `player4`

Representative output:

```text
SUCCESS Ring light updated
TL=green, TR=off, BL=off, BR=off
```

### `rgh led state`

Shows the last ring-light state requested through XeCLI.

Examples:

```powershell
rgh led state
rgh led state --json
```

Representative output:

```text
Source        Last XeCLI-applied ring-light state
Preset        quadrant1
Top Left      green
Top Right     off
Bottom Left   off
Bottom Right  off
Updated       08:45:30
```

Important note:

- `led state` is a local cache of the last XeCLI-applied state
- it is not a hardware readback from the SMC

That distinction matters after a reboot or if another tool changes the LEDs.

## Manual Fan Control
### `rgh fan set`

Dispatches a manual fan-speed command through the console’s SMC RPC path.

Examples:

```powershell
rgh fan set --speed 55 --channel both
rgh fan set --speed 45 --channel primary
rgh fan set --speed 60 --channel secondary --notify
```

Channel values:

- `primary`
- `secondary`
- `both`

Representative output:

```text
SUCCESS Fan command sent
50% requested for both
```

Practical notes:

- XeCLI clamps the requested value to `10-100`
- current fan control is a command-dispatch workflow, not a tachometer readback workflow
- `fan show` reports the last XeCLI-requested manual setting, not a live measured RPM

### `rgh fan show`

Shows the last manual fan setting requested through XeCLI.

Examples:

```powershell
rgh fan show
rgh fan show --json
```

Representative output:

```text
Source   Last XeCLI-applied manual setting
Speed    50%
Channel  both
Updated  09:12:04
```

## SMC Version
### `rgh smc version`

Attempts to query the console’s SMC version.

Examples:

```powershell
rgh smc version
rgh smc version --json
```

When the installed console-side plugin stack exposes the required SMC path cleanly, the command returns a simple version string.

Representative success output:

```text
SUCCESS SMC version
2.3
```

Operational reality:

- SMC version probing is more plugin-stack-sensitive than normal JRPC title/temperature calls
- if the installed JRPC/XBDM stack does not expose a usable SMC reply path, XeCLI reports the failure instead of fabricating a value
- `rgh status` treats SMC version as optional and does not let an SMC failure poison the rest of the status output

## Disc Tray Control
### `rgh tray open`
### `rgh tray close`

Open or close the physical optical tray through the console-side XAM/JRPC path.

Examples:

```powershell
rgh tray open
rgh tray close
```

Representative output:

```text
SUCCESS Disc tray opened
```

```text
SUCCESS Disc tray closed
```

Use this when you need quick hardware confirmation that the console is responding to control calls beyond plain XBDM connectivity.

## Native Message Boxes
### `rgh popup show`

Shows a native Xbox 360 popup. This is the same UI family trainers commonly use for full-screen prompts and instruction popups.

Examples:

```powershell
rgh popup show --title "XeCLI" --body "Connected to console"
rgh popup show --title "Warning" --body "Reboot required" --preset warning
rgh popup show --title "Question" --body "Continue?" --preset question
rgh popup show --title "Raw Style" --body "Test" --style 3
```

Preset values:

- `none`
- `error`
- `warning`
- `question`

Representative output:

```text
SUCCESS Popup requested
Title="XeCLI" Preset=warning Buttons=1
```

Operational notes:

- `popup show` is different from `rgh notify`
- `notify` sends a toast in the XNotify queue
- `popup show` blocks the foreground UI until the user dismisses it
- use `--preset none` when you want the cleaner look without the red error icon

## Status Integration

`rgh status` and `rgh status --json` surface the session fields directly:

- `Signed In`
- `Sign-In State`
- `Gamertag`
- `Signed In XUID`
- `SMC Version` when available

That means you can use `status` for the dashboard-level snapshot and `signin state` when you want the session details isolated.

## Recommended Operator Usage

Use the commands in this order:

```powershell
rgh ping
rgh status --quick
rgh signin state
rgh led set --preset quadrant1
rgh notify "XeCLI connected" 14
```

Then move to the less routine controls:

```powershell
rgh fan set --speed 55 --channel both
rgh smc version
```

That sequencing keeps the basic transport validation separate from the hardware/session controls.
