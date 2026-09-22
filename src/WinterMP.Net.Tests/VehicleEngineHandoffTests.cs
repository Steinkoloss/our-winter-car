using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleEngineHandoffTests
    {
        private static VehicleState Running() => new VehicleState {
            VehicleId = 42, OwnerPlayerId = 1, Sequence = 10,
            Flags = VehicleState.FlagAccOn | VehicleState.FlagEngineOn, Rpm = 900, Gear = 2 };

        [Fact]
        public void FreshAcceptedEngineIsCopiedBeforeOwnershipChanges()
        {
            var source = Running();
            var claim = VehicleStateStreamPolicy.CaptureEngineClaim(source, 42, 1, true, false, 10, 11);
            Assert.NotNull(claim); Assert.NotSame(source, claim);
            source.Rpm = 0; source.Flags = 0;
            Assert.Equal((ushort)900, claim!.Rpm); Assert.True(claim.EngineOn); Assert.Equal((byte)2, claim.Gear);
        }

        [Theory]
        [InlineData(false, false, 1, 42, 10, 11)]
        [InlineData(true, true, 1, 42, 10, 11)]
        [InlineData(true, false, 255, 42, 10, 11)]
        [InlineData(true, false, 2, 42, 10, 11)]
        [InlineData(true, false, 1, 43, 10, 11)]
        [InlineData(true, false, 1, 42, 11, 11)]
        [InlineData(true, false, 1, 42, 12, 11)]
        [InlineData(true, false, 1, 42, double.NaN, 11)]
        [InlineData(true, false, 1, 42, 10, double.PositiveInfinity)]
        public void UnrelatedOrExpiredStateCannotStartAnEngine(bool seated, bool owned, byte previous, uint vehicle, double now, double expiry)
            => Assert.Null(VehicleStateStreamPolicy.CaptureEngineClaim(Running(), vehicle, previous, seated, owned, now, expiry));

        [Fact]
        public void OffAndAccessoryOnlyArePreservedWithoutInventingARunningEngine()
        {
            foreach (byte flags in new byte[] { 0, VehicleState.FlagAccOn })
            {
                var source = Running(); source.Flags = flags; source.Rpm = 0;
                var result = VehicleStateStreamPolicy.CaptureEngineClaim(source, 42, 1, true, false, 10, 11);
                Assert.NotNull(result); Assert.False(result!.EngineOn); Assert.Equal(flags, result.Flags);
            }
            Assert.Null(VehicleStateStreamPolicy.CaptureEngineClaim(null, 42, 1, true, false, 10, 11));
            var invalid = Running(); invalid.Flags = 128;
            Assert.Null(VehicleStateStreamPolicy.CaptureEngineClaim(invalid, 42, 1, true, false, 10, 11));
        }

        [Theory]
        [InlineData(-100f)] [InlineData(-17.25f)] [InlineData(0f)] [InlineData(34.875f)] [InlineData(300f)]
        public void HandoffCarriesExactSignedTemperatureThroughWireAndOwnershipCopy(float celsius)
        {
            var state = Running(); state.HandoffTemperatureAvailable = true; state.HandoffTemperature = celsius;
            var wire = PacketCodec.Encode(state);
            Assert.Equal(43, wire.Length); Assert.Equal(1, wire[30]); Assert.Equal(celsius, BitConverter.ToSingle(wire, 31));
            var decoded = (VehicleState)PacketCodec.Decode(wire);
            var copy = VehicleStateStreamPolicy.CaptureEngineClaim(decoded, 42, 1, true, false, 10, 11)!;
            decoded.HandoffTemperature = 0; decoded.HandoffTemperatureAvailable = false;
            Assert.True(copy.HandoffTemperatureAvailable); Assert.Equal(celsius, copy.HandoffTemperature);
            Assert.Equal(wire, PacketCodec.Encode(copy));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        [InlineData(-100.01f)] [InlineData(300.01f)]
        public void InvalidTemperatureCannotReachTheNativeSimulator(float celsius)
        {
            var state = Running(); state.HandoffTemperatureAvailable = true; state.HandoffTemperature = celsius;
            Assert.False(VehicleStateStreamPolicy.IsValid(state));
            Assert.Null(VehicleStateStreamPolicy.CaptureEngineClaim(state, 42, 1, true, false, 10, 11));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            state.HandoffTemperature = 0; var wire = PacketCodec.Encode(state);
            Array.Copy(BitConverter.GetBytes(celsius), 0, wire, 31, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
        }

        [Fact]
        public void MissingTemperatureIsDistinctFromFreezingAndCannotHideAValue()
        {
            var state = Running(); var wire = PacketCodec.Encode(state);
            Assert.False(((VehicleState)PacketCodec.Decode(wire)).HandoffTemperatureAvailable);
            wire[30] = 2; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
            state.HandoffTemperature = 1; Assert.False(VehicleStateStreamPolicy.IsValid(state));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            state.HandoffTemperature = 0; state.HandoffTemperatureAvailable = true;
            wire = PacketCodec.Encode(state); Assert.True(((VehicleState)PacketCodec.Decode(wire)).HandoffTemperatureAvailable);
            for (int size = 30; size < 35; size++)
            { var truncated = new byte[size]; Array.Copy(wire, truncated, size); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(truncated)); }
        }

        [Fact]
        public void CatalogOnlyEnablesTheAuditedSorbetHandoff()
        {
            var catalog = SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            Assert.Null(catalog.VehicleEngineHandoffError);
            var source = Assert.Single(catalog.VehicleEngineHandoff!.Sources);
            Assert.Equal("SORBET(190-200psi)", source.RootPath);
            Assert.EndsWith("/Simulation/STARTERxSorbet", source.StarterPath);
            Assert.EndsWith("/IGNITIONxSorbet", source.IgnitionPath);
        }

        [Theory]
        [InlineData("starterPath")]
        [InlineData("ignitionPath")]
        public void HandoffCannotAddressAnotherVehiclesGraph(string field)
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            json["vehicleEngineHandoff"]!["sources"]![0]![field] = "CORRIS/Simulation/STARTERxCorris";
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VehicleEngineHandoff); Assert.NotNull(parsed.VehicleEngineHandoffError);
            Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.VehicleEngineRpm);
        }
    }
}
