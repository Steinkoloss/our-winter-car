#!/usr/bin/env python3
"""H10 read-only CoffeeAutomatic topology audit of the existing source catalog.

Exit 2: precise missing native evidence (expected for toolsVersion 0.1.0).
Exit 1: invalid input/output or operational failure. Never launches or builds.
This checker cannot promote production/native readiness, even with newer fields.
"""
from __future__ import annotations
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sys

from check_fsm_bindings import purchase_path_matches

SOURCE = Path(__file__).resolve().parents[1]
EXPECTED_ROOTS = ("INSPECTION/LOD/CoffeeAutomatic",
                  "JOBS/FACTORY/OpeningTimes/LOD1/Kitchen/CoffeeAutomatic")
ROLE_SCHEMA = {
    "purchase": ("Functions/CoffeeButton", "Buy", "Wait player",
                 {"Wait player", "Wait button", "Purchase"},
                 [("Wait player", "FINISHED", "Wait button"), ("Wait button", "FINISHED", "Wait player"),
                  ("Wait button", "USE", "Purchase"), ("Purchase", "FINISHED", "Wait player")],
                 {"StringVariables": ["Notification"], "GameObjectVariables": ["Pan"]}),
    "acquire": ("Functions/GetACup", "Use", "Wait player",
                {"Wait player", "Wait button", "State 1", "State 2"},
                [("Wait player", "FINISHED", "Wait button"), ("Wait button", "FINISHED", "Wait player"),
                 ("Wait button", "USE", "State 1"), ("State 1", "FINISHED", "State 2")], {}),
    "pour": ("Functions/PanTarget", "Data", "OFF", {"ON", "OFF"},
             [("ON", "FINISHED", "OFF")], {"BoolVariables": ["Pouring"]}),
    "cup": ("Functions/CupPivot/coffee cup(itemx)", "Use", "Wait player",
            {"Pour", "Cup full?", "Wait player", "Wait button", "Delay", "Play anim", "Data", "Check drink 2", "State 1"},
            [("Pour", "LOOP", "Delay"), ("Cup full?", "LOOP", "Delay"), ("Cup full?", "FINISHED", "Pour"),
             ("Wait player", "FINISHED", "Wait button"), ("Wait player", "POUR", "Cup full?"),
             ("Wait button", "FINISHED", "Delay"), ("Wait button", "POUR", "Cup full?"),
             ("Wait button", "USE", "Check drink 2"), ("Delay", "FINISHED", "Wait player"),
             ("Play anim", "FINISHED", "State 1"), ("Data", "FINISHED", "Wait player"),
             ("Check drink 2", "STOP", "Delay"), ("Check drink 2", "USE", "Play anim")],
            {"FloatVariables": ["Coffee", "Distance", "Pos", "Scale"], "BoolVariables": ["Pouring"],
             "GameObjectVariables": ["HandDrink", "Mesh", "Pivot", "TargetPan"]}),
}
# Full hashes plus bounded line-addressable excerpts; no historical build/save logs.
INPUT_RANGES = {
    "catalog/dump-23268598.json": None, "catalog/sync-catalog.json": None,
    "src/WinterMP.Core/Sync/ItemWorldSync.Coffee.cs": [(1, 194)],
    "src/WinterMP.Core/Sync/ItemWorldSync.CoffeeInput.cs": [(1, 181)],
    "src/WinterMP.Core/Sync/ItemWorldSync.CoffeeState.cs": [(1, 94)],
    "src/WinterMP.Core/Sync/ItemWorldSync.CoffeePackets.cs": [(1, 97)],
    "src/WinterMP.Core/Sync/ItemWorldSync.Scan.cs": [(11, 151)],
    "src/WinterMP.Core/Catalog/VehicleCatalogConfig.cs": [(62, 145)],
    "src/WinterMP.Core/Sync/WalletSync.cs": [(9, 35), (54, 66)],
    "src/WinterMP.Core/Catalog/SyncCatalogJson.Coffee.cs": [(1, 22)],
    "src/WinterMP.Core/Catalog/SyncCatalog.cs": [(400, 415), (700, 770)],
    "src/WinterMP.Core/Catalog/ShopBuyInference.cs": [(1, 47)],
    "src/WinterMP.Core/Sync/FsmWorldSync.Registry.cs": [(12, 35), (116, 161)],
    "src/WinterMP.Core/Sync/FsmWorldSync.Local.cs": [(52, 114), (146, 258)],
    "src/WinterMP.Core/Sync/FsmWorldSync.Remote.cs": [(142, 180), (234, 258)],
    "src/WinterMP.Core/Sync/WorldSyncManager.cs": [(530, 599)],
    "src/WinterMP.Core/Sync/WorldSyncManager.Handlers.cs": [(74, 76), (143, 148)],
    "src/WinterMP.Core/Sync/WorldSyncManager.Snapshots.cs": [(100, 110), (160, 170), (260, 270)],
    "src/WinterMP.Core/Session/SessionManager.Messages.cs": [(838, 846)],
    "src/WinterMP.Net/Sync/CoffeePolicy.cs": [(1, 33)],
    "src/WinterMP.Net/Messages/CoffeeMessages.cs": [(1, 37)],
    "src/WinterMP.Net.Tests/CoffeeTests.cs": [(1, 93)],
    "src/WinterMP.Net/SessionMessagePolicy.cs": [(1, 46)],
    "src/WinterMP.Net/Protocol.cs": None,
    "src/WinterMP.Tools/FsmDumperPlugin.cs": [(188, 258)],
    "src/WinterMP.Tools/WinterMP.Tools.csproj": [(1, 20)],
    "docs/BUILDING.md": [(9981, 10052)], "protocol/PROTOCOL.md": [(345, 380)],
    "docs/H10-VENDOR-COFFEE-AUDIT.md": None,
    "PLAN.md": [(60, 67), (295, 331)],
    "tools/check_fsm_bindings.py": [(62, 70)],
    "tools/h10_vendor_coffee_audit.py": None, "tools/h10_vendor_coffee_verify.py": None,
    "tools/tests/test_h10_vendor_coffee_audit.py": None,
}


