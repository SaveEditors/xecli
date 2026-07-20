#!/usr/bin/env bash
set -euo pipefail

package_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
libxenon_root="$package_root/dependencies/libxenon"
fat_root="$package_root/dependencies/fat-xenon"
xell_root="$package_root/xell"
newlib_archive="$package_root/dependencies/newlib-3.1.0.tar.gz"
version_header="$package_root/build-metadata/version.h"

expected_newlib="fb4fa1cc21e9060719208300a61420e4089d6de6ef59cf533b57fe74801d102a"
expected_version_header="5f69c432e46416cc825e394b8c5bf9a36e38a383e31e58898f7b77971139d577"
expected_gcc_version="9.2.0"
expected_binutils_version="2.32"

hash_file() {
    sha256sum "$1" | awk '{print $1}'
}

require_hash() {
    local path="$1"
    local expected="$2"
    local actual
    actual="$(hash_file "$path")"
    if [[ "$actual" != "$expected" ]]; then
        printf 'SHA-256 mismatch for %s\nExpected: %s\nActual:   %s\n' "$path" "$expected" "$actual" >&2
        exit 1
    fi
}

require_tool_version() {
    local tool="$1"
    local expected="$2"
    local actual

    if ! command -v "$tool" >/dev/null 2>&1; then
        printf '%s was not found on PATH. Build or select the matching toolchain first.\n' "$tool" >&2
        exit 1
    fi

    actual="$("$tool" --version | sed -n '1p')"
    if [[ "$actual" != *" $expected" ]]; then
        printf 'Unexpected %s version.\nExpected: %s\nActual:   %s\n' "$tool" "$expected" "$actual" >&2
        exit 1
    fi
}

if [[ -z "${DEVKITXENON:-}" ]]; then
    printf 'Set DEVKITXENON to the LibXenon toolchain prefix before building.\n' >&2
    exit 1
fi

export PATH="$DEVKITXENON/bin:$DEVKITXENON/usr/bin${PATH:+:$PATH}"

require_hash "$newlib_archive" "$expected_newlib"
require_hash "$version_header" "$expected_version_header"

if [[ "${1:-}" == "--build-toolchain" ]]; then
    cp "$newlib_archive" "$libxenon_root/toolchain/newlib-3.1.0.tar.gz"
    (
        cd "$libxenon_root/toolchain"
        PREFIX="$DEVKITXENON" ./build-xenon-toolchain toolchain
    )
elif [[ $# -ne 0 ]]; then
    printf 'Usage: %s [--build-toolchain]\n' "$0" >&2
    exit 1
fi

hash -r
require_tool_version xenon-gcc "$expected_gcc_version"
require_tool_version xenon-g++ "$expected_gcc_version"
require_tool_version xenon-ld "$expected_binutils_version"
require_tool_version xenon-ar "$expected_binutils_version"

for tool in xenon-objcopy xenon-strip; do
    command -v "$tool" >/dev/null 2>&1 || {
        printf '%s was not found on PATH. Build or select the matching toolchain first.\n' "$tool" >&2
        exit 1
    }
done

if [[ ! -f "$DEVKITXENON/xenon/lib/32/libc.a" || ! -f "$DEVKITXENON/xenon/lib/32/libm.a" ]]; then
    printf 'The selected toolchain does not contain the required 32-bit Newlib libraries.\n' >&2
    exit 1
fi

make -C "$libxenon_root/libxenon/ports/xenon" clean
make -C "$libxenon_root/libxenon/ports/xenon" libxenon.a
make -C "$libxenon_root/libxenon/ports/xenon" install

make -C "$fat_root" clean
make -C "$fat_root" all
make -C "$fat_root" install

MAKEFLAGS= MFLAGS= GNUMAKEFLAGS= make -j1 -C "$xell_root" clean
cp "$version_header" "$xell_root/source/lv2/version.h"
MAKEFLAGS= MFLAGS= GNUMAKEFLAGS= make -j1 -C "$xell_root" xell-2f.bin

printf 'Built %s\n' "$xell_root/xell-2f.bin"
sha256sum "$xell_root/xell-2f.bin"
