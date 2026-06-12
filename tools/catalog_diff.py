"""Diff two WinterMP FSM dumps (catalog-*.json from F9).

Usage:
  python tools/catalog_diff.py old.json new.json

Reports FSMs added/removed and those whose states, events, or variables changed.
Use after a game patch to see what moved before updating catalog/sync-catalog.json.
"""

from __future__ import annotations

import json
import sys
from collections import defaultdict


def fsm_key(f: dict) -> str:
    return f"{f.get('path', '?')}::{f.get('fsmName', '?')}"


def signature(f: dict) -> tuple:
    states = tuple(s.get("name") for s in f.get("states", []))
    events = tuple(sorted(e for e in f.get("events", []) if e))
    var = f.get("variables", {})
    vars_flat = tuple(
        sorted(
            f"{kind}:{name}"
            for kind, names in var.items()
            for name in names
            if name
        )
    )
    return states, events, vars_flat


def main() -> None:
    if len(sys.argv) != 3:
        print(__doc__.strip())
        sys.exit(1)

    with open(sys.argv[1], encoding="utf-8") as f:
        old_data = json.load(f)
    with open(sys.argv[2], encoding="utf-8") as f:
        new_data = json.load(f)

    old_fsms = {fsm_key(f): f for f in old_data.get("fsms", [])}
    new_fsms = {fsm_key(f): f for f in new_data.get("fsms", [])}

    old_keys = set(old_fsms)
    new_keys = set(new_fsms)
    added = sorted(new_keys - old_keys)
    removed = sorted(old_keys - new_keys)

    changed: list[str] = []
    for key in sorted(old_keys & new_keys):
        if signature(old_fsms[key]) != signature(new_fsms[key]):
            changed.append(key)

    print(f"Old: {len(old_fsms)} FSMs  New: {len(new_fsms)} FSMs")
    print(f"Added: {len(added)}  Removed: {len(removed)}  Changed: {len(changed)}")
    print()

    def show(title: str, keys: list[str], limit: int = 30) -> None:
        if not keys:
            return
        print(f"## {title} ({len(keys)})")
        for key in keys[:limit]:
            print(f"  {key}")
        if len(keys) > limit:
            print(f"  ... and {len(keys) - limit} more")
        print()

    show("Added", added)
    show("Removed", removed)
    show("Changed (states/events/variables)", changed)

    # Prefix moves: same fsmName+variables under different parent path
    by_tail: dict[tuple, list[str]] = defaultdict(list)
    for key in added:
        path, fsm = key.rsplit("::", 1)
        tail = (fsm, path.rsplit("/", 1)[-1])
        by_tail[tail].append(path)

    likely_moves = [
        (tail, paths)
        for tail, paths in by_tail.items()
        if len(paths) == 1
        and any(
            fsm_key(old_fsms[k]).rsplit("/", 1)[-1] == f"::{tail[0]}"
            and old_fsms[k].get("fsmName") == tail[0]
            for k in removed
        )
    ]
    if likely_moves:
        print(f"## Possible renames ({len(likely_moves)} hints)")
        for (fsm, leaf), paths in likely_moves[:15]:
            print(f"  {fsm}/{leaf} -> {paths[0]}")


if __name__ == "__main__":
    main()
