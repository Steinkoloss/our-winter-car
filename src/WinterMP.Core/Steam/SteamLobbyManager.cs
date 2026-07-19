#if STEAMWORKS
using System;
using System.Collections.Generic;
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

        private static Callback<GameLobbyJoinRequested_t>? _joinRequested;
        private static Callback<LobbyChatUpdate_t>? _lobbyChatUpdate;

        private static Action<ITransport>? _onReady;
        private static Action<string>? _onFailure;
        private static CSteamID _currentLobby;
        private static SteamP2PTransport? _activeTransport;
        private static LobbyAttempt? _activeAttempt;
        private static readonly List<LobbyAttempt> RetiredAttempts = new List<LobbyAttempt>();

        /// <summary>
        /// Steam delivers lobby completion callbacks by API-call handle. Keep a
        /// cancelled attempt alive until that handle resolves so its late success
        /// can leave only its own lobby, never drive a newer session's handlers.
        /// </summary>
        private sealed class LobbyAttempt
        {
            public readonly ulong RequestedLobbyId;
            public bool Cancelled;
            public CallResult<LobbyCreated_t>? CreatedResult;
            public CallResult<LobbyEnter_t>? EnteredResult;

            public LobbyAttempt(ulong requestedLobbyId)
            {
                RequestedLobbyId = requestedLobbyId;
            }
        }

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
            var attempt = BeginAttempt(requestedLobbyId: 0);
            EnsureCallbacksRegistered();

            WinterMPPlugin.Log.LogInfo("Creating friends-only Steam lobby...");
            try
            {
                var result = CallResult<LobbyCreated_t>.Create(
                    (data, ioFailure) => OnLobbyCreated(attempt, data, ioFailure));
                attempt.CreatedResult = result;
                SteamAPICall_t call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, DefaultMaxPlayers);
                if (call == SteamAPICall_t.Invalid)
                    throw new InvalidOperationException("Steam rejected the lobby creation request.");
                result.Set(call);
            }
            catch
            {
                CompleteAttempt(attempt);
                throw;
            }
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
            var attempt = BeginAttempt(requestedLobbyId: lobbyId);
            EnsureCallbacksRegistered();

            WinterMPPlugin.Log.LogInfo($"Joining Steam lobby {lobbyId}...");
            try
            {
                var result = CallResult<LobbyEnter_t>.Create(
                    (data, ioFailure) => OnLobbyEntered(attempt, data, ioFailure));
                attempt.EnteredResult = result;
                SteamAPICall_t call = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
                if (call == SteamAPICall_t.Invalid)
                    throw new InvalidOperationException("Steam rejected the lobby join request.");
                result.Set(call);
            }
            catch
            {
                CompleteAttempt(attempt);
                throw;
            }
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
                if (_joinRequested == null) _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
                if (_lobbyChatUpdate == null) _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"Steam callback registration failed: {e}");
            }
        }

        private static void OnLobbyCreated(LobbyAttempt attempt, LobbyCreated_t data, bool ioFailure)
        {
            bool isCurrent = IsCurrentAttempt(attempt);
            try
            {
                HandleLobbyCreated(attempt, data, ioFailure, isCurrent);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"Steam lobby creation callback failed: {e}");
                CompleteAttempt(attempt);
                if (isCurrent)
                    Fail("Steam lobby creation callback failed: " + e.Message);
            }
        }

        private static void HandleLobbyCreated(LobbyAttempt attempt, LobbyCreated_t data, bool ioFailure, bool isCurrent)
        {
            if (!isCurrent)
            {
                if (!ioFailure && data.m_eResult == EResult.k_EResultOK)
                    LeaveStaleLobby(data.m_ulSteamIDLobby);
                CompleteAttempt(attempt);
                return;
            }

            CompleteAttempt(attempt);
            if (ioFailure || data.m_eResult != EResult.k_EResultOK)
            {
                Fail(ioFailure ? "Lobby creation request failed in Steam." : $"Lobby creation failed: {data.m_eResult}");
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

        private static void OnLobbyEntered(LobbyAttempt attempt, LobbyEnter_t data, bool ioFailure)
        {
            bool isCurrent = IsCurrentAttempt(attempt);
            try
            {
                HandleLobbyEntered(attempt, data, ioFailure, isCurrent);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"Steam lobby enter callback failed: {e}");
                CompleteAttempt(attempt);
                if (isCurrent)
                    Fail("Steam lobby enter callback failed: " + e.Message);
            }
        }

        private static void HandleLobbyEntered(LobbyAttempt attempt, LobbyEnter_t data, bool ioFailure, bool isCurrent)
        {
            if (!isCurrent)
            {
                WinterMPPlugin.Log.LogInfo($"Discarding stale Steam lobby entry {data.m_ulSteamIDLobby}.");
                if (!ioFailure && data.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                    LeaveStaleLobby(data.m_ulSteamIDLobby);
                CompleteAttempt(attempt);
                return;
            }

            if (data.m_ulSteamIDLobby != attempt.RequestedLobbyId)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"Steam returned lobby {data.m_ulSteamIDLobby} for join attempt {attempt.RequestedLobbyId}.");
                if (!ioFailure && data.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                    LeaveStaleLobby(data.m_ulSteamIDLobby);
                CompleteAttempt(attempt);
                Fail("Steam entered an unexpected lobby.");
                return;
            }

            CompleteAttempt(attempt);
            if (ioFailure || data.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Fail(ioFailure ? "Lobby join request failed in Steam."
                    : $"Could not enter lobby (response {data.m_EChatRoomEnterResponse}).");
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
            try
            {
                WinterMPPlugin.Log.LogInfo($"Steam join request for lobby {data.m_steamIDLobby.m_SteamID}.");
                var session = Session.SessionManager.Instance;
                if (session != null)
                    session.RequestJoinFromSteam(data.m_steamIDLobby.m_SteamID);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"Steam join request callback failed: {e}");
            }
        }

        private static void OnLobbyChatUpdate(LobbyChatUpdate_t data)
        {
            try
            {
                if (!_currentLobby.IsValid() || data.m_ulSteamIDLobby != _currentLobby.m_SteamID)
                    return;

                const uint leftOrDropped =
                    (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft
                    | (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                    | (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked
                    | (uint)EChatMemberStateChange.k_EChatMemberStateChangeBanned;

                if ((data.m_rgfChatMemberStateChange & leftOrDropped) != 0)
                    _activeTransport?.NotifyPeerLeft(data.m_ulSteamIDUserChanged, "Left the Steam lobby.");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"Steam lobby chat callback failed: {e}");
            }
        }

        public static void LeaveLobby()
        {
            CancelActiveAttempt();
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
            CancelActiveAttempt();
            // Capture-then-clear so a stale callback from a superseded attempt can't refire a handler.
            var failure = _onFailure;
            _onReady = null;
            _onFailure = null;
            failure?.Invoke(error);
        }

        private static LobbyAttempt BeginAttempt(ulong requestedLobbyId)
        {
            CancelActiveAttempt();
            var attempt = new LobbyAttempt(requestedLobbyId);
            _activeAttempt = attempt;
            return attempt;
        }

        private static bool IsCurrentAttempt(LobbyAttempt attempt)
        {
            return ReferenceEquals(_activeAttempt, attempt) && !attempt.Cancelled;
        }

        private static void CancelActiveAttempt()
        {
            if (_activeAttempt == null) return;

            _activeAttempt.Cancelled = true;
            RetiredAttempts.Add(_activeAttempt);
            _activeAttempt = null;
        }

        private static void CompleteAttempt(LobbyAttempt attempt)
        {
            if (ReferenceEquals(_activeAttempt, attempt))
                _activeAttempt = null;
            RetiredAttempts.Remove(attempt);
        }

        private static void LeaveStaleLobby(ulong lobbyId)
        {
            if (lobbyId == 0 || (_currentLobby.IsValid() && _currentLobby.m_SteamID == lobbyId))
                return;

            try
            {
                SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
            }
            catch
            {
                // best effort: a stale Steam callback must never perturb the active attempt
            }
        }
    }
}
#endif
