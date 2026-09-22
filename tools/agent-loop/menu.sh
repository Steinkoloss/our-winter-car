#!/usr/bin/env bash
# Small terminal menu around loop.sh, for the desktop shortcut.
cd "$(dirname "${BASH_SOURCE[0]}")/../.." || exit 1
L=tools/agent-loop/loop.sh
STATE=.agent-loop

while true; do
  clear
  echo "Our Winter Car — agent loop"; echo
  $L status
  echo
  echo "[s] start loop   [h] halt after current slice   [p] playtest done (write notes)"
  echo "[l] follow log   [q] quit"
  read -rn1 -p "> " key; echo
  case "$key" in
    s)
      branch="$(git branch --show-current)"
      if [[ "$branch" != wip/* ]]; then
        new="wip/loop-$(date +%F)"
        read -rp "On '$branch'. Create and switch to '$new' from here? [y/N] " yn
        [[ "$yn" == [yY] ]] || continue
        git switch -c "$new" || git switch "$new" || { read -rp "Branch switch failed. Enter to go back."; continue; }
      fi
      $L; read -rp "Loop ended. Enter to go back." ;;
    h) mkdir -p "$STATE"; touch "$STATE/HALT"; echo "Halt requested; it stops before the next slice."; sleep 2 ;;
    p)
      mkdir -p "$STATE"
      [ -f "$STATE/playtest-notes.md" ] || printf '# Playtest notes\n\nWhat I tested:\n\nWhat broke:\n\nWhat to focus on next:\n' > "$STATE/playtest-notes.md"
      "${VISUAL:-${EDITOR:-nano}}" "$STATE/playtest-notes.md"
      $L playtested; read -rp "Enter to go back." ;;
    l) echo "Ctrl-C to go back."; trap : INT; tail -n 30 -f "$STATE/log.md"; trap - INT ;;
    q) exit 0 ;;
  esac
done
