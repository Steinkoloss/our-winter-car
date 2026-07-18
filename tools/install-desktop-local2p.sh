#!/usr/bin/env bash
# One-click Linux setup: install mod into Steam/Proton MWC + desktop Local 2P launcher.
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

echo
echo "Our Winter Car - mod + local 2P desktop setup (Linux)"
echo "====================================================="
echo

WINTERMP_GAME_DIR="$GAME_DIR_OVERRIDE" "$SCRIPT_DIR/install-mod-linux.sh" ${GAME_DIR_OVERRIDE:+--game-dir "$GAME_DIR_OVERRIDE"}

GAME_DIR="$(find_game_dir "$GAME_DIR_OVERRIDE")"
DESKTOP="$(desktop_dir)"
LAUNCHER="$DESKTOP/local-2p-test.sh"
LOCAL2P="$SCRIPT_DIR/local2p-test.sh"

cat >"$LAUNCHER" <<EOF
#!/usr/bin/env bash
export WINTERMP_GAME_DIR='$GAME_DIR'
exec '$LOCAL2P'
EOF
chmod +x "$LAUNCHER"

DESKTOP_FILE="$DESKTOP/Local 2P Test.desktop"
cat >"$DESKTOP_FILE" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Local 2P Test
Comment=Our Winter Car local 2-player test (Proton)
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
echo "Requires: Steam + Proton, game launched at least once so compatdata exists."
