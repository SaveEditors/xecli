# Remote Spoofing

XeCLI's spoof helpers are title-aware memory tools. They are not generic trainer writes and they do not touch the console profile container or Xbox Live account data. They only change the values the running title is currently reading.

The local source tree is the canonical behavior model for these docs. The shipped release is expected to mirror the same command names, layouts, and verified title-specific constraints.

This page covers the currently implemented spoof surface:

- local in-game gamertag spoofing
- local in-game XUID spoofing
- remote/lobby-player text spoofing
- restore/reset helpers

## BO2 Support Summary

Black Ops II is the title most people ask about, so its current state should be explicit:

| BO2 operation | Status | Notes |
| --- | --- | --- |
| `rgh spoof gt` | Supported | Verified live. Updates the visible local name surfaces and keeps BO2's local XUID surfaces intact. |
| `rgh spoof remote` | Supported | Verified live. Writes BO2 remote/lobby slots 2-12 only. |
| `rgh spoof reset` | Supported for GT/remote restore | Restores the cached or explicit local identity and can clear BO2 remote slots, but can also hand the session back out of BO2 while the title rebinds identity state. |
| `rgh spoof xuid set` | Intentionally blocked | Full BO2 XUID/account spoof is not exposed because the deeper account/profile path is not safe enough yet. |

If you are in BO2 and need a stable spoof workflow today, use `rgh spoof gt` for the local player label and `rgh spoof remote` for lobby-slot text.

## Command Surface

| Canonical command | Compatibility aliases | Purpose |
| --- | --- | --- |
| `rgh spoof gt` | `rgh spoof gamertag`, `rgh spoof name` | Read or write the local in-game gamertag block |
| `rgh spoof xuid` | - | Read or write the local in-game XUID block |
| `rgh spoof remote` | - | Read or write the title's remote/lobby slot text |
| `rgh spoof reset` | `rgh spoof restore` | Restore the in-game identity from the signed-in user or explicit values |

The `show`/`state` and `set`/`apply` subcommands are also accepted by the `gt`, `xuid`, and `remote` branches.

## Supported Titles

Current spoof profiles are title-aware and limited to the following games:

| Game | Title ID | Local GT | Local XUID | Remote Slots |
| --- | --- | --- | --- | --- |
| Call of Duty: Black Ops | `0x41560855` | Yes | Yes | Yes |
| Call of Duty: Modern Warfare 3 | `0x415608CB` | Yes | Yes | Yes |
| Call of Duty: Black Ops II | `0x415608C3` | Yes | Disabled | Yes, slots 2-12 |
| Call of Duty 4: Modern Warfare | `0x415607E6` | Yes | Yes | No |
| Call of Duty: World at War | `0x4156081C` | Yes | Yes | No |
| Call of Duty: Modern Warfare 2 | `0x41560817` | Yes | Yes | No |
| Call of Duty: Ghosts | `0x415608FC` | Yes | Yes | No |
| Call of Duty: Advanced Warfare | `0x41560914` | Yes | Yes | No |

If the running title is not one of these profiles, XeCLI refuses the request instead of blindly writing memory.

## `rgh spoof gt`

Use this command to read or change the local in-game gamertag block for the running supported title.

Read the current value:

```powershell
rgh spoof gt
rgh spoof gt show
```

Apply a new value:

```powershell
rgh spoof gt ExampleTag
rgh spoof gt set --value ExampleTag
rgh spoof gamertag ExampleTag
```

Use the currently signed-in user:

```powershell
rgh spoof gt set --current-user
```

Send a success notification after the write:

```powershell
rgh spoof gt set --value ExampleTag --notify
```

Example output:

```text
SUCCESS Gamertag spoof applied
ExampleTag
```

### Verified limitations

- The local gamertag path is title-specific.
- BO2 updates the visible ASCII and Unicode name blocks and preserves the live local XUID surfaces.
- BO2 also updates a deeper local account block so the in-title identity state stays consistent during a gamertag-only spoof.
- If you only change the remote/lobby slots, the local in-game name can remain unchanged.

## `rgh spoof xuid`

Use this command to read or change the local in-game XUID block for the running supported title.

Read the current value:

```powershell
rgh spoof xuid
rgh spoof xuid show
```

Apply a new value:

```powershell
rgh spoof xuid 5D83300C00000900
rgh spoof xuid set --value 5D83300C00000900
```

Use the currently signed-in user XUID:

```powershell
rgh spoof xuid set --current-user
```

Send a success notification after the write:

```powershell
rgh spoof xuid set --value 5D83300C00000900 --notify
```

Example output:

```text
Game            Call of Duty: Black Ops II
XUID            5D83300C00000900
Stored          000900000C30835D
Binary Addr     0x841E1B50
Text Addr       0x841E1B58
```

