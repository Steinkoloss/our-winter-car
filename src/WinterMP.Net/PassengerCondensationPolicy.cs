using System;

namespace WinterMP.Net
{
    /// <summary>Combine seated occupants while retaining native sweat/300 and .02–.1 rate limits.</summary>
    public static class PassengerCondensationPolicy
    {
        public const float MaximumReportAgeSeconds = 5f;
        public static bool ValidSweat(float sweat) => !float.IsNaN(sweat) && !float.IsInfinity(sweat)
            && sweat >= 0f && sweat <= 100f;

        public static bool Fresh(float now, float receivedAt) => !float.IsNaN(now) && !float.IsInfinity(now)
            && !float.IsNaN(receivedAt) && !float.IsInfinity(receivedAt) && receivedAt > 0
            && now >= receivedAt && now - receivedAt < MaximumReportAgeSeconds;

        // A dry occupant still has the native .02 minimum. Summing in sweat units
        // leaves the original divide/clamp actions in charge of the final rate.
        public static float AddOccupant(float combinedSweat, float sweat)
        {
            if (!ValidSweat(sweat)) return combinedSweat;
            return Math.Min(30f, combinedSweat + Math.Max(6f, Math.Min(30f, sweat)));
        }
    }
}
