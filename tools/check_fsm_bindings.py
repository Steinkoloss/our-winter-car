"""Class-D audit: cross-check FindFsm{Float,Int,Bool,String} bindings against a dump.

A binding whose variable name does not exist under the matching type on ANY FSM in
the dump silently binds null and can inert an entire subsystem (the ice-race "Time"
Int-vs-Float bug, the car-radio "Channel" bug). Rerun this after every game update /
fresh F9 dump and after adding sync subsystems.

Usage: python3 tools/check_fsm_bindings.py [catalog/dump-XXXX.json]

Covers literal bindings, `cond ? "A" : "B"` ternaries, and same-file
`string[] Name = { ... }` constants used as FindFsmX(Name[i]). Direct GlobalVariables.*
lookups use globalVariables, optionally from a separate --globals asset extract.
Missing global evidence is reported as unverified. Exit 1 on wrong-type bindings
or a missing literal global when global evidence is supplied.
"""

from __future__ import annotations

import json
import argparse
import os
import re
import sys

CATEGORY = {
    "Float": "FloatVariables",
    "Int": "IntVariables",
    "Bool": "BoolVariables",
    "String": "StringVariables",
}

# (type, name) pairs that are known-absent on this game build and deliberately kept in
# code (documented in the source + PROTOCOL.md). They still print, but do not fail the run.
KNOWN_ABSENT = {
    # InspectionSync keeps all 38 checklist slots; ShockRL/RR don't exist as bools on
    # this build (only as game objects) — slots kept to avoid a bit reshuffle.
    ("Bool", "ShockRL"),
    ("Bool", "ShockRR"),
}

LITERAL = re.compile(r'FindFsm(Float|Int|Bool|String)\(\s*"([^"]+)"\s*\)')
TERNARY = re.compile(r'FindFsm(Float|Int|Bool|String)\(\s*[^()]*?\?\s*"([^"]+)"\s*:\s*"([^"]+)"\s*\)')
DYNAMIC = re.compile(r'FindFsm(Float|Int|Bool|String)\(\s*([A-Za-z_][A-Za-z0-9_.\[\]]*)\s*\)')
ARRAY_DECL = re.compile(r'string\[\]\s+(\w+)\s*=\s*(?:new\s+string\[\]\s*)?\{([^}]*)\}', re.S)
STR = re.compile(r'"([^"]*)"')


def load_dump(path: str):
    with open(path) as f:
        data = json.load(f)
    fsms = data["fsms"] if isinstance(data, dict) else data
    # name -> type-category -> set of "path::fsmName" carrying it
    where: dict[str, dict[str, set[str]]] = {}
    for r in fsms:
        loc = f"{r.get('path')}::{r.get('fsmName')}"
        for cat, names in (r.get("variables") or {}).items():
            for n in names:
                if n:
                    where.setdefault(n, {}).setdefault(cat, set()).add(loc)
    return where


def load_globals(path: str):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    values = data.get("globalVariables") if isinstance(data, dict) else None
    if not values:
        return None
    where = {}
    for cat, variables in values.items():
        for variable in variables:
            name = variable if isinstance(variable, str) else variable.get("name")
            if name:
                where.setdefault(name, {}).setdefault(cat, set()).add("<globals>")
    return where


def bindings_in(root: str):
    out = []  # (file, line_no, type, name, origin)
    dynamic = []  # (file, line_no, type, expr)
    for dirpath, _dirs, files in os.walk(root):
        if any(p in dirpath for p in (os.sep + "bin", os.sep + "obj")):
            continue
        for fn in files:
            if not fn.endswith(".cs"):
                continue
            fp = os.path.join(dirpath, fn)
            with open(fp, encoding="utf-8", errors="replace") as source:
                text = source.read()
            arrays = {m.group(1): STR.findall(m.group(2)) for m in ARRAY_DECL.finditer(text)}
            for i, line in enumerate(text.splitlines(), 1):
                scope = "global " if "GlobalVariables" in line else ""
                consumed_spans = []
                for m in TERNARY.finditer(line):
                    consumed_spans.append(m.span())
                    for name in (m.group(2), m.group(3)):
                        out.append((fp, i, m.group(1), name, scope + "ternary"))
                for m in LITERAL.finditer(line):
                    out.append((fp, i, m.group(1), m.group(2), scope + "literal"))
                for m in DYNAMIC.finditer(line):
                    if any(s <= m.start() < e for s, e in consumed_spans):
                        continue
                    expr = m.group(2)
                    base = expr.split("[")[0].split(".")[0]
                    if base in arrays:
                        for name in arrays[base]:
                            out.append((fp, i, m.group(1), name, f"{scope}array {base}"))
                    else:
                        dynamic.append((fp, i, m.group(1), expr))
    return out, dynamic


def main() -> int:
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump", nargs="?", default=os.path.join(repo, "catalog", "dump-23268598.json"))
    parser.add_argument("--globals", dest="global_dump", help="Separate dump or asset extract containing globals")
    args = parser.parse_args()
    dump_path = args.dump
    where = load_dump(dump_path)
    globals_ = load_globals(args.global_dump or dump_path)
    core = os.path.join(repo, "src", "WinterMP.Core")
    found, dynamic = bindings_in(core)

    wrong, missing, dual, unverified = [], [], [], []
    for fp, ln, typ, name, origin in found:
        cat = CATEGORY[typ]
        rel = os.path.relpath(fp, repo)
        if origin.startswith("global ") and globals_ is None:
            unverified.append((rel, ln, typ, name))
            continue
        types = (globals_ if origin.startswith("global ") else where).get(name)
        if types is None:
            missing.append((rel, ln, typ, name, origin))
        elif cat not in types:
            wrong.append((rel, ln, typ, name, origin, sorted(types)))
        elif len(types) > 1:
            dual.append((rel, ln, typ, name, sorted(types)))

    print(f"checked {len(found)} resolvable bindings against {dump_path}")
    if unverified:
        print(f"UNVERIFIED: {len(unverified)} literal global bindings; supply a fresh dump or --globals asset extract.")
    unexpected = [w for w in wrong if (w[2], w[3]) not in KNOWN_ABSENT]
    if wrong:
        print("\nWRONG-TYPE (class D — the name exists in the dump but NEVER under the bound type):")
        for rel, ln, typ, name, origin, types in wrong:
            known = "  [known-absent, documented]" if (typ, name) in KNOWN_ABSENT else ""
            print(f"  {rel}:{ln}  FindFsm{typ}(\"{name}\") [{origin}] — dump has it only as {types}{known}")
    if missing:
        print("\nNOT-IN-DUMP (may be a global, a runtime-created var, or a typo — verify by hand):")
        for rel, ln, typ, name, origin in missing:
            print(f"  {rel}:{ln}  FindFsm{typ}(\"{name}\") [{origin}]")
    if dual:
        print("\nDUAL-TYPE names (bound type exists, but so do others — verify the right FSM is targeted):")
        for rel, ln, typ, name, types in dual:
            print(f"  {rel}:{ln}  FindFsm{typ}(\"{name}\") — dump types {types}")
    if dynamic:
        print("\nUNRESOLVED dynamic bindings (audit by hand):")
        for fp, ln, typ, expr in dynamic:
            print(f"  {os.path.relpath(fp, repo)}:{ln}  FindFsm{typ}({expr})")
    if not (wrong or missing):
        print("no wrong-type or missing bindings.")
    missing_globals = [m for m in missing if m[4].startswith("global ")]
    return 1 if unexpected or missing_globals else 0


if __name__ == "__main__":
    sys.exit(main())
