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
    public sealed class SendTests : IDisposable
    {
        private static readonly PeerId First = new PeerId(101), Second = new PeerId(202);
        private readonly ITestOutputHelper _output;
        public SendTests(ITestOutputHelper output) { _output = output; }
        private static TransportDouble Setup(SessionState role = SessionState.Hosting)
        {
            SessionManager.Instance?.ClearFixtureSession();
            Time.unscaledTime = 10;
            WinterMPPlugin.Log.Lines.Clear(); SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            NetTrafficMeter.Instance.Channels.Clear();
            var session = SessionManager.Instance = new SessionManager { State = role };
            session.Admit(First);
            WorldSyncManager.Instance = new WorldSyncManager();
            return session.AttachTransport();
        }
        public void Dispose()
        {
            SessionManager.Instance?.ClearFixtureSession(); SessionManager.Instance = null;
            WorldSyncManager.Instance = null;
        }
        private static IMessage Request(int kind) => kind == 0 ? new WorldSnapshotRequest { IdHash = 42 }
            : kind == 1 ? new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates | WorldResyncRequest.FlagItems, ChecksumSequence = 7 }
            : new WorldObjectStateRequest { NetId = 123 };
        private static void Packet(PeerId peer, IMessage message, Channel channel = Channel.ReliableOrdered)
            => SessionManager.Instance!.Packet(peer, PacketCodec.Encode(message), channel);
        private static string Key(TransportDouble.Attempt a) => a.Peer.Value + ":" + (byte)a.Channel + ":" + Convert.ToBase64String(a.Payload);
        private static Exception Failure() => new InvalidOperationException("selected TrainState send failure", new ArgumentException("original transport cause"));

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void TrainSendFailurePreservesLaterChunksRecipientsOrderingAndException(int kind)
        {
            var healthy = Setup(); Packet(First, Request(kind));
            var expected = healthy.Delivered.Select(Key).ToArray();
            var expectedWithoutTrain = healthy.Delivered.Where(a => !(a.Message is TrainState)).Select(Key).ToArray();
            Assert.Single(healthy.Delivered, a => a.Message is TrainState);
            var transport = Setup();
            var failure = Failure();
            transport.BeforeDeliver = a => { if (a.Peer == First && a.Message is TrainState) throw failure; };
            Packet(First, Request(kind));
            _output.WriteLine(string.Join("\n", WinterMPPlugin.Log.Lines));
            Assert.Equal(expectedWithoutTrain, transport.Delivered.Select(Key));
            Assert.Equal(expected, transport.Attempts.Select(Key));
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("Error sending TrainState to 101 on channel 0") && s.Contains(failure.ToString()));
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("(" + expectedWithoutTrain.Length + " messages)"));
            Assert.DoesNotContain(WinterMPPlugin.Log.Lines, s => s.Contains("Error handling"));
            Assert.Equal(0, WorldSyncManager.Instance!.Errors);
            Assert.Equal(0, WorldSyncManager.Instance.Backoff);
            Assert.False(WorldSyncManager.Instance.Disabled);
            Assert.Equal(transport.Attempts.Count, NetTrafficMeter.Instance.Channels.Count);
            Assert.All(transport.Attempts, a => Assert.Equal(Channel.ReliableOrdered, a.Channel));
            // Same duplicate request is rate-limited: no replay of even the failed chunk.
            int attempts = transport.Attempts.Count;
            Packet(First, Request(kind)); Assert.Equal(attempts, transport.Attempts.Count);
            Assert.Equal(1, WorldSyncManager.Instance._train.Snapshots);
            SessionManager.Instance!.Admit(Second);
            Packet(Second, Request(kind));
            Assert.Equal(expected.Select(s => s.Replace("101:", "202:")), transport.Delivered.Where(a => a.Peer == Second).Select(Key));
            Assert.Single(transport.Delivered, a => a.Message is TrainState);
            Assert.Equal(2, WorldSyncManager.Instance._train.Snapshots);
            // A new request captures a new sequence; advancing time alone sends nothing.
            Time.unscaledTime += 3; transport.Update(); Assert.Equal(attempts * 2, transport.Attempts.Count);
            WorldSyncManager.Instance._train.OnSnapshot = () => new TrainState { Sequence = 2 };
            transport.BeforeDeliver = _ => { };
            Packet(First, Request(kind));
            Assert.Equal((uint)2, Assert.Single(transport.Delivered.Where(a => a.Peer == First).Select(a => a.Message).OfType<TrainState>()).Sequence);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void RealTrainEncodingFailureDoesNotContaminateNextPayload(int kind)
        {
            var healthy = Setup(); Packet(First, Request(kind));
            var expected = healthy.Delivered.Where(a => !(a.Message is TrainState)).Select(Key).ToArray();
            var transport = Setup();
            WorldSyncManager.Instance!._train.OnSnapshot = () => new TrainState { Phase = 255 };
            Packet(First, Request(kind));
            _output.WriteLine(string.Join("\n", WinterMPPlugin.Log.Lines));
            Assert.Equal(expected, transport.Delivered.Select(Key));
            Assert.DoesNotContain(transport.Attempts, a => a.Message is TrainState);
            Assert.Equal(expected.Length, NetTrafficMeter.Instance.Channels.Count);
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("Error sending TrainState") && s.Contains("WinterMP.Net.ProtocolException: Invalid train state.") && s.Contains("TrainState.Validate") && s.Contains("PacketCodec.Encode") && s.Contains("SessionManager.SendTo"));
            Assert.Equal(0, WorldSyncManager.Instance.Errors);
            Packet(First, new PingMessage { Nonce = 90 });
            Assert.Equal((uint)90, Assert.Single(transport.Delivered.Select(a => a.Message).OfType<PongMessage>()).Nonce);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void AmbiguousPostDeliveryExceptionNeverRetriesTrain(int kind)
        {
            var transport = Setup(); int applied = 0;
            transport.BeforeDeliver = a => { if (a.Message is TrainState) { applied++; throw Failure(); } };
            Packet(First, Request(kind));
            Assert.Single(transport.Attempts, a => a.Message is TrainState);
            Assert.Equal(1, applied);
            Packet(First, Request(kind));
            Assert.Equal(1, applied);
            Time.unscaledTime += 10; transport.Update();
            Assert.Equal(1, applied);
            Assert.True(transport.Attempts.Last().Message is not TrainState);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void NonTrainFailureStillUsesOriginalOuterPacketFallback(int kind)
        {
            var transport = Setup(); var failure = Failure();
            transport.BeforeDeliver = a => { if (!(a.Message is TrainState)) throw failure; };
            Packet(First, Request(kind));
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("Error handling " + Request(kind).Id) && s.Contains(failure.ToString()));
            Assert.DoesNotContain(WinterMPPlugin.Log.Lines, s => s.Contains("Error sending TrainState"));
            Assert.Equal(1, transport.Attempts.Count(a => !(a.Message is TrainState)));
            Assert.Equal(0, WorldSyncManager.Instance!.Errors);
            transport.BeforeDeliver = _ => { };
            Packet(First, new PingMessage { Nonce = 91 });
            Assert.Single(transport.Delivered.Select(a => a.Message).OfType<PongMessage>());
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void InvalidIngressDoesNotReachSnapshotSendOrConsumeCooldown(int kind)
        {
            var transport = Setup(); var request = Request(kind);
            Packet(Second, request);
            Packet(First, request, Channel.UnreliableSequenced);
            Packet(First, request, Channel.ReliableBulk);
            Packet(First, request, (Channel)255);
            var bytes = PacketCodec.Encode(request);
            SessionManager.Instance!.Packet(First, bytes.Take(bytes.Length - 1).ToArray(), Channel.ReliableOrdered);
            Packet(First, new WorldResyncRequest { Flags = 0 });
            Packet(First, new WorldResyncRequest { Flags = 128 });
            Assert.Empty(transport.Attempts); Assert.Equal(0, WorldSyncManager.Instance!._train.Snapshots);
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("unauthorised"));
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("unexpected channel"));
            Assert.Contains(WinterMPPlugin.Log.Lines, s => s.Contains("malformed"));
            Packet(First, request); Assert.NotEmpty(transport.Delivered);
        }

        [Theory]
        [InlineData(SessionState.Connected)] [InlineData(SessionState.Connecting)] [InlineData(SessionState.Idle)]
        public void NonHostCannotServeSnapshotButConnectedGuestStillAcceptsHostTrain(SessionState role)
        {
            var transport = Setup(role);
            for (int kind = 0; kind < 3; kind++) Packet(First, Request(kind));
            Assert.Empty(transport.Attempts); Assert.Equal(0, WorldSyncManager.Instance!._train.Snapshots);
            Packet(First, new TrainState { Sequence = 1 });
            Assert.Equal(role == SessionState.Connected, WorldSyncManager.Instance._train._remote != null);
        }

        [Fact]
        public void FixtureCleanupDisposesTransportAndNewSessionHasNoSendFailureState()
        {
            var failed = Setup(); failed.BeforeDeliver = a => { if (a.Message is TrainState) throw Failure(); };
            Packet(First, Request(1)); var count = failed.Attempts.Count;
            SessionManager.Instance!.ClearFixtureSession();
            Assert.True(failed.Disposed);
            Packet(First, Request(1)); Assert.Equal(count, failed.Attempts.Count);
            var fresh = Setup(); Packet(First, Request(1));
            Assert.Single(fresh.Delivered.Select(a => a.Message).OfType<TrainState>());
            Assert.DoesNotContain(WinterMPPlugin.Log.Lines, s => s.Contains("Error sending"));
        }
    }
}
