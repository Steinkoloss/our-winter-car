using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleTemperatureCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Fact]
        public void InstalledCatalogIdentifiesThreeSeparateNativeCoolantSources()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.Null(parsed.VehicleTemperatureError);
            var sources = parsed.VehicleTemperature!.Sources;
            Assert.Equal(3, sources.Count);
            Assert.Equal(new[] { "CoolantTemp", "Temp", "Temp" }, sources.Select(s => s.SourceVariable));
            Assert.Equal(new[] { "Angle", "Rotation", "Rotation" }, sources.Select(s => s.GaugeVariable));
            Assert.Equal("CORRIS/Simulation/Systems/Cooling", sources[0].SourcePath);
            Assert.Equal("JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi/Radiator", sources[1].SourcePath);
            Assert.Equal("SORBET(190-200psi)/Simulation/CarTempSorbet/Radiator", sources[2].SourcePath);
            Assert.All(sources, s => Assert.Equal("Cooling", s.SourceFsm));
            Assert.True(sources[0].HostAuthoritative); Assert.Equal("Coolant temp 2", sources[0].ReadyState);
            Assert.All(sources.Skip(1), s => { Assert.False(s.HostAuthoritative); Assert.Empty(s.ReadyState); });
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"sources\":[]}")]
        [InlineData("{\"sources\":[null]}")]
        [InlineData("{\"sources\":false}")]
        public void MalformedProfileLeavesUnrelatedSyncAvailable(string profile)
        {
            var json = Catalog(); json["vehicleTemperature"] = JsonNode.Parse(profile); Reject(json);
        }

        [Theory]
        [InlineData("rootPath", "../CORRIS")]
        [InlineData("rootPath", "CORRIS/")]
        [InlineData("rootPath", "CORRIS//x")]
        [InlineData("rootPath", "CORRIS/./x")]
        [InlineData("rootPath", " CORRIS")]
        [InlineData("rootPath", "CORRIS\n")]
        [InlineData("rootPath", "C:\\CORRIS")]
        [InlineData("sourcePath", "SORBET(190-200psi)/Cooling")]
        [InlineData("gaugePath", "CORRIS/Simulation/Systems/Cooling")]
        [InlineData("sourceFsm", "")]
        [InlineData("sourceFsm", " Cooling")]
        [InlineData("sourceVariable", "Cooling/Temp")]
        [InlineData("gaugeVariable", "Angle\t")]
        public void InvalidNamesAndForeignOrAliasedPathsAreRejected(string field, string value)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]![field] = value; Reject(json);
        }

        [Theory]
        [InlineData("rootPath")]
        [InlineData("gaugePath")]
        [InlineData("sourcePath")]
        [InlineData("sourceFsm")]
        [InlineData("sourceVariable")]
        [InlineData("gaugeVariable")]
        [InlineData("hostAuthoritative")]
        [InlineData("readyState")]
        public void EverySourceBindingIsRequired(string field)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!.AsObject().Remove(field); Reject(json);
        }

        [Fact]
        public void DuplicateVehicleRootsAreRejected()
        {
            var json = Catalog(); var sources = json["vehicleTemperature"]!["sources"]!.AsArray();
            sources.Add(sources[0]!.DeepClone()); Reject(json);
        }

        [Theory]
        [InlineData("null")] [InlineData("1")] [InlineData("\"true\"")]
        public void AuthorityMustBeExplicitBoolean(string invalid)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!["hostAuthoritative"] = JsonNode.Parse(invalid); Reject(json);
        }

        [Theory]
        [InlineData("")] [InlineData(" Coolant temp 2")] [InlineData("Cooling/State")]
        public void HostReadinessMustNameALocalState(string state)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!["readyState"] = state; Reject(json);
        }

        [Fact]
        public void DriverSourcesCannotAccidentallyBecomeHostSources()
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![1]!["readyState"] = "Ready"; Reject(json);
        }

        [Fact]
        public void AbsentProfileDoesNotDiscardOlderCatalogRules()
        {
            var json = Catalog(); json.AsObject().Remove("vehicleTemperature");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VehicleTemperature); Assert.Null(parsed.VehicleTemperatureError); Assert.NotEmpty(parsed.Doors);
        }

        private static void Reject(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VehicleTemperature); Assert.NotEmpty(parsed.VehicleTemperatureError!);
            Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.VehicleEngineRpm);
            Assert.NotNull(parsed.GuestEngineInputs); Assert.NotNull(parsed.GuestEngineProtection);
        }
    }
}
