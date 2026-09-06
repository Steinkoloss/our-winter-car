using System;
using System.Collections.Generic;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Sync;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        internal uint ComputeWorldCrc()
        {
            uint crc = StableHash.OffsetBasis;
            MixFsmStates(ref crc, _doors);
            MixFsmStates(ref crc, _ignitions);
            MixFsmStates(ref crc, _controls);
            MixFsmStates(ref crc, _starters);
            MixFsmStates(ref crc, _buys);

            var partIds = new List<uint>(_parts.Keys);
            partIds.Sort();
            foreach (uint id in partIds)
            {
                if (!_parts.TryGetValue(id, out var part)) continue;
                var state = ReadPartState(id, part);
                if (state != null) crc = PartStatePolicy.MixChecksum(crc, state);
            }

            var boltIds = new List<uint>(_bolts.Keys);
            boltIds.Sort();
            foreach (uint id in boltIds)
            {
                var state = BuildBoltState(id);
                if (state != null) crc = WinterMP.Net.Sync.BoltStatePolicy.MixChecksum(crc, state);
            }

            return crc;
        }

        public static void MixFsmStates<T>(ref uint crc, Dictionary<uint, T> entries) where T : class
        {
            var ids = new List<uint>(entries.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                if (!entries.TryGetValue(id, out var entry)) continue;
                if (entry is SyncedControl control && control.ScalarFloat != null)
                {
                    float rotation = control.ScalarFloat.Value;
                    if (float.IsNaN(rotation) || float.IsInfinity(rotation)) continue;
                    crc = StableHash.Combine(crc, id);
                    crc = StableHash.Combine(crc, unchecked((uint)Mathf.RoundToInt(rotation * 1000f)));
                    continue;
                }

                string? state = entry switch
                {
                    SyncedDoor d => d.LastSyncedState,
                    SyncedIgnition i => i.LastSyncedState,
                    SyncedControl c => c.LastSyncedState,
                    SyncedStarter s => s.LastSyncedState,
                    SyncedBuy b => b.LastSyncedState,
                    _ => null,
                };
                if (state == null) continue;
                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, StableHash.Fnv1a32(state));
            }
        }

        internal string? TryGetFsmSnapshotState(uint netId)
        {
            if (_doors.TryGetValue(netId, out var door))
                return door.LastSyncedState ?? TryReadActiveSyncedState(door.Fsm, door.SyncedStates);
            if (_ignitions.TryGetValue(netId, out var ignition))
                return ignition.LastSyncedState ?? TryReadActiveSyncedState(ignition.Fsm, ignition.SyncedStates);
            if (_controls.TryGetValue(netId, out var control))
            {
                if (control.ScalarFloat != null) return null;
                return control.LastSyncedState ?? TryReadActiveSyncedState(control.Fsm, control.SyncedStates);
            }
            if (_starters.TryGetValue(netId, out var starter))
                return starter.LastSyncedState ?? TryReadActiveSyncedState(starter.Fsm, starter.SyncedStates);
            if (_buys.TryGetValue(netId, out var buy))
                return buy.LastSyncedState ?? TryReadActiveSyncedState(buy.Fsm, buy.ResultStates);
            if (_parts.TryGetValue(netId, out var part))
            {
                string? state = part.LastSyncedState ?? TryReadActiveSyncedState(part.Fsm, part.SyncedStates);
                return IsDerivedPartState(state) ? null : state;
            }
            return null;
        }

        private static string? TryReadActiveSyncedState(PlayMakerFSM? fsm, string[] syncedStates)
        {
            if (fsm?.Fsm == null) return null;

            try
            {
                string active = fsm.Fsm.ActiveStateName;
                if (Array.IndexOf(syncedStates, active) >= 0) return active;
            }
            catch
            {
                // FSM not ready.
            }

            return null;
        }


    }
}
