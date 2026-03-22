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
| `rgh spoof gt` | Supported | Verified live. Updates the visible local name surfaces and the known BO2 local account/persona caches while keeping the title's local XUID surfaces aligned. |
| `rgh spoof remote` | Supported | Verified live. Writes BO2 remote/lobby slots 2-12 using the current live slot layout. |
| `rgh spoof reset` | Supported | Restores the cached or explicit local identity, reapplies the supported BO2 local cache set, and can clear BO2 remote slots. |
| `rgh spoof xuid set` | Supported with BO2 readiness gating | Verified live once BO2's multiplayer/profile path is active. Updates the local XUID surfaces and the known BO2 account/persona caches, but it is still only a title-local memory spoof. |

If you are in BO2, let the game reach the multiplayer front-end or a live lobby before using `rgh spoof xuid set` or `rgh spoof reset`. These commands only change BO2's in-title identity state. They do not replace the signed-in console profile or Xbox Live account.

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
| Call of Duty: Black Ops II | `0x415608C3` | Yes | Yes | Yes, slots 2-12 |
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
rgh spoof xuid <xuid>
rgh spoof xuid set --value <xuid>
```

Use the currently signed-in user XUID:

```powershell
rgh spoof xuid set --current-user
```

Send a success notification after the write:

```powershell
rgh spoof xuid set --value <xuid> --notify
```

Example output:

```text
Game            Call of Duty: Black Ops II
XUID            <xuid>
Stored          <stored-xuid>
Binary Addr     0x841E1B50
Text Addr       0x841E1B58
```

### Verified limitations

- The XUID path is title-specific.
- On BO2, both `show` and `set` are supported once the title has bound its multiplayer/profile path.
- BO2 updates the local binary and text XUID surfaces plus the known local account/persona blocks used by the title.
- This is still a title-local memory spoof. It does not replace the signed-in console profile, Xbox Live auth state, or multiplayer entitlement checks.
- `--current-user` requires a signed-in user to be available.

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
rgh spoof reset --gamertag <gamertag> --xuid <xuid>
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
- On BO2, `reset` restores the supported local GT/XUID/cache spoof state and can also clear the remote slots.
- BO2 `reset` is not a full account handoff or Xbox Live sign-in change.

## BO2 Address Map

Black Ops II is the most complete spoof target and uses multiple local memory paths:

| Field | Address / Value |
| --- | --- |
| Local gamertag ASCII | `0x841E1B30` |
| Local XUID binary | `0x841E1B50` |
| Local XUID text | `0x841E1B58` |
| Secondary local name | `0x81B69E94` |
| Unicode name block | `0x81AA2DDC` |
| Account/persona wide name | `0x81AA28FC` |
| Account/persona wide XUID | `0x81AA291C` |
| Account/persona text XUID | `0x81AEE9EC` |
| Account/persona text name | `0x81AEE9FC` |
| Refresh patch | `0x825DE240` (`60 00 00 00`) |
| Primary BO2 stub | `0x81B69F80` |
| Secondary BO2 stub | `0x816DD040` |
| Remote player base | `0x841DC690` |
| Remote player stride | `22520` bytes |
| Remote display offset | `0` bytes |
| Remote XUID binary offset | `32` bytes |
| Remote XUID text offset | `40` bytes |
| Remote mirror offset | `136` bytes |
| Remote slot count | `11` |
| Remote start slot | `1` |

What this BO2 map is for:

- `rgh spoof gt` changes the local in-game BO2 name surfaces and known local persona caches.
- `rgh spoof xuid show` and `rgh spoof xuid set` read or write BO2's visible XUID surfaces and known local account/persona caches once the title is ready.
- `rgh spoof remote` changes the remote/lobby slot text only.
- `rgh spoof reset` restores the supported local spoof state and can optionally clear the remote slots.

## Live Validation Notes

XeCLI verifies the write path by reading the same memory back after the write.

That means:

- XeCLI verifies the addresses changed
- XeCLI verifies the stored remote-slot text matches the requested value
- final visual confirmation in the actual game UI is still useful when testing a new title/session setup

Example BO2 validation flow:

```powershell
rgh launch "Hdd1:\Games\Call of Duty Black Ops II\default_mp.xex" --titleid 415608C3
rgh spoof gt ExampleTag --notify
rgh spoof xuid show
rgh spoof xuid set --value <xuid> --notify
rgh spoof remote ExampleRemote --notify
rgh spoof remote list
rgh spoof reset --current-user --clear-remote
```

## Related Pages

- [Commands Reference](Commands.md)
- [Hardware and System Controls](Hardware-and-System.md)
- [XNotify](XNotify.md)
