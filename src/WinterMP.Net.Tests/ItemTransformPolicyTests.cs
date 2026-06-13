using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ItemTransformPolicyTests
    {
        [Theory]
        [InlineData(false, false, ItemTransformPolicy.ItemRemoteHoldSeconds)]
        [InlineData(true, false, ItemTransformPolicy.DriverRemoteHoldSeconds)]
        [InlineData(false, true, ItemTransformPolicy.DriverRemoteHoldSeconds)]
        [InlineData(true, true, ItemTransformPolicy.DriverRemoteHoldSeconds)]
        public void GetRemoteHoldSeconds_DependsOnDriverOrVehicle(bool remoteIsDriver, bool isVehicle, float expected)
        {
            Assert.Equal(expected, ItemTransformPolicy.GetRemoteHoldSeconds(remoteIsDriver, isVehicle));
        }

        [Fact]
        public void IsRemoteStreamLive_UsesDriverHoldForDriversAndVehicles()
        {
            float now = 100f;
            Assert.True(ItemTransformPolicy.IsRemoteStreamLive(now - 2f, now, remoteIsDriver: true, isVehicle: false));
            Assert.True(ItemTransformPolicy.IsRemoteStreamLive(now - 2f, now, remoteIsDriver: false, isVehicle: true));
            Assert.False(ItemTransformPolicy.IsRemoteStreamLive(now - 4f, now, remoteIsDriver: true, isVehicle: false));
            Assert.False(ItemTransformPolicy.IsRemoteStreamLive(now - 1f, now, remoteIsDriver: false, isVehicle: false));
        }

        [Theory]
        [InlineData(true, true, Channel.ReliableOrdered)]
        [InlineData(true, false, Channel.ReliableOrdered)]
        [InlineData(false, true, Channel.ReliableOrdered)]
        [InlineData(false, false, Channel.UnreliableSequenced)]
        public void SelectSendChannel_FinalAndVehicleAreReliable(bool isFinal, bool isVehicle, Channel expected)
        {
            Assert.Equal(expected, ItemTransformPolicy.SelectSendChannel(isFinal, isVehicle));
        }

        [Fact]
        public void ShouldSeatDriverOutClaimRemote_BlocksLiveRemoteDriverEvenForHost()
        {
            float now = 50f;
            float last = now - 1f;

            Assert.False(ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteStreamLive: true,
                remoteIsDriver: true,
                remoteIsVehicle: false,
                lastRemoteAt: last,
                now: now,
                localPlayerId: 0,
                remoteOwnerId: 2));
        }

        [Fact]
        public void ShouldSeatDriverOutClaimRemote_AllowsClaimOverLiveProximityVehicleStream()
        {
            float now = 50f;
            float last = now - 1f;

            Assert.True(ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteStreamLive: true,
                remoteIsDriver: false,
                remoteIsVehicle: true,
                lastRemoteAt: last,
                now: now,
                localPlayerId: 0,
                remoteOwnerId: 2));
        }

        [Fact]
        public void RemoteClaimWinsOverLocal_LocalDriverBeatsRemotePusher()
        {
            Assert.False(ItemTransformPolicy.RemoteClaimWinsOverLocal(
                localIsDriver: true,
                remoteIsDriver: false,
                localPlayerId: 2,
                remoteOwnerId: 0));
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

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        public void VehicleCargoPolicy_BlocksClaimAndAllItemStreamsWhileMoving(
            bool isVehicle,
            bool insideActivelyDrivenVehicle,
            bool ignoreStream)
        {
            Assert.Equal(insideActivelyDrivenVehicle && !isVehicle,
                ItemTransformPolicy.ShouldBlockClaimForVehicleCargo(isVehicle, insideActivelyDrivenVehicle));
            Assert.Equal(ignoreStream,
                ItemTransformPolicy.ShouldIgnoreRemoteItemTransformForVehicleCargo(
                    isVehicle, insideActivelyDrivenVehicle));
        }

        [Fact]
        public void ItemTransform_Flags_RoundTrip()
        {
            var original = new ItemTransform
            {
                ItemId = 1,
                OwnerPlayerId = 2,
                Sequence = 3,
                Flags = ItemTransform.FlagDriver | ItemTransform.FlagVehicle,
            };

            var decoded = Assert.IsType<ItemTransform>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.True(decoded.IsDriver);
            Assert.True(decoded.IsVehicle);
            Assert.False(decoded.IsFinal);
            Assert.Equal(Channel.ReliableOrdered, ItemTransformPolicy.SelectSendChannel(decoded.IsFinal, decoded.IsVehicle));
        }
    }
}
