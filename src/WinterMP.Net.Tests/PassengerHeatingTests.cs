using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class PassengerHeatingTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Theory]
        [InlineData(-100f, 0)] [InlineData(-40f, 0)] [InlineData(0f, 128)]
        [InlineData(40f, 255)] [InlineData(100f, 255)]
        [InlineData(float.MinValue, 0)] [InlineData(float.MaxValue, 255)]
        public void FiniteCabinDegreesKeepTheExistingQuantizedRange(float degrees, byte expected)
        {
            Assert.True(VehicleClimate.TryQuantizeCabinTemperature(degrees, out byte wire));
            Assert.Equal(expected, wire);
            Assert.InRange(Math.Abs(VehicleClimate.DequantizeCabinTemperature(wire) - Math.Max(-40f, Math.Min(40f, degrees))), 0f, 80f / 255f / 2f + .00001f);
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteCabinSourceIsUnavailableWithNeutralByte(float degrees)
        {
            Assert.False(VehicleClimate.TryQuantizeCabinTemperature(degrees, out byte wire));
            Assert.Equal(128, wire);
        }

        [Theory]
        [InlineData(false, 0)] [InlineData(false, 128)] [InlineData(false, 255)]
        [InlineData(true, 0)] [InlineData(true, 128)] [InlineData(true, 255)]
        public void AvailabilityAndTheIgnoredUnavailableByteSurviveWireAndCache(bool available, byte temperature)
        {
            var state = new VehicleClimate { VehicleId = 91, OwnerPlayerId = 1, Sequence = 0,
                Flags = (byte)(7 | (available ? VehicleClimate.FlagCabinTemperature : 0)), CabinTemp = temperature };
            var bytes = PacketCodec.Encode(state); Assert.Equal(28, bytes.Length);
            var read = Assert.IsType<VehicleClimate>(PacketCodec.Decode(bytes));
            Assert.Equal(available, read.HasCabinTemperature); Assert.Equal(temperature, read.CabinTemp);
            Assert.True(VehicleClimateStreamPolicy.IsValid(read));
            var copy = VehicleClimateStreamPolicy.Copy(read); read.Flags = 16; read.CabinTemp = 77;
            Assert.Equal(available, copy.HasCabinTemperature); Assert.Equal(temperature, copy.CabinTemp);
        }

        [Fact]
        public void AvailabilityCannotBypassOwnerAndSequenceChecks()
        {
            var p = new VehicleClimateStreamPolicy();
            var state = new VehicleClimate { VehicleId = 91, OwnerPlayerId = 1, Sequence = 5, Flags = VehicleClimate.FlagCabinTemperature };
            Assert.True(p.Receive(state, true, false, 1)); state.Flags = 0;
            Assert.False(p.Receive(state, true, false, 1)); state.Sequence++;
            Assert.False(p.Receive(state, true, false, 2)); Assert.True(p.Receive(state, true, false, 1));
            state.Sequence++; state.Flags = 16; Assert.False(p.Receive(state, true, false, 1));
            state.Flags = VehicleClimate.FlagCabinTemperature; Assert.True(p.Receive(state, true, false, 1));
        }

        [Fact]
        public void ShippedHeatingProfileIdentifiesBothNativeBodyReads()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); var climate = parsed.VehicleClimate!;
            Assert.Null(climate.PassengerHeatingError); var p = climate.PassengerHeating!;
            Assert.Equal("BodyTemp", p.BodyPath); Assert.Equal("Calculations", p.BodyFsm);
            Assert.Equal("Get temp", p.AmbientState); Assert.Equal("Source", p.HeatState);
            Assert.Equal("Rain", p.RainPath); Assert.Equal("Rain", p.RainVariable); Assert.Equal("RoofCheck", p.RainFsm);
            Assert.Equal("HeatSource", p.HeatVariable); Assert.Equal("Data", p.HeatFsm); Assert.Equal("Temperature", p.TemperatureVariable);
        }

        [Theory]
        [InlineData("bodyPath")] [InlineData("bodyFsm")] [InlineData("ambientState")] [InlineData("heatState")]
        [InlineData("rainPath")] [InlineData("rainVariable")] [InlineData("rainFsm")]
        [InlineData("heatVariable")] [InlineData("heatFsm")] [InlineData("temperatureVariable")]
        public void MissingHeatingBindingKeepsOtherClimateRules(string field)
        {
            var json = Catalog(); json["vehicleClimate"]!["passengerHeating"]!.AsObject().Remove(field); Reject(json);
        }

        [Theory]
        [InlineData("bodyPath", "../BodyTemp")] [InlineData("rainPath", "Rain/")]
        [InlineData("bodyFsm", " Calculations")] [InlineData("heatVariable", "Heat/Source")]
        public void InvalidHeatingNamesFailLocally(string field, string value)
        {
            var json = Catalog(); json["vehicleClimate"]!["passengerHeating"]![field] = value; Reject(json);
        }

        [Theory]
        [InlineData("null")] [InlineData("[]")] [InlineData("false")]
        public void MalformedHeatingProfileFailsLocally(string profile)
        {
            var json = Catalog(); json["vehicleClimate"]!["passengerHeating"] = JsonNode.Parse(profile); Reject(json);
        }

        [Fact]
        public void MissingOptionalProfileKeepsOlderCatalogsUsable()
        {
            var json = Catalog(); json["vehicleClimate"]!.AsObject().Remove("passengerHeating");
            var climate = SyncCatalogJson.Parse(json.ToJsonString()).VehicleClimate!;
            Assert.Null(climate.PassengerHeating); Assert.Null(climate.PassengerHeatingError); Assert.NotNull(climate.PassengerCondensation);
        }

        private static void Reject(JsonNode json)
        {
            var p = SyncCatalogJson.Parse(json.ToJsonString()); var climate = p.VehicleClimate!;
            Assert.Null(climate.PassengerHeating); Assert.NotEmpty(climate.PassengerHeatingError!);
            Assert.NotNull(climate.PassengerCondensation); Assert.NotEmpty(climate.CarTempPathContains); Assert.NotEmpty(p.Doors);
        }
    }
}
