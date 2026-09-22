using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class BatteryExternalWriteProtectionTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonObject Battery(JsonNode root) => root["guestEngineProtection"]!["pausedFsms"]!.AsArray()
            .Single(p => (string?)p!["path"] == "CORRIS/Assemblies/VINP_Battery")!.AsObject();

        [Fact]
        public void BatteryDestinationIsProtectedIndependentlyOfTheConsumerGraph()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.Null(parsed.GuestEngineProtectionError); Assert.Null(parsed.GuestEngineInputsError);
            var target = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.Path == "CORRIS/Assemblies/VINP_Battery");
            Assert.True(target.BlockExternalFloatWrites);
            Assert.Equal("CORRIS/Assemblies/VINP_Battery", target.Path); Assert.Equal("Data", target.Fsm);
            Assert.NotNull(parsed.GuestEngineInputs!.Battery);
            Assert.Equal(90, parsed.GuestEngineProtection.Writers.Sum(w => w.Actions.Count));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("1")]
        [InlineData("\"true\"")]
        [InlineData("[]")]
        [InlineData("{}")]
        public void MalformedDestinationPolicyCannotAdmitAnUnprotectedBattery(string value)
        {
            var json = Catalog(); Battery(json)["blockExternalFloatWrites"] = JsonNode.Parse(value);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtection); Assert.NotNull(parsed.GuestEngineProtectionError);
            Assert.Null(parsed.GuestEngineInputs); Assert.NotNull(parsed.GuestEngineInputsError);
            Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.VehicleElectrical); Assert.NotNull(parsed.VehicleTemperature);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingOrDisabledDestinationGuardRejectsBatteryProjection(bool remove)
        {
            var json = Catalog();
            if (remove) Battery(json).Remove("blockExternalFloatWrites"); else Battery(json)["blockExternalFloatWrites"] = false;
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtectionError); Assert.NotNull(parsed.GuestEngineProtection);
            Assert.Null(parsed.GuestEngineInputs); Assert.NotNull(parsed.GuestEngineInputsError);
            Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.VehicleElectrical);
        }

        [Fact]
        public void OtherPausedGraphsKeepTheirExistingPolicyWhenTheOptionIsAbsent()
        {
            var json = Catalog(); var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            var unconfigured = json["guestEngineProtection"]!["pausedFsms"]!.AsArray()
                .Where(p => !p!.AsObject().ContainsKey("blockExternalFloatWrites")).Select(p => p!["path"]!.GetValue<string>()).ToArray();
            Assert.All(parsed.GuestEngineProtection!.PausedFsms.Where(p => unconfigured.Contains(p.Path)),
                p => Assert.False(p.BlockExternalFloatWrites));
        }
    }
}
