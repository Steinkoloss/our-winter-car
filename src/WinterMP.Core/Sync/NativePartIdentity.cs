using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed class NativePartIdentity
    {
        private readonly Dictionary<uint, PlayMakerFSM> _owners = new Dictionary<uint, PlayMakerFSM>();
        private readonly Dictionary<PlayMakerFSM, uint> _ids = new Dictionary<PlayMakerFSM, uint>();
        private readonly HashSet<PlayMakerFSM> _warnings = new HashSet<PlayMakerFSM>();
        private readonly HashSet<PlayMakerFSM> _replicas = new HashSet<PlayMakerFSM>();
        private readonly HashSet<PlayMakerFSM> _isolated = new HashSet<PlayMakerFSM>();

        internal static bool IsData(PlayMakerFSM fsm)
        {
            var c = SyncCatalog.PartIdentity;
            if (c == null || fsm == null || fsm.FsmName != c["fsm"]) return false;
            var vars = fsm.FsmVariables;
            return vars.FindFsmString(c["idVariable"]) != null && vars.FindFsmInt(c["assemblyVariable"]) != null
                && vars.FindFsmBool(c["consumedVariable"]) != null;
        }

        internal static NativePartPhase Phase(PlayMakerFSM? data)
        {
            if (data == null) return NativePartPhase.Retired;
            var c = SyncCatalog.PartIdentity;
            if (c == null || !IsData(data)) return NativePartPhase.Unavailable;
            return PartStatePolicy.NativePhase(true, data.FsmVariables.FindFsmInt(c["assemblyVariable"]).Value,
                data.FsmVariables.FindFsmBool(c["consumedVariable"]).Value, data.GetComponent<Rigidbody>() != null);
        }

        internal static PlayMakerFSM? FindData(Transform node)
        {
            var c = SyncCatalog.PartIdentity;
            if (c == null) return null;
            for (var current = node; current != null; current = current.parent)
            {
                var body = current.GetComponent<Rigidbody>();
                if (body != null && SyncCatalog.IsVehicleRoot(body)) return null;
                foreach (var fsm in current.GetComponents<PlayMakerFSM>())
                {
                    if (IsData(fsm)) return fsm;
                }
            }
            return null;
        }

        internal bool TryRootId(PlayMakerFSM data, out uint id)
        {
            id = 0;
            var c = SyncCatalog.PartIdentity;
            if (c == null || data == null || _isolated.Contains(data) || !data.Fsm.Initialized || !data.Fsm.Started
                || Array.IndexOf(c.InitializingStates, data.ActiveStateName) >= 0) return false;
            var vars = data.FsmVariables;
            string nativeId = vars.FindFsmString(c["idVariable"]).Value;
            if (string.IsNullOrEmpty(nativeId)) return false;
            if (!PartIdentity.TryPersistentId(nativeId,
                vars.FindFsmString(c["assemblyKeyVariable"])?.Value ?? string.Empty,
                vars.FindFsmString(c["positionKeyVariable"])?.Value ?? string.Empty,
                c["assemblyKeySuffix"], c["positionKeySuffix"], out id))
            {
                Warn(data, "Missing or inconsistent native save identity: " + nativeId);
                return false;
            }
            if ((_ids.TryGetValue(data, out uint previous) && previous != id)
                || (_owners.TryGetValue(id, out var owner) && owner != null && owner != data))
            {
                Warn(data, "Duplicate or changed native part identity: " + nativeId);
                return false;
            }
            _ids[data] = id; _owners[id] = data;
            return true;
        }

        internal bool TryFsmId(PlayMakerFSM fsm, string path, out uint id)
        {
            var data = FindData(fsm.transform);
            if (data == null) { id = StableHash.Fnv1a32(path + "::" + fsm.FsmName); return true; }
            id = 0;
            if (_replicas.Contains(data)) return false;
            if (!TryRootId(data, out uint itemId)) return false;
            var relative = ScenePath.RelativeTo(fsm.transform, data.transform);
            if (relative != null && PartIdentity.TryFsmId(itemId, relative, fsm.FsmName, out id)) return true;
            Warn(data, "Unusable child FSM identity: " + path + "::" + fsm.FsmName);
            return false;
        }

        private void Warn(PlayMakerFSM data, string reason)
        {
            if (!_warnings.Add(data)) return;
            WinterMPPlugin.Log.LogWarning("WorldSync: part identity unavailable: " + reason);
            SyncEventLog.Record("part-identity", reason);
        }

        internal void MarkReplica(PlayMakerFSM data) => _replicas.Add(data);
        internal bool IsReplica(PlayMakerFSM data) => _replicas.Contains(data);
        internal void MarkIsolated(PlayMakerFSM data) { Forget(data); _isolated.Add(data); }
        internal void UnmarkIsolated(PlayMakerFSM data) => _isolated.Remove(data);
        internal void Forget(PlayMakerFSM? data)
        {
            if (data is null) return;
            if (_ids.TryGetValue(data, out uint id) && _owners.TryGetValue(id, out var owner) && owner == data) _owners.Remove(id);
            _ids.Remove(data); _warnings.Remove(data); _replicas.Remove(data);
        }
        internal void Clear() { _owners.Clear(); _ids.Clear(); _warnings.Clear(); _replicas.Clear(); _isolated.Clear(); }
    }
}
