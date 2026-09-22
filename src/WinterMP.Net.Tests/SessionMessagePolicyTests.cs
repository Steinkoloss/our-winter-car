using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class SessionMessagePolicyTests
    {
        [Theory]
        [InlineData(false, true, true, true, true)]
        [InlineData(false, true, true, false, false)]
        [InlineData(false, true, false, true, false)]
        [InlineData(true, true, false, true, false)]
        [InlineData(true, false, false, false, false)]
        public void SessionSettingsRequireAnAcceptedHost(bool host, bool authenticated, bool selected, bool complete, bool allowed)
        {
            Assert.Equal(allowed, SessionMessagePolicy.IsSenderAllowed(MessageId.SessionSettings, host, authenticated, selected, complete));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.SessionSettings, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.SessionSettings, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.SessionSettings, Channel.ReliableBulk));
        }

        [Theory]
        [InlineData((byte)0)] [InlineData((byte)1)]
        public void SessionSettingsAreExactlyOneKnownFlagByte(byte flags)
        {
            var packet = PacketCodec.Encode(new SessionSettings { Flags = flags });
            Assert.Equal(new byte[] { 213, 0, flags }, packet);
            Assert.Equal(flags, Assert.IsType<SessionSettings>(PacketCodec.Decode(packet)).Flags);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new byte[] { 213, 0 }));
        }

        [Theory]
        [InlineData((byte)2)] [InlineData((byte)3)] [InlineData((byte)255)]
        public void SessionSettingsRejectUnknownBits(byte flags)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new SessionSettings { Flags = flags }));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new byte[] { 213, 0, flags }));
        }

        [Fact]
        public void Host_OnlyAdmitsHandshakeBeforePeerAuthentication()
        {
            Assert.True(SessionMessagePolicy.IsSenderAllowed(
                MessageId.HandshakeRequest, receiverIsHost: true,
                senderIsAuthenticated: false, senderIsSelectedHost: false, receiverHandshakeComplete: false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(
                MessageId.WorldSnapshotRequest, receiverIsHost: true,
                senderIsAuthenticated: false, senderIsSelectedHost: false, receiverHandshakeComplete: false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(
                MessageId.WorldSnapshotRequest, receiverIsHost: true,
                senderIsAuthenticated: true, senderIsSelectedHost: false, receiverHandshakeComplete: false));
        }

        [Fact]
        public void Guest_OnlyAdmitsItsSelectedHost()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(
                MessageId.HandshakeResponse, receiverIsHost: false,
                senderIsAuthenticated: false, senderIsSelectedHost: false, receiverHandshakeComplete: false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(
                MessageId.HandshakeResponse, receiverIsHost: false,
                senderIsAuthenticated: false, senderIsSelectedHost: true, receiverHandshakeComplete: false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(
                MessageId.WorldItemSnapshot, receiverIsHost: false,
                senderIsAuthenticated: false, senderIsSelectedHost: true, receiverHandshakeComplete: false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(
                MessageId.WorldItemSnapshot, receiverIsHost: false,
                senderIsAuthenticated: false, senderIsSelectedHost: true, receiverHandshakeComplete: true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GuestsCannotReportVehicleDamageEvenAfterAuthentication(bool authenticated)
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.VehicleDamage, receiverIsHost: true,
                senderIsAuthenticated: authenticated, senderIsSelectedHost: true, receiverHandshakeComplete: true));
            foreach (var stream in new[] { MessageId.VehicleState, MessageId.VehicleCondition, MessageId.VehicleClimate, MessageId.ItemTransform })
                Assert.Equal(authenticated, SessionMessagePolicy.IsSenderAllowed(stream, receiverIsHost: true,
                    senderIsAuthenticated: authenticated, senderIsSelectedHost: false, receiverHandshakeComplete: true));
        }

        [Fact]
        public void VehicleDamageRequiresTheSelectedHostAndCompletedGuestHandshake()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.VehicleDamage, receiverIsHost: false,
                senderIsAuthenticated: true, senderIsSelectedHost: false, receiverHandshakeComplete: true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.VehicleDamage, receiverIsHost: false,
                senderIsAuthenticated: true, senderIsSelectedHost: true, receiverHandshakeComplete: false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.VehicleDamage, receiverIsHost: false,
                senderIsAuthenticated: true, senderIsSelectedHost: true, receiverHandshakeComplete: true));
        }

        [Fact]
        public void MessageChannels_RejectUnexpectedAndUnknownValues()
        {
            Assert.True(SessionMessagePolicy.IsChannelAllowed(
                MessageId.HandshakeRequest, Channel.ReliableOrdered));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(
                MessageId.WorldSnapshotRequest, Channel.ReliableOrdered));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(
                MessageId.ItemTransform, Channel.UnreliableSequenced));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(
                MessageId.ItemTransform, Channel.ReliableOrdered));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(
                MessageId.PlayerTransform, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(
                MessageId.WorldResyncRequest, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(
                MessageId.WorldObjectStateRequest, Channel.ReliableBulk));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(
                MessageId.HandshakeRequest, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(
                MessageId.WorldItemSnapshot, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(
                MessageId.PlayerTransform, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsKnownChannel((Channel)3));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(
                MessageId.HandshakeRequest, (Channel)255));
        }

        [Fact]
        public void ResyncFlags_MustNameDefinedStateGroups()
        {
            Assert.False(SessionMessagePolicy.IsValidResyncFlags(0));
            Assert.True(SessionMessagePolicy.IsValidResyncFlags(
                (byte)(WorldResyncRequest.FlagWallet | WorldResyncRequest.FlagVehicles)));
            Assert.False(SessionMessagePolicy.IsValidResyncFlags(
                (byte)(WorldResyncRequest.AllFlags | (1 << 6))));
        }
    }
}
