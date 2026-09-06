using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class PartSlotPolicy
    {
        public const int MaxSlots = 32;

        public static bool ValidRequest(PartFitOperation operation, byte slot) => (byte)operation <= 3
            && slot <= MaxSlots && (operation == PartFitOperation.Install || slot == 0);

        /// <summary>Native arrays reserve index zero and break distance ties with the later slot.</summary>
        public static byte Nearest(NetVector3 part, NetVector3?[] points, float tolerance)
        {
            if (points == null || points.Length < 2 || points.Length > MaxSlots + 1 || points[0].HasValue
                || !Finite(part)) return 0;
            float nearest = float.PositiveInfinity;
            byte index = 0;
            for (int i = 1; i < points.Length; i++)
            {
                var candidate = points[i];
                if (!candidate.HasValue) continue;
                var point = candidate.Value;
                if (!Finite(point)) return 0;
                float x = part.X - point.X, y = part.Y - point.Y, z = part.Z - point.Z;
                float distance = x * x + y * y + z * z;
                if (distance <= nearest) { nearest = distance; index = (byte)i; }
            }
            return index != 0 && PartFitLedger.WithinMount(part, points[index]!.Value, tolerance) ? index : (byte)0;
        }

        private static bool Finite(NetVector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
