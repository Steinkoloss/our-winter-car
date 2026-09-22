"""Synthetic schema fixtures only. No native values, game, saves or rig access."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import h10_vendor_coffee_serialized as reader

SOURCE = Path(__file__).resolve().parents[2]


def field(name="fixture", kind="Float", value=7.25):
    return {"field": name, "type": kind, "value": value, "useVariable": False,
            "useDefault": False, "binding": None}


def fixture():
    """Deliberately fictitious data; never copied into the production catalog."""
    objects, fsms = [], []
    for i, (role, (suffix, name, states)) in enumerate(reader.ROLES.items()):
        path = reader.ROOT + "/" + suffix
        objects.append({"fileId": "fixture.assets", "pathId": i + 1, "path": path,
                        "activeSelf": True, "parent": None,
                        "transform": {"space": "local", "position": [0, 1, 2],
                                      "rotation": [0, 0, 0, 1], "scale": [1, 1, 1]},
                        "colliders": [{"type": "BoxCollider", "enabled": True, "isTrigger": False,
                                       "parameters": [field("size", "Vector3", [1, 1, 1])]}],
                        "rigidbody": {"parameters": [field("mass")]}})
        fsms.append({"fileId": "fixture.assets", "pathId": i + 101, "path": path,
                     "fsmName": name, "enabled": True, "startState": states[0],
                     "globalTransitions": [{"event": "FIXTURE", "to": states[0]}],
                     "variables": [field("FixtureAmount")],
                     "states": [{"name": state, "transitions": [{"event": "FINISHED", "to": states[0]}],
                                 "actions": [{"type": "FixtureActionNotNative", "enabled": True,
                                              "parameters": [field()]}]} for state in states]})
    # A resolved cross-object reference, a separate effect receiver and external writer.
    ref = {"fileId": "fixture.assets", "pathId": 3, "path": objects[2]["path"], "fsmName": None}
    fsms[0]["states"][0]["actions"][0]["parameters"].append(field("Pan", "GameObject", ref))
    for i, path in enumerate(("FIXTURE_PLAYER/Drink", "FIXTURE_EXTERNAL/Save")):
        obj = copy.deepcopy(objects[0]); obj.update(path=path, pathId=10 + i); objects.append(obj)
        fsm = copy.deepcopy(fsms[0]); fsm.update(path=path, pathId=110 + i, fsmName="Fixture")
        fsms.append(fsm)
    def fref(index):
        f = fsms[index]
        return {k: f[k] for k in ("fileId", "pathId", "path", "fsmName")}
    # Direct edge to receiver; writer references machine. No unrelated FSM is allowed.
    fsms[3]["states"][6]["actions"][0]["parameters"].extend([
        field("receiver", "Fsm", fref(4)), field("event", "Event", "FIXTURE")])
    fsms[5]["states"][0]["actions"][0]["parameters"].append(field("written", "Fsm", fref(0)))
    scans = [{"purpose": purpose, "sourceIds": ["fixture-source"],
              "scope": [fref(i) for i in range(len(fsms))],
              "complete": True, "writers": [fref(5)], "absence": False,
              "method": "synthetic fixture full reference index; NOT native evidence"}
             for purpose in ("persistence", "initialization")]
    doc = {"schemaVersion": 1, "kind": "owc-inspection-coffee-serialized",
           "provenance": {"buildId": "fixture-build", "sourceId": "fixture-source",
                          "sourceSha256": "computed below from synthetic asset manifest",
                          "assets": [{"fileId": "fixture.assets", "sha256": hashlib.sha256(b"synthetic-source-not-game").hexdigest(),
                                      "bytes": len(b"synthetic-source-not-game")}],
                          "extractorId": "unittest-fixture", "extractorVersion": "1",
                          "evidenceClass": "synthetic-fixture"},
           "root": reader.ROOT, "objects": objects, "fsms": fsms, "components": [],
           "globals": [field("FixtureGlobal")], "scans": scans, "foundation": {}}
    doc["provenance"]["sourceSha256"] = hashlib.sha256(reader.canonical(doc["provenance"]["assets"])).hexdigest()
    pointers = {"action": "/fsms/0/states/0/actions/0", "parameter": "/fsms/0/states/0/actions/0/parameters/0",
                "reference": "/fsms/0/states/0/actions/0/parameters/1",
                "transition": "/fsms/0/globalTransitions/0", "fsm": "/fsms/4",
                "transform": "/objects/0/transform", "collider": "/objects/0/colliders/0",
                "rigidbody": "/objects/0/rigidbody", "scan": "/scans/0"}
    for section, topics in reader.TOPICS.items():
        doc["foundation"][section] = {}
        for topic, kinds in topics.items():
            pointer = pointers[kinds[0]]
            if section == "initialization" and kinds[0] == "scan": pointer = "/scans/1"
            doc["foundation"][section][topic] = {"origin": "serialized", "pointers": [pointer]}
    purchase = "/fsms/0/states/2/actions/0"
    fsms[0]["states"][2]["actions"][0]["parameters"].append(field("WalletTarget", "GameObject", ref))
    doc["foundation"]["price"]["payment"]["pointers"] = [purchase]
    doc["foundation"]["price"]["amount"]["pointers"] = [purchase + "/parameters/0"]
    doc["foundation"]["price"]["wallet"]["pointers"] = [purchase + "/parameters/1"]
    writer = "/fsms/5/states/0/actions/0"
    fsms[5]["states"][0]["actions"][0]["parameters"].append(field("SaveKey", "String", "fixture-save-key-not-native"))
    for topic in ("writers", "keys", "savedFields", "resetFields"):
        pointer = writer if topic == "writers" else writer + ("/parameters/3" if topic == "keys" else "/parameters/0")
        doc["foundation"]["persistence"][topic]["pointers"] = [pointer]
    for topic in ("emitter", "animation", "beforeEffectSeam"):
        doc["foundation"]["playerEffect"][topic]["pointers"] = ["/fsms/3/states/6/actions/0"]
    doc["foundation"]["playerEffect"]["assignments"]["pointers"] = ["/fsms/4/states/0/actions/0"]
    return doc


class SerializedTests(unittest.TestCase):
    def result(self, doc=None, **pins):
        raw = json.dumps(fixture() if doc is None else doc, allow_nan=False).encode()
        return reader.validate_bytes(raw, expected_sha256=pins.get("sha", hashlib.sha256(raw).hexdigest()),
                                     build_id=pins.get("build", "fixture-build"),
                                     source_id=pins.get("source", "fixture-source"),
                                     source_sha256=pins.get("source_sha", fixture()["provenance"]["sourceSha256"]))

    def reject(self, doc, text):
        result = self.result(doc)
        self.assertEqual("REJECTED", result["status"])
        self.assertFalse(result["production_ready"])
        self.assertIn(text, " ".join(result["errors"]))

    def test_complete_fixture_retains_all_fields_and_is_not_native_evidence(self):
        doc = fixture(); result = self.result(doc)
        self.assertEqual("ACCEPTED_FIELDS", result["status"])
        self.assertEqual(list(reader.TOPICS), list(result["fields"]))
        self.assertEqual(doc, result["extraction"])
        self.assertEqual("synthetic-fixture", result["evidence_level"])
        self.assertFalse(result["production_ready"])
        self.assertTrue(all(v == "NOT_TESTED" for v in result["gameplay_claims"].values()))

    def test_every_missing_foundation_and_topic_fails_closed(self):
        for section, topics in reader.TOPICS.items():
            doc = fixture(); del doc["foundation"][section]
            self.reject(doc, section)
            for topic in topics:
                doc = fixture(); del doc["foundation"][section][topic]
                self.reject(doc, topic)

    def test_guessed_price_persistence_lifetime_cannot_be_literal_claims(self):
        for section in ("price", "persistence", "cupLifecycle"):
            doc = fixture(); topic = next(iter(doc["foundation"][section]))
            doc["foundation"][section][topic] = {"origin": "guessed", "value": 0}
            self.reject(doc, section)
        doc = fixture(); doc["foundation"]["price"]["amount"]["pointers"] = ["/provenance/buildId"]
        self.reject(doc, "pointer")

    def test_build_source_and_exact_hash_must_match_external_pins(self):
        for pins in ({"build": "different"}, {"source": "different"},
                     {"sha": "0" * 64}, {"source_sha": "0" * 64}):
            result = self.result(**pins)
            self.assertEqual("REJECTED", result["status"], pins)

    def test_duplicate_json_keys_nonfinite_trailing_data_depth_and_size(self):
        for raw in (b'{"x":1,"x":2}', b'{"x":NaN}', b'{"x":1e999}', b'{} {}',
                    b'[' * 80 + b'0' + b']' * 80, b' ' * (reader.MAX_BYTES + 1)):
            result = reader.validate_bytes(raw, expected_sha256=hashlib.sha256(raw).hexdigest(),
                                           build_id="x", source_id="x", source_sha256="0" * 64)
            self.assertEqual("REJECTED", result["status"])

    def test_missing_enabled_fields_defaults_and_unsupported_types(self):
        for key in ("enabled", "parameters"):
            doc = fixture(); del doc["fsms"][0]["states"][0]["actions"][0][key]
            self.reject(doc, key)
        for key in ("useVariable", "useDefault", "value", "binding"):
            doc = fixture(); del doc["fsms"][0]["states"][0]["actions"][0]["parameters"][0][key]
            self.reject(doc, key)
        doc = fixture(); doc["fsms"][0]["variables"][0]["type"] = "Unsupported"
        self.reject(doc, "type")
        doc = fixture(); doc["fsms"][0]["variables"][0]["value"] = True
        self.reject(doc, "value")

    def test_ambiguous_ids_paths_fields_states_and_roles_are_rejected(self):
        for kind in ("object", "fsm", "field", "state", "role", "scope"):
            doc = fixture()
            if kind == "object": doc["objects"].append(copy.deepcopy(doc["objects"][0]))
            if kind == "fsm": doc["fsms"][1]["pathId"] = doc["fsms"][0]["pathId"]
            if kind == "field": doc["fsms"][0]["variables"].append(field("FixtureAmount"))
            if kind == "state": doc["fsms"][0]["states"].append(copy.deepcopy(doc["fsms"][0]["states"][0]))
            if kind == "role": doc["fsms"][0]["fsmName"] = "Other"
            if kind == "scope": doc["root"] = "JOBS/FACTORY/OpeningTimes/LOD1/Kitchen/CoffeeAutomatic"
            self.assertEqual("REJECTED", self.result(doc)["status"], kind)

    def test_unresolved_reference_wrong_path_and_external_unrelated_fsm(self):
        for key, value in (("pathId", 999), ("path", "Wrong"), ("fileId", "../escape"), ("fsmName", "Wrong")):
            doc = fixture(); doc["fsms"][0]["states"][0]["actions"][0]["parameters"][1]["value"][key] = value
            self.reject(doc, "reference")
        doc = fixture(); doc["fsms"][3]["states"][6]["actions"][0]["parameters"].pop(1)
        self.reject(doc, "unrelated")

    def test_variable_bindings_require_resolved_type_and_explicit_flags(self):
        doc = fixture(); p = doc["fsms"][0]["states"][0]["actions"][0]["parameters"][0]
        p.update(useVariable=True, binding={"scope": "local", "name": "FixtureAmount"})
        self.assertEqual("ACCEPTED_FIELDS", self.result(doc)["status"])
        p["binding"]["name"] = "Absent"; self.reject(doc, "binding")
        p["binding"]["name"] = "FixtureAmount"; p["useDefault"] = True
        self.reject(doc, "flags")

    def test_transition_start_and_global_routes_are_not_optional(self):
        for fault in ("start", "global", "target", "ambiguous"):
            doc = fixture(); fsm = doc["fsms"][0]
            if fault == "start": fsm["startState"] = "Missing"
            if fault == "global": del fsm["globalTransitions"]
            if fault == "target": fsm["globalTransitions"][0]["to"] = "Missing"
            if fault == "ambiguous": fsm["globalTransitions"].append(copy.deepcopy(fsm["globalTransitions"][0]))
            self.assertEqual("REJECTED", self.result(doc)["status"], fault)

    def test_geometry_and_external_writer_coverage_required(self):
        doc = fixture(); doc["objects"][0]["transform"]["position"] = [0, 1]
        self.reject(doc, "position")
        for key, value in (("complete", False), ("writers", []), ("scope", []), ("sourceIds", ["wrong"])):
            doc = fixture(); doc["scans"][0][key] = value
            self.reject(doc, "scan")
        doc = fixture(); doc["scans"][0].update(writers=[], absence=True)
        for topic in ("writers", "keys", "savedFields", "resetFields"):
            doc["foundation"]["persistence"][topic]["pointers"] = ["/scans/0"]
        self.assertEqual("ACCEPTED_FIELDS", self.result(doc)["status"])
        doc["foundation"]["persistence"]["externalCoverage"]["pointers"] = ["/scans/1"]
        self.reject(doc, "persistence")

    def test_action_order_disabled_actions_and_default_flags_preserved(self):
        doc = fixture(); actions = doc["fsms"][0]["states"][0]["actions"]
        other = copy.deepcopy(actions[0]); other.update(type="SecondFixtureAction", enabled=False)
        other["parameters"][0]["useDefault"] = True; actions.append(other)
        result = self.result(doc)
        self.assertEqual(actions, result["extraction"]["fsms"][0]["states"][0]["actions"])
        self.assertEqual(reader.canonical(result), reader.canonical(self.result(doc)))
        actions.reverse(); self.assertNotEqual(reader.canonical(result), reader.canonical(self.result(doc)))

    def test_unrelated_parameter_cannot_stand_in_for_price_or_receiver(self):
        doc = fixture()
        doc["foundation"]["price"]["amount"]["pointers"] = ["/objects/0/rigidbody/parameters/0"]
        self.reject(doc, "price")
        doc = fixture()
        doc["foundation"]["playerEffect"]["receiver"]["pointers"] = ["/fsms/0"]
        self.reject(doc, "receiver")

    def test_empty_physics_and_undeclared_source_asset_are_rejected(self):
        doc = fixture(); doc["objects"][0]["rigidbody"]["parameters"] = []
        self.reject(doc, "rigidbody")
        doc = fixture(); doc["objects"][0]["fileId"] = "unidentified.assets"
        self.reject(doc, "source")

    def test_historical_dump_build_mismatch_is_rejected_not_relabelled(self):
        raw = (SOURCE / "catalog/dump-23268598.json").read_bytes()
        result = reader.validate_bytes(raw, expected_sha256=hashlib.sha256(raw).hexdigest(),
                                       build_id="wrong-build", source_id="historical-dump-23268598",
                                       source_sha256=hashlib.sha256(raw).hexdigest())
        self.assertEqual("REJECTED", result["status"])

    def test_price_cannot_cite_hover_action_and_save_keys_need_writer_fields(self):
        doc = fixture()
        doc["foundation"]["price"]["payment"]["pointers"] = ["/fsms/0/states/0/actions/0"]
        self.reject(doc, "Purchase")
        doc = fixture()
        doc["foundation"]["persistence"]["keys"]["pointers"] = ["/fsms/0/states/0/actions/0/parameters/0"]
        self.reject(doc, "writer")

    def test_object_parent_cycle_and_fsm_as_transform_parent_are_rejected(self):
        doc = fixture(); obj = doc["objects"][0]
        obj["parent"] = {"fileId": obj["fileId"], "pathId": obj["pathId"], "path": obj["path"], "fsmName": None}
        self.reject(doc, "parent")
        doc = fixture(); fsm = doc["fsms"][0]
        doc["objects"][0]["parent"] = {k: fsm[k] for k in ("fileId", "pathId", "path", "fsmName")}
        self.reject(doc, "parent")

    def test_external_non_fsm_writer_fields_are_preserved_and_resolved(self):
        doc = fixture()
        component = {"fileId": "fixture.assets", "pathId": 200, "path": "FIXTURE_EXTERNAL/Save",
                     "componentType": "FixtureSaveComponent", "enabled": True,
                     "parameters": [field("Key", "String", "fixture-only"), field("SavedValue"),
                                    field("target", "Fsm", {k: doc["fsms"][0][k] for k in ("fileId", "pathId", "path", "fsmName")})]}
        doc["components"].append(component)
        ref = {k: component[k] for k in ("fileId", "pathId", "path")}; ref["fsmName"] = None
        for scan in doc["scans"]: scan["scope"].append(ref)
        doc["scans"][0]["writers"] = [ref]
        for topic, pointer in (("writers", "/components/0"), ("keys", "/components/0/parameters/0"),
                               ("savedFields", "/components/0/parameters/1"), ("resetFields", "/components/0/parameters/1")):
            doc["foundation"]["persistence"][topic]["pointers"] = [pointer]
        result = self.result(doc)
        self.assertEqual("ACCEPTED_FIELDS", result["status"], result["errors"])
        self.assertEqual(component, result["extraction"]["components"][0])
        component["parameters"][2]["value"]["pathId"] = 999
        self.reject(doc, "reference")

    def test_effect_requires_cup_emitter_and_resolved_receiver_event(self):
        doc = fixture(); doc["foundation"]["playerEffect"]["emitter"]["pointers"] = ["/fsms/0/states/0/actions/0"]
        self.reject(doc, "emitter")
        doc = fixture(); doc["fsms"][3]["states"][6]["actions"][0]["parameters"][2]["value"] = "UNROUTED"
        self.reject(doc, "event")

    def test_historical_dump_is_missing_all_eight_fields_not_production_ready(self):
        raw = (SOURCE / "catalog/dump-23268598.json").read_bytes()
        result = reader.validate_bytes(raw, expected_sha256=hashlib.sha256(raw).hexdigest(),
                                       build_id="23268598", source_id="historical-dump-23268598",
                                       source_sha256=hashlib.sha256(raw).hexdigest())
        self.assertEqual("MISSING_FIELDS", result["status"])
        self.assertEqual(set(reader.TOPICS), set(result["fields"]))
        self.assertTrue(all(v["status"] == "MISSING" for v in result["fields"].values()))
        self.assertFalse(result["production_ready"])


class FileSafetyTests(unittest.TestCase):
    def test_bounded_permitted_read_rejects_symlink_traversal_hardlink_and_outside(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); path = root / "input.json"; path.write_bytes(b'{}')
            self.assertEqual(b'{}', reader.read_permitted(str(path), [root]))
            link = root / "link.json"; link.symlink_to(path)
            folder = root / "folder"; folder.symlink_to(root, target_is_directory=True)
            hard = root / "hard.json"; hard.hardlink_to(path)
            for bad in (str(link), str(folder / "input.json"), str(root) + "/../input.json", str(hard), "/etc/passwd"):
                with self.assertRaises((ValueError, OSError), msg=bad): reader.read_permitted(bad, [root])
            hard.unlink()
            with self.assertRaises(ValueError): reader.read_permitted(str(path), [root], max_bytes=1)

    def test_new_run_receipt_exact_bytes_hashes_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as tmp:
            run = Path(tmp); raw = json.dumps(fixture(), indent=1).encode()
            result = reader.validate_bytes(raw, expected_sha256=hashlib.sha256(raw).hexdigest(),
                                           build_id="fixture-build", source_id="fixture-source",
                                           source_sha256=fixture()["provenance"]["sourceSha256"])
            out = reader.emit(run, "new", raw, result, {"role": "synthetic-test"})
            self.assertEqual(raw, (out / "input.json").read_bytes())
            receipt = json.loads((out / "receipt.json").read_text())
            self.assertEqual(hashlib.sha256(raw).hexdigest(), receipt["input_sha256"])
            self.assertEqual(hashlib.sha256((out / "report.json").read_bytes()).hexdigest(), receipt["output_sha256"])
            before = {p.name: p.read_bytes() for p in out.iterdir()}
            with self.assertRaises(FileExistsError): reader.emit(run, "new", b'bad', {}, {})
            self.assertEqual(before, {p.name: p.read_bytes() for p in out.iterdir()})
            for name in ("../escape", "/tmp/escape", ".", "..", ""):
                with self.assertRaises(ValueError): reader.emit(run, name, raw, result, {})

    def test_mismatched_raw_and_report_cannot_create_false_receipt(self):
        with tempfile.TemporaryDirectory() as tmp:
            run = Path(tmp)
            with self.assertRaises(ValueError): reader.emit(run, "bad", b'{}', SerializedTests().result(), {})
            self.assertFalse((run / "bad").exists())

    def test_run_contract_and_assignment_are_checked(self):
        with tempfile.TemporaryDirectory() as tmp:
            rounds = Path(tmp); run = rounds / "000036-work"; run.mkdir()
            (run / "contract.json").write_text(json.dumps({"ids": ["H10"], "work_type": "infrastructure"}))
            self.assertEqual(run, reader.validate_run(str(run), rounds))
            (run / "contract.json").write_text('{"ids":["V11"],"work_type":"infrastructure"}')
            with self.assertRaises(ValueError): reader.validate_run(str(run), rounds)
            with self.assertRaises(ValueError): reader.validate_run(str(run) + "/../000036-work", rounds)


if __name__ == "__main__":
    unittest.main()
