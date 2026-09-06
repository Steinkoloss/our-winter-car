using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class PartRemovalPolicy
    {
        public static bool Unbolted(float tightness) => tightness >= 0 && tightness < 1;
        public static bool ValidAllowance(ReplacementPartState state, int index) => state.Installed && state.AssemblyId > 0
            && PartAttachmentPolicy.HasAttachment(state) && state.Scalars != null && index >= 0 && index < state.Scalars.Length
            && Unbolted(state.Scalars[index]);

        public static PartFitStatus Check(PartFitRequest request, ReplacementPartState? state, int tightnessIndex,
            bool available, bool nearby, bool ready)
        {
            if (request.Operation != PartFitOperation.Remove || !available || state == null
                || !PartIdentity.TryItemId(state.NativeId, out uint id) || id != request.ItemId)
                return PartFitStatus.Unavailable;
            if (request.ExpectedRevision != state.Revision) return PartFitStatus.Stale;
            if (!state.Installed || state.AssemblyId <= 0) return PartFitStatus.NotFitted;
            if (!nearby) return PartFitStatus.TooFar;
            if (state.Scalars == null || tightnessIndex < 0 || tightnessIndex >= state.Scalars.Length
                || !PartAttachmentPolicy.HasAttachment(state)) return PartFitStatus.Unavailable;
            if (!Unbolted(state.Scalars[tightnessIndex])) return PartFitStatus.Bolted;
            if (!state.RemovalAllowed) return PartFitStatus.Blocked;
            return ready ? PartFitStatus.Pending : PartFitStatus.Busy;
        }

        /// <summary>The ray's local direction retains scale so t stays in world metres.</summary>
        public static bool RayBox(NetVector3 origin, NetVector3 direction, NetVector3 center, NetVector3 size,
            float range, out float distance)
        {
            distance = 0;
            if (!Finite(range) || range <= 0 || !Finite(origin) || !Finite(direction) || !Finite(center) || !Finite(size)
                || size.X <= 0 || size.Y <= 0 || size.Z <= 0
                || (direction.X == 0 && direction.Y == 0 && direction.Z == 0)) return false;
            double lo = 0, hi = range;
            if (!Slab(origin.X, direction.X, center.X, size.X, ref lo, ref hi)
                || !Slab(origin.Y, direction.Y, center.Y, size.Y, ref lo, ref hi)
                || !Slab(origin.Z, direction.Z, center.Z, size.Z, ref lo, ref hi)) return false;
            // Unity's raycast does not select a collider containing the origin.
            if (lo <= 0) return false;
            distance = (float)lo; return true;
        }
        private static bool Slab(double origin, double direction, double center, double size, ref double lo, ref double hi)
        {
            double min = center - size / 2, max = center + size / 2;
            if (direction == 0) return origin >= min && origin <= max;
            double a = (min - origin) / direction, b = (max - origin) / direction;
            if (a > b) { double swap = a; a = b; b = swap; }
            lo = Math.Max(lo, a); hi = Math.Min(hi, b);
            return lo <= hi;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(NetVector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
