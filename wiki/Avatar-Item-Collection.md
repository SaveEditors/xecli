# Avatar Item Collection

`Avatar-Item-Collection` is the avatar library name used by XeCLI for browsing, download, install planning, and console-side install.

XeCLI includes two selection paths for this library:

- `rgh avatar choose` for a terminal-based picker
- `rgh avatar browse` for a Windows picker

Both sit on top of the same manifest, cache, ownership patch, and install pipeline.

This is the name shown throughout the CLI, the wiki, and the hosted library.

## What XeCLI Supports
XeCLI now exposes a finished avatar command surface against this collection in local or remote mode:

- `rgh avatar library show`
- `rgh avatar library set`
- `rgh avatar cache status`
- `rgh avatar cache refresh`
- `rgh avatar games`
- `rgh avatar items`
- `rgh avatar choose`
- `rgh avatar browse`
- `rgh avatar install`
- `rgh avatar apply`

That surface can:

- resolve the active collection root
- fall back to the hosted GitHub collection when a local corpus is not configured
- build and cache a searchable item index
- resolve item names and title labels from package metadata before falling back to raw IDs
- list games with avatar content
- list items by title, text match, publisher, or derived tag
- offer a terminal picker for title-first or item-first selection
- offer a Windows picker for search, filtering, multi-select, and select-all workflows
- patch ownership for a target XUID
- install directly into the console content tree with progress-aware transfer output
- fall back to XBDM upload and verification when the console FTP service is not running
- show progress bars and per-item status during multi-item installs

## Collection Layout
The top-level folder should be named `Avatar-Item-Collection`.

XeCLI supports two on-disk layouts inside each title folder.

### Standard avatar-content layout
```text
Avatar-Item-Collection/
  415608C3/
    00009000/
      000000080DF3B242CAE65A52415608C3
```

This is the common Xbox 360 avatar-content structure.

### Irregular title-root payload layout
```text
Avatar-Item-Collection/
  58410A5D/
    0000020800060102C383304058410A5D
```

XeCLI preserves that root layout during install instead of forcing a `00009000` folder.

## Runtime Paths
By default XeCLI resolves the collection root in this order:

1. explicit `--library <DIR>`
2. saved config value
3. `XECLI_AVATAR_COLLECTION_ROOT`
4. `Avatar-Item-Collection` beside the executable
5. the bundled local fallback path, when one is included with the release package

If no local corpus is available, XeCLI can switch to the hosted GitHub-backed collection automatically or when `--remote` is supplied.

Hosted collection:

