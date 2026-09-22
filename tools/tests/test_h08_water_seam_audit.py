"""Static parser/discovery tests only; no synthetic transfer success."""
import copy
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import h08_water_seam_audit as audit

SOURCE = Path(__file__).resolve().parents[2]


class SaunaWaterDiscoveryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog = json.loads((SOURCE / 'catalog/dump-23268598.json').read_text())
        cls.steam = next(f for f in cls.catalog['fsms'] if f['path'] == audit.STEAM and f['fsmName'] == 'Steam')

    def report(self, steam=None):
        # A catalog-only input intentionally supplies NO native action operands.
        return audit.analyze(self.catalog, {'fsms': [self.steam if steam is None else steam]})

    def test_real_catalog_water_bool_is_not_a_finite_supply(self):
        result = self.report()
        self.assertEqual('bool', result['steam_water_type'])
        self.assertFalse(result['steam_has_numeric_water'])
        self.assertEqual('NOT_ESTABLISHED', result['transfer_readiness'])

    def test_exact_nested_graph_required_not_a_direct_trigger_shortcut(self):
        other = copy.deepcopy(self.steam)
        other['path'] = audit.ROOT + '/StoveTrigger'
        with self.assertRaisesRegex(ValueError, 'exactly one audited nested'):
            self.report(other)
        with self.assertRaises(ValueError):
            audit.analyze(self.catalog, {'fsms': [self.steam, self.steam]})

    def test_discovery_deduplicates_records_without_inventing_operands(self):
        result = self.report()
        records = result['related_catalog_records']
        self.assertEqual(1, len([r for r in records if r['path'] == 'EQUIPMENTS/dipper(itemx)']))
        hand = next(r for r in records if r['fsmName'] == 'PickUp')
        self.assertIn('Sauna dipper', [s['name'] for s in hand['states']])
        missing = result['missing_catalog_action_operands']
        self.assertTrue(any(m['path'] == hand['path'] and 'Sauna dipper' in m['states_without_operands'] for m in missing))
        self.assertNotIn('globalTransitions', hand)
        self.assertTrue(all('actions' not in s for s in hand['states']))

    def test_bucket_water_name_alone_never_certifies_transfer(self):
        result = self.report()
        water = next(r for r in result['related_catalog_records'] if r['path'] == 'EQUIPMENTS/water bucket(itemx)/Water')
        self.assertIn('Water', water['variables']['FloatVariables'])
        self.assertEqual('NOT_ESTABLISHED', result['transfer_readiness'])
        changed = copy.deepcopy(self.steam)
        changed['variables']['FloatVariables'].append('Water')
        result = self.report(changed)
        self.assertTrue(result['steam_has_numeric_water'])
        self.assertEqual('NOT_ESTABLISHED', result['transfer_readiness'])


if __name__ == '__main__':
    unittest.main()
