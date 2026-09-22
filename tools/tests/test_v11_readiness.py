"""Readiness parser regressions. Catalog mutants are not native game evidence."""
import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import v11_contact_discovery as audit


class ReadinessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog = audit.strict_json((audit.SOURCE / 'catalog/sync-catalog.json').read_bytes())
        cls.dump = audit.strict_json((audit.SOURCE / 'catalog/dump-23268598.json').read_bytes())
        cls.sources = {p: (audit.SOURCE / p).read_text() for p in audit.TEXT_INPUTS}

    def report(self, dump=None, sources=None):
        return audit.classify(self.catalog, self.dump if dump is None else dump,
                              self.sources if sources is None else sources)['readiness']

    def test_nested_equipment_and_contact_routes_are_traced_without_native_promotion(self):
        r = self.report()
        self.assertTrue(r['catalog_routes_match'])
        self.assertEqual('NOT_READY', r['native_status'])
        self.assertEqual('NOT_TESTED', r['portable_fixture_execution']['status'])
        self.assertFalse(r['native_executed'])
        self.assertEqual({'native_objects', 'contact', 'tool_lease', 'host_away_guest',
                          'accepted_action', 'absolute_result', 'vanilla_save_reload'},
                         set(r['prerequisites']))
        for p in r['prerequisites'].values():
            self.assertEqual('NOT_READY', p['status'])
            self.assertTrue(p['missing_artifacts'])
            self.assertTrue(p['source_evidence'])
        nodes = r['catalog_nodes']
        self.assertEqual('icescraper0', nodes['tool_save']['path'])
        self.assertNotEqual(nodes['pane']['net_id'], nodes['pane_body']['net_id'])
        self.assertEqual('DECLARED', nodes['tool_save']['status'])
        self.assertEqual('NOT_RECORDED', nodes['pane']['states']['Scrape 2']['actions']['status'])
        self.assertTrue(nodes['pane']['pointer'].startswith('/fsms/'))
        self.assertEqual('UNKNOWN', r['persistence']['windshield_saved'])
        self.assertEqual('DECLARED', r['persistence']['tool_save_declarations'])

    def test_nested_transition_change_or_ambiguity_is_not_hidden_by_matching_fsm_names(self):
        for kind in ('changed', 'duplicate', 'missing', 'malformed'):
            with self.subTest(kind=kind):
                d = copy.deepcopy(self.dump)
                hand = next(f for f in d['fsms'] if f['path'] == audit.PROFILE['handPath'] and f['fsmName'] == 'PickUp')
                state = next(s for s in hand['states'] if s['name'] == 'Check item')
                edge = next(t for t in state['transitions'] if t['event'] == 'ICESCRAPER')
                if kind == 'changed': edge['to'] = 'Flashlight'
                elif kind == 'duplicate': state['transitions'].append(copy.deepcopy(edge))
                elif kind == 'missing': state['transitions'].remove(edge)
                else: state['transitions'] = 'ICESCRAPER'
                r = self.report(d)
                self.assertFalse(r['catalog_routes_match'])
                self.assertNotEqual('DECLARED', r['catalog_nodes']['hand']['states']['Check item']['routes']['ICESCRAPER']['status'])
                self.assertFalse(audit.classify(self.catalog, d, self.sources)['static_checks_passed'])

    def test_action_fields_remain_untrusted_regardless_of_action_shape(self):
        for actions in ([], ['SendEventByName'], [{'type': 'SendEventByName', 'sendEvent': 'WINDSHIELD'}], None):
            with self.subTest(actions=actions):
                d = copy.deepcopy(self.dump)
                f = next(f for f in d['fsms'] if f['path'] == audit.PROFILE['panePath'] and f['fsmName'] == 'Scrape')
                next(s for s in f['states'] if s['name'] == 'Scrape 2')['actions'] = actions
                r = self.report(d)
                action = r['catalog_nodes']['pane']['states']['Scrape 2']['actions']
                self.assertFalse(action['live_fields_verified'])
                self.assertEqual(actions, action['raw'])
                self.assertEqual('NOT_READY', r['prerequisites']['accepted_action']['status'])

    def test_missing_or_duplicate_nested_tool_save_is_not_proof_of_no_persistence(self):
        for duplicate in (False, True):
            d = copy.deepcopy(self.dump)
            f = next(f for f in d['fsms'] if f['path'] == 'icescraper0' and f['fsmName'] == 'Use')
            if duplicate: d['fsms'].append(copy.deepcopy(f))
            else: d['fsms'].remove(f)
            r = self.report(d)
            self.assertFalse(r['catalog_routes_match'])
            self.assertEqual('UNKNOWN', r['persistence']['windshield_saved'])
            self.assertEqual('UNKNOWN', r['persistence']['tool_saved_fields'])
            self.assertEqual('AMBIGUOUS' if duplicate else 'MISSING', r['catalog_nodes']['tool_save']['status'])

    def test_driver_shortcuts_and_unproved_host_away_are_explicit(self):
        r = self.report()
        self.assertIn('pane-contact', r['driver_limits'])
        self.assertIn('collider.Raycast', r['driver_limits']['pane-contact'])
        self.assertIn('not ordinary input', r['driver_limits']['pane-stroke-bridge'])
        self.assertIn('not asserted', r['driver_limits']['bridge_host_away'])
        self.assertIn('portable only', r['driver_limits']['wrong_pane'])
        self.assertEqual('MISSING', r['protected_input']['independent_original_provenance'])
        self.assertFalse(r['protected_input']['native_launch_allowed'])

    def test_source_seam_drift_is_recorded_not_silently_exact(self):
        sources = dict(self.sources)
        path = 'tools/GuestSaveProbe/LiveBagProbe.Pane.cs'
        self.assertIn('coll.Raycast', sources[path])
        sources[path] = sources[path].replace('coll.Raycast', 'changed.Raycast')
        r = self.report(sources=sources)
        self.assertFalse(r['source_seams_match'])
        self.assertTrue(r['missing_source_anchors'])


if __name__ == '__main__':
    unittest.main()
