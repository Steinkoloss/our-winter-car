"""Train message lifecycle/object-iterator wiring: static, not native evidence."""
from pathlib import Path
import unittest
from test_s09_callback_wiring import method

SOURCE = Path(__file__).resolve().parents[2]


class TrainMessageWiringTests(unittest.TestCase):
    def setUp(self):
        self.world = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.cs').read_text()
        self.callbacks = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs').read_text()
        self.snapshots = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.Snapshots.cs').read_text()
        self.handlers = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.Handlers.cs').read_text()

    def test_all_three_snapshot_paths_share_one_capture_boundary_and_original_order(self):
        for signature, call, next_call in (
            ('public IEnumerable<IMessage> BuildResyncMessages(', 'BuildTrainSnapshot()', '_lottery.BuildSnapshot()'),
            ('public IEnumerable<IMessage> BuildObjectStateMessages(', 'BuildTrainSnapshot(netId)', '_items.BuildMeatState(netId)'),
            ('public IEnumerable<IMessage> BuildWorldSnapshot(', 'BuildTrainSnapshot()', '_trailer.Snapshot()'),
        ):
            body = method(self.snapshots, signature)
            self.assertEqual(1, body.count(call))
            self.assertLess(body.index(call), body.index(next_call))
            self.assertNotIn('_train.Snapshot()', body)
        body = method(self.snapshots, 'private TrainState? BuildTrainSnapshot(')
        self.assertEqual(1, body.count('_train.Snapshot()'))
        self.assertLess(body.index('try {'), body.index('_train.NetId'))
        self.assertIn('HandleSyncError("Snapshot.TrainSync", e, _trainSnapshotFailure)', body)

    def test_receive_keeps_game_prepare_receive_order_without_replay_or_authority_edits(self):
        body = method(self.handlers, 'public void OnTrainState(')
        order = ['IsGameLevel()', 'try {', 'EnsureSyncReady();', '_train.Receive(message);']
        positions = [body.index(x) for x in order]
        self.assertEqual(sorted(positions), positions)
        self.assertEqual(1, body.count('_train.Receive(message);'))
        self.assertIn('HandleSyncError("Receive.TrainSync", e, _trainReceiveFailure)', body)
        for forbidden in ('.Clear(', 'SendTo(', 'Broadcast(', 'IsHost', 'Action<', 'Queue<', 'ES2.'):
            self.assertNotIn(forbidden, body)

    def test_resets_precede_native_cleanup_but_preserve_global_history(self):
        for signature in ('private void OnDestroy()', 'private void WatchLevelChanges()'):
            body = method(self.world, signature)
            self.assertLess(body.index('ResetTrainMessageErrors();'), body.index('_train.Clear();'))
            self.assertNotIn('ResetSyncErrors();', body)
            if 'WatchLevel' in signature:
                self.assertLess(body.index('if (level == _lastLevel) return;'), body.index('ResetTrainMessageErrors();'))
                self.assertLess(body.index('ResetTrainMessageErrors();'), body.index('if (!_syncReady) return;'))
        reset = method(self.callbacks, 'private void ResetTrainMessageErrors()')
        self.assertEqual('{\n            _trainReceiveFailure.Clear();\n            _trainSnapshotFailure.Clear();\n        }', reset)
        self.assertIn('ResetTrainMessageErrors();', method(self.callbacks, 'private void ResetSyncErrors()'))
        release = method(self.world, 'private void ReleaseEverything()')
        self.assertIn('if (!_syncReady) { ResetSyncErrors(); return; }', release)
        self.assertLess(release.index('_train.Clear();'), release.rindex('ResetSyncErrors();'))

    def test_fixture_extracts_real_packet_checks_and_lazy_iterators(self):
        fixture = (SOURCE / 'tools/s09_train_fixture.py').read_text()
        for sig in ('private void OnPacketReceived(', 'private void HandleSnapshotRequest(',
                    'private void HandleResyncRequest(', 'internal void Receive(',
                    'public IEnumerable<IMessage> BuildResyncMessages(', 'public IEnumerable<IMessage> BuildWorldSnapshot('):
            self.assertIn(sig, fixture)
        self.assertIn("('case TrainState trainState when !IsHost:', 'case CoffeeState')", fixture)
        self.assertIn('s09_train_fixture.py', (SOURCE / 'tools/TrainDispatch.Tests/TrainDispatch.Tests.csproj').read_text())


if __name__ == '__main__':
    unittest.main()
