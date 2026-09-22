using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleDifferentialSpeedTests
    {
        private static VehicleState State(float speed = -3150.25f) => new VehicleState {
            VehicleId = 91, OwnerPlayerId = 1, Sequence = 5, Rpm = 3000, SpeedTenthsKmh = 1800,
            MovementSpeedAvailable = true, MovementSpeedTenthsKmh = 500, TorqueAvailable = true, EngineTorque = 123,
            DifferentialSpeedAvailable = true, DifferentialSpeed = speed };

        [Theory]
        [InlineData(0f)] [InlineData(1f)] [InlineData(-1f)] [InlineData(3150.25f)] [InlineData(-3150.25f)]
        [InlineData(float.MaxValue)] [InlineData(float.MinValue)]
        public void ExactSignedFieldIsIndependentFromRpmAndBothRoadSpeedFields(float speed)
        {
            var packet = PacketCodec.Encode(State(speed)); Assert.Equal(35, packet.Length); Assert.Equal(1, packet[25]);
            Assert.Equal(speed, BitConverter.ToSingle(packet, 26));
            var read = (VehicleState)PacketCodec.Decode(packet); var copy = VehicleStateStreamPolicy.Copy(read);
            Assert.True(copy.DifferentialSpeedAvailable); Assert.Equal(speed, copy.DifferentialSpeed);
            Assert.Equal(3000, copy.Rpm); Assert.Equal(1800, copy.SpeedTenthsKmh); Assert.Equal(500, copy.MovementSpeedTenthsKmh);
            Assert.Equal(123, copy.EngineTorque); Assert.Equal(packet, PacketCodec.Encode(copy));
            read.DifferentialSpeedAvailable = false; read.DifferentialSpeed = 0;
            Assert.True(copy.DifferentialSpeedAvailable); Assert.Equal(speed, copy.DifferentialSpeed);
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteValuesCannotBeWrittenDecodedAcceptedOrUsedAsWearSamples(float invalid)
        {
            var state = State(invalid); Assert.False(VehicleStateStreamPolicy.IsValid(state));
            Assert.False(VehicleWearSimulationPolicy.HasSample(state, 91, 1, true));
            Assert.False(new VehicleStateStreamPolicy().Receive(state, true, false, 1));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var packet = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(invalid), 0, packet, 26, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        }

        [Fact]
        public void UnknownCannotCarryAValueAndKnownZeroIsNotUnknown()
        {
            var state = State(); state.DifferentialSpeedAvailable = false;
            Assert.False(VehicleStateStreamPolicy.IsValid(state)); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var packet = PacketCodec.Encode(State()); packet[25] = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
            state.DifferentialSpeed = 0; var unknown = PacketCodec.Encode(state); var known = PacketCodec.Encode(State(0));
            Assert.Equal(0, unknown[25]); Assert.Equal(1, known[25]);
            Assert.False(((VehicleState)PacketCodec.Decode(unknown)).DifferentialSpeedAvailable);
        }

        [Theory]
        [InlineData(2)] [InlineData(255)]
        public void AvailabilityByteRejectsNonBooleanValues(byte value)
        {
            var packet = PacketCodec.Encode(State(0)); packet[25] = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        }

        [Theory]
        [InlineData(25)] [InlineData(26)] [InlineData(27)] [InlineData(28)] [InlineData(29)]
        public void OldAndTruncatedPayloadsCannotSilentlyBecomeUnavailable(int length)
        {
            var packet = PacketCodec.Encode(State()); Array.Resize(ref packet, length);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        }

        [Fact]
        public void OnlyCurrentOwnerCanAdvanceTheSampleAndHostSnapshotRemainsDistinct()
        {
            var stream = new VehicleStateStreamPolicy(); var state = State(); Assert.True(stream.Receive(state, true, false, 1));
            var accepted = VehicleStateStreamPolicy.Copy(state); state.DifferentialSpeed = 15;
            Assert.False(stream.Receive(state, true, false, 1)); state.Sequence++;
            Assert.False(stream.Receive(state, true, true, 1)); Assert.False(stream.Receive(state, true, false, 2));
            state.OwnerPlayerId = 2; state.Sequence = 0; Assert.True(stream.Receive(state, true, false, 2));
            Assert.Equal(-3150.25f, accepted.DifferentialSpeed);
            var snapshot = VehicleStateStreamPolicy.Copy(state); snapshot.OwnerPlayerId = 0; snapshot.Sequence = VehicleState.SnapshotSequence;
            Assert.False(stream.Receive(snapshot, true, false, 2)); Assert.False(stream.Receive(snapshot, false, false, 2));
            Assert.True(stream.Receive(snapshot, false, false, 255));
            Assert.False(VehicleWearSimulationPolicy.HasSample(snapshot, 91, 2, true));
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        [Fact]
        public void CatalogBindsTheAuditedNativePropertyAndCycle()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(data.VehicleDifferentialSpeedError);
            var rule = data.VehicleDifferentialSpeed!;
            Assert.Equal("CORRIS", rule.RootPath); Assert.Equal("CORRIS/Simulation/Systems/Drivetrain", rule.Path);
            Assert.Equal("Wear", rule.Fsm); Assert.Equal("State 1", rule.State); Assert.Equal("Wear", rule.WearState);
            Assert.Equal("State 2", rule.WaitState); Assert.Equal(0, rule.Index);
            Assert.Equal("Drivetrain", rule.ObjectVariable); Assert.Equal("Drivetrain", rule.ComponentType);
            Assert.Equal("differentialSpeed", rule.Member); Assert.Equal("DiffSpeed", rule.Output);
        }

        [Theory]
        [InlineData("rootPath")] [InlineData("path")] [InlineData("fsm")] [InlineData("state")]
        [InlineData("wearState")] [InlineData("waitState")] [InlineData("index")]
        [InlineData("objectVariable")] [InlineData("componentType")] [InlineData("member")] [InlineData("output")]
        public void IncompleteMetadataCannotEnableCapture(string field)
        { var json = Catalog(); json["vehicleDifferentialSpeed"]!.AsObject().Remove(field); Reject(json); }

        [Theory]
        [InlineData("path", "SORBET/Simulation")] [InlineData("path", "CORRIS")]
        [InlineData("path", "CORRIS/../Simulation")] [InlineData("output", " ")]
        [InlineData("member", "../torque")] [InlineData("wearState", "State 1")]
        [InlineData("waitState", "Wear")] [InlineData("waitState", "State 1")]
        public void ForeignAndAmbiguousMetadataIsIsolated(string field, string value)
        { var json = Catalog(); json["vehicleDifferentialSpeed"]![field] = value; Reject(json); }

        [Theory]
        [InlineData("null")] [InlineData("[]")] [InlineData("{}")] [InlineData("true")]
        public void MalformedOptionalProfileKeepsUnrelatedSystemsAvailable(string value)
        { var json = Catalog(); json["vehicleDifferentialSpeed"] = JsonNode.Parse(value); Reject(json); }

        private static void Reject(JsonNode json)
        {
            var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.VehicleDifferentialSpeed);
            Assert.NotEmpty(data.VehicleDifferentialSpeedError!); Assert.NotNull(data.VehicleWearInputs);
            Assert.NotNull(data.GuestEngineProtection); Assert.NotNull(data.VehicleHeat);
        }
    }
}
