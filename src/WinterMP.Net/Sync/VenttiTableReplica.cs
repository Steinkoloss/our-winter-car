using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Keeps the latest complete table observation until the guest's bindings are ready.</summary>
    public sealed class VenttiTableReplica
    {
        public VenttiTableState? Current { get; private set; }

        public static bool IsValid(VenttiTableState state) =>
            !float.IsNaN(state.Stake) && !float.IsInfinity(state.Stake) && state.Stake >= 0f
            && state.PlayerTotal >= 0 && state.HouseTotal >= 0 && state.Outcome <= VenttiTableState.LoseHouse;

        public static bool SameTable(VenttiTableState left, VenttiTableState right) =>
            left.TableId == right.TableId && left.Stake == right.Stake
            && left.PlayerTotal == right.PlayerTotal && left.HouseTotal == right.HouseTotal
            && left.Outcome == right.Outcome;

        public bool Receive(uint expectedTableId, VenttiTableState state)
        {
            if (state.TableId != expectedTableId || !IsValid(state)) return false;
            var previous = Current;
            if (previous != null)
            {
                uint difference = unchecked(state.Sequence - previous.Sequence);
                if (difference == 0 || difference > int.MaxValue) return false;
            }
            Current = new VenttiTableState
            {
                TableId = state.TableId, Sequence = state.Sequence, Stake = state.Stake,
                PlayerTotal = state.PlayerTotal, HouseTotal = state.HouseTotal, Outcome = state.Outcome,
            };
            return true;
        }

        public void Clear() { Current = null; }
    }
}
