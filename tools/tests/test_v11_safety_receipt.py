"""Portable receipt regressions; temporary files are NOT native evidence."""
import contextlib
import io
import json
from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import v11_pane_audit as audit
import v11_safety_receipt as safety


class SafetyReceiptTests(unittest.TestCase):
    def test_hash_is_bound_to_descriptor_and_symlink_target(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'real.log'
            target.write_bytes(b'portable content')
            alias = Path(tmp) / 'alias.log'
            alias.symlink_to(target)
            result = safety.hash_pass(alias, chunk_size=4)
            self.assertTrue(result['identity_stable'])
            self.assertEqual(str(target), result['resolved_path'])
            self.assertEqual(result['before'], result['descriptor_before'])
            self.assertEqual(result['after'], result['descriptor_after'])
            self.assertEqual(target.stat().st_size, result['bytes_read'])
            self.assertEqual(audit.digest(target), result['sha256'])

    def test_replaced_path_cannot_impersonate_the_open_descriptor(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'target.log'
            target.write_bytes(b'old content')
            replacement = Path(tmp) / 'replacement.log'
            replacement.write_bytes(b'new content')
            original_open = Path.open
            def open_then_replace(path, mode='r', *args, **kwargs):
                stream = original_open(path, mode, *args, **kwargs)
                if path == target and mode == 'rb':
                    replacement.replace(target)
                return stream
            with patch.object(Path, 'open', open_then_replace):
                result = safety.hash_pass(target)
            self.assertFalse(result['identity_stable'])
            self.assertNotEqual(result['descriptor_before']['inode'], result['after']['inode'])

    def test_missing_file_is_not_an_empty_file_digest(self):
        with tempfile.TemporaryDirectory() as tmp:
            result = safety.hash_pass(Path(tmp) / 'missing')
            self.assertIn('FileNotFoundError', result['error'])
            self.assertFalse(result['identity_stable'])
            self.assertNotIn('sha256', result)

    def receipt_for_change(self, change):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            run = root / 'rounds/portable'
            run.mkdir(parents=True)
            (run / 'contract.json').write_text('{}')
            rig = root / 'rig'
            (rig / 'game/WinterMP').mkdir(parents=True)
            target = root / 'protected.log'
            target.write_bytes(b'a' * (1024 * 1024))
            (run / 'protected-before.json').write_text(json.dumps({str(root): {target.name: 'baseline-double'}}))
            opened = 0
            original_open = Path.open

            def open_file(path, mode='r', *args, **kwargs):
                nonlocal opened
                if path == target and mode == 'rb':
                    opened += 1
                    if opened == 2:
                        change(target, original_open)
                return original_open(path, mode, *args, **kwargs)

            with patch.multiple(audit, AUTO=root, RIG=rig, GAME=rig / 'game', ROUND=None, MARKER=None), \
                    patch.object(audit, 'protected', return_value={str(root): {target.name: 'current-double'}}), \
                    patch.object(Path, 'open', open_file), \
                    patch.object(sys, 'argv', ['v11_safety_receipt.py', '--run', str(run), '--stability']), \
                    contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(SystemExit) as exit_result:
                    runpy.run_path(str(TOOLS / 'v11_safety_receipt.py'), run_name='__main__')
            self.assertEqual(1, exit_result.exception.code, 'A differing baseline must never be safety-green')
            receipt = json.loads((run / 'protected-hash-stability.json').read_text())[str(target)]
            return receipt

    def test_stability_reports_added_tail_chunk(self):
        def append(target, original_open):
            with original_open(target, 'ab') as stream:
                stream.write(b'b')
        receipt = self.receipt_for_change(append)
        self.assertEqual([1], receipt['changed_chunk_indices'])

    def test_stability_reports_removed_tail_chunk(self):
        def truncate(target, original_open):
            with original_open(target, 'wb'):
                pass
        receipt = self.receipt_for_change(truncate)
        self.assertEqual([0], receipt['changed_chunk_indices'])

    def test_stability_records_disappearance_instead_of_losing_receipt(self):
        receipt = self.receipt_for_change(lambda target, _: target.unlink())
        self.assertIn('error', receipt['passes'][1])
        self.assertFalse(receipt['stable'])

    def test_stable_reads_are_not_a_baseline_match(self):
        receipt = self.receipt_for_change(lambda *_: None)
        self.assertEqual([], receipt['changed_chunk_indices'])
        self.assertEqual(receipt['passes'][0]['sha256'], receipt['passes'][1]['sha256'])


if __name__ == '__main__':
    unittest.main()
