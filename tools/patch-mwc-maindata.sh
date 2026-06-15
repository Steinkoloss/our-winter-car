#!/usr/bin/env bash
# Disable Unity 5 ScreenSelector in My Winter Car (mainData patch).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=linux-common.sh
source "$SCRIPT_DIR/linux-common.sh"

GAME_DIR="${1:-${WINTERMP_GAME_DIR:-}}"
if [[ -z "$GAME_DIR" ]]; then
    GAME_DIR="$(find_game_dir)" || {
        echo "ERROR: could not locate My Winter Car. Pass the game folder or set WINTERMP_GAME_DIR." >&2
        exit 1
    }
fi

MAIN_DATA="$GAME_DIR/mywintercar_Data/mainData"
if [[ ! -f "$MAIN_DATA" ]]; then
    echo "ERROR: mainData not found: $MAIN_DATA" >&2
    exit 1
fi

python3 - "$MAIN_DATA" <<'PY'
import struct
import sys
from pathlib import Path

path = Path(sys.argv[1])
offset = 4224
data = bytearray(path.read_bytes())
if len(data) < offset + 4:
    raise SystemExit(f"mainData too small ({len(data)} bytes)")

text = bytes(data).decode("ascii", errors="ignore")
if "Amistech" not in text or "My Winter Car" not in text:
    raise SystemExit("mainData does not look like My Winter Car.")

current = struct.unpack_from("<i", data, offset)[0]
if current == 0:
    print("Resolution dialog already disabled in mainData.")
    raise SystemExit(0)
if current not in (1, 2):
    raise SystemExit(f"Unexpected displayResolutionDialog value {current} at offset {offset}.")

backup = path.with_suffix(path.suffix + ".wintermp-original")
if not backup.exists():
    backup.write_bytes(data)

struct.pack_into("<i", data, offset, 0)
path.write_bytes(data)
print(f"Patched mainData: displayResolutionDialog {current} -> 0 (Disabled).")
PY
