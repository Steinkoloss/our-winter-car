using System;
using System.IO;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class PassengerCondensationTests
    {
        [Theory]
        [InlineData(0f)] [InlineData(.125f)] [InlineData(6f)] [InlineData(23.75f)] [InlineData(100f)]
        public void SweatAppendsAfterTheUnchangedPosePrefix(float sweat)
        {
            var state = new PlayerTransform { PlayerId = 3, Sequence = 65535, Position = new NetVector3(1, 2, 3),
                Rotation = new NetQuaternion(0, 0, 0, 1), MoveState = 16, HasSweat = true, Sweat = sweat };
            var bytes = PacketCodec.Encode(state); Assert.Equal(39, bytes.Length); Assert.Equal(1, bytes[34]);
            Assert.Equal(sweat, BitConverter.ToSingle(bytes, 35));
            var copy = Assert.IsType<PlayerTransform>(PacketCodec.Decode(bytes));
            Assert.True(copy.HasSweat); Assert.Equal(sweat, copy.Sweat); Assert.Equal((byte)16, copy.MoveState);
            Assert.Equal((ushort)65535, copy.Sequence); Assert.Equal(bytes, PacketCodec.Encode(copy));
            state.HasSweat = false; state.Sweat = 0;
            Assert.Equal(bytes.Take(34), PacketCodec.Encode(state).Take(34));
        }

        [Fact]
        public void UnavailableSweatAndTruncatedExtensionsHaveUnambiguousFraming()
        {
            var bytes = PacketCodec.Encode(new PlayerTransform());
            Assert.Equal(new byte[5], bytes.Skip(34));
            Assert.False(((PlayerTransform)PacketCodec.Decode(bytes)).HasSweat);
            for (int n = 0; n < bytes.Length; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(n).ToArray()));
        }

        [Theory]
        [InlineData(-.01f)] [InlineData(100.01f)] [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidSweatCannotCrossEitherWireBoundary(float sweat)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new PlayerTransform { HasSweat = true, Sweat = sweat }));
            var bytes = PacketCodec.Encode(new PlayerTransform { HasSweat = true });
            Array.Copy(BitConverter.GetBytes(sweat), 0, bytes, 35, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Theory]
        [InlineData(2)] [InlineData(128)] [InlineData(255)]
        public void UnknownAvailabilityIsRejected(byte flag)
        {
            var bytes = PacketCodec.Encode(new PlayerTransform()); bytes[34] = flag;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void UnavailableReportsCannotCarryHiddenSweat()
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new PlayerTransform { Sweat = 1 }));
            var bytes = PacketCodec.Encode(new PlayerTransform { HasSweat = true, Sweat = 1 }); bytes[34] = 0;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Theory]
        [InlineData(0, 6)] [InlineData(3, 6)] [InlineData(6, 6)] [InlineData(15, 15)]
        [InlineData(30, 30)] [InlineData(100, 30)]
        public void SingleOccupantKeepsTheNativeMinimumAndMaximum(float sweat, float effective)
            => Assert.Equal(effective, PassengerCondensationPolicy.AddOccupant(0, sweat));

        [Fact]
        public void PassengersCombineWithoutExceedingTheNativeMaximumRate()
        {
            float dry = PassengerCondensationPolicy.AddOccupant(0, 0);
            Assert.Equal(12, PassengerCondensationPolicy.AddOccupant(dry, 0));
            Assert.Equal(21, PassengerCondensationPolicy.AddOccupant(dry, 15));
            Assert.Equal(30, PassengerCondensationPolicy.AddOccupant(21, 20));
            Assert.Equal(30, PassengerCondensationPolicy.AddOccupant(30, 100));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)] [InlineData(101)]
        public void InvalidInputsCannotPoisonOtherOccupants(float sweat)
            => Assert.Equal(12, PassengerCondensationPolicy.AddOccupant(12, sweat));

        [Theory]
        [InlineData(10, 10, true)] [InlineData(14.999f, 10, true)] [InlineData(15, 10, false)]
        [InlineData(9, 10, false)] [InlineData(10, 0, false)] [InlineData(float.NaN, 10, false)]
        [InlineData(10, float.PositiveInfinity, false)]
        public void OldFutureAndMissingPlayerReportsDoNotContribute(float now, float receivedAt, bool fresh)
            => Assert.Equal(fresh, PassengerCondensationPolicy.Fresh(now, receivedAt));

        [Fact]
        public void ContributionQueryFollowsAcceptedPlayerAndVehicleIdentity()
        {
            var seats = new PassengerSeatLedger();
            seats.Apply(new PassengerState { PlayerId = 1, VehicleId = 5, SeatIndex = 0 }, (_, _) => true);
            Assert.True(seats.IsOccupant(1, 5)); Assert.False(seats.IsOccupant(2, 5)); Assert.False(seats.IsOccupant(1, 0));
            seats.Apply(new PassengerState { PlayerId = 1, VehicleId = 6, SeatIndex = 1, Sequence = 1 }, (_, _) => true);
            Assert.False(seats.IsOccupant(1, 5)); Assert.True(seats.IsOccupant(1, 6));
            seats.ForgetPlayer(1); Assert.False(seats.IsOccupant(1, 6));
        }

        [Fact]
        public void ShippedCondensationProfileNamesTheAuditedNativeOperands()
        {
            var catalog = WinterMP.Core.Catalog.SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            var p = catalog.VehicleClimate!.PassengerCondensation!;
            Assert.NotNull(p); Assert.Null(catalog.VehicleClimate.PassengerCondensationError);
            Assert.Equal("Player in?", p.State); Assert.Equal("PlayerIn", p.EntryVariable); Assert.Equal("PlayerSweat", p.SweatGlobal);
            Assert.Equal("SweatRate", p.SweatVariable); Assert.Equal("FrostingRate", p.RateVariable);
        }

        [Theory]
        [InlineData("null")] [InlineData("{}")] [InlineData("{\"state\":\"bad/path\"}")]
        public void MalformedCondensationProfileDoesNotDiscardOtherClimateRules(string profile)
        {
            var catalog = WinterMP.Core.Catalog.SyncCatalogJson.Parse("{\"vehicleClimate\":{\"pathPrefixes\":[\"CORRIS/\"],\"passengerCondensation\":" + profile + "}}");
            Assert.Null(catalog.VehicleClimate!.PassengerCondensation); Assert.NotNull(catalog.VehicleClimate.PassengerCondensationError);
            Assert.Contains("CORRIS/", catalog.VehicleClimate.PathPrefixes);
        }
    }
}
