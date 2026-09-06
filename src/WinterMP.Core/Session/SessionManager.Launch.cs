using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core;
using WinterMP.Core.Catalog;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Session
{
    public sealed partial class SessionManager
    {
        public void Initialize(LaunchOptions launch)
        {
            Util.BootTrace.Crumb("SessionManager.Initialize: begin");
            Instance = this;
            _launch = launch;
            GuestSaveGuard.Initialize();
            SessionLaunchPolicy.FastSessionLaunch = launch.FastSessionLaunch;

            // Precedence: command line (test tooling) > config > Steam persona/OS user.
            LocalPlayerName = !string.IsNullOrEmpty(launch.PlayerName)
                ? launch.PlayerName!
                : !string.IsNullOrEmpty(WinterMPPlugin.PlayerNameOverride.Value)
                    ? WinterMPPlugin.PlayerNameOverride.Value
                    : DefaultPlayerName();

            Util.BootTrace.Crumb("SessionManager.Initialize: adding DebugOverlay");
            gameObject.AddComponent<UI.DebugOverlay>();

            // Host/join from the command line is deferred: this code runs at the
            // MonoBehaviour entrypoint, long before the game initializes Steam.
            // We act once the main menu is up (or after a timeout as a fallback).
            _pendingMode = launch.Mode;
            _pendingLobbyId = launch.LobbyId;
            _joinBrowseActive = launch.Mode == LaunchMode.JoinBrowse;

            if ((launch.Mode == LaunchMode.Join || launch.Mode == LaunchMode.JoinLocal
                || launch.Mode == LaunchMode.JoinBrowse) && !PrepareGuestSaveProtection())
            {
                _pendingMode = LaunchMode.None;
                _joinBrowseActive = false;
                return;
            }

            if (_pendingMode != LaunchMode.None)
                SetState(SessionState.Idle, PendingLaunchStatus(launch.Mode));

            Util.BootTrace.Crumb("SessionManager.Initialize: done");
        }

        private static bool IsMainMenuReady()
        {
            try
            {
                return Application.loadedLevelName == "MainMenu";
            }
            catch
            {
                return false;
            }
        }

        private bool WantsEarlySteamWork =>
            _launch.FastSessionLaunch
            && (_pendingMode == LaunchMode.Host
                || _pendingMode == LaunchMode.Join
                || _pendingMode == LaunchMode.JoinBrowse);

        private bool CanExecutePendingLaunch(bool menuReady)
        {
            if (menuReady) return true;
            if (!_launch.FastSessionLaunch) return false;

            return _pendingMode == LaunchMode.Host
                || (_pendingMode == LaunchMode.Join && _pendingLobbyId != 0);
        }

        private void EnsureSteamInviteHandlers()
        {
#if STEAMWORKS
            if (_steamCallbacksRegistered) return;

            bool menuReady = IsMainMenuReady();
            if ((menuReady || WantsEarlySteamWork) && !_steamMainMenuNotified)
            {
                _steamMainMenuNotified = true;
                Steam.SteamBootstrap.NoteMainMenuReady();
            }

            if (!menuReady && !WantsEarlySteamWork && Time.realtimeSinceStartup < PendingLaunchTimeoutSeconds)
                return;

            if (!Steam.SteamBootstrap.EnsureInitialized()) return;

            Steam.SteamLobbyManager.EnsureCallbacksRegistered();
            Steam.SteamBootstrap.SelfPump = true;
            _steamCallbacksRegistered = true;
#endif
        }

        private bool TryEnsureSteamReady(string waitingStatus)
        {
#if STEAMWORKS
            if (Steam.SteamBootstrap.EnsureInitialized()) return true;

            if (State == SessionState.Idle && StatusText != waitingStatus)
                SetState(SessionState.Idle, waitingStatus);
            return false;
#else
            return false;
#endif
        }

        private void RunPendingLaunchMode()
        {
            EnsureSteamInviteHandlers();

            if (_pendingMode == LaunchMode.None || State != SessionState.Idle) return;

            bool menuReady = IsMainMenuReady();
            if (!CanExecutePendingLaunch(menuReady)
                && Time.realtimeSinceStartup < PendingLaunchTimeoutSeconds)
            {
                return;
            }

#if STEAMWORKS
            if (_pendingMode == LaunchMode.Host || _pendingMode == LaunchMode.Join)
            {
                if (!TryEnsureSteamReady("Connecting to Steam…"))
                    return;
            }
#endif

            var mode = _pendingMode;
            _pendingMode = LaunchMode.None;
            WinterMPPlugin.Log.LogInfo($"Game is up — executing launch mode '{mode}'.");

            switch (mode)
            {
                case LaunchMode.Host:
                    StartHost();
                    break;
                case LaunchMode.Join:
                    StartJoin(_pendingLobbyId);
                    break;
                case LaunchMode.JoinBrowse:
                    SetState(SessionState.Idle, "Pick a friend to join");
                    break;
                case LaunchMode.HostLocal:
                    StartHostLocal(_launch.LocalPort);
                    break;
                case LaunchMode.JoinLocal:
                    StartJoinLocal(_launch.LocalAddress, _launch.LocalPort);
                    break;
            }
        }

        private static string PendingLaunchStatus(LaunchMode mode)
        {
            switch (mode)
            {
                case LaunchMode.JoinBrowse:
                    return "Waiting for main menu — pick a friend to join";
                default:
                    return "Waiting for game to boot before '" + mode + "'...";
            }
        }

        private static string DefaultPlayerName()
        {
            // Steam persona is fetched lazily when a session starts — touching
            // SteamAPI at Awake time is too early for this game (see SteamBootstrap).
            return Environment.UserName;
        }

#if STEAMWORKS
        private void RefreshPlayerNameFromSteam()
        {
            if (!string.IsNullOrEmpty(WinterMPPlugin.PlayerNameOverride.Value)) return;
            var persona = Steam.SteamBootstrap.TryGetPersonaName();
            if (!string.IsNullOrEmpty(persona))
                LocalPlayerName = persona!;
        }
#endif

        // ---------------------------------------------------------------- session control

        public void StartHost()
        {
            if (State != SessionState.Idle)
            {
                WinterMPPlugin.Log.LogWarning($"StartHost ignored: session state is {State}.");
                return;
            }

            if (!CheckHostSaveProtection()) return;

#if STEAMWORKS
            try
            {
                if (!TryEnsureSteamReady("Waiting for Steam…"))
                    return;

                _bypassHostPlayerGate = HostLaunchPolicy.BypassPlayerGate;
                IsHost = true;
                LocalPlayerId = 0;
                _steamOpStartedAt = Time.unscaledTime;
                RefreshPlayerNameFromSteam();
                Steam.SteamBootstrap.SelfPump = true;
                Steam.SteamLobbyManager.LeaveLobby();
                _steamLobbyAttemptActive = true;
                SetState(SessionState.Hosting, "Creating Steam lobby...");
                Steam.SteamLobbyManager.HostLobby(OnSteamTransportReady, OnSteamFailure);
            }
            catch (Exception e)
            {
                FailSession($"Steam hosting failed: {e.Message}");
                WinterMPPlugin.Log.LogError(e);
            }
#else
            WinterMPPlugin.Log.LogWarning(
                "Steam transport not compiled in (drop Steamworks.NET.dll into libs/ and rebuild). " +
                "Starting a loopback dev session instead.");
            StartDevLoopback();
#endif
        }

        /// <summary>
        /// Steam overlay / friends-list join while the game is running (or cold-start
        /// <c>+connect_lobby</c> queued until the main menu).
        /// </summary>
        public void RequestJoinFromSteam(ulong lobbyId)
        {
            if (lobbyId == 0) return;

#if STEAMWORKS
            Steam.SteamLobbyManager.EnsureCallbacksRegistered();

            if (State == SessionState.Failed)
            {
                if (_failedSessionCleanupPending)
                {
                    _pendingMode = LaunchMode.Join;
                    _pendingLobbyId = lobbyId;
                    _joinBrowseActive = false;
                    return;
                }

                SetState(SessionState.Idle, "Idle");
            }

            if (State != SessionState.Idle)
            {
                WinterMPPlugin.Log.LogInfo($"Leaving current session to join lobby {lobbyId}...");
                Shutdown("Switching to friend's lobby.");
            }

            if (IsMainMenuReady())
                StartJoin(lobbyId);
            else
            {
                _pendingMode = LaunchMode.Join;
                _pendingLobbyId = lobbyId;
                SetState(SessionState.Idle, $"Queued Steam join to lobby {lobbyId}...");
            }
#else
            WinterMPPlugin.Log.LogWarning($"Steam join to lobby {lobbyId} ignored: Steam transport not compiled in.");
#endif
        }

        public void StartJoin(ulong lobbyId)
        {
            if (State != SessionState.Idle)
            {
                WinterMPPlugin.Log.LogWarning($"StartJoin ignored: session state is {State}.");
                return;
            }

            _joinBrowseActive = false;

#if STEAMWORKS
            try
            {
                if (!TryEnsureSteamReady("Waiting for Steam…"))
                    return;

                if (!PrepareGuestSaveProtection()) return;
                IsHost = false;
                _steamOpStartedAt = Time.unscaledTime;
                RefreshPlayerNameFromSteam();
                Steam.SteamBootstrap.SelfPump = true;
                Steam.SteamLobbyManager.LeaveLobby();
                _steamLobbyAttemptActive = true;
                SetState(SessionState.Connecting, $"Joining lobby {lobbyId}...");
                Steam.SteamLobbyManager.JoinLobby(lobbyId, OnSteamTransportReady, OnSteamFailure);
            }
            catch (Exception e)
            {
                FailSession($"Steam join failed: {e.Message}");
                WinterMPPlugin.Log.LogError(e);
            }
#else
            WinterMPPlugin.Log.LogWarning($"Cannot join lobby {lobbyId}: Steam transport not compiled in.");
            FailSession("Steam transport not available in this build.");
#endif
        }

        /// <summary>Local two-instance test: host a session on a localhost UDP port. No Steam needed.</summary>
        public void StartHostLocal(int port)
        {
            if (State != SessionState.Idle)
            {
                WinterMPPlugin.Log.LogWarning($"StartHostLocal ignored: session state is {State}.");
                return;
            }

            if (!CheckHostSaveProtection()) return;

            try
            {
                _bypassHostPlayerGate = true;
                IsHost = true;
                LocalPlayerId = 0;
                ConnectionQuality.Instance.TransportName = $"UDP :{port}";
                AttachTransport(UdpTransport.CreateHost(port));
                SetState(SessionState.Hosting, $"Hosting local test session on UDP port {port}");
            }
            catch (Exception e)
            {
                FailSession($"Could not host on UDP port {port}: {e.Message}");
                WinterMPPlugin.Log.LogError(e);
            }
        }

        /// <summary>Local two-instance test: join a localhost UDP session. No Steam needed.</summary>
        public void StartJoinLocal(string address, int port)
        {
            if (State != SessionState.Idle)
            {
                WinterMPPlugin.Log.LogWarning($"StartJoinLocal ignored: session state is {State}.");
                return;
            }

            if (!PrepareGuestSaveProtection()) return;

            try
            {
                IsHost = false;
                var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(address), port);
                AttachTransport(UdpTransport.CreateClient(endpoint));
                SetState(SessionState.Connecting, $"Joining local game at {endpoint}...");
            }
            catch (Exception e)
            {
                FailSession($"Could not join {address}:{port}: {e.Message}");
                WinterMPPlugin.Log.LogError(e);
            }
        }

        /// <summary>Dev convenience (F8): host + fake guest inside one game instance.</summary>
        public void StartDevLoopback()
        {
            if (State != SessionState.Idle) return;
            if (!CheckHostSaveProtection()) return;

            _bypassHostPlayerGate = true;
            ConnectionQuality.Instance.TransportName = "Loopback";
            var pair = LoopbackTransport.CreatePair();
            IsHost = true;
            LocalPlayerId = 0;
            AttachTransport(pair.Host);
            _devClient = new DevLoopbackClient(pair.Client, "LoopbackGuest");
            SetState(SessionState.Hosting, "Hosting (loopback dev session)");
        }

        private bool PrepareGuestSaveProtection()
        {
            if (GuestSaveGuard.TryBeginGuest()) return true;
            SetState(SessionState.Failed, GuestSaveGuard.Unavailable);
            return false;
        }

        private bool CheckHostSaveProtection()
        {
            if (GuestSaveGuard.CanHost) return true;
            SetState(SessionState.Idle, GuestSaveGuard.RestartToHost);
            AddChatLine("* " + GuestSaveGuard.RestartToHost);
            return false;
        }
    }
}
