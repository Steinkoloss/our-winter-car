using System;
using System.Globalization;

namespace WinterMP.Net.Sync
{
    /// <summary>Engine-independent codec for the host's historical CSV guest sidecar.</summary>
    public sealed class GuestProfile
    {
        public struct NeedsSnapshot
        {
            public float Hunger, Fatigue, Thirst, Urine, BodyTemp, Stress, Drunk, Dirtiness, PlayerAlco;
            public bool HasBodyTemp, HasDirtiness, HasAlco, Valid;

            public bool HasFiniteValues => Finite(Hunger) && Finite(Fatigue) && Finite(Thirst)
                && Finite(Urine) && PlayerWarmthPolicy.Valid(HasBodyTemp, BodyTemp)
                && Finite(Stress) && Finite(Drunk) && (!HasDirtiness || Finite(Dirtiness))
                && (!HasAlco || Finite(PlayerAlco));
        }

        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public NeedsSnapshot Needs;
        public const string Columns = "# steamId,x,y,z,qx,qy,qz,qw,hunger,fatigue,thirst,urine,retiredAirTemp,stress,drunk,dirtiness,playeralco,playerTemp";

        public static bool TryParse(string line, out ulong steamId, out GuestProfile profile)
        {
            steamId = 0; profile = new GuestProfile();
            if (string.IsNullOrEmpty(line) || line.TrimStart().StartsWith("#")) return false;
            string[] parts = line.Split(',');
            if (parts.Length < 8 || !ulong.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
                return false;
            profile.Position = new NetVector3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
            profile.Rotation = new NetQuaternion(Parse(parts[4]), Parse(parts[5]), Parse(parts[6]), Parse(parts[7]));
            if (parts.Length < 12) return true;
            float dirtiness = 0, alco = 0, warmth = 0;
            bool hasDirtiness = parts.Length >= 16 && TryFinite(parts[15], out dirtiness);
            bool hasAlco = parts.Length >= 17 && TryFinite(parts[16], out alco);
            // Column 12 held ambient Temperature through v188, even when it
            // looked like a plausible body temperature. Never migrate its value.
            bool hasWarmth = parts.Length >= 18 && TryFinite(parts[17], out warmth);
            profile.Needs = new NeedsSnapshot {
                Hunger = Parse(parts[8]), Fatigue = Parse(parts[9]), Thirst = Parse(parts[10]), Urine = Parse(parts[11]),
                BodyTemp = hasWarmth ? warmth : 0, HasBodyTemp = hasWarmth,
                Stress = parts.Length >= 14 ? Parse(parts[13]) : 0,
                Drunk = parts.Length >= 15 ? Parse(parts[14]) : 0,
                Dirtiness = hasDirtiness ? dirtiness : 0, HasDirtiness = hasDirtiness,
                PlayerAlco = hasAlco ? alco : 0, HasAlco = hasAlco, Valid = true };
            if (!profile.Needs.HasFiniteValues) profile.Needs.Valid = false;
            return true;
        }

        public string Serialize(ulong steamId)
        {
            string pose = string.Format(CultureInfo.InvariantCulture,
                "{0},{1:0.####},{2:0.####},{3:0.####},{4:0.####},{5:0.####},{6:0.####},{7:0.####}",
                steamId, Position.X, Position.Y, Position.Z, Rotation.X, Rotation.Y, Rotation.Z, Rotation.W);
            if (!Needs.Valid) return pose;
            if (!Needs.HasFiniteValues) throw new ArgumentException("Invalid guest needs.");
            // Keep column positions stable even when optional globals arrive
            // independently. Older readers see absent warmth as their legacy zero.
            return pose + string.Format(CultureInfo.InvariantCulture,
                ",{0:0.####},{1:0.####},{2:0.####},{3:0.####},0,{4:0.####},{5:0.####},{6},{7},{8}",
                Needs.Hunger, Needs.Fatigue, Needs.Thirst, Needs.Urine, Needs.Stress, Needs.Drunk,
                Optional(Needs.HasDirtiness, Needs.Dirtiness), Optional(Needs.HasAlco, Needs.PlayerAlco),
                Optional(Needs.HasBodyTemp, Needs.BodyTemp));
        }

        private static string Optional(bool available, float value) => available
            ? value.ToString("R", CultureInfo.InvariantCulture) : "";
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Parse(string text)
        {
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            return value;
        }
        private static bool TryFinite(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && Finite(value);
    }
}
