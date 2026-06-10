using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Transport;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class LoopbackTransportTests
    {
        [Fact]
        public void Pair_RaisesPeerConnected_OnFirstUpdate()
        {
            var pair = LoopbackTransport.CreatePair();
            PeerId? hostSaw = null, clientSaw = null;
            pair.Host.PeerConnected += p => hostSaw = p;
            pair.Client.PeerConnected += p => clientSaw = p;

            pair.Host.Update();
            pair.Client.Update();

            Assert.Equal(pair.Client.LocalPeerId, hostSaw);
            Assert.Equal(pair.Host.LocalPeerId, clientSaw);
            Assert.True(pair.Host.IsHost);
            Assert.False(pair.Client.IsHost);
        }

        [Fact]
        public void Send_DeliversPayloadAndChannel_BothDirections()
        {
            var pair = LoopbackTransport.CreatePair();
            pair.Host.Update();
            pair.Client.Update();

            var received = new List<(PeerId From, byte[] Payload, Channel Channel)>();
            pair.Client.PacketReceived += (from, payload, channel) => received.Add((from, payload, channel));

            pair.Host.Send(pair.Client.LocalPeerId, new byte[] { 1, 2, 3 }, Channel.ReliableOrdered);
            pair.Host.Send(pair.Client.LocalPeerId, new byte[] { 9 }, Channel.UnreliableSequenced);
            pair.Client.Update();

            Assert.Equal(2, received.Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, received[0].Payload);
            Assert.Equal(Channel.ReliableOrdered, received[0].Channel);
            Assert.Equal(Channel.UnreliableSequenced, received[1].Channel);
            Assert.Equal(pair.Host.LocalPeerId, received[0].From);

            var backward = new List<byte[]>();
            pair.Host.PacketReceived += (_, payload, _) => backward.Add(payload);
            pair.Client.Send(pair.Host.LocalPeerId, new byte[] { 42 }, Channel.ReliableBulk);
            pair.Host.Update();
            Assert.Single(backward);
            Assert.Equal(42, backward[0][0]);
        }

        [Fact]
        public void Dispose_NotifiesRemotePeer()
        {
            var pair = LoopbackTransport.CreatePair();
            pair.Host.Update();
            pair.Client.Update();

            string? reason = null;
            pair.Host.PeerDisconnected += (_, r) => reason = r;

            pair.Client.Dispose();
            pair.Host.Update();

            Assert.NotNull(reason);
        }

        [Fact]
        public void Send_MutationOfOriginalArray_DoesNotAffectDeliveredPayload()
        {
            var pair = LoopbackTransport.CreatePair();
            pair.Host.Update();
            pair.Client.Update();

            byte[]? delivered = null;
            pair.Client.PacketReceived += (_, payload, _) => delivered = payload;

            var buffer = new byte[] { 7, 7, 7 };
            pair.Host.Send(pair.Client.LocalPeerId, buffer, Channel.ReliableOrdered);
            buffer[0] = 0; // simulate writer reuse before the packet is pumped

            pair.Client.Update();
            Assert.NotNull(delivered);
            Assert.Equal(7, delivered![0]);
        }
    }
}
