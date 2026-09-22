using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleHeatTests
    {
        private static VehicleState State(float torque = 120) => new VehicleState {
            VehicleId = 91, OwnerPlayerId = 1, Sequence = 5, Rpm = 2500, Gear = 3,
            TorqueAvailable = true, EngineTorque = torque };

        [Theory]
        [InlineData(-100f)] [InlineData(0f)] [InlineData(120f)] [InlineData(float.MaxValue)] [InlineData(float.MinValue)]
        public void NativeTorqueRoundTripsWithoutQuantizationAndIsCopied(float torque)
        {
            var original = State(torque); byte[] packet = PacketCodec.Encode(original);
            Assert.Equal(43, packet.Length); Assert.Equal(1, packet[17]);
            var read = (VehicleState)PacketCodec.Decode(packet); var copy = VehicleStateStreamPolicy.Copy(read);
            Assert.True(copy.TorqueAvailable); Assert.Equal(torque, copy.EngineTorque); Assert.Equal(packet, PacketCodec.Encode(copy));
            read.TorqueAvailable = false; read.EngineTorque = 0; Assert.True(copy.TorqueAvailable); Assert.Equal(torque, copy.EngineTorque);
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonFiniteTorqueCannotEnterAnAcceptedStream(float torque)
        {
            var state = State(torque); Assert.False(state.ValidTorque); Assert.False(VehicleStateStreamPolicy.IsValid(state));
            Assert.False(new VehicleStateStreamPolicy().Receive(state, true, false, 1));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var packet = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(torque), 0, packet, 18, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        }

        [Fact]
        public void UnavailableTorqueIsExplicitAndCannotHideAValue()
        {
            var state = State(); state.TorqueAvailable = false;
            Assert.False(VehicleStateStreamPolicy.IsValid(state)); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            state.EngineTorque = 0; var wire = PacketCodec.Encode(state); Assert.Equal(0, wire[17]);
            var read = (VehicleState)PacketCodec.Decode(wire); Assert.False(read.TorqueAvailable); Assert.Equal(0, read.EngineTorque);
            wire[17] = 2; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
            var old = new byte[17]; Array.Copy(wire, old, 17); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(old));
        }

        [Fact]
        public void OldAndForeignPacketsCannotReplacePairedRpmAndTorque()
        {
            var stream = new VehicleStateStreamPolicy(); var current = State(); Assert.True(stream.Receive(current, true, false, 1));
            var accepted = VehicleStateStreamPolicy.Copy(current);
            var old = State(500); old.Sequence = 4; old.Rpm = 65000; Assert.False(stream.Receive(old, true, false, 1));
            old.Sequence = 6; old.OwnerPlayerId = 2; Assert.False(stream.Receive(old, true, false, 1));
            Assert.Equal(120, accepted.EngineTorque); Assert.Equal(2500, accepted.Rpm);
            old.Sequence = 0; Assert.True(stream.Receive(old, true, false, 2));
            var next = VehicleStateStreamPolicy.Copy(old); Assert.Equal(500, next.EngineTorque); Assert.Equal(65000, next.Rpm);
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Fact]
        public void HeatProfileIdentifiesNativeTorqueRatherThanPowerOrDashboard()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.VehicleHeatError);
            var heat = parsed.VehicleHeat!; Assert.Equal("CORRIS", heat.RootPath); Assert.Equal("CORRIS/Simulation/CarData", heat.Path);
            Assert.Equal("HeatGeneration", heat.Fsm); Assert.Equal("Drivetrain", heat.ComponentType);
            Assert.Equal("torque", heat.TorqueMember); Assert.Equal("Power", heat.TorqueVariable);
            Assert.Equal("RPM", heat.RpmGlobal); Assert.Equal("EngineTemp", heat.TemperatureGlobal);
        }

        [Theory]
        [InlineData("rootPath")] [InlineData("path")] [InlineData("fsm")] [InlineData("runningState")]
        [InlineData("stoppedState")] [InlineData("objectVariable")] [InlineData("componentType")]
        [InlineData("torqueMember")] [InlineData("torqueVariable")] [InlineData("rpmGlobal")] [InlineData("temperatureGlobal")]
        public void IncompleteMetadataDoesNotDiscardUnrelatedSync(string field)
        {
            var json = Catalog(); json["vehicleHeat"]!.AsObject().Remove(field); Reject(json);
        }

        [Theory]
        [InlineData("path", "SORBET/CarData")] [InlineData("runningState", "State 2")]
        [InlineData("torqueVariable", "RPM")] [InlineData("temperatureGlobal", "RPM")]
        public void AliasedAndForeignInputsAreRejected(string field, string value)
        { var json = Catalog(); json["vehicleHeat"]![field] = value; Reject(json); }

        private static void Reject(JsonNode json)
        {
            var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.VehicleHeat); Assert.NotEmpty(data.VehicleHeatError!);
            Assert.NotNull(data.VehicleWearInputs); Assert.NotNull(data.GuestEngineInputs); Assert.NotEmpty(data.Doors);
        }
    }
}
