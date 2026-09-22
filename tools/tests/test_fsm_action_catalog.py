import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from fsm_action_catalog import action_inventory, load_catalog, query


def fixture():
    return {"meta": {"schemaVersion": 4}, "fsms": [{"path": "Hand", "fsmName": "PickUp",
        "states": [{"name": "Sauna dipper", "transitions": [],
            "actionTypes": ["ActivateGameObject", None, "SendEvent"],
            "actionRecordsStatus": {"status": "ok", "reason": "observed-action-array"},
            "actionRecords": [
                {"index": 0, "type": "ActivateGameObject", "status": "ok", "fields": [
                    {"name": "target", "declaringType": "ActivateGameObject", "type": "GameObject",
                     "data": {"status": "ok", "kind": "scene-reference", "path": "Held", "pathCandidates": 1,
                              "resolution": "unique-observed-path", "componentType": "PlayMakerFSM", "componentIndex": 0}}]},
                {"index": 1, "type": None, "status": "unknown", "reason": "null-action-slot", "fields": []},
                {"index": 2, "type": "SendEvent", "status": "ok", "fields": [
                    {"name": "event", "declaringType": "SendEvent", "type": "FsmEvent",
                     "data": {"status": "unreadable", "reason": "reflection-or-reference-read-failed"}}]}]}]},
        {"path": "Held", "fsmName": "Use", "objectReference": {"componentType": "PlayMakerFSM", "componentIndex": 0}, "states": []}]}


class ActionCatalogTests(unittest.TestCase):
    def test_order_references_and_unknowns_are_losslessly_retained(self):
        data = fixture()
        before = copy.deepcopy(data)
        result = query(data, "Hand", "PickUp", ["Sauna dipper"])
        record = result["matches"][0]["states"][0]
        self.assertEqual(record["inventory"]["records"], data["fsms"][0]["states"][0]["actionRecords"])
        self.assertEqual([0, 1, 2], [r["index"] for r in record["inventory"]["records"]])
        self.assertEqual(["/fsms/1"], record["references"][0]["candidateFsms"])
        self.assertEqual("unique-observed-component", record["references"][0]["resolution"])
        self.assertTrue(record["unresolved"])
        self.assertEqual(before, data)
        self.assertEqual("PARTIAL_DISCOVERY_NOT_GAMEPLAY", result["evidence"])

    def test_old_catalogs_are_unknown_not_empty_action_proof(self):
        for schema in (None, 1, 2, 3):
            for state in ({"name": "old"}, {"name": "old", "actionTypes": ["A", "A"]}):
                result = action_inventory(state, schema)
                self.assertEqual("unknown", result["status"]["status"])
                self.assertEqual(len(state.get("actionTypes", [])), len(result["records"]))
                self.assertTrue(all(r["status"] == "unknown" for r in result["records"]))

    def test_malformed_records_cannot_fall_back_to_legacy(self):
        for fault in ("missing", "order", "type", "duplicate-field", "no-marker", "unknown-marker", "field-shape", "schema", "empty-types"):
            data = fixture()
            state = data["fsms"][0]["states"][0]
            if fault == "missing": del state["actionRecords"]
            if fault == "order": state["actionRecords"][1]["index"] = 0
            if fault == "type": state["actionRecords"][0]["type"] = "Other"
            if fault == "duplicate-field": state["actionRecords"][0]["fields"] *= 2
            if fault == "no-marker": del state["actionRecords"][0]["fields"][0]["data"]["status"]
            if fault == "unknown-marker": state["actionRecords"][1]["status"] = "success-ish"
            if fault == "field-shape": state["actionRecords"][0]["fields"] = {}
            if fault == "schema": data["meta"]["schemaVersion"] = 999
            if fault == "empty-types": state["actionTypes"] = []
            with self.subTest(fault=fault), self.assertRaises(ValueError):
                query(data, "Hand", "PickUp", ["Sauna dipper"])

    def test_duplicate_paths_remain_ambiguous_not_first_match_wins(self):
        data = fixture()
        data["fsms"].append(copy.deepcopy(data["fsms"][1]))
        data["fsms"].append(copy.deepcopy(data["fsms"][0]))
        result = query(data, "Hand", "PickUp", ["Sauna dipper"])
        self.assertEqual(2, len(result["matches"]))
        reference = result["matches"][0]["states"][0]["references"][0]
        self.assertEqual(["/fsms/1", "/fsms/2"], reference["candidateFsms"])
        self.assertEqual("ambiguous", reference["resolution"])

    def test_missing_states_and_references_are_explicit(self):
        data = fixture()
        data["fsms"].pop()
        result = query(data, "Hand", "PickUp", ["Check item", "Sauna dipper"])
        self.assertEqual(["Check item"], result["matches"][0]["missingStates"])
        self.assertEqual("unresolved", result["matches"][0]["states"][0]["references"][0]["resolution"])

    def test_json_duplicate_keys_and_nonfinite_are_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "bad.json"
            for text in ('{"fsms": [], "fsms": []}', '{"fsms": [], "bad": NaN}'):
                path.write_text(text)
                with self.assertRaises(ValueError): load_catalog(path)

    def test_cli_uses_fresh_output_and_never_overwrites_evidence(self):
        with tempfile.TemporaryDirectory() as td:
            source = Path(td) / "fixture.json"
            source.write_text(json.dumps(fixture()))
            out = Path(td) / "out"
            argv = [sys.executable, "-B", str(Path(__file__).resolve().parents[1] / "fsm_action_catalog.py"),
                    str(source), "--path", "Hand", "--fsm", "PickUp", "--state", "Sauna dipper", "--out", str(out)]
            first = subprocess.run(argv, capture_output=True)
            self.assertEqual(0, first.returncode, first.stderr)
            before = {p.name: p.read_bytes() for p in out.iterdir()}
            self.assertNotEqual(0, subprocess.run(argv, capture_output=True).returncode)
            self.assertEqual(before, {p.name: p.read_bytes() for p in out.iterdir()})
            receipt = json.loads((out / "receipt.json").read_text())
            self.assertEqual(0, receipt["exitCode"])
            self.assertIn("sha256", receipt["input"])


if __name__ == "__main__":
    unittest.main()
