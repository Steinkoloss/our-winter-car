using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class HouseholdFusePolicy
    {
        public static uint ItemId(int holder)
        {
            if (holder < 0 || holder >= 11) throw new ArgumentOutOfRangeException(nameof(holder));
            return StableHash.Fnv1a32("household:fuseholder:" + holder.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        public static bool SameHome(int holder, int slot) => holder >= 0 && holder < 11 && slot >= 0 && slot < 11 && (holder < 7) == (slot < 7);
        public static bool Valid(HouseholdFuseState s)
        {
            if (s == null || s.Revision == 0 || (s.PowerMask & ~2047) != 0 || s.Holders == null || s.Holders.Length != 11) return false;
            int occupied = 0;
            for (int i = 0; i < 11; i++)
            {
                var h = s.Holders[i];
                if (h == null || h.ControlRevision == 0 || h.Fuse > 2 || h.Tightness > 8 || (h.Flags & ~7) != 0
                    || (h.Slot != 255 && !SameHome(i, h.Slot)) || (h.Slot == 255 ? h.Tightness != 0 : h.Tightness == 0) || !Pose(h)) return false;
                if (h.Slot == 255) continue;
                if ((occupied & (1 << h.Slot)) != 0) return false;
                occupied |= 1 << h.Slot;
            }
            return true;
        }
        public static bool Valid(HouseholdFuseIntent i) => i != null && i.PlayerId != 255 && i.Sequence != 0 && i.ControlRevision != 0 && i.Holder < 11
            && (byte)i.Action <= 4 && (i.Action == HouseholdFuseAction.FitHolder ? SameHome(i.Holder, i.Slot) : i.Slot == 255)
            && (i.Action == HouseholdFuseAction.InsertFuse ? i.ItemId != 0 : i.ItemId == 0);
        public static bool CanAct(HouseholdFuseState s, HouseholdFuseIntent i, byte actor, bool near, bool itemOwned, bool busy)
        {
            if (!Valid(s) || !Valid(i) || actor != i.PlayerId || !near || busy) return false;
            var h = s.Holders[i.Holder];
            if (h.ControlRevision != i.ControlRevision) return false;
            switch (i.Action)
            {
                case HouseholdFuseAction.InsertFuse: return h.Slot == 255 && h.Fuse == 0 && (h.Flags & 4) != 0 && itemOwned;
                case HouseholdFuseAction.FitHolder:
                    if (h.Slot != 255 || !itemOwned) return false;
                    foreach (var other in s.Holders) if (other.Slot == i.Slot) return false;
                    return true;
                case HouseholdFuseAction.RemoveHolder: return h.Slot != 255 && h.Tightness <= 1;
                case HouseholdFuseAction.Tighten: return h.Slot != 255 && h.Tightness < 8;
                case HouseholdFuseAction.Loosen: return h.Slot != 255 && h.Tightness > 1;
                default: return false;
            }
        }
        private static bool Pose(HouseholdFuseHolder h)
        {
            var p = h.Position; var q = h.Rotation;
            float n = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            return Math.Abs(p.X) <= 100000 && Math.Abs(p.Y) <= 100000 && Math.Abs(p.Z) <= 100000 && n >= .9f && n <= 1.1f;
        }
    }
}
