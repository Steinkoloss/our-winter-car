"""Train FixedUpdate coordinator/lifecycle wiring only, NOT native cleanup evidence."""
from pathlib import Path
import unittest
from test_s09_callback_wiring import method

SOURCE = Path(__file__).resolve().parents[2]


class FixedCallbackWiringTests(unittest.TestCase):
    def setUp(self):
        self.world = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.cs').read_text()
        self.callbacks = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs').read_text()

    def test_exact_production_fixed_callback_is_linked_once(self):
        project = (SOURCE / 'tools/WorldSyncCallbacks.Tests/WorldSyncCallbacks.Tests.csproj').read_text()
        self.assertIn('../../src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs', project)
        self.assertNotIn('private void FixedUpdate()', self.world)
        self.assertEqual(1, self.callbacks.count('private void FixedUpdate()'))
        fixed = method(self.callbacks, 'private void FixedUpdate()')
        self.assertEqual(1, fixed.count('_train.FixedUpdate();'))
        self.assertIn('try { _train.FixedUpdate(); }', fixed)
        self.assertIn('HandleSyncError("FixedUpdate.TrainSync", e, _trainFixedFailure);', fixed)
        for gate in ('!_syncReady', '_worldSyncDisabled', '!_wasSessionActive',
                     'Time.unscaledTime < _syncErrorBackoffUntil', '!IsGameLevel()',
                     '!_trainFixedFailure.CanRun(Time.unscaledTime)'):
            self.assertLess(fixed.index(gate), fixed.index('_train.FixedUpdate();'))
        for forbidden in ('.IsHost', 'SessionState.Hosting', 'ReleaseEverything(', '.Clear(',
                          'SendWorldMessage(', 'Receive(', 'ES2.', 'InvokeRepeating(', 'new Action', '=>'):
            self.assertNotIn(forbidden, fixed)

    def test_fixed_record_uses_common_budget_and_only_resets_itself(self):
        self.assertIn('private readonly CallbackFailure _trainFixedFailure = new CallbackFailure();', self.callbacks)
        self.assertEqual('{\n            _trainFixedFailure.Clear();\n        }',
                         method(self.callbacks, 'private void ResetFixedUpdateErrors()'))
        reset = method(self.callbacks, 'private void ResetSyncErrors()')
        self.assertIn('ResetFixedUpdateErrors();', reset)
        for sibling in ('ResetLateUpdateErrors();', 'ResetVehicleUpdateErrors();'):
            self.assertIn(sibling, reset)

    def test_level_reset_after_level_equality_guard_before_lazy_init_and_native_train_clear(self):
        watch = method(self.world, 'private void WatchLevelChanges()')
        ordered = ('if (level == _lastLevel) return;', '_lastLevel = level;',
                   'ResetFixedUpdateErrors();', 'if (!_syncReady) return;', '_train.Clear();', '_woodDelivery.Clear();')
        positions = [watch.index(text) for text in ordered]
        self.assertEqual(sorted(positions), positions)
        self.assertNotIn('ResetSyncErrors();', watch)
        self.assertNotIn('_worldSyncDisabled', watch)
        self.assertNotIn('SyncEventLog.Clear();', watch)

    def test_session_release_keeps_train_and_later_cleanup_before_reset_including_lazy_path(self):
        release = method(self.world, 'private void ReleaseEverything()')
        self.assertIn('if (!_syncReady) { ResetSyncErrors(); return; }', release)
        ordered = ('_items.ReleaseSession();', '_npcTraffic.ReleaseSession();', '_train.Clear();',
                   '_woodDelivery.Clear();', '_welfare.Clear();', '_carRadio.Clear();')
        positions = [release.index(text) for text in ordered] + [release.rindex('ResetSyncErrors();')]
        self.assertEqual(sorted(positions), positions)
        self.assertNotIn('_trainFixedFailure', release)

    def test_destruction_resets_before_native_cleanup_without_refunding_global_budget(self):
        destroy = method(self.world, 'private void OnDestroy()')
        ordered = ('ResetFixedUpdateErrors();', '_vehicles.ClearDamageState();', '_train.Clear();',
                   '_woodDelivery.Clear();', '_wallet.Reset();', '_welfare.Clear();',
                   'if (Instance == this) Instance = null;')
        positions = [destroy.index(text) for text in ordered]
        self.assertEqual(sorted(positions), positions)
        self.assertNotIn('ResetSyncErrors();', destroy)
        self.assertNotIn('_worldSyncDisabled', destroy)
        self.assertNotIn('SyncEventLog.Clear();', destroy)


if __name__ == '__main__':
    unittest.main()
