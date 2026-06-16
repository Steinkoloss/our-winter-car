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

        /// <summary>
        /// Zero-copy send overload: sends <paramref name="length"/> bytes from the start of
        /// <paramref name="payload"/>. The buffer is valid only for the duration of this call —
        /// implementations must consume/copy it synchronously and must NOT retain the reference,
        /// since the caller reuses the buffer for the next send.
        /// </summary>
        void Send(PeerId peer, byte[] payload, int length, Channel channel);

        /// <summary>Pump the transport: dispatch received packets and connection events.</summary>
        void Update();
    }
}
