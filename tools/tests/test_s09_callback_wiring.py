"""Static lifecycle wiring assertions, not native teardown or gameplay evidence."""
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[2]


def method(source, signature):
    start = source.index('{', source.index(signature))
    depth = 1
    end = start + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


class CallbackWiringTests(unittest.TestCase):
    def setUp(self):
        self.world = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.cs').read_text()
        self.callbacks = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs').read_text()
        self.update = (SOURCE / 'src/WinterMP.Core/Sync/WorldSyncManager.Update.cs').read_text()

    def test_real_callback_source_is_compiled_by_portable_tests(self):
        project = (SOURCE / 'tools/WorldSyncCallbacks.Tests/WorldSyncCallbacks.Tests.csproj').read_text()
        self.assertIn('../../src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs', project)
        self.assertIn('../../src/WinterMP.Core/Sync/WorldSyncManager.Update.cs', project)
        self.assertNotIn('private void LateUpdate()', self.world)
        self.assertNotIn('private void Update()', self.world)
        self.assertNotIn('private void UpdateWorldSync()', self.world)
        self.assertIn('UpdateWorldSync();', method(self.callbacks, 'private void Update()'))

    def test_session_release_preserves_native_cleanup_and_resets_even_before_lazy_init(self):
        release = method(self.world, 'private void ReleaseEverything()')
        self.assertIn('if (!_syncReady) { ResetSyncErrors(); return; }', release)
        for native in ('_items.ReleaseSession();', '_npcTraffic.ReleaseSession();', '_fsm.Clear();',
                       '_ventti.Clear();', '_trailer.Clear();'):
            self.assertIn(native, release)
            self.assertLess(release.index(native), release.rindex('ResetSyncErrors();'))
        reset = method(self.callbacks, 'private void ResetSyncErrors()')
        for line in ('ResetLateUpdateErrors();', '_worldSyncDisabled = false;', '_syncErrorCount = 0;',
                     '_syncErrorBackoffUntil = 0f;', 'SyncEventLog.Clear();'):
            self.assertIn(line, reset)

    def test_scene_change_clears_local_gate_without_refunding_global_session_budget(self):
        watch = method(self.world, 'private void WatchLevelChanges()')
        self.assertLess(watch.index('if (level == _lastLevel) return;'), watch.index('ResetLateUpdateErrors();'))
        self.assertLess(watch.index('ResetLateUpdateErrors();'), watch.index('if (!_syncReady) return;'))
        self.assertNotIn('ResetSyncErrors();', watch)
        self.assertIn('_ventti.Clear();', watch)
        self.assertIn('_trailer.Clear();', watch)
        reset = method(self.callbacks, 'private void ResetLateUpdateErrors()')
        for name in ('_vehicleLateFailure', '_trailerLateFailure', '_venttiLateFailure'):
            self.assertIn(name + '.Clear();', reset)

    def test_destruction_clears_local_error_state_without_removing_cleanup_or_diagnostics(self):
        destroy = method(self.world, 'private void OnDestroy()')
        self.assertIn('ResetLateUpdateErrors();', destroy)
        self.assertNotIn('ResetSyncErrors();', destroy)
        self.assertNotIn('SyncEventLog.Clear();', destroy)
        for native in ('_vehicles.ClearDamageState();', '_vehicles.ClearVehicleStateStreams();',
                       '_ventti.Clear();', '_trailer.Clear();', 'if (Instance == this) Instance = null;'):
            self.assertIn(native, destroy)

    def test_local_quarantine_does_not_rollback_authority_or_disable_other_callbacks(self):
        late = method(self.callbacks, 'private void LateUpdate()')
        self.assertLess(late.index('_vehicles.LateUpdateRemoteVehicles(now)'), late.index('_trailer.LateUpdate()'))
        self.assertLess(late.index('_trailer.LateUpdate()'), late.index('SessionManager.Instance'))
        self.assertLess(late.index('SessionManager.Instance'), late.index('_ventti.LateUpdate(session)'))
        for forbidden in ('ReleaseEverything(', '.Clear(', '.SendWorldMessage(', '.IsHost', 'ES2.',
                          'StartCoroutine(', 'InvokeRepeating(', 'new Action'):
            self.assertNotIn(forbidden, late)
        for update in ('_vehicles.UpdateVehicleStates(session!)', '_ventti.Update(session!)', '_trailer.Update(session!, _items)'):
            self.assertIn(update, self.update)

    def test_all_ten_vehicle_updates_have_distinct_gates_in_original_order(self):
        names = ('UpdateVehicleStates', 'UpdateStarterDraws', 'UpdateStarterWear', 'UpdateVehicleCoolant',
                 'UpdateDrivetrainWearStates', 'UpdateWheelHealthStates', 'UpdateVehicleDamage',
                 'UpdateVehicleCondition', 'UpdateFuelTransfers', 'UpdateVehicleClimate')
        selected = self.update[self.update.index('_train.Update(session!);'):self.update.index('_clothing.Update(session!);')]
        self.assertEqual(list(names), re.findall(r'_vehicles\.(\w+)\(session!\);', selected))
        gates = re.findall(r'if \((\w+)\.CanRun\(vehicleNow\)\)', selected)
        self.assertEqual(10, len(set(gates)))
        for name, gate in zip(names, gates):
            self.assertIn('try { _vehicles.' + name + '(session!); }', selected)
            self.assertIn('HandleSyncError("Update.VehicleWorldSync.' + name + '", e, ' + gate + ');', selected)
            self.assertIn('private readonly CallbackFailure ' + gate + ' = new CallbackFailure();', self.update)
            self.assertIn(gate + '.Clear();', method(self.update, 'private void ResetVehicleUpdateErrors()'))
        for forbidden in ('.IsHost', 'ReleaseEverything(', '.Clear(', '.SendWorldMessage(', 'ES2.',
                          'StartCoroutine(', 'InvokeRepeating(', 'new Action', '=>', '_worldSyncDisabled'):
            self.assertNotIn(forbidden, selected)

    def test_vehicle_gates_reset_at_all_existing_lifecycle_boundaries(self):
        reset = method(self.callbacks, 'private void ResetSyncErrors()')
        self.assertIn('ResetVehicleUpdateErrors();', reset)
        watch = method(self.world, 'private void WatchLevelChanges()')
        self.assertLess(watch.index('if (level == _lastLevel) return;'), watch.index('ResetVehicleUpdateErrors();'))
        self.assertLess(watch.index('ResetVehicleUpdateErrors();'), watch.index('if (!_syncReady) return;'))
        self.assertIn('ResetVehicleUpdateErrors();', method(self.world, 'private void OnDestroy()'))
        local_reset = method(self.update, 'private void ResetVehicleUpdateErrors()')
        self.assertEqual(10, local_reset.count('.Clear();'))
        self.assertNotIn('SyncEventLog', local_reset)
        self.assertNotIn('_syncErrorCount', local_reset)

    def test_protection_discovery_and_session_admission_still_precede_selected_callbacks(self):
        body = method(self.update, 'private void UpdateWorldSync()')
        ordered = ('WatchLevelChanges();', 'GuestSaveGuard.ProtectWorld', '_vehicles.PrepareGuestDamageIsolation();',
                   '_vehicles.PrepareGuestEngineProtection();', 'var session = SessionManager.Instance;',
                   'if (!sessionActive)', 'if (!IsGameLevel())', '_items.ProcessBags(session!);',
                   'UpdateWorldDiscovery();', '_train.Update(session!);', '_vehicles.UpdateVehicleStates(session!);',
                   '_clothing.Update(session!);', '_carRadio.Update(session!);')
        positions = [body.index(text) for text in ordered]
        self.assertEqual(sorted(positions), positions)
        self.assertIn('if (_wasSessionActive) ReleaseEverything();', body)


if __name__ == '__main__':
    unittest.main()
