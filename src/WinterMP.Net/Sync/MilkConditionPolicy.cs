using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class MilkConditionPolicy
    {
        public static bool ValidCondition(float value) => value >= 0 && value <= 100;
        public static bool Valid(MilkConditionState state) => state != null && state.NetId != 0
            && ValidCondition(state.Condition) && state.Spoiled <= 1 && (state.Spoiled == 0 || state.Condition <= 1);
        public static bool Same(MilkConditionState a, MilkConditionState b) =>
            a.NetId == b.NetId && a.Condition == b.Condition && a.Spoiled == b.Spoiled;
        public static bool CanReceive(MilkConditionState? previous, MilkConditionState next)
        {
            if (!Valid(next)) return false;
            if (previous == null) return true;
            if (previous.NetId != next.NetId) return false;
            uint difference = unchecked(next.Revision - previous.Revision);
            return difference == 0 ? Same(previous, next) : difference < 0x80000000u;
        }
        public static MilkConditionState Copy(MilkConditionState state) => new MilkConditionState {
            NetId = state.NetId, Revision = state.Revision, Condition = state.Condition, Spoiled = state.Spoiled };
    }
}
