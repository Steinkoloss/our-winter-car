"""Static beer-case wiring/graph checks, not game execution or native discovery."""
import json
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[2]


class BeerCaseWiringTests(unittest.TestCase):
    def text(self, path):
        return (SOURCE / path).read_text()

    def test_relative_remove_bottle_is_not_a_generic_control(self):
        catalog = json.loads(self.text('catalog/sync-catalog.json'))
        self.assertFalse(any(r.get('objectName') == 'beercase' and r.get('fsmName') == 'Use'
                             for r in catalog['controls']))
        source = self.text('src/WinterMP.Core/Sync/FsmWorldSync.Registry.cs')
        self.assertIn('BeerCasePolicy.QuarantineControl(fsm.gameObject.name, fsm.FsmName)', source)

    def test_session_uses_authenticated_actor_and_guest_only_result(self):
        source = self.text('src/WinterMP.Core/Session/SessionManager.Messages.cs')
        self.assertRegex(source, r'case BeerCaseExtractIntent beerCaseIntent when IsHost:\s*if \(TryGetPlayerId\(peer, out byte beerCaseActor\)\) Sync.WorldSyncManager.Instance\?\.OnBeerCaseExtract\(beerCaseIntent, beerCaseActor\);')
        self.assertRegex(source, r'case BeerCaseUpdate beerCaseUpdate when !IsHost:\s*Sync.WorldSyncManager.Instance\?\.OnBeerCaseUpdate\(beerCaseUpdate\);')

    def test_full_group_targeted_snapshots_and_release_are_wired(self):
        source = self.text('src/WinterMP.Core/Sync/WorldSyncManager.Snapshots.cs')
        self.assertEqual(2, source.count('_items.BuildBeerCaseStates()'))
        self.assertEqual(1, source.count('_items.BuildBeerCaseState(netId)'))
        source = self.text('src/WinterMP.Core/Sync/ItemWorldSync.cs')
        self.assertRegex(source, r'internal void Clear\(\)\s*{\s*ClearBeerCase\(\);')
        self.assertRegex(source, r'internal void ReleaseSession\(\)\s*{\s*ClearBeerCase\(\);')
        self.assertIn('_items.ForgetBeerCasePlayer(playerId)', self.text('src/WinterMP.Core/Sync/WorldSyncManager.Handlers.cs'))

    def test_wire_documentation_tracks_version_and_ids(self):
        protocol = self.text('protocol/PROTOCOL.md')
        version = re.search(r'public const ushort Version = (\d+);', self.text('src/WinterMP.Net/Protocol.cs')).group(1)
        self.assertIn('Protocol version: **' + version + '**', protocol)
        ids = self.text('src/WinterMP.Net/Messages/IMessage.cs')
        for name, value in (('BeerCaseExtractIntent', 265), ('BeerCaseUpdate', 266)):
            self.assertIn(f'{name} = {value}', ids)
            self.assertIn(f'`{name}`', protocol)

    def test_graph_does_not_establish_capacity_or_save_keys(self):
        dump = json.loads(self.text('catalog/dump-23268598.json'))
        cases = [f for f in dump['fsms'] if f['path'] == 'beercase' and f['fsmName'] == 'Use']
        self.assertEqual(1, len(cases))
        fsm = cases[0]
        self.assertEqual(1319252914, fsm['netId'])
        self.assertFalse(fsm['active'])
        self.assertEqual(['DestroyProgress', 'DestroyedBottles'], fsm['variables']['IntVariables'])
        self.assertEqual(['ID', 'UniqueTagBottles'], fsm['variables']['StringVariables'])
        states = {s['name']: s for s in fsm['states']}
        self.assertTrue({'Remove bottle', 'Save', 'Load', 'NPC Drink', 'Bottles'}.issubset(states))
        self.assertNotIn('actions', states['Remove bottle'])
        self.assertNotIn('globalTransitions', fsm)

    def test_production_bridge_has_no_generic_event_inventory_or_save_writer(self):
        bridge = self.text('src/WinterMP.Core/Sync/ItemWorldSync.BeerCase.cs')
        for forbidden in ('FireRemoteEntry(', 'SendEvent(', 'ES2.', '_items[', 'AnnounceItemDespawn(', 'Instantiate(', 'DestroyedBottles'):
            self.assertNotIn(forbidden, bridge)
        # Native bootstrap must not be implied by the portable bind seams.
        self.assertIn('No caller installs a native binding yet', bridge)
        for path in (SOURCE / 'src/WinterMP.Core').rglob('*.cs'):
            if path.name == 'ItemWorldSync.BeerCase.cs' or 'obj' in path.parts or 'bin' in path.parts:
                continue
            self.assertNotIn('BindBeerCaseHost(', path.read_text(), str(path))


if __name__ == '__main__':
    unittest.main()
