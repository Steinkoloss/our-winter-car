using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using WinterMP.Net;

namespace WinterMP.Core.Session
{
    /// <summary>
    /// Host-side guest profiles keyed by SteamID, persisted to
    /// <c>wintermp-guests.json</c> beside the vanilla save (PLAN.md §4.6).
    /// </summary>
    internal static class GuestProfileStore
    {
        internal struct NeedsSnapshot
        {
            public float Hunger;
            public float Fatigue;
            public float Thirst;
            public float Urine;
            public float BodyTemp;
            public bool Valid;
        }

        private sealed class Profile
        {
            public NetVector3 Position;
            public NetQuaternion Rotation = NetQuaternion.Identity;
            public NeedsSnapshot Needs;
        }

        private static readonly Dictionary<ulong, Profile> Profiles = new Dictionary<ulong, Profile>();
        private static bool _loaded;
        private static string SidecarPath => Path.Combine(Application.persistentDataPath, "wintermp-guests.json");

        public static void Remember(ulong steamId, NetVector3 position, NetQuaternion rotation)
        {
            if (steamId == 0) return;

            EnsureLoaded();
            if (!Profiles.TryGetValue(steamId, out var profile))
                profile = new Profile();

            profile.Position = position;
            profile.Rotation = rotation;
            Profiles[steamId] = profile;
            TrySave();
        }

        public static void RememberNeeds(ulong steamId, NeedsSnapshot needs)
        {
            if (steamId == 0 || !needs.Valid) return;

            EnsureLoaded();
            if (!Profiles.TryGetValue(steamId, out var profile))
                profile = new Profile();

            profile.Needs = needs;
            Profiles[steamId] = profile;
            TrySave();
        }

        public static bool TryGet(ulong steamId, out NetVector3 position, out NetQuaternion rotation)
        {
            EnsureLoaded();
            if (Profiles.TryGetValue(steamId, out var profile))
            {
                position = profile.Position;
                rotation = profile.Rotation;
                return true;
            }

            position = default(NetVector3);
            rotation = NetQuaternion.Identity;
            return false;
        }

        public static bool TryGetNeeds(ulong steamId, out NeedsSnapshot needs)
        {
            EnsureLoaded();
            if (Profiles.TryGetValue(steamId, out var profile) && profile.Needs.Valid)
            {
                needs = profile.Needs;
                return true;
            }

            needs = default(NeedsSnapshot);
            return false;
        }

        public static void Forget(ulong steamId)
        {
            if (steamId == 0) return;

            EnsureLoaded();
            if (!Profiles.Remove(steamId)) return;
            TrySave();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            if (!File.Exists(SidecarPath)) return;

            try
            {
                foreach (string line in File.ReadAllLines(SidecarPath))
                {
                    if (line == null || line.Trim().Length == 0 || line.TrimStart().StartsWith("#"))
                        continue;

                    string[] parts = line.Split(',');
                    if (parts.Length < 8) continue;

                    if (!ulong.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong steamId))
                        continue;

                    var profile = new Profile
                    {
                        Position = new NetVector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])),
                        Rotation = new NetQuaternion(
                            ParseFloat(parts[4]), ParseFloat(parts[5]), ParseFloat(parts[6]), ParseFloat(parts[7])),
                    };

                    if (parts.Length >= 12)
                    {
                        profile.Needs = new NeedsSnapshot
                        {
                            Hunger = ParseFloat(parts[8]),
                            Fatigue = ParseFloat(parts[9]),
                            Thirst = ParseFloat(parts[10]),
                            Urine = ParseFloat(parts[11]),
                            BodyTemp = parts.Length >= 13 ? ParseFloat(parts[12]) : 0f,
                            Valid = true,
                        };
                    }

                    Profiles[steamId] = profile;
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("GuestProfileStore: could not read sidecar: " + e.Message);
            }
        }

        private static void TrySave()
        {
            try
            {
                var lines = new List<string>
                {
                    "# wintermp-guests.json — guest spawn poses + needs (host only; do not edit while hosting)",
                    "# steamId,x,y,z,qx,qy,qz,qw,hunger,fatigue,thirst,urine,bodytemp",
                };

                foreach (var pair in Profiles)
                {
                    var p = pair.Value;
                    if (p.Needs.Valid)
                    {
                        lines.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1:0.####},{2:0.####},{3:0.####},{4:0.####},{5:0.####},{6:0.####},{7:0.####},{8:0.####},{9:0.####},{10:0.####},{11:0.####},{12:0.####}",
                            pair.Key,
                            p.Position.X, p.Position.Y, p.Position.Z,
                            p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W,
                            p.Needs.Hunger, p.Needs.Fatigue, p.Needs.Thirst, p.Needs.Urine, p.Needs.BodyTemp));
                    }
                    else
                    {
                        lines.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1:0.####},{2:0.####},{3:0.####},{4:0.####},{5:0.####},{6:0.####},{7:0.####}",
                            pair.Key,
                            p.Position.X, p.Position.Y, p.Position.Z,
                            p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W));
                    }
                }

                File.WriteAllLines(SidecarPath, lines.ToArray());
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("GuestProfileStore: could not write sidecar: " + e.Message);
            }
        }

        private static float ParseFloat(string text)
        {
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            return value;
        }
    }
}
