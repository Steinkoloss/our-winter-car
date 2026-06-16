using System;
using System.Collections.Generic;

namespace WinterMP.Net.Transport
{
    public sealed class LoopbackPair
    {
        public LoopbackTransport Host = null!;
        public LoopbackTransport Client = null!;
    }

    /// <summary>
    /// In-process transport pair for development and protocol tests: lets a single game
    /// instance (or a unit test) run the full host+client protocol without Steam.
    /// (No tuples/modern BCL here — this must compile for the net35 game profile.)
    /// </summary>
    public sealed class LoopbackTransport : ITransport
    {
        private struct InboxItem
        {
            public PeerId From;
            public byte[] Payload;
            public Channel Channel;
        }

        private readonly Queue<InboxItem> _inbox = new Queue<InboxItem>();

        private LoopbackTransport? _remote;
        private bool _connectRaised;
        private bool _remoteClosed;
        private bool _disposed;

        public PeerId LocalPeerId { get; }
        public bool IsHost { get; }

        public event Action<PeerId>? PeerConnected;
        public event Action<PeerId, string>? PeerDisconnected;
        public event Action<PeerId, byte[], Channel>? PacketReceived;

        private LoopbackTransport(bool isHost, PeerId localPeerId)
        {
            IsHost = isHost;
            LocalPeerId = localPeerId;
        }

        public static LoopbackPair CreatePair()
        {
            var host = new LoopbackTransport(true, new PeerId(1));
            var client = new LoopbackTransport(false, new PeerId(2));
            host._remote = client;
            client._remote = host;
            return new LoopbackPair { Host = host, Client = client };
        }

        public void Send(PeerId peer, byte[] payload, Channel channel)
        {
            Send(peer, payload, payload.Length, channel);
        }

        public void Send(PeerId peer, byte[] payload, int length, Channel channel)
        {
            if (_disposed || _remote == null || _remote._disposed) return;
            if (peer != _remote.LocalPeerId) return;

            var copy = new byte[length];
            Array.Copy(payload, copy, length);
            lock (_remote._inbox)
            {
                _remote._inbox.Enqueue(new InboxItem { From = LocalPeerId, Payload = copy, Channel = channel });
            }
        }

        public void Update()
        {
            if (_disposed) return;

            if (!_connectRaised && _remote != null)
            {
                _connectRaised = true;
                PeerConnected?.Invoke(_remote.LocalPeerId);
            }

            while (true)
            {
                InboxItem item;
                lock (_inbox)
                {
                    if (_inbox.Count == 0) break;
                    item = _inbox.Dequeue();
                }

                PacketReceived?.Invoke(item.From, item.Payload, item.Channel);
            }

            if (_remoteClosed)
            {
                _remoteClosed = false;
                var remote = _remote;
                _remote = null;
                if (remote != null)
                    PeerDisconnected?.Invoke(remote.LocalPeerId, "Remote end closed.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_remote != null)
                _remote._remoteClosed = true;
        }
    }
}
