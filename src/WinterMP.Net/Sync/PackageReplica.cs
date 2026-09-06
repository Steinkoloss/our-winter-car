using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class PackageReplica
    {
        private readonly PackageIdentityResolver _identities;
        private readonly ItemSpawnLifecycle _lifecycle;
        private readonly Dictionary<uint, PackageState> _states = new Dictionary<uint, PackageState>();
        public PackageReplica(PackageIdentityResolver identities, ItemSpawnLifecycle lifecycle)
        { _identities = identities; _lifecycle = lifecycle; }

        public bool Receive(PackageState state, out uint itemId)
        {
            itemId = 0;
            if (state == null || !ValidPose(state) || !_identities.TryResolve(state.FactoryId, state.NativeId,
                    state.Quantity, out itemId) || _lifecycle.IsRetired(itemId)) return false;
            if (_states.TryGetValue(itemId, out var previous))
            {
                if (previous.FactoryId != state.FactoryId || previous.NativeId != state.NativeId) return false;
                uint difference = unchecked(state.Revision - previous.Revision);
                if (difference > int.MaxValue || (difference == 0 && previous.Quantity != state.Quantity)) return false;
            }
            // Equal-revision snapshots refresh the creation pose of a missing body.
            _states[itemId] = Copy(state);
            return true;
        }

        public PackageState? Get(uint itemId) => !_lifecycle.IsRetired(itemId)
            && _states.TryGetValue(itemId, out var state) ? Copy(state) : null;
        public void Clear() { _states.Clear(); }
        public static PackageState Copy(PackageState state) => new PackageState
        {
            Revision = state.Revision, FactoryId = state.FactoryId, NativeId = state.NativeId,
            Quantity = state.Quantity, Position = state.Position, Rotation = state.Rotation,
        };
        private static bool ValidPose(PackageState state)
        {
            var p = state.Position; var q = state.Rotation;
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(q.X) || !Finite(q.Y)
                || !Finite(q.Z) || !Finite(q.W)) return false;
            double norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
            return norm >= .9 && norm <= 1.1;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Reading for one joiner must not consume the next broadcast to existing peers.</summary>
    public sealed class PackagePublication
    {
        private bool _observed, _sent;
        private ushort _quantity;
        private uint _revision, _sentRevision;
        public uint Observe(ushort quantity)
        {
            if (!_observed || quantity != _quantity) { _revision++; _quantity = quantity; _observed = true; }
            return _revision;
        }
        public bool NeedsBroadcast => _observed && (!_sent || _sentRevision != _revision);
        public void MarkBroadcast(uint revision) { _sentRevision = revision; _sent = true; }
    }
}
