# XeCLI-XellFetch Custom Payload Notes

This tree carries the XeCLI-specific branding patch for the staged `xell.bin` payload used by XeCLI and the standalone XeCLI-XellFetch bundle.

Current customization:

- Theme colors use `0xFF0D0D0D` background and `0xFF5C4E8A` text.
- The console shows a bottom-right footer: `Github.com/SaveEditors`.
- The staged payload shows a stable `Waiting for XeCLI connection` screen instead of dropping into the normal file/TFTP search loop.
- The screen now carries a XeCLI ASCII wordmark above the live job status.
- `/FLASH` and `/rawflash` both trigger the NAND HTTP dump handler.
- `/KV`, `/KVRAW`, and `/KVRAW2` expose keyvault export paths through the same branded payload.
- During an HTTP NAND dump, XeLL pauses the normal file-search and TFTP retry loop so the screen is not overwritten with idle spam.
- During a XeCLI-synchronized job, the payload switches between waiting, transfer, verifying, success, and failure states without falling back into idle search spam.

Font note:

- XeLL itself is using libxenon's console renderer and 8x16 bitmap font.
- A true font swap is not a XeLL-only patch; it requires rebuilding against a modified libxenon console/font implementation.

Build note:

- This source tree requires `DEVKITXENON`, `xenon-gcc`, and the matching libxenon environment to rebuild `xell.bin`.
- The current workspace does not have that toolchain installed, so these changes are source-only until the devkit is available.
- For quick host-side iteration, `tools/preview_xell_branding.py` renders an approximate SVG version of the waiting/active/complete/failed screens.
