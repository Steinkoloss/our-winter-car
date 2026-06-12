using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ItemTransformPolicyTests
    {
        [Theory]
        [InlineData(false, ItemTransformPolicy.ItemRemoteHoldSeconds)]
        [InlineData(true, ItemTransformPolicy.DriverRemoteHoldSeconds)]
        public void GetRemoteHoldSeconds_DependsOnDriverFlag(bool remoteIsDriver, float expected)
        {
            Assert.Equal(expected, ItemTransformPolicy.GetRemoteHoldSeconds(remoteIsDriver));
        }

        [Fact]
        public void IsRemoteStreamLive_UsesDriverHoldForDrivers()
        {
            float now = 100f;
            float last = now - 2f;
            Assert.True(ItemTransformPolicy.IsRemoteStreamLive(last, now, remoteIsDriver: true));
            Assert.False(ItemTransformPolicy.IsRemoteStreamLive(now - 4f, now, remoteIsDriver: true));
            Assert.False(ItemTransformPolicy.IsRemoteStreamLive(now - 1f, now, remoteIsDriver: false));
        }

        [Theory]
        [InlineData(true, true, Channel.ReliableOrdered)]
        [InlineData(false, true, Channel.ReliableOrdered)]
        [InlineData(true, false, Channel.ReliableOrdered)]
        [InlineData(false, false, Channel.UnreliableSequenced)]
        public void SelectSendChannel_DriverAndFinalAreReliable(bool isFinal, bool isDriver, Channel expected)
        {
            Assert.Equal(expected, ItemTransformPolicy.SelectSendChannel(isFinal, isDriver));
        }

        [Fact]
        public void ShouldSeatDriverOutClaimRemote_BlocksLiveRemoteDriverEvenForHost()
        {
            float now = 50f;
            float last = now - 1f;

            Assert.False(ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteStreamLive: true,
                remoteIsDriver: true,
                lastRemoteAt: last,
                now: now,
                localPlayerId: 0,
                remoteOwnerId: 2));
        }

        [Fact]
        public void ShouldSeatDriverOutClaimRemote_AllowsClaimAfterDriverStreamStale()
        {
            float now = 50f;
            float last = now - ItemTransformPolicy.DriverRemoteHoldSeconds - 0.1f;

            Assert.True(ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteStreamLive: false,
                remoteIsDriver: true,
                lastRemoteAt: last,
                now: now,
                localPlayerId: 0,
                remoteOwnerId: 2));
        }

        [Fact]
        public void ShouldSeatDriverOutClaimRemote_TieBreaksStaleDualDriverByPlayerId()
        {
            float now = 50f;
            float last = now - ItemTransformPolicy.DriverRemoteHoldSeconds - 0.1f;

            Assert.False(ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteStreamLive: false,
                remoteIsDriver: true,
                lastRemoteAt: last,
                now: now,
                localPlayerId: 2,
                remoteOwnerId: 0));
        }

        [Fact]
        public void AllowsVehicleProximityClaim_BlocksNearLiveRemoteDriver()
        {
            float now = 10f;
            Assert.False(ItemTransformPolicy.AllowsVehicleProximityClaim(
                remoteIsDriver: true,
                remoteOwnerId: 1,
                lastRemoteAt: now - 0.5f,
                now: now));
        }

        [Fact]
        public void AllowsVehicleProximityClaim_AllowsAfterDriverHoldExpires()
        {
            float now = 10f;
            Assert.True(ItemTransformPolicy.AllowsVehicleProximityClaim(
                remoteIsDriver: true,
                remoteOwnerId: 1,
                lastRemoteAt: now - ItemTransformPolicy.DriverRemoteHoldSeconds - 0.1f,
                now: now));
        }

        [Theory]
        [InlineData(true, false, 1, 2, false)]
        [InlineData(false, true, 1, 2, true)]
        [InlineData(true, true, 2, 0, true)]
        [InlineData(true, true, 0, 2, false)]
        public void RemoteClaimWinsOverLocal_DriverBeatsNonDriverThenLowestId(
            bool localIsDriver, bool remoteIsDriver, byte localId, byte remoteId, bool remoteWins)
        {
            Assert.Equal(remoteWins, ItemTransformPolicy.RemoteClaimWinsOverLocal(
                localIsDriver, remoteIsDriver, localId, remoteId));
        }

        [Fact]
        public void IsStaleSequence_WrapAware()
        {
            Assert.True(ItemTransformPolicy.IsStaleSequence(10, 10));
            Assert.False(ItemTransformPolicy.IsStaleSequence(10, 11));
            Assert.True(ItemTransformPolicy.IsStaleSequence(100, 50));
        }

        [Fact]
        public void ItemTransform_DriverFlag_RoundTrips()
        {
            var original = new ItemTransform
            {
                ItemId = 1,
                OwnerPlayerId = 2,
                Sequence = 3,
                Flags = ItemTransform.FlagDriver,
            };

            var decoded = Assert.IsType<ItemTransform>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.True(decoded.IsDriver);
            Assert.False(decoded.IsFinal);
            Assert.Equal(Channel.ReliableOrdered, ItemTransformPolicy.SelectSendChannel(decoded.IsFinal, decoded.IsDriver));
        }
    }
}
