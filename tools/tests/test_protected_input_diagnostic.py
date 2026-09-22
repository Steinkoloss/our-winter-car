"""Portable fixtures only: no installed game, personal saves or native execution."""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import protected_input_diagnostic as diag


class ProtectedInputDiagnosticTests(unittest.TestCase):
    def fixture(self, root, content=b'abcdefghij'):
        path = root / 'input.log'
        path.write_bytes(content)
        return path

    def test_descriptor_bound_noatime_read_has_exact_chunk_ranges(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            before = path.stat()
            row = diag.hash_read(path, chunk_size=4)
            self.assertTrue(row['identity_stable'])
            self.assertEqual(hashlib.sha256(b'abcdefghij').hexdigest(), row['sha256'])
            self.assertEqual([(0, 4), (4, 8), (8, 10)],
                             [(r['start'], r['end_exclusive']) for r in row['chunks']])
            self.assertEqual(row['before']['stat'], row['descriptor_before'])
            self.assertEqual(row['after']['stat'], row['descriptor_after'])
            self.assertEqual(before.st_atime_ns, path.stat().st_atime_ns)
            self.assertEqual(os.O_RDONLY, row['open_flags'] & os.O_ACCMODE)
            self.assertTrue(row['open_flags'] & os.O_NOATIME)
            self.assertIn('mnt_id:', row['fdinfo_before'])

    def test_no_permission_fallback_that_could_update_protected_atime(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            with patch.object(diag.os, 'open', side_effect=PermissionError('portable denied')) as opened:
                row = diag.hash_read(path)
        self.assertFalse(row['identity_stable'])
        self.assertIn('error', row)
        self.assertEqual(1, opened.call_count)

    def test_missing_and_nonregular_inputs_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            for path in (Path(tmp) / 'absent', Path(tmp)):
                row = diag.hash_read(path)
                self.assertFalse(row['identity_stable'])
                self.assertIn('error', row)

    def test_replaced_path_cannot_impersonate_open_descriptor(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            new = Path(tmp) / 'new'
            new.write_bytes(b'abcdefghij')
            original = diag.os.open
            def replace_after_open(name, flags, *args, **kwargs):
                fd = original(name, flags, *args, **kwargs)
                if Path(name) == path:
                    new.replace(path)
                return fd
            with patch.object(diag.os, 'open', side_effect=replace_after_open):
                row = diag.hash_read(path)
            self.assertFalse(row['identity_stable'])
            self.assertNotEqual(row['descriptor_before']['inode'], row['after']['stat']['inode'])

    def test_symlink_retarget_is_instability_even_for_equal_bytes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.fixture(root)
            other = root / 'other'
            other.write_bytes(path.read_bytes())
            alias = root / 'alias'
            alias.symlink_to(path)
            original = diag.os.read
            retargeted = False
            def read_and_retarget(fd, count):
                nonlocal retargeted
                data = original(fd, count)
                if not retargeted:
                    alias.unlink()
                    alias.symlink_to(other)
                    retargeted = True
                return data
            with patch.object(diag.os, 'read', side_effect=read_and_retarget):
                row = diag.hash_read(alias)
            self.assertFalse(row['identity_stable'])
            self.assertNotEqual(row['before']['canonical_path'], row['after']['canonical_path'])

    def test_stable_different_is_not_original_equality(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            reads = [diag.hash_read(path, 4), diag.hash_read(path, 4)]
            result = diag.classify('0' * 64, reads, True, [], [])
            self.assertTrue(result['current_reads_stable'])
            self.assertFalse(result['original_digest_matches'])
            self.assertFalse(result['reconciled'])
            self.assertEqual(1, result['exit_code'])

    def test_historical_drift_is_not_erased_by_current_original_match(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            reads = [diag.hash_read(path), diag.hash_read(path)]
            result = diag.classify(reads[0]['sha256'], reads, True, ['0' * 64], [])
            self.assertTrue(result['original_digest_matches'])
            self.assertFalse(result['reconciled'])
            self.assertIn('historical', ' '.join(result['blockers']))
            self.assertFalse(result['native_launch_allowed'])

    def test_baseline_pin_and_command_failures_are_never_a_pass(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            reads = [diag.hash_read(path), diag.hash_read(path)]
            sha = reads[0]['sha256']
            for pin, errors in ((False, []), (True, ['independent command failed'])):
                result = diag.classify(sha, reads, pin, [], errors)
                self.assertEqual(1, result['exit_code'])
                self.assertFalse(result['reconciled'])

    def test_metadata_drift_is_failure_even_with_equal_digests(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            reads = [diag.hash_read(path), diag.hash_read(path)]
            reads[1]['before']['stat']['mtime_ns'] += 1
            result = diag.classify(reads[0]['sha256'], reads, True, [], [])
            self.assertFalse(result['current_reads_stable'])
            self.assertEqual(1, result['exit_code'])

    def test_digest_drift_with_unchanged_descriptor_metadata_is_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp))
            reads = [diag.hash_read(path), diag.hash_read(path)]
            # Portable receipt double for the observed live anomaly, not invented
            # game output: metadata alone must never authenticate changing bytes.
            reads[1]['sha256'] = '0' * 64
            self.assertEqual(reads[0]['descriptor_after'], reads[1]['descriptor_before'])
            result = diag.classify(reads[0]['sha256'], reads, True, [], [])
            self.assertFalse(result['current_reads_stable'])
            self.assertFalse(result['reconciled'])
            self.assertEqual(1, result['exit_code'])

    def test_added_and_removed_tail_ranges_are_not_hidden(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.fixture(Path(tmp), b'abcd')
            first = diag.hash_read(path, 4)
            path.write_bytes(b'abcde')
            second = diag.hash_read(path, 4)
            for a, b in ((first, second), (second, first)):
                diff = diag.chunk_changes(a, b)
                self.assertEqual([1], [r['index'] for r in diff])
                self.assertEqual((4, 5), (diff[0]['start'], diff[0]['end_exclusive']))

    def test_external_implementations_hash_descriptor_not_path_and_keep_raw_receipts(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.fixture(root)
            before = path.stat()
            for kind in ('sha256sum', 'openssl'):
                row = diag.external_hash(path, root, kind, kind)
                self.assertEqual(0, row['exit_code'])
                self.assertTrue(row['identity_stable'])
                self.assertEqual(hashlib.sha256(b'abcdefghij').hexdigest(), row['sha256'])
                self.assertNotIn(str(path), row['argv'])
                self.assertEqual(before.st_atime_ns, path.stat().st_atime_ns)
                receipt = json.loads((root / (kind + '.command.json')).read_text())
                self.assertEqual(row, receipt)
                self.assertEqual(row['output_sha256'], hashlib.sha256(Path(row['output']).read_bytes()).hexdigest())
            with self.assertRaises(FileExistsError):
                diag.external_hash(path, root, 'sha256sum', 'sha256sum')

    def test_command_failure_keeps_receipt_and_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.fixture(root)
            with patch.object(diag.subprocess, 'run', side_effect=FileNotFoundError('portable missing')):
                row = diag.external_hash(path, root, 'missing', 'sha256sum')
            self.assertNotEqual(0, row['exit_code'])
            self.assertFalse(row['identity_stable'])
            self.assertIn('error', json.loads((root / 'missing.command.json').read_text()))

    def test_timeout_is_nonzero_and_retains_partial_raw_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = self.fixture(root)
            failure = diag.subprocess.TimeoutExpired(['sha256sum', '-'], 1, output=b'partial fixture')
            with patch.object(diag.subprocess, 'run', side_effect=failure):
                row = diag.external_hash(path, root, 'timeout', 'sha256sum', timeout=1)
            self.assertNotEqual(0, row['exit_code'])
            self.assertFalse(row['identity_stable'])
            self.assertEqual(b'partial fixture', (root / 'timeout.log').read_bytes())

    def test_full_diagnostic_preserves_baseline_and_reports_mismatch_or_missing_target(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            rounds = root / 'rounds'
            run = rounds / 'assigned'
            run.mkdir(parents=True)
            (run / 'contract.json').write_text('{}')
            installed = root / 'installed'
            installed.mkdir()
            target = self.fixture(installed)
            baseline = rounds / 'original.json'
            baseline.write_text(json.dumps({str(installed): {target.name: '0' * 64}}))
            original_bytes = baseline.read_bytes()
            original_stat = baseline.stat()
            with patch.multiple(diag, ROUNDS=rounds, ORIGINAL=baseline, INSTALLED=installed,
                    LOG_NAME=target.name, HISTORY=(), ORIGINAL_SHA256=hashlib.sha256(original_bytes).hexdigest()), \
                    patch.dict(os.environ, {'RUN': str(run)}):
                first = diag.diagnose(run, 'mismatch', baseline)
                target.unlink()  # Deliberate portable fixture only, never a real protected file.
                missing = diag.diagnose(run, 'missing', baseline)
            self.assertTrue(first['baseline_pinned_unchanged'])
            self.assertTrue(first['current_reads_stable'])
            for row in (first, missing):
                self.assertEqual(1, row['exit_code'])
                self.assertFalse(row['native_launch_allowed'])
                self.assertFalse(row['reconciled'])
            self.assertEqual(original_bytes, baseline.read_bytes())
            self.assertEqual(original_stat.st_mtime_ns, baseline.stat().st_mtime_ns)
            self.assertEqual(original_stat.st_ctime_ns, baseline.stat().st_ctime_ns)
            self.assertEqual(missing, json.loads((run / 'missing/report.json').read_text()))
            self.assertFalse((root / 'test-rig').exists())

    def test_output_must_be_new_under_actual_assigned_run_without_symlinks(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            rounds = root / 'rounds'
            run = rounds / 'assigned'
            run.mkdir(parents=True)
            (run / 'contract.json').write_text('{}')
            with patch.object(diag, 'ROUNDS', rounds), patch.dict(os.environ, {'RUN': str(run)}):
                out = diag.create_output(run, 'fresh')
                self.assertEqual(run / 'fresh', out)
                for bad in ('.', '..', '../escape', '/absolute', 'fresh'):
                    with self.assertRaises((ValueError, FileExistsError)):
                        diag.create_output(run, bad)
                alias = rounds / 'alias'
                alias.symlink_to(run, target_is_directory=True)
                with self.assertRaises(ValueError):
                    diag.create_output(alias, 'new')
                with patch.dict(os.environ, {'RUN': str(rounds / 'someone-else')}):
                    with self.assertRaises(ValueError):
                        diag.create_output(run, 'wrong-assignment')


if __name__ == '__main__':
    unittest.main()
