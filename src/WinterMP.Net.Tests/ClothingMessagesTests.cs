using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ClothingMessagesTests
    {
        [Fact]
        public void PlayerClothingState_RoundTrips()
        {
            var original = new PlayerClothingState
            {
                PlayerId = 3,
                ClothingStage = 4,
                ClothingType = 2,
            };

            var decoded = Assert.IsType<PlayerClothingState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.ClothingStage, decoded.ClothingStage);
            Assert.Equal(original.ClothingType, decoded.ClothingType);
        }

        [Fact]
        public void PlayerClothingState_Default_IsZero()
        {
            var decoded = Assert.IsType<PlayerClothingState>(PacketCodec.Decode(PacketCodec.Encode(new PlayerClothingState())));
            Assert.Equal(0, decoded.PlayerId);
            Assert.Equal(0, decoded.ClothingStage);
            Assert.Equal(0, decoded.ClothingType);
        }
    }
}
