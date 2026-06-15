#!/usr/bin/env bash
# Install/repair BepInEx + Our Winter Car mod on Linux (Steam/Proton game folder).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=linux-common.sh
source "$SCRIPT_DIR/linux-common.sh"

GAME_DIR_OVERRIDE="${WINTERMP_GAME_DIR:-}"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --game-dir)
            GAME_DIR_OVERRIDE="${2:-}"
            shift 2
            ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

GAME_DIR="$(find_game_dir "$GAME_DIR_OVERRIDE")" || {
    cat >&2 <<EOF
My Winter Car not found.

Install the game in Steam (Proton), then rerun:
  ./tools/install-mod-linux.sh

Or pass your folder:
  ./tools/install-mod-linux.sh --game-dir "\$HOME/.steam/steam/steamapps/common/My Winter Car"
EOF
    exit 1
}

BEPINEX_ZIP="$ROOT/vendor/BepInEx_win_x64_5.4.23.5.zip"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip"
MOD_DIR="$GAME_DIR/BepInEx/plugins/WinterMP"
CONFIG_DIR="$GAME_DIR/BepInEx/config"

echo "Target game: $GAME_DIR"

set_mwc_game_path "$ROOT" "$GAME_DIR"
echo "[1/5] Directory.Build.props.user ready."

mkdir -p "$ROOT/vendor"
if [[ ! -f "$BEPINEX_ZIP" ]]; then
    echo "[2/5] Downloading BepInEx..."
    curl -fsSL "$BEPINEX_URL" -o "$BEPINEX_ZIP"
else
    echo "[2/5] BepInEx vendor zip present."
fi

if [[ ! -f "$GAME_DIR/winhttp.dll" || ! -d "$GAME_DIR/BepInEx/core" ]]; then
    echo "      Extracting BepInEx into the game folder..."
    unzip -oq "$BEPINEX_ZIP" -d "$GAME_DIR"
else
    echo "      BepInEx loader already present."
fi

mkdir -p "$CONFIG_DIR"
BEPINEX_CFG="$CONFIG_DIR/BepInEx.cfg"
if [[ ! -f "$BEPINEX_CFG" ]]; then
    cp "$ROOT/src/WinterMP.Launcher/Assets/BepInEx.cfg" "$BEPINEX_CFG"
    echo "      Wrote BepInEx.cfg with MonoBehaviour entrypoint."
elif ! grep -A5 '^\[Preloader\.Entrypoint\]' "$BEPINEX_CFG" | grep -q 'Type = MonoBehaviour'; then
    cp "$BEPINEX_CFG" "$BEPINEX_CFG.wintermp.bak"
    sed -i '/^\[Preloader\.Entrypoint\]/,/^\[/ s/^\s*Type\s*=.*/Type = MonoBehaviour/' "$BEPINEX_CFG"
    echo "      Patched BepInEx.cfg entrypoint to MonoBehaviour."
else
    echo "      BepInEx.cfg already configured."
fi

FASTBOOT_CFG="$CONFIG_DIR/com.ourwintercar.wintermp.fastboot.cfg"
if [[ ! -f "$FASTBOOT_CFG" ]]; then
    cat >"$FASTBOOT_CFG" <<'EOF'
## FastBoot settings — speed profile (managed by Our Winter Car launcher)
## Plugin GUID: com.ourwintercar.wintermp.fastboot

[Boot]
Enabled = true
SkipSplashScreen = true
SkipConfigScreen = true
AutoLoadSave = true
SplashGraceSeconds = 0
MenuSettleSeconds = 0
SaveCheckTimeoutSeconds = 0
ContinueStepDelaySeconds = 0
SkipMenuLoadWaits = true
ForceGameLoadAfterSeconds = 90
PreloadGameAsync = true
DevMode = true
BypassHostContinueWait = true
DevDirectGameLoad = false
DevSkipEs2Tags = true
DevEs2Whitelist = true
DevSkipEs2ExtraPrefixes =
LogTimings = false
DeferredEs2Hydrate = true
DeferredHydrateDelaySeconds = 3
AnalyzeEs2SaveOnStartup = false
EOF
    echo "      Wrote FastBoot speed profile."
fi

echo "[3/5] Building mod from source..."
dotnet build "$ROOT/src/WinterMP.FastBoot/WinterMP.FastBoot.csproj" -c Release >/dev/null
dotnet build "$ROOT/src/WinterMP.Core/WinterMP.Core.csproj" -c Release
dotnet build "$ROOT/src/WinterMP.Tools/WinterMP.Tools.csproj" -c Release >/dev/null

CORE_OUT="$ROOT/src/WinterMP.Core/bin/Release/net35"
FASTBOOT_OUT="$ROOT/src/WinterMP.FastBoot/bin/Release/net35"

mkdir -p "$MOD_DIR"
cp -f "$CORE_OUT/WinterMP.Core.dll" "$MOD_DIR/"
cp -f "$CORE_OUT/WinterMP.Net.dll" "$MOD_DIR/"
cp -f "$CORE_OUT/sync-catalog.json" "$MOD_DIR/"
cp -f "$FASTBOOT_OUT/WinterMP.FastBoot.dll" "$MOD_DIR/"
cp -f "$ROOT/src/WinterMP.Launcher/Assets/wintermp-compat.json" "$MOD_DIR/"

if [[ ! -f "$MOD_DIR/WinterMP.Core.dll" ]]; then
    echo "ERROR: mod deploy failed — WinterMP.Core.dll missing in $MOD_DIR" >&2
    exit 1
fi
echo "[4/5] Mod DLLs deployed to $MOD_DIR"

"$SCRIPT_DIR/patch-mwc-maindata.sh" "$GAME_DIR"
echo "[5/5] Boot patch applied."

echo ""
echo "Install complete. Rebuild Core anytime — DLLs auto-deploy when MwcGamePath is set."
