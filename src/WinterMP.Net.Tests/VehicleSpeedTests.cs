using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleSpeedTests
    {
        private static VehicleState State(ushort movement = 800) => new VehicleState {
            VehicleId = 91, OwnerPlayerId = 1, Sequence = 5, Rpm = 3000, SpeedTenthsKmh = 1800,
            TorqueAvailable = true, EngineTorque = -12.375f, MovementSpeedAvailable = true, MovementSpeedTenthsKmh = movement };

        [Theory]
        [InlineData((ushort)0)] [InlineData((ushort)20)] [InlineData((ushort)21)] [InlineData(ushort.MaxValue)]
        public void MovementAndWheelSpeedRemainIndependentAcrossWireAndCopies(ushort movement)
        {
            var packet = PacketCodec.Encode(State(movement)); Assert.Equal(35, packet.Length); Assert.Equal(1, packet[22]);
            var read = (VehicleState)PacketCodec.Decode(packet); var copy = VehicleStateStreamPolicy.Copy(read);
            Assert.True(copy.MovementSpeedAvailable); Assert.Equal(movement, copy.MovementSpeedTenthsKmh);
            Assert.Equal(1800, copy.SpeedTenthsKmh); Assert.Equal(-12.375f, copy.EngineTorque); Assert.Equal(3000, copy.Rpm);
            Assert.Equal(packet, PacketCodec.Encode(copy)); read.MovementSpeedTenthsKmh = 99; Assert.Equal(movement, copy.MovementSpeedTenthsKmh);
        }

        [Fact]
        public void MissingSpeedCannotHideAValueOrEnterTheStream()
        {
            var state = State(); state.MovementSpeedAvailable = false;
            Assert.False(VehicleStateStreamPolicy.IsValid(state)); Assert.False(new VehicleStateStreamPolicy().Receive(state, true, false, 1));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var packet = PacketCodec.Encode(State()); packet[22] = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
            state.MovementSpeedTenthsKmh = 0; var read = (VehicleState)PacketCodec.Decode(PacketCodec.Encode(state));
            Assert.False(read.MovementSpeedAvailable); Assert.Equal(0, read.MovementSpeedTenthsKmh);
        }

        [Theory]
        [InlineData((byte)2)] [InlineData(byte.MaxValue)]
        public void AvailabilityIsStrictAndPreviousLayoutIsNotAccepted(byte invalid)
        {
            var packet = PacketCodec.Encode(State()); packet[22] = invalid;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
            var old = new byte[22]; Array.Copy(packet, old, 22); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(old));
        }

        [Fact]
        public void StaleOrForeignMovementCannotDisplaceAcceptedLoad()
        {
            var policy = new VehicleStateStreamPolicy(); var state = State(); Assert.True(policy.Receive(state, true, false, 1));
            var accepted = VehicleStateStreamPolicy.Copy(state); state.MovementSpeedTenthsKmh = 2500;
            Assert.False(policy.Receive(state, true, false, 1)); state.Sequence++; state.OwnerPlayerId = 2;
            Assert.False(policy.Receive(state, true, false, 1)); Assert.Equal(800, accepted.MovementSpeedTenthsKmh);
            state.Sequence = 0; Assert.True(policy.Receive(state, true, false, 2));
            Assert.Equal(2500, VehicleStateStreamPolicy.Copy(state).MovementSpeedTenthsKmh);
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Fact]
        public void CatalogUsesMovementProducerAndBothCoolingConsumers()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(data.VehicleCoolingError); var speed = data.VehicleCooling!;
            Assert.Equal("CORRIS", speed.RootPath); Assert.Equal("CORRIS/Simulation/CarData", speed.ProducerPath);
            Assert.Equal("Measurements", speed.ProducerFsm); Assert.Equal("SpeedKMH", speed.SpeedGlobal);
            Assert.Equal("CORRIS/Simulation/Systems/Cooling", speed.CoolingPath); Assert.Equal("Cooling", speed.CoolingFsm);
            Assert.Equal("Air cooling", speed.AirState); Assert.Equal("Check temp", speed.CheckState);
            Assert.Equal("RPM", speed.RpmGlobal); Assert.Equal("Water Pump 2", speed.PumpState);
            Assert.Equal("Fan", speed.FanState); Assert.Equal("Motor on?", speed.LeakState);
        }

        [Theory]
        [InlineData("rootPath")] [InlineData("producerPath")] [InlineData("producerFsm")] [InlineData("producerState")]
        [InlineData("speedGlobal")] [InlineData("coolingPath")] [InlineData("coolingFsm")] [InlineData("airState")] [InlineData("checkState")]
        [InlineData("rpmGlobal")] [InlineData("pumpState")] [InlineData("fanState")] [InlineData("leakState")]
        public void MissingMetadataIsIsolated(string field)
        { var json = Catalog(); json["vehicleCooling"]!.AsObject().Remove(field); Reject(json); }

        [Theory]
        [InlineData("producerPath", "SORBET/Simulation")] [InlineData("coolingPath", "CORRIS")]
        [InlineData("coolingPath", "CORRIS/Simulation/CarData")] [InlineData("airState", "Check temp")]
        [InlineData("speedGlobal", " ")] [InlineData("producerPath", "CORRIS/../Simulation")]
        [InlineData("rpmGlobal", "SpeedKMH")] [InlineData("pumpState", "Air cooling")]
        [InlineData("fanState", "Water Pump 2")] [InlineData("leakState", "Fan")]
        [InlineData("rpmGlobal", "RPM/foreign")] [InlineData("fanState", " ")]
        public void ForeignOrAmbiguousBindingsAreIsolated(string field, string value)
        { var json = Catalog(); json["vehicleCooling"]![field] = value; Reject(json); }

        private static void Reject(JsonNode json)
        {
            var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.VehicleCooling); Assert.NotNull(data.VehicleCoolingError);
            Assert.NotNull(data.VehicleHeat); Assert.NotNull(data.VehicleEngineRpm); Assert.NotNull(data.GuestEngineInputs);
        }

        [Theory]
        [InlineData((ushort)0)] [InlineData((ushort)99)] [InlineData((ushort)100)]
        [InlineData((ushort)199)] [InlineData((ushort)200)] [InlineData(ushort.MaxValue)]
        public void CoolingRpmDoesNotRequireMovementOrTorqueAvailability(ushort rpm)
        {
            var state = new VehicleState { VehicleId = 91, OwnerPlayerId = 1, Sequence = 10, Rpm = rpm };
            var wire = PacketCodec.Encode(state); Assert.Equal(35, wire.Length);
            var copy = VehicleStateStreamPolicy.Copy((VehicleState)PacketCodec.Decode(wire));
            Assert.False(copy.MovementSpeedAvailable); Assert.False(copy.TorqueAvailable); Assert.Equal(rpm, copy.Rpm);
            Assert.True(VehicleWearSimulationPolicy.HasSample(copy, 91, 1, true));
            Assert.False(VehicleWearSimulationPolicy.HasSample(copy, 91, 1, false));
            Assert.False(VehicleWearSimulationPolicy.HasSample(copy, 91, 2, true));
        }
    }
}
