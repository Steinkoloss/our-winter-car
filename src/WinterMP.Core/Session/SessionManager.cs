using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net;
using WinterMP.Net.Messages;
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

        public IEnumerable<RemotePlayer> Players => _playersByPeer.Values;
        public int PlayerCount => _playersByPeer.Count;
        public IList<string> ChatLog => _chatLog;

        public event Action<RemotePlayer>? PlayerJoined;
        public event Action<RemotePlayer>? PlayerLeft;

        /// <summary>Don't touch Steam before the game's own init; menu load is the safe signal.
        /// Splash → menu takes 30-40s on a typical machine, so the fallback must be generous.</summary>
        private const float PendingLaunchTimeoutSeconds = 60f;

        private LaunchMode _pendingMode = LaunchMode.None;
        private ulong _pendingLobbyId;
        private bool _steamCallbacksRegistered;
        private float _failedAt = -1f;
        private const float FailedRecoverySeconds = 45f;

        public void Initialize(LaunchOptions launch)
        {
            Util.BootTrace.Crumb("SessionManager.Initialize: begin");
            Instance = this;
            _launch = launch;

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

            // Test mode only: drop Unity's single-instance guard immediately (does
            // not touch Steam, so it's safe this early) so the second test instance
            // can launch within seconds rather than after the host reaches the menu.
            if (launch.Mode == LaunchMode.HostLocal)
                Util.SingleInstanceUnlocker.Release();
            if (_pendingMode != LaunchMode.None)
                SetState(SessionState.Idle, $"Waiting for game to boot before '{_pendingMode}'...");

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

        private void EnsureSteamInviteHandlers()
        {
#if STEAMWORKS
            if (_steamCallbacksRegistered) return;
            if (!IsMainMenuReady() && Time.realtimeSinceStartup < PendingLaunchTimeoutSeconds) return;

            Steam.SteamBootstrap.EnsureInitialized();
            Steam.SteamLobbyManager.EnsureCallbacksRegistered();
            Steam.SteamBootstrap.SelfPump = true;
            _steamCallbacksRegistered = true;
#endif
        }

        private void RunPendingLaunchMode()
        {
            EnsureSteamInviteHandlers();

            if (_pendingMode == LaunchMode.None || State != SessionState.Idle) return;

            bool menuReady = IsMainMenuReady();
            if (!menuReady && Time.realtimeSinceStartup < PendingLaunchTimeoutSeconds) return;

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
                case LaunchMode.HostLocal:
                    StartHostLocal(_launch.LocalPort);
                    break;
                case LaunchMode.JoinLocal:
                    StartJoinLocal(_launch.LocalAddress, _launch.LocalPort);
                    break;
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

#if STEAMWORKS
            try
            {
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
                IsHost = true;
                LocalPlayerId = 0;
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
            IsHost = false;
            SetState(SessionState.Idle, "Idle");
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
            AttachTransport(transport);
            if (IsHost)
            {
                SetState(SessionState.Hosting, "Hosting — friends can join via Steam");
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
            WinterMPPlugin.Log.LogInfo($"Peer connected: {peer}");

            if (!IsHost)
            {
                _hostPeer = peer;

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

        private void OnPeerDisconnected(PeerId peer, string reason)
        {
            if (_playersByPeer.TryGetValue(peer, out var player))
            {
                _playersByPeer.Remove(peer);
                AddChatLine($"* {player.Name} left ({reason})");
                PlayerLeft?.Invoke(player);

                if (IsHost)
                    Broadcast(new PlayerDespawn { PlayerId = player.PlayerId, Reason = reason }, Channel.ReliableOrdered);
            }

            if (!IsHost)
                SetState(SessionState.Failed, $"Lost connection to host: {reason}");
        }

        private void OnPacketReceived(PeerId peer, byte[] payload, Channel channel)
        {
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
                    HandlePlayerTransform(peer, transform);
                    break;

                case PassengerState passengerState:
                    Sync.PassengerController.Instance?.OnRemotePassengerState(passengerState);
                    if (IsHost)
                        Broadcast(passengerState, Channel.ReliableOrdered, except: peer);
                    break;

                case FsmStateEnter stateEnter:
                    Sync.WorldSyncManager.Instance?.OnRemoteStateEnter(stateEnter);
                    if (IsHost)
                        Broadcast(stateEnter, Channel.ReliableOrdered, except: peer);
                    break;

                case FsmRawEvent rawEvent:
                    Sync.WorldSyncManager.Instance?.OnRemoteRawEvent(rawEvent);
                    if (IsHost)
                        Broadcast(rawEvent, Channel.ReliableOrdered, except: peer);
                    break;

                case BoltState boltState:
                    Sync.WorldSyncManager.Instance?.OnRemoteBoltState(boltState);
                    if (IsHost)
                        Broadcast(boltState, Channel.ReliableOrdered, except: peer);
                    break;

                case PartState partState:
                    Sync.WorldSyncManager.Instance?.OnRemotePartState(partState);
                    if (IsHost)
                        Broadcast(partState, Channel.ReliableOrdered, except: peer);
                    break;

                case ItemDespawn itemDespawn:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemDespawn(itemDespawn);
                    if (IsHost)
                        Broadcast(itemDespawn, Channel.ReliableOrdered, except: peer);
                    break;

                case ItemTransform itemTransform:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemTransform(itemTransform);
                    if (IsHost)
                        Broadcast(itemTransform,
                            itemTransform.IsFinal ? Channel.ReliableOrdered : Channel.UnreliableSequenced,
                            except: peer);
                    break;

                case VehicleState vehicleState:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleState(vehicleState);
                    if (IsHost)
                        Broadcast(vehicleState, Channel.UnreliableSequenced, except: peer);
                    break;

                case VehicleClimate vehicleClimate:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleClimate(vehicleClimate);
                    if (IsHost)
                        Broadcast(vehicleClimate, Channel.UnreliableSequenced, except: peer);
                    break;

                case TimeSync timeSync when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteTimeSync(timeSync);
                    break;

                case WalletState walletState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWalletState(walletState);
                    break;

                case PurchaseIntent purchaseIntent when IsHost:
                    Sync.WorldSyncManager.Instance?.OnHostPurchaseIntent(purchaseIntent);
                    break;

                case WorldSnapshotRequest snapshotRequest when IsHost:
                    HandleSnapshotRequest(peer, snapshotRequest);
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
                PlayerId = _nextPlayerId++,
                Peer = peer,
                SteamId = peer.Value,
                Name = request.PlayerName,
            };

            SendTo(peer, new HandshakeResponse
            {
                Accepted = true,
                PlayerId = player.PlayerId,
                HostPlayerName = LocalPlayerName,
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

            AddChatLine($"* {player.Name} joined");
            PlayerJoined?.Invoke(player);

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

            WinterMPPlugin.Log.LogInfo($"Sent world snapshot to {peer} ({messages} messages).");
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
            _playersByPeer[peer] = new RemotePlayer
            {
                PlayerId = 0,
                Peer = peer,
                SteamId = peer.Value,
                Name = response.HostPlayerName,
            };
            SetState(SessionState.Connected, $"Connected to {response.HostPlayerName}");
            AddChatLine($"* Connected to {response.HostPlayerName}'s game");
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

                // Drop stale unreliable packets (sequence wrap-aware).
                ushort diff = (ushort)(transform.Sequence - player.LastTransformSequence);
                if (diff == 0 || diff > short.MaxValue) return;

                player.LastTransformSequence = transform.Sequence;
                player.Position = transform.Position.ToUnity();
                player.Rotation = transform.Rotation.ToUnity();
                player.MoveState = transform.MoveState;
                player.LastTransformTime = Time.unscaledTime;
                break;
            }

            // Host relays transforms so all guests see all players.
            if (IsHost)
                Broadcast(transform, Channel.UnreliableSequenced, except: peer);
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
            _transport.Send(peer, _sendWriter.ToArray(), channel);
        }

        private void Broadcast(IMessage message, Channel channel, PeerId? except = null)
        {
            if (_transport == null) return;
            PacketCodec.Encode(message, _sendWriter);
            var payload = _sendWriter.ToArray();
            foreach (var peer in _playersByPeer.Keys)
            {
                if (except.HasValue && peer == except.Value) continue;
                _transport.Send(peer, payload, channel);
            }
        }

        // ---------------------------------------------------------------- misc

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
