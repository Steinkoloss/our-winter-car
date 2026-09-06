import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from extract_fsm_assets import decode_actions, decode_parameter
from check_fsm_bindings import (audit_purchase_guards, bindings_in, check_purchase_catalog,
                                load_dump, load_globals, purchase_path_matches)


class FsmEvidenceTests(unittest.TestCase):
    def test_global_float_action_preserves_both_field_and_variable_name(self):
        raw = struct.pack("<fB", 2140.0, 1) + b"PlayerMoney"
        data = {"actionStartIndex": [0], "actionNames": ["FloatSubtract"],
                "actionEnabled": [1], "paramDataType": [15],
                "paramName": ["floatVariable"], "paramDataPos": [0],
                "paramByteDataSize": [len(raw)], "byteData": list(raw)}
        parameter = decode_actions(data)[0]["parameters"][0]
        self.assertEqual(parameter["field"], "floatVariable")
        self.assertEqual(parameter["name"], "PlayerMoney")
        self.assertEqual(parameter["value"], 2140.0)
        self.assertTrue(parameter["useVariable"])

    def test_truncated_scalar_is_not_silently_evidence_of_zero(self):
        with self.assertRaises(ValueError):
            decode_parameter({"paramDataType": [15], "paramDataPos": [0],
                              "paramByteDataSize": [2], "byteData": [0, 0]}, 0)

    def test_material_and_texture_resolve_the_shared_object_table(self):
        for kind in (32, 33):
            reference = {"useVariable": 0, "name": "", "value": {"m_FileID": 2, "m_PathID": 7364}}
            data = {"paramDataType": [kind], "paramDataPos": [1], "paramByteDataSize": [0],
                    "byteData": [], "fsmObjectParams": [{}, reference]}
            self.assertEqual(reference, decode_parameter(data, 0)["value"])

    def test_globals_cannot_be_satisfied_by_a_same_named_local_variable(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "Binding.cs"
            source.write_text('var money = FsmVariables.GlobalVariables.FindFsmFloat("Money");\n'
                              'var local = fsm.FsmVariables.FindFsmFloat("Money");\n')
            evidence = Path(directory) / "evidence.json"
            evidence.write_text(json.dumps({"fsms": [{"path": "Something", "fsmName": "Use",
                "variables": {"FloatVariables": ["Money"]}}], "globalVariables": {
                "FloatVariables": [{"name": "PlayerMoney", "value": "2140"}]}}))
            bindings, _ = bindings_in(directory)
            self.assertEqual(bindings[0][-1], "global literal")
            self.assertEqual(bindings[1][-1], "literal")
            self.assertIn("Money", load_dump(str(evidence)))
            self.assertNotIn("Money", load_globals(str(evidence)))
            self.assertIn("PlayerMoney", load_globals(str(evidence)))

    def test_old_dump_means_unknown_globals_not_an_empty_global_table(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = Path(directory) / "old.json"
            evidence.write_text('{"fsms": []}')
            self.assertIsNone(load_globals(str(evidence)))


class PurchaseGuardEvidenceTests(unittest.TestCase):
    @staticmethod
    def sample(action_type="GetMouseButtonDown", event="USE"):
        catalog = {"buys": [{"pathContains": "Lotto/BuyLotto", "fsmName": "Use",
            "requireStates": ["Wait button", "Open"],
            "entryGuards": [{"state": "Wait button", "event": "USE"}], "resultStates": ["Open"]}]}
        fsms = [{"path": "Shop/Lotto/BuyLotto", "fsmName": "Use", "states": [
            {"name": "Wait button", "transitions": [{"event": "USE", "to": "Open"}],
             "actions": [{"type": action_type, "enabled": True,
                          "parameters": [{"field": "sendEvent", "type": "FsmEvent", "value": event}]}]},
            {"name": "Open", "actions": [{"type": "ActivateGameObject", "enabled": True}]}]}]
        return catalog, fsms

    def test_input_waits_are_reported_before_any_payment_action(self):
        for name in ("GetMouseButtonDown", "HutongGames.PlayMaker.Actions.GetButtonDown", "GetKeyUp"):
            with self.subTest(action=name):
                catalog, fsms = self.sample(name)
                result = audit_purchase_guards(catalog, fsms)
                self.assertEqual(1, len(result["hazards"]))
                self.assertEqual("USE", result["hazards"][0][2])
                self.assertEqual(1, result["checked"])
                self.assertFalse(result["unverified"])

    def test_mouse_pick_checks_the_actual_click_field(self):
        catalog, fsms = self.sample("MousePickEvent")
        action = fsms[0]["states"][0]["actions"][0]
        action["parameters"] = [{"field": field, "value": "USE" if field == "mouseDown" else ""}
                                for field in ("mouseOver", "mouseDown", "mouseUp", "mouseOff")]
        self.assertEqual(["MousePickEvent.mouseDown"], audit_purchase_guards(catalog, fsms)["hazards"][0][3])

    def test_disabled_or_different_input_is_not_proof_of_an_early_intent(self):
        catalog, fsms = self.sample(event="CANCEL")
        self.assertFalse(audit_purchase_guards(catalog, fsms)["hazards"])
        for enabled in (False, 0):
            catalog, fsms = self.sample()
            fsms[0]["states"][0]["actions"][0]["enabled"] = enabled
            self.assertFalse(audit_purchase_guards(catalog, fsms)["hazards"])
        catalog, fsms = self.sample()
        catalog["buys"][0]["entryGuards"][0]["state"] = "Open"
        self.assertFalse(audit_purchase_guards(catalog, fsms)["hazards"])

    def test_missing_or_partial_actions_are_unverified_not_a_clean_guard(self):
        for fault in ("missing", "partial", "parameter", "template", "state"):
            catalog, fsms = self.sample()
            state = fsms[0]["states"][0]
            if fault == "missing": del state["actions"]
            if fault == "partial":
                state["actionTypes"] = ["GetMouseButtonDown"]
                state["actions"] = []
            if fault == "parameter": state["actions"][0]["parameters"] = []
            if fault == "template": catalog["buys"][0]["template"] = "shopBuy"
            if fault == "state": fsms[0]["states"] = []
            result = audit_purchase_guards(catalog, fsms)
            self.assertEqual(0, result["checked"], fault)
            self.assertTrue(result["unverified"], fault)
        catalog, _ = self.sample()
        result = audit_purchase_guards(catalog, [])
        self.assertEqual([0], result["unmatched"])
        self.assertEqual(0, result["checked"])

    def test_path_and_state_filters_do_not_audit_the_wrong_fsm(self):
        catalog, fsms = self.sample()
        rule, fsm = catalog["buys"][0], fsms[0]
        self.assertTrue(purchase_path_matches(rule, fsm))
        for key, value in (("pathPrefix", "Other/"), ("objectName", "Other"),
                           ("objectNameContains", "Other"), ("fsmName", "Other"),
                           ("excludePathPrefixes", ["Shop/"])):
            other = dict(rule, **{key: value})
            self.assertFalse(purchase_path_matches(other, fsm), key)
        self.assertTrue(purchase_path_matches(dict(rule, objectNameContains="buylotto"), fsm))
        self.assertFalse(purchase_path_matches(dict(rule, pathContains="lotto/buylotto"), fsm))
        self.assertTrue(purchase_path_matches(dict(rule, pathPrefix=None, pathContains=None,
                            objectName=None, objectNameContains=None, excludePathPrefixes=None), fsm))

    def test_cli_fails_for_confirmed_hazards_and_reads_both_dump_shapes(self):
        from contextlib import redirect_stdout
        import io
        catalog, fsms = self.sample()
        with tempfile.TemporaryDirectory() as directory:
            rules = Path(directory) / "catalog.json"
            dump = Path(directory) / "dump.json"
            rules.write_text(json.dumps(catalog))
            for data in (fsms, {"fsms": fsms}):
                dump.write_text(json.dumps(data))
                with redirect_stdout(io.StringIO()) as output:
                    self.assertEqual(1, check_purchase_catalog(str(rules), str(dump)))
                self.assertIn("EARLY INTENT:", output.getvalue())
            rules.write_text('{"buys": []}')
            with redirect_stdout(io.StringIO()):
                self.assertEqual(0, check_purchase_catalog(str(rules), str(dump)))


if __name__ == "__main__":
    unittest.main()
