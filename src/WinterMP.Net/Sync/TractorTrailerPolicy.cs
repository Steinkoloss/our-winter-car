using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class TractorTrailerPolicy
    {
        public static bool Newer(uint next, uint previous) => next != previous && unchecked((int)(next - previous)) > 0;
        public static bool Valid(TractorTrailerState s) => s != null && s.Revision != 0 && s.Owner != 255 && (s.Attached || s.Owner == 0) && Bound(s.ConnectedAnchor, 100) && Valid(s.Bodies);
        public static bool Valid(TractorTrailerIntent i) => i != null && i.PlayerId != 0 && i.PlayerId != 255 && i.Revision != 0 && i.Sequence != 0;
        public static bool Valid(TractorTrailerMotion m) => m != null && m.Revision != 0 && m.Sequence != 0 && m.Owner != 255 && Valid(m.Bodies);
        public static bool Valid(TrailerBodyPose[] bodies)
        {
            if (bodies == null || bodies.Length != 3) return false;
            foreach (var b in bodies)
            {
                if (b == null || !Bound(b.Position, 100000) || !Bound(b.Velocity, 250) || !Bound(b.AngularVelocity, 100)) return false;
                var q = b.Rotation; float n = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
                if (!(n >= .9f && n <= 1.1f)) return false;
            }
            return DistanceSquared(bodies[0].Position, bodies[1].Position) <= 100;
        }
        private static bool Bound(NetVector3 v, float bound) => Math.Abs(v.X) <= bound && Math.Abs(v.Y) <= bound && Math.Abs(v.Z) <= bound;
        private static float DistanceSquared(NetVector3 a, NetVector3 b) { float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return x * x + y * y + z * z; }
        public static bool CanRelease(TractorTrailerState s, TractorTrailerIntent i, byte actor, uint previous, bool fresh, float distanceSquared)
            => Valid(s) && Valid(i) && actor == i.PlayerId && Newer(i.Sequence, previous) && i.Revision == s.Revision && s.Attached && fresh && distanceSquared >= 0 && distanceSquared <= 9;
        public static bool CanMove(TractorTrailerState s, TractorTrailerMotion m, byte actor, uint previous, bool tractorOwner, float hitchDistanceSquared)
            => Valid(s) && Valid(m) && s.Attached && s.Owner != 0 && m.Owner == actor && actor == s.Owner && m.Revision == s.Revision
                && Newer(m.Sequence, previous) && tractorOwner && hitchDistanceSquared >= 0 && hitchDistanceSquared <= 9;
    }
}
