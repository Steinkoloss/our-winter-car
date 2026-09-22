"""Static fixture checks only: no game, physics, input or native responses."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('v11_pane_audit', Path(__file__).parents[1] / 'v11_pane_audit.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class PaneStaticTests(unittest.TestCase):
    def fixture(self):
        catalog = {'paneScrape': {
            'vehicle': 'CORRIS', 'panePath': audit.PANE, 'paneFsm': 'Scrape',
            'freezingPath': 'CORRIS/Simulation/CarTempCorris', 'freezingFsm': 'Freezing',
            'cutoff': 'CutoffWindshield', 'toolName': 'ice scraper(itemx)',
            'deltaState': 'State 7', 'strokeState': 'Scrape 2', 'strokeEvent': 'WINDSHIELD',
            'picked': 'PickedObject', 'pickup': 'Set pivot 2', 'equip': 'Ice Scraper',
            'off': 'Off', 'drop': 'Drop part', 'throw': 'Drop part 2',
            'eyePath': 'PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Camera/Camera',
            'handPath': 'PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand',
            'handFsm': 'PickUp', 'insidePath': 'CORRIS/Functions/PlayerTrigger'}}
        c = catalog['paneScrape']
        dump = {'meta': {'toolsVersion': '0.1.0'}, 'fsms': []}
        for path, name in ((c['panePath'], 'Scrape'), (c['freezingPath'], 'Freezing'),
                           (c['freezingPath'], 'GlassFrosting'), (c['handPath'], 'PickUp'),
                           (c['insidePath'], 'PlayerTrigger'), ('Spawner/CreateItems', 'IceScraper')):
            dump['fsms'].append({'path': path, 'fsmName': name, 'states': [{'name': 'fixture'}],
                                 'variables': {}, 'events': []})
        dump['fsms'].append({'path': c['panePath'], 'fsmName': 'Break', 'states': []})
        return catalog, dump

    def test_exact_fsm_not_same_path_break_or_other_vehicle(self):
        catalog, dump = self.fixture()
        dump['fsms'].append({'path': 'SORBET/BODY/Windshield/collider', 'fsmName': 'Scrape'})
        result = audit.discover_pane(catalog, dump)
        self.assertEqual('Scrape', result['fsms']['pane']['fsmName'])
        self.assertEqual(audit.PANE, result['fsms']['pane']['path'])
        self.assertEqual(6, len(result['fsms']))

    def test_missing_or_ambiguous_exact_pane_is_not_a_pass(self):
        catalog, dump = self.fixture()
        for rows in (dump['fsms'][1:], dump['fsms'] + [copy.deepcopy(dump['fsms'][0])]):
            with self.assertRaisesRegex(ValueError, 'exactly one'):
                audit.discover_pane(catalog, dict(dump, fsms=rows))

    def test_binding_scope_cannot_expand(self):
        catalog, dump = self.fixture()
        for key, bad in (('panePath', 'CORRIS/BODY/Rear/collider'), ('vehicle', 'SORBET'),
                         ('cutoff', 'CutoffRear'), ('toolName', 'axe(itemx)')):
            changed = copy.deepcopy(catalog)
            changed['paneScrape'][key] = bad
            with self.assertRaisesRegex(ValueError, 'one-pane'):
                audit.discover_pane(changed, dump)

    def test_old_dump_omissions_do_not_prove_no_actions_no_save_or_live_tool(self):
        result = audit.discover_pane(*self.fixture())
        self.assertFalse(result['action_fields_available'])
        self.assertFalse(result['global_transitions_available'])
        self.assertEqual('UNKNOWN', result['native_persistence'])
        self.assertEqual('UNKNOWN', result['live_tool_instance'])
        self.assertFalse(result['native_executed'])

    def test_every_profile_field_is_pinned_not_only_pane_identity(self):
        catalog, dump = self.fixture()
        for key in catalog['paneScrape']:
            for change in ('wrong', None):
                with self.subTest(key=key, change=change):
                    changed = copy.deepcopy(catalog)
                    if change is None:
                        del changed['paneScrape'][key]
                    else:
                        changed['paneScrape'][key] = change
                    with self.assertRaisesRegex(ValueError, 'one-pane'):
                        audit.discover_pane(changed, dump)

    def test_action_type_names_are_not_native_action_fields(self):
        catalog, dump = self.fixture()
        for representation in (['FloatAdd'], [{'type': 'FloatAdd'}], None, {}, 'FloatAdd'):
            with self.subTest(representation=representation):
                for fsm in dump['fsms']:
                    for state in fsm.get('states', []):
                        state['actions'] = representation
                self.assertFalse(audit.discover_pane(catalog, dump)['action_fields_available'])

    def test_unversioned_action_objects_are_not_a_supported_native_field_schema(self):
        catalog, dump = self.fixture()
        for fsm in dump['fsms']:
            for state in fsm.get('states', []):
                state['actions'] = [{'type': 'FloatAdd', 'fields': {'add': 'ScrapeEfficiency'}}]
        # Neither current nor old FsmDumper emits this schema; do not invent support.
        self.assertFalse(audit.discover_pane(catalog, dump)['action_fields_available'])

    def test_explicit_empty_globals_are_distinct_from_missing_metadata(self):
        catalog, dump = self.fixture()
        for fsm in dump['fsms']:
            fsm['globalTransitions'] = []
            for state in fsm.get('states', []):
                state['actions'] = []
        result = audit.discover_pane(catalog, dump)
        self.assertTrue(result['global_transitions_available'])
        self.assertTrue(result['action_fields_available'])
        self.assertEqual('UNKNOWN', result['native_persistence'])


if __name__ == '__main__':
    unittest.main()
