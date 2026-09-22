using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class MooseMeatPolicy
    {
        public static bool Valid(MooseMeatState s)
        {
            if (s == null || s.FactoryId == 0 || string.IsNullOrEmpty(s.NativeId) || s.NativeId.Length > 128
                || s.Condition < 0 || s.Condition > 100 || !Finite(s.Condition) || s.Kind > 4) return false;
            foreach (char c in s.NativeId) if (char.IsControl(c) || char.IsWhiteSpace(c)) return false;
            var p = s.Position; var q = s.Rotation;
            double norm = (double)q.X*q.X + (double)q.Y*q.Y + (double)q.Z*q.Z + (double)q.W*q.W;
            return Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(q.X) && Finite(q.Y) && Finite(q.Z) && Finite(q.W)
                && norm >= .9 && norm <= 1.1;
        }
        public static bool SameFood(MooseMeatState a, MooseMeatState b) =>
            a.FactoryId == b.FactoryId && a.NativeId == b.NativeId && a.Kind == b.Kind && a.Condition == b.Condition;
        public static bool CanReceive(MooseMeatState? old, MooseMeatState next)
        {
            if (!Valid(next)) return false;
            if (old == null) return true;
            if (old.FactoryId != next.FactoryId || old.NativeId != next.NativeId) return false;
            uint delta = unchecked(next.Revision - old.Revision);
            // Equal revisions can refresh the creation pose after movement/resync.
            return delta == 0 ? SameFood(old, next) : delta < 0x80000000u;
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
