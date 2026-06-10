using System;

namespace WinterMP.Net.Transport
{
    /// <summary>
    /// Datagram transport between this peer and one or more remote peers.
    ///
    /// Threading contract: all events are raised exclusively from inside <see cref="Update"/>,
    /// which the owner calls once per frame on the main thread. Implementations buffer
    /// internally if their backend delivers on other threads.
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>True when this end accepts incoming connections (listen server side).</summary>
        bool IsHost { get; }

        event Action<PeerId>? PeerConnected;
        event Action<PeerId, string>? PeerDisconnected;
        event Action<PeerId, byte[], Channel>? PacketReceived;

        void Send(PeerId peer, byte[] payload, Channel channel);

        /// <summary>Pump the transport: dispatch received packets and connection events.</summary>
        void Update();
    }
}
