using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class SessionMessagePolicyTests
    {
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