### Verified limitations

- The XUID path is title-specific.
- BO2 `show` is supported, but BO2 `set` is intentionally blocked because the deeper account/profile path is not yet safe enough for release use.
- BO2 still preserves the local binary and text XUID surfaces during a gamertag-only spoof.
- `--current-user` requires a signed-in user to be available.

Example BO2 refusal:

```text
BO2 XUID spoof is disabled: use `rgh spoof gt` for local-name spoofing and `rgh spoof remote` for lobby-slot spoofing.
```

## `rgh spoof remote`

Use this command to overwrite the remote/lobby player text slots exposed by supported titles.

This is not the same thing as changing the local player label in the running game.

List the currently visible remote slots:

```powershell
rgh spoof remote list
rgh spoof remote show
```

Apply one value across every supported slot:

```powershell
rgh spoof remote XeCliRemote
rgh spoof remote apply --all --text XeCliRemote
```

Apply a single slot:

```powershell
rgh spoof remote apply --slot 2 --text HostName
```

Inject slot numbers automatically:

```powershell
rgh spoof remote apply --all --text XeCLI-{slot}
```

Send a success notification after the write:

```powershell
rgh spoof remote apply --all --text XeCliRemote --notify
```

Example output:

```text
SUCCESS Remote spoof applied
Call of Duty: Black Ops II => XeCliRemote

Slot  Verified       Address
2     XeCliRemote    0x841E7334
...
12    XeCliRemote    0x8421E2E4
```

### Verified limitations

- Remote spoofing only exists on the titles that expose lobby/player slot text.
- BO1 and MW3 expose remote slots, but the slot layout is title-specific.
- BO2 exposes eleven remote slots and XeCLI reports them as slots 2-12.
- Remote spoofing does not change the local in-game gamertag block.

## `rgh spoof reset`

Use this command to restore the in-game identity from the signed-in user or from explicit values.

Restore from the currently signed-in user:

```powershell
rgh spoof reset --current-user
```

Restore from explicit values:

```powershell
rgh spoof reset --gamertag Diamond KSG --xuid 5D83300C00000900
```

Restore the local identity and clear the supported remote slots:

```powershell
rgh spoof reset --current-user --clear-remote
```

Example output:

```text
SUCCESS Spoof reset applied
Local identity restored and remote slots cleared
```

### Verified limitations

- `--clear-remote` only clears the supported remote slots for the current title.
- `--current-user` requires a signed-in user to be available.
- On BO2, `reset` is for restoring the supported GT/remote spoof state. It is not a full account handoff feature.
- On the live validation box, BO2 `reset --clear-remote` restored the local identity but handed the session back out to Aurora instead of keeping BO2 in-place.

## BO2 Address Map

Black Ops II is the most complete spoof target and uses multiple local memory paths:

| Field | Address / Value |
| --- | --- |
| Local gamertag ASCII | `0x841E1B30` |
| Local XUID binary | `0x841E1B50` |
| Local XUID text | `0x841E1B58` |
| Secondary local name | `0x81B69E94` |
| Unicode name block | `0x81AA2DDC` |
| Refresh patch | `0x825DE240` (`60 00 00 00`) |
| Primary BO2 stub | `0x81B69F80` |
| Secondary BO2 stub | `0x816DD040` |
| Remote player base | `0x841DC690` |
| Remote player stride | `22520` bytes |
| Remote display offset | `21676` bytes |
| Remote mirror offset | `21812` bytes |
| Remote slot count | `11` |
| Remote start slot | `1` |

What this BO2 map is for:

- `rgh spoof gt` changes the local in-game BO2 name surfaces.
- `rgh spoof remote` changes the remote/lobby slot text only.
- `rgh spoof xuid show` can read BO2's visible XUID surfaces.
- `rgh spoof xuid set` remains intentionally blocked on BO2.

## Live Validation Notes

XeCLI verifies the write path by reading the same memory back after the write.

That means:

- XeCLI verifies the addresses changed
- XeCLI verifies the stored remote-slot text matches the requested value
- final visual confirmation in the actual game UI is still useful when testing a new title/session setup

Example BO2 validation flow:

```powershell
rgh launch "Hdd1:\Games\Call of Duty Black Ops II\default_mp.xex" --titleid 415608C3
rgh spoof gt NobodyEpic --notify
rgh spoof xuid show
rgh spoof xuid 1111111111111111
rgh spoof remote NobodyEpic --notify
rgh spoof remote list
rgh spoof reset --clear-remote
```

## Related Pages

- [Commands Reference](Commands.md)
- [Hardware and System Controls](Hardware-and-System.md)
- [XNotify](XNotify.md)
