# XeLL corresponding source

This directory contains the source and build material for the `xell.bin` payload bundled with XeCLI.

## Included source

- `xell/` — XeLL Reloaded at commit `d4f08b423f6335eef9d67674307f221f0086ddf8`, including the XeCLI status, synchronization, and branding changes used by the bundled payload.
- `dependencies/libxenon/` — LibXenon at commit `2b5e0e572db8db72c5e6a396d173b18aa2046394`.
- `dependencies/fat-xenon/` — fat-xenon at commit `0f2092a9a86026e07e903b596c903c1629159762`.
- `dependencies/newlib-3.1.0.tar.gz` — the Newlib source corresponding to the C library linked into the payload.
- `build-metadata/version.h` — the version metadata generated for the distributed build.

`SOURCE-MANIFEST.json` records every packaged file, its size, and its SHA-256 digest. It also records the exact source revisions, build-tool versions, linked-library evidence, and the checksum of the distributed payload.

## Payload identity

| File | Size | SHA-256 |
| --- | ---: | --- |
| `../payload/xell.bin` | 262,144 bytes | `E364FAD816CD651B1F27C1A7DCD5AE8A0C3A3C70BF11D04B6A66123A38AF2C9C` |

The source manifest is the authoritative inventory for release validation and pins the distributed payload independently of any local build output.

## License

XeLL and the XeCLI modifications are distributed under GNU GPL version 2 only; see `LICENSE.GPL-2.0.txt`. Only the notices needed by code in this source package are retained: `COMPONENT-NOTICES.md`, `NEWLIB-NOTICES.txt`, `GCC-RUNTIME-LIBRARY-EXCEPTION.txt`, the LibXenon `LICENSE`, and applicable source-file headers.

See `BUILDING.md` for the build procedure and reproducibility scope.
