using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

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
                if (part.LastSyncedState == null) continue;

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

        internal IEnumerable<WorldBoltSnapshot> BuildBoltSnapshotChunks()
        {
            var bolts = new WorldBoltSnapshot();
            foreach (var pair in _bolts)
            {
                ReadBoltVars(pair.Value, out ushort tightness, out ushort screwInt);
                if (tightness == 0 && screwInt == 0) continue;

                bolts.Entries.Add(new WorldBoltSnapshot.Entry
                {
                    NetId = pair.Key,
                    BoltTightness = tightness,
                    ScrewInt = screwInt,
                });
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
                if (!ShouldIncludePartSnapshot(pair.Value, out byte flags, out byte tightness, out byte wear))
                    continue;

                parts.Entries.Add(new WorldPartSnapshot.Entry
                {
                    NetId = pair.Key,
                    Flags = flags,
                    Tightness = tightness,
                    Wear = wear,
                });
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

        public void OnRemoteBoltSnapshot(WorldBoltSnapshot message)
        {
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                if (ApplyBoltState(entry.NetId, entry.BoltTightness, entry.ScrewInt))
                    applied++;
                else
                {
                    _pendingBoltStates[entry.NetId] = new PendingBoltState
                    {
                        BoltTightness = entry.BoltTightness,
                        ScrewInt = entry.ScrewInt,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        public void OnRemoteBoltState(BoltState message)
        {
            if (!ApplyBoltState(message.NetId, message.BoltTightness, message.ScrewInt))
                QueuePendingBolt(message.NetId, message.BoltTightness, message.ScrewInt);
        }

        public void OnRemotePartState(PartState message)
        {
            if (!ApplyPartState(message.NetId, message.Flags, message.Tightness, message.Wear))
                QueuePendingPart(message.NetId, message.Flags, message.Tightness, message.Wear);
        }

        public void OnRemotePartSnapshot(WorldPartSnapshot message)
        {
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                if (ApplyPartState(entry.NetId, entry.Flags, entry.Tightness, entry.Wear))
                    applied++;
                else
                {
                    _pendingPartStates[entry.NetId] = new PendingPartState
                    {
                        Flags = entry.Flags,
                        Tightness = entry.Tightness,
                        Wear = entry.Wear,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: part snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        private void QueuePendingPart(uint netId, byte flags, byte tightness, byte wear)
        {
            _pendingPartStates[netId] = new PendingPartState
            {
                Flags = flags,
                Tightness = tightness,
                Wear = wear,
                ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
            };
        }

        private bool ApplyPartState(uint netId, byte flags, byte tightness, byte wear)
        {
            if (!_parts.TryGetValue(netId, out var part) || part.Fsm == null) return false;
            if (!part.Fsm.gameObject.activeInHierarchy || !part.Fsm.enabled) return false;

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: part {netId:X8} -> installed={((flags & PartState.FlagInstalled) != 0)} tightness={tightness} wear={wear} (remote).");
            _bridge.ApplyingRemote = true;
            try
            {
                if (part.InstalledVar != null)
                    part.InstalledVar.Value = (flags & PartState.FlagInstalled) != 0;
                if (part.TightnessVar != null)
                    part.TightnessVar.Value = DecodeUnitFloat(tightness);
                if (part.WearVar != null)
                    part.WearVar.Value = DecodeUnitFloat(wear);
            }
            finally
            {
                _bridge.ApplyingRemote = false;
            }

            return true;
        }

        private void QueuePendingBolt(uint netId, ushort tightness, ushort screwInt)
        {
            _pendingBoltStates[netId] = new PendingBoltState
            {
                BoltTightness = tightness,
                ScrewInt = screwInt,
                ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
            };
        }

        private bool ApplyBoltState(uint netId, ushort tightness, ushort screwInt)
        {
            if (!_bolts.TryGetValue(netId, out var bolt) || bolt.Fsm == null) return false;
            if (!bolt.Fsm.gameObject.activeInHierarchy || !bolt.Fsm.enabled) return false;

            // Already at this tightness — skip the var writes and the "Set pos" re-entry, which would
            // replay the wrench animation/sound and churn the FSM on every redundant snapshot/resync.
            ReadBoltVars(bolt, out ushort curTightness, out ushort curScrew);
            if (curTightness == tightness && curScrew == screwInt)
                return true;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} -> tightness={tightness} screw={screwInt} (remote).");
            _bridge.ApplyingRemote = true;
            try
            {
                if (bolt.BoltTightnessVar != null) bolt.BoltTightnessVar.Value = tightness;
                if (bolt.ScrewIntVar != null) bolt.ScrewIntVar.Value = screwInt;
                if (bolt.TightnessFVar != null) bolt.TightnessFVar.Value = tightness;
                if (bolt.ScrewFloatVar != null) bolt.ScrewFloatVar.Value = screwInt;

                if (FsmHook.EnsureRemoteEntry(bolt.Fsm, "Set pos"))
                    FsmHook.FireRemoteEntry(bolt.Fsm, "Set pos");
            }
            finally
            {
                _bridge.ApplyingRemote = false;
            }

            return true;
        }


    }
}
