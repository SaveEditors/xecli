# XeLL component notices

This file identifies only license material required by code included in the XeCLI XeLL payload and its corresponding source.

- **XeLL Reloaded and XeCLI modifications:** GNU GPL version 2 only. The complete terms are in `LICENSE.GPL-2.0.txt`; file-level copyright and modification notices remain in `xell/`.
- **LibXenon:** BSD 3-Clause. The complete notice for free60.org and the Broadcom USB stack is in `dependencies/libxenon/libxenon/LICENSE`; additional file-level notices remain with the source files they cover.
- **fat-xenon:** BSD 3-Clause. Copyright and complete terms remain in the applicable headers and source files under `dependencies/fat-xenon/`.
- **puff:** zlib-style terms by Mark Adler. The complete notice remains in `xell/source/lv1/puff/puff.c` and `puff.h`.
- **Newlib 3.1.0:** the exact upstream aggregate notices are in `NEWLIB-NOTICES.txt`, and the corresponding source archive is `dependencies/newlib-3.1.0.tar.gz`.
- **GCC runtime support:** compiler-emitted routines from GCC 9.2.0 `libgcc.a` are covered by the GCC Runtime Library Exception 3.1 in `GCC-RUNTIME-LIBRARY-EXCEPTION.txt`.

GCC 9.2.0 and Binutils 2.32 are the pinned build tools; only the `libgcc` runtime support identified above is linked into the payload. The LibXenon toolchain scripts retain the exact version pins and target patches used for this build.
