#!/usr/bin/env bash
# Build OurWinterCar-Setup.exe (the Windows Inno Setup installer) *on Linux*, via Wine.
#
# Setup.exe is NOT Windows-only: Inno Setup's ISCC.exe compiles fine under Wine, and
# the resulting installer runs on real Windows unchanged. This mirrors what
# tools/build-installer.ps1 does on Windows, so a full versioned release can now be
# cut entirely from Linux (this + build-ape-installer.sh + build-appimage.sh + the
# launcher/payload zips).
#
# Output: dist/OurWinterCar-Setup.exe
# Requires: wine, the .NET SDK, and a My Winter Car install (for the net35 payload).
# Usage: ./tools/build-setup-linux.sh [--skip-build]   (--skip-build reuses the last publish)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST="$ROOT/dist"
ISS_DIR="$ROOT/installer"
PUBLISH_DIR="$ROOT/src/WinterMP.Launcher/bin/publish/win-x64"   # the path WinterMP.iss #ifexist-picks
LAUNCHER_PROJ="$ROOT/src/WinterMP.Launcher/WinterMP.Launcher.csproj"

INNO_VERSION="6.7.3"
INNO_URL="https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-${INNO_VERSION}.exe"
# Persistent, throwaway Wine prefix — installs Inno once, reused across runs, never
# touches ~/.wine or your Steam/Proton prefixes.
WINEPREFIX="${OWC_INNO_WINEPREFIX:-$HOME/.cache/ourwintercar/inno-wine}"
ISCC="$WINEPREFIX/drive_c/inno/ISCC.exe"

export WINEPREFIX
export WINEDLLOVERRIDES="mscoree,mshtml="   # skip the Mono/Gecko install prompts
export WINEDEBUG="${WINEDEBUG:-fixme-all}"

command -v wine >/dev/null || { echo "wine not found — install Wine to build Setup.exe on Linux." >&2; exit 1; }

# --- 1. publish the win-x64 launcher (with payload + vendor) ---------------
if [[ "${1:-}" == "--skip-build" && -f "$PUBLISH_DIR/WinterMPLauncher.exe" ]]; then
    echo "==> Reusing existing publish: $PUBLISH_DIR"
else
    echo "==> Building mod payload (Core + FastBoot, Release)"
    dotnet build "$ROOT/src/WinterMP.Core/WinterMP.Core.csproj" \
        -c Release -t:Rebuild -p:DeployToGame=false -v q /clp:ErrorsOnly
    dotnet build "$ROOT/src/WinterMP.FastBoot/WinterMP.FastBoot.csproj" \
        -c Release -p:DeployToGame=false -v q /clp:ErrorsOnly
    echo "==> Publishing self-contained launcher (win-x64)"
    rm -rf "$PUBLISH_DIR"
    dotnet publish "$LAUNCHER_PROJ" -c Release -r win-x64 --self-contained true \
        -p:PublishSingleFile=false -p:DeployToGame=false -v q /clp:ErrorsOnly -o "$PUBLISH_DIR"
fi
test -f "$PUBLISH_DIR/payload/WinterMP.Core.dll" \
    || { echo "ERROR: payload missing — build Core in Release first." >&2; exit 1; }

# --- 2. ensure Inno Setup is installed in the Wine prefix -----------------
if [[ ! -f "$ISCC" ]]; then
    echo "==> Installing Inno Setup $INNO_VERSION under Wine (one-time, prefix: $WINEPREFIX)"
    cache="$HOME/.cache/ourwintercar"
    mkdir -p "$cache"
    inno_exe="$cache/innosetup-${INNO_VERSION}.exe"
    [[ -f "$inno_exe" ]] || curl -fsSL "$INNO_URL" -o "$inno_exe"
    wine "$inno_exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="C:\\inno" /NOICONS >/dev/null 2>&1 || true
    test -f "$ISCC" || { echo "ERROR: Inno install failed — ISCC.exe not found in prefix." >&2; exit 1; }
fi

# --- 3. compile the installer ---------------------------------------------
# Run from installer/ with a bare filename: a leading-'/' Linux path is misread by
# ISCC as an option, and the script's own '..\' paths resolve from its directory.
echo "==> Compiling WinterMP.iss with ISCC.exe under Wine"
mkdir -p "$DIST"
( cd "$ISS_DIR" && wine "$ISCC" /Qp WinterMP.iss )

SETUP="$DIST/OurWinterCar-Setup.exe"
test -f "$SETUP" || { echo "ERROR: ISCC finished but $SETUP was not produced." >&2; exit 1; }
echo ""
echo "Windows installer (built on Linux): $SETUP  ($(du -h "$SETUP" | cut -f1))"
