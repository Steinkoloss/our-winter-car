namespace WinterMP.Net.Sync
{
    /// <summary>Additional pose checks after the host resolves an accepted taxi driver claim.</summary>
    public static class TaxiPickupPolicy
    {
        public static bool CanUseDriver(bool dead, float now, float lastPoseAt, float seatDistanceSquared)
        {
            return !dead && Finite(now) && Finite(lastPoseAt) && lastPoseAt > 0
                && now >= lastPoseAt && now - lastPoseAt <= 2f
                && Finite(seatDistanceSquared) && seatDistanceSquared >= 0 && seatDistanceSquared <= 64f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
