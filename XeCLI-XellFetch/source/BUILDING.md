# Building the XeLL payload

The distributed payload was built with GCC 9.2.0, Binutils 2.32, Newlib 3.1.0, LibXenon commit `2b5e0e572db8db72c5e6a396d173b18aa2046394`, and fat-xenon commit `0f2092a9a86026e07e903b596c903c1629159762`.

## Validated host

Release rebuilds are validated on Windows in the 64-bit MSYS2 MSYS shell. Run the commands below from that shell rather than PowerShell or Command Prompt. A native Linux environment with equivalent GNU tooling may also work, but it is not the host used for release validation.

The required tools are Bash, GNU Make, Git, Wget, patch, tar, gzip, `sha256sum`, awk, sed, and the normal LibXenon toolchain prerequisites. Use a writable MSYS path without spaces for `DEVKITXENON`.

## Build

To create the cross-toolchain and then build the exact dependency revisions and `xell-2f.bin`:

```bash
export DEVKITXENON="$HOME/xenon"
export PATH="$DEVKITXENON/bin:$DEVKITXENON/usr/bin:$PATH"
./build.sh --build-toolchain
```

If the matching toolchain is already installed, omit `--build-toolchain`:

```bash
export DEVKITXENON="$HOME/xenon"
export PATH="$DEVKITXENON/bin:$DEVKITXENON/usr/bin:$PATH"
./build.sh
```

The output is `xell/xell-2f.bin`. The bootstrap verifies the exact Binutils 2.32, GCC 9.2.0, and Newlib 3.1.0 source archives before extraction. Before building LibXenon, fat-xenon, or XeLL, `build.sh` verifies the selected cross-tool versions, the required 32-bit Newlib libraries, and the preserved version header. The completed payload checksum is printed after the build.

XeLL must be built serially because its LV1 and LV2 stages reuse object paths. `build.sh` clears inherited GNU Make parallel settings and uses `-j1` for the XeLL clean and build operations; do not invoke the XeLL target with parallel Make options.

## Reproducibility scope

The source revisions, Newlib archive, generated version header, payload, and installed LibXenon and fat-xenon libraries are checksum-locked in `SOURCE-MANIFEST.json`. The captured build metadata identifies GCC 9.2.0 and Binutils 2.32.

A new build can differ at the byte level when the host toolchain, archive implementation, or filesystem metadata differs. The release process therefore validates the distributed payload against its fixed SHA-256 value and does not claim that an arbitrary host will produce a byte-identical file.
