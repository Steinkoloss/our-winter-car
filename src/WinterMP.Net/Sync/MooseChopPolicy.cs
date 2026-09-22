using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class MooseChopPolicy
    {
        public static bool Valid(MooseChopIntent r) => r != null && r.PlayerId != 255 && r.Corpse != 0 && r.Section < 2 && r.ExpectedPieces < 4;
        public static bool CanChop(MooseChopIntent r, uint corpse, int pieces, bool dead, bool ready, bool freshAlive, float distanceSquared)
            => Valid(r) && r.Corpse == corpse && dead && ready && freshAlive && Finite(distanceSquared)
                && distanceSquared >= 0 && distanceSquared <= 16 && pieces == r.ExpectedPieces;
        public static bool Valid(MooseCorpseState s)
        {
            if (s == null || s.Corpse == 0 || s.FrontPieces > 4 || s.RearPieces > 4 || s.Positions == null || s.Rotations == null
                || s.Positions.Length != (s.Dead ? MooseCorpseState.BodyCount : 0) || s.Rotations.Length != s.Positions.Length
                || (!s.Dead && (s.FrontPieces != 0 || s.RearPieces != 0))) return false;
            for (int i = 0; i < s.Positions.Length; i++)
            {
                var p = s.Positions[i]; var q = s.Rotations[i];
                double norm = (double)q.X*q.X + (double)q.Y*q.Y + (double)q.Z*q.Z + (double)q.W*q.W;
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(q.X) || !Finite(q.Y) || !Finite(q.Z) || !Finite(q.W)
                    || norm < .9 || norm > 1.1) return false;
            }
            return true;
        }
        public static bool CanReceive(MooseCorpseState? old, MooseCorpseState next)
        {
            if (!Valid(next)) return false;
            if (old == null) return true;
            if (old.Corpse != next.Corpse) return Newer(next.Corpse, old.Corpse);
            return Newer(next.Revision, old.Revision) && (!old.Dead || next.Dead)
                && next.FrontPieces >= old.FrontPieces && next.RearPieces >= old.RearPieces;
        }
        private static bool Newer(uint a, uint b) { uint d = unchecked(a - b); return d != 0 && d < 0x80000000u; }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
