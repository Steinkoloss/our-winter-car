using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class HeatingMessagesTests
    {
        [Fact]
        public void HeatSourceState_RoundTrips()
        {
            var original = new HeatSourceState
            {
                SourceId = 0xCAFEBABE,
                Flags = HeatSourceState.FlagLit,
                Fuel = 3,
                HeatOutput = 200,
                SaunaTemp = 8025,
            };

            var decoded = Assert.IsType<HeatSourceState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SourceId, decoded.SourceId);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.Equal(original.Fuel, decoded.Fuel);
            Assert.Equal(original.HeatOutput, decoded.HeatOutput);
            Assert.Equal(original.SaunaTemp, decoded.SaunaTemp);
            Assert.True(decoded.IsLit);
        }

        [Fact]
        public void HeatSourceState_Default_IsNotLit()
        {
            var decoded = Assert.IsType<HeatSourceState>(PacketCodec.Decode(PacketCodec.Encode(new HeatSourceState())));
            Assert.False(decoded.IsLit);
            Assert.Equal((ushort)0, decoded.SaunaTemp);
        }

        [Fact]
        public void HeatSourceIntent_RoundTrips()
        {
            var original = new HeatSourceIntent
            {
                SourceId = 0x12345678,
                Action = HeatSourceIntent.ActionSaunaThrow,
            };

            var decoded = Assert.IsType<HeatSourceIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SourceId, decoded.SourceId);
            Assert.Equal(original.Action, decoded.Action);
        }
    }
}
