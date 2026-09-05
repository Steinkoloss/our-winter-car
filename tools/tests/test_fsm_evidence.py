import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from extract_fsm_assets import decode_actions, decode_parameter
from check_fsm_bindings import bindings_in, load_dump, load_globals


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


if __name__ == "__main__":
    unittest.main()
