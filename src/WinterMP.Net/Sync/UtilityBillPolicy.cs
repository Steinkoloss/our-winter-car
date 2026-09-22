using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class UtilityBillPolicy
    {
        public static bool Valid(UtilityBillState state) => state != null && state.Meter <= UtilityBillState.MeterPhone2
            && state.UnpaidBills >= 0 && !float.IsInfinity(state.UnpaidBills)
            && (state.Flags & ~(state.Meter <= UtilityBillState.MeterElectricity2 ? 7 : 3)) == 0
            && (state.Meter < 2 ? state.Phone == null : state.Phone != null && state.Phone.Valid);
        public static bool Same(UtilityBillState a, UtilityBillState b) => a.Meter == b.Meter
            && a.UnpaidBills == b.UnpaidBills && a.Flags == b.Flags && a.Revision == b.Revision
            && (a.Phone == null ? b.Phone == null : a.Phone.Same(b.Phone));
        public static UtilityBillState Copy(UtilityBillState state) => new UtilityBillState {
            Meter = state.Meter, UnpaidBills = state.UnpaidBills, Flags = state.Flags, Revision = state.Revision, Phone = state.Phone?.Copy() };
    }
}
