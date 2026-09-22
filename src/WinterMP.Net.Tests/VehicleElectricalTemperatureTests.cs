using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleElectricalTemperatureTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Fact]
        public void ElectricalTemperatureReadersUseDistinctMainAndInteriorGraphs()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.VehicleTemperatureError);
            var source = parsed.VehicleTemperature!.Sources[0]; var p = source.ElectricalInputs!;
            Assert.Equal("EngineTemp", source.EngineGlobal); Assert.Equal("CORRIS/Simulation/Systems/Electrics", p.ElectricsPath);
            Assert.Equal("Electrics", p.ElectricsFsm); Assert.Equal("Battery", p.BatteryState); Assert.Equal("Charge battery", p.ChargingState);
            Assert.Equal("CORRIS/InteriorLight/Electrics", p.InteriorPath); Assert.Equal("Consumption", p.InteriorFsm); Assert.Equal("Battery", p.InteriorBatteryState);
            Assert.Null(parsed.VehicleTemperature.Sources[1].ElectricalInputs); Assert.Null(parsed.VehicleTemperature.Sources[2].ElectricalInputs);
        }

        [Theory]
        [InlineData("electricsPath")] [InlineData("electricsFsm")] [InlineData("batteryState")]
        [InlineData("chargingState")] [InlineData("interiorPath")] [InlineData("interiorFsm")] [InlineData("interiorBatteryState")]
        public void EveryElectricalTemperatureBindingIsRequired(string field)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!["electricalInputs"]!.AsObject().Remove(field); Reject(json);
        }

        [Theory]
        [InlineData("electricsPath", "SORBET/Electrics")]
        [InlineData("interiorPath", "SORBET/InteriorLight/Electrics")]
        [InlineData("electricsPath", "CORRIS/Simulation/Systems/Cooling")]
        [InlineData("interiorPath", "CORRIS/Simulation/Systems/Cooling")]
        [InlineData("electricsPath", "CORRIS/InteriorLight/Electrics")]
        [InlineData("interiorPath", "CORRIS/Simulation/Systems/Electrics")]
        [InlineData("electricsPath", "CORRIS/../Electrics")]
        [InlineData("interiorPath", "CORRIS/ InteriorLight")]
        [InlineData("electricsFsm", " Electrics")]
        [InlineData("interiorFsm", "Consumption/Other")]
        [InlineData("batteryState", "")]
        [InlineData("chargingState", "Battery")]
        [InlineData("interiorBatteryState", "Battery\n")]
        public void AmbiguousForeignOrMalformedElectricalTemperatureBindingsFailTogether(string field, string value)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!["electricalInputs"]![field] = value; Reject(json);
        }

        [Theory]
        [InlineData("missing")] [InlineData("null")] [InlineData("array")] [InlineData("driver")]
        [InlineData("main gauge")] [InlineData("interior gauge")]
        public void ElectricalTemperatureInputsRequireACompleteHostSource(string fault)
        {
            var json = Catalog(); var sources = json["vehicleTemperature"]!["sources"]!; var first = sources[0]!;
            switch (fault)
            {
                case "missing": first.AsObject().Remove("electricalInputs"); break;
                case "null": first["electricalInputs"] = null; break;
                case "array": first["electricalInputs"] = new JsonArray(); break;
                case "driver": sources[1]!["electricalInputs"] = first["electricalInputs"]!.DeepClone(); break;
                case "main gauge": first["electricalInputs"]!["electricsPath"] = first["gaugePath"]!.GetValue<string>(); break;
                case "interior gauge": first["electricalInputs"]!["interiorPath"] = first["gaugePath"]!.GetValue<string>(); break;
            }
            Reject(json);
        }

        private static void Reject(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.VehicleTemperature); Assert.NotEmpty(parsed.VehicleTemperatureError!);
            Assert.NotNull(parsed.VehicleCooling); Assert.NotNull(parsed.GuestEngineInputs); Assert.NotNull(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.Doors);
        }
    }
}
