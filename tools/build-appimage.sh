#!/usr/bin/env bash
# Package the launcher as a Linux AppImage for distribution.
# Usage: ./tools/build-appimage.sh
# Output: dist/OurWinterCar-Launcher-linux-x64.AppImage
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST="$ROOT/dist"
LAUNCHER_PROJ="$ROOT/src/WinterMP.Launcher/WinterMP.Launcher.csproj"
APPDIR="$DIST/WinterMPLauncher.AppDir"
VENDOR_DIR="$ROOT/vendor"
APPIMAGETOOL="$VENDOR_DIR/appimagetool-x86_64.AppImage"
APPIMAGETOOL_URL="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
OUTPUT="$DIST/OurWinterCar-Launcher-linux-x64.AppImage"

mkdir -p "$DIST" "$VENDOR_DIR"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"

echo "==> Publishing linux-x64 launcher"
dotnet publish "$LAUNCHER_PROJ" -c Release -r linux-x64 --self-contained true \
    -p:DeployToGame=false -v q /clp:ErrorsOnly \
    -o "$APPDIR/usr/bin"

echo "==> Creating AppDir"

cat >"$APPDIR/AppRun" <<'APPRUN'
#!/usr/bin/env bash
exec "$APPDIR/usr/bin/WinterMPLauncher" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

cat >"$APPDIR/WinterMPLauncher.desktop" <<'DESKTOP'
[Desktop Entry]
Name=Our Winter Car
Comment=Co-op multiplayer launcher for My Winter Car
Exec=WinterMPLauncher
Icon=WinterMPLauncher
Type=Application
Categories=Game;
DESKTOP

# Generate a 256×256 PNG icon using Python3 stdlib (no Pillow needed).
python3 - "$APPDIR/WinterMPLauncher.png" <<'PY'
import struct, zlib, sys

def make_png(path, w, h, rgb):
    def chunk(tag, data):
        buf = tag + data
        return struct.pack('>I', len(data)) + buf + struct.pack('>I', zlib.crc32(buf) & 0xffffffff)
    row = b'\x00' + bytes(rgb) * w
    idat = zlib.compress(row * h, 9)
    out = (b'\x89PNG\r\n\x1a\n'
           + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
           + chunk(b'IDAT', idat)
           + chunk(b'IEND', b''))
    with open(path, 'wb') as f:
        f.write(out)

make_png(sys.argv[1], 256, 256, [0x00, 0x50, 0xa0])
PY

if [[ ! -f "$APPIMAGETOOL" ]]; then
    echo "==> Downloading appimagetool"
    curl -fsSL "$APPIMAGETOOL_URL" -o "$APPIMAGETOOL"
    chmod +x "$APPIMAGETOOL"
fi

echo "==> Packaging AppImage"
rm -f "$OUTPUT"
ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$OUTPUT" 2>&1
chmod +x "$OUTPUT"

echo ""
echo "AppImage: $OUTPUT"
