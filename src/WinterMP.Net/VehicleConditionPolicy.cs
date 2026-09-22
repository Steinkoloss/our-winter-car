using System;

namespace WinterMP.Net
{
    public static class VehicleConditionPolicy
    {
        public static byte EncodeTirePressure(float bar)
            => EncodeNativeTirePressure(bar * 100f);

        /// <summary>Native TirePressure.Data uses kPa (default 190), one per wire unit.</summary>
        public static byte EncodeNativeTirePressure(float kilopascals)
        {
            if (float.IsNaN(kilopascals) || kilopascals <= 0f) return 0;
            if (kilopascals >= byte.MaxValue) return byte.MaxValue;
            // Truncation loses a unit for some decoded n/100 floats, causing
            // permanent checksum mismatches and pressure loss on each handoff.
            return (byte)Math.Floor(kilopascals + 0.5f);
        }

        public static float DecodeTirePressure(byte pressure) => pressure / 100f;

        public static float DecodeNativeTirePressure(byte pressure) => pressure;
    }
}
