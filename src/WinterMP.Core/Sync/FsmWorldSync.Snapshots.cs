using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        internal IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks()
        {
            var doors = new WorldDoorSnapshot();
            foreach (var pair in _doors)
            {
                var door = pair.Value;
                if (door.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = door.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _ignitions)
            {
                var ignition = pair.Value;
                if (ignition.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = ignition.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _controls)
            {
                var control = pair.Value;
                // Scalar controls have a dedicated snapshot. Replaying their last
                // +/- state would apply a delta to a joining guest's unrelated value.
                if (control.ScalarFloat != null) continue;
                if (control.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = control.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _starters)
            {
                var starter = pair.Value;
                if (starter.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = starter.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _parts)
            {
                var part = pair.Value;
                if (part.LastSyncedState == null || IsDerivedPartState(part.LastSyncedState)) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = part.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _buys)
            {
                var buy = pair.Value;
                if (buy.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = buy.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            if (doors.Entries.Count > 0)
                yield return doors;
        }

        internal IEnumerable<RadiatorThermostatState> BuildRadiatorThermostatStates()
        {
            var ids = new List<uint>(_controls.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                if (TryBuildRadiatorThermostatState(id, out var state))
                    yield return state;
            }
        }

        internal bool TryBuildRadiatorThermostatState(uint netId, out RadiatorThermostatState state)
        {
            state = new RadiatorThermostatState();
            if (!_controls.TryGetValue(netId, out var control)
                || control.ScalarFloat == null
                || string.IsNullOrEmpty(control.ScalarCommitState))
                return false;

            float rotation = control.ScalarFloat.Value;
            if (!IsFinite(rotation)) return false;

            state.NetId = netId;
            state.Rotation = rotation;
            return true;
        }

        internal IEnumerable<WorldBoltSnapshot> BuildBoltSnapshotChunks()
        {
            var bolts = new WorldBoltSnapshot();
            foreach (var pair in _bolts)
            {
                var state = BuildBoltState(pair.Key);
                if (state == null) continue;
                bolts.Entries.Add(BoltStatePolicy.SnapshotEntry(state));
                if (bolts.Entries.Count >= BoltSnapshotChunk)
                {
                    yield return bolts;
                    bolts = new WorldBoltSnapshot();
                }
            }

            if (bolts.Entries.Count > 0)
                yield return bolts;
        }

        internal IEnumerable<WorldPartSnapshot> BuildPartSnapshotChunks()
        {
            var parts = new WorldPartSnapshot();
            foreach (var pair in _parts)
            {
                var state = ReadPartState(pair.Key, pair.Value);
                if (state == null) continue;
                // Zero wear/tightness and an uninstalled flag also clear stale guest state.
                parts.Entries.Add(PartStatePolicy.SnapshotEntry(state));
                if (parts.Entries.Count >= PartSnapshotChunk)
                {
                    yield return parts;
                    parts = new WorldPartSnapshot();
                }
            }

            if (parts.Entries.Count > 0)
                yield return parts;
        }

        public void OnRemoteDoorSnapshot(WorldDoorSnapshot message)
        {
            int applied = 0;
            foreach (var entry in message.Entries)
            {
                // Already in that state (we touched it ourselves, or an earlier
                // chunk) — replaying would re-run animation and sound for nothing.
                if (_doors.TryGetValue(entry.NetId, out var door) && door.LastSyncedState == entry.StateName)
                    continue;
                if (_parts.TryGetValue(entry.NetId, out var part) && part.LastSyncedState == entry.StateName)
                    continue;
                if (_buys.TryGetValue(entry.NetId, out var buy) && buy.LastSyncedState == entry.StateName)
                    continue;
                // Ignition/control/starter entries ride the same snapshot stream. Without these
                // checks a redundant snapshot/resync re-applies them, re-running PrepareRemoteControl/
                // FireRemoteEntry side effects (electrics toggles, heater/hazard replays, FSM re-entry).
                if (_ignitions.TryGetValue(entry.NetId, out var ignition) && ignition.LastSyncedState == entry.StateName)
                    continue;
                if (_controls.TryGetValue(entry.NetId, out var control) && control.LastSyncedState == entry.StateName)
                    continue;
                if (_starters.TryGetValue(entry.NetId, out var starter) && starter.LastSyncedState == entry.StateName)
                    continue;

                applied++;
                if (!TryApplyStateEnter(entry.NetId, entry.StateName))
                    QueuePending(entry.NetId, false, entry.StateName);
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: door snapshot — {message.Entries.Count} entries, {applied} applied/queued.");
        }

        public void OnRemoteRadiatorThermostatState(RadiatorThermostatState message)
        {
            if (!IsFinite(message.Rotation))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: dropped non-finite thermostat state {message.NetId:X8}.");
                return;
            }

            if (!ApplyRadiatorThermostatState(message.NetId, message.Rotation))
                QueuePendingRadiatorThermostatState(message.NetId, message.Rotation);
        }

        private bool ApplyRadiatorThermostatState(uint netId, float rotation)
        {
            string commitState = string.Empty;
            if (!_controls.TryGetValue(netId, out var control)
                || control.Fsm == null
                || control.ScalarFloat == null
                || !control.Fsm.gameObject.activeInHierarchy
                || !control.Fsm.enabled)
                return false;

            commitState = control.ScalarCommitState ?? string.Empty;
            if (commitState.Length == 0) return false;

            if (Mathf.Abs(control.ScalarFloat.Value - rotation) <= 0.0001f)
                return true;

            try
            {
                control.ScalarFloat.Value = rotation;
                _bridge.ApplyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(control.Fsm, commitState);
                }
                finally
                {
                    _bridge.ApplyingRemote = false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: thermostat apply failed for {netId:X8}: {e.Message}");
                return false;
            }
        }

        private void QueuePendingRadiatorThermostatState(uint netId, float rotation)
        {
            _pendingRadiatorThermostatStates[netId] = new PendingRadiatorThermostatState
            {
                Rotation = rotation,
                ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
            };
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void OnRemoteBoltSnapshot(WorldBoltSnapshot message)
        {
            if (!BoltStatePolicy.Valid(message)) return;
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                ulong order = ++_partReceiptOrder;
                if (ApplyBoltState(entry.NetId, entry.BoltTightness, entry.ScrewInt, entry.PartTightness, order))
                {
                    _pendingBoltStates.Remove(entry.NetId);
                    applied++;
                }
                else
                {
                    _pendingBoltStates[entry.NetId] = new PendingBoltState
                    {
                        BoltTightness = entry.BoltTightness,
                        ScrewInt = entry.ScrewInt,
                        PartTightness = entry.PartTightness,
                        ReceiptOrder = order,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        public void OnRemoteBoltState(BoltState message)
        {
            if (!BoltStatePolicy.Valid(message)) return;
            ulong order = ++_partReceiptOrder;
            if (!ApplyBoltState(message.NetId, message.BoltTightness, message.ScrewInt, message.PartTightness, order))
                QueuePendingBolt(message.NetId, message.BoltTightness, message.ScrewInt, message.PartTightness, order);
            else _pendingBoltStates.Remove(message.NetId);
        }

        public void OnRemotePartState(PartState message)
        {
            if (!PartStatePolicy.Valid(message)) return;
            ulong order = ++_partReceiptOrder;
            if (!ApplyPartState(message.NetId, message.Flags, message.TightnessValue, message.WearValue, order))
                QueuePendingPart(message.NetId, message.Flags, message.TightnessValue, message.WearValue, order);
        }

        public void OnRemotePartSnapshot(WorldPartSnapshot message)
        {
            if (!PartStatePolicy.Valid(message)) return;
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                ulong order = ++_partReceiptOrder;
                if (ApplyPartState(entry.NetId, entry.Flags, entry.TightnessValue, entry.WearValue, order))
                    applied++;
                else
                {
                    _pendingPartStates[entry.NetId] = new PendingPartState
                    {
                        Flags = entry.Flags,
                        Tightness = entry.TightnessValue,
                        Wear = entry.WearValue,
                        ReceiptOrder = order,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: part snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        private void QueuePendingPart(uint netId, byte flags, float tightness, float wear, ulong order)
        {
            _pendingPartStates[netId] = new PendingPartState
            {
                Flags = flags,
                Tightness = tightness,
                Wear = wear,
                ReceiptOrder = order,
                ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
            };
        }

        private bool ApplyPartState(uint netId, byte flags, float tightness, float wear, ulong order)
        {
            if (!PartStatePolicy.Valid(flags, tightness, wear)) return false;
            if (!_parts.TryGetValue(netId, out var part) || part.Fsm == null) return false;
            if (!part.Fsm.Fsm.Initialized || !part.Fsm.Fsm.Started
                || !part.Fsm.gameObject.activeInHierarchy || !part.Fsm.enabled) return false;

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: part {netId:X8} -> installed={((flags & PartState.FlagInstalled) != 0)} tightness={tightness} wear={wear} (remote).");
            tightness = ResolvePartTightness(part.Fsm, order, tightness);
            bool applying = _bridge.ApplyingRemote;
            _bridge.ApplyingRemote = true;
            try
            {
                if (part.InstalledVar != null)
                    part.InstalledVar.Value = (flags & PartState.FlagInstalled) != 0;
                if (part.TightnessVar != null)
                    part.TightnessVar.Value = tightness;
                if (part.WearVar != null)
                    part.WearVar.Value = wear;
                if (NativePartIdentity.Phase(part.Fsm) == NativePartPhase.Fitted && HasFsmEvent(part.Fsm, "BOLTING"))
                    part.Fsm.SendEvent("BOLTING");
            }
            finally
            {
                _bridge.ApplyingRemote = applying;
            }

            _pendingPartStates.Remove(netId);
            return true;
        }

        private void QueuePendingBolt(uint netId, ushort tightness, ushort screwInt, float partTightness, ulong order)
        {
            _pendingBoltStates[netId] = new PendingBoltState
            {
                BoltTightness = tightness,
                ScrewInt = screwInt,
                PartTightness = partTightness,
                ReceiptOrder = order,
                ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
            };
        }

    }
}