def utc():
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds")


def digest_bytes(data):
    return hashlib.sha256(data).hexdigest()


def source_file(root, relative):
    path = root / relative
    if path != path.resolve() or not path.is_relative_to(root) or not path.is_file():
        raise ValueError("Refuse missing/redirected/out-of-source input: " + str(path))
    return path


def validate_run(path, rounds_root=None):
    rounds = SOURCE.parent / "rounds" if rounds_root is None else rounds_root
    path = Path(path)
    if not path.is_absolute() or path != path.resolve() or path.parent != rounds:
        raise ValueError("--run must be an absolute assigned round path (no symlinks)")
    contract = source_file(path, "contract.json")
    data = json.loads(contract.read_text())
    if data.get("work_type") != "audit" or "H10" not in data.get("ids", []):
        raise ValueError("The assigned RUN must contain an H10 audit contract")
    return path


def create_output(run, name):
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", name):
        raise ValueError("--name must be a new lowercase/digit/hyphen leaf")
    output = run / name
    output.mkdir(exist_ok=False)
    return output


def save(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")


def relevant(fsm):
    return ("coffee" in fsm["path"].lower() or "coffee" in fsm["fsmName"].lower()
            or any("COFFEE" in e for e in fsm.get("events", [])))


def sorted_records(records):
    return sorted(records, key=lambda f: (f["path"], f.get("fsmName", ""), json.dumps(f, sort_keys=True)))


def edges(fsm):
    return sorted([s["name"], t["event"], t["to"]]
                  for s in fsm["states"] for t in s.get("transitions", []))


def summarize(fsm, entry=None):
    es = edges(fsm)
    states = sorted(s["name"] for s in fsm["states"])
    reached = set() if entry is None or entry not in states else {entry}
    pending = list(reached)
    while pending:
        current = pending.pop()
        for start, _, end in es:
            if start == current and end in states and end not in reached:
                reached.add(end)
                pending.append(end)
    variables = {k: sorted(v) for k, v in fsm.get("variables", {}).items() if v}
    return {"path": fsm["path"], "fsmName": fsm["fsmName"], "dump_net_id": fsm["netId"],
            "active_at_dump": fsm.get("active"), "active_state_at_dump": fsm.get("activeState"),
            "states": states, "edges": es, "named_entry_for_graph_query": entry,
            "reachable_from_named_entry": sorted(reached),
            "reachability_limit": "Conditional state-edge reachability only; native start/action conditions and LOD availability not proved",
            "states_without_outgoing_edges": sorted(set(states) - {s for s, _, _ in es}),
            "events_without_state_edge": sorted(set(fsm.get("events", [])) - {e for _, e, _ in es}),
            "global_transitions_known": "globalTransitions" in fsm,
            "action_fields_known": all("actions" in s for s in fsm["states"]),
            "action_types_known": all("actionTypes" in s for s in fsm["states"]),
            "variables": variables, "variable_values_known": False}


def analyze(dump, catalog):
    fsms = sorted_records(dump["fsms"])
    chosen = [f for f in fsms if relevant(f)]
    roots = sorted({f["path"].split("/CoffeeAutomatic", 1)[0] + "/CoffeeAutomatic"
                    for f in fsms if "/CoffeeAutomatic/" in f["path"]})
    errors, machines, links = [], [], []
    if roots != list(EXPECTED_ROOTS): errors.append("CoffeeAutomatic root set changed: " + repr(roots))
    seen_ids = Counter(f["netId"] for f in fsms if "/CoffeeAutomatic/" in f["path"])
    for root in roots:
        roles = {}
        own = [f for f in fsms if f["path"].startswith(root + "/")]
        if len(own) != len(ROLE_SCHEMA): errors.append(root + ": expected exactly four FSM records")
        for role, (suffix, name, entry, states, expected_edges, variables) in ROLE_SCHEMA.items():
            records = [f for f in own if f["path"] == root + "/" + suffix and f["fsmName"] == name]
            roles[role] = [summarize(f, entry) for f in records]
            if len(records) != 1:
                errors.append(root + ": " + role + " expected one record, got " + str(len(records)))
            for f, summary in zip(records, roles[role]):
                where = f["path"] + "::" + name
                if type(f["netId"]) is not int or not 0 < f["netId"] <= 0xffffffff or seen_ids[f["netId"]] != 1:
                    errors.append(where + ": invalid/ambiguous dump FSM identity")
                if summary["states"] != sorted(states): errors.append(where + ": state schema drift")
                if summary["edges"] != sorted(list(t) for t in expected_edges): errors.append(where + ": transition schema drift")
                if summary["variables"] != variables: errors.append(where + ": variable-name/type schema drift")
        buy = roles["purchase"][0] if roles["purchase"] else None
        cup = roles["cup"][0] if roles["cup"] else None
        cup_states = set(cup["states"]) if cup else set()
        cup_name = cup["path"].rsplit("/", 1)[-1] if cup else ""
        consumables = catalog.get("consumables", {})
        pickables = catalog.get("pickables", {})
        candidates = [{"catalog_index": i, "template": rule.get("template"), "rule": rule}
                      for i, rule in enumerate(catalog.get("buys", []))
                      if any(purchase_path_matches(rule, f) for f in own if f["fsmName"] == "Buy")]
        save_tokens = sorted({token for f in own for token in
                              [s["name"] for s in f["states"]] + f.get("events", [])
                              if "save" in token.lower() or "load" in token.lower()})
        machines.append({"root": root, "roles": roles, "generic_buy_candidates": candidates,
                         "generic_inference_guard_candidate": "Purchase" if buy and "Purchase" in buy["states"] and "Wait button" in buy["states"] else None,
                         "generic_route_limit": "ShopBuyInference and source registration predict a candidate, not successful native execution; dedicated binding must avoid double registration",
                         "generic_item_boundary": {
                             "pickable_name_candidate": bool(cup_name and any(n in cup_name for n in pickables.get("nameSuffixes", []))
                                                             and not any(n in cup_name for n in pickables.get("excludeNameContains", []))),
                             "matches_generic_drink_check": consumables.get("drinkCheckState") in cup_states,
                             "generic_destroy_state_candidates": sorted(cup_states.intersection(consumables.get("destroyStates", []))),
                             "vendor_contents_or_effects_proved": False,
                             "limit": "Active-body scanning may sync pose; Check drink 2 is not Check drink. No amount/effect/retirement authority follows from pickability."},
                         "price_and_payment": {"semantics": "UNKNOWN", "local_price_name_present": bool(buy and "Price" in buy["variables"].get("FloatVariables", [])),
                                               "reason": "No action fields/global variable values; no local Price does not establish zero/free or shared-wallet debit"},
                         "identity": {"household_save_tag_present": bool(cup and "UniqueTagCoffee" in cup["variables"].get("StringVariables", [])),
                                      "factory_or_reuse": "UNKNOWN", "dump_ids_are": "historical FSM path IDs, not a proven dynamic cup identity contract"},
                         "native_persistence": {"semantics": "UNKNOWN", "save_named_states_or_events": save_tokens,
                                                "reason": "Missing action fields, initialization, external object/ES2 writers and global transitions; absence is not no-save proof"}})
        for from_role, field, target_role in (("purchase", "Pan", "pour"), ("acquire", "action target not dumped", "cup"), ("cup", "TargetPan", "pour")):
            links.append({"root": root, "from_role": from_role, "reference_field": field,
                          "candidate_target_role": target_role, "resolved": False,
                          "basis": "co-located named roles only; action object references required"})
    related = [summarize(f) for f in chosen if "/CoffeeAutomatic/" not in f["path"]]
    effects = [{"path": f["path"], "fsmName": f["fsmName"], "state": start, "event": event,
                "target": end, "emitter_resolved": False}
               for f in chosen for start, event, end in edges(f) if event.startswith("DRINKCOFFEE")]
    missing = [
        ("action_fields", "Ordered enabled native actions + serialized parameters in all eight machine FSMs, especially Purchase/GetACup State 1/2 and cup Data/Pour/Play anim/State 1"),
        ("event_routes", "Global transitions and actual cross-FSM SendEvent targets: PanTarget POUR has no local state edge; native start states not present"),
        ("payment_price", "Purchase payment/check/global binding, amount and failure ordering; machine Buy exposes Notification/Pan but no local Price; price may be constant/global or no charge"),
        ("cup_lifecycle", "GetACup activate/clone/reparent behavior, CupPivot/reference identity, refill capacity/rate and empty/full/consume/destruction or return writers"),
        ("factory_relation", "Resolve Spawner/CreateItems::Coffee Prefab/New and root Coffee::Use IDs; do not alias this factory to fixed machine cups by name"),
        ("actor_effect", "Cup Play anim SendEvent emitter and target/actions: DRINKCOFFEE vs DRINKCOFFEEPAPER vs HOME effects and amount/timing are not identified"),
        ("save_boundary", "Initialize/reset and ES2/global/external save writers for machine availability, contents, cup pose/lifetime and factory products; no persistence or no-save claim is justified"),
        ("runtime_authority", "Both-player accepted intent, once-only wallet/cup change, replica/personal-effect suppression, fresh rejoin and invalid/duplicate cases require future authorized native evidence"),
    ]
    return {"schema_version": 1, "evidence_level": "historical-static-dump-and-current-source",
            "dump_meta": dump.get("meta", {}), "build_label": "23268598 (filename, not independently verified installed build)",
            "status": "MISSING_EVIDENCE", "production_ready": False,
            "topology_checks_passed": not errors, "schema_errors": sorted(errors),
            "machines": machines, "related_records": related, "candidate_object_links": links,
            "player_effect_candidates": effects,
            "coffee_rigidbodies": sorted_records([b for b in dump.get("rigidbodies", []) if "coffee" in b["path"].lower()]),
            "current_coffee_adapter": {"scope": "household_only", "cup": catalog.get("coffee", {}).get("cup"),
                                       "descriptor": catalog.get("coffee"),
                                       "wire_scope": "CoffeeAction 1..4; CoffeeState kinds 0 pot/1 household cup/2 grounds; no vendor semantics"},
            "missing_evidence": [{"id": key, "required": text} for key, text in missing],
            "evidence_claims": {"native_gameplay_proved": False, "guest_capability_proved": False,
                                "ordinary_input_proved": False, "steam_two_pc_proved": False,
                                "H10_complete": False, "V11_promoted": False},
            "next_contract": "docs/H10-VENDOR-COFFEE-AUDIT.md; audit missing action/reference/save evidence before implementing one INSPECTION machine journey"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", required=True, type=Path)
    parser.add_argument("--name", required=True, help="New immutable output leaf beneath the assigned RUN")
    args = parser.parse_args()
    output = None
    started = utc()
    invocation = {"argv": [sys.executable, *sys.argv], "cwd": str(Path.cwd()), "started_utc": started,
                  "role": "static-reader", "assigned_run": str(args.run), "python": sys.version}
    try:
        run = validate_run(args.run)
        output = create_output(run, args.name)
        inputs, excerpts, loaded = {}, {}, {}
        for relative, ranges in INPUT_RANGES.items():
            path = source_file(SOURCE, relative)
            data = path.read_bytes()
            inputs[relative] = {"absolute_path": str(path), "bytes": len(data), "sha256": digest_bytes(data)}
            if relative.startswith("catalog/"): loaded[relative] = json.loads(data)
            if ranges:
                lines = data.decode("utf-8").splitlines()
                excerpts[relative] = [f"{i}|{lines[i-1]}" for low, high in ranges for i in range(low, min(high, len(lines)) + 1)]
        invocation["contract_sha256"] = digest_bytes((run / "contract.json").read_bytes())
        save(output / "inputs-before.json", inputs)
        save(output / "source-excerpts.json", excerpts)
        dump, catalog = loaded["catalog/dump-23268598.json"], loaded["catalog/sync-catalog.json"]
        raw = {"meta": dump.get("meta"), "fsms": sorted_records([f for f in dump["fsms"] if relevant(f)]),
               "rigidbodies": sorted_records([b for b in dump.get("rigidbodies", []) if "coffee" in b["path"].lower()])}
        save(output / "raw-coffee-records.json", raw)
        report = analyze(dump, catalog)
        save(output / "report.json", report)
        after = {relative: {"absolute_path": value["absolute_path"], "bytes": source_file(SOURCE, relative).stat().st_size,
                            "sha256": digest_bytes(source_file(SOURCE, relative).read_bytes())}
                 for relative, value in inputs.items()}
        save(output / "inputs-after.json", after)
        unchanged = inputs == after
        code = 2 if unchanged else 1
        invocation.update({"finished_utc": utc(), "exit_code": code, "inputs_unchanged": unchanged,
                           "status": report["status"], "topology_checks_passed": report["topology_checks_passed"],
                           "report_sha256": digest_bytes((output / "report.json").read_bytes()),
                           "cleanup": {"subprocesses_started": 0, "game_processes_started": 0,
                                       "temporary_files_created": 0, "rig_touched": False,
                                       "protected_files_read": False, "builds_or_deployments": 0}})
        print("MISSING_EVIDENCE: machine_count=" + str(len(report["machines"])) +
              " topology_checks_passed=" + str(report["topology_checks_passed"]) + " production_ready=False")
        for gap in report["missing_evidence"]: print(gap["id"] + ": " + gap["required"])
        for error in report["schema_errors"]: print("SCHEMA_ERROR: " + error)
        print("Report: " + str(output / "report.json"))
    except (OSError, ValueError, KeyError, TypeError) as error:
        code = 1
        invocation.update({"finished_utc": utc(), "exit_code": code, "error": str(error)})
        print("AUDIT_ERROR: " + str(error), file=sys.stderr)
    if output is not None: save(output / "receipt.json", invocation)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
