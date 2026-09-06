using System;
using System.Collections.Generic;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private void RetainIsolatedPartView(PlayMakerFSM fsm)
        {
            var states = SyncCatalog.TryMatchPart(fsm);
            var root = NativePartIdentity.FindData(fsm.transform);
            if (states == null || root == null || !_bridge.PartIdentities.TryRootId(root, out uint rootId)
                || !_bridge.PartIdentities.TryFsmId(fsm, ScenePath.Of(fsm.transform), out uint id)) return;
            if (_parts.TryGetValue(id, out var old) && old.Fsm != fsm)
                throw new InvalidOperationException("Isolated part view has an ambiguous identity.");
            _parts[id] = new SyncedPart { Fsm = fsm, Path = ScenePath.Of(fsm.transform), SyncedStates = states,
                Replica = true, ReplicaRootId = rootId };
        }

        internal void PrepareReplacementPartView(PlayMakerFSM data)
        {
            if (!_bridge.PartIdentities.IsReplica(data) || !_bridge.PartIdentities.TryRootId(data, out uint rootId)) return;
            var states = SyncCatalog.TryMatchPart(data);
            if (states == null || !PartIdentity.TryFsmId(rootId, string.Empty, data.FsmName, out uint id)) return;
            if (_doors.ContainsKey(id) || _bolts.ContainsKey(id)) throw new InvalidOperationException("Replacement part view identity collision.");
            if (_parts.TryGetValue(id, out var previous) && !previous.Replica)
                throw new InvalidOperationException("Replacement part view overlaps a live native part.");
            _parts[id] = new SyncedPart { Fsm = data, Path = ScenePath.Of(data.transform), SyncedStates = states,
                Replica = true, ReplicaCopy = true, ReplicaRootId = rootId, ReplicaState = previous?.ReplicaState,
                InstalledVar = data.FsmVariables.FindFsmBool("Installed"),
                TightnessVar = data.FsmVariables.FindFsmFloat("Tightness"), WearVar = data.FsmVariables.FindFsmFloat("Wear") };
            MarkRegistered(data);
            _bridge.RequestObjectState(id);
        }

        private PartState? ReadReplicaPart(uint id, SyncedPart part)
        {
            var state = part.ReplicaState;
            if (state == null) return null;
            float tightness = state.Tightness, wear = state.Wear;
            if (part.ReplicaCopy && part.Fsm != null && _bridge.PartIdentities.TryRootId(part.Fsm, out uint rootId))
            {
                tightness = _partTightnessReceipts.Latest(rootId, tightness);
                if (part.WearVar != null) wear = part.WearVar.Value;
            }
            return PartStatePolicy.Capture(id, (state.Flags & PartState.FlagInstalled) != 0, tightness, wear);
        }

        private bool ApplyReplicaPartState(uint id, SyncedPart part, byte flags, float tightness, float wear, ulong order)
        {
            // Keep scratch Installed and generic PartState checksums separate
            // from the revisioned attachment/scalar stream. Never touch saved Data.
            if (part.ReplicaCopy) tightness = ResolvePartTightness(part.Fsm, order, tightness);
            part.ReplicaState = PartStatePolicy.Capture(id, (flags & PartState.FlagInstalled) != 0, tightness, wear);
            _pendingPartStates.Remove(id);
            return true;
        }

        internal void RetireReplacementPartViews(uint rootId)
        {
            foreach (uint id in new List<uint>(_parts.Keys))
                if (_parts[id].Replica && _parts[id].ReplicaRootId == rootId)
                { _parts.Remove(id); _pendingPartStates.Remove(id); }
        }

        private void ProcessReplicaPartViews()
        {
            foreach (var pair in _parts)
                if (pair.Value.Replica && pair.Value.ReplicaState == null) _bridge.RequestObjectState(pair.Key);
        }
    }
}
