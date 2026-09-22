using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public enum PartAdjustmentProfile
    {
        Alternator = 0, Distributor = 1,
    }

    public static class PartAdjustmentPolicy
    {
        public const float MinimumRotation = 0, MaximumRotation = 7, RotationStep = .5f;
        public const float MinimumTiming = 0, MaximumTiming = 20, TimingStep = .2f;
        public static bool IsRotation(PartFitOperation operation) => operation == PartFitOperation.RotateIncrease
            || operation == PartFitOperation.RotateDecrease;

        public static bool TryRotation(float current, PartFitOperation operation, out float result)
            => TryRotation(PartAdjustmentProfile.Alternator, current, operation, out result);

        public static bool ValidRotation(PartAdjustmentProfile profile, float value)
            => Finite(value) && Bounds(profile, out float minimum, out float maximum, out _)
                && value >= minimum && value <= maximum;

        public static bool IsDistributorLoose(float tightness)
            => Finite(tightness) && tightness >= 0 && tightness < 8;

        public static bool TryRotation(PartAdjustmentProfile profile, float current, PartFitOperation operation, out float result)
        {
            result = current;
            if (!IsRotation(operation) || !ValidRotation(profile, current)
                || !Bounds(profile, out float minimum, out float maximum, out float step)) return false;
            result = Math.Max(minimum, Math.Min(maximum,
                current + (operation == PartFitOperation.RotateIncrease ? step : -step)));
            return result != current;
        }

        public static PartFitStatus Check(PartFitRequest request, ReplacementPartState? state, int scalarIndex,
            bool available, bool nearby, bool mountReady, bool boltLoose, bool busy)
            => Check(PartAdjustmentProfile.Alternator, request, state, scalarIndex, available, nearby, mountReady, boltLoose, busy);

        public static PartFitStatus Check(PartAdjustmentProfile profile, PartFitRequest request, ReplacementPartState? state, int scalarIndex,
            bool available, bool nearby, bool mountReady, bool boltLoose, bool busy)
        {
            if (!Bounds(profile, out _, out _, out _) || request == null || !IsRotation(request.Operation)
                || request.SlotIndex != 0 || !available || state == null
                || !PartIdentity.TryItemId(state.NativeId, out uint id) || id != request.ItemId
                || state.Scalars == null || scalarIndex < 0 || scalarIndex >= state.Scalars.Length) return PartFitStatus.Unavailable;
            if (state.Revision != request.ExpectedRevision) return PartFitStatus.Stale;
            if (!PartAttachmentPolicy.HasAttachment(state)) return PartFitStatus.NotFitted;
            if (!nearby) return PartFitStatus.TooFar;
            if (!mountReady) return PartFitStatus.Blocked;
            if (!boltLoose) return PartFitStatus.Bolted;
            if (busy) return PartFitStatus.Busy;
            return TryRotation(profile, state.Scalars[scalarIndex], request.Operation, out _) ? PartFitStatus.Pending : PartFitStatus.Blocked;
        }

        private static bool Bounds(PartAdjustmentProfile profile, out float minimum, out float maximum, out float step)
        {
            minimum = maximum = step = 0;
            switch (profile)
            {
                case PartAdjustmentProfile.Alternator:
                    minimum = MinimumRotation; maximum = MaximumRotation; step = RotationStep; return true;
                case PartAdjustmentProfile.Distributor:
                    minimum = MinimumTiming; maximum = MaximumTiming; step = TimingStep; return true;
                default: return false;
            }
        }

        /// <summary>Distances are in ray parameters, as with RayBox; disabled replica picks stay non-physical.</summary>
        public static bool RaySphere(NetVector3 origin, NetVector3 direction, NetVector3 center, float radius,
            float range, out float distance)
        {
            distance = 0;
            if (!Finite(origin) || !Finite(direction) || !Finite(center) || !Finite(radius) || radius <= 0
                || !Finite(range) || range <= 0) return false;
            double x = (double)origin.X - center.X, y = (double)origin.Y - center.Y, z = (double)origin.Z - center.Z;
            double a = (double)direction.X * direction.X + (double)direction.Y * direction.Y + (double)direction.Z * direction.Z;
            double b = x * direction.X + y * direction.Y + z * direction.Z;
            double c = x * x + y * y + z * z - (double)radius * radius;
            double discriminant = b * b - a * c;
            if (a <= 0 || c <= 0 || b >= 0 || discriminant < 0) return false;
            double hit = (-b - Math.Sqrt(discriminant)) / a;
            if (hit <= 0 || hit > range) return false;
            distance = (float)hit; return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(NetVector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
