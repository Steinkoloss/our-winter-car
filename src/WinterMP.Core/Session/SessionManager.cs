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
    public sealed partial class SessionManager : MonoBehaviour
    {
        private const float PingIntervalSeconds = 2f;
        private const float SnapshotRequestCooldownSeconds = 10f;
        private const float ResyncRequestCooldownSeconds = 15f;
        private const float ObjectStateRequestCooldownSeconds = 0.25f;

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
        private readonly Dictionary<PeerId, float> _nextSnapshotRequestAt = new Dictionary<PeerId, float>();
        private readonly Dictionary<PeerId, float> _nextResyncRequestAt = new Dictionary<PeerId, float>();
        private readonly Dictionary<PeerId, float> _nextObjectStateRequestAt = new Dictionary<PeerId, float>();
        private bool _failedSessionCleanupPending;

        private readonly PassengerSeatLedger _passengerSeats = new PassengerSeatLedger();

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
        private bool _steamLobbyAttemptActive;
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

            _passengerSeats.Record(state);
        }

        private float _failedAt = -1f;
        private const float FailedRecoverySeconds = 45f;

        public void Shutdown(string reason)
        {
            if (_transport != null)
            {
                var bye = new DisconnectMessage { Reason = reason };
                foreach (var peer in _playersByPeer.Keys)
                    SendTo(peer, bye, Channel.ReliableOrdered);
            }

            _failedSessionCleanupPending = false;
            DisposeSessionTransport();
            ResetSessionRuntimeState();
            SetState(SessionState.Idle, "Idle");
        }

        private void DisposeSessionTransport()
        {
            _devClient?.Dispose();
            _devClient = null;
            _transport?.Dispose();
            _transport = null;
#if STEAMWORKS
            // Lobby creation/join can fail before SteamP2PTransport is constructed.
            // Releasing here covers that path as well as normal transport teardown.
            if (_steamLobbyAttemptActive)
                Steam.SteamLobbyManager.LeaveLobby();
#endif
        }

        /// <summary>Clears state tied to a transport without replacing the current user-facing session state.</summary>
        private void ResetSessionRuntimeState()
        {
            _hostPeer = null;
            _playersByPeer.Clear();
            _pendingPings.Clear();
            _nextSnapshotRequestAt.Clear();
            _nextResyncRequestAt.Clear();
            _nextObjectStateRequestAt.Clear();
            _passengerSeats.Clear();
            _guestSlotsBySteam.Clear();
            _nextPlayerId = 1;
            LocalPlayerId = 0;
            _nextPingAt = 0f;
            _pingNonce = 0;
            _steamOpStartedAt = -1f;
            _steamLobbyAttemptActive = false;
            _bypassHostPlayerGate = false;
            IsHost = false;
            PermanentDeathEnabled = false;
            _joinBrowseActive = false;
            ConnectionQuality.Instance.Reset();
            NetTrafficMeter.Instance.Reset();
        }

        /// <summary>
        /// Transport callbacks may report a host loss while their own update loop is
        /// enumerating peers. Dispose only after that callback pump has returned, but
        /// retain the failure text so users can see why the session ended.
        /// </summary>
        private void FlushFailedSessionCleanup()
        {
            if (!_failedSessionCleanupPending) return;

            _failedSessionCleanupPending = false;
            DisposeSessionTransport();
            ResetSessionRuntimeState();

            if (_pendingMode != LaunchMode.None)
                SetState(SessionState.Idle, PendingLaunchStatus(_pendingMode));
        }

        private void FailSession(string status)
        {
            SetState(SessionState.Failed, status);
            _failedSessionCleanupPending = true;
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
            FailSession(error);
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
            FlushFailedSessionCleanup();
            RunPendingLaunchMode();
            RecoverFromFailed();

            _transport?.Update();
            _devClient?.Update();
            FlushFailedSessionCleanup();

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
                    if (State != SessionState.Connecting)
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Ignored unexpected client transport connection from {peer} while {State}.");
                        return;
                    }

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
                if (!IsHost && _hostPeer.HasValue && _hostPeer.Value != peer)
                {
                    WinterMPPlugin.Log.LogDebug($"Ignored unexpected client transport disconnect from {peer}.");
                    return;
                }
                if (!IsHost && !_hostPeer.HasValue && State != SessionState.Connecting)
                {
                    WinterMPPlugin.Log.LogDebug($"Ignored client transport disconnect from {peer} while {State}.");
                    return;
                }

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
                    {
                        _passengerSeats.ForgetPlayer(player.PlayerId);
                        Sync.WorldSyncManager.Instance?.OnPlayerDeparted(player.PlayerId);
                    }
                    AddChatLine($"* {player.Name} left ({reason})");
                    PlayerLeft?.Invoke(player);

                    if (IsHost)
                        Broadcast(new PlayerDespawn { PlayerId = player.PlayerId, Reason = reason }, Channel.ReliableOrdered);
                }

                _nextSnapshotRequestAt.Remove(peer);
                _nextResyncRequestAt.Remove(peer);
                _nextObjectStateRequestAt.Remove(peer);

                // Only set the failure once: an in-band DisconnectMessage delivers the precise host
                // reason first, then the transport-level disconnect fires again — don't clobber it
                // with the generic "Lost connection to host."
                if (!IsHost && State != SessionState.Failed)
                {
                    string guestMessage = FormatGuestDisconnectReason(reason);
                    FailSession(guestMessage);
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
