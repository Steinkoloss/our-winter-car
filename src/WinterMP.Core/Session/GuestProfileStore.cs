using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WinterMP.Net;
using WinterMP.Net.Sync;
using NeedsSnapshot = WinterMP.Net.Sync.GuestProfile.NeedsSnapshot;

namespace WinterMP.Core.Session
{
    /// <summary>
    /// Host-side guest profiles keyed by SteamID, persisted to
    /// <c>wintermp-guests.json</c> beside the vanilla save (PLAN.md §4.6).
    /// </summary>
    internal static class GuestProfileStore
    {
        private static readonly Dictionary<ulong, GuestProfile> Profiles = new Dictionary<ulong, GuestProfile>();
        private static bool _loaded;
        private static string SidecarPath => Path.Combine(Application.persistentDataPath, "wintermp-guests.json");

        public static void Remember(ulong steamId, NetVector3 position, NetQuaternion rotation)
        {
            if (steamId == 0) return;

            EnsureLoaded();
            if (!Profiles.TryGetValue(steamId, out var profile))
                profile = new GuestProfile();

            profile.Position = position;
            profile.Rotation = rotation;
            Profiles[steamId] = profile;
            TrySave();
        }

        public static void RememberNeeds(ulong steamId, NeedsSnapshot needs)
        {
            if (steamId == 0 || !needs.Valid || !needs.HasFiniteValues) return;

            EnsureLoaded();
            if (!Profiles.TryGetValue(steamId, out var profile))
                profile = new GuestProfile();

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
                    if (!GuestProfile.TryParse(line, out ulong steamId, out GuestProfile profile)) continue;

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
            if (GuestSaveGuard.ProtectWorld || SessionManager.Instance == null || !SessionManager.Instance.IsHost) return;
            try
            {
                var lines = new List<string>
                {
                    "# wintermp-guests.json — guest spawn poses + needs (host only; do not edit while hosting)",
                    GuestProfile.Columns,
                };

                foreach (var pair in Profiles)
                {
                    lines.Add(pair.Value.Serialize(pair.Key));
                }

                File.WriteAllLines(SidecarPath, lines.ToArray());
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("GuestProfileStore: could not write sidecar: " + e.Message);
            }
        }

    }
}
