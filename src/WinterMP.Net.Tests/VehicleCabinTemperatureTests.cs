using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleCabinTemperatureTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Fact]
        public void CabinAndHeaterHaveSeparateNativeSources()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.VehicleTemperatureError);
            var source = parsed.VehicleTemperature!.Sources[0]; var p = source.CabinInputs!;
            Assert.Equal("CORRIS/Simulation/CarTempCorris", p.CabinPath); Assert.Equal("Data", p.CabinFsm); Assert.Equal("Data", p.CabinState);
            Assert.Equal("CORRIS/Simulation/Electricity/PowerON/HeaterUnit", p.HeaterPath);
            Assert.Equal("Function", p.HeaterFsm); Assert.Equal("Calc defrosting", p.HeaterState);
            Assert.Equal("EngineTemp", source.EngineGlobal); Assert.Equal("CoolantTemp", source.SourceVariable);
            Assert.Null(parsed.VehicleTemperature.Sources[1].CabinInputs); Assert.Null(parsed.VehicleTemperature.Sources[2].CabinInputs);
        }

        [Theory]
        [InlineData("cabinPath")] [InlineData("cabinFsm")] [InlineData("cabinState")]
        [InlineData("heaterPath")] [InlineData("heaterFsm")] [InlineData("heaterState")]
        public void EveryCabinBindingIsRequired(string field)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!["cabinInputs"]!.AsObject().Remove(field); Reject(json);
        }

        [Theory]
        [InlineData("cabinPath", "SORBET/Simulation/CarTemp")]
        [InlineData("heaterPath", "SORBET/Simulation/Heater")]
        [InlineData("cabinPath", "CORRIS/Simulation/Systems/Cooling")]
        [InlineData("heaterPath", "CORRIS/Simulation/Systems/Cooling")]
        [InlineData("cabinPath", "CORRIS/Simulation/Electricity/PowerON/HeaterUnit")]
        [InlineData("heaterPath", "CORRIS/Simulation/CarTempCorris")]
        [InlineData("cabinPath", "CORRIS/../CarTemp")]
        [InlineData("heaterPath", "CORRIS/ Heater")]
        [InlineData("cabinFsm", " Data")]
        [InlineData("heaterFsm", "Function/Other")]
        [InlineData("cabinState", "")]
        [InlineData("heaterState", "Calc\ndefrosting")]
        public void InvalidOrAmbiguousCabinBindingsAreIsolated(string field, string value)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!["cabinInputs"]![field] = value; Reject(json);
        }

        [Theory]
        [InlineData("missing")] [InlineData("null")] [InlineData("array")] [InlineData("driver")]
        [InlineData("cabin gauge")] [InlineData("heater gauge")]
        public void HostCabinGroupCannotBeOmittedOrAppliedToDriverThermalSources(string fault)
        {
            var json = Catalog(); var sources = json["vehicleTemperature"]!["sources"]!; var first = sources[0]!;
            switch (fault)
            {
                case "missing": first.AsObject().Remove("cabinInputs"); break;
                case "null": first["cabinInputs"] = null; break;
                case "array": first["cabinInputs"] = new JsonArray(); break;
                case "driver": sources[1]!["cabinInputs"] = first["cabinInputs"]!.DeepClone(); break;
                case "cabin gauge": first["cabinInputs"]!["cabinPath"] = first["gaugePath"]!.GetValue<string>(); break;
                case "heater gauge": first["cabinInputs"]!["heaterPath"] = first["gaugePath"]!.GetValue<string>(); break;
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
