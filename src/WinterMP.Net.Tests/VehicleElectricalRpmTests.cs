using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleElectricalRpmTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        [Fact]
        public void ElectricalProfileCoversAllThreeNativeRpmReaders()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.VehicleElectricalError);
            var p = parsed.VehicleElectrical!; Assert.Equal("CORRIS", p.RootPath); Assert.Equal("CORRIS/Simulation/Systems/Electrics", p.Path);
            Assert.Equal("Electrics", p.Fsm); Assert.Equal("RPM", p.RpmGlobal); Assert.Equal("Engine running?", p.RunningState);
            Assert.Equal("Charge battery", p.ChargingState); Assert.Equal("Run on battery", p.BatteryState);
        }
        [Theory]
        [InlineData("rootPath")] [InlineData("path")] [InlineData("fsm")] [InlineData("rpmGlobal")]
        [InlineData("runningState")] [InlineData("chargingState")] [InlineData("batteryState")]
        public void EveryElectricalRpmBindingIsRequired(string field)
        {
            var json = Catalog(); json["vehicleElectrical"]!.AsObject().Remove(field); Reject(json);
        }
        [Theory]
        [InlineData("path", "SORBET/Electrics")] [InlineData("path", "CORRIS")]
        [InlineData("path", "CORRIS/../Electrics")] [InlineData("path", "CORRIS/ Electrics")]
        [InlineData("rootPath", "CORRIS/")] [InlineData("fsm", "Electrics/Other")]
        [InlineData("rpmGlobal", " RPM")] [InlineData("rpmGlobal", "")]
        [InlineData("runningState", "Run on battery")] [InlineData("chargingState", "Engine running?")]
        [InlineData("batteryState", "Charge battery")] [InlineData("chargingState", "Charge\nbattery")]
        public void ForeignAliasedOrMalformedElectricalRpmBindingsAreIsolated(string field, string value)
        {
            var json = Catalog(); json["vehicleElectrical"]![field] = value; Reject(json);
        }
        [Theory]
        [InlineData("null")] [InlineData("array")] [InlineData("number")]
        public void InvalidElectricalProfileLeavesOtherVehicleGroupsUsable(string fault)
        {
            var json = Catalog(); json["vehicleElectrical"] = fault == "array" ? new JsonArray() : fault == "number" ? JsonValue.Create(1) : null; Reject(json);
        }
        [Fact]
        public void AbsentElectricalProfileDoesNotInventBindings()
        {
            var json = Catalog(); json.AsObject().Remove("vehicleElectrical"); var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VehicleElectrical); Assert.Null(parsed.VehicleElectricalError); Assert.NotNull(parsed.VehicleCooling);
        }
        private static void Reject(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.VehicleElectrical); Assert.NotEmpty(parsed.VehicleElectricalError!);
            Assert.NotNull(parsed.VehicleCooling); Assert.NotNull(parsed.VehicleTemperature); Assert.NotNull(parsed.GuestEngineInputs); Assert.NotNull(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.Doors);
        }
    }
}