- [SaveEditors/Avatar-Item-Collection](https://github.com/SaveEditors/Avatar-Item-Collection)

Index cache and working files live under the XeCLI config or cache directories unless overridden.

Installed-mode default paths:

- `%APPDATA%\XeCLI\avatar-index.v5.json`
- `%APPDATA%\XeCLI\avatar-work\...`
- `%LOCALAPPDATA%\XeCLI\cache\avatar\avatar-remote-index.v2.json`
- `%LOCALAPPDATA%\XeCLI\cache\avatar\packages\...`
- `%LOCALAPPDATA%\XeCLI\cache\avatar\download-cache\...`

Portable mode places the same config and cache trees under package-local `UserData`. `XECLI_HOME` places both under the directory it names.

## Command Usage
### Show the active collection path
```powershell
rgh avatar library show
rgh avatar library show --remote
```

### Save the default collection path
```powershell
rgh avatar library set --path "$env:USERPROFILE\Downloads\Avatar-Item-Collection"
rgh avatar library set --manifest-url https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/avatar-manifest.json
rgh avatar library set --title-map-url https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/avatar-title-map.json
rgh avatar library set --content-base-url https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/
rgh avatar library set --download-cache "$env:LOCALAPPDATA\XeCLI\cache\avatar\download-cache"
```

### List games with avatar items
```powershell
rgh avatar games
rgh avatar games --search "Black Ops"
rgh avatar games --remote --search "Black Ops"
```

### List items for a title
```powershell
rgh avatar items --titleid 415608C3 --limit 10
rgh avatar items --titleid 58410A5D --limit 10
rgh avatar items --remote --titleid 415608C3 --limit 10
```

### Choose items in the terminal
```powershell
rgh avatar choose --search "Black Ops" --current-user
rgh avatar choose --remote --titleid 58410A5D --all --current-user
```

### Browse items in the Windows picker
```powershell
rgh avatar browse --remote
rgh avatar browse --remote --titleid 415608C3 --search hoodie
```

### Install one item for the current signed-in user
```powershell
rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user
rgh avatar install --remote --contentid 000000080DF3B242CAE65A52415608C3 --current-user
rgh avatar install --contentid 000000080DF3B242CAE65A52415608C3 --current-user --dry-run
```

### Install every item for a title
```powershell
rgh avatar install --titleid 415608C3 --all --current-user
rgh avatar install --remote --titleid 415608C3 --all --current-user
```

### Install one item through the alias
```powershell
rgh avatar apply --contentid 0000020800060102C383304058410A5D --current-user
```

## Ownership and Install Model
Avatar installs are not just file copies.

XeCLI:

1. resolves the target user XUID
2. patches ownership data in a temporary working copy
3. preserves the item’s original relative store layout
4. uploads to the console content tree
5. verifies the remote file size

When `--remote` is used, XeCLI first downloads the selected package from the hosted `Avatar-Item-Collection` repo into the local cache, verifies it, patches ownership, and then uploads it to the console.

The download step is cached so repeated installs do not re-fetch the same package unless the manifest changes or the local cache is cleared.

If no explicit XUID is provided, XeCLI resolves the current signed-in user from the console and uses that XUID for patching.

For offline planning, `--dry-run` prints the intended title, category, profile, and cache state without connecting to the console.

## Remote Install Paths
XeCLI installs avatar items into the console content tree under:

- `/Hdd1/Content/0000000000000000/<TITLEID>/...`

Examples below use user-facing output:

### Standard layout example
- source:
  - `<cache>\Avatar-Item-Collection\415608C3\00009000\000000080DF3B242CAE65A52415608C3`
- remote:
  - `/Hdd1/Content/0000000000000000/415608C3/00009000/000000080DF3B242CAE65A52415608C3`

### Root-layout example
- source:
  - `<cache>\Avatar-Item-Collection\58410A5D\0000020800060102C383304058410A5D`
- remote:
  - `/Hdd1/Content/0000000000000000/58410A5D/0000020800060102C383304058410A5D`

## Transport Behavior
XeCLI prefers FTP for avatar install when the console FTP service is running.

If the console FTP service is not running, XeCLI can fall back to:

- XBDM directory creation
- XBDM file upload
- XBDM-side size verification

That means avatar install does not depend entirely on FTP being up.

The simplest way to use the library is:

1. pick a game from the catalog
2. filter the items for that game
3. select one or many items
4. install them with `rgh avatar install`

XeCLI includes both a terminal picker and a Windows picker that follow that flow.

## Search and Metadata
XeCLI indexes avatar items with:

- title ID
- resolved title name
- content ID
- display name
- game name when present
- publisher
- file size
- relative store path
- derived tags
- package layout type

Title matching lets a configured user Title Database override collection/package metadata, then falls back to that metadata and finally the raw hexadecimal Title ID.

For hosted browsing, user Title Database entries take precedence over the remote manifest/title-map pair. XeCLI does not bundle or download a general-purpose title database as part of this workflow.

## Use With Other Tools
Other launchers or managers can reuse the same collection name and layout model.

The clean integration pattern is:

1. point to `Avatar-Item-Collection`
2. build or reuse an index
3. filter by title or item name
4. hand selected installs to XeCLI

That keeps one shared library name and one predictable storage model across tools.
