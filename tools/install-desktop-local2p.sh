#!/usr/bin/env bash
# Prepare an isolated local test copy, then install its desktop launcher.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=linux-common.sh
source "$SCRIPT_DIR/linux-common.sh"

GAME_DIR_OVERRIDE="${WINTERMP_GAME_DIR:-}"
WRITE_DESKTOP=1
while [[ $# -gt 0 ]]; do
    case "$1" in
        --game-dir)
            GAME_DIR_OVERRIDE="${2:-}"
            shift 2
            ;;
        --no-desktop) WRITE_DESKTOP=0; shift ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

echo
echo "Our Winter Car - isolated local 2P desktop setup (Linux)"
echo "====================================================="
echo

SOURCE_GAME="$(find_game_dir "$GAME_DIR_OVERRIDE")"
STEAM_ROOT="$(find_steam_root)"
PROTON="$(find_proton "$STEAM_ROOT")"
TEST_ROOT="${WINTERMP_LOCAL2P_DIR:-$ROOT/build/local2p}"
mkdir -p "$TEST_ROOT"
TEST_ROOT="$(cd "$TEST_ROOT" && pwd)"
GAME_DIR="$TEST_ROOT/game"
COMPAT="$TEST_ROOT/compatdata"
[[ "$GAME_DIR" != "$(cd "$SOURCE_GAME" && pwd)" ]] || { echo "Test copy must differ from the normal game." >&2; exit 1; }

if [[ ! -f "$TEST_ROOT/ready.txt" ]]; then
    [[ ! -e "$GAME_DIR" && ! -e "$COMPAT" ]] || { echo "Incomplete test setup exists at $TEST_ROOT; preserve it before retrying." >&2; exit 1; }
    mkdir -p "$GAME_DIR" "$COMPAT"
    echo "Copying game files and the current save profile to $TEST_ROOT..."
    cp -aL --reflink=auto "$SOURCE_GAME/mywintercar_Data" "$GAME_DIR/"
    # Install only this mod's loader/plugins, not unrelated plugins or stale configs.
    for file in "$SOURCE_GAME"/*; do
        [[ ! -f "$file" ]] || cp --preserve=mode,timestamps --reflink=auto "$file" "$GAME_DIR/"
    done
    SOURCE_COMPAT="${WINTERMP_SOURCE_COMPAT_DATA_PATH:-$STEAM_ROOT/steamapps/compatdata/$WINTERMP_APP_ID}"
    if [[ -d "$SOURCE_COMPAT/pfx" ]]; then
        cp -a --reflink=auto "$SOURCE_COMPAT/." "$COMPAT/"
        # A cloned LocalLow must not route the test back to personal save files.
        python3 - "$COMPAT/pfx" <<'PY'
import sys
from pathlib import Path
root = Path(sys.argv[1]).resolve()
for path in root.glob('drive_c/users/*/AppData/LocalLow'):
    if not path.resolve().is_relative_to(root):
        raise SystemExit('Copied LocalLow points outside the test prefix: ' + str(path))
for save in root.glob('drive_c/users/*/AppData/LocalLow/Amistech/My Winter Car'):
    for path in [save, *save.rglob('*')]:
        if not path.resolve().is_relative_to(root):
            raise SystemExit('Copied save points outside the test prefix: ' + str(path))
PY
    fi
    unzip -oq "$ROOT/vendor/BepInEx_win_x64_5.4.23.5.zip" -d "$GAME_DIR"
    mkdir -p "$GAME_DIR/BepInEx/config"
    cp "$ROOT/src/WinterMP.Launcher/Assets/BepInEx.cfg" "$GAME_DIR/BepInEx/config/BepInEx.cfg"
    printf 'Isolated local 2P game and save profile.\n' >"$TEST_ROOT/ready.txt"
fi

mkdir -p "$GAME_DIR/WinterMP"
exec 8>"$GAME_DIR/WinterMP/local2p.lock"
flock -n 8 || { echo "Close the local test windows before updating their mod." >&2; exit 1; }

echo "Building the local test mod (the Steam installation is unchanged)..."
dotnet build "$ROOT/src/WinterMP.Core/WinterMP.Core.csproj" -c Release -p:DeployToGame=false "-p:MwcGamePath=$SOURCE_GAME"
dotnet build "$ROOT/src/WinterMP.FastBoot/WinterMP.FastBoot.csproj" -c Release -p:DeployToGame=false "-p:MwcGamePath=$SOURCE_GAME"
MOD_DIR="$GAME_DIR/BepInEx/plugins/WinterMP"
mkdir -p "$MOD_DIR"
cp "$ROOT/src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll" "$MOD_DIR/"
cp "$ROOT/src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll" "$MOD_DIR/"
cp "$ROOT/src/WinterMP.FastBoot/bin/Release/net35/WinterMP.FastBoot.dll" "$MOD_DIR/"
cp "$ROOT/catalog/sync-catalog.json" "$MOD_DIR/"
cp "$ROOT/src/WinterMP.Launcher/Assets/wintermp-compat.json" "$MOD_DIR/"

# Keep native save hydration and the normal Continue sequence. A same-frame
# click can be lost before the menu FSM starts; force-loading then skips setup.
cat >"$GAME_DIR/BepInEx/config/com.ourwintercar.wintermp.fastboot.cfg" <<'EOF'
[Boot]
Enabled = true
SkipSplashScreen = true
SkipConfigScreen = true
AutoLoadSave = true
SplashGraceSeconds = 0
MenuSettleSeconds = 1
SaveCheckTimeoutSeconds = 5
ContinueStepDelaySeconds = 0.2
SkipMenuLoadWaits = false
ForceGameLoadAfterSeconds = 0
PreloadGameAsync = false
DevMode = false
BypassHostContinueWait = true
DevDirectGameLoad = false
DevSkipEs2Tags = false
DevEs2Whitelist = false
DevSkipEs2ExtraPrefixes =
DeferredEs2Hydrate = false
AnalyzeEs2SaveOnStartup = false
LogTimings = true
EOF
"$SCRIPT_DIR/patch-mwc-maindata.sh" "$GAME_DIR"
echo "Prepared $GAME_DIR with profile $COMPAT"
[[ "$WRITE_DESKTOP" == 1 ]] || exit 0
DESKTOP="$(desktop_dir)"
mkdir -p "$DESKTOP"
LAUNCHER="$DESKTOP/local-2p-test.sh"
LOCAL2P="$SCRIPT_DIR/local2p-test.sh"

printf '#!/usr/bin/env bash\nexport WINTERMP_GAME_DIR=%q\nexport WINTERMP_COMPAT_DATA_PATH=%q\nexport WINTERMP_PROTON=%q\nexec %q\n' \
    "$GAME_DIR" "$COMPAT" "$PROTON" "$LOCAL2P" >"$LAUNCHER"
chmod +x "$LAUNCHER"

DESKTOP_FILE="$DESKTOP/Local 2P Test.desktop"
cat >"$DESKTOP_FILE" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Local 2P Test
Comment=Our Winter Car local 2-player test with a separate save copy
Exec="$LAUNCHER"
Icon=steam_icon_${WINTERMP_APP_ID}
Terminal=true
Categories=Game;
EOF
chmod +x "$DESKTOP_FILE"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$DESKTOP" >/dev/null 2>&1 || true
fi

echo
echo "Desktop launcher created:"
echo "  $LAUNCHER"
echo "  $DESKTOP_FILE"
echo
echo "Done. Double-click \"Local 2P Test\" on your desktop (or run $LAUNCHER)."
echo "Uses the isolated test save. The normal Steam installation and saves are unchanged."
