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

namespace TrainDispatch.Tests
{
    public sealed class IsolationTests
    {
        private static readonly PeerId Peer = new PeerId(101);
        private static WorldSyncManager Setup(SessionState role = SessionState.Hosting)
        {
            Time.unscaledTime = 10;
            WinterMPPlugin.Log.Lines.Clear(); SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            SessionManager.Instance = new SessionManager { State = role };
            SessionManager.Instance.Admit(Peer);
            return WorldSyncManager.Instance = new WorldSyncManager();
        }
        private static void Send(IMessage message, Channel channel = Channel.ReliableOrdered)
            => SessionManager.Instance!.Packet(Peer, PacketCodec.Encode(message), channel);
        private static Exception Failure() => new InvalidOperationException("injected boundary escape", new ArgumentException("inner identity"));

        [Theory]
        [InlineData("null")]
        [InlineData("empty")]
        [InlineData("truncated")]
        [InlineData("trailing")]
        [InlineData("invalid-phase")]
        [InlineData("unknown-message")]
        [InlineData("unauthorized")]
        [InlineData("wrong-channel")]
        [InlineData("unknown-channel")]
        public void PacketNegativesNeverReachReceiveOrConsumeFailureBudget(string kind)
        {
            var world = Setup(SessionState.Connected);
            var bytes = PacketCodec.Encode(new TrainState { Sequence = 1 });
            var peer = Peer; var channel = Channel.ReliableOrdered;
            switch (kind)
            {
                case "null": bytes = null!; break;
                case "empty": bytes = new byte[0]; break;
                case "truncated": bytes = bytes.Take(bytes.Length - 1).ToArray(); break;
                case "trailing": bytes = bytes.Concat(new byte[] { 0 }).ToArray(); break;
                case "invalid-phase": bytes[6] = 255; break; // ushort id + uint sequence
                case "unknown-message": bytes = new byte[] { 255, 255 }; break;
                case "unauthorized": peer = new PeerId(909); break;
                case "wrong-channel": channel = Channel.ReliableBulk; break;
                case "unknown-channel": channel = (Channel)255; break;
            }
            SessionManager.Instance!.Packet(peer, bytes, channel);
            Assert.Null(world._train._remote); Assert.Equal(0, world.Prepared); Assert.Equal(0, world.Errors);
            Assert.Single(WinterMPPlugin.Log.Lines);
            Send(new TrainState { Sequence = 2 });
            Assert.Equal(2u, world._train._remote!.Sequence);
        }

