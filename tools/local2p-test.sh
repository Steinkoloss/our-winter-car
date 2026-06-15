#!/usr/bin/env bash
# Local 2-player test via Proton (host + guest on one Linux machine).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=linux-common.sh
source "$SCRIPT_DIR/linux-common.sh"

MAX_WAIT="${WINTERMP_LOCAL2P_WAIT:-90}"
READY_FLAG_NAME="WinterMP/hostlocal-ready.flag"

GAME_DIR="$(find_game_dir "${WINTERMP_GAME_DIR:-}")" || {
    echo "ERROR: Game not found. Set WINTERMP_GAME_DIR or run tools/install-mod-linux.sh first." >&2
    exit 1
}

if [[ ! -f "$GAME_DIR/mywintercar.exe" ]]; then
    echo "ERROR: mywintercar.exe not found under $GAME_DIR" >&2
    exit 1
fi

READY_FLAG="$GAME_DIR/$READY_FLAG_NAME"
HOST_UNITY_LOG="$GAME_DIR/BepInEx/LogOutput-host-unity.log"
GUEST_UNITY_LOG="$GAME_DIR/BepInEx/LogOutput-guest-unity.log"

launch_instance() {
    local role="$1"
    shift
    (
        # shellcheck source=linux-common.sh
        source "$SCRIPT_DIR/linux-common.sh"
        run_mwc "$role" "$GAME_DIR" "$@"
    ) &
}

echo "============================================"
echo " WinterMP local 2-player test (Linux/Proton)"
echo " Host = your profile, Guest = \"Guest\""
echo " Game: $GAME_DIR"
echo "============================================"
echo

echo "[1/2] Starting HOST instance (HostLocal mode)..."
rm -f "$READY_FLAG"
"$SCRIPT_DIR/patch-mwc-maindata.sh" "$GAME_DIR"

launch_instance host \
    -no-dialogs -fastboot-dev \
    -screen-fullscreen 0 -screen-width 960 -screen-height 540 \
    -wintermp hostlocal \
    -logFile "$HOST_UNITY_LOG"

echo "      Waiting for host single-instance unlock (up to ${MAX_WAIT}s)..."
waited=0
while [[ ! -f "$READY_FLAG" ]]; do
    if (( waited >= MAX_WAIT )); then
        echo
        echo "ERROR: Host never released the single-instance lock within ${MAX_WAIT} seconds."
        echo "       Check BepInEx/LogOutput-host.log for \"HostLocal ready (mutex released)\"."
        exit 1
    fi
    sleep 1
    waited=$((waited + 1))
done
echo "      Host unlocked after ${waited} second(s)."
echo "[2/2] Starting GUEST instance..."

"$SCRIPT_DIR/patch-mwc-maindata.sh" "$GAME_DIR"

launch_instance guest \
    -no-dialogs -fastboot-dev \
    -screen-fullscreen 0 -screen-width 960 -screen-height 540 \
    -wintermp joinlocal -wintermp-playername Guest \
    -logFile "$GUEST_UNITY_LOG"

echo
echo "Both instances are starting."
echo
echo "  Logs: BepInEx/LogOutput-host.log + BepInEx/LogOutput-guest.log"
echo "        WinterMP/boot-trace-host.log + WinterMP/boot-trace-guest.log"
echo
echo "  - Hold TAB in either window: both players + the \"World:\" sync line."
echo "  - IMPORTANT: never save the game in the GUEST window."
echo
