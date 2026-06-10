"""Analyze a WinterMP catalog dump (GAME scene) to inform the sync catalog.

Usage: python tools/analyze_catalog.py <catalog.json> [--out report.md]

Produces aggregate statistics:
- FSM name frequency (which FSM "templates" exist: Use, Door, etc.)
- Top-level root objects by FSM/rigidbody count (find the car, the house)
- Door-like, vehicle-like, item-like candidates
- netId collision check (FNV-1a over scene paths must be unique enough)
"""

from __future__ import annotations

import json
import sys
from collections import Counter, defaultdict


def main() -> None:
    path = sys.argv[1]
    out_path = None
    if "--out" in sys.argv:
        out_path = sys.argv[sys.argv.index("--out") + 1]

    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    fsms = data.get("fsms", [])
    bodies = data.get("rigidbodies", [])
    lines: list[str] = []
    w = lines.append

    w(f"# Catalog analysis: {data.get('meta', {}).get('level')} "
      f"({len(fsms)} FSMs, {len(bodies)} rigidbodies)")
    w("")

    # --- FSM name frequency ---
    name_counts = Counter(f.get("fsmName") or "?" for f in fsms)
    w("## FSM template frequency (top 40)")
    for name, count in name_counts.most_common(40):
        w(f"- {name}: {count}")
    w("")

    # --- roots ---
    def root(p: str) -> str:
        return (p or "?").split("/", 1)[0]

    fsm_roots = Counter(root(f.get("path", "?")) for f in fsms)
    body_roots = Counter(root(b.get("path", "?")) for b in bodies)
    w("## Root objects by FSM count (top 30)")
    for name, count in fsm_roots.most_common(30):
        w(f"- {name}: {count} FSMs, {body_roots.get(name, 0)} rigidbodies")
    w("")

    # --- door-like FSMs ---
    w("## Door-like FSMs (path or fsmName contains door/Door)")
    door_names = Counter()
    door_samples: dict[str, str] = {}
    for f in fsms:
        p, n = f.get("path", ""), f.get("fsmName", "")
        if "door" in p.lower() or "door" in n.lower():
            key = f"{n} @ .../{p.rsplit('/', 1)[-1]}"
            door_names[key] += 1
            door_samples.setdefault(key, p)
    for key, count in door_names.most_common(25):
        w(f"- {key} x{count}   e.g. {door_samples[key]}")
    w("")

    # --- event vocabulary ---
    event_counts = Counter()
    for f in fsms:
        for e in f.get("events", []):
            if e:
                event_counts[e] += 1
    w("## Global FSM event vocabulary (top 40)")
    for name, count in event_counts.most_common(40):
        w(f"- {name}: {count}")
    w("")

    # --- netId uniqueness ---
    fsm_ids = Counter(f.get("netId") for f in fsms)
    body_ids = Counter(b.get("netId") for b in bodies)
    fsm_dupes = {i: c for i, c in fsm_ids.items() if c > 1}
    body_dupes = {i: c for i, c in body_ids.items() if c > 1}
    w("## netId uniqueness")
    w(f"- FSM netIds: {len(fsm_ids)} unique / {len(fsms)} total, "
      f"{len(fsm_dupes)} colliding ids")
    w(f"- Rigidbody netIds: {len(body_ids)} unique / {len(bodies)} total, "
      f"{len(body_dupes)} colliding ids")
    if fsm_dupes:
        by_id = defaultdict(list)
        for f in fsms:
            if f.get("netId") in fsm_dupes:
                by_id[f.get("netId")].append(f"{f.get('path')}::{f.get('fsmName')}")
        w("- sample collisions:")
        for i, paths in list(by_id.items())[:10]:
            w(f"  - {i}: {len(paths)} entries")
            for p in paths[:3]:
                w(f"    - {p}")
    w("")

    # --- vehicles: roots with many non-kinematic bodies ---
    w("## Rigidbody-heavy roots (top 20, dynamic bodies only)")
    dyn_roots = Counter(
        root(b.get("path", "?")) for b in bodies if not b.get("kinematic")
    )
    for name, count in dyn_roots.most_common(20):
        w(f"- {name}: {count} dynamic bodies")

    report = "\n".join(lines)
    if out_path:
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(report)
        print(f"written: {out_path}")
    else:
        print(report)


if __name__ == "__main__":
    main()
