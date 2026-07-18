"""Extract full FSM records from a WinterMP catalog dump for sync curation.

Usage: python tools/extract_fsm_details.py <catalog.json> <mode>

Modes:
  doors    - representative door FSMs (house, garage, vehicle)
  items    - pickable (itemx) FSMs
  bolts    - Screw/Assemble FSMs on car parts
  player   - PLAYER root FSMs (hand/pickup logic)
  rivett   - anything matching RIVETT/VIN/CORRIS (the project car)
  find:<s> - all FSMs whose path or fsmName contains <s> (paths only)
  show:<s> - full records for FSMs whose path contains <s> (max 10)
"""

from __future__ import annotations

import json
import sys


def fmt(f: dict) -> str:
    lines = [f"=== {f.get('path')} :: {f.get('fsmName')}  (netId {f.get('netId')}, "
             f"active={f.get('active')}, state={f.get('activeState')})"]
    for s in f.get("states", []):
        ts = ", ".join(f"{t.get('event')}->{t.get('to')}" for t in s.get("transitions", []))
        lines.append(f"  state '{s.get('name')}'  [{ts}]")
        actions = [a for a in s.get("actionTypes", []) if a]
        if actions:
            lines.append(f"    actions: {', '.join(actions)}")
    ev = ", ".join(e for e in f.get("events", []) if e)
    lines.append(f"  events: {ev}")
    var = f.get("variables", {})
    for k, names in var.items():
        names = [n for n in names if n]
        if names:
            lines.append(f"  {k}: {', '.join(names)}")
    return "\n".join(lines)


def main() -> None:
    path, mode = sys.argv[1], sys.argv[2]
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    fsms = data.get("fsms", [])

    def show(pred, limit=10):
        n = 0
        for f in fsms:
            if pred(f):
                print(fmt(f))
                print()
                n += 1
                if n >= limit:
                    print(f"... (limit {limit} reached)")
                    break
        print(f"[{n} shown]")

    if mode == "doors":
        targets = [
            "HOMENEW/LOD/DoorWC/Pivot/Handle",
            "YARD/Building/Garage/GarageDoors/Left/Door/Coll",
            "YARD/Building/MIDDLEROOM/DoorRear/Pivot/Handle",
            "STORE_AREA/Stuff/LOD/DoorTeimo/Pivot",
        ]
        show(lambda f: f.get("path") in targets, limit=20)
    elif mode == "items":
        show(lambda f: "(itemx)" in (f.get("path") or "") and f.get("fsmName") == "Use", limit=4)
    elif mode == "bolts":
        show(lambda f: f.get("fsmName") in ("Screw", "Assemble", "Assembly", "BoltCheck")
             and "VIN" in (f.get("path") or ""), limit=8)
    elif mode == "player":
        show(lambda f: (f.get("path") or "").startswith("PLAYER"), limit=100)
    elif mode == "rivett":
        seen = set()
        for f in fsms:
            p = f.get("path") or ""
            if "RIVETT" in p.upper() or "VIN" in p or p.startswith("CORRIS"):
                root = "/".join(p.split("/")[:3])
                if root not in seen:
                    seen.add(root)
                    print(f"{root}   (e.g. ::{f.get('fsmName')})")
        print(f"[{len(seen)} distinct roots]")
    elif mode.startswith("find:"):
        needle = mode[5:].lower()
        n = 0
        for f in fsms:
            p = (f.get("path") or "")
            if needle in p.lower() or needle in (f.get("fsmName") or "").lower():
                print(f"{p} :: {f.get('fsmName')}  (state={f.get('activeState')})")
                n += 1
                if n >= 80:
                    print("... (80 limit)")
                    break
        print(f"[{n} shown]")
    elif mode.startswith("show:"):
        needle = mode[5:].lower()
        show(lambda f: needle in (f.get("path") or "").lower(), limit=10)


if __name__ == "__main__":
    main()
