#!/usr/bin/env python3
"""Lossless, fail-closed queries for schema-4 F9 action metadata (not gameplay).

Requires an explicit input and a fresh output directory. No game/save/rig access.
Legacy catalogs remain readable, but omitted operands always mean unknown.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
from pathlib import Path
import sys

STATUSES = {"ok", "null", "unknown", "unreadable", "withheld"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def marker(value):
    require(isinstance(value, dict) and isinstance(value.get("status"), str) and value["status"] in STATUSES, "invalid/missing status marker")


def integer(value):
    return type(value) is int and value >= 0


def node(value, depth=0):
    require(depth <= 32, "excessive metadata depth")
    marker(value)
    if value.get("kind") == "scene-reference":
        require(isinstance(value.get("path"), str), "reference lacks path")
        require(integer(value.get("pathCandidates")), "reference lacks candidate count")
        require(isinstance(value.get("resolution"), str), "reference lacks resolution")
        if "componentType" in value or "componentIndex" in value:
            require(isinstance(value.get("componentType"), str) and integer(value.get("componentIndex")), "invalid component slot")
    for key in ("metadata", "members"):
        if key in value:
            require(isinstance(value[key], dict), "invalid member metadata")
            for child in value[key].values(): node(child, depth + 1)
    if "value" in value and isinstance(value["value"], dict): node(value["value"], depth + 1)
    if "items" in value:
        require(isinstance(value["items"], list) and integer(value.get("length")), "invalid operand array")
        require(len(value["items"]) <= value["length"], "array length mismatch")
        require(value["status"] != "ok" or len(value["items"]) == value["length"], "unmarked truncated array")
        for child in value["items"]: node(child, depth + 1)


def action_inventory(state, schema_version=None):
    require(schema_version is None or (type(schema_version) is int and 1 <= schema_version <= 4), "unsupported catalog schema")
    require(isinstance(state, dict), "invalid state")
    types = state.get("actionTypes", [])
    require(isinstance(types, list) and all(t is None or isinstance(t, str) for t in types), "invalid actionTypes")
    if "actionRecords" not in state:
        require(schema_version != 4 and "actionRecordsStatus" not in state, "schema-4 actionRecords missing")
        return {"status": {"status": "unknown", "reason": "legacy-catalog-omits-operands"},
                "records": [{"index": i, "type": t, "status": "unknown", "reason": "legacy-type-only", "fields": []}
                            for i, t in enumerate(types)]}
    require("actionTypes" in state, "actionRecords lacks legacy actionTypes cross-check")
    marker(state.get("actionRecordsStatus"))
    records = state["actionRecords"]
    require(isinstance(records, list) and len(records) == len(types), "action record/type length mismatch")
    for i, record in enumerate(records):
        marker(record)
        require(type(record.get("index")) is int and record["index"] == i, "action index/order mismatch")
        require("type" in record and record["type"] == types[i], "action type mismatch")
        require(record["type"] is not None or record["status"] != "ok", "null action marked ok")
        fields = record.get("fields")
        require(isinstance(fields, list), "invalid action fields")
        seen = set()
        for field in fields:
            require(isinstance(field, dict) and all(isinstance(field.get(k), str) for k in ("name", "declaringType", "type")), "invalid field metadata")
            key = field["declaringType"], field["name"]
            require(key not in seen, "duplicate field identity")
            seen.add(key)
            node(field.get("data"))
        if "enabled" in record: node(record["enabled"])
        if "fieldCount" in record:
            require(integer(record["fieldCount"]) and record["fieldCount"] >= len(fields), "field count mismatch")
            require(record["status"] != "ok" or record["fieldCount"] == len(fields), "unmarked missing fields")
    return {"status": state["actionRecordsStatus"], "records": records}


def walk(value, pointer=""):
    yield pointer, value
    if isinstance(value, dict):
        for key, child in value.items():
            escaped = str(key).replace("~", "~0").replace("/", "~1")
            yield from walk(child, pointer + "/" + escaped)
    elif isinstance(value, list):
        for i, child in enumerate(value): yield from walk(child, pointer + "/" + str(i))


def query(data, path, fsm_name, states):
    require(isinstance(data, dict) and isinstance(data.get("fsms"), list), "catalog must contain fsms array")
    meta = data.get("meta", {})
    require(isinstance(meta, dict), "invalid meta")
    version = meta.get("schemaVersion")
    require(version is None or (type(version) is int and 1 <= version <= 4), "unsupported catalog schema")
    fsms = data["fsms"]
    require(all(isinstance(f, dict) and isinstance(f.get("path"), str) and isinstance(f.get("fsmName"), str) for f in fsms), "invalid FSM identity")
    result = {"evidence": "PARTIAL_DISCOVERY_NOT_GAMEPLAY", "schemaVersion": version,
              "query": {"path": path, "fsmName": fsm_name, "states": states}, "matches": []}
    for fi, fsm in enumerate(fsms):
        if fsm["path"] != path or fsm["fsmName"] != fsm_name: continue
        all_states = fsm.get("states")
        require(isinstance(all_states, list) and all(isinstance(s, dict) and isinstance(s.get("name"), str) for s in all_states), "invalid state list")
        match = {"pointer": f"/fsms/{fi}", "header": {k: v for k, v in fsm.items() if k != "states"},
                 "states": [], "missingStates": [s for s in states if not any(s == item["name"] for item in all_states)]}
        for si, state in enumerate(all_states):
            if states and state["name"] not in states: continue
            inventory = action_inventory(state, version)
            entry = {"pointer": f"/fsms/{fi}/states/{si}", "raw": state, "inventory": inventory,
                     "references": [], "unresolved": []}
            for pointer, value in walk(inventory):
                if not isinstance(value, dict): continue
                if isinstance(value.get("status"), str) and value["status"] in {"unknown", "unreadable", "withheld"}:
                    entry["unresolved"].append({"pointer": pointer, "marker": value})
                if value.get("kind") != "scene-reference": continue
                candidates = []
                for ci, candidate in enumerate(fsms):
                    if candidate["path"] != value["path"]: continue
                    if "componentIndex" in value:
                        reference = candidate.get("objectReference", {})
                        if not isinstance(reference, dict) or any(reference.get(k) != value[k] for k in ("componentType", "componentIndex")): continue
                    candidates.append(f"/fsms/{ci}")
                unique = value.get("status") == "ok" and value.get("pathCandidates") == 1
                resolution = "unresolved" if not candidates else "ambiguous"
                if unique and len(candidates) == 1 and "componentIndex" in value:
                    resolution = "unique-observed-component"
                elif unique and "componentIndex" not in value and candidates:
                    resolution = "object-path-candidates-not-component-selection"
                entry["references"].append({"pointer": pointer, "reference": value,
                    "candidateFsms": candidates, "resolution": resolution,
                    "descendantCandidates": [{"pointer": f"/fsms/{ci}", "path": c["path"], "fsmName": c["fsmName"]}
                        for ci, c in enumerate(fsms) if c["path"].startswith(value["path"] + "/")]})
            match["states"].append(entry)
        result["matches"].append(match)
    return result


def load_catalog(path):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate JSON key: " + key)
            result[key] = value
        return result
    def bad_constant(value):
        raise ValueError("nonfinite JSON constant: " + value)
    with Path(path).open(encoding="utf-8-sig") as stream:
        return json.load(stream, object_pairs_hook=pairs, parse_constant=bad_constant)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path)
    parser.add_argument("--path", required=True)
    parser.add_argument("--fsm", required=True)
    parser.add_argument("--state", action="append", default=[])
    parser.add_argument("--out", type=Path, required=True, help="new evidence directory; must not exist")
    args = parser.parse_args(argv)
    args.out.mkdir(parents=True, exist_ok=False)
    receipt = {"argv": sys.argv if argv is None else argv, "cwd": str(Path.cwd()),
               "startedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "input": {"path": str(args.catalog.resolve())},
               "toolSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
               "evidenceLevel": "retained catalog parsing only; no native execution"}
    try:
        receipt["input"]["sha256"] = hashlib.sha256(args.catalog.read_bytes()).hexdigest()
        report = query(load_catalog(args.catalog), args.path, args.fsm, args.state)
        raw = (json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode()
        with (args.out / "query.json").open("xb") as stream: stream.write(raw)
        receipt["reportSha256"] = hashlib.sha256(raw).hexdigest()
        receipt["exitCode"] = 0
        print(json.dumps({"matches": len(report["matches"]), "evidence": report["evidence"], "report": str(args.out / "query.json")}))
    except (ValueError, OSError) as error:
        receipt["exitCode"] = 2
        receipt["error"] = str(error)
        print(str(error), file=sys.stderr)
    with (args.out / "receipt.json").open("x") as stream: json.dump(receipt, stream, indent=2)
    return receipt["exitCode"]


if __name__ == "__main__":
    sys.exit(main())
