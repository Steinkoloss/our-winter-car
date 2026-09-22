#!/usr/bin/env python3
"""Compare a cabin binding with supplied static extracts; never launches the game."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def audit(source, extracted):
    path = "CABIN/Cabin/woodstove/Fireplace"
    cabin_file = extracted / "cabin-installed-assets.json"
    objects_file = extracted / "asset-objects.json"
    cabin = json.loads(cabin_file.read_text())
    objects = json.loads(objects_file.read_text())
    trigger = next(f for f in cabin["fsms"] if f["path"] == path + "/WoodTrigger")
    fire = next(f for f in cabin["fsms"] if f["path"] == path + "/SetFire")
    state = lambda f, name: next(s for s in f["states"] if s["name"] == name)
    parameter = lambda action, field: next(p for p in action["parameters"] if p["field"] == field)
    assertions = []
    def check(name, condition):
        assertions.append({"assertion": name, "passed": bool(condition)})
        if not condition:
            raise AssertionError(name)
    destroy = state(trigger, "Destroy firewood")
    implementation = (source / "src/WinterMP.Core/Sync/HeatSourceSync.Cabin.cs").read_text()
    match = re.search(r'RequireCabinState\(trigger, "Destroy firewood",(.*?)\);', implementation, re.S)
    check("production feed action types match audited static array", re.findall(r'"([^"]+)"', match.group(1)) ==
          [a["type"].split(".")[-1] for a in destroy["actions"]])
    check("audited native destruction targets Collider", parameter(destroy["actions"][3], "gameObject")["value"]["name"] == "Collider")
    check("audited destruction is deferred Unity Destroy request, zero delay", parameter(destroy["actions"][3], "delay")["value"] == 0)
    check("audited canonical increment is one WoodTrigger.Woods", parameter(destroy["actions"][4], "intVariable")["name"] == "Woods" and
          parameter(destroy["actions"][4], "add")["value"] == 1)
    check("audited capacity is four", parameter(state(trigger, "State 1")["actions"][0], "integer2")["value"] == 4)
    burn = state(fire, "Burn")
    check("SetFire is an every-frame cache reader, not feed canonical state", burn["actions"][2]["type"].endswith(".GetFsmInt"))
    prefab = next(a for a in objects["assets"] if a["asset"] == "sharedassets3.assets")
    halves = [o["serialized"] for o in prefab["objects"] if o["type"] == "BoxCollider" and o["path"] in ("log", "log/log(Clone)")]
    check("native half-collider centers distinguish root and child", len(halves) == 2 and halves[0]["m_Center"] != halves[1]["m_Center"])
    check("both native half scales are unit", all(o["serialized"]["m_LocalScale"] == {"x": 1.0, "y": 1.0, "z": 1.0}
          for o in prefab["objects"] if o["type"] == "Transform"))
    catalog = json.loads((source / "catalog/sync-catalog.json").read_text())
    check("catalog fixed source", catalog["woodstoveFuel"]["source"] == path)
    def rules(value):
        if isinstance(value, dict):
            yield value
            for v in value.values(): yield from rules(v)
        elif isinstance(value, list):
            for v in value: yield from rules(v)
    relay = next(r for r in rules(catalog) if r.get("pathContains") == "/SetFire")
    check("generic SetFire relay excludes cabin only", relay["excludePathPrefixes"] == [path + "/"])
    native = (source / "src/WinterMP.Core/Sync/HeatSourceSync.CabinNative.cs").read_text()
    check("production postcondition is later-frame Unity-null, not a synthesized retirement", "Time.frameCount > _mutatedFrame" in native and
          "_owner._cabinWood[resourceId].Piece == null" in native)
    check("production feed reads canonical WoodTrigger binding", 'source.Woods = trigger.FsmVariables.FindFsmInt("Woods")' in implementation)
    check("host and guest production entry share Decide", "_fuelAuthority.Decide(authenticatedActor, message.Request())" in implementation and
          "OnCabinIntent(intent, session.LocalPlayerId)" in implementation)
    return {"level": "static extract/source assertions, NOT native gameplay", "assertions": assertions,
            "inputs": {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in (cabin_file, objects_file)}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--audit-dir", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.source, args.audit_dir)
    args.out.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))

if __name__ == "__main__":
    main()
