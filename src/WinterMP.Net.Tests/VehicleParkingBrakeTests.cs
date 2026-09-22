using System;
using System.IO;
using System.Linq;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleParkingBrakeTests
    {
        private static VehicleClimate State(float value, byte owner = 1, ushort sequence = 3) => new VehicleClimate {
            VehicleId = 42, OwnerPlayerId = owner, Sequence = sequence, ParkingBrakeAvailable = true, ParkingBrake = value };

        [Theory]
        [InlineData(0f)] [InlineData(.125f)] [InlineData(.8207586f)] [InlineData(1f)]
        public void ExactSettingSurvivesWireAndOwnershipCopy(float value)
        {
            var bytes = PacketCodec.Encode(State(value));
            Assert.Equal(28, bytes.Length); Assert.Equal(1, bytes[23]); Assert.Equal(value, BitConverter.ToSingle(bytes, 24));
            var decoded = (VehicleClimate)PacketCodec.Decode(bytes); var copy = VehicleClimateStreamPolicy.Copy(decoded);
            decoded.ParkingBrake = 0; decoded.ParkingBrakeAvailable = false;
            Assert.True(copy.ParkingBrakeAvailable); Assert.Equal(value, copy.ParkingBrake); Assert.Equal(bytes, PacketCodec.Encode(copy));
            for (int n = 23; n < 28; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(n).ToArray()));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        [InlineData(-.01f)] [InlineData(1.01f)]
        public void InvalidFractionCannotBeWrittenReadOrAdvanceSequence(float value)
        {
            var invalid = State(value); Assert.False(VehicleClimateStreamPolicy.IsValid(invalid));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(invalid));
            var bytes = PacketCodec.Encode(State(1)); Array.Copy(BitConverter.GetBytes(value), 0, bytes, 24, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            var policy = new VehicleClimateStreamPolicy(); Assert.False(policy.Receive(invalid, true, false, 1));
            Assert.True(policy.Receive(State(1), true, false, 1));
        }

        [Fact]
        public void AbsentIsCanonicalAndAvailabilityByteIsStrict()
        {
            var state = State(.5f); state.ParkingBrakeAvailable = false;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var bytes = PacketCodec.Encode(State(0)); bytes[23] = 2;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            state.ParkingBrake = 0;
            Assert.False(((VehicleClimate)PacketCodec.Decode(PacketCodec.Encode(state))).ParkingBrakeAvailable);
        }

        [Fact]
        public void LatePacketsCannotReleaseTheAcceptedBrakeAndSnapshotsCannotOverrideDriver()
        {
            var policy = new VehicleClimateStreamPolicy();
            Assert.True(policy.Receive(State(1), true, false, 1));
            Assert.False(policy.Receive(State(0, sequence: 2), true, false, 1));
            Assert.False(policy.Receive(State(0, owner: 2, sequence: 4), true, false, 1));
            Assert.False(policy.Receive(State(0, owner: 0, sequence: VehicleClimate.SnapshotSequence), false, true, 255));
            Assert.True(policy.Receive(State(1, owner: 0, sequence: VehicleClimate.SnapshotSequence), false, false, 255));
        }
        [Fact]
        public void CatalogEnablesOnlyTheAuditedNativeSorbetLever()
        {
            var data = SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            Assert.Null(data.ParkingBrakeError); var source = Assert.Single(data.ParkingBrake!.Sources);
            Assert.Equal("SORBET(190-200psi)", source.RootPath); Assert.EndsWith("/HandBrake/LeverPivot", source.ControlPath);
            Assert.Equal("KnobPos", source.Variable); Assert.Equal("Wait player", source.IdleState);
        }

        [Theory]
        [InlineData("path")] [InlineData("state")] [InlineData("duplicate")]
        public void InvalidMetadataDisablesOnlyTheBrakeBinding(string defect)
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var sources = json["vehicleParkingBrake"]!["sources"]!;
            if (defect == "path") sources[0]!["controlPath"] = "CORRIS/Functions/HandBrake/LeverPivot";
            else if (defect == "state") sources[0]!["idleState"] = "INCREASE";
            else sources.AsArray().Add(sources[0]!.DeepClone());
            var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.ParkingBrake); Assert.NotNull(data.ParkingBrakeError);
            Assert.NotEmpty(data.Doors); Assert.NotNull(data.VehicleClimate);
        }
    }
}
