#if STEAMWORKS
using System;
using System.Collections.Generic;
using Steamworks;
using WinterMP.Net;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Steam
{
    /// <summary>
    /// EXPERIMENTAL (M1) — ITransport over the classic Steam P2P API
    /// (SteamNetworking.SendP2PPacket / ReadP2PPacket), which is what the game's
    /// embedded Steamworks.NET provides (no modern SteamNetworkingSockets here).
    ///
    /// Classic P2P is connectionless: "connected" means we accepted a P2P session.
    /// NAT traversal + Steam relay fallback are handled by Steam. Channels map
    /// directly onto the API's nChannel parameter — no prefix byte needed.
    /// </summary>
    internal sealed class SteamP2PTransport : ITransport
    {
        private const int ChannelCount = 3; // Channel.ReliableOrdered/UnreliableSequenced/ReliableBulk

        private struct PendingEvent
        {
            public ulong Peer;
            public bool Connected;
            public string Reason;
        }

        private readonly Dictionary<ulong, CSteamID> _peers = new Dictionary<ulong, CSteamID>();
        private readonly Queue<PendingEvent> _pendingEvents = new Queue<PendingEvent>();
        private readonly Callback<P2PSessionRequest_t> _sessionRequest;
        private readonly Callback<P2PSessionConnectFail_t> _sessionFail;
        private byte[] _recvBuffer = new byte[64 * 1024];
        private bool _disposed;

        public bool IsHost { get; }

        public event Action<PeerId>? PeerConnected;
        public event Action<PeerId, string>? PeerDisconnected;
        public event Action<PeerId, byte[], Channel>? PacketReceived;

        private SteamP2PTransport(bool isHost)
        {
            IsHost = isHost;
            _sessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            _sessionFail = Callback<P2PSessionConnectFail_t>.Create(OnSessionFail);
        }

        public static SteamP2PTransport CreateHost()
        {
            return new SteamP2PTransport(true);
        }

        public static SteamP2PTransport CreateClient(CSteamID host)
        {
            var transport = new SteamP2PTransport(false);
            // Classic P2P has no explicit connect: mark the host as a peer right away;
            // the first SendP2PPacket triggers the session request on the host's side.
            transport._peers[host.m_SteamID] = host;
            transport._pendingEvents.Enqueue(new PendingEvent { Peer = host.m_SteamID, Connected = true, Reason = string.Empty });
            return transport;
        }

        private void OnSessionRequest(P2PSessionRequest_t data)
        {
            ulong remote = data.m_steamIDRemote.m_SteamID;

            if (!IsHost)
            {
                // Clients only ever talk to the host; accept its session (return traffic).
                if (_peers.ContainsKey(remote))
                    SteamNetworking.AcceptP2PSessionWithUser(data.m_steamIDRemote);
                else
                    WinterMPPlugin.Log.LogWarning($"Refused P2P session from unexpected peer {remote}.");
                return;
            }

            if (!SteamLobbyManager.IsLobbyMember(data.m_steamIDRemote))
            {
                WinterMPPlugin.Log.LogWarning($"Refused P2P session from non-lobby-member {remote}.");
                return;
            }

            SteamNetworking.AcceptP2PSessionWithUser(data.m_steamIDRemote);
            if (!_peers.ContainsKey(remote))
            {
                _peers[remote] = data.m_steamIDRemote;
                _pendingEvents.Enqueue(new PendingEvent { Peer = remote, Connected = true, Reason = string.Empty });
            }
        }

        private void OnSessionFail(P2PSessionConnectFail_t data)
        {
            ulong remote = data.m_steamIDRemote.m_SteamID;
            if (_peers.ContainsKey(remote))
            {
                _pendingEvents.Enqueue(new PendingEvent
                {
                    Peer = remote,
                    Connected = false,
                    Reason = $"P2P session failed (error {data.m_eP2PSessionError}).",
                });
            }
        }

        /// <summary>Called by the lobby manager when a member leaves the lobby.</summary>
        public void NotifyPeerLeft(ulong steamId, string reason)
        {
            if (_peers.ContainsKey(steamId))
                _pendingEvents.Enqueue(new PendingEvent { Peer = steamId, Connected = false, Reason = reason });
        }

        public void Send(PeerId peer, byte[] payload, Channel channel)
        {
            if (_disposed) return;

            CSteamID target;
            if (!_peers.TryGetValue(peer.Value, out target))
            {
                WinterMPPlugin.Log.LogWarning($"SteamP2PTransport.Send: unknown peer {peer}.");
                return;
            }

            EP2PSend sendType = channel == Channel.UnreliableSequenced
                ? EP2PSend.k_EP2PSendUnreliable
                : EP2PSend.k_EP2PSendReliable;

            if (!SteamNetworking.SendP2PPacket(target, payload, (uint)payload.Length, sendType, (int)channel))
                WinterMPPlugin.Log.LogWarning($"SendP2PPacket to {peer} failed (channel {channel}).");
        }

        public void Update()
        {
            if (_disposed) return;

            while (_pendingEvents.Count > 0)
            {
                var evt = _pendingEvents.Dequeue();
                if (evt.Connected)
                {
                    PeerConnected?.Invoke(new PeerId(evt.Peer));
                }
                else
                {
                    _peers.Remove(evt.Peer);
                    try
                    {
                        SteamNetworking.CloseP2PSessionWithUser(new CSteamID(evt.Peer));
                    }
                    catch
                    {
                        // best effort
                    }

                    PeerDisconnected?.Invoke(new PeerId(evt.Peer), evt.Reason);
                }
            }

            for (int channel = 0; channel < ChannelCount; channel++)
            {
                uint size;
                while (SteamNetworking.IsP2PPacketAvailable(out size, channel))
                {
                    if (size > _recvBuffer.Length)
                        _recvBuffer = new byte[Math.Max((int)size, _recvBuffer.Length * 2)];

                    uint read;
                    CSteamID remote;
                    if (!SteamNetworking.ReadP2PPacket(_recvBuffer, (uint)_recvBuffer.Length, out read, out remote, channel))
                        break;

                    ulong remoteId = remote.m_SteamID;
                    if (!_peers.ContainsKey(remoteId))
                    {
                        // Host: a lobby member's first packet can arrive before/instead of a
                        // session-request callback — treat it as the connect.
                        if (IsHost && SteamLobbyManager.IsLobbyMember(remote))
                        {
                            _peers[remoteId] = remote;
                            PeerConnected?.Invoke(new PeerId(remoteId));
                        }
                        else
                        {
                            WinterMPPlugin.Log.LogWarning($"Dropped packet from unknown peer {remoteId}.");
                            continue;
                        }
                    }

                    var payload = new byte[read];
                    Array.Copy(_recvBuffer, payload, (int)read);
                    PacketReceived?.Invoke(new PeerId(remoteId), payload, (Channel)channel);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var peer in _peers.Values)
            {
                try
                {
                    SteamNetworking.CloseP2PSessionWithUser(peer);
                }
                catch
                {
                    // best effort
                }
            }

            _peers.Clear();
            SteamLobbyManager.LeaveLobby();
        }
    }
}
#endif
