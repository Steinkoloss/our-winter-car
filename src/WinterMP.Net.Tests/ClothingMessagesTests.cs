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

        [Fact]
        public void ClothingV264AppendsAdmissionAndSequenceWithoutChangingPrefix()
        {
            var state = new PlayerClothingState { PlayerId = 2, ClothingStage = 4, ClothingType = 2,
                WinterGarment = 1, Admission = 71, Sequence = 8 };
            var bytes = PacketCodec.Encode(state);
            Assert.Equal(18, bytes.Length);
            Assert.Equal(new byte[] { 2, 4, 2, 1 }, new byte[] { bytes[2], bytes[3], bytes[4], bytes[5] });
            Assert.Equal(71UL, System.BitConverter.ToUInt64(bytes, 6));
            Assert.Equal(8U, System.BitConverter.ToUInt32(bytes, 14));
            var copy = Assert.IsType<PlayerClothingState>(PacketCodec.Decode(bytes));
            Assert.Equal(state.Admission, copy.Admission); Assert.Equal(state.Sequence, copy.Sequence);
            bytes[5] = 3;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            state.WinterGarment = 255;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        }

        [Fact]
        public void GuestSpawnClothingExtensionIsStrictAndRoundTripsExplicitZero()
        {
            var offer = new GuestSpawn { ClothingPlayerId = 2, ClothingAdmission = 71, HasSavedClothing = true };
            byte[] bytes = PacketCodec.Encode(offer);
            Assert.Equal(108, bytes.Length);
            Assert.Equal(2, bytes[95]); Assert.Equal(71UL, System.BitConverter.ToUInt64(bytes, 96));
            Assert.Equal(1, bytes[104]);
            var copy = Assert.IsType<GuestSpawn>(PacketCodec.Decode(bytes));
            Assert.True(copy.HasSavedClothing); Assert.Equal(0, copy.WinterGarment);
            foreach (byte invalid in new byte[] { 2, 255 })
            {
                bytes[104] = invalid;
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            }
            bytes[104] = 1; bytes[107] = 3;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            offer.HasSavedClothing = false; offer.ClothingStage = 1;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(offer));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new byte[] { 24, 0 }));
        }
    }
}
