#if STEAMWORKS
using System;
using System.Collections.Generic;
using Steamworks;

namespace WinterMP.Core.Steam
{
    /// <summary>Enumerates Steam friends currently playing My Winter Car.</summary>
    internal static class SteamFriendBrowser
    {
        private const uint MwcAppId = 4164420;
        private const string ConnectKey = "connect";

        internal struct FriendEntry
        {
            public ulong SteamId;
            public string Name;
            /// <summary>Non-zero when the friend is hosting a joinable co-op session.</summary>
            public ulong LobbyId;
            public string StatusHint;
        }

        public static void OpenSteamFriendsOverlay()
        {
            if (!SteamBootstrap.EnsureInitialized()) return;

            try
            {
                SteamFriends.ActivateGameOverlay("friends");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"Steam friends overlay failed: {e.Message}");
            }
        }

        public static List<FriendEntry> ListFriendsInMwc()
        {
            var results = new List<FriendEntry>();
            if (!SteamBootstrap.EnsureInitialized()) return results;

            try
            {
                int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < count; i++)
                {
                    CSteamID friendId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    if (!friendId.IsValid()) continue;

                    FriendGameInfo_t gameInfo;
                    if (!SteamFriends.GetFriendGamePlayed(friendId, out gameInfo)) continue;
                    if (gameInfo.m_gameID.AppID().m_AppId != MwcAppId) continue;

                    string name = SteamFriends.GetFriendPersonaName(friendId);
                    ulong lobbyId = 0;
                    string status = "Playing solo";

                    string connect = SteamFriends.GetFriendRichPresence(friendId, ConnectKey);
                    if (TryParseLobbyId(connect, out lobbyId))
                        status = "Hosting co-op";
                    else if (!string.IsNullOrEmpty(connect))
                        status = "In multiplayer";

                    string richStatus = SteamFriends.GetFriendRichPresence(friendId, "status");
                    if (!string.IsNullOrEmpty(richStatus))
                        status = richStatus;

                    results.Add(new FriendEntry
                    {
                        SteamId = friendId.m_SteamID,
                        Name = string.IsNullOrEmpty(name) ? "Friend" : name,
                        LobbyId = lobbyId,
                        StatusHint = status,
                    });
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"Steam friend scan failed: {e.Message}");
            }

            results.Sort(CompareEntries);
            return results;
        }

        private static int CompareEntries(FriendEntry a, FriendEntry b)
        {
            int joinable = b.LobbyId.CompareTo(a.LobbyId);
            if (joinable != 0) return joinable;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryParseLobbyId(string connect, out ulong lobbyId)
        {
            lobbyId = 0;
            if (string.IsNullOrEmpty(connect)) return false;

            const string prefix = "+connect_lobby ";
            if (!connect.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string idText = connect.Substring(prefix.Length).Trim();
            return ulong.TryParse(idText, out lobbyId) && lobbyId != 0;
        }
    }
}
#endif
