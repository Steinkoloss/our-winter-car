using System;

namespace WinterMP.Net
{
    public static class VehicleConditionPolicy
    {
        public static byte EncodeTirePressure(float bar)
        {
            if (float.IsNaN(bar) || bar <= 0f) return 0;
            if (bar >= 2.55f) return byte.MaxValue;
            // Truncation loses a unit for some decoded n/100 floats, causing
            // permanent checksum mismatches and pressure loss on each handoff.
            return (byte)Math.Floor(bar * 100f + 0.5f);
        }

        public static float DecodeTirePressure(byte pressure) => pressure / 100f;
    }
}
