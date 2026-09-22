using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class SupplyItemReplica
    {
        private readonly Dictionary<uint, string> _factories;
        private readonly ItemSpawnLifecycle _lifecycle;
        private readonly Dictionary<uint, SupplyItemState> _states = new Dictionary<uint, SupplyItemState>();
        public SupplyItemReplica(IDictionary<uint, string> factories, ItemSpawnLifecycle lifecycle)
        {
            _factories = new Dictionary<uint, string>(factories);
            foreach (var pair in _factories)
                if (!FactoryItemIdentity.IsNativeId(pair.Value + "1", pair.Value))
                    throw new ArgumentException("Invalid supply prefix.");
            _lifecycle = lifecycle;
        }
        public bool Receive(SupplyItemState state, out uint id)
        {
            id = 0;
            if (state == null || !_factories.TryGetValue(state.FactoryId, out var prefix)
                || !FactoryItemIdentity.IsNativeId(state.NativeId, prefix) || !ValidPose(state)) return false;
            id = FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId);
            if (_lifecycle.IsRetired(id)) return false;
            if (_states.TryGetValue(id, out var old) && (old.FactoryId != state.FactoryId || old.NativeId != state.NativeId)) return false;
            _states[id] = Copy(state); return true;
        }
        public SupplyItemState? Get(uint id) => !_lifecycle.IsRetired(id) && _states.TryGetValue(id, out var state) ? Copy(state) : null;
        public void Clear() => _states.Clear();
        public static SupplyItemState Copy(SupplyItemState s) => new SupplyItemState {
            FactoryId = s.FactoryId, NativeId = s.NativeId, Position = s.Position, Rotation = s.Rotation };
        private static bool ValidPose(SupplyItemState s)
        {
            var p = s.Position; var q = s.Rotation;
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(q.X) || !Finite(q.Y) || !Finite(q.Z) || !Finite(q.W)) return false;
            double norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
            return norm >= .9 && norm <= 1.1;
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
