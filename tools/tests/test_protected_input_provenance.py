"""Portable fixtures; no game paths or hardware health claims."""
import hashlib
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import protected_input_provenance as prov


class ProvenanceTests(unittest.TestCase):
    def test_output_requires_explicit_matching_run_and_never_overwrites(self):
        with tempfile.TemporaryDirectory() as tmp:
            rounds = Path(tmp)
            run = rounds / 'assigned'
            run.mkdir()
            (run / 'contract.json').write_text('{}')
            with patch.object(prov.diag, 'ROUNDS', rounds), patch.dict(os.environ, {'RUN': ''}):
                with self.assertRaises(ValueError):
                    prov.create_output(run, 'new')
                with patch.dict(os.environ, {'RUN': str(run)}):
                    output = prov.create_output(run, 'new')
                    (output / 'retained').write_bytes(b'old')
                    for name in ('new', '.', '..', '../escape', '/tmp/escape'):
                        with self.assertRaises((ValueError, FileExistsError)):
                            prov.create_output(run, name)
                    self.assertEqual(b'old', (output / 'retained').read_bytes())
                    alias = rounds / 'alias'
                    alias.symlink_to(run, target_is_directory=True)
                    with self.assertRaises(ValueError):
                        prov.create_output(alias, 'new')

    def test_metadata_observation_never_reads_target_bytes_and_closes_fd(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'target'
            target.write_bytes(b'not log text')
            before = target.stat()
            real_open = os.open
            opened = []
            def observe_open(path, flags, *args, **kwargs):
                fd = real_open(path, flags, *args, **kwargs)
                opened.append(fd)
                return fd
            with patch.object(prov.diag.os, 'read', side_effect=AssertionError('content read forbidden')), \
                    patch.object(prov.diag.os, 'open', side_effect=observe_open):
                row = prov.target_metadata(target)
            self.assertTrue(row['identity_stable'])
            self.assertEqual(0, row['bytes_read'])
            self.assertEqual(before.st_atime_ns, target.stat().st_atime_ns)
            self.assertIn('mountinfo', row['mount'])
            self.assertEqual(str(target), row['descriptor_target'])
            for fd in opened:
                with self.assertRaises(OSError):
                    os.fstat(fd)

    def test_missing_target_has_machine_readable_nonstable_receipt(self):
        with tempfile.TemporaryDirectory() as tmp:
            row = prov.target_metadata(Path(tmp) / 'absent')
        self.assertFalse(row['identity_stable'])
        self.assertIn('error', row)
        self.assertEqual(0, row['bytes_read'])

    def test_metadata_requires_unambiguous_descriptor_bound_mount_record(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'target'
            target.write_bytes(b'portable fixture only')
            mount = prov.diag.mount_observation(target)
            for mutation in ({'mountinfo': []}, {'mountinfo': mount['mountinfo'] * 2},
                             {'mount_id': mount['mount_id'] + 1}, {'fdinfo': ''},
                             {'fdinfo': mount['fdinfo'].replace('pos:\t0', 'pos:\t1')}):
                with self.subTest(mutation=mutation), \
                        patch.object(prov.diag, 'mount_observation', return_value=dict(mount, **mutation)), \
                        patch.object(prov.diag.os, 'read', side_effect=AssertionError('no target bytes')):
                    row = prov.target_metadata(target)
                    self.assertFalse(row['identity_stable'])
                    self.assertIn('error', row)

    def test_retains_exact_json_bytes_noatime_and_raw_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / 'old.json'
            raw = b'{ "historical_failure": true }\n'
            source.write_bytes(raw)
            before = source.stat()
            row, data = prov.retain_json(source, root, 'copy')
            self.assertEqual({'historical_failure': True}, data)
            self.assertEqual(raw, (root / 'copy.raw.json').read_bytes())
            self.assertEqual(hashlib.sha256(raw).hexdigest(), row['sha256'])
            self.assertEqual(before.st_atime_ns, source.stat().st_atime_ns)
            with self.assertRaises(FileExistsError):
                prov.retain_json(source, root, 'copy')

    def test_bad_json_and_missing_receipts_are_explicit_errors(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / 'bad.json'
            source.write_bytes(b'{invalid')
            nonobject = root / 'nonobject.json'
            nonobject.write_text('[1, 2]')
            for i, path in enumerate((source, root / 'absent', nonobject)):
                row, data = prov.retain_json(path, root, 'copy' + str(i))
                self.assertIsNone(data)
                self.assertIn('error', row)
                self.assertTrue((root / ('copy' + str(i) + '.read.json')).exists())

    def test_exact_command_raw_streams_utc_exit_and_wait(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            argv = [sys.executable, '--version']
            row = prov.observe_command(root, 'version', argv)
            self.assertEqual(argv, row['argv'])
            self.assertEqual(0, row['exit_code'])
            self.assertTrue(row['waited'])
            self.assertTrue(row['started_utc'].endswith('+00:00'))
            for stream in ('stdout', 'stderr'):
                self.assertEqual(row[stream + '_sha256'], hashlib.sha256(Path(row[stream]).read_bytes()).hexdigest())
            with self.assertRaises(FileExistsError):
                prov.observe_command(root, 'version', argv)

    def test_command_unavailable_and_timeout_keep_raw_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            missing = prov.observe_command(root, 'missing', ['/not/an/executable'])
            self.assertNotEqual(0, missing['exit_code'])
            self.assertIn('error', missing)
            timeout = subprocess.TimeoutExpired(['fixture'], 1, output=b'partial out', stderr=b'partial err')
            with patch.object(prov.subprocess, 'run', side_effect=timeout):
                row = prov.observe_command(root, 'timeout', ['fixture'], timeout=1)
            self.assertEqual(124, row['exit_code'])
            self.assertTrue(row['timed_out'])
            self.assertEqual(b'partial out', Path(row['stdout']).read_bytes())
            self.assertEqual(b'partial err', Path(row['stderr']).read_bytes())

    def test_command_names_cannot_escape_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            for name in ('../escape', '.', '/absolute'):
                with self.assertRaises(ValueError):
                    prov.observe_command(Path(tmp), name, ['unused'])

    def test_fixed_host_probes_cannot_launch_repair_reset_or_hash_target(self):
        probes = prov.host_commands(Path('/fixture/installed'), Path('/fixture'))
        self.assertEqual({'device-stats', 'subvolume-show', 'readonly-snapshots'}, set(probes))
        self.assertEqual(['btrfs', 'device', 'stats', '/fixture/installed'], probes['device-stats'])
        self.assertEqual(['btrfs', 'subvolume', 'show', '/fixture'], probes['subvolume-show'])
        self.assertEqual(['btrfs', 'subvolume', 'list', '-r', '-s', '-u', '/fixture'], probes['readonly-snapshots'])
        self.assertNotIn('-z', probes['device-stats'])
        self.assertTrue(all(argv[0] == 'btrfs' for argv in probes.values()))

    def test_subvolume_query_uses_descriptor_mount_root_not_arbitrary_subdirectory(self):
        row = {'mount_id': 42, 'mountinfo': [r'42 1 0:51 / /fixture\040root ro - btrfs /dev/example rw,subvolid=5']}
        self.assertEqual(Path('/fixture root'), prov.mount_root(row))
        for invalid in ({}, dict(row, mount_id=43), dict(row, mountinfo=row['mountinfo'] * 2),
                        dict(row, mountinfo=['42 1 0:51 / /fixture ro - tmpfs tmpfs rw'])):
            with self.assertRaises(ValueError):
                prov.mount_root(invalid)

    def test_real_owned_command_timeout_is_waited_and_records_only_its_termination(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            script = root / 'slow.py'
            script.write_text('import time\nprint("started", flush=True)\ntime.sleep(30)\n')
            row = prov.observe_command(root, 'slow', [sys.executable, str(script)], timeout=0.2)
            self.assertEqual(124, row['exit_code'])
            self.assertTrue(row['waited'])
            self.assertTrue(row['owned_child_killed_on_timeout'])
            self.assertEqual(b'started\n', Path(row['stdout']).read_bytes())

    def fixture(self, root):
        rounds = root / 'rounds'
        run = rounds / 'assigned'
        run.mkdir(parents=True)
        (run / 'contract.json').write_text('{}')
        installed = root / 'installed'
        installed.mkdir()
        target = installed / 'target'
        target.write_bytes(b'target bytes must never be read')
        original = rounds / 'original.json'
        original.write_text(json.dumps({str(installed): {'target': 'a' * 64}}))
        before = dict(chunks=[dict(index=0, start=0, end_exclusive=4, sha256='b' * 64)],
                      sha256='c' * 64, bytes_read=4, chunk_size=4)
        after = dict(before, sha256='d' * 64,
                     chunks=[dict(index=0, start=0, end_exclusive=4, sha256='e' * 64)])
        history = {}
        for name, data in (('report', {'reconciled': False, 'exit_code': 1}),
                           ('before', before), ('after', after)):
            path = rounds / (name + '.json')
            path.write_text(json.dumps(data))
            history[name] = path
        return run, installed, original, history

    def run_fixture(self, root, name='evidence', bad_pin=False, retained_only=False):
        run, installed, original, history = self.fixture(root)
        pin = '0' * 64 if bad_pin else hashlib.sha256(original.read_bytes()).hexdigest()
        with patch.multiple(prov.diag, ROUNDS=run.parent, INSTALLED=installed, LOG_NAME='target',
                            ORIGINAL=original, ORIGINAL_SHA256=pin), \
                patch.object(prov, 'HISTORY', history), patch.dict(os.environ, {'RUN': str(run)}), \
                patch.object(prov, 'observe_host', side_effect=AssertionError('no host probes') if retained_only else None,
                             return_value={'portable_fixture': True, 'commands': {}}), \
                patch.object(prov.diag, 'hash_read', wraps=prov.diag.hash_read) as reads:
            result = (prov.investigate(run, name, retained_only=True) if retained_only
                      else prov.investigate(run, name))
            self.assertTrue(all(Path(call.args[0]) != installed / 'target' for call in reads.call_args_list))
        return run, result

    def test_end_to_end_failure_is_not_erased_by_available_host_observations(self):
        with tempfile.TemporaryDirectory() as tmp:
            run, report = self.run_fixture(Path(tmp))
            self.assertEqual(report, json.loads((run / 'evidence/report.json').read_text()))
            self.assertEqual(1, report['exit_code'])
            self.assertFalse(report['reconciled'])
            self.assertFalse(report['native_launch_allowed'])
            self.assertTrue(report['baseline_pinned_unchanged'])
            self.assertTrue(report['retained_evidence_unchanged'])
            self.assertEqual(0, report['target_content_bytes_read'])
            self.assertEqual([0], [row['index'] for row in report['historical_chunk_changes']])
            self.assertEqual({'NOT_TESTED'}, set(report['dimensions'].values()))
            self.assertIn('same kernel', report['independence_limit'])
            self.assertFalse(report['cleanup']['processes_signaled'])
            self.assertFalse((run.parent.parent / 'test-rig').exists())

    def test_bad_baseline_pin_blocks_host_probe_and_target_open(self):
        with tempfile.TemporaryDirectory() as tmp, \
                patch.object(prov, 'target_metadata', side_effect=AssertionError('untrusted input')):
            _, report = self.run_fixture(Path(tmp), bad_pin=True)
            self.assertFalse(report['baseline_pinned_unchanged'])
            self.assertEqual(1, report['exit_code'])
            self.assertIn('baseline', ' '.join(report['blockers']))

    def test_report_preserves_interpreter_flags_and_bounds_source_read_timestamps(self):
        invocation = [sys.executable, '-B', str(Path(prov.__file__)), '--run', '/fixture',
                      '--name', 'evidence', '--retained-only']
        with tempfile.TemporaryDirectory() as tmp, patch.object(sys, 'orig_argv', invocation):
            run, report = self.run_fixture(Path(tmp), retained_only=True)
            self.assertEqual(invocation, report['argv'])
            for path in (run / 'evidence').glob('*.source-read.json'):
                row = json.loads(path.read_text())
                self.assertLessEqual(report['started_utc'], row['started_utc'])
                self.assertLessEqual(row['ended_utc'], report['ended_utc'])

    def test_history_chunk_holes_are_rejected_not_compared_as_valid(self):
        row = dict(bytes_read=8, chunk_size=4, chunks=[dict(index=0, start=4, end_exclusive=8, sha256='a' * 64)])
        self.assertFalse(prov.valid_chunks(row))
        row['chunks'] = [dict(index=i, start=i * 4, end_exclusive=(i + 1) * 4, sha256='a' * 64) for i in range(2)]
        self.assertTrue(prov.valid_chunks(row))
        row['chunks'][1]['sha256'] = 'not-a-digest'
        self.assertFalse(prov.valid_chunks(row))

    def test_cli_wrong_run_has_nonzero_exit_without_host_operations(self):
        with tempfile.TemporaryDirectory() as tmp:
            result = subprocess.run([sys.executable, str(Path(prov.__file__)), '--run', tmp, '--name', 'new'],
                                    capture_output=True, timeout=10)
            self.assertNotEqual(0, result.returncode)
            self.assertFalse((Path(tmp) / 'new').exists())

    def test_retained_only_does_not_query_host_and_emits_deterministic_blocked_assessment(self):
        with tempfile.TemporaryDirectory() as tmp, \
                patch.object(prov, 'observe_host', side_effect=AssertionError('no host probes')):
            run, report = self.run_fixture(Path(tmp), retained_only=True)
            assessment = json.loads((run / 'evidence/assessment.json').read_text())
            self.assertEqual('BLOCKED', report['gate_status'])
            self.assertEqual('BLOCKED', assessment['gate_status'])
            self.assertFalse(assessment['native_launch_allowed'])
            self.assertFalse(assessment['future_isolated_launch_eligible'])
            self.assertEqual('NOT_RUN: retained-only reconciliation', report['host_observation_status'])
            self.assertEqual([], report['cleanup']['processes_signaled'])
            self.assertEqual(0, report['target_content_bytes_read'])
            self.assertEqual({'NOT_TESTED'}, set(assessment['dimensions'].values()))
            self.assertEqual(prov.gate_assessment(report), prov.gate_assessment(copy.deepcopy(report)))
            self.assertNotIn('started_utc', assessment)
            self.assertIn('independent_original_provenance', assessment['conditions'])
            self.assertEqual('MISSING', assessment['conditions']['independent_original_provenance']['status'])

    def test_gate_cannot_be_cleared_by_agreement_metadata_or_forged_prior_verdict(self):
        for digest in ('a' * 64, 'b' * 64):
            report = dict(baseline_pinned_unchanged=True, retained_evidence_unchanged=True,
                          target_metadata_stable=True, historical_full_digests=[digest, digest],
                          expected_original_log_sha256='a' * 64, historical_chunk_changes=[],
                          reconciled=True, gate_status='CLEARED', native_launch_allowed=True,
                          independent_original_provenance=True, blockers=[])
            assessment = prov.gate_assessment(report)
            self.assertEqual('BLOCKED', assessment['gate_status'])
            self.assertFalse(assessment['future_isolated_launch_eligible'])
            self.assertIn('PROHIBITED', assessment['authorization_boundary'])
            self.assertEqual('MISSING', assessment['conditions']['independent_drift_attribution']['status'])

    def test_ambiguous_or_redirected_retained_json_is_not_accepted(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for i, raw in enumerate((b'{"reconciled": false, "reconciled": true}', b'{"x": NaN}',
                                     b'{"x": 1e999}')):
                path = root / ('ambiguous%d.json' % i)
                path.write_bytes(raw)
                row, data = prov.retain_json(path, root, 'copy%d' % i)
                self.assertIsNone(data)
                self.assertFalse(row['identity_stable'])
            source = root / 'real.json'
            source.write_text('{}')
            alias = root / 'alias.json'
            alias.symlink_to(source)
            with patch.object(prov.diag, 'hash_read', side_effect=AssertionError('must refuse before read')):
                row, data = prov.retain_json(alias, root, 'redirected')
            self.assertIsNone(data)
            self.assertIn('redirect', row['error'])

    def test_chunk_boolean_offsets_and_nonlist_shapes_are_not_valid_evidence(self):
        row = dict(bytes_read=1, chunk_size=1, chunks=[dict(index=0, start=0, end_exclusive=1, sha256='a' * 64)])
        self.assertTrue(prov.valid_chunks(row))
        for key in ('bytes_read', 'chunk_size'):
            self.assertFalse(prov.valid_chunks(dict(row, **{key: True})))
        for key, value in (('index', False), ('start', False), ('end_exclusive', True)):
            bad = copy.deepcopy(row)
            bad['chunks'][0][key] = value
            self.assertFalse(prov.valid_chunks(bad))
        for chunks in ({}, 'x', [None]):
            self.assertFalse(prov.valid_chunks(dict(row, chunks=chunks)))

    def test_historical_comparison_requires_descriptor_identity_not_claimed_stability(self):
        target = Path('/fixture/target')
        metadata = dict(device=1, inode=2, size=4, atime_ns=3, mtime_ns=4, ctime_ns=5,
                        mode=33188, uid=1000, gid=1000, links=1)
        path = dict(requested_path=str(target), canonical_path=str(target),
                    stat=metadata, lstat=metadata)
        before = dict(path=str(target), before=path, after=path, descriptor_before=metadata,
                      descriptor_after=metadata, descriptor_target=str(target), identity_stable=True,
                      sha256='a' * 64, bytes_read=4, chunk_size=4,
                      chunks=[dict(index=0, start=0, end_exclusive=4, sha256='b' * 64)])
        after = copy.deepcopy(before)
        after['sha256'] = 'c' * 64
        after['chunks'][0]['sha256'] = 'd' * 64
        pair = prov.historical_pair(before, after, target)
        self.assertEqual('VALID_RETAINED_OBSERVATIONS', pair['status'])
        self.assertTrue(pair['metadata_equal'])
        self.assertFalse(pair['content_digests_equal'])
        self.assertEqual([0], [c['index'] for c in pair['chunk_changes']])
        for mutation in ({'descriptor_after': {}}, {'path': '/wrong'}, {'sha256': 'bad'},
                         {'bytes_read': 3}, {'identity_stable': 'true'}, {'before': {}},
                         {'before': []}, {'after': None}):
            bad = dict(after, **mutation)
            self.assertEqual('INVALID_OR_MISSING', prov.historical_pair(before, bad, target)['status'])
        forged = dict(before, sha256=after['sha256'])
        self.assertEqual('INVALID_OR_MISSING', prov.historical_pair(forged, after, target)['status'])


if __name__ == '__main__':
    unittest.main()
