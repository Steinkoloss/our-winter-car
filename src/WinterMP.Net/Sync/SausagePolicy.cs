using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class SausagePolicy
    {
        public static uint PackageId(string nativeId)
        {
            if (!FactoryItemIdentity.IsNativeId(nativeId, "sausages")) throw new ArgumentException("Invalid native sausage package ID.");
            return StableHash.Fnv1a32("sausage-package:" + nativeId);
        }
        public static uint ItemId(uint package, int output)
        {
            if (package == 0 || output < 0 || output >= 4) throw new ArgumentOutOfRangeException();
            return StableHash.Fnv1a32("sausage:" + package.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + output);
        }
        public static bool Valid(SausageOpenIntent i) => i != null && i.SourceId != 0 && i.PackageId != 0 && i.Sequence != 0 && i.PlayerId != 0 && i.PlayerId != 255;
        public static bool Valid(SausageState s)
        {
            if (s == null || s.ItemId == 0 || s.Revision == 0 || !(s.Condition >= 0 && s.Condition <= 100) || s.Kind > 3
                || (s.Kind == 0 && s.Grilled) || (s.Kind == 1 && !s.Grilled)) return false;
            var p = s.Position; var q = s.Rotation;
            float n = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            return Math.Abs(p.X) <= 100000 && Math.Abs(p.Y) <= 100000 && Math.Abs(p.Z) <= 100000 && n >= .9f && n <= 1.1f;
        }
        public static bool CanOpen(SausageOpenIntent i, byte actor, uint previous, bool available, bool nearby, bool owned, bool inTrigger)
            => Valid(i) && i.PlayerId == actor && TractorTrailerPolicy.Newer(i.Sequence, previous) && available && nearby && owned && inTrigger;
        public static bool SameFood(SausageState a, SausageState b) => a.Condition == b.Condition && a.Kind == b.Kind && a.Grilled == b.Grilled;
    }
}
