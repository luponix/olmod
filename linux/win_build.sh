#!/bin/bash
# Cross-build the Windows loader (olmod.exe) from Linux with mingw-w64.
#
# The official build is Visual Studio (olmod.sln, project "olmod", x64/Release).
# This script produces the same binary from a Linux box so a Windows package can
# be cut without a Windows machine. Two source guards make it work:
#   * targetver.h  skips <SDKDDKVer.h> (Windows SDK only) when not _MSC_VER
#   * olmod.cpp    skips its LIBCTINY wWinMainCRTStartup, since mingw's own
#                  -municode startup already calls wWinMain
#
# Requires: mingw-w64 cross toolchain
#     Arch/CachyOS : sudo pacman -S mingw-w64-gcc
#     Debian/Ubuntu: sudo apt install g++-mingw-w64-x86-64 binutils-mingw-w64
#
# Usage: ./win_build.sh [-o <output.exe>]
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"
SRC=../olmod
OUT=olmod.exe
if [ "${1:-}" = "-o" ]; then OUT="$2"; fi

CXX=x86_64-w64-mingw32-g++
RC=x86_64-w64-mingw32-windres
for t in "$CXX" "$RC"; do
    command -v "$t" >/dev/null || { echo "ERROR: $t not found -- install mingw-w64" >&2; exit 1; }
done

BUILD=$(mktemp -d)
trap 'rm -rf "$BUILD"' EXIT

# windres cannot read the UTF-16 .rc Visual Studio wrote; hand it a UTF-8 copy
# next to the headers/icons it #includes.
cp "$SRC"/resource.h "$SRC"/targetver.h "$SRC"/olmod.ico "$SRC"/small.ico "$BUILD"/
iconv -f UTF-16LE -t UTF-8 "$SRC"/olmod.rc | sed '1s/^\xEF\xBB\xBF//' > "$BUILD"/olmod.rc

# The Visual Studio build embeds a per-monitor DPI-aware manifest
# (olmod.vcxproj <EnableDpiAwareness>PerMonitorHighDPIAware). mingw embeds no
# manifest at all, and olmod.exe IS the game process (it loads UnityPlayer.dll
# and calls UnityMain in-process) -- without the manifest, Windows display
# scaling >100% DPI-virtualizes the process, the desktop resolution is reported
# scaled, and the game's settable resolution list breaks. Embed the equivalent
# manifest as resource 1 of type RT_MANIFEST (24).
cat > "$BUILD"/olmod.manifest <<'EOF'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">True/PM</dpiAware>
    </windowsSettings>
  </application>
</assembly>
EOF
printf '\n1 24 "olmod.manifest"\n' >> "$BUILD"/olmod.rc

"$RC" -I "$BUILD" -I "$SRC" -O coff -o "$BUILD"/olmod.res "$BUILD"/olmod.rc

"$CXX" -municode -O2 -DUNICODE -D_UNICODE -DNDEBUG -D_WINDOWS \
    -fno-exceptions -fno-rtti -I "$SRC" \
    -o "$OUT" \
    "$SRC"/olmod.cpp "$SRC"/stdafx.cpp "$BUILD"/olmod.res \
    -mwindows -static -static-libgcc -static-libstdc++ \
    -ldbghelp -lshell32 -ladvapi32 -luser32 -lkernel32

x86_64-w64-mingw32-strip "$OUT" 2>/dev/null || true
echo "built $OUT ($(du -h "$OUT" | cut -f1))"
file "$OUT"
