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
    public enum SessionState
    {
        Idle,
        Hosting,
        Connecting,
        Connected,
        Failed,
    }

    /// <summary>
    /// Orchestrates the multiplayer session: transport lifecycle, handshake, player
    /// registry, chat, ping loop. World-sync subsystems (M3+) subscribe to this.
    /// </summary>
    public sealed class SessionManager : MonoBehaviour
    {
        private const float PingIntervalSeconds = 2f;

        public static SessionManager? Instance { get; private set; }

        public SessionState State { get; private set; } = SessionState.Idle;
        public bool IsHost { get; private set; }
        public string LocalPlayerName { get; private set; } = "Player";
        public byte LocalPlayerId { get; private set; }
        public string StatusText { get; private set; } = "Idle";

        private ITransport? _transport;
        private PeerId? _hostPeer;
        private DevLoopbackClient? _devClient;
        private LaunchOptions _launch = new LaunchOptions();
        private readonly Dictionary<PeerId, RemotePlayer> _playersByPeer = new Dictionary<PeerId, RemotePlayer>();
        private readonly List<string> _chatLog = new List<string>();
        private readonly NetWriter _sendWriter = new NetWriter(1024);
        private byte _nextPlayerId = 1;
        private float _nextPingAt;
        private float _steamOpStartedAt = -1f;
        private uint _pingNonce;
        private readonly Dictionary<uint, float> _pendingPings = new Dictionary<uint, float>();

        private readonly Dictionary<byte, PassengerState> _passengerOccupancy = new Dictionary<byte, PassengerState>();

        /// <summary>Stable player ids + disconnect tracking for mid-session guest rejoin (PLAN.md §4.5).</summary>
        private sealed class GuestSlot
        {
            public byte PlayerId;
            public bool Disconnected;
        }

        private readonly Dictionary<ulong, GuestSlot> _guestSlotsBySteam = new Dictionary<ulong, GuestSlot>();

        public IEnumerable<RemotePlayer> Players => _playersByPeer.Values;
        public int PlayerCount => _playersByPeer.Count;
        public IList<string> ChatLog => _chatLog;

        /// <summary>From the host save's permadeath flag — guests mirror this for the session.</summary>
        public bool PermanentDeathEnabled { get; private set; }

        public void SetPermanentDeathEnabled(bool enabled)
        {
            PermanentDeathEnabled = enabled;
        }

        public event Action<RemotePlayer>? PlayerJoined;
        public event Action<RemotePlayer>? PlayerLeft;

        /// <summary>Don't touch Steam before the game's own init; menu load is the safe signal.
        /// Splash → menu takes 30-40s on a typical machine, so the fallback must be generous.</summary>
        private const float PendingLaunchTimeoutSeconds = 60f;

        private LaunchMode _pendingMode = LaunchMode.None;
        private ulong _pendingLobbyId;
        private bool _steamCallbacksRegistered;
        private bool _bypassHostPlayerGate;
        private bool _steamMainMenuNotified;
        private bool _joinBrowseActive;

        /// <summary>Launcher join path — show friend picker on the main menu.</summary>
        public bool ShowJoinBrowseUI =>
            _joinBrowseActive
            && !IsHost
            && State == SessionState.Idle;

        /// <summary>Steam host on the main menu with no guests — load/resume is blocked.</summary>
        public bool ShouldBlockHostMainMenuLoad =>
            IsHost
            && !_bypassHostPlayerGate
            && PlayerCount == 0
            && (State == SessionState.Hosting || State == SessionState.Connected);

        /// <summary>Show lobby roster on the main menu while hosting a real session.</summary>
        public bool ShowHostLobbyRoster =>
            IsHost
            && !_bypassHostPlayerGate
            && (State == SessionState.Hosting || State == SessionState.Connected);

        /// <summary>Alias kept for world-sync auto-load gating.</summary>
        public bool IsHostWaitingForPlayers => ShouldBlockHostMainMenuLoad;

        /// <summary>Called by FastBoot when BypassHostContinueWait is enabled.</summary>
        public void SetBypassHostPlayerGate(bool bypass)
        {
            _bypassHostPlayerGate = bypass;
        }

        /// <summary>Host tracks seat occupancy so join snapshots can replay it immediately.</summary>
        public void RecordPassengerState(PassengerState state)
        {
            if (!IsHost) return;

            if (state.IsSeated)
                _passengerOccupancy[state.PlayerId] = state;
            else
                _passengerOccupancy.Remove(state.PlayerId);
        }

        private float _failedAt = -1f;
        private const float FailedRecoverySeconds = 45f;

        public void Initialize(LaunchOptions launch)
        {
            Util.BootTrace.Crumb("SessionManager.Initialize: begin");
            Instance = this;
            _launch = launch;
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
                SetState(SessionState.Hosting, "Creating Steam lobby...");
                Steam.SteamLobbyManager.HostLobby(OnSteamTransportReady, OnSteamFailure);
            }
            catch (Exception e)
            {
                SetState(SessionState.Failed, $"Steam hosting failed: {e.Message}");
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
                SetState(SessionState.Idle, "Idle");

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

                IsHost = false;
                _steamOpStartedAt = Time.unscaledTime;
                RefreshPlayerNameFromSteam();
                Steam.SteamBootstrap.SelfPump = true;
                Steam.SteamLobbyManager.LeaveLobby();
                SetState(SessionState.Connecting, $"Joining lobby {lobbyId}...");
                Steam.SteamLobbyManager.JoinLobby(lobbyId, OnSteamTransportReady, OnSteamFailure);
            }
            catch (Exception e)
            {
                SetState(SessionState.Failed, $"Steam join failed: {e.Message}");
                WinterMPPlugin.Log.LogError(e);
            }
#else
            WinterMPPlugin.Log.LogWarning($"Cannot join lobby {lobbyId}: Steam transport not compiled in.");
            SetState(SessionState.Failed, "Steam transport not available in this build.");
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
                SetState(SessionState.Failed, $"Could not host on UDP port {port}: {e.Message}");
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

            try
            {
                IsHost = false;
                var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(address), port);
                AttachTransport(UdpTransport.CreateClient(endpoint));
                SetState(SessionState.Connecting, $"Joining local game at {endpoint}...");
            }
            catch (Exception e)
            {
                SetState(SessionState.Failed, $"Could not join {address}:{port}: {e.Message}");
                WinterMPPlugin.Log.LogError(e);
            }
        }

        /// <summary>Dev convenience (F8): host + fake guest inside one game instance.</summary>
        public void StartDevLoopback()
        {
            if (State != SessionState.Idle) return;

            _bypassHostPlayerGate = true;
            ConnectionQuality.Instance.TransportName = "Loopback";
            var pair = LoopbackTransport.CreatePair();
            IsHost = true;
            LocalPlayerId = 0;
            AttachTransport(pair.Host);
            _devClient = new DevLoopbackClient(pair.Client, "LoopbackGuest");
            SetState(SessionState.Hosting, "Hosting (loopback dev session)");
        }

        public void Shutdown(string reason)
        {
            if (_transport != null)
            {
                var bye = new DisconnectMessage { Reason = reason };
                foreach (var peer in _playersByPeer.Keys)
                    SendTo(peer, bye, Channel.ReliableOrdered);
            }

            _devClient?.Dispose();
            _devClient = null;
            _transport?.Dispose();
            _transport = null;
            _hostPeer = null;
            _playersByPeer.Clear();
            _pendingPings.Clear();
            _steamOpStartedAt = -1f;
            _bypassHostPlayerGate = false;
            IsHost = false;
            _joinBrowseActive = false;
            _guestSlotsBySteam.Clear();
            ConnectionQuality.Instance.Reset();
            NetTrafficMeter.Instance.Reset();
            SetState(SessionState.Idle, "Idle");
        }

        private void PollTransportQuality()
        {
#if STEAMWORKS
            if (_transport is Steam.SteamP2PTransport steam)
            {
                if (!IsHost && _hostPeer.HasValue)
                    steam.PollSessionQuality(_hostPeer.Value);
                else if (IsHost)
                {
                    foreach (var peer in _playersByPeer.Keys)
                    {
                        steam.PollSessionQuality(peer);
                        break;
                    }
                }
            }
#endif
        }

        private void AttachTransport(ITransport transport)
        {
            _transport = transport;
            transport.PeerConnected += OnPeerConnected;
            transport.PeerDisconnected += OnPeerDisconnected;
            transport.PacketReceived += OnPacketReceived;
        }

