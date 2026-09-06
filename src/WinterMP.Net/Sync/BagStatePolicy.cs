using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class BagStatePolicy
    {
        public static bool IsValidState(BagState? state)
        {
            if (state == null || state.ItemId == 0 || state.FactoryId == 0 || string.IsNullOrEmpty(state.NativeId)
                || state.NativeId.Length > 128 || FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId) != state.ItemId
                || !Finite(state.Condition) || state.Condition < 0 || state.Condition > 100) return false;
            var p = state.Position; var q = state.Rotation;
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(q.X) || !Finite(q.Y)
                || !Finite(q.Z) || !Finite(q.W)) return false;
            double norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
            return norm >= .9 && norm <= 1.1;
        }

        public static BagState Copy(BagState s) => new BagState {
            ItemId = s.ItemId, FactoryId = s.FactoryId, NativeId = s.NativeId, Revision = s.Revision,
            Remaining = s.Remaining, Condition = s.Condition, Position = s.Position, Rotation = s.Rotation };
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>A targeted snapshot must not consume the next shared bag update.</summary>
    public sealed class BagPublication
    {
        private bool _observed, _sent;
        private ushort _remaining;
        private float _condition;
        private uint _revision, _sentRevision;
        public uint Observe(ushort remaining, float condition)
        {
            if (float.IsNaN(condition) || float.IsInfinity(condition) || condition < 0 || condition > 100)
                throw new ArgumentOutOfRangeException("condition");
            if (!_observed || remaining != _remaining || condition != _condition)
            {
                _revision = unchecked(_revision + 1); _remaining = remaining; _condition = condition; _observed = true;
            }
            return _revision;
        }
        public bool NeedsBroadcast => _observed && (!_sent || _sentRevision != _revision);
        public void MarkBroadcast(uint revision) { _sentRevision = revision; _sent = true; }
    }
}
