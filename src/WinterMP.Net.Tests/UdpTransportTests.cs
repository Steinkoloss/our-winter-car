using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using WinterMP.Net;
using WinterMP.Net.Transport;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class UdpTransportTests
    {
        private static int RandomPort() => 28000 + new Random().Next(20000);

        private static void PumpUntil(Func<bool> condition, params ITransport[] transports)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "timed out waiting for transport condition");
                foreach (var transport in transports)
                    transport.Update();
                Thread.Sleep(5);
            }
        }

        [Fact]
        public void ClientConnectsAndBothSidesExchangePackets()
        {
            int port = RandomPort();
            using var host = UdpTransport.CreateHost(port);
            using var client = UdpTransport.CreateClient(new IPEndPoint(IPAddress.Loopback, port));

            PeerId? clientSeenByHost = null;
            PeerId? hostSeenByClient = null;
            host.PeerConnected += p => clientSeenByHost = p;
            client.PeerConnected += p => hostSeenByClient = p;

            PumpUntil(() => clientSeenByHost != null && hostSeenByClient != null, host, client);

            // client -> host
            byte[]? received = null;
            Channel? receivedChannel = null;
            host.PacketReceived += (peer, payload, channel) =>
            {
                received = payload;
                receivedChannel = channel;
            };
            client.Send(hostSeenByClient!.Value, new byte[] { 1, 2, 3, 4 }, Channel.UnreliableSequenced);
            PumpUntil(() => received != null, host, client);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, received);
            Assert.Equal(Channel.UnreliableSequenced, receivedChannel);

            // host -> client
            byte[]? echoed = null;
            client.PacketReceived += (peer, payload, channel) => echoed = payload;
            host.Send(clientSeenByHost!.Value, new byte[] { 9, 8 }, Channel.ReliableOrdered);
            PumpUntil(() => echoed != null, host, client);
            Assert.Equal(new byte[] { 9, 8 }, echoed);
        }

        [Fact]
        public void DisposingClientNotifiesHost()
        {
            int port = RandomPort();
            using var host = UdpTransport.CreateHost(port);
            var client = UdpTransport.CreateClient(new IPEndPoint(IPAddress.Loopback, port));

            PeerId? clientPeer = null;
            host.PeerConnected += p => clientPeer = p;
            PumpUntil(() => clientPeer != null, host, client);

            string? reason = null;
            host.PeerDisconnected += (peer, r) => reason = r;
            client.Dispose();
            PumpUntil(() => reason != null, host);
            Assert.Equal("Peer left.", reason);
        }

        [Fact]
        public void SecondHelloFromSamePeerDoesNotDoubleConnect()
        {
            int port = RandomPort();
            using var host = UdpTransport.CreateHost(port);
            using var client = UdpTransport.CreateClient(new IPEndPoint(IPAddress.Loopback, port));

            var connects = new List<PeerId>();
            host.PeerConnected += connects.Add;

            // HELLO retries happen every 0.5s until WELCOME; pump long enough for several.
            PumpUntil(() => connects.Count > 0, host, client);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
            while (DateTime.UtcNow < deadline)
            {
                host.Update();
                client.Update();
                Thread.Sleep(10);
            }

            Assert.Single(connects);
        }
    }
}
