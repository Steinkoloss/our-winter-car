using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class FirewoodLoadPolicy
    {
        public const int MaxPiles = 128;
        private static bool Range(float v, float min, float max) => !float.IsNaN(v) && v >= min && v <= max;
        public static bool Newer(uint next, uint old) { uint d = unchecked(next - old); return d != 0 && d < 0x80000000u; }
        public static bool Valid(FirewoodLoadState s)
        {
            if (s == null || !Range(s.Logs, 0, 1600) || !Range(s.Firewood, 0, 10000000)
                || !Range(s.Mass, 1, 100000) || !Range(s.BedScale, 0, 1) || !Range(s.Unloaded, 0, 1600)
                || s.Piles == null || s.Piles.Length > MaxPiles) return false;
            foreach (var p in s.Piles)
            {
                if (p == null || !Range(p.Scale, 0, 1) || !Range(p.Position.X, -1000000, 1000000)
                    || !Range(p.Position.Y, -1000000, 1000000) || !Range(p.Position.Z, -1000000, 1000000)) return false;
                var q = p.Rotation;
                double norm = (double)q.X*q.X + (double)q.Y*q.Y + (double)q.Z*q.Z + (double)q.W*q.W;
                if (!(norm >= .9 && norm <= 1.1)) return false;
            }
            return true;
        }
        public static bool CanUnload(FirewoodUnloadIntent r, uint epoch, float logs, bool unloading, bool ready, bool freshAlive, float distanceSquared)
            => r != null && r.PlayerId != 255 && r.Epoch == epoch && ready && freshAlive
                && Range(distanceSquared, 0, 144) && r.Unload != unloading && (!r.Unload || Range(logs, 10, 1600));
        public static float SiteSecondary(byte kind, float value) => kind == JobSiteState.KindFirewood ? value : (value < 0 ? 0 : value);
        public static FirewoodLoadState Copy(FirewoodLoadState s)
        {
            var copy = new FirewoodLoadState { Revision = s.Revision, Epoch = s.Epoch, Logs = s.Logs, Firewood = s.Firewood,
                Mass = s.Mass, BedScale = s.BedScale, Unloaded = s.Unloaded, Unloading = s.Unloading, Piles = new FirewoodPile[s.Piles.Length] };
            for (int i = 0; i < copy.Piles.Length; i++) copy.Piles[i] = new FirewoodPile { Position = s.Piles[i].Position, Rotation = s.Piles[i].Rotation, Scale = s.Piles[i].Scale };
            return copy;
        }
        public static bool Same(FirewoodLoadState a, FirewoodLoadState b)
        {
            if (a.Epoch != b.Epoch || a.Logs != b.Logs || a.Firewood != b.Firewood || a.Mass != b.Mass
                || a.BedScale != b.BedScale || a.Unloaded != b.Unloaded || a.Unloading != b.Unloading || a.Piles.Length != b.Piles.Length) return false;
            for (int i = 0; i < a.Piles.Length; i++)
                if (!a.Piles[i].Position.Equals(b.Piles[i].Position) || !a.Piles[i].Rotation.Equals(b.Piles[i].Rotation) || a.Piles[i].Scale != b.Piles[i].Scale) return false;
            return true;
        }
    }
}
