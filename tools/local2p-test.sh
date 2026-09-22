#!/usr/bin/env bash
# Local 2-player test via Proton (host + guest on one Linux machine).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=linux-common.sh
source "$SCRIPT_DIR/linux-common.sh"

MAX_WAIT="${WINTERMP_LOCAL2P_WAIT:-90}"
READY_FLAG_NAME="WinterMP/hostlocal-ready.flag"
[[ "$MAX_WAIT" =~ ^[1-9][0-9]*$ ]] || { echo "Invalid local test wait time." >&2; exit 1; }
PROFILE_MODE="${WINTERMP_LOCAL2P_PROFILE:-0}"
case "$PROFILE_MODE" in
    0|1|frames|heap) ;;
    *) echo "Invalid local profile mode; use 0, 1, frames or heap." >&2; exit 1 ;;
esac

DESKTOP_MODE="${WINTERMP_LOCAL2P_DESKTOP:-auto}"
case "$DESKTOP_MODE" in
    auto)
        DESKTOP_MODE=0
        if [[ "${WINTERMP_LOCAL2P_HEADLESS:-0}" != 1 ]] && command -v xrandr >/dev/null 2>&1; then
            monitor_info="$(LC_ALL=C xrandr --listmonitors 2>/dev/null)" || monitor_info=""
            if [[ "$monitor_info" =~ ^Monitors:[[:space:]]+0([[:space:]]|$) ]]; then DESKTOP_MODE=1; fi
        fi
        ;;
    0|1) ;;
    *) echo "Invalid local desktop mode; use auto, 0 or 1." >&2; exit 1 ;;
esac
export WINTERMP_VIRTUAL_DESKTOP_SIZE=""
if [[ "$DESKTOP_MODE" == 1 && "${WINTERMP_LOCAL2P_HEADLESS:-0}" != 1 ]]; then
    export WINTERMP_VIRTUAL_DESKTOP_SIZE=1280x720
fi

GAME_DIR="$(find_game_dir "${WINTERMP_GAME_DIR:-}")" || {
    echo "ERROR: Game not found. Set WINTERMP_GAME_DIR or run tools/install-mod-linux.sh first." >&2
    exit 1
}

if [[ ! -f "$GAME_DIR/mywintercar.exe" ]]; then
    echo "ERROR: mywintercar.exe not found under $GAME_DIR" >&2
    exit 1
fi
if [[ ! -f "$GAME_DIR/BepInEx/plugins/WinterMP/WinterMP.Core.dll" ]]; then
    echo "Mod missing. Run tools/install-desktop-local2p.sh first." >&2
    exit 1
fi
mkdir -p "$GAME_DIR/WinterMP" "$GAME_DIR/BepInEx"
exec 9>"$GAME_DIR/WinterMP/local2p.lock"
flock -n 9 || { echo "A local two-player test is already running." >&2; exit 1; }

READY_FLAG="$GAME_DIR/$READY_FLAG_NAME"
# Wine exposes this Unix game path on Z:. Unity changes its working directory
# during boot, so use a Windows absolute log path for both instances.
WINDOWS_GAME_DIR="Z:${GAME_DIR//\//\\}"
HOST_UNITY_LOG="$WINDOWS_GAME_DIR\\BepInEx\\LogOutput-host-unity.log"
GUEST_UNITY_LOG="$WINDOWS_GAME_DIR\\BepInEx\\LogOutput-guest-unity.log"

launch_instance() {
    local role="$1"
    shift
    (
        # shellcheck source=linux-common.sh
        source "$SCRIPT_DIR/linux-common.sh"
        run_mwc "$role" "$GAME_DIR" "$@"
    ) >"$GAME_DIR/BepInEx/launch-$role.log" 2>&1 &
    # Command substitution waits for inherited stdout to close, which previously
    # held up the guest until the host game exited. Keep the PID in this shell.
    LAUNCHED_PID=$!
}

EXTRA_ARGS=()
if [[ "${WINTERMP_LOCAL2P_HEADLESS:-0}" == 1 ]]; then EXTRA_ARGS=(-batchmode -nographics); fi
if [[ "$PROFILE_MODE" != 0 ]]; then EXTRA_ARGS+=(--wintermp-live-performance-probe); fi
if [[ "$PROFILE_MODE" == frames ]]; then EXTRA_ARGS+=(--wintermp-live-performance-frames-only); fi
if [[ "$PROFILE_MODE" == heap ]]; then EXTRA_ARGS+=(--wintermp-live-performance-heap); fi

echo "============================================"
echo " WinterMP local 2-player test (Linux/Proton)"
echo " Host and Guest connect over localhost."
echo " Game: $GAME_DIR"
echo "============================================"
echo

echo "[1/2] Starting HOST instance (HostLocal mode)..."
if [[ -n "$WINTERMP_VIRTUAL_DESKTOP_SIZE" ]]; then
    echo "Using a virtual desktop window for each player so the game has a usable display."
fi
rm -f "$READY_FLAG"
"$SCRIPT_DIR/patch-mwc-maindata.sh" "$GAME_DIR"

launch_instance host \
    -no-dialogs "${EXTRA_ARGS[@]}" \
    -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
    -wintermp hostlocal \
    -logFile "$HOST_UNITY_LOG"
HOST_PID="$LAUNCHED_PID"

echo "      Waiting for host single-instance unlock (up to ${MAX_WAIT}s)..."
waited=0
while [[ ! -f "$READY_FLAG" ]]; do
    if ! kill -0 "$HOST_PID" 2>/dev/null; then
        echo
        echo "ERROR: Host instance exited before signalling ready."
        echo "       Check $GAME_DIR/BepInEx/launch-host.log and LogOutput-host.log."
        echo
        [[ ! -t 0 ]] || read -rp "Press Enter to close..." _ || true
        exit 1
    fi
    if (( waited >= MAX_WAIT )); then
        echo
        echo "ERROR: Host never released the single-instance lock within ${MAX_WAIT} seconds."
        echo "       Check BepInEx/LogOutput-host.log for \"HostLocal ready (mutex released)\"."
        echo "       Make sure the latest WinterMP.Core.dll is deployed."
        echo
        [[ ! -t 0 ]] || read -rp "Press Enter to close..." _ || true
        exit 1
    fi
    sleep 1
    waited=$((waited + 1))
done
echo "      Host unlocked after ${waited} second(s)."
echo "[2/2] Starting GUEST instance..."

launch_instance guest \
    -no-dialogs "${EXTRA_ARGS[@]}" \
    -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
    -wintermp joinlocal -wintermp-playername Guest \
    -logFile "$GUEST_UNITY_LOG"
GUEST_PID="$LAUNCHED_PID"
: "${GUEST_PID:?guest failed to launch}"

echo
echo "Both instances are starting."
echo
echo "  Logs: BepInEx/LogOutput-host.log + BepInEx/LogOutput-guest.log"
echo "        WinterMP/boot-trace-host.log + WinterMP/boot-trace-guest.log"
echo
echo "  - Hold TAB in either window: both players + the \"World:\" sync line."
echo "  - Save from the HOST window. Guest world saves are blocked by current builds."
echo
echo "Close both game windows when finished. Keep this terminal open during the test."
host_status=0
guest_status=0
wait "$HOST_PID" || host_status=$?
wait "$GUEST_PID" || guest_status=$?
if (( host_status != 0 || guest_status != 0 )); then
    echo "A game process exited with an error (host $host_status, guest $guest_status). Check BepInEx/launch-host.log and launch-guest.log." >&2
    exit 1
fi
