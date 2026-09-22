using System;
using System.IO;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class CylinderHeadPolicyTests
    {
        private static CylinderHeadState Loose(uint revision = 1) => new CylinderHeadState {
            NetId = 1, Revision = revision, Position = new NetVector3(1933, 4.5f, -422), Mass = 12 };

        [Fact]
        public void WireIncludesTheAttachmentAndLooseRecoveryPose()
        {
            var state = Loose(); byte[] bytes = PacketCodec.Encode(state); Assert.Equal(61, bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal(207, reader.ReadUInt16()); Assert.Equal(1u, reader.ReadUInt32());
            Assert.Equal(1u, reader.ReadUInt32()); Assert.Equal(0u, reader.ReadUInt32());
            var copy = Assert.IsType<CylinderHeadState>(PacketCodec.Decode(bytes));
            Assert.Equal(1933, copy.Position.X); Assert.Equal(12, copy.Mass);
            state.ParentId = 2; state.Mass = 0;
            Assert.Equal(2u, Assert.IsType<CylinderHeadState>(PacketCodec.Decode(PacketCodec.Encode(state))).ParentId);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        [InlineData(0f)] [InlineData(-1f)] [InlineData(10001f)]
        public void InvalidLooseMassCannotCreateGuestPhysics(float mass)
        {
            var state = Loose(); var bytes = PacketCodec.Encode(state); state.Mass = mass;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Array.Copy(BitConverter.GetBytes(mass), 0, bytes, 42, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void InvalidIdentityParentAndPoseCannotMoveASavedPart()
        {
            var state = Loose(); state.NetId = 0; Assert.False(CylinderHeadPolicy.Valid(state));
            state = Loose(); state.ParentId = state.NetId; state.Mass = 0; Assert.False(CylinderHeadPolicy.Valid(state));
            state = Loose(); state.ParentId = 2; Assert.False(CylinderHeadPolicy.Valid(state));
            state = Loose(); state.Position.X = float.NaN; Assert.False(CylinderHeadPolicy.Valid(state));
            state = Loose(); state.Rotation = default; Assert.False(CylinderHeadPolicy.Valid(state));
            state = Loose(); state.Rotation.W = float.PositiveInfinity; Assert.False(CylinderHeadPolicy.Valid(state));
        }

        [Fact]
        public void OldAndContradictoryAttachmentsCannotUndoFitting()
        {
            var fitted = Loose(2); fitted.ParentId = 2; fitted.Mass = 0;
            Assert.True(CylinderHeadPolicy.CanReceive(Loose(), fitted));
            Assert.False(CylinderHeadPolicy.CanReceive(fitted, Loose()));
            Assert.False(CylinderHeadPolicy.CanReceive(fitted, Loose(2)));
            Assert.True(CylinderHeadPolicy.CanReceive(fitted, Loose(3)));
            var other = Loose(3); other.NetId = 4; Assert.False(CylinderHeadPolicy.CanReceive(fitted, other));
            Assert.True(CylinderHeadPolicy.CanReceive(Loose(uint.MaxValue), Loose(0)));
            Assert.False(CylinderHeadPolicy.CanReceive(Loose(0), Loose(uint.MaxValue)));
            Assert.False(CylinderHeadPolicy.CanReceive(Loose(0), Loose(0x80000000)));
        }

        [Fact]
        public void MotionAndRepeatedSnapshotsDoNotInventAnAttachmentRevision()
        {
            var a = Loose(); var b = CylinderHeadPolicy.Copy(a); b.Position.X += 10;
            Assert.Equal(1933, a.Position.X); Assert.True(CylinderHeadPolicy.SameAttachment(a, b));
            Assert.True(CylinderHeadPolicy.CanReceive(a, b));
            Assert.Equal(CylinderHeadPolicy.MixChecksum(0, a), CylinderHeadPolicy.MixChecksum(0, b));
            b.ParentId = 2; b.Mass = 0;
            Assert.NotEqual(CylinderHeadPolicy.MixChecksum(0, a), CylinderHeadPolicy.MixChecksum(0, b));
        }

        [Fact]
        public void OnlyTheAuthenticatedHostCanPublishAnAttachment()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.CylinderHeadState, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.CylinderHeadState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.CylinderHeadState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.CylinderHeadState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.CylinderHeadState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.CylinderHeadState, Channel.UnreliableSequenced));
        }

        [Fact]
        public void CatalogAcceptsOnlyTheVerifiedSceneAssembly()
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var valid = WinterMP.Core.Catalog.SyncCatalogJson.Parse(json.ToJsonString());
            Assert.NotNull(valid.CylinderHead);
            foreach (string field in new[] { "nativeId", "parentNativeId", "mountPath" })
            {
                var copy = json.DeepClone(); copy["cylinderHead"]![field] = "Changed";
                var bad = WinterMP.Core.Catalog.SyncCatalogJson.Parse(copy.ToJsonString());
                Assert.Null(bad.CylinderHead); Assert.NotNull(bad.CylinderHeadError); Assert.NotNull(bad.ValveAdjustment);
            }
        }
    }
}
