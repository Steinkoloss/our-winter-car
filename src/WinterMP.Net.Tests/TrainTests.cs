using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TrainTests
    {
        private static TrainState Moving() => new TrainState { Sequence = 4, Phase = 2, Flags = 7, ColliderMask = 2047,
            HornSequence = 9, Position = new NetVector3(100, .12f, 200), Velocity = new NetVector3(30, 0, 0), Volume = .8f };
        [Fact]
        public void ExactWireRoundTripAndEveryTruncationBoundary()
        {
            var bytes = PacketCodec.Encode(Moving()); Assert.Equal(58, bytes.Length);
            Assert.Equal(bytes, PacketCodec.Encode(PacketCodec.Decode(bytes)));
            for (int n = 0; n < bytes.Length; n++)
            { var cut = new byte[n]; Array.Copy(bytes, cut, n); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(cut)); }
            bytes[6] = 4; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonFinitePhysicsCannotCrossWire(float bad)
        {
            var s = Moving(); s.Position.X = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Moving(); s.Velocity.Y = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Moving(); s.Rotation.W = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Moving(); s.Volume = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
        }
        [Fact]
        public void WaitingAllowsNativeInvisibleRootColliderButNoMovement()
        {
            var s = Moving(); s.Phase = 1; Assert.False(TrainPolicy.Valid(s));
            s.Velocity = new NetVector3(); s.Flags = 1; s.ColliderMask = 1; Assert.True(TrainPolicy.Valid(s));
            s.Phase = 3; Assert.True(TrainPolicy.Valid(s));
            s.ColliderMask = 2048; Assert.False(TrainPolicy.Valid(s));
            s = Moving(); s.Flags = 8; Assert.False(TrainPolicy.Valid(s));
            s = Moving(); s.Sequence = 0; Assert.False(TrainPolicy.Valid(s));
            s = Moving(); s.Velocity.X = 32; Assert.False(TrainPolicy.Valid(s));
            s = Moving(); s.Position.Z = 100001; Assert.False(TrainPolicy.Valid(s));
            s = Moving(); s.Rotation.W = 0; Assert.False(TrainPolicy.Valid(s));
        }
        [Fact]
        public void ReplayOrderCrossesCounterWrapAndRejectsHalfRange()
        {
            Assert.True(TrainPolicy.Newer(1, uint.MaxValue)); Assert.True(TrainPolicy.Newer(10, 9));
            Assert.False(TrainPolicy.Newer(9, 9)); Assert.False(TrainPolicy.Newer(8, 9));
            Assert.False(TrainPolicy.Newer(0, uint.MaxValue)); Assert.False(TrainPolicy.Newer(uint.MaxValue, 1));
            Assert.False(TrainPolicy.Newer(0x80000001, 1));
        }
        [Fact]
        public void ExtrapolationStopsAtSixMetresAndStaleHazardsExpire()
        {
            var s = Moving(); Assert.Equal(103, TrainPolicy.Predict(s, .1f).X);
            Assert.Equal(106, TrainPolicy.Predict(s, 4).X); Assert.Equal(100, TrainPolicy.Predict(s, -1).X);
            Assert.Equal(100, TrainPolicy.Predict(s, float.NaN).X);
            Assert.True(TrainPolicy.Fresh(0)); Assert.True(TrainPolicy.Fresh(1));
            Assert.False(TrainPolicy.Fresh(1.001f)); Assert.False(TrainPolicy.Fresh(-1)); Assert.False(TrainPolicy.Fresh(float.NaN));
        }
        [Fact]
        public void OnlyAuthenticatedHostSuppliesTrainOnItsTwoChannels()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TrainState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TrainState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TrainState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TrainState, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.TrainState, Channel.ReliableOrdered));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.TrainState, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.TrainState, Channel.ReliableBulk));
        }
        [Fact]
        public void LifecycleChangesRequireReliableDeliveryWhileMotionDoesNot()
        {
            var a = Moving(); var b = Moving(); b.Position.X++; Assert.True(TrainPolicy.SameLifecycle(a, b));
            b.Phase = 0; Assert.False(TrainPolicy.SameLifecycle(a, b));
            b = Moving(); b.Flags = 1; Assert.False(TrainPolicy.SameLifecycle(a, b));
            b = Moving(); b.ColliderMask = 1; Assert.False(TrainPolicy.SameLifecycle(a, b));
            b = Moving(); b.HornSequence++; Assert.False(TrainPolicy.SameLifecycle(a, b));
        }
        [Fact]
        public void CatalogErrorsAreContainedWithoutLosingOtherAdapters()
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var c = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(c.TrainError); Assert.NotNull(c.Train);
            Assert.Equal(11, c.Train!.Colliders.Count);
            json["train"]!["colliders"]![1] = "Coll"; c = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.NotNull(c.TrainError); Assert.Null(c.Train); Assert.NotNull(c.Coffee);
        }
    }
}
