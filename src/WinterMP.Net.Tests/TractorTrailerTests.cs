using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TractorTrailerTests
    {
        private static TractorTrailerState State() => new TractorTrailerState { Revision = 1, Attached = true, Owner = 1 };
        private static TractorTrailerMotion Motion() => new TractorTrailerMotion { Revision = 1, Sequence = 1, Owner = 1 };
        private static TractorTrailerIntent Intent() => new TractorTrailerIntent { Revision = 1, Sequence = 1, PlayerId = 1 };
        [Fact]
        public void ConnectionAndIndependentBodyMomentumRoundTrip()
        {
            var s = State(); s.ConnectedAnchor = new NetVector3(1, 2, 3);
            for (int i = 0; i < 3; i++) { s.Bodies[i].Position = new NetVector3(i, 5, 6); s.Bodies[i].Velocity = new NetVector3(3, 4, 5); s.Bodies[i].AngularVelocity.Y = 2; }
            var bytes = PacketCodec.Encode(s); Assert.Equal(176, bytes.Length);
            var read = Assert.IsType<TractorTrailerState>(PacketCodec.Decode(bytes));
            Assert.Equal(1u, read.Revision); Assert.Equal(1, read.Owner); Assert.True(read.Attached); Assert.Equal(3, read.ConnectedAnchor.Z);
            Assert.Equal(2, read.Bodies[2].Position.X); Assert.Equal(5, read.Bodies[1].Velocity.Z); Assert.Equal(2, read.Bodies[0].AngularVelocity.Y);
            Array.Resize(ref bytes, bytes.Length - 1); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            var m = Motion(); m.Bodies = s.Bodies;
            Assert.Equal(167, PacketCodec.Encode(m).Length);
            Assert.Equal(2, Assert.IsType<TractorTrailerMotion>(PacketCodec.Decode(PacketCodec.Encode(m))).Bodies[2].Position.X);
            Assert.Equal(11, PacketCodec.Encode(Intent()).Length);
        }
        [Fact]
        public void OnlyCurrentNearbyActorCanReleaseOnce()
        {
            var s = State(); var i = Intent();
            Assert.True(TractorTrailerPolicy.CanRelease(s, i, 1, 0, true, 9));
            Assert.False(TractorTrailerPolicy.CanRelease(s, i, 2, 0, true, 0));
            Assert.False(TractorTrailerPolicy.CanRelease(s, i, 1, 1, true, 0));
            Assert.False(TractorTrailerPolicy.CanRelease(s, i, 1, 0, false, 0));
            Assert.False(TractorTrailerPolicy.CanRelease(s, i, 1, 0, true, 9.01f));
            Assert.False(TractorTrailerPolicy.CanRelease(s, i, 1, 0, true, float.NaN));
            s.Revision++; Assert.False(TractorTrailerPolicy.CanRelease(s, i, 1, 0, true, 0));
            s = State(); s.Attached = false; s.Owner = 0; Assert.False(TractorTrailerPolicy.CanRelease(s, i, 1, 0, true, 0));
        }
        [Fact]
        public void MotionRequiresAcceptedTractorAuthorityAndSameConnectionRevision()
        {
            var s = State(); var m = Motion();
            Assert.True(TractorTrailerPolicy.CanMove(s, m, 1, 0, true, 0));
            Assert.False(TractorTrailerPolicy.CanMove(s, m, 2, 0, true, 0));
            Assert.False(TractorTrailerPolicy.CanMove(s, m, 1, 1, true, 0));
            Assert.False(TractorTrailerPolicy.CanMove(s, m, 1, 0, false, 0));
            Assert.False(TractorTrailerPolicy.CanMove(s, m, 1, 0, true, 9.1f));
            m.Revision++; Assert.False(TractorTrailerPolicy.CanMove(s, m, 1, 0, true, 0));
            s.Revision++; s.Owner = 2; Assert.False(TractorTrailerPolicy.CanMove(s, m, 1, 0, true, 0));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(100001f)]
        public void InvalidPosesNeverReachPhysics(float bad)
        {
            var s = State(); s.Bodies[0].Position.X = bad;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            var bytes = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(bad), 0, bytes, 20, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void BrokenAssembliesInvalidOwnershipAndEpochZeroAreRefused()
        {
            var s = State(); s.Attached = false; Assert.False(TractorTrailerPolicy.Valid(s));
            s = State(); s.Owner = 255; Assert.False(TractorTrailerPolicy.Valid(s));
            s = State(); s.Revision = 0; Assert.False(TractorTrailerPolicy.Valid(s));
            s = State(); s.Bodies[1].Position.Y = 11; Assert.False(TractorTrailerPolicy.Valid(s));
            s = State(); s.Bodies[0].Rotation.W = 0; Assert.False(TractorTrailerPolicy.Valid(s));
            s = State(); s.Bodies[0].Velocity.X = 251; Assert.False(TractorTrailerPolicy.Valid(s));
            s = State(); s.ConnectedAnchor.Z = float.NaN; Assert.False(TractorTrailerPolicy.Valid(s));
            s = State(); s.Bodies = new TrailerBodyPose[2]; Assert.False(TractorTrailerPolicy.Valid(s));
            Assert.True(TractorTrailerPolicy.Newer(1, uint.MaxValue)); Assert.False(TractorTrailerPolicy.Newer(uint.MaxValue, 1));
        }
        [Fact]
        public void StateCannotBeForgedAndIntentsCannotBeSentToGuests()
        {
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.TractorTrailerMotion, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.TractorTrailerMotion, Channel.ReliableOrdered));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.TractorTrailerState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.TractorTrailerIntent, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TractorTrailerState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TractorTrailerIntent, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TractorTrailerMotion, false, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TractorTrailerMotion, true, true, false, true));
        }
        [Fact]
        public void MissingTrailerMetadataIsContainedToItsAdapter()
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            Assert.Null(SyncCatalogJson.Parse(json.ToJsonString()).TractorTrailerError);
            json["tractorTrailer"]!.AsObject().Remove("hook");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.TractorTrailer); Assert.NotNull(parsed.TractorTrailerError); Assert.NotNull(parsed.HouseholdFuses);
        }
    }
}
