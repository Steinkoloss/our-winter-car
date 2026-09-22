"""Static wiring contracts complement (do not replace) the linked H15 bridge tests."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


class PissAreaWiringTests(unittest.TestCase):
    def test_authenticated_actor_reaches_world_without_vehicle_claim(self):
        source = (ROOT / 'src/WinterMP.Core/Session/SessionManager.Messages.cs').read_text()
        route = source.split('case PissAreaIntent pissIntent when IsHost:', 1)[1].split('break;', 1)[0]
        self.assertIn('TryGetPlayerId(peer, out byte pissActor)', route)
        self.assertIn('OnPissAreaIntent(pissIntent, pissActor)', route)
        handlers = (ROOT / 'src/WinterMP.Core/Sync/WorldSyncManager.Handlers.cs').read_text()
        self.assertIn('EnsureSyncReady(); _pissAreas.OnIntent(message, actor);', handlers)
        self.assertIn('EnsureSyncReady(); _pissAreas.Apply(message);', handlers)
        self.assertNotIn('Ownership', route)

    def test_world_retains_both_clear_paths_update_and_absolute_join(self):
        source = (ROOT / 'src/WinterMP.Core/Sync/WorldSyncManager.cs').read_text()
        self.assertEqual(2, source.count('_pissAreas.Clear();'))
        update = (ROOT / 'src/WinterMP.Core/Sync/WorldSyncManager.Update.cs').read_text()
        self.assertIn('_pissAreas.Update(session!);', update)
        snapshots = (ROOT / 'src/WinterMP.Core/Sync/WorldSyncManager.Snapshots.cs').read_text()
        self.assertIn('var pissAreas = _pissAreas.BuildSnapshot();', snapshots)
        self.assertIn('yield return pissAreas;', snapshots)
        self.assertNotIn('PissAreaIntent', snapshots)

    def test_v263_wire_registry_and_spec_agree(self):
        protocol = (ROOT / 'src/WinterMP.Net/Protocol.cs').read_text()
        version = re.search(r'public const ushort Version = (\d+);', protocol).group(1)
        spec = (ROOT / 'protocol/PROTOCOL.md').read_text()
        self.assertIn('Protocol version: **' + version + '**', spec)
        ids = (ROOT / 'src/WinterMP.Net/Messages/IMessage.cs').read_text()
        self.assertIn('PissAreaIntent = 269,', ids)
        self.assertIn('| 269 | PissAreaIntent |', spec)
        registry = (ROOT / 'src/WinterMP.Net/Messages/MessageRegistry.cs').read_text()
        self.assertIn('{ MessageId.PissAreaIntent, () => new PissAreaIntent() }', registry)

    def test_no_new_save_format_and_tests_link_actual_bridge(self):
        source = '\n'.join(p.read_text() for p in (ROOT / 'src/WinterMP.Core/Sync').glob('PissAreaSync*.cs'))
        for forbidden in ('ES2.Save', 'File.Write', 'SaveFloat(', 'SendEvent("SAVEGAME")'):
            self.assertNotIn(forbidden, source)
        project = (ROOT / 'tools/PissArea.Tests/PissArea.Tests.csproj').read_text()
        self.assertIn('../../src/WinterMP.Core/Sync/PissAreaSync*.cs', project)


if __name__ == '__main__':
    unittest.main()
