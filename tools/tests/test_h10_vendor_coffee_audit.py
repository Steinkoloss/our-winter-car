"""Static catalog regressions, not native execution or vendor gameplay tests."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import h10_vendor_coffee_audit as audit

SOURCE = Path(__file__).resolve().parents[2]
INSPECTION = "INSPECTION/LOD/CoffeeAutomatic"
FACTORY = "JOBS/FACTORY/OpeningTimes/LOD1/Kitchen/CoffeeAutomatic"


class VendorCoffeeDiscoveryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dump = json.loads((SOURCE / "catalog/dump-23268598.json").read_text())
        cls.catalog = json.loads((SOURCE / "catalog/sync-catalog.json").read_text())

    def report(self, dump=None):
        return audit.analyze(self.dump if dump is None else dump, self.catalog)

    def test_real_dump_discovers_two_complete_topologies_but_fails_closed(self):
        result = self.report()
        self.assertEqual([INSPECTION, FACTORY], [m["root"] for m in result["machines"]])
        self.assertTrue(result["topology_checks_passed"])
        self.assertEqual("MISSING_EVIDENCE", result["status"])
        self.assertFalse(result["production_ready"])
        for machine in result["machines"]:
            self.assertEqual({"purchase", "acquire", "pour", "cup"}, set(machine["roles"]))
            self.assertTrue(all(len(records) == 1 for records in machine["roles"].values()))

    def test_only_explicit_edges_are_reachable_not_unresolved_pour_event(self):
        roles = self.report()["machines"][0]["roles"]
        buy = roles["purchase"][0]
        self.assertIn(["Wait button", "USE", "Purchase"], buy["edges"])
        self.assertIn("Purchase", buy["reachable_from_named_entry"])
        pour = roles["pour"][0]
        self.assertEqual(["OFF"], pour["reachable_from_named_entry"])
        self.assertIn("POUR", pour["events_without_state_edge"])
        self.assertFalse(pour["global_transitions_known"])
        cup = roles["cup"][0]
        self.assertIn("Play anim", cup["reachable_from_named_entry"])
        self.assertNotIn("Data", cup["reachable_from_named_entry"])
        self.assertEqual(["State 1"], cup["states_without_outgoing_edges"])

    def test_no_price_field_does_not_mean_free_and_no_save_state_is_not_no_save(self):
        for machine in self.report()["machines"]:
            self.assertEqual("UNKNOWN", machine["price_and_payment"]["semantics"])
            self.assertFalse(machine["price_and_payment"]["local_price_name_present"])
            self.assertEqual("UNKNOWN", machine["native_persistence"]["semantics"])
            self.assertEqual([], machine["native_persistence"]["save_named_states_or_events"])
            self.assertFalse(machine["identity"]["household_save_tag_present"])
            self.assertEqual("UNKNOWN", machine["identity"]["factory_or_reuse"])

    def test_same_named_factory_is_separate_and_id_names_are_not_values(self):
        related = self.report()["related_records"]
        factory = next(f for f in related if f["path"] == "Spawner/CreateItems")
        self.assertEqual("Coffee", factory["fsmName"])
        self.assertIn("Save", factory["states"])
        self.assertIn("SAVEGAME", factory["events_without_state_edge"])
        self.assertIn("SPAWNITEM", factory["events_without_state_edge"])
        self.assertIn("Prefab", factory["variables"]["GameObjectVariables"])
        self.assertTrue(all(not edge["resolved"] for edge in self.report()["candidate_object_links"]))
        self.assertIn("factory_relation", {g["id"] for g in self.report()["missing_evidence"]})

    def test_generic_buy_rule_is_reported_not_mistaken_for_vendor_coverage(self):
        result = self.report()
        for machine in result["machines"]:
            candidates = machine["generic_buy_candidates"]
            self.assertTrue(any(c["template"] == "shopBuy" for c in candidates))
            self.assertEqual("Purchase", machine["generic_inference_guard_candidate"])
        self.assertEqual("household_only", result["current_coffee_adapter"]["scope"])
        self.assertEqual("EQUIPMENTS/coffee cup(itemx)", result["current_coffee_adapter"]["cup"])
        self.assertFalse(result["evidence_claims"]["guest_capability_proved"])

    def test_rally_store_prefab_and_effect_variants_are_not_silently_excluded(self):
        result = self.report()
        paths = {f["path"] for f in result["related_records"]}
        self.assertIn("Coffee", paths)
        self.assertIn("CoffeePaperFly", paths)
        self.assertTrue(any(p.startswith("RACES/") for p in paths))
        self.assertTrue(any(p.startswith("PERAPORTTI/") for p in paths))
        effects = {e["event"] for e in result["player_effect_candidates"]}
        self.assertTrue({"DRINKCOFFEE", "DRINKCOFFEEPAPER", "DRINKCOFFEEHOME"} <= effects)
        self.assertTrue(all(e["emitter_resolved"] is False for e in result["player_effect_candidates"]))

    def test_generic_pickable_pose_is_not_vendor_drink_or_despawn_coverage(self):
        for machine in self.report()["machines"]:
            boundary = machine["generic_item_boundary"]
            self.assertTrue(boundary["pickable_name_candidate"])
            self.assertFalse(boundary["matches_generic_drink_check"])
            self.assertEqual([], boundary["generic_destroy_state_candidates"])
            self.assertFalse(boundary["vendor_contents_or_effects_proved"])

    def test_output_is_deterministic_under_dump_record_order(self):
        changed = dict(self.dump, fsms=list(reversed(self.dump["fsms"])),
                       rigidbodies=list(reversed(self.dump["rigidbodies"])))
        self.assertEqual(self.report(), self.report(changed))

    def test_missing_role_duplicate_identity_and_broken_edge_are_not_green(self):
        cup = next(f for f in self.dump["fsms"] if f["path"] == INSPECTION + "/Functions/CupPivot/coffee cup(itemx)")
        for fault in ("missing", "duplicate", "edge", "identity"):
            changed = dict(self.dump, fsms=[f for f in self.dump["fsms"] if f is not cup])
            if fault != "missing":
                mutated = copy.deepcopy(cup)
                if fault == "edge": mutated["states"][0]["transitions"][0]["to"] = "Missing state"
                if fault == "identity": mutated["netId"] = 0
                changed["fsms"].append(mutated)
                if fault == "duplicate": changed["fsms"].append(copy.deepcopy(cup))
            result = self.report(changed)
            self.assertFalse(result["topology_checks_passed"], fault)
            self.assertFalse(result["production_ready"], fault)
            self.assertTrue(result["schema_errors"], fault)

    def test_action_names_alone_cannot_supply_payment_or_persistence_proof(self):
        changed = dict(self.dump, fsms=[])
        for fsm in self.dump["fsms"]:
            if "CoffeeAutomatic" in fsm["path"]:
                fsm = copy.deepcopy(fsm)
                fsm["globalTransitions"] = []
                for state in fsm["states"]: state["actionTypes"] = ["FloatSubtract"]
            changed["fsms"].append(fsm)
        result = self.report(changed)
        self.assertEqual("MISSING_EVIDENCE", result["status"])
        self.assertTrue(all(m["price_and_payment"]["semantics"] == "UNKNOWN" for m in result["machines"]))
        self.assertFalse(result["production_ready"])


class VendorCoffeeOutputSafetyTests(unittest.TestCase):
    def test_declared_source_inputs_exist_without_redirection(self):
        for relative in audit.INPUT_RANGES:
            self.assertEqual(SOURCE / relative, audit.source_file(SOURCE, relative))

    def test_assigned_run_contract_and_immutable_new_output_are_required(self):
        with tempfile.TemporaryDirectory() as directory:
            rounds = Path(directory).resolve() / "rounds"
            run = rounds / "new-work"
            run.mkdir(parents=True)
            contract = run / "contract.json"
            contract.write_text(json.dumps({"ids": ["H10"], "work_type": "audit"}))
            self.assertEqual(run, audit.validate_run(run, rounds))
            output = audit.create_output(run, "fresh")
            sentinel = output / "evidence.json"
            sentinel.write_text("retain")
            with self.assertRaises(FileExistsError): audit.create_output(run, "fresh")
            self.assertEqual("retain", sentinel.read_text())
            for name in ("../old", "/tmp/output", "", ".", ".."):
                with self.assertRaises(ValueError): audit.create_output(run, name)
            contract.write_text(json.dumps({"ids": ["V11"], "work_type": "audit"}))
            with self.assertRaises(ValueError): audit.validate_run(run, rounds)

    def test_symlink_run_and_symlink_inputs_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            real = root / "real"
            real.mkdir()
            (real / "contract.json").write_text('{"ids":["H10"],"work_type":"audit"}')
            link = root / "link"
            link.symlink_to(real, target_is_directory=True)
            with self.assertRaises(ValueError): audit.validate_run(link, root)
            (root / "input.json").symlink_to(real / "contract.json")
            with self.assertRaises(ValueError): audit.source_file(root, "input.json")
            with self.assertRaises(ValueError): audit.source_file(root, "../elsewhere")


if __name__ == "__main__":
    unittest.main()
