#if STEAMWORKS
using System;
using Steamworks;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Steam
{
    /// <summary>
    /// EXPERIMENTAL (M1) — lobby lifecycle on the classic Steamworks API: create/join
    /// friends-only lobbies, wire overlay invites ("Join Game" in the friends list) via
    /// rich presence, and hand a transport back to the session manager.
    /// </summary>
    internal static class SteamLobbyManager
    {
        private const string LobbyKeyHostId = "wmp_host";
        private const string LobbyKeyModVersion = "wmp_version";
        private const int DefaultMaxPlayers = 8;

        private static Callback<LobbyCreated_t>? _lobbyCreated;
        private static Callback<LobbyEnter_t>? _lobbyEntered;
        private static Callback<GameLobbyJoinRequested_t>? _joinRequested;
        private static Callback<LobbyChatUpdate_t>? _lobbyChatUpdate;

        private static Action<ITransport>? _onReady;
        private static Action<string>? _onFailure;
        private static CSteamID _currentLobby;
        private static bool _isOwner;
        private static SteamP2PTransport? _activeTransport;

        /// <summary>
        /// Register overlay / friends-list join handlers as soon as Steam is live.
        /// Must happen before the first invite — not only when hosting/joining.
        /// </summary>
        public static void EnsureCallbacksRegistered()
        {
            if (!SteamBootstrap.EnsureInitialized()) return;
            RegisterCallbacks();
        }

        public static void HostLobby(Action<ITransport> onReady, Action<string> onFailure)
        {
            if (!SteamBootstrap.EnsureInitialized())
            {
                onFailure("Steam isn't ready yet — still waiting for the game to connect.");
                return;
            }

            _onReady = onReady;
            _onFailure = onFailure;
            _isOwner = true;
            EnsureCallbacksRegistered();

            WinterMPPlugin.Log.LogInfo("Creating friends-only Steam lobby...");
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, DefaultMaxPlayers);
        }

        public static void JoinLobby(ulong lobbyId, Action<ITransport> onReady, Action<string> onFailure)
        {
            if (!SteamBootstrap.EnsureInitialized())
            {
                onFailure("Steam isn't ready yet — still waiting for the game to connect.");
                return;
            }

            _onReady = onReady;
            _onFailure = onFailure;
            _isOwner = false;
            EnsureCallbacksRegistered();

            WinterMPPlugin.Log.LogInfo($"Joining Steam lobby {lobbyId}...");
            SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
        }

        public static bool IsLobbyMember(CSteamID steamId)
        {
            if (!_currentLobby.IsValid()) return false;

            try
            {
                int count = SteamMatchmaking.GetNumLobbyMembers(_currentLobby);
                for (int i = 0; i < count; i++)
                {
                    if (SteamMatchmaking.GetLobbyMemberByIndex(_currentLobby, i) == steamId)
                        return true;
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"IsLobbyMember failed: {e.Message}");
            }

            return false;
        }

        private static void RegisterCallbacks()
        {
            try
            {
                if (_lobbyCreated == null) _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
                if (_lobbyEntered == null) _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                if (_joinRequested == null) _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
                if (_lobbyChatUpdate == null) _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"Steam callback registration failed: {e}");
            }
        }

        private static void OnLobbyCreated(LobbyCreated_t data)
        {
            if (data.m_eResult != EResult.k_EResultOK)
            {
                Fail($"Lobby creation failed: {data.m_eResult}");
                return;
            }

            // A host attempt superseded by a join (RequestJoinFromSteam → StartJoin set
            // _isOwner=false) must not build a host transport or drive the now-join callbacks.
            // Release the orphaned lobby rather than leaking it on Steam's servers.
            if (!_isOwner)
            {
                WinterMPPlugin.Log.LogInfo($"Discarding superseded host lobby {data.m_ulSteamIDLobby}.");
                try { SteamMatchmaking.LeaveLobby(new CSteamID(data.m_ulSteamIDLobby)); }
                catch { /* best effort */ }
                return;
            }

            _currentLobby = new CSteamID(data.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_currentLobby, LobbyKeyHostId, SteamBootstrap.LocalSteamId.ToString());
            SteamMatchmaking.SetLobbyData(_currentLobby, LobbyKeyModVersion, MyPluginInfo.PLUGIN_VERSION);

            // Makes "Join Game" appear on us in friends lists; Steam launches the game
            // with "+connect_lobby <id>", which LaunchOptions handles at boot.
            try
            {
                SteamFriends.SetRichPresence("connect", $"+connect_lobby {data.m_ulSteamIDLobby}");
                SteamFriends.SetRichPresence("status", $"Hosting {MyPluginInfo.PLUGIN_NAME}");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"Rich presence failed (invites via overlay still work): {e.Message}");
            }

            WinterMPPlugin.Log.LogInfo($"Lobby created: {data.m_ulSteamIDLobby}");

            try
            {
                _activeTransport = SteamP2PTransport.CreateHost();
                _onReady?.Invoke(_activeTransport);
            }
            catch (Exception e)
            {
                Fail($"Failed to start host transport: {e.Message}");
            }
        }

        private static void OnLobbyEntered(LobbyEnter_t data)
        {
            if (_isOwner) return; // the host also receives LobbyEnter for its own lobby

            if (data.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Fail($"Could not enter lobby (response {data.m_EChatRoomEnterResponse}).");
                return;
            }

            _currentLobby = new CSteamID(data.m_ulSteamIDLobby);

            string lobbyMod = SteamMatchmaking.GetLobbyData(_currentLobby, LobbyKeyModVersion);
            if (!string.IsNullOrEmpty(lobbyMod)
                && !string.Equals(lobbyMod, MyPluginInfo.PLUGIN_VERSION, StringComparison.Ordinal))
            {
                Fail($"Mod version mismatch (host {lobbyMod}, you {MyPluginInfo.PLUGIN_VERSION}). Update {MyPluginInfo.PLUGIN_NAME}.");
                LeaveLobby();
                return;
            }

            string hostIdRaw = SteamMatchmaking.GetLobbyData(_currentLobby, LobbyKeyHostId);
            ulong hostId;
            if (!ulong.TryParse(hostIdRaw, out hostId) || hostId == 0)
            {
                Fail($"Lobby has no {MyPluginInfo.PLUGIN_NAME} host data — is the host running the mod?");
                return;
            }

            WinterMPPlugin.Log.LogInfo($"Entered lobby {data.m_ulSteamIDLobby}; connecting to host {hostId}.");

            try
            {
                _activeTransport = SteamP2PTransport.CreateClient(new CSteamID(hostId));
                _onReady?.Invoke(_activeTransport);
            }
            catch (Exception e)
            {
                Fail($"Failed to start client transport: {e.Message}");
            }
        }

        /// <summary>Overlay invite or friends-list join while the game is already running.</summary>
        private static void OnJoinRequested(GameLobbyJoinRequested_t data)
        {
            WinterMPPlugin.Log.LogInfo($"Steam join request for lobby {data.m_steamIDLobby.m_SteamID}.");
            var session = Session.SessionManager.Instance;
            if (session != null)
                session.RequestJoinFromSteam(data.m_steamIDLobby.m_SteamID);
        }

        private static void OnLobbyChatUpdate(LobbyChatUpdate_t data)
        {
            const uint leftOrDropped =
                (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft
                | (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                | (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked
                | (uint)EChatMemberStateChange.k_EChatMemberStateChangeBanned;

            if ((data.m_rgfChatMemberStateChange & leftOrDropped) != 0)
                _activeTransport?.NotifyPeerLeft(data.m_ulSteamIDUserChanged, "Left the Steam lobby.");
        }

        public static void LeaveLobby()
        {
            _activeTransport = null;
            // Drop result handlers so a late Steam callback from this (now torn-down) attempt
            // cannot drive a newer session's _onReady/_onFailure.
            _onReady = null;
            _onFailure = null;

            if (_currentLobby.IsValid())
            {
                try
                {
                    SteamMatchmaking.LeaveLobby(_currentLobby);
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogWarning($"LeaveLobby failed: {e.Message}");
                }

                _currentLobby = default(CSteamID);
            }

            try
            {
                SteamFriends.ClearRichPresence();
            }
            catch
            {
                // best effort
            }
        }

        private static void Fail(string error)
        {
            WinterMPPlugin.Log.LogError($"Steam lobby: {error}");
            // Capture-then-clear so a stale callback from a superseded attempt can't refire a handler.
            var failure = _onFailure;
            _onReady = null;
            _onFailure = null;
            failure?.Invoke(error);
        }
    }
}
#endif
