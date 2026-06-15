#!/usr/bin/env bash
# Shared helpers for Linux/Proton dev scripts (Steam + My Winter Car).

WINTERMP_APP_ID="${WINTERMP_APP_ID:-4164420}"
WINTERMP_GAME_NAME="${WINTERMP_GAME_NAME:-My Winter Car}"

_linux_common_root() {
    local src="${BASH_SOURCE[1]:-${BASH_SOURCE[0]}}"
    while [[ -L "$src" ]]; do
        local dir
        dir="$(cd "$(dirname "$src")" && pwd)"
        src="$(readlink "$src")"
        [[ "$src" != /* ]] && src="$dir/$src"
    done
    cd "$(dirname "$src")/.." && pwd
}

find_steam_root() {
    local candidate
    for candidate in \
        "${STEAM_DIR:-}" \
        "$HOME/.steam/root" \
        "$HOME/.steam/steam" \
        "$HOME/.local/share/Steam" \
        "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"
    do
        if [[ -n "$candidate" && -d "$candidate/steamapps" ]]; then
            echo "$candidate"
            return 0
        fi
    done
    return 1
}

read_mwc_game_path() {
    local root="${1:-$(_linux_common_root)}"
    local props="$root/Directory.Build.props.user"
    if [[ ! -f "$props" ]]; then
        return 1
    fi
    sed -n 's:.*<MwcGamePath>\(.*\)</MwcGamePath>.*:\1:p' "$props" | head -1 | tr -d '\r'
}

set_mwc_game_path() {
    local root="$1"
    local game_dir="$2"
    local props="$root/Directory.Build.props.user"
    local example="$root/Directory.Build.props.user.example"

    if [[ ! -f "$props" ]]; then
        if [[ ! -f "$example" ]]; then
            echo "Missing template: $example" >&2
            return 1
        fi
        cp "$example" "$props"
    fi

    if grep -q '<MwcGamePath>' "$props"; then
        sed -i "s#<MwcGamePath>[^<]*</MwcGamePath>#<MwcGamePath>${game_dir}</MwcGamePath>#" "$props"
    else
        sed -i "s#</Project>#  <PropertyGroup>\n    <MwcGamePath>${game_dir}</MwcGamePath>\n  </PropertyGroup>\n</Project>#" "$props"
    fi
}

find_game_dir() {
    local override="${1:-}"

    if [[ -n "$override" && -f "$override/mywintercar.exe" ]]; then
        echo "$override"
        return 0
    fi

    if [[ -n "${WINTERMP_GAME_DIR:-}" && -f "$WINTERMP_GAME_DIR/mywintercar.exe" ]]; then
        echo "$WINTERMP_GAME_DIR"
        return 0
    fi

    local from_props
    from_props="$(read_mwc_game_path 2>/dev/null || true)"
    if [[ -n "$from_props" && -f "$from_props/mywintercar.exe" ]]; then
        echo "$from_props"
        return 0
    fi

    local steam_root hit
    steam_root="$(find_steam_root)" || return 1

    hit="$(find "$steam_root/steamapps" -path "*/common/${WINTERMP_GAME_NAME}/mywintercar.exe" -print -quit 2>/dev/null || true)"
    if [[ -n "$hit" ]]; then
        dirname "$hit"
        return 0
    fi

    return 1
}

find_proton() {
    local steam_root="$1"
    local proton_dir proton_bin

    for proton_dir in \
        "$steam_root/steamapps/common/Proton - Experimental" \
        $(ls -1d "$steam_root/steamapps/common/Proton - "[0-9]* 2>/dev/null | sort -V)
    do
        proton_bin="$proton_dir/proton"
        if [[ -x "$proton_bin" ]]; then
            echo "$proton_bin"
            return 0
        fi
    done

    echo "Proton not found under $steam_root/steamapps/common." >&2
    echo "Install Proton in Steam, then launch My Winter Car once with Proton enabled." >&2
    return 1
}

run_mwc() {
    local role="$1"
    local game_dir="$2"
    shift 2

    local steam_root proton compat
    steam_root="$(find_steam_root)" || return 1
    proton="$(find_proton "$steam_root")" || return 1
    compat="$steam_root/steamapps/compatdata/${WINTERMP_APP_ID}"

    export STEAM_COMPAT_DATA_PATH="$compat"
    export STEAM_COMPAT_CLIENT_INSTALL_PATH="$steam_root"
    export SteamAppId="$WINTERMP_APP_ID"
    export SteamGameId="$WINTERMP_APP_ID"

    (
        cd "$game_dir" || exit 1
        WINTERMP_LOG_ROLE="$role" exec "$proton" run ./mywintercar.exe "$@"
    )
}

desktop_dir() {
    if command -v xdg-user-dir >/dev/null 2>&1; then
        xdg-user-dir DESKTOP
        return 0
    fi
    if [[ -d "$HOME/Desktop" ]]; then
        echo "$HOME/Desktop"
        return 0
    fi
    echo "$HOME"
}