        [Fact]
        public void ActualReceiveRejectsStaleDuplicateAndInvalidWithoutChangingSettledState()
        {
            var world = Setup(SessionState.Connected);
            Send(new TrainState { Sequence = 7, HornSequence = 2 });
            var settled = world._train._remote;
            Time.unscaledTime = 20;
            Send(new TrainState { Sequence = 7, HornSequence = 20 });
            Send(new TrainState { Sequence = 6, HornSequence = 20 });
            world.OnTrainState(new TrainState { Sequence = 8, Phase = 255 });
            Assert.Same(settled, world._train._remote);
            Assert.Equal(10, world._train._receivedAt);
            Assert.Equal(2u, world._train._presentedHorn);
            Assert.Equal(0, world.Errors);
            Send(new TrainState { Sequence = 8, HornSequence = 3 });
            Assert.Equal(8u, world._train._remote!.Sequence);
            Assert.Equal(20, world._train._receivedAt);
            SessionManager.Instance!.State = SessionState.Hosting;
            world.OnTrainState(new TrainState { Sequence = 9 });
            Assert.Equal(8u, world._train._remote.Sequence);
            SessionManager.Instance = null;
            world.OnTrainState(new TrainState { Sequence = 10 });
            Assert.Equal(8u, world._train._remote.Sequence);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HealthyAndFaultedSnapshotsHaveExactlyTheSameNonTrainOrdering(bool full)
        {
            var world = Setup();
            var healthy = (full ? world.BuildWorldSnapshot() : world.BuildResyncMessages(WorldResyncRequest.FlagFsmStates)).ToArray();
            world._train.OnSnapshot = () => throw Failure();
            var faulty = (full ? world.BuildWorldSnapshot() : world.BuildResyncMessages(WorldResyncRequest.FlagFsmStates)).ToArray();
            Assert.Single(healthy.OfType<TrainState>());
            Assert.Equal(healthy.Where(x => !(x is TrainState)).Cast<PingMessage>().Select(x => x.Nonce), faulty.Cast<PingMessage>().Select(x => x.Nonce));
            Assert.Equal(2, world._train.Snapshots);
        }

        [Fact]
        public void ReceiveBackoffAdmitsOnlyNewCurrentCallsNeverCapturedWork()
        {
            var world = Setup(SessionState.Connected);
            world.OnPrepare = () => throw Failure();
            Send(new TrainState { Sequence = 1 });
            Assert.Equal(1, world.Errors);
            Send(new TrainState { Sequence = 2 });
            Assert.Equal(1, world.Prepared);
            Send(new PingMessage { Nonce = 5 });
            Assert.Single(SessionManager.Instance!.Sent.OfType<PongMessage>());
            world.OnPrepare = () => { };
            Time.unscaledTime = 11;
            Assert.Null(world._train._remote); // advancing time performs no work
            Send(new TrainState { Sequence = 3 });
            Assert.Equal(3u, world._train._remote!.Sequence);
            Assert.Equal(1, world.Errors); // success does not refund a cumulative failure
            world.OnPrepare = () => throw Failure();
            Send(new TrainState { Sequence = 4 });
            Time.unscaledTime = 12.99f; Send(new TrainState { Sequence = 5 });
            Assert.Equal(3, world.Prepared);
            Time.unscaledTime = 13; Send(new TrainState { Sequence = 6 });
            Time.unscaledTime = 100; Send(new TrainState { Sequence = 7 });
            Assert.Equal(4, world.Prepared); Assert.Equal(3, world.Errors);
            Assert.Equal(3u, world._train._remote.Sequence);
            world.Reset(); world.Reset(); world.OnPrepare = () => { };
            Send(new TrainState { Sequence = 8 });
            Assert.Equal(8u, world._train._remote.Sequence);
            Assert.Equal(3, world.Errors); Assert.Equal(2, world.Cleanup);
        }

        [Fact]
        public void SnapshotBudgetIsSharedAcrossFanoutsAndOnlyCapturesNewStateAfterDeadline()
        {
            var world = Setup();
            world._train.OnSnapshot = () => throw Failure();
            world.BuildWorldSnapshot().ToArray();
            world.BuildResyncMessages(WorldResyncRequest.FlagFsmStates).ToArray();
            Assert.Null(world.TrainSnapshot(123)); Assert.Equal(1, world._train.Snapshots);
            Time.unscaledTime = 11;
            world.BuildResyncMessages(WorldResyncRequest.FlagFsmStates).ToArray();
            Time.unscaledTime = 12.99f;
            world.BuildWorldSnapshot().ToArray(); Assert.Equal(2, world._train.Snapshots);
            Time.unscaledTime = 13;
            Assert.Null(world.TrainSnapshot(123));
            Time.unscaledTime = 100;
            world.BuildWorldSnapshot().ToArray(); Assert.Equal(3, world._train.Snapshots);
            Assert.Equal(3, world.Errors); Assert.False(world.Disabled);
            // Snapshot quarantine does not inhibit the independent receive boundary.
            SessionManager.Instance!.State = SessionState.Connected;
            Send(new TrainState { Sequence = 9 }); Assert.Equal(9u, world._train._remote!.Sequence);
            SessionManager.Instance.State = SessionState.Hosting;
            world.Reset(); world._train.OnSnapshot = () => new TrainState { Sequence = 99 };
            Assert.Equal(99u, world.TrainSnapshot()!.Sequence);
            Assert.Equal(4, world._train.Snapshots); Assert.Equal(3, world.Errors);
        }

        [Fact]
        public void ObjectIdentityIsFilteredInsideSnapshotBoundaryAndNullIsNotAnError()
        {
            var world = Setup();
            Assert.Null(world.TrainSnapshot(456)); Assert.Equal(0, world._train.Snapshots);
            world._train.ReadId = () => throw Failure();
            Assert.Null(world.TrainSnapshot(123)); Assert.Equal(1, world.Errors);
            world.Reset(); world._train.ReadId = () => 123;
            world._train.OnSnapshot = () => null;
            Assert.Null(world.TrainSnapshot(123)); Assert.Equal(1, world.Errors);
            Assert.Equal(1, world._train.Snapshots);
        }

        [Fact]
        public void UnreadyWorldAndNonGameReceiveDoNotReachTrain()
        {
            var world = Setup(); world._syncReady = false;
            Assert.Empty(world.BuildWorldSnapshot()); Assert.Empty(world.BuildResyncMessages(WorldResyncRequest.FlagFsmStates));
            Assert.Equal(0, world._train.Snapshots);
            world.Game = false; world.OnPrepare = () => throw Failure();
            world.OnTrainState(new TrainState { Sequence = 1 });
            Assert.Equal(0, world.Prepared); Assert.Equal(0, world.Errors);
        }

        [Fact]
        public void GlobalBudgetStillDisablesAndLocalResetDoesNotRefundHistory()
        {
            var world = Setup();
            for (int i = 0; i < 7; i++) world.GlobalError(Failure());
            Assert.Null(world.TrainSnapshot()); Assert.Equal(0, world._train.Snapshots);
            Time.unscaledTime = 11; world._train.OnSnapshot = () => throw Failure();
            Assert.Null(world.TrainSnapshot());
            Assert.True(world.Disabled); Assert.Equal(8, world.Errors); Assert.Equal(1, SyncEventLog.Dumps);
            Assert.Contains(WinterMPPlugin.Log.Lines, x => x.Contains("WorldSync disabled after 8 errors; last (Snapshot.TrainSync)") && x.Contains("inner identity"));
            int entries = SyncEventLog.Entries.Count;
            world.Reset(); world.Reset(); Time.unscaledTime = 100;
            Assert.Null(world.TrainSnapshot()); Assert.Equal(1, world._train.Snapshots);
            world.OnTrainState(new TrainState { Sequence = 1 }); Assert.Equal(0, world.Prepared);
            Assert.True(world.Disabled); Assert.Equal(8, world.Errors); Assert.Equal(entries, SyncEventLog.Entries.Count);
            Send(new PingMessage { Nonce = 77 }); Assert.Single(SessionManager.Instance!.Sent.OfType<PongMessage>());
        }

        [Fact]
        public void SelectedTrainSendFailureContinuesHealthyResyncWithoutRetry()
        {
            Setup();
            Send(new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates });
            var expected = SessionManager.Instance!.Sent.Select(x => Convert.ToBase64String(PacketCodec.Encode(x))).ToArray();
            var expectedNonTrain = SessionManager.Instance.Sent.Where(x => !(x is TrainState))
                .Select(x => Convert.ToBase64String(PacketCodec.Encode(x))).ToArray();
            Assert.NotEmpty(expectedNonTrain);
            var world = Setup();
            var attempts = new System.Collections.Generic.List<IMessage>();
            var failure = Failure();
            SessionManager.Instance!.BeforeSend = message =>
            {
                attempts.Add(message);
                if (message is TrainState) throw failure;
            };
            Send(new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates });
            Assert.Equal(expected, attempts.Select(x => Convert.ToBase64String(PacketCodec.Encode(x))));
            Assert.Equal(expectedNonTrain, SessionManager.Instance.Sent.Select(x => Convert.ToBase64String(PacketCodec.Encode(x))));
            Assert.Single(attempts.OfType<TrainState>());
            Assert.Equal(0, world.Errors);
            Assert.Contains(WinterMPPlugin.Log.Lines, x => x.Contains("Error sending TrainState to 101 on channel 0") && x.Contains(failure.ToString()));
            Assert.DoesNotContain(WinterMPPlugin.Log.Lines, x => x.Contains("Error handling"));
            Send(new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates });
            Assert.Equal(expected.Length, attempts.Count);
            Assert.Equal(1, world._train.Snapshots);
        }

        [Fact]
        public void ExistingSessionFallbackContainsTransportFailureWithoutAnyRetry()
        {
            var world = Setup();
            // No Train output: this preserves the original all-send-failure fallback assertions.
            world._train.OnSnapshot = () => null;
            int attempts = 0;
            SessionManager.Instance!.BeforeSend = _ => { attempts++; throw Failure(); };
            Send(new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates });
            Assert.Equal(1, attempts); Assert.Equal(0, world.Errors); Assert.Empty(SessionManager.Instance.Sent);
            Assert.Contains(WinterMPPlugin.Log.Lines, x => x.Contains("Error handling WorldResyncRequest from") && x.Contains("inner identity"));
            SessionManager.Instance.BeforeSend = _ => { };
            Send(new PingMessage { Nonce = 1 });
            Assert.Single(SessionManager.Instance.Sent.OfType<PongMessage>());
            // Existing request cooldown applies; containment is not blanket retry.
            Send(new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates });
            Assert.Equal(1, world._train.Snapshots);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(128)]
        public void InvalidResyncFlagsProduceNoSnapshot(byte flags)
        {
            var world = Setup();
            Send(new WorldResyncRequest { Flags = flags });
            Assert.Empty(SessionManager.Instance!.Sent); Assert.Equal(0, world._train.Snapshots); Assert.Equal(0, world.Errors);
        }
    }
}
