#!/usr/bin/env bash
# Build the Our Winter Car universal installer as an Actually Portable Executable.
#
# Output: dist/OurWinterCar-Installer.com — ONE file that installs the mod on both
# Windows and Linux. It carries both self-contained launcher builds (each with the
# mod payload + BepInEx) in its zip store and, at run time, extracts the OS-matched
# one and calls `WinterMPLauncher --install-mod --silent`.
#
# Requires: cosmocc on PATH or $COSMOCC set to the cosmocc binary
#           (https://cosmo.zip/pub/cosmocc/cosmocc.zip), the .NET SDK, and a
#           My Winter Car install (Directory.Build.props.user) so the net35 mod
#           DLLs can be built into the payload.
#
# Usage: ./tools/build-ape-installer.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST="${OWC_RELEASE_DIR:-$ROOT/dist}"
WORK="$ROOT/build/ape"
APEROOT="$WORK/aperoot"
SRC="$ROOT/installer/ape/ourwintercar-installer.c"
OUT="$DIST/OurWinterCar-Installer.com"
LAUNCHER_PROJ="$ROOT/src/WinterMP.Launcher/WinterMP.Launcher.csproj"

# --- locate cosmocc -------------------------------------------------------
COSMOCC="${COSMOCC:-$(command -v cosmocc || true)}"
if [[ -z "$COSMOCC" || ! -x "$COSMOCC" ]]; then
    cat >&2 <<EOF
cosmocc not found. Install the Cosmopolitan toolchain, then re-run:

  mkdir -p ~/cosmocc && cd ~/cosmocc
  curl -fsSL https://cosmo.zip/pub/cosmocc/cosmocc.zip -o cosmocc.zip
  unzip -q cosmocc.zip
  export COSMOCC=~/cosmocc/bin/cosmocc

EOF
    exit 1
fi
echo "==> cosmocc: $COSMOCC"

rm -rf "$WORK"
mkdir -p "$APEROOT" "$DIST"

# --- 1. mod DLLs (net35 payload) -----------------------------------------
# Rebuild so a version bump can't leave a stale Core.dll in the payload.
echo "==> Building mod payload (Core + FastBoot, Release)"
if [[ -z "${OWC_WIN_PUBLISH_DIR:-}" || -z "${OWC_LINUX_PUBLISH_DIR:-}" ]]; then
dotnet build "$ROOT/src/WinterMP.Core/WinterMP.Core.csproj" \
    -c Release -t:Rebuild -p:DeployToGame=false -v q /clp:ErrorsOnly
dotnet build "$ROOT/src/WinterMP.FastBoot/WinterMP.FastBoot.csproj" \
    -c Release -p:DeployToGame=false -v q /clp:ErrorsOnly
fi

# --- 2. self-contained launchers for both OSes ---------------------------
publish_launcher() {
    local rid="$1" dest="$2"
    echo "==> Publishing launcher ($rid)"
    dotnet publish "$LAUNCHER_PROJ" -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=false -p:DeployToGame=false -v q /clp:ErrorsOnly \
        -o "$dest"
}
if [[ -n "${OWC_WIN_PUBLISH_DIR:-}" && -n "${OWC_LINUX_PUBLISH_DIR:-}" ]]; then
    cp -a "$OWC_WIN_PUBLISH_DIR" "$APEROOT/win"
    cp -a "$OWC_LINUX_PUBLISH_DIR" "$APEROOT/linux"
else
    publish_launcher win-x64   "$APEROOT/win"
    publish_launcher linux-x64 "$APEROOT/linux"
fi

# Sanity: the headless install path needs the payload + BepInEx vendor zip bundled.
for os in win linux; do
    test -f "$APEROOT/$os/payload/WinterMP.Core.dll" \
        || { echo "ERROR: payload missing in $os launcher (build Core in Release first)" >&2; exit 1; }
    test -f "$APEROOT/$os"/vendor/BepInEx_win_x64_*.zip \
        || { echo "ERROR: BepInEx vendor zip missing in $os launcher (run tools/fetch-vendor)" >&2; exit 1; }
done

# --- 3. per-platform file manifests --------------------------------------
# The C reads these to know which files to copy out of the zip store.
for os in win linux; do
    ( cd "$APEROOT/$os" && find . -type f | sed 's|^\./||' | LC_ALL=C sort > "$APEROOT/$os.manifest" )
    echo "==> $os: $(wc -l < "$APEROOT/$os.manifest") files"
done

# --- 4. compile the installer to an APE ----------------------------------
echo "==> Compiling installer (cosmocc; first build is slow)"
# -Wno-format-truncation: snprintf into PATH_MAX buffers is bounded and safe here.
"$COSMOCC" -O2 -Wall -Wno-format-truncation -o "$OUT" "$SRC"

# --- 5. embed launchers + manifests into the .com zip store --------------
echo "==> Embedding launcher payload into the zip store"
python3 - "$OUT" "$APEROOT" <<'PY'
import os, sys, zipfile

out, aperoot = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "a", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for os_key in ("win", "linux"):
        z.write(os.path.join(aperoot, f"{os_key}.manifest"), f"{os_key}.manifest")
        root = os.path.join(aperoot, os_key)
        for dirpath, _dirs, files in os.walk(root):
            for name in files:
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, root).replace(os.sep, "/")
                z.write(full, f"{os_key}/{rel}")
print("embedded", out)
PY

chmod +x "$OUT"

echo ""
echo "Universal installer: $OUT  ($(du -h "$OUT" | cut -f1))"
echo "  Windows: double-click (or rename to .exe if SmartScreen blocks the .com)"
echo "  Linux:   chmod +x and run (fallback: sh ./OurWinterCar-Installer.com)"
