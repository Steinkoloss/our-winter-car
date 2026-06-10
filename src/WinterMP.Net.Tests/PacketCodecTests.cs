using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PacketCodecTests
    {
        [Fact]
        public void HandshakeRequest_RoundTrips()
        {
            var original = new HandshakeRequest
            {
                ProtocolVersion = 7,
                ModVersion = "0.1.0",
                GameVersion = "EA v.260102-01",
                CatalogHash = 0xCAFEBABE,
                PlayerName = "Jokke",
            };

            var decoded = Assert.IsType<HandshakeRequest>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
            Assert.Equal(original.ModVersion, decoded.ModVersion);
            Assert.Equal(original.GameVersion, decoded.GameVersion);
            Assert.Equal(original.CatalogHash, decoded.CatalogHash);
            Assert.Equal(original.PlayerName, decoded.PlayerName);
        }

        [Fact]
        public void HandshakeResponse_RoundTrips()
        {
            var original = new HandshakeResponse
            {
                Accepted = false,
                Reason = "Mod version mismatch (host 0.2.0, you 0.1.0).",
                PlayerId = 3,
                HostPlayerName = "Host",
            };

            var decoded = Assert.IsType<HandshakeResponse>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.False(decoded.Accepted);
            Assert.Equal(original.Reason, decoded.Reason);
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.HostPlayerName, decoded.HostPlayerName);
        }

        [Fact]
        public void ChatMessage_RoundTrips()
        {
            var original = new ChatMessage { SenderPlayerId = 2, Text = "perkele" };
            var decoded = Assert.IsType<ChatMessage>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SenderPlayerId, decoded.SenderPlayerId);
            Assert.Equal(original.Text, decoded.Text);
        }

        [Fact]
        public void PlayerTransform_RoundTrips()
        {
            var original = new PlayerTransform
            {
                PlayerId = 1,
                Sequence = 65000,
                Position = new NetVector3(-1542.7f, 4.25f, 980.1f),
                Rotation = new NetQuaternion(0f, 0.7071f, 0f, 0.7071f),
                MoveState = 4,
            };

            var decoded = Assert.IsType<PlayerTransform>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Position.X, decoded.Position.X);
            Assert.Equal(original.Rotation.W, decoded.Rotation.W);
            Assert.Equal(original.MoveState, decoded.MoveState);
        }

        [Fact]
        public void EveryRegisteredMessage_EncodesAndDecodesWithDefaults()
        {
            foreach (var id in MessageRegistry.KnownIds)
            {
                var message = MessageRegistry.Create(id);
                var decoded = PacketCodec.Decode(PacketCodec.Encode(message));
                Assert.Equal(id, decoded.Id);
            }
        }

        [Fact]
        public void UnknownMessageId_ThrowsProtocolException()
        {
            var writer = new NetWriter();
            writer.WriteUInt16(0xFFF0);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(writer.ToArray()));
        }
    }
}
