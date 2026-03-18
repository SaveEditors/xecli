# FAQ

## Is the Title ID database bundled or downloaded
It is bundled. XeCLI ships with local Title ID files in the repo and in the publish output.

## Does XeCLI ship console-side plugins
No. XeCLI ships the local CLI, bundled assets, and release documentation. Console-side components such as XBDM, JRPC2, and FTP services are still prerequisites on the target console.

## Where are the bundled Title ID files
In source:
- `src/Xbox360.Remote.Cli/Assets/xbox360_gamelist.csv`
- `src/Xbox360.Remote.Cli/Assets/xbox360_titleids.txt`

In published output:
- `Assets/xbox360_gamelist.csv`
- `Assets/xbox360_titleids.txt`

## Can I use the Title ID database in another project
Yes. That is an intended use case. The CSV is the preferred source for external tools.

## Does XeCLI need internet access for metadata
No. Normal Title ID resolution is local.

## What is the difference between `modules dump` and `xex dump`
`modules dump` pulls the live memory image of a loaded module. `xex dump` retrieves a real XEX file suitable for static analysis workflows.

## Which command should I use first on a new console session
Use `rgh status` or `rgh status --quick`.

## When should I use FTP instead of XBDM
Use FTP when you want:
- Storage-oriented file access
- Recursive searches
- Alternate retrieval of XEX files from disk paths

Use XBDM when you want:
- Live state
- Memory access
- Threads and debug controls
- Module information

## What happens if the console disconnects
XeCLI retries automatically. It attempts reconnection three times and gives the operator a chance to cancel.

## Does XeCLI support scripting
Yes. Many commands support `--json` specifically for script and tool integration.

## Can I add my own Title IDs
Yes. Use `%APPDATA%\XeCLI\titleids.local.csv`.

## Is XeCLI only for terminal users
No. It is terminal-first, but its JSON outputs and bundled assets are designed so GUI tools and companion apps can reuse it cleanly.


