using System;
using System.Linq;
using UnityEngine;
using WinterMP.Core;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;
using Xunit;
using Xunit.Abstractions;

namespace TrainSend.Tests
{
    public sealed class FallbackTests : IDisposable
    {
        private static readonly PeerId Peer = new PeerId(101);
        private readonly ITestOutputHelper _output;
        public FallbackTests(ITestOutputHelper output) { _output = output; }
        private static TransportDouble Setup()
        {
            SessionManager.Instance?.ClearFixtureSession();
            Time.unscaledTime = 10;
            WinterMPPlugin.Log.Lines.Clear(); SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            NetTrafficMeter.Instance.Channels.Clear();
            var session = SessionManager.Instance = new SessionManager { State = SessionState.Hosting };
            session.Admit(Peer);
            WorldSyncManager.Instance = new WorldSyncManager();
            return session.AttachTransport();
        }
        public void Dispose()
        {
            SessionManager.Instance?.ClearFixtureSession(); SessionManager.Instance = null;
            WorldSyncManager.Instance = null;
        }
        private static IMessage Request(int kind) => kind == 0 ? new WorldSnapshotRequest { IdHash = 42 }
            : kind == 1 ? new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates }
            : new WorldObjectStateRequest { NetId = 123 };
        private static void Packet(IMessage request)
            => SessionManager.Instance!.Packet(Peer, PacketCodec.Encode(request), Channel.ReliableOrdered);
        private static string Key(TransportDouble.Attempt a) => a.Peer.Value + ":" + (byte)a.Channel + ":" + Convert.ToBase64String(a.Payload);

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void EveryMessageTransportFailureStopsAtFirstNonTrainWithoutRetry(int kind)
        {
            var healthy = Setup(); Packet(Request(kind));
            // Full snapshots start with a non-Train chunk; resync/object start with Train.
            var expected = healthy.Attempts.TakeWhile(a => a.Message is TrainState)
                .Concat(healthy.Attempts.SkipWhile(a => a.Message is TrainState).Take(1)).Select(Key).ToArray();
            Assert.Equal(kind == 0 ? 1 : 2, expected.Length);
            var transport = Setup();
            var failure = new InvalidOperationException("all messages transport failure", new ArgumentException("original all-send cause"));
            transport.BeforeDeliver = _ => throw failure;
            Packet(Request(kind));
            _output.WriteLine(string.Join("\n", WinterMPPlugin.Log.Lines));
            Assert.Equal(expected, transport.Attempts.Select(Key));
            Assert.Empty(transport.Delivered);
            Assert.Equal(expected.Length, transport.Attempts.Select(Key).Distinct().Count());
            Assert.Equal(kind == 0 ? 0 : 1, transport.Attempts.Count(a => a.Message is TrainState));
            Assert.Equal(1, transport.Attempts.Count(a => !(a.Message is TrainState)));
            Assert.All(transport.Attempts, a => Assert.Equal(Channel.ReliableOrdered, a.Channel));
            Assert.Equal(expected.Length, NetTrafficMeter.Instance.Channels.Count);
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("Error handling " + Request(kind).Id) && s.Contains(failure.ToString()) && s.Contains("TransportDouble.Send"));
            Assert.Equal(kind == 0 ? 0 : 1, WinterMPPlugin.Log.Lines.Count(s => s.Contains("Error sending TrainState") && s.Contains("original all-send cause")));
            Assert.DoesNotContain(WinterMPPlugin.Log.Lines, s => s.Contains(" messages)"));
            Assert.Equal(0, WorldSyncManager.Instance!.Errors);
            Assert.Equal(0, WorldSyncManager.Instance.Backoff);
            Assert.False(WorldSyncManager.Instance.Disabled);
            Packet(Request(kind));
            Assert.Equal(expected, transport.Attempts.Select(Key));
            Assert.Equal(kind == 0 ? 0 : 1, WorldSyncManager.Instance._train.Snapshots);
            Time.unscaledTime += 10; transport.Update();
            Assert.Equal(expected, transport.Attempts.Select(Key));
            transport.BeforeDeliver = _ => { };
            Packet(new PingMessage { Nonce = 91 });
            Assert.Equal((uint)91, Assert.Single(transport.Delivered.Select(a => a.Message).OfType<PongMessage>()).Nonce);
            Assert.Equal(expected, transport.Attempts.Take(expected.Length).Select(Key));
        }
    }
}