#if STEAMWORKS
        private void OnSteamTransportReady(ITransport transport)
        {
            ConnectionQuality.Instance.TransportName = "Steam P2P";
            AttachTransport(transport);
            if (IsHost)
            {
                SetState(SessionState.Hosting, "Waiting for a friend — Steam → Join Game");
            }
            // As client: handshake is sent from OnPeerConnected when the connection to the host opens.
        }

        private void OnSteamFailure(string error)
        {
            SetState(SessionState.Failed, error);
            WinterMPPlugin.Log.LogError($"Steam session failure: {error}");
        }
#endif

        // ---------------------------------------------------------------- unity loop

        private void Update()
        {
#if STEAMWORKS
            Steam.SteamBootstrap.Pump();

            // Fallback: if the game does not pump Steam callbacks itself, our lobby
            // callbacks never fire. Detect the stall and start pumping ourselves.
            if (_transport == null && _steamOpStartedAt >= 0f && !Steam.SteamBootstrap.SelfPump
                && Time.unscaledTime - _steamOpStartedAt > 5f
                && (State == SessionState.Hosting || State == SessionState.Connecting))
            {
                Steam.SteamBootstrap.SelfPump = true;
                WinterMPPlugin.Log.LogWarning(
                    "No Steam callbacks within 5s — the game does not seem to pump them; enabling self-pump.");
            }
#endif
            RunPendingLaunchMode();
            RecoverFromFailed();

            _transport?.Update();
            _devClient?.Update();

            ConnectionQuality.Instance.TickWindow(Time.unscaledTime);
            NetTrafficMeter.Instance.Tick(Time.unscaledTime, _playersByPeer.Count);
            PollTransportQuality();

            if (State == SessionState.Hosting || State == SessionState.Connected)
                PingLoop();

            if (WinterMPPlugin.DevKeysEnabled.Value)
            {
                if (State == SessionState.Idle && UnityEngine.Input.GetKeyDown(KeyCode.F8))
                    StartDevLoopback();
                else if ((State == SessionState.Idle || State == SessionState.Failed)
                         && UnityEngine.Input.GetKeyDown(KeyCode.F10))
                {
                    if (State == SessionState.Failed)
                        SetState(SessionState.Idle, "Idle");
                    WinterMPPlugin.Log.LogInfo("F10 pressed — hosting a real Steam lobby.");
                    StartHost();
                }
            }
        }

        private void RecoverFromFailed()
        {
            if (State != SessionState.Failed || _failedAt < 0f) return;
            if (Time.unscaledTime - _failedAt < FailedRecoverySeconds) return;

            SetState(SessionState.Idle, "Idle — join via Steam or press F10 to host");
            AddChatLine("* Session reset — try joining again.");
        }

        private void OnDestroy()
        {
            Shutdown("Game closed.");
            if (Instance == this) Instance = null;
        }

        private void PingLoop()
        {
            if (Time.unscaledTime < _nextPingAt) return;
            _nextPingAt = Time.unscaledTime + PingIntervalSeconds;

            var ping = new PingMessage
            {
                Nonce = ++_pingNonce,
                SenderTimeMs = (long)(Time.unscaledTime * 1000f),
            };
            _pendingPings[ping.Nonce] = Time.unscaledTime;

            foreach (var peer in _playersByPeer.Keys)
                SendTo(peer, ping, Channel.ReliableOrdered);
        }

        // ---------------------------------------------------------------- transport events

        private void OnPeerConnected(PeerId peer)
        {
            try
            {
                WinterMPPlugin.Log.LogInfo($"Peer connected: {peer}");

                if (!IsHost)
                {
                    _hostPeer = peer;
                    SyncCatalog.EnsureLoaded();

                    // We just reached the host: introduce ourselves.
                    var request = new HandshakeRequest
                    {
                        ProtocolVersion = ProtocolInfo.Version,
                        ModVersion = MyPluginInfo.PLUGIN_VERSION,
                        GameVersion = Util.SafeApp.GameVersion,
                        CatalogHash = SyncCatalog.Hash,
                        PlayerName = LocalPlayerName,
                    };
                    SendTo(peer, request, Channel.ReliableOrdered);
                }
                // Host: wait for the peer's HandshakeRequest before treating it as a player.
            }
            catch (Exception e)
            {
                // Crash containment: a transport-event handler fault must not escape into the game loop.
                WinterMPPlugin.Log.LogError($"OnPeerConnected({peer}) failed: {e}");
            }
        }

        private void OnPeerDisconnected(PeerId peer, string reason)
        {
            try
            {
                if (_playersByPeer.TryGetValue(peer, out var player))
                {
                    if (IsHost && player.SteamId != 0 && player.LastTransformTime > 0f)
                    {
                        GuestProfileStore.Remember(
                            player.SteamId,
                            player.Position.ToNet(),
                            player.Rotation.ToNet());

                        if (_guestSlotsBySteam.TryGetValue(player.SteamId, out GuestSlot slot))
                            slot.Disconnected = true;
                    }

                    _playersByPeer.Remove(peer);
                    if (IsHost)
                        _passengerOccupancy.Remove(player.PlayerId);
                    AddChatLine($"* {player.Name} left ({reason})");
                    PlayerLeft?.Invoke(player);

                    if (IsHost)
                        Broadcast(new PlayerDespawn { PlayerId = player.PlayerId, Reason = reason }, Channel.ReliableOrdered);
                }

                // Only set the failure once: an in-band DisconnectMessage delivers the precise host
                // reason first, then the transport-level disconnect fires again — don't clobber it
                // with the generic "Lost connection to host."
                if (!IsHost && State != SessionState.Failed)
                {
                    string guestMessage = FormatGuestDisconnectReason(reason);
                    SetState(SessionState.Failed, guestMessage);
                }
            }
            catch (Exception e)
            {
                // Crash containment: a transport-event handler fault must not escape into the game loop.
                WinterMPPlugin.Log.LogError($"OnPeerDisconnected({peer}) failed: {e}");
            }
        }

        private static string FormatGuestDisconnectReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return "Lost connection to host.";

            string lower = reason.ToLowerInvariant();
            if (lower.Contains("game closed")
                || lower.Contains("shutdown")
                || lower.Contains("host ended")
                || lower.Contains("idle"))
            {
                return "Host ended the session.";
            }

            return "Lost connection to host: " + reason;
        }

        /// <summary>
        /// Host-side: resolve the assigned player id for an authenticated peer. Used to bind
        /// guest-authored messages to their sender so a guest cannot act for another player id.
        /// </summary>
        internal bool TryGetPlayerId(PeerId peer, out byte playerId)
        {
            if (_playersByPeer.TryGetValue(peer, out var player))
            {
                playerId = player.PlayerId;
                return true;
            }

            playerId = 0;
            return false;
        }

        private void OnPacketReceived(PeerId peer, byte[] payload, Channel channel)
        {
            NetTrafficMeter.Instance.RecordReceived(payload.Length);

            IMessage message;
            try
            {
                message = PacketCodec.Decode(payload);
            }
            catch (ProtocolException e)
            {
                WinterMPPlugin.Log.LogWarning($"Dropped malformed packet from {peer}: {e.Message}");
                return;
            }

            try
            {
                HandleMessage(peer, message);
            }
            catch (Exception e)
            {
                // Crash containment: a bug in one handler must not take down the game loop.
                WinterMPPlugin.Log.LogError($"Error handling {message.Id} from {peer}: {e}");
            }
        }

        // ---------------------------------------------------------------- message handling

        private void HandleMessage(PeerId peer, IMessage message)
        {
            switch (message)
            {
                case HandshakeRequest request when IsHost:
                    HandleHandshakeRequest(peer, request);
                    break;

                case HandshakeResponse response when !IsHost:
                    HandleHandshakeResponse(peer, response);
                    break;

                case ChatMessage chat:
                    HandleChat(peer, chat);
                    break;

                case PingMessage ping:
                    SendTo(peer, new PongMessage { Nonce = ping.Nonce, SenderTimeMs = ping.SenderTimeMs }, Channel.ReliableOrdered);
                    break;

                case PongMessage pong:
                    if (_pendingPings.TryGetValue(pong.Nonce, out float sentAt))
                    {
                        _pendingPings.Remove(pong.Nonce);
                        if (_playersByPeer.TryGetValue(peer, out var pingedPlayer))
                            pingedPlayer.PingMs = (int)((Time.unscaledTime - sentAt) * 1000f);
                    }
                    break;

                case PlayerSpawn spawn when !IsHost:
                    if (spawn.PlayerId != LocalPlayerId)
                    {
                        var remote = new RemotePlayer
                        {
                            PlayerId = spawn.PlayerId,
                            Peer = peer, // clients only talk to the host; host relays
                            SteamId = spawn.SteamId,
                            Name = spawn.Name,
                        };
                        _playersByPeer[new PeerId(spawn.SteamId != 0 ? spawn.SteamId : spawn.PlayerId)] = remote;
                        AddChatLine($"* {remote.Name} joined");
                        PlayerJoined?.Invoke(remote);
                    }
                    break;

                case PlayerDespawn despawn when !IsHost:
                    RemovePlayerById(despawn.PlayerId, despawn.Reason);
                    break;

                case PlayerTransform transform:
                    if (!HasValidPlayerTransform(transform))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped malformed PlayerTransform from {peer} for player {transform.PlayerId}.");
                        break;
                    }
                    if (IsHost && !IsPeerPlayer(peer, transform.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PlayerTransform claiming player {transform.PlayerId} from {peer}.");
                        break;
                    }
                    HandlePlayerTransform(peer, transform);
                    break;

                case PassengerState passengerState:
                    if (IsHost && !IsPeerPlayer(peer, passengerState.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PassengerState claiming player {passengerState.PlayerId} from {peer}.");
                        break;
                    }
                    if (IsHost && !TryAcceptGuestPassengerState(peer, passengerState))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped invalid or stale PassengerState from player {passengerState.PlayerId}.");
                        SendTo(peer, new PassengerState
                        {
                            PlayerId = passengerState.PlayerId,
                            VehicleId = 0,
                            SeatIndex = PassengerState.SeatNone,
                            Sequence = passengerState.Sequence,
                        }, Channel.ReliableOrdered);
                        break;
                    }
                    RecordPassengerState(passengerState);
                    Sync.PassengerController.Instance?.OnRemotePassengerState(passengerState);
                    if (IsHost)
                        Broadcast(passengerState, Channel.ReliableOrdered, except: peer);
                    break;

                case GuestSpawn guestSpawn when !IsHost:
                    Sync.PlayerSyncManager.Instance?.OnGuestSpawn(guestSpawn);
                    break;

                case PlayerNeedsReport needsReport when IsHost:
                    if (!IsPeerPlayer(peer, needsReport.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PlayerNeedsReport claiming player {needsReport.PlayerId} from {peer}.");
                        break;
                    }
                    if (!HandlePlayerNeedsReport(needsReport))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped invalid or stale PlayerNeedsReport from player {needsReport.PlayerId}.");
                    }
                    break;

                case SleepConsentRequest sleepRequest when !IsHost:
                    Sync.SleepConsentManager.Instance?.OnGuestRequest(sleepRequest);
                    break;

                case SleepConsentResponse sleepResponse when IsHost:
                    if (!IsPeerPlayer(peer, sleepResponse.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped SleepConsentResponse claiming player {sleepResponse.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.SleepConsentManager.Instance?.OnRemoteResponse(sleepResponse);
                    break;

                case SleepConsentResult sleepResult when !IsHost:
                    Sync.SleepConsentManager.Instance?.OnGuestResult(sleepResult);
                    break;

                case PlayerDeathReport deathReport when IsHost:
                    // Host-authority: a guest may only report its OWN death. Bind the report to the
                    // authenticated sender so a spoofed PlayerId (e.g. 0/host, or another player)
                    // cannot drive a permadeath wipe of someone who never died. Drop if unknown.
                    if (TryGetPlayerId(peer, out byte deathReporterId))
                    {
                        deathReport.PlayerId = deathReporterId;
                        Sync.DeathSyncManager.Instance?.OnRemoteDeathReport(deathReport);
                    }
                    break;

                case PlayerDeathEvent deathEvent when !IsHost:
                    Sync.DeathSyncManager.Instance?.OnRemoteDeathEvent(deathEvent);
                    break;

                case PlayerRespawn respawn when IsHost:
                    // Bind respawn to its sender: a guest may only respawn itself.
                    if (TryGetPlayerId(peer, out byte respawnerId))
                    {
                        respawn.PlayerId = respawnerId;
                        Sync.DeathSyncManager.Instance?.OnRemoteRespawn(respawn);
                        Broadcast(respawn, Channel.ReliableOrdered, except: peer);
                    }
                    break;

                case PlayerRespawn respawn when !IsHost:
                    Sync.DeathSyncManager.Instance?.OnRemoteRespawn(respawn);
                    break;

                case PlayerClothingState clothingState:
                    if (IsHost && !IsPeerPlayer(peer, clothingState.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PlayerClothingState claiming player {clothingState.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnRemoteClothingState(clothingState);
                    if (IsHost)
                        Broadcast(clothingState, Channel.ReliableOrdered, except: peer);
                    break;

                case FsmStateEnter stateEnter when IsHost:
                    if (TryGetPlayerId(peer, out byte fsmPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestStateEnter(stateEnter, fsmPlayerId) == true)
                        Broadcast(stateEnter, Channel.ReliableOrdered, except: peer);
                    break;

                case FsmStateEnter stateEnter:
                    Sync.WorldSyncManager.Instance?.OnRemoteStateEnter(stateEnter);
                    break;

                case FsmRawEvent rawEvent when IsHost:
                    if (TryGetPlayerId(peer, out byte rawEventPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestRawEvent(rawEvent, rawEventPlayerId) == true)
                        Broadcast(rawEvent, Channel.ReliableOrdered, except: peer);
                    break;

                case FsmRawEvent rawEvent:
                    Sync.WorldSyncManager.Instance?.OnRemoteRawEvent(rawEvent);
                    break;

                case BoltState boltState when IsHost:
                    if (TryGetPlayerId(peer, out byte boltPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestBoltState(boltState, boltPlayerId) == true)
                        Broadcast(boltState, Channel.ReliableOrdered, except: peer);
                    break;

                case BoltState boltState:
                    Sync.WorldSyncManager.Instance?.OnRemoteBoltState(boltState);
                    break;

                case PartState partState when IsHost:
                    if (TryGetPlayerId(peer, out byte partPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestPartState(partState, partPlayerId) == true)
                        Broadcast(partState, Channel.ReliableOrdered, except: peer);
                    break;

                case PartState partState:
                    Sync.WorldSyncManager.Instance?.OnRemotePartState(partState);
                    break;

                case ItemDespawn itemDespawn when IsHost:
                    if (TryGetPlayerId(peer, out byte despawnPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestItemDespawn(itemDespawn, despawnPlayerId) == true)
                        Broadcast(itemDespawn, Channel.ReliableOrdered, except: peer);
                    break;

                case ItemDespawn itemDespawn:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemDespawn(itemDespawn);
                    break;

                case ItemSpawn itemSpawn when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemSpawn(itemSpawn);
                    break;

                case SpawnIntent spawnIntent when IsHost:
                    if (!IsPeerPlayer(peer, spawnIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped SpawnIntent claiming player {spawnIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostGuestSpawnIntent(spawnIntent, spawnIntent.PlayerId);
                    break;

                case ItemTransform itemTransform when IsHost:
                    if (!IsPeerPlayer(peer, itemTransform.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped ItemTransform claiming player {itemTransform.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestItemTransform(itemTransform, itemTransform.OwnerPlayerId) == true)
                        Broadcast(itemTransform,
                            ItemTransformPolicy.SelectSendChannel(itemTransform.IsFinal, itemTransform.IsVehicle),
                            except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped unauthorized ItemTransform {itemTransform.ItemId:X8} from player {itemTransform.OwnerPlayerId}.");
                    break;

                case ItemTransform itemTransform:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemTransform(itemTransform);
                    break;

                case NpcTransform npcTransform when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteNpcTransform(npcTransform);
                    break;

                case VehicleState vehicleState when IsHost:
                    if (!IsPeerPlayer(peer, vehicleState.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleState claiming player {vehicleState.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleState(vehicleState, vehicleState.OwnerPlayerId) == true)
                        Broadcast(vehicleState, Channel.UnreliableSequenced, except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning($"Dropped unauthorized VehicleState {vehicleState.VehicleId:X8}.");
                    break;

                case VehicleState vehicleState:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleState(vehicleState);
                    break;

                case VehicleClimate vehicleClimate when IsHost:
                    if (!IsPeerPlayer(peer, vehicleClimate.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleClimate claiming player {vehicleClimate.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleClimate(vehicleClimate, vehicleClimate.OwnerPlayerId) == true)
                        Broadcast(vehicleClimate, Channel.UnreliableSequenced, except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning($"Dropped unauthorized VehicleClimate {vehicleClimate.VehicleId:X8}.");
                    break;

                case VehicleClimate vehicleClimate:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleClimate(vehicleClimate);
                    break;

                case VehicleCargo vehicleCargo when IsHost:
                    if (!IsPeerPlayer(peer, vehicleCargo.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleCargo claiming player {vehicleCargo.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleCargo(vehicleCargo, vehicleCargo.OwnerPlayerId) == true)
                    {
                        // Empty-set transitions ride the reliable channel end to end —
                        // losing one on relay would strand pinned cargo on other guests.
                        Broadcast(vehicleCargo,
                            vehicleCargo.Entries.Length == 0
                                ? Channel.ReliableOrdered
                                : Channel.UnreliableSequenced,
                            except: peer);
                    }
                    break;

                case VehicleCargo vehicleCargo:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleCargo(vehicleCargo);
                    break;

                case VehicleFuelIntent fuelIntent when IsHost:
                    if (!IsPeerPlayer(peer, fuelIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleFuelIntent claiming player {fuelIntent.PlayerId} from {peer}.");
                        break;
                    }

                    if (Sync.WorldSyncManager.Instance?.OnHostVehicleFuelIntent(fuelIntent, out var fuelState) == true)
                        Broadcast(fuelState, Channel.ReliableOrdered);
                    break;

                case TimeSync timeSync when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteTimeSync(timeSync);
                    break;

                case WalletState walletState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWalletState(walletState);
                    break;

                case PurchaseIntent purchaseIntent when IsHost:
                    if (!IsPeerPlayer(peer, purchaseIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PurchaseIntent claiming player {purchaseIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostGuestPurchaseIntent(purchaseIntent, purchaseIntent.PlayerId);
                    break;

                case PoliceIntent policeIntent when IsHost:
                    if (!IsPeerPlayer(peer, policeIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PoliceIntent claiming player {policeIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostPoliceIntent(policeIntent, out var policeState) == true)
                        Broadcast(policeState, Channel.ReliableOrdered);
                    break;

                case HeatSourceState heatState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteHeatSourceState(heatState);
                    break;

                case HeatSourceIntent heatIntent when IsHost:
                    if (!IsPeerPlayer(peer, heatIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped HeatSourceIntent claiming player {heatIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostHeatSourceIntent(heatIntent);
                    break;

                case FluidContainerState fluidState when IsHost:
                    if (!IsPeerPlayer(peer, fluidState.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped FluidContainerState claiming player {fluidState.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestFluidContainerState(
                            fluidState, fluidState.OwnerPlayerId) == true)
                    {
                        Broadcast(fluidState, Channel.ReliableOrdered, except: peer);
                    }
                    else
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped unauthorized FluidContainerState {fluidState.ItemId:X8} from player {fluidState.OwnerPlayerId}.");
                    }
                    break;

                case FluidContainerState fluidState:
                    Sync.WorldSyncManager.Instance?.OnRemoteFluidContainerState(fluidState);
                    break;

                case WorldProgressState progressState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWorldProgressState(progressState);
                    break;

                case JobSiteState jobSiteState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteJobSiteState(jobSiteState);
                    break;

                case MailOrderState mailOrderState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteMailOrderState(mailOrderState);
                    break;

                case MailOrderIntent mailOrderIntent when IsHost:
                    if (!IsPeerPlayer(peer, mailOrderIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped MailOrderIntent claiming player {mailOrderIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostMailOrderIntent(mailOrderIntent);
                    break;

                case InspectionState inspectionState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteInspectionState(inspectionState);
                    break;

                case PoliceState remotePoliceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePoliceState(remotePoliceState);
                    break;

                case HomeStereoIntent homeStereoIntent when IsHost:
                    if (!IsPeerPlayer(peer, homeStereoIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped HomeStereoIntent claiming player {homeStereoIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostHomeStereoIntent(homeStereoIntent, out var homeStereoState) == true)
                        Broadcast(homeStereoState, Channel.ReliableOrdered);
                    break;

                case HomeStereoState remoteHomeStereoState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteHomeStereoState(remoteHomeStereoState);
                    break;

                case RallyIntent rallyIntent when IsHost:
                    if (!IsPeerPlayer(peer, rallyIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped RallyIntent claiming player {rallyIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostRallyIntent(rallyIntent, out var rallyState) == true)
                        Broadcast(rallyState, Channel.ReliableOrdered);
                    break;

                case RallyState remoteRallyState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteRallyState(remoteRallyState);
                    break;

                case IceRaceIntent iceRaceIntent when IsHost:
                    if (!IsPeerPlayer(peer, iceRaceIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped IceRaceIntent claiming player {iceRaceIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostIceRaceIntent(iceRaceIntent, out var iceRaceState) == true)
                        Broadcast(iceRaceState, Channel.ReliableOrdered);
                    break;

                case IceRaceState remoteIceRaceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceState(remoteIceRaceState);
                    break;

                case IceRaceEventState iceRaceEventState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceEventState(iceRaceEventState);
                    break;

                case IceRaceResultsState iceRaceResultsState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceResultsState(iceRaceResultsState);
                    break;

                case WorldSnapshotRequest snapshotRequest when IsHost:
                    HandleSnapshotRequest(peer, snapshotRequest);
                    break;

                case WorldStateChecksum checksum when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteStateChecksum(checksum);
                    break;

                case WorldResyncRequest resync when IsHost:
                    HandleResyncRequest(peer, resync);
                    break;

                case WorldObjectStateRequest objectRequest when IsHost:
                    HandleObjectStateRequest(peer, objectRequest);
                    break;

                case WorldDoorSnapshot doorSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteDoorSnapshot(doorSnapshot);
                    break;

                case WorldItemSnapshot itemSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemSnapshot(itemSnapshot);
                    break;

                case WorldBoltSnapshot boltSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteBoltSnapshot(boltSnapshot);
                    break;

                case WorldPartSnapshot partSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePartSnapshot(partSnapshot);
                    break;

                case WorldItemDespawnSnapshot despawnSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemDespawnSnapshot(despawnSnapshot);
                    break;

                case DisconnectMessage disconnect:
                    OnPeerDisconnected(peer, disconnect.Reason);
                    break;

                default:
                    WinterMPPlugin.Log.LogDebug($"Unhandled message {message.Id} from {peer}.");
                    break;
            }
        }

        private void HandleHandshakeRequest(PeerId peer, HandshakeRequest request)
        {
            SyncCatalog.EnsureLoaded();

            string? refusal = null;
            string hostGameVersion = Util.SafeApp.GameVersion;
            if (request.ProtocolVersion != ProtocolInfo.Version)
                refusal = $"Protocol mismatch (host v{ProtocolInfo.Version}, you v{request.ProtocolVersion}). Update {MyPluginInfo.PLUGIN_NAME}.";
            else if (request.ModVersion != MyPluginInfo.PLUGIN_VERSION)
                refusal = $"Mod version mismatch (host {MyPluginInfo.PLUGIN_VERSION}, you {request.ModVersion}).";
            else if (request.GameVersion != hostGameVersion)
                refusal = $"Game version mismatch (host {hostGameVersion}, you {request.GameVersion}).";
            else if (SyncCatalog.Loaded && request.CatalogHash != SyncCatalog.Hash)
                refusal = $"Sync catalog mismatch (host {SyncCatalog.Hash:X8}, you {request.CatalogHash:X8}). Reinstall {MyPluginInfo.PLUGIN_NAME}.";

            if (refusal != null)
            {
                WinterMPPlugin.Log.LogWarning($"Refused {request.PlayerName} ({peer}): {refusal}");
                SendTo(peer, new HandshakeResponse { Accepted = false, Reason = refusal }, Channel.ReliableOrdered);
                return;
            }

            var player = new RemotePlayer
            {
                PlayerId = AssignPlayerId(peer.Value, out bool reconnecting),
                Peer = peer,
                SteamId = peer.Value,
                Name = request.PlayerName,
                ReturningGuest = GuestProfileStore.TryGet(peer.Value, out _, out _),
            };

            SendTo(peer, new HandshakeResponse
            {
                Accepted = true,
                PlayerId = player.PlayerId,
                HostPlayerName = LocalPlayerName,
                SessionFlags = BuildSessionFlags(),
            }, Channel.ReliableOrdered);

            // Introduce existing players to the newcomer...
            foreach (var existing in _playersByPeer.Values)
            {
                SendTo(peer, new PlayerSpawn
                {
                    PlayerId = existing.PlayerId,
                    SteamId = existing.SteamId,
                    Name = existing.Name,
                }, Channel.ReliableOrdered);
            }

            // ...then the newcomer to everyone (including itself is harmless; clients filter their own id).
            _playersByPeer[peer] = player;
            Broadcast(new PlayerSpawn
            {
                PlayerId = player.PlayerId,
                SteamId = player.SteamId,
                Name = player.Name,
            }, Channel.ReliableOrdered);

            AddChatLine(reconnecting
                ? $"* {player.Name} reconnected"
                : $"* {player.Name} joined");
            PlayerJoined?.Invoke(player);

            if (IsHost && PlayerCount == 1)
                StatusText = $"{player.Name} joined — click Continue";

            // World snapshot is request-driven: the guest asks once its own world
            // scan completes (see WorldSnapshotRequest), not at handshake time —
            // at this point it is typically still in the main menu.
        }

        private void HandleSnapshotRequest(PeerId peer, WorldSnapshotRequest request)
        {
            var world = Sync.WorldSyncManager.Instance;
            if (world == null) return;

            if (request.IdHash != world.IdHash)
                WinterMPPlugin.Log.LogWarning(
                    $"Snapshot request from {peer}: id hash {request.IdHash:X8} != ours {world.IdHash:X8} " +
                    "(usually transient — scans grow as the world streams in).");

            int messages = 0;
            foreach (var chunk in world.BuildWorldSnapshot())
            {
                SendTo(peer, chunk, Channel.ReliableOrdered);
                messages++;
            }

            var time = world.BuildTimeSync();
            if (time != null)
            {
                SendTo(peer, time, Channel.ReliableOrdered);
                messages++;
            }

            var wallet = world.BuildWalletState();
            if (wallet != null)
            {
                SendTo(peer, wallet, Channel.ReliableOrdered);
                messages++;
            }

            foreach (var occupancy in _passengerOccupancy.Values)
            {
                SendTo(peer, occupancy, Channel.ReliableOrdered);
                messages++;
            }

            if (_playersByPeer.TryGetValue(peer, out var joining))
            {
                SendTo(peer, BuildGuestSpawn(joining), Channel.ReliableOrdered);
                messages++;

                // Clothing is change-only and not in the chunked snapshot; send the joiner
                // every other player's current outfit so already-dressed players don't render
                // in default clothing (wrong warmth tier + visual) until each next changes.
                foreach (var clothing in world.BuildClothingSnapshot(LocalPlayerId, joining.PlayerId))
                {
                    SendTo(peer, clothing, Channel.ReliableOrdered);
                    messages++;
                }
            }

            // Heat sources ride the periodic HeatSourceState stream, not snapshot chunks;
            // force a full re-broadcast so the joiner isn't cold until the 20 s keepalive.
            world.ForceHeatSourceBroadcast();

            WinterMPPlugin.Log.LogInfo($"Sent world snapshot to {peer} ({messages} messages).");
        }

        private byte AssignPlayerId(ulong steamId, out bool reconnecting)
        {
            reconnecting = false;
            if (steamId == 0)
                return AllocateFreshPlayerId();

            if (_guestSlotsBySteam.TryGetValue(steamId, out GuestSlot slot))
            {
                reconnecting = slot.Disconnected;
                slot.Disconnected = false;
                return slot.PlayerId;
            }

            byte id = AllocateFreshPlayerId();
            _guestSlotsBySteam[steamId] = new GuestSlot { PlayerId = id, Disconnected = false };
            return id;
        }

        private byte AllocateFreshPlayerId()
        {
            byte id = _nextPlayerId++;
            if (id == 0) id = _nextPlayerId++;
            return id;
        }

        private static GuestSpawn BuildGuestSpawn(RemotePlayer guest)
        {
            var offer = new GuestSpawn();

            if (TryReadHostFeet(out Vector3 hostFeet, out Quaternion hostRot))
            {
                offer.HostPosition = hostFeet.ToNet();
                offer.HostRotation = hostRot.ToNet();
            }

            if (guest.ReturningGuest
                && GuestProfileStore.TryGet(guest.SteamId, out NetVector3 lastPos, out NetQuaternion lastRot))
            {
                offer.LastPosition = lastPos;
                offer.LastRotation = lastRot;
                offer.Flags |= GuestSpawn.FlagHasLastPosition;
            }

            if (guest.ReturningGuest
                && GuestProfileStore.TryGetNeeds(guest.SteamId, out GuestProfileStore.NeedsSnapshot needs))
            {
                offer.Hunger = needs.Hunger;
                offer.Fatigue = needs.Fatigue;
                offer.Thirst = needs.Thirst;
                offer.Urine = needs.Urine;
                offer.BodyTemp = needs.BodyTemp;
                offer.Stress = needs.Stress;
                offer.Drunk = needs.Drunk;
                offer.Flags |= GuestSpawn.FlagHasSavedNeeds;
            }

            return offer;
        }

        private bool HandlePlayerNeedsReport(PlayerNeedsReport report)
        {
            if (!HasFiniteNeeds(report)) return false;

            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId != report.PlayerId || player.SteamId == 0) continue;

                if (player.HasNeedsReport)
                {
                    ushort difference = (ushort)(report.Sequence - player.LastNeedsSequence);
                    if (difference == 0 || difference > short.MaxValue) return false;
                }
                player.LastNeedsSequence = report.Sequence;
                player.HasNeedsReport = true;

                GuestProfileStore.RememberNeeds(player.SteamId, new GuestProfileStore.NeedsSnapshot
                {
                    Hunger = report.Hunger,
                    Fatigue = report.Fatigue,
                    Thirst = report.Thirst,
                    Urine = report.Urine,
                    BodyTemp = report.BodyTemp,
                    Stress = report.Stress,
                    Drunk = report.Drunk,
                    Valid = true,
                });
                return true;
            }
            return false;
        }

        private static bool HasFiniteNeeds(PlayerNeedsReport report)
        {
            return IsFinite(report.Hunger) && IsFinite(report.Fatigue) && IsFinite(report.Thirst)
                && IsFinite(report.Urine) && IsFinite(report.BodyTemp) && IsFinite(report.Stress)
                && IsFinite(report.Drunk);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryReadHostFeet(out Vector3 feet, out Quaternion lookRotation)
        {
            feet = Vector3.zero;
            lookRotation = Quaternion.identity;

            var playerObject = GameObject.Find("PLAYER");
            if (playerObject == null) return false;

            var player = playerObject.transform;
            var controller = playerObject.GetComponent<CharacterController>();
            feet = Sync.PlayerPoseReader.ReadFeetPosition(player, controller);
            lookRotation = Sync.PlayerPoseReader.ReadLookRotation(player);
            return true;
        }

        private void HandleResyncRequest(PeerId peer, WorldResyncRequest request)
        {
            var world = Sync.WorldSyncManager.Instance;
            if (world == null) return;

            int messages = 0;
            foreach (var chunk in world.BuildResyncMessages(request.Flags))
            {
                SendTo(peer, chunk, Channel.ReliableOrdered);
                messages++;
            }

            WinterMPPlugin.Log.LogInfo(
                $"Soft resync to {peer} for checksum seq {request.ChecksumSequence} flags 0x{request.Flags:X2} ({messages} messages).");
        }

        private void HandleObjectStateRequest(PeerId peer, WorldObjectStateRequest request)
        {
            var world = Sync.WorldSyncManager.Instance;
            if (world == null) return;

            int messages = 0;
            foreach (var message in world.BuildObjectStateMessages(request.NetId))
            {
                Channel channel = message switch
                {
                    ItemTransform t => ItemTransformPolicy.SelectSendChannel(t.IsFinal, t.IsVehicle),
                    VehicleState => Channel.ReliableOrdered,
                    VehicleClimate => Channel.ReliableOrdered,
                    _ => Channel.ReliableOrdered,
                };
                SendTo(peer, message, channel);
                messages++;
            }

            if (messages > 0)
                WinterMPPlugin.Log.LogInfo($"Object state for {request.NetId:X8} -> {peer} ({messages} messages).");
        }

        private void HandleHandshakeResponse(PeerId peer, HandshakeResponse response)
        {
            if (!response.Accepted)
            {
                string reason = response.Reason ?? "unknown reason";
                AddChatLine($"* Join refused: {reason}");
                SetState(SessionState.Failed, $"Join refused: {reason}");
                WinterMPPlugin.Log.LogWarning($"Join refused by host: {reason}");
                Shutdown("Refused by host.");
                return;
            }

            LocalPlayerId = response.PlayerId;
            SetPermanentDeathEnabled((response.SessionFlags & SessionFlags.PermadeathEnabled) != 0);
            _playersByPeer[peer] = new RemotePlayer
            {
                PlayerId = 0,
                Peer = peer,
                SteamId = peer.Value,
                Name = response.HostPlayerName,
            };
            SetState(SessionState.Connected, $"Connected to {response.HostPlayerName}");
            AddChatLine($"* Connected to {response.HostPlayerName}'s game");
            if (PermanentDeathEnabled)
                AddChatLine("* Host session: PERMADEATH (one death ends it for everyone)");
        }

        private void HandleChat(PeerId peer, ChatMessage chat)
        {
            string sender = ResolveName(chat.SenderPlayerId);
            AddChatLine($"{sender}: {chat.Text}");

            // Host relays guest chat to all other guests.
            if (IsHost)
                Broadcast(chat, Channel.ReliableOrdered, except: peer);
        }

        private void HandlePlayerTransform(PeerId peer, PlayerTransform transform)
        {
            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId != transform.PlayerId) continue;

                // Drop stale unreliable packets (sequence wrap-aware). Always accept the
                // first pose so avatars appear even after a sender sequence reset.
                if (player.LastTransformTime > 0f)
                {
                    ushort diff = (ushort)(transform.Sequence - player.LastTransformSequence);
                    if (diff == 0 || diff > short.MaxValue) return;
                }

                player.LastTransformSequence = transform.Sequence;
                player.Position = transform.Position.ToUnity();
                player.Rotation = transform.Rotation.ToUnity();
                player.MoveState = transform.MoveState;
                player.LastTransformTime = Time.unscaledTime;

                if (IsHost && player.SteamId != 0)
                    GuestProfileStore.Remember(player.SteamId, transform.Position, transform.Rotation);
                break;
            }

            // Host relays transforms so all guests see all players.
            if (IsHost)
                Broadcast(transform, Channel.UnreliableSequenced, except: peer);
        }

        /// <summary>
        /// Player poses are presentation data, but the host also uses a fresh pose
        /// as the physical-presence proof for validated intents. Reject malformed
        /// values before they can poison remote transforms, sidecar saves, or an
        /// intent's proximity comparison. The normal local sender always produces a
        /// unit quaternion and only defines the seven documented animation bits.
        /// </summary>
        private static bool HasValidPlayerTransform(PlayerTransform transform)
        {
            const byte KnownMoveStateFlags = Sync.PlayerMoveState.Walking
                | Sync.PlayerMoveState.Running
                | Sync.PlayerMoveState.Crouch
                | Sync.PlayerMoveState.Carry
                | Sync.PlayerMoveState.Driving
                | Sync.PlayerMoveState.Passenger
                | Sync.PlayerMoveState.Swimming;
            const float MaxCoordinate = 100000f;

            if (!IsFinite(transform.Position.X) || !IsFinite(transform.Position.Y) || !IsFinite(transform.Position.Z)
                || !IsFinite(transform.Rotation.X) || !IsFinite(transform.Rotation.Y)
                || !IsFinite(transform.Rotation.Z) || !IsFinite(transform.Rotation.W)
                || Mathf.Abs(transform.Position.X) > MaxCoordinate || Mathf.Abs(transform.Position.Y) > MaxCoordinate
                || Mathf.Abs(transform.Position.Z) > MaxCoordinate || (transform.MoveState & ~KnownMoveStateFlags) != 0)
            {
                return false;
            }

            float rotationLengthSquared = transform.Rotation.X * transform.Rotation.X
                + transform.Rotation.Y * transform.Rotation.Y
                + transform.Rotation.Z * transform.Rotation.Z
                + transform.Rotation.W * transform.Rotation.W;
            return rotationLengthSquared >= 0.25f && rotationLengthSquared <= 2.25f;
        }

        /// <summary>Host boundary for a guest's cosmetic passenger-seat claim. The
        /// player identity/sequence, fresh pose, exact registered seat, and existing
        /// occupancy are all checked before any avatar or snapshot state changes.</summary>
        private bool TryAcceptGuestPassengerState(PeerId peer, PassengerState state)
        {
            if (!_playersByPeer.TryGetValue(peer, out var player) || player.PlayerId != state.PlayerId)
                return false;

            if (player.HasPassengerState)
            {
                ushort difference = (ushort)(state.Sequence - player.LastPassengerSequence);
                if (difference == 0 || difference > short.MaxValue) return false;
            }

            var passengers = Sync.PassengerController.Instance;
            if (passengers == null || !passengers.TryValidateGuestPassengerState(state, player))
                return false;

            if (state.IsSeated && !TryResolvePassengerSeatClaim(state))
                return false;

            player.LastPassengerSequence = state.Sequence;
            player.HasPassengerState = true;
            return true;
        }

        /// <summary>Seat races are settled by player id on the host, not packet arrival
        /// order. A lower id replacing a higher occupant is safe: its relayed claim makes
        /// the displaced guest's local controller exit; a higher id is corrected directly.</summary>
        private bool TryResolvePassengerSeatClaim(PassengerState state)
        {
            byte occupantId = 0;
            bool occupied = false;
            foreach (var occupancy in _passengerOccupancy)
            {
                if (occupancy.Key != state.PlayerId && occupancy.Value.IsSeated
                    && occupancy.Value.VehicleId == state.VehicleId
                    && occupancy.Value.SeatIndex == state.SeatIndex)
                {
                    occupantId = occupancy.Key;
                    occupied = true;
                    break;
                }
            }

            if (!occupied) return true;
            if (occupantId < state.PlayerId) return false;

            _passengerOccupancy.Remove(occupantId);
            return true;
        }

        /// <summary>Guest-originated owner fields must match the authenticated peer.</summary>
        private bool IsPeerPlayer(PeerId peer, byte playerId)
        {
            return _playersByPeer.TryGetValue(peer, out var player) && player.PlayerId == playerId;
        }

        // ---------------------------------------------------------------- send helpers

        /// <summary>
        /// Pose stream entry point used by <see cref="Sync.PlayerSyncManager"/>.
        /// Host broadcasts to all guests; a guest sends only to the host, which
        /// relays (we run star topology, not a P2P mesh — see PLAN §3.1).
        /// </summary>
        public void SendPlayerTransform(PlayerTransform transform)
        {
            if (IsHost)
                Broadcast(transform, Channel.UnreliableSequenced);
            else if (_hostPeer.HasValue)
                SendTo(_hostPeer.Value, transform, Channel.UnreliableSequenced);
        }

        /// <summary>
        /// World-sync entry point (doors, bolts, items — see <see cref="Sync.WorldSyncManager"/>).
        /// Star topology: the host broadcasts, guests send to the host which relays.
        /// </summary>
        public void SendWorldMessage(IMessage message, Channel channel)
        {
            if (State != SessionState.Hosting && State != SessionState.Connected) return;

            if (IsHost)
                Broadcast(message, channel);
            else if (_hostPeer.HasValue)
                SendTo(_hostPeer.Value, message, channel);
        }

        /// <summary>Guest profile messages (needs reports, sleep consent answers).</summary>
        public void SendPlayerProfileMessage(IMessage message)
        {
            if (State != SessionState.Hosting && State != SessionState.Connected) return;

            if (IsHost)
                Broadcast(message, Channel.ReliableOrdered);
            else if (_hostPeer.HasValue)
                SendTo(_hostPeer.Value, message, Channel.ReliableOrdered);
        }

        /// <summary>Host-only broadcast for sleep consent rounds.</summary>
        public void BroadcastProfileMessage(IMessage message)
        {
            if (!IsHost || (State != SessionState.Hosting && State != SessionState.Connected)) return;
            Broadcast(message, Channel.ReliableOrdered);
        }

        public void AddSystemChat(string line) => AddChatLine(line);

        public string ResolvePlayerName(byte playerId) => ResolveName(playerId);

        public void SetPlayerDead(byte playerId, bool dead)
        {
            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId != playerId) continue;
                player.IsDead = dead;
                return;
            }
        }

        public void ApplyPlayerRespawnPose(byte playerId, Vector3 position, Quaternion rotation)
        {
            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId != playerId) continue;
                player.IsDead = false;
                player.Position = position;
                player.Rotation = rotation;
                player.LastTransformTime = Time.unscaledTime;
                return;
            }
        }

        public void SendChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var chat = new ChatMessage { SenderPlayerId = LocalPlayerId, Text = text };
            AddChatLine($"{LocalPlayerName}: {text}");
            Broadcast(chat, Channel.ReliableOrdered);
        }

        private void SendTo(PeerId peer, IMessage message, Channel channel)
        {
            if (_transport == null) return;
            PacketCodec.Encode(message, _sendWriter);
            var payload = _sendWriter.ToArray();
            NetTrafficMeter.Instance.RecordSent(channel, payload.Length);
            _transport.Send(peer, payload, channel);
        }

        private void Broadcast(IMessage message, Channel channel, PeerId? except = null)
        {
            if (_transport == null) return;
            PacketCodec.Encode(message, _sendWriter);
            var payload = _sendWriter.ToArray();
            foreach (var peer in _playersByPeer.Keys)
            {
                if (except.HasValue && peer == except.Value) continue;
                NetTrafficMeter.Instance.RecordSent(channel, payload.Length);
                _transport.Send(peer, payload, channel);
            }
        }

        // ---------------------------------------------------------------- misc

        private byte BuildSessionFlags()
        {
            byte flags = 0;
            if (IsHost && Sync.PermadeathSettings.TryRead(out bool enabled))
                SetPermanentDeathEnabled(enabled);

            if (PermanentDeathEnabled)
                flags |= SessionFlags.PermadeathEnabled;
            return flags;
        }

        private void RemovePlayerById(byte playerId, string reason)
        {
            PeerId? key = null;
            RemotePlayer? removed = null;
            foreach (var pair in _playersByPeer)
            {
                if (pair.Value.PlayerId != playerId) continue;
                key = pair.Key;
                removed = pair.Value;
                break;
            }

            if (key.HasValue && removed != null)
            {
                _playersByPeer.Remove(key.Value);
                AddChatLine($"* {removed.Name} left ({reason})");
                PlayerLeft?.Invoke(removed);
            }
        }

        private string ResolveName(byte playerId)
        {
            if (playerId == LocalPlayerId) return LocalPlayerName;
            foreach (var player in _playersByPeer.Values)
                if (player.PlayerId == playerId)
                    return player.Name;
            return $"Player {playerId}";
        }

        private void AddChatLine(string line)
        {
            _chatLog.Add(line);
            if (_chatLog.Count > 100)
                _chatLog.RemoveAt(0);
            WinterMPPlugin.Log.LogInfo($"[chat] {line}");
        }

        private void SetState(SessionState state, string status)
        {
            State = state;
            StatusText = status;
            _failedAt = state == SessionState.Failed ? Time.unscaledTime : -1f;
            WinterMPPlugin.Log.LogInfo($"Session: {state} — {status}");
        }
    }
}
