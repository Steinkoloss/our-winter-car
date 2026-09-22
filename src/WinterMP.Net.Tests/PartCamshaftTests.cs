using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class PartCamshaftTests
    {
        private static readonly ReplacementPartRule Rule = new ReplacementPartRule(71, "VIN115", 4, 1, supportsCamProfile: true);
        private static ReplacementPartState State() => new ReplacementPartState { FactoryId = Rule.FactoryId, NativeId = "VIN1157",
            Revision = 1, Scalars = new[] { 90f, 0f, 1.1f, 2f }, CamProfile = "55003500", Rotation = NetQuaternion.Identity };
        private static ReplacementPartReplica Replica() => new ReplacementPartReplica(new[] { Rule }, new ItemSpawnLifecycle());

        [Theory]
        [InlineData("55003500")]
        [InlineData("60003840")]
        [InlineData("70004480")]
        [InlineData("75004800")]
        [InlineData("80005120")]
        [InlineData("56003600")]
        public void NativeAndChangedHostProfilesSurviveTheWireAndReplica(string profile)
        {
            var state = State(); state.CamProfile = profile;
            var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(PacketCodec.Encode(state)));
            Assert.Equal(profile, decoded.CamProfile); Assert.Equal(state.Scalars, decoded.Scalars);
            var replica = Replica(); Assert.True(replica.Receive(decoded, out uint id));
            Assert.Equal(profile, replica.Get(id)!.CamProfile); Assert.True(Rule.CanCreate(decoded));
            decoded.CamProfile = "00000000"; Assert.Equal(profile, replica.Get(id)!.CamProfile);
        }

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("5500350")] [InlineData("550035000")]
        [InlineData("5500x500")] [InlineData("5500-500")] [InlineData("5500\n500")]
        [InlineData("５５００３５００")]
        public void InvalidProfilesCannotReplaceAcceptedStateOrCreateAPart(string? profile)
        {
            var replica = Replica(); var state = State(); Assert.True(replica.Receive(state, out uint id));
            state.Revision++; state.CamProfile = profile!;
            Assert.False(replica.Receive(state, out _)); Assert.False(Rule.CanCreate(state)); Assert.Equal("55003500", replica.Get(id)!.CamProfile);
            if (profile != "") Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        }

        [Fact]
        public void ProfilesAreGameplayStateAndCannotChangeAtTheSameRevision()
        {
            var state = State(); var replica = Replica(); Assert.True(replica.Receive(state, out uint id));
            var publication = new ReplacementPartPublication(); state.Revision = publication.Observe(state);
            publication.MarkBroadcast(state.Revision, state.PresentationRevision);
            state.CamProfile = "60003840"; state.PresentationRevision++;
            Assert.False(replica.Receive(state, out _));
            state.Revision = publication.Observe(state); Assert.Equal(2u, state.Revision); Assert.True(publication.NeedsBroadcast);
            Assert.True(replica.Receive(state, out _)); Assert.Equal("60003840", replica.Get(id)!.CamProfile);
        }

        [Fact]
        public void OtherFamiliesRequireAnEmptyProfileAndOldOrTruncatedPacketsAreRejected()
        {
            var other = new ReplacementPartRule(72, "VIN132", 3, 1);
            var state = State(); state.FactoryId = 72; state.NativeId = "VIN1327"; state.Scalars = new[] { 90f, 0f, 1.1f };
            var replica = new ReplacementPartReplica(new[] { other }, new ItemSpawnLifecycle());
            Assert.False(replica.Receive(state, out _)); state.CamProfile = ""; Assert.True(replica.Receive(state, out _));
            byte[] bytes = PacketCodec.Encode(State());
            for (int length = bytes.Length - 10; length < bytes.Length; length++)
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(length).ToArray()));
            bytes[bytes.Length - 2] = (byte)'x'; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
    }
}
