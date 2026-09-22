"""Portable audit-tool tests; no native objects, game or synthetic game replies."""
import copy
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import v11_contact_discovery as audit


class ContactDiscoveryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog = json.loads((audit.SOURCE / 'catalog/sync-catalog.json').read_text())
        cls.dump = json.loads((audit.SOURCE / 'catalog/dump-23268598.json').read_text())
        cls.sources = {p: (audit.SOURCE / p).read_text() for p in audit.TEXT_INPUTS}

    def report(self, catalog=None, dump=None, sources=None):
        return audit.classify(catalog or self.catalog, dump or self.dump, sources or self.sources)

    def test_current_profile_matches_production_and_all_bindings_are_classified(self):
        r = self.report()
        self.assertTrue(r['static_checks_passed'])
        self.assertEqual(set(audit.PROFILE), {row['id'][8:] for row in r['bindings'] if row['id'].startswith('profile.')})
        self.assertTrue(all(row['classification'] in ('exact', 'changed', 'ambiguous', 'unreachable') for row in r['bindings']))
        self.assertEqual(len(r['bindings']), len({row['id'] for row in r['bindings']}))
        self.assertEqual(6, len(r['selected_fsms']))
        self.assertFalse(r['native_executed'])
        self.assertEqual('partial', r['V11_status'])

    def test_names_only_dump_cannot_supply_live_fields_or_initialization(self):
        r = self.report()
        live = [row for row in r['bindings'] if row['level'] == 'live_native']
        self.assertTrue(live)
        self.assertTrue(all(row['classification'] == 'unreachable' for row in live))
        for name in ('glass_actions', 'material_6', 'hand_equipment', 'eye_contact', 'ownership', 'save_load_reset'):
            self.assertIn('native.' + name, {row['id'] for row in live})
        self.assertTrue(all(v == 'NOT_TESTED' for v in r['dimensions'].values()))
        self.assertTrue(all(f['runtime_initialized'] is None and f['runtime_started'] is None
                            for f in r['selected_fsms'].values()))

    def test_changed_or_missing_profile_cannot_hide_behind_matching_dump(self):
        for key in audit.PROFILE:
            for value in ('changed', None):
                with self.subTest(key=key, value=value):
                    c = copy.deepcopy(self.catalog)
                    if value is None:
                        del c['paneScrape'][key]
                    else:
                        c['paneScrape'][key] = value
                    r = self.report(catalog=c)
                    self.assertFalse(r['static_checks_passed'])
                    self.assertEqual('changed', next(row for row in r['bindings'] if row['id'] == 'profile.' + key)['classification'])

    def test_duplicate_fsm_state_and_variable_fail_closed(self):
        for kind in ('fsm', 'state', 'variable'):
            with self.subTest(kind=kind):
                d = copy.deepcopy(self.dump)
                f = next(f for f in d['fsms'] if f['path'] == audit.PROFILE['panePath'] and f['fsmName'] == 'Scrape')
                if kind == 'fsm':
                    d['fsms'].append(copy.deepcopy(f))
                elif kind == 'state':
                    f['states'].append(copy.deepcopy(next(s for s in f['states'] if s['name'] == 'Scrape 2')))
                else:
                    f['variables']['FloatVariables'].append('Distance')
                r = self.report(dump=d)
                self.assertFalse(r['static_checks_passed'])
                self.assertIn('ambiguous', [row['classification'] for row in r['bindings']])

    def test_missing_material_is_changed_not_current_native_absence(self):
        d = copy.deepcopy(self.dump)
        f = next(f for f in d['fsms'] if f['path'] == audit.PROFILE['freezingPath'] and f['fsmName'] == 'Freezing')
        f['variables']['MaterialVariables'].remove('6')
        r = self.report(dump=d)
        self.assertFalse(r['static_checks_passed'])
        self.assertEqual('changed', next(row for row in r['bindings'] if row['id'] == 'variable.freezing.Material.6')['classification'])
        self.assertEqual('unreachable', next(row for row in r['bindings'] if row['id'] == 'native.material_6')['classification'])

    def test_production_profile_or_signature_anchor_drift_is_not_exact(self):
        for path, old, new in (
            (audit.PROFILE_SOURCE, '"CutoffWindshield"', '"CutoffRear"'),
            (audit.BINDINGS_SOURCE, 'found.Fsm.Initialized && found.Fsm.Started', 'true'),
            (audit.SYNC_SOURCE, 'hit.collider == _pane', 'hit.collider != null')):
            with self.subTest(path=path):
                sources = dict(self.sources)
                self.assertIn(old, sources[path])
                sources[path] = sources[path].replace(old, new)
                self.assertFalse(self.report(sources=sources)['static_checks_passed'])

    def test_duplicate_nonfinite_json_and_redirected_input_are_refused(self):
        for text in ('{"x":1,"x":2}', '{"x":NaN}', '{"x":1e999}'):
            with self.assertRaises(ValueError):
                audit.strict_json(text)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'real.json').write_text('{}')
            (root / 'link.json').symlink_to(root / 'real.json')
            with self.assertRaises(ValueError):
                audit.read_input(root / 'link.json', root)

    def test_provenance_must_belong_to_this_run_and_not_override_gate(self):
        with tempfile.TemporaryDirectory() as temp:
            run = Path(temp)
            gate = run / 'gate'
            gate.mkdir()
            raw = {'run': str(run), 'output': str(gate), 'gate_status': 'CLEARED',
                   'native_launch_allowed': True, 'target_content_bytes_read': 0,
                   'host_observation_status': 'NOT_RUN: retained-only reconciliation', 'exit_code': 1}
            (gate / 'report.json').write_text(json.dumps(raw))
            (gate / 'assessment.json').write_text(json.dumps(audit.provenance.gate_assessment(raw)))
            with self.assertRaises(ValueError):
                audit.load_gate(run, 'gate')
            raw.update(gate_status='BLOCKED', native_launch_allowed=False)
            (gate / 'report.json').write_text(json.dumps(raw))
            report, assessment = audit.load_gate(run, 'gate')
            self.assertEqual('BLOCKED', assessment['gate_status'])
            raw['run'] = str(run / 'old')
            (gate / 'report.json').write_text(json.dumps(raw))
            with self.assertRaises(ValueError):
                audit.load_gate(run, 'gate')

    def test_execute_never_calls_native_or_overwrites_evidence_and_replays_offline(self):
        with tempfile.TemporaryDirectory() as temp:
            rounds = Path(temp)
            run = rounds / 'test-work'
            run.mkdir()
            (run / 'contract.json').write_text('{}')
            gate = run / 'gate'
            gate.mkdir()
            # Explicit portable provenance fixture, not a native/game reply.
            raw = {'run': str(run), 'output': str(gate), 'gate_status': 'BLOCKED',
                   'native_launch_allowed': False, 'target_content_bytes_read': 0,
                   'host_observation_status': 'NOT_RUN: retained-only reconciliation', 'exit_code': 1}
            (gate / 'report.json').write_text(json.dumps(raw))
            (gate / 'assessment.json').write_text(json.dumps(audit.provenance.gate_assessment(raw)))
            with mock.patch.object(audit.provenance.diag, 'ROUNDS', rounds), \
                    mock.patch.dict(os.environ, RUN=str(run)), \
                    mock.patch('subprocess.Popen', side_effect=AssertionError('No child/native launch')), \
                    mock.patch('os.kill', side_effect=AssertionError('No process signaling')):
                receipt = audit.execute(run, 'audit', 'gate')
                self.assertEqual(1, receipt['exit_code'], receipt.get('error'))
                self.assertTrue(receipt['inputs_unchanged'])
                self.assertEqual([], receipt['cleanup']['rig_accesses'])
                verified = audit.verify(run / 'audit')
                self.assertTrue(verified['offline_replay_equal'])
                with self.assertRaises(FileExistsError):
                    audit.execute(run, 'audit', 'gate')
                target = run / 'audit/inputs/catalog/sync-catalog.json'
                target.write_bytes(target.read_bytes() + b' ')
                with self.assertRaisesRegex(ValueError, 'hash'):
                    audit.verify(run / 'audit')


if __name__ == '__main__':
    unittest.main()
