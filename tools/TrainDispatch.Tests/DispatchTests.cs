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
    public sealed class DispatchTests
    {
        private static readonly PeerId Peer = new PeerId(101);
        private static WorldSyncManager Setup(SessionState role)
        {
            Time.unscaledTime = 10;
            WinterMPPlugin.Log.Lines.Clear(); SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            SessionManager.Instance = new SessionManager { State = role };
            SessionManager.Instance.Admit(Peer);
            return WorldSyncManager.Instance = new WorldSyncManager();
        }
        private static void Send(IMessage message, Channel channel = Channel.ReliableOrdered)
            => SessionManager.Instance!.Packet(Peer, PacketCodec.Encode(message), channel);
        private static Exception Failure() => new InvalidOperationException("train dispatch escape", new ArgumentException("original inner cause"));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TrainSnapshotEscapeDoesNotTruncateProductionFanout(bool full)
        {
            var world = Setup(SessionState.Hosting);
            world._train.OnSnapshot = () => throw Failure();
            if (full) Send(new WorldSnapshotRequest());
            else Send(new WorldResyncRequest { Flags = WorldResyncRequest.FlagFsmStates });
            Assert.Contains(SessionManager.Instance!.Sent, x => x is PingMessage p && WorldSyncManager.Labels[p.Nonce] == "BuildSnapshot");
            if (full) Assert.Contains(SessionManager.Instance.Sent, x => x is GuestSpawn);
            Assert.Equal(1, world.Errors);
            Assert.Equal(1, world._train.Snapshots);
            Assert.Equal(0, world.Backoff);
            Assert.False(world.Disabled);
            Assert.Contains(SyncEventLog.Entries, x => x.Contains("Snapshot.TrainSync #1") && x.Contains("System.InvalidOperationException: train dispatch escape") && x.Contains("System.ArgumentException: original inner cause") && x.Contains(nameof(TrainSnapshotEscapeDoesNotTruncateProductionFanout)));
            Send(new PingMessage { Nonce = 55 });
            Assert.Single(SessionManager.Instance.Sent.OfType<PongMessage>());
        }

        [Fact]
        public void DirectReceivePreparationEscapeIsContainedWithDetail()
        {
            var world = Setup(SessionState.Connected);
            world.OnPrepare = () => throw Failure();
            Assert.Null(Record.Exception(() => world.OnTrainState(new TrainState { Sequence = 1 })));
            Assert.Equal(1, world.Errors);
            Assert.Null(world._train._remote);
            Assert.Contains(SyncEventLog.Entries, x => x.Contains("Receive.TrainSync #1") && x.Contains("original inner cause"));
        }

        [Theory]
        [InlineData(SessionState.Connected, Channel.ReliableOrdered, true)]
        [InlineData(SessionState.Connected, Channel.UnreliableSequenced, true)]
        [InlineData(SessionState.Connected, Channel.ReliableBulk, false)]
        [InlineData(SessionState.Hosting, Channel.ReliableOrdered, false)]
        [InlineData(SessionState.Connecting, Channel.ReliableOrdered, false)]
        [InlineData(SessionState.Idle, Channel.ReliableOrdered, false)]
        public void TrainPolicyAndRoleAdmissionStayIntact(SessionState role, Channel channel, bool accepted)
        {
            var world = Setup(role);
            Send(new TrainState { Sequence = 1 }, channel);
            Assert.Equal(accepted, world._train._remote != null);
            Assert.Equal(accepted ? 1 : 0, world.Prepared);
            Assert.Equal(0, world.Errors);
        }
    }
}
