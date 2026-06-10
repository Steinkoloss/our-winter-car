using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace WinterMP.Net.Transport
{
    /// <summary>
    /// Plain UDP transport for the local two-instance test setup: one game hosts on
    /// a localhost port, a second game on the same machine joins it. This bypasses
    /// Steam entirely, so the second instance can run without a Steam context.
    ///
    /// IMPORTANT: this transport implements no reliability or ordering of its own —
    /// on the loopback interface datagrams are not lost or reordered in practice,
    /// which is exactly the scope this transport is meant for (dev/test only, see
    /// PLAN §3.2; real sessions use Steam P2P which provides reliable channels).
    ///
    /// Wire format: [packetType:byte] for control packets,
    ///              [packetType:byte][channel:byte][payload...] for data.
    /// </summary>
    public sealed class UdpTransport : ITransport
    {
        public const int DefaultPort = 27556;

        private const byte TypeHello = 1;
        private const byte TypeWelcome = 2;
        private const byte TypeData = 3;
        private const byte TypeBye = 4;
        private const byte TypeKeepAlive = 5;

        private const double KeepAliveIntervalSeconds = 3.0;
        private const double PeerTimeoutSeconds = 15.0;
        private const double HelloRetryIntervalSeconds = 0.5;
        /// <summary>Generous: the joining instance may be waiting on the host instance to finish booting.</summary>
        private const double ConnectTimeoutSeconds = 60.0;

        private readonly UdpClient _socket;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Dictionary<PeerId, IPEndPoint> _peerEndpoints = new Dictionary<PeerId, IPEndPoint>();
        private readonly Dictionary<PeerId, double> _lastReceivedAt = new Dictionary<PeerId, double>();
        private readonly List<PeerId> _timedOut = new List<PeerId>();
        private double _nextKeepAliveAt;
        private bool _disposed;

        // Client-side connection state.
        private readonly IPEndPoint? _hostEndpoint;
        private readonly PeerId _hostPeerId;
        private bool _connected;
        private bool _connectFailed;
        private double _nextHelloAt;

        public bool IsHost { get; }

        public event Action<PeerId>? PeerConnected;
        public event Action<PeerId, string>? PeerDisconnected;
        public event Action<PeerId, byte[], Channel>? PacketReceived;

        private UdpTransport(bool isHost, int listenPort, IPEndPoint? hostEndpoint)
        {
            IsHost = isHost;
            _hostEndpoint = hostEndpoint;
            _hostPeerId = hostEndpoint != null ? PeerIdFor(hostEndpoint) : default;

            _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, listenPort));

            // Windows quirk: a send to a closed port poisons the socket with
            // ICMP-driven ConnectionReset errors on subsequent receives. Disable.
            try
            {
                _socket.Client.IOControl(unchecked((IOControlCode)(-1744830452)), new byte[] { 0 }, null);
            }
            catch
            {
                // not supported on this platform — the receive loop also catches resets
            }
        }

        public static UdpTransport CreateHost(int port)
        {
            return new UdpTransport(isHost: true, listenPort: port, hostEndpoint: null);
        }

        public static UdpTransport CreateClient(IPEndPoint host)
        {
            return new UdpTransport(isHost: false, listenPort: 0, hostEndpoint: host);
        }

        public void Send(PeerId peer, byte[] payload, Channel channel)
        {
            if (_disposed) return;
            if (!_peerEndpoints.TryGetValue(peer, out var endpoint)) return;

            var datagram = new byte[payload.Length + 2];
            datagram[0] = TypeData;
            datagram[1] = (byte)channel;
            Buffer.BlockCopy(payload, 0, datagram, 2, payload.Length);

            try
            {
                _socket.Send(datagram, datagram.Length, endpoint);
            }
            catch (SocketException)
            {
                // peer process died; the timeout sweep will report the disconnect
            }
        }

        public void Update()
        {
            if (_disposed) return;

            ReceiveAll();

            if (!IsHost && !_connected && !_connectFailed)
                PumpConnect();

            double now = _clock.Elapsed.TotalSeconds;
            if (now >= _nextKeepAliveAt)
            {
                _nextKeepAliveAt = now + KeepAliveIntervalSeconds;
                SendControlToAll(TypeKeepAlive);
            }

            SweepTimeouts(now);
        }

        public void Dispose()
        {
            if (_disposed) return;
            SendControlToAll(TypeBye);
            _disposed = true;
            try
            {
                _socket.Close();
            }
            catch
            {
                // closing a socket should never take the session teardown down
            }
        }

        // ------------------------------------------------------------------ receive path

        private void ReceiveAll()
        {
            while (!_disposed)
            {
                byte[] datagram;
                var from = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    if (_socket.Available <= 0) return;
                    datagram = _socket.Receive(ref from);
                }
                catch (SocketException)
                {
                    continue; // stray ConnectionReset from a dead peer
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (datagram.Length == 0) continue;
                HandleDatagram(from, datagram);
            }
        }

        private void HandleDatagram(IPEndPoint from, byte[] datagram)
        {
            byte type = datagram[0];
            var peer = PeerIdFor(from);

            // Clients only ever talk to the host endpoint.
            if (!IsHost && (_hostEndpoint == null || !from.Equals(_hostEndpoint)))
                return;

            switch (type)
            {
                case TypeHello when IsHost:
                    if (!_peerEndpoints.ContainsKey(peer))
                    {
                        _peerEndpoints[peer] = from;
                        _lastReceivedAt[peer] = _clock.Elapsed.TotalSeconds;
                        PeerConnected?.Invoke(peer);
                    }
                    // Always answer — the client retries HELLO until WELCOME arrives.
                    SendControl(TypeWelcome, from);
                    break;

                case TypeWelcome when !IsHost:
                    if (!_connected)
                    {
                        _connected = true;
                        _peerEndpoints[peer] = from;
                        _lastReceivedAt[peer] = _clock.Elapsed.TotalSeconds;
                        PeerConnected?.Invoke(peer);
                    }
                    break;

                case TypeData:
                    if (_peerEndpoints.ContainsKey(peer) && datagram.Length >= 2)
                    {
                        _lastReceivedAt[peer] = _clock.Elapsed.TotalSeconds;
                        var payload = new byte[datagram.Length - 2];
                        Buffer.BlockCopy(datagram, 2, payload, 0, payload.Length);
                        PacketReceived?.Invoke(peer, payload, (Channel)datagram[1]);
                    }
                    break;

                case TypeKeepAlive:
                    if (_peerEndpoints.ContainsKey(peer))
                        _lastReceivedAt[peer] = _clock.Elapsed.TotalSeconds;
                    break;

                case TypeBye:
                    if (_peerEndpoints.Remove(peer))
                    {
                        _lastReceivedAt.Remove(peer);
                        PeerDisconnected?.Invoke(peer, "Peer left.");
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ client connect

        private void PumpConnect()
        {
            double now = _clock.Elapsed.TotalSeconds;

            if (now > ConnectTimeoutSeconds)
            {
                _connectFailed = true;
                PeerDisconnected?.Invoke(_hostPeerId, $"Could not reach host at {_hostEndpoint} within {ConnectTimeoutSeconds:0}s.");
                return;
            }

            if (now >= _nextHelloAt)
            {
                _nextHelloAt = now + HelloRetryIntervalSeconds;
                SendControl(TypeHello, _hostEndpoint!);
            }
        }

        // ------------------------------------------------------------------ helpers

        private void SendControl(byte type, IPEndPoint to)
        {
            try
            {
                _socket.Send(new[] { type }, 1, to);
            }
            catch (SocketException)
            {
                // host not up yet (connect retries) or peer gone (timeout sweep handles it)
            }
        }

        private void SendControlToAll(byte type)
        {
            foreach (var endpoint in _peerEndpoints.Values)
                SendControl(type, endpoint);
        }

        private void SweepTimeouts(double now)
        {
            _timedOut.Clear();
            foreach (var pair in _lastReceivedAt)
            {
                if (now - pair.Value > PeerTimeoutSeconds)
                    _timedOut.Add(pair.Key);
            }

            foreach (var peer in _timedOut)
            {
                _peerEndpoints.Remove(peer);
                _lastReceivedAt.Remove(peer);
                PeerDisconnected?.Invoke(peer, "Connection timed out.");
            }
        }

        /// <summary>Stable peer id from an endpoint (IPv4 address + port). Loopback/LAN scope.</summary>
        private static PeerId PeerIdFor(IPEndPoint endpoint)
        {
            ulong address;
            var bytes = endpoint.Address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                address = ((ulong)bytes[0] << 24) | ((ulong)bytes[1] << 16) | ((ulong)bytes[2] << 8) | bytes[3];
            }
            else
            {
                // IPv6: fold the address down; collisions are irrelevant at this scale.
                address = 0;
                foreach (byte b in bytes)
                    address = address * 31 + b;
            }

            return new PeerId((address << 16) | (uint)endpoint.Port);
        }
    }
}
