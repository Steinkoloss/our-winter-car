#!/usr/bin/env python3
"""Offline, bounded Inspection CoffeeAutomatic field validation. Never opens assets.

Exit 0 ACCEPTED_FIELDS (structural evidence only), 2 MISSING_FIELDS (old dump),
1 REJECTED/IO error. No status enables an adapter or proves native gameplay.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import sys
from datetime import datetime, timezone

SOURCE = Path(__file__).resolve().parents[1]
ROOT = "INSPECTION/LOD/CoffeeAutomatic"
MAX_BYTES = 32 * 1024 * 1024  # Also admits the historical full-world name-only dump.
MAX_NODES = 1500000
LEGACY_SHA256 = "0d8137ac3c497bc316916768aa63b1c99b99da97739de801c3e1fd776601937e"
ROLES = {
    "buy": ("Functions/CoffeeButton", "Buy", ("Wait player", "Wait button", "Purchase")),
    "acquire": ("Functions/GetACup", "Use", ("Wait player", "Wait button", "State 1", "State 2")),
    "target": ("Functions/PanTarget", "Data", ("ON", "OFF")),
    "cup": ("Functions/CupPivot/coffee cup(itemx)", "Use",
            ("Wait player", "Wait button", "Cup full?", "Pour", "Delay", "Check drink 2", "Play anim", "State 1", "Data")),
}
# Topic values specify admissible serialized record types, not native semantics.
# Every topic requires source pointers; there is deliberately no literal-claim API.
TOPICS = {
    "price": {"payment": ("action",), "amount": ("parameter", "scan"),
              "wallet": ("reference", "scan"), "failure": ("transition", "action")},
    "references": {"directTargets": ("reference",)},
    "actions": {"orderedFields": ("action",), "nativeRoutes": ("transition",)},
    "cupLifecycle": {"acquisition": ("action",), "identity": ("parameter",),
                     "fill": ("action",), "consume": ("action",), "retirement": ("action",), "reuse": ("action",)},
    "playerEffect": {"emitter": ("action",), "receiver": ("fsm",), "assignments": ("action",),
                     "animation": ("action",), "beforeEffectSeam": ("action",)},
    "persistence": {"writers": ("action", "component", "scan"), "keys": ("parameter", "scan"),
                    "savedFields": ("parameter", "scan"), "resetFields": ("parameter", "scan"),
                    "externalCoverage": ("scan",)},
    "initialization": {"start": ("fsm",), "defaults": ("parameter",), "reset": ("action",),
                       "load": ("action", "scan"), "lod": ("action",), "externalCoverage": ("scan",)},
    "geometry": {"transforms": ("transform",), "colliders": ("collider",), "physics": ("rigidbody",),
                 "holder": ("reference",), "range": ("parameter",), "fill": ("parameter",)},
}
NEXT = {
    "price": "Extract Purchase debit/check ordering, literal or local/global amount and units, wallet target, failure branches; explicit source-wide negative evidence if free.",
    "references": "Resolve file/path IDs and full object/FSM paths for Pan, TargetPan, GetACup targets, CupPivot, HandDrink, Mesh, Pivot and all directly affected controllers.",
    "actions": "Extract every ordered action (including disabled), enabled flags, typed serialized fields, variable/default flags, native start and local/global event routes for all four roles and direct targets.",
    "cupLifecycle": "Extract acquisition, native identity, activation/clone/reparent/reset, fill/consume bounds and removal/return/reuse writers; choose no serving/lifetime model yet.",
    "playerEffect": "Resolve the actual cup emitter to the player Drink receiver, assignments, animation/thrown objects and before-effect suppression seam; do not select an event by name.",
    "persistence": "Enumerate external parent/global/reference-chain/component save writers, keys and saved/reset contents/pose/availability with complete source inventory; absence of Save names is not absence proof.",
    "initialization": "Extract start/data/reset/load order, variable defaults and external/LOD initialization sources; distinguish cold boot, host save reload and live join.",
    "geometry": "Extract local transforms/parents, collider and rigidbody parameters, holder/placement/range and fill geometry; no household constants.",
}
CLAIMS = {key: "NOT_TESTED" for key in (
    "native_execution", "host_action", "guest_action", "native_result", "rejoin",
    "ordinary_input", "different_saves", "save_reload", "steam_two_pc", "four_player_soak")}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return (json.dumps(value, sort_keys=True, indent=2, ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def keys(value, expected, where):
    require(type(value) is dict, where + ": expected object")
    missing, extra = set(expected) - value.keys(), value.keys() - set(expected)
    require(not missing and not extra, where + ": missing=" + repr(sorted(missing)) + " extra=" + repr(sorted(extra)))


def text(value, where):
    require(type(value) is str and 0 < len(value) <= 4096 and not any(ord(c) < 32 for c in value), where + ": invalid string")


def sequence(value, where, maximum=256, minimum=0):
    require(type(value) is list and minimum <= len(value) <= maximum, where + ": invalid/bounded list")


def unique(values, where):
    require(len(values) == len(set(values)), where + ": ambiguous duplicate")


def safe_name(value, where):
    text(value, where)
    require(not value.startswith("/") and "\\" not in value and all(p not in ("", ".", "..") for p in value.split("/")), where + ": traversal/ambiguous path")


def hash_value(value, where):
    require(type(value) is str and re.fullmatch(r"[0-9a-f]{64}", value) is not None, where + ": invalid SHA256")


def load_json(raw):
    require(len(raw) <= MAX_BYTES, "input exceeds byte bound")
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate JSON key: " + key)
            result[key] = value
        return result
    def invalid(value):
        raise ValueError("nonfinite JSON: " + value)
    value = json.loads(raw.decode("utf-8"), object_pairs_hook=pairs, parse_constant=invalid)
    pending, count = [(value, 0)], 0
    while pending:
        node, depth = pending.pop(); count += 1
        require(depth <= 64 and count <= MAX_NODES, "JSON depth/node bound exceeded")
        if type(node) is float: require(math.isfinite(node), "nonfinite JSON number")
        if type(node) is dict: pending.extend((v, depth + 1) for v in node.values())
        if type(node) is list: pending.extend((v, depth + 1) for v in node)
    return value


def identity(value):
    return value["fileId"], value["pathId"]


class Graph:
    def __init__(self, doc):
        self.doc, self.ids, self.pointers, self.references, self.bindings = doc, {}, {}, [], []
        self.fsm_edges, self.record_kinds = [], {}

    def register(self, record, pointer, kind):
        safe_name(record["fileId"], pointer + "/fileId")
        require("/" not in record["fileId"], pointer + ": fileId is an asset identifier, not a filesystem path")
        require(record["fileId"] in self.assets, pointer + ": unidentified source asset")
        require(type(record["pathId"]) is int and 0 < record["pathId"] < 2**63, pointer + ": invalid pathId")
        safe_name(record["path"], pointer + "/path")
        require("/CoffeeAutomatic/" not in record["path"] or record["path"].startswith(ROOT + "/"), pointer + ": other machine variant forbidden")
        require(identity(record) not in self.ids, pointer + ": ambiguous identity")
        self.ids[identity(record)] = record
        self.record_kinds[identity(record)] = kind
        self.pointers[pointer] = (kind, record)

    def reference(self, value, where, owner=None):
        keys(value, ("fileId", "pathId", "path", "fsmName"), where + " reference")
        safe_name(value["fileId"], where + " reference fileId")
        require(type(value["pathId"]) is int, where + " reference pathId")
        safe_name(value["path"], where + " reference path")
        require(value["fsmName"] is None or type(value["fsmName"]) is str, where + " reference fsmName")
        self.references.append((value, where, owner))

    def parameters(self, values, pointer, owner=None, defaults=False):
        sequence(values, pointer, 256)
        names = []
        for i, p in enumerate(values):
            where = pointer + "/" + str(i)
            keys(p, ("field", "type", "value", "useVariable", "useDefault", "binding"), where)
            text(p["field"], where + "/field"); names.append(p["field"])
            require(type(p["useVariable"]) is bool and type(p["useDefault"]) is bool, where + ": invalid flags")
            require(not (p["useVariable"] and p["useDefault"]), where + ": conflicting flags")
            require(not defaults or not (p["useVariable"] or p["useDefault"]), where + ": variable defaults must be literal")
            kind, value = p["type"], p["value"]
            require(type(kind) is str and kind in ("Float", "Int", "Bool", "String", "Event", "Vector3", "Quaternion", "GameObject", "Fsm"), where + ": unsupported type")
            if kind == "Float": require(type(value) in (int, float) and math.isfinite(value), where + ": invalid Float value")
            elif kind == "Int": require(type(value) is int and -2**31 <= value < 2**31, where + ": invalid Int value")
            elif kind == "Bool": require(type(value) is bool, where + ": invalid Bool value")
            elif kind in ("String", "Event"): require(type(value) is str and len(value) <= 4096, where + ": invalid string value")
            elif kind in ("Vector3", "Quaternion"): self.vector(value, 3 if kind == "Vector3" else 4, where)
            elif value is not None:
                self.reference(value, where, owner)
                require((value["fsmName"] is None) == (kind == "GameObject"), where + ": reference type mismatch")
            if p["useVariable"]:
                keys(p["binding"], ("scope", "name"), where + " binding")
                require(p["binding"]["scope"] in ("local", "global"), where + " binding scope")
                text(p["binding"]["name"], where + " binding name")
                self.bindings.append((p, owner, where))
            else: require(p["binding"] is None, where + ": literal/default binding must be null")
            self.pointers[where] = ("reference" if kind in ("GameObject", "Fsm") and value is not None else "parameter", p)
        unique(names, pointer)

    @staticmethod
    def vector(value, length, where):
        require(type(value) is list and len(value) == length and all(type(v) in (int, float) and math.isfinite(v) for v in value), where + ": invalid vector")

    def transitions(self, values, pointer, states):
        sequence(values, pointer)
        events = []
        for i, t in enumerate(values):
            where = pointer + "/" + str(i)
            keys(t, ("event", "to"), where); text(t["event"], where)
            require(type(t["to"]) is str and t["to"] in states, where + ": unresolved transition target")
            events.append(t["event"]); self.pointers[where] = ("transition", t)
        unique(events, pointer)

    def validate(self):
        doc = self.doc
        assets = doc["provenance"]["assets"]
        sequence(assets, "provenance/assets", 32, 1)
        for asset in assets:
            keys(asset, ("fileId", "sha256", "bytes"), "provenance/assets")
            safe_name(asset["fileId"], "provenance/assets/fileId")
            require("/" not in asset["fileId"], "source asset must be an identifier")
            hash_value(asset["sha256"], "provenance/assets")
            require(type(asset["bytes"]) is int and asset["bytes"] > 0, "source asset byte count")
        self.assets = [a["fileId"] for a in assets]
        unique(self.assets, "source assets")
        require(doc["provenance"]["sourceSha256"] == sha(canonical(assets)), "source manifest SHA256 mismatch")
        sequence(doc["objects"], "objects", 128, 4)
        sequence(doc["fsms"], "fsms", 128, 4)
        sequence(doc["components"], "components", 128)
        unique([o["path"] for o in doc["objects"]], "object paths")
        unique([(f["path"], f["fsmName"]) for f in doc["fsms"]], "FSM paths/names")
        for i, obj in enumerate(doc["objects"]):
            where = "/objects/" + str(i)
            keys(obj, ("fileId", "pathId", "path", "activeSelf", "parent", "transform", "colliders", "rigidbody"), where)
            self.register(obj, where, "object")
            require(type(obj["activeSelf"]) is bool, where + ": activeSelf")
            if obj["parent"] is not None:
                self.reference(obj["parent"], where + "/parent")
                require(obj["parent"]["fsmName"] is None, where + ": parent must be a GameObject")
            t = obj["transform"]
            keys(t, ("space", "position", "rotation", "scale"), where + "/transform")
            require(t["space"] == "local", where + ": explicit local coordinate space required")
            for key, length in (("position", 3), ("rotation", 4), ("scale", 3)):
                self.vector(t[key], length, where + "/transform/" + key)
            self.pointers[where + "/transform"] = ("transform", t)
            sequence(obj["colliders"], where + "/colliders", 32)
            for c, collider in enumerate(obj["colliders"]):
                cp = where + "/colliders/" + str(c)
                keys(collider, ("type", "enabled", "isTrigger", "parameters"), cp)
                text(collider["type"], cp + "/type")
                require(type(collider["enabled"]) is bool and type(collider["isTrigger"]) is bool, cp + ": flags")
                sequence(collider["parameters"], cp + "/parameters", 256, 1)
                self.parameters(collider["parameters"], cp + "/parameters")
                self.pointers[cp] = ("collider", collider)
            body = obj["rigidbody"]
            if body is not None:
                keys(body, ("parameters",), where + "/rigidbody")
                sequence(body["parameters"], where + "/rigidbody/parameters", 256, 1)
                self.parameters(body["parameters"], where + "/rigidbody/parameters")
                self.pointers[where + "/rigidbody"] = ("rigidbody", body)
        self.parameters(doc["globals"], "/globals", defaults=True)
        for i, fsm in enumerate(doc["fsms"]):
            where = "/fsms/" + str(i)
            keys(fsm, ("fileId", "pathId", "path", "fsmName", "enabled", "startState", "globalTransitions", "variables", "states"), where)
            self.register(fsm, where, "fsm"); text(fsm["fsmName"], where)
            require(type(fsm["enabled"]) is bool, where + ": enabled")
            require(any(o["path"] == fsm["path"] and o["fileId"] == fsm["fileId"] for o in doc["objects"]), where + ": missing FSM GameObject")
            self.parameters(fsm["variables"], where + "/variables", fsm, defaults=True)
            sequence(fsm["states"], where + "/states", 256, 1)
            states = [s["name"] for s in fsm["states"]]
            for name in states: text(name, where + "/state name")
            unique(states, where + "/states")
            require(fsm["startState"] in states, where + ": missing native start state")
            self.transitions(fsm["globalTransitions"], where + "/globalTransitions", states)
            for s, state in enumerate(fsm["states"]):
                sp = where + "/states/" + str(s)
                keys(state, ("name", "transitions", "actions"), sp)
                self.transitions(state["transitions"], sp + "/transitions", states)
                sequence(state["actions"], sp + "/actions", 256)
                for a, action in enumerate(state["actions"]):
                    ap = sp + "/actions/" + str(a)
                    keys(action, ("type", "enabled", "parameters"), ap)
                    text(action["type"], ap + "/type")
                    require(type(action["enabled"]) is bool, ap + ": enabled flag missing/malformed")
                    self.parameters(action["parameters"], ap + "/parameters", fsm)
                    self.pointers[ap] = ("action", action)
        for i, component in enumerate(doc["components"]):
            cp = "/components/" + str(i)
            keys(component, ("fileId", "pathId", "path", "componentType", "enabled", "parameters"), cp)
            self.register(component, cp, "component")
            text(component["componentType"], cp + "/componentType")
            require(type(component["enabled"]) is bool, cp + ": enabled")
            require(any(o["path"] == component["path"] and o["fileId"] == component["fileId"] for o in doc["objects"]), cp + ": missing component GameObject")
            sequence(component["parameters"], cp + "/parameters", 256, 1)
            self.parameters(component["parameters"], cp + "/parameters", component)
        roles = []
        for role, (suffix, name, required_states) in ROLES.items():
            matches = [f for f in doc["fsms"] if f["path"] == ROOT + "/" + suffix and f["fsmName"] == name]
            require(len(matches) == 1, "role " + role + ": expected exactly one identified FSM")
            require(set(required_states) <= {s["name"] for s in matches[0]["states"]}, "role " + role + ": missing foundation state")
            roles.append(identity(matches[0]))
        for ref, where, owner in self.references:
            self.resolve(ref, where)
            if owner is not None: self.fsm_edges.append((identity(owner), identity(ref)))
        for obj in doc["objects"]:
            seen, current = set(), obj
            while current["parent"] is not None:
                require(identity(current) not in seen, "parent cycle: " + obj["path"])
                seen.add(identity(current))
                current = self.resolve(current["parent"], "parent")
                require(self.record_kinds[identity(current)] == "object", "parent must be a GameObject")
        for parameter, owner, where in self.bindings:
            binding = parameter["binding"]
            variables = doc["globals"] if binding["scope"] == "global" else ([] if owner is None else owner.get("variables", []))
            matches = [p for p in variables if p["field"] == binding["name"] and p["type"] == parameter["type"]]
            require(len(matches) == 1, where + ": unresolved/ambiguous variable binding")
        self.scans()
        # Outbound references admit only direct targets. Inbound external writers
        # must also appear in the complete scan; arbitrary extra worlds stay out.
        reached = set(roles)
        for _ in range(len(self.ids)):
            previous = set(reached)
            paths = {self.ids[key]["path"] for key in reached}
            reached.update(identity(f) for f in doc["fsms"] + doc["components"] if f["path"] in paths)
            reached.update(identity(o) for o in doc["objects"] if o["path"] in paths)
            reached.update(b for a, b in self.fsm_edges if a in reached)
            reached.update(a for a, b in self.fsm_edges if b in reached and a in self.writers)
            if reached == previous: break
        for fsm in doc["fsms"] + doc["components"]:
            require(identity(fsm) in reached, "unrelated FSM outside one-machine reference closure: " + fsm["path"])
        relevant_paths = {f["path"] for f in doc["fsms"] + doc["components"]}
        relevant_paths.update(ref["path"] for ref, _, _ in self.references)
        for obj in doc["objects"]:
            require(obj["path"] in relevant_paths, "unrelated object: " + obj["path"])
        self.foundation()

    def resolve(self, ref, where):
        target = self.ids.get(identity(ref))
        require(target is not None and target["path"] == ref["path"] and target.get("fsmName") == ref["fsmName"], where + ": unresolved/mismatched reference")
        return target

    def scans(self):
        sequence(self.doc["scans"], "scans", 3, 2)
        purposes, self.writers, self.writers_by_purpose = [], set(), {}
        for i, scan in enumerate(self.doc["scans"]):
            where = "/scans/" + str(i)
            keys(scan, ("purpose", "sourceIds", "scope", "complete", "writers", "absence", "method"), where)
            require(scan["purpose"] in ("persistence", "initialization", "price"), where + ": purpose")
            purposes.append(scan["purpose"])
            require(scan["sourceIds"] == [self.doc["provenance"]["sourceId"]], where + ": source inventory mismatch")
            require(scan["complete"] is True and type(scan["absence"]) is bool, where + ": incomplete scan")
            text(scan["method"], where + "/method")
            sequence(scan["scope"], where + "/scope", 128, 4)
            sequence(scan["writers"], where + "/writers", 128)
            require(bool(scan["writers"]) != scan["absence"], where + ": writers/absence contradiction")
            for ref in scan["scope"] + scan["writers"]:
                self.reference(ref, where); self.resolve(ref, where)
            scope = [identity(r) for r in scan["scope"]]; unique(scope, where + "/scope")
            require(set(scope) == {identity(f) for f in self.doc["fsms"] + self.doc["components"]}, where + ": incomplete external scope")
            writers = [identity(r) for r in scan["writers"]]; unique(writers, where + "/writers")
            require(set(writers) <= set(scope), where + ": writer outside scope")
            self.writers.update(writers); self.pointers[where] = ("scan", scan)
            self.writers_by_purpose[scan["purpose"]] = set(writers)
        unique(purposes, "scans/purpose")
        require({"persistence", "initialization"} <= set(purposes), "scans: missing external coverage")

    def foundation(self):
        keys(self.doc["foundation"], TOPICS, "foundation")
        for section, topics in TOPICS.items():
            data = self.doc["foundation"][section]
            keys(data, topics, "foundation/" + section)
            for topic, kinds in topics.items():
                where = "foundation/" + section + "/" + topic
                claim = data[topic]
                keys(claim, ("origin", "pointers"), where)
                require(claim["origin"] == "serialized", where + ": guessed/inferred claims forbidden")
                sequence(claim["pointers"], where, 128, 1)
                for pointer in claim["pointers"]:
                    require(type(pointer) is str and pointer in self.pointers, where + ": unresolved source pointer")
                    kind, value = self.pointers[pointer]
                    require(kind in kinds, where + ": wrong pointer record type")
                    if section == "price" and kind != "scan":
                        require(pointer.startswith("/fsms/"), where + ": price must cite native Buy fields/routes")
                        fsm = self.doc["fsms"][int(pointer.split("/")[2])]
                        require(fsm["path"] == ROOT + "/" + ROLES["buy"][0] and fsm["fsmName"] == "Buy", where + ": price must cite native Buy")
                        if topic == "payment":
                            parts = pointer.split("/")
                            require("states" in parts and fsm["states"][int(parts[4])]["name"] == "Purchase", where + ": payment must cite Purchase action")
                        if topic == "amount":
                            require(value["type"] in ("Float", "Int"), where + ": numeric amount field required")
                    if section == "persistence" and kind != "scan":
                        parts = pointer.split("/")
                        require(parts[1] in ("fsms", "components"), where + ": writer fields required")
                        owner = self.doc[parts[1]][int(parts[2])]
                        require(identity(owner) in self.writers_by_purpose["persistence"], where + ": not an identified persistence writer")
                        if topic == "keys": require(value["type"] == "String" and value["value"], where + ": nonempty serialized writer key required")
                    if section == "playerEffect" and topic == "receiver":
                        require(not value["path"].startswith(ROOT + "/"), where + ": receiver must be external to machine")
                        cup = next(f for f in self.doc["fsms"] if f["path"] == ROOT + "/" + ROLES["cup"][0] and f["fsmName"] == "Use")
                        require((identity(cup), identity(value)) in self.fsm_edges, where + ": receiver needs resolved cup emitter target")
                    if kind == "scan":
                        require(value["purpose"] == section, where + ": wrong scan purpose")
                        if topic != "externalCoverage": require(value["absence"], where + ": no negative evidence")
                unique(claim["pointers"], where)
        effect = self.doc["foundation"]["playerEffect"]
        require(len(effect["receiver"]["pointers"]) == 1, "playerEffect: ambiguous receiver")
        receiver = self.pointers[effect["receiver"]["pointers"][0]][1]
        receiver_events = {t["event"] for t in receiver["globalTransitions"]}
        receiver_events.update(t["event"] for s in receiver["states"] for t in s["transitions"])
        for pointer in effect["emitter"]["pointers"]:
            parts = pointer.split("/")
            require(parts[1] == "fsms", "playerEffect: emitter must be cup FSM action")
            owner = self.doc["fsms"][int(parts[2])]
            require(owner["path"] == ROOT + "/" + ROLES["cup"][0] and owner["fsmName"] == "Use",
                    "playerEffect: emitter must be cup FSM action")
            action = self.pointers[pointer][1]
            require(action["enabled"], "playerEffect: emitter is disabled")
            targets = [p["value"] for p in action["parameters"] if p["type"] in ("Fsm", "GameObject") and p["value"] is not None]
            require(any(t["path"] == receiver["path"] and t["fileId"] == receiver["fileId"] and
                        (t["fsmName"] is None or identity(t) == identity(receiver)) for t in targets),
                    "playerEffect: emitter/receiver target mismatch")
            events = [p["value"] for p in action["parameters"] if p["type"] == "Event" and not p["useVariable"] and not p["useDefault"]]
            require(len(events) == 1 and events[0] in receiver_events, "playerEffect: unresolved/ambiguous receiver event")
        for pointer in effect["assignments"]["pointers"]:
            parts = pointer.split("/")
            require(parts[1] == "fsms" and identity(self.doc["fsms"][int(parts[2])]) == identity(receiver),
                    "playerEffect: assignments must cite receiver actions")


def validate_bytes(raw, *, expected_sha256, build_id, source_id, source_sha256):
    result = {"schema_version": 1, "root": ROOT, "status": "REJECTED", "production_ready": False,
              "acceptance_scope": "structural fields only; caller-pinned provenance is not independent source authentication or native semantic proof",
              "input_sha256": sha(raw), "evidence_level": "unverified-input", "errors": [],
              "fields": {name: {"status": "MISSING", "required": NEXT[name], "topics": list(topics)} for name, topics in TOPICS.items()},
              "gameplay_claims": dict(CLAIMS), "next_contract": "docs/H10-VENDOR-COFFEE-FOUNDATION.md#bounded-next-native-extraction-contract-not-authorization-to-execute"}
    try:
        for value, label in ((expected_sha256, "input"), (source_sha256, "source")): hash_value(value, label)
        text(build_id, "expected buildId"); text(source_id, "expected sourceId")
        require(sha(raw) == expected_sha256, "input SHA256 mismatch")
        doc = load_json(raw)
        if type(doc) is dict and "meta" in doc and "fsms" in doc and "schemaVersion" not in doc:
            require(type(doc["meta"]) is dict and type(doc["fsms"]) is list, "malformed legacy dump")
            require((sha(raw), build_id, source_id, source_sha256) ==
                    (LEGACY_SHA256, "23268598", "historical-dump-23268598", LEGACY_SHA256),
                    "unidentified legacy input or build/source mismatch; supply a fully pinned serialized schema")
            result.update(status="MISSING_FIELDS", evidence_level="historical-topology-only",
                          legacy_meta=doc["meta"], declared_build_id=build_id, declared_source_id=source_id,
                          build_identity_verified=False)
            result["errors"] = ["Legacy dump/extract has no pinned full serialized schema: action names/topology do not establish native values or persistence."]
            return result
        keys(doc, ("schemaVersion", "kind", "provenance", "root", "objects", "fsms", "components", "globals", "scans", "foundation"), "extraction")
        require(type(doc["schemaVersion"]) is int and doc["schemaVersion"] == 1, "unsupported schemaVersion")
        require(doc["kind"] == "owc-inspection-coffee-serialized" and doc["root"] == ROOT, "one Inspection root only")
        p = doc["provenance"]
        keys(p, ("buildId", "sourceId", "sourceSha256", "extractorId", "extractorVersion", "evidenceClass", "assets"), "provenance")
        for key in p:
            if key != "assets": text(p[key], "provenance/" + key)
        require((p["buildId"], p["sourceId"], p["sourceSha256"]) == (build_id, source_id, source_sha256), "build/source mismatch")
        require(p["evidenceClass"] in ("serialized-asset", "synthetic-fixture"), "unpermitted evidence class")
        Graph(doc).validate()
        result.update(status="ACCEPTED_FIELDS", extraction=doc, evidence_level=p["evidenceClass"])
        for name in TOPICS:
            result["fields"][name].update(status="ACCEPTED", evidence=doc["foundation"][name])
    except (ValueError, KeyError, TypeError, RecursionError, OverflowError) as error:
        result["errors"] = [str(error)]
    return result


def path_parts(value):
    require(type(value) is str and value.startswith("/") and "\\" not in value, "absolute nonredirected path required")
    parts = value.split("/")[1:]
    require(all(p not in ("", ".", "..") for p in parts), "path traversal/normalization forbidden")
    return parts


def open_directory(value):
    parts = path_parts(str(value))
    fd = os.open("/", os.O_RDONLY | os.O_DIRECTORY)
    try:
        for part in parts:
            child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd); fd = child
        return fd
    except BaseException:
        os.close(fd); raise


def read_permitted(value, roots, max_bytes=MAX_BYTES):
    parts = path_parts(value)
    path = Path(value)
    require(any(path.is_relative_to(root) for root in roots), "input outside permitted source/RUN roots")
    parent = open_directory("/" + "/".join(parts[:-1]))
    try:
        fd = os.open(parts[-1], os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=parent)
        try:
            before = os.fstat(fd)
            require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1, "input must be regular, not symlink/hardlink/device")
            require(before.st_size <= max_bytes, "input exceeds byte bound")
            with os.fdopen(fd, "rb", closefd=False) as stream: raw = stream.read(max_bytes + 1)
            after = os.fstat(fd)
            require(len(raw) <= max_bytes and len(raw) == before.st_size, "input size changed/exceeded bound")
            require((before.st_size, before.st_mtime_ns, before.st_ctime_ns) == (after.st_size, after.st_mtime_ns, after.st_ctime_ns), "input changed while reading")
            return raw
        finally: os.close(fd)
    finally: os.close(parent)


def validate_run(value, rounds=None):
    rounds = SOURCE.parent / "rounds" if rounds is None else rounds
    path_parts(value); path = Path(value)
    require(path.parent == rounds and re.fullmatch(r"[0-9]{6}-work", path.name), "use the absolute assigned work RUN")
    doc = load_json(read_permitted(str(path / "contract.json"), [rounds], 128 * 1024))
    require(doc.get("work_type") == "infrastructure" and "H10" in doc.get("ids", []), "RUN must have an H10 infrastructure contract")
    return path


def emit(run, name, raw, report, invocation):
    require(type(name) is str and re.fullmatch(r"[a-z0-9][a-z0-9-]{0,95}", name), "new lowercase leaf required")
    fd = open_directory(str(run))
    try:
        try:
            os.stat(name, dir_fd=fd, follow_symlinks=False)
        except FileNotFoundError:
            pass
        else:
            raise FileExistsError("Refuse existing evidence: " + name)
        require(report.get("input_sha256") == sha(raw), "report/input SHA256 mismatch")
        os.mkdir(name, mode=0o700, dir_fd=fd)  # Atomic no-clobber, including symlinks.
        out = os.open(name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
        try:
            def write(leaf, data):
                target = os.open(leaf, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=out)
                with os.fdopen(target, "wb") as stream: stream.write(data)
            encoded = canonical(report)
            write("input.json", raw); write("report.json", encoded)
            receipt = dict(invocation, input_sha256=sha(raw), output_sha256=sha(encoded),
                           input_bytes=len(raw), output_bytes=len(encoded),
                           status=report.get("status"), exit_code={"ACCEPTED_FIELDS": 0, "MISSING_FIELDS": 2}.get(report.get("status"), 1))
            write("receipt.json", canonical(receipt))
        finally: os.close(out)
    finally: os.close(fd)
    return run / name


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("run", "name", "input", "input-sha256", "build-id", "source-id", "source-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        run = validate_run(args.run)
        raw = read_permitted(args.input, [SOURCE / "catalog", run / "permitted-inputs"])
        report = validate_bytes(raw, expected_sha256=args.input_sha256, build_id=args.build_id,
                                source_id=args.source_id, source_sha256=args.source_sha256)
        receipt = {"argv": [sys.executable, *sys.argv], "cwd": str(Path.cwd()), "assigned_run": str(run),
                   "utc": datetime.now(timezone.utc).isoformat(), "role": "offline-serialized-field-validator",
                   "input_path": args.input, "expected_input_sha256": args.input_sha256,
                   "expected_build_id": args.build_id, "expected_source_id": args.source_id,
                   "expected_source_sha256": args.source_sha256,
                   "tool_sha256": sha(read_permitted(str(Path(__file__).absolute()), [SOURCE / "tools"])),
                   "contract_sha256": sha(read_permitted(str(run / "contract.json"), [run])),
                   "cleanup": {"game_processes_started": 0, "subprocesses_started": 0, "protected_inputs_read": False,
                               "rig_touched": False, "deployments": 0, "temporary_files_created": 0},
                   "limits": "Structural evidence validation only; pins supplied by caller, no authentication of extractor or native source. Never promotes runtime/gameplay readiness."}
        output = emit(run, args.name, raw, report, receipt)
        print(report["status"] + ": " + str(output / "report.json"))
        for name, field in report["fields"].items(): print(name + ": " + field["status"] + " — " + field["required"])
        for error in report["errors"]: print("DIAGNOSTIC: " + error)
        return {"ACCEPTED_FIELDS": 0, "MISSING_FIELDS": 2}.get(report["status"], 1)
    except (OSError, ValueError) as error:
        print("REJECTED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
