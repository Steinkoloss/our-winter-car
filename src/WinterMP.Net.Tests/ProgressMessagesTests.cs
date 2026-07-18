using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ProgressMessagesTests
    {
        [Fact]
        public void FluidContainerState_RoundTrips()
        {
            var original = new FluidContainerState
            {
                ItemId = 0xCAFE1234,
                OwnerPlayerId = 2,
                Sequence = 99,
                Flags = FluidContainerState.FlagPouring,
                Level = 7.25f,
                Capacity = 10f,
            };

            var decoded = Assert.IsType<FluidContainerState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ItemId, decoded.ItemId);
            Assert.Equal(original.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.True(decoded.IsPouring);
            Assert.Equal(original.Level, decoded.Level);
            Assert.Equal(original.Capacity, decoded.Capacity);
        }

        [Fact]
        public void WorldProgressState_RoundTrips()
        {
            var original = new WorldProgressState
            {
                Kind = WorldProgressKind.Classifieds,
                Phase = 3,
                Sequence = 421,
                Primary = 12,
                Secondary = 4,
                Tertiary = -1,
                Value = 345.5f,
            };

            var decoded = Assert.IsType<WorldProgressState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Kind, decoded.Kind);
            Assert.Equal(original.Phase, decoded.Phase);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Primary, decoded.Primary);
            Assert.Equal(original.Secondary, decoded.Secondary);
            Assert.Equal(original.Tertiary, decoded.Tertiary);
            Assert.Equal(original.Value, decoded.Value);
        }

        [Fact]
        public void JobSiteState_RoundTrips()
        {
            var original = new JobSiteState
            {
                SiteId = 0x4411AA22,
                Kind = JobSiteState.KindSewage,
                Flags = JobSiteState.FlagActive,
                Sequence = 65530,
                Primary = 1843.25f,
                Secondary = 42.5f,
            };

            var decoded = Assert.IsType<JobSiteState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SiteId, decoded.SiteId);
            Assert.Equal(original.Kind, decoded.Kind);
            Assert.True(decoded.IsActive);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Primary, decoded.Primary);
            Assert.Equal(original.Secondary, decoded.Secondary);
        }
    }
}
