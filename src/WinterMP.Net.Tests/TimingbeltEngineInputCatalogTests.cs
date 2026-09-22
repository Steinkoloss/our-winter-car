using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class TimingbeltEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "VIN107")!;

        [Fact]
        public void TimingBeltUsesActualCombustionReadersWithoutChangingExistingScalars()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.FamilyPrefix == "VIN107");
            var family = Assert.Single(entry.Families); Assert.Equal("VIN107", family.Prefix);
            Assert.Equal(new[] { "Wear", "Tightness" }, family.Scalars); Assert.Equal("CARPARTS/PARTSYSTEM/SPAWNERS_VIN/TimingBelt107", family.Path);
            Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_TimingBelt", entry.MountPath); Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CORRIS/Simulation/Engine/Combustion", entry.ReaderPath); Assert.Equal("Cylinders", entry.Fsm);
            Assert.Equal("db_TimingBelt", entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(new[] { "Powertrain:1:GetFsmBool:Installed:Installed2", "Timing belt:0:GetFsmFloat:Wear:Wear" },
                entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.ActionType + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
            var sources = parsed.GuestEngineInputs.Entries.Where(e => e.Fsm == "Cylinders").ToArray(); Assert.Equal(30, sources.Length);
            Assert.Equal(sources.Length, sources.Select(e => e.TargetVariable).Distinct().Count());
            var writer = Assert.Single(parsed.GuestEngineProtection!.Writers, w => w.Fsm == "Cylinders");
            Assert.Contains(writer.Actions, w => w.State == "Timing belt wear" && w.Index == 1
                && w.ActionType == "SubtractFsmFloat" && w.TargetVariable == "db_TimingBelt" && w.TargetScalar == "Wear");
        }

        [Theory]
        [InlineData("missing source")] [InlineData("missing installed")] [InlineData("missing wear")]
        [InlineData("wrong type")] [InlineData("wrong scalar")] [InlineData("shared source")]
        [InlineData("overlapping write")] [InlineData("overlapping read")] [InlineData("shared output")]
        [InlineData("wrong mount reference")] [InlineData("variant")] [InlineData("slot")]
        public void InvalidBeltInputMetadataFailsOnlyTheInputProfile(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var reads = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing source": json["guestEngineInputs"]!["entries"]!.AsArray().Remove(entry); break;
                case "missing installed": reads.RemoveAt(0); break;
                case "missing wear": reads.RemoveAt(1); break;
                case "wrong type": reads[1]!["actionType"] = "GetFsmBool"; break;
                case "wrong scalar": reads[1]!["variable"] = "Durability"; break;
                case "shared source": entry["targetVariable"] = "db_Camshaft"; break;
                case "overlapping write": reads[1]!["state"] = "Timing belt wear"; reads[1]!["actionIndex"] = 1; break;
                case "overlapping read": reads[0]!["state"] = "Powertrain"; reads[0]!["actionIndex"] = 3; break;
                case "shared output": reads[1]!["output"] = "Installed2"; break;
                case "wrong mount reference": entry["mountVariable"] = "PartBlocking"; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("FANBELT0"); break;
                case "slot": entry["slotIndex"] = 1; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs);
            Assert.False(string.IsNullOrEmpty(parsed.GuestEngineInputsError)); Assert.NotNull(parsed.GuestEngineProtection);
            Assert.Null(parsed.GuestEngineProtectionError); Assert.NotNull(parsed.ReplacementParts); Assert.NotNull(parsed.ShoppingBags);
        }
    }
}
