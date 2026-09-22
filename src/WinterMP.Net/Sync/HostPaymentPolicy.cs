namespace WinterMP.Net.Sync
{
    public static class HostPaymentPolicy
    {
        public static bool ValidAmount(float amount) => amount > 0 && !float.IsInfinity(amount);
        public static bool CanCollect(bool active, bool enabled, bool claimed, string state, float amount) =>
            active && enabled && !claimed && ValidAmount(amount) && (state == "Wait player" || state == "Wait button");
    }
}
