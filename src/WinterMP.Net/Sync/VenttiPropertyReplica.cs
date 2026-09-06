using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Retains host access state across late bindings without replaying native transfers.</summary>
    public sealed class VenttiPropertyReplica
    {
        public VenttiPropertyState? Current { get; private set; }

        public static bool IsValid(VenttiPropertyState state) =>
            (state.Keys & ~VenttiPropertyState.AllKeys) == 0
            && (state.KnownAccess & ~VenttiPropertyState.AllAccess) == 0
            && (state.Access & ~state.KnownAccess) == 0;

        public static bool SameProperties(VenttiPropertyState left, VenttiPropertyState right) =>
            left.Keys == right.Keys && left.KnownAccess == right.KnownAccess && left.Access == right.Access;

        public bool Receive(VenttiPropertyState state)
        {
            if (!IsValid(state)) return false;
            var previous = Current;
            if (previous != null)
            {
                uint difference = unchecked(state.Sequence - previous.Sequence);
                if (difference == 0 || difference > int.MaxValue) return false;
            }
            Current = new VenttiPropertyState
            {
                Sequence = state.Sequence, Keys = state.Keys,
                KnownAccess = (byte)(state.KnownAccess | (previous != null ? previous.KnownAccess : 0)),
                Access = (byte)(state.Access | (previous != null ? previous.Access & ~state.KnownAccess : 0)),
            };
            return true;
        }

        public void Clear() => Current = null;
    }
}
