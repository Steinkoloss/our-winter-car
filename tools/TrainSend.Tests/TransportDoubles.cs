using System;
using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Session
{
    internal sealed partial class NetTrafficMeter
    {
        internal readonly List<Channel> Channels = new List<Channel>();
        internal void RecordSent(Channel channel, int length) => Channels.Add(channel);
    }
    internal sealed partial class SessionManager
    {
        private ITransport? _transport;
        private readonly NetWriter _sendWriter = new NetWriter();
        private readonly Dictionary<PeerId, float> _nextObjectStateRequestAt = new Dictionary<PeerId, float>();
        private const float ObjectStateRequestCooldownSeconds = 0.5f;
        internal TransportDouble AttachTransport()
        {
            var transport = new TransportDouble { IsHost = IsHost };
            _transport = transport;
            return transport;
        }
        internal void ClearFixtureSession()
        {
            _transport?.Dispose(); _transport = null;
            _playersByPeer.Clear(); _hostPeer = null;
            _nextSnapshotRequestAt.Clear(); _nextResyncRequestAt.Clear(); _nextObjectStateRequestAt.Clear();
            State = SessionState.Idle;
        }
    }
    internal sealed class TransportDouble : ITransport
    {
        internal sealed class Attempt
        {
            internal PeerId Peer;
            internal Channel Channel;
            internal IMessage Message = null!;
            internal byte[] Payload = null!;
        }
        public bool IsHost { get; set; }
        public event Action<PeerId>? PeerConnected { add { } remove { } }
        public event Action<PeerId, string>? PeerDisconnected { add { } remove { } }
        public event Action<PeerId, byte[], Channel>? PacketReceived { add { } remove { } }
        internal readonly List<Attempt> Attempts = new List<Attempt>();
        internal readonly List<Attempt> Delivered = new List<Attempt>();
        internal Action<Attempt> BeforeDeliver = _ => { };
        internal bool Disposed;
        public void Send(PeerId peer, byte[] payload, Channel channel)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(TransportDouble));
            var attempt = new Attempt { Peer = peer, Channel = channel, Payload = payload, Message = PacketCodec.Decode(payload) };
            Attempts.Add(attempt);
            BeforeDeliver(attempt);
            Delivered.Add(attempt);
        }
        public void Send(PeerId peer, byte[] payload, int length, Channel channel)
        {
            var copy = new byte[length]; Array.Copy(payload, copy, length); Send(peer, copy, channel);
        }
        public void Update() { }
        public void Dispose() { Disposed = true; }
    }
}
namespace WinterMP.Core.Sync
{
    public sealed partial class WorldSyncManager
    {
        // Deliberate producer double: the complete object iterator is static-audited,
        // unlike the full/resync iterators extracted by the shared fixture generator.
        public IEnumerable<IMessage> BuildObjectStateMessages(uint id)
        {
            var train = TrainSnapshot(id);
            if (train != null) yield return train;
            yield return new PingMessage { Nonce = Label("object-after-train") };
        }
    }
}
