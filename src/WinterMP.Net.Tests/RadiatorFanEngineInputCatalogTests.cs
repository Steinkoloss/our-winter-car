using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class RadiatorFanEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, string consumer) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "VIN137" && e["fsm"]!.GetValue<string>() == consumer)!;

        [Theory]
        [InlineData("Valves", "Engine/Valves", "Radiator fan", 0, 9)]
        [InlineData("Cooling", "Systems/Cooling", "Fan", 1, 17)]
        public void FanUsesAuditedInstalledReadersAndNestedFactoryMount(string consumer, string path, string state, int index, int sources)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(102, parsed.GuestEngineInputs!.Entries.Count);
            Assert.Equal(182, parsed.GuestEngineInputs.Entries.Sum(e => e.Readers.Count));
            var entry = Assert.Single(parsed.GuestEngineInputs.Entries, e => e.FamilyPrefix == "VIN137" && e.Fsm == consumer);
            var family = Assert.Single(entry.Families);
            Assert.Equal("CARPARTS/PARTSYSTEM/SPAWNERS_VIN/RadiatorFan137", family.Path);
            Assert.Equal(new[] { "Wear", "Tightness" }, family.Scalars);
            var pulley = Assert.Single(parsed.ReplacementParts!.Factories, f => f.Prefix == "VIN127");
            Assert.Equal("CARPARTS/PARTSYSTEM/SPAWNERS_VIN/WaterpumpPulley127", pulley.Path);
            Assert.NotEqual(pulley.Identity.FactoryId, family.Identity.FactoryId);
            Assert.Equal("CARPARTS/StartParts/VIN1010/WaterpumpParent/VINP_RadiatorFan", entry.MountPath);
            Assert.Equal("VINP", entry.MountVariable); Assert.Equal("db_RadiatorFan", entry.TargetVariable);
            Assert.Equal("CORRIS/Simulation/" + path, entry.ReaderPath); Assert.Equal("Data", entry.InputFsm);
            var read = Assert.Single(entry.Readers); Assert.Equal(state, read.State); Assert.Equal(index, read.ActionIndex);
            Assert.Equal("GetFsmBool", read.ActionType); Assert.Equal("Installed", read.Variable);
            Assert.Equal("Installed1", read.Output); Assert.False(read.EveryFrame);
            var inputs = parsed.GuestEngineInputs.Entries.Where(e => e.Fsm == consumer).ToArray();
            Assert.Equal(sources, inputs.Length); Assert.Equal(sources, inputs.Select(e => e.TargetVariable).Distinct().Count());
            Assert.Single(parsed.GuestEngineProtection!.Writers, w => w.Fsm == consumer && w.Path == entry.ReaderPath);
        }

        [Theory]
        [InlineData("Valves")] [InlineData("Cooling")]
        public void PulleyFamilyCannotStandInForRadiatorFan(string consumer)
        {
            var json = Catalog(); Entry(json, consumer)["familyPrefix"] = "VIN127";
            AssertContained(json);
        }

        [Theory]
        [InlineData("Valves")] [InlineData("Cooling")]
        public void MissingFanConsumerFailsOnlyInputProfile(string consumer)
        {
            var json = Catalog(); json["guestEngineInputs"]!["entries"]!.AsArray().Remove(Entry(json, consumer));
            AssertContained(json);
        }

        [Theory]
        [InlineData("missing read")] [InlineData("wrong type")] [InlineData("wrong field")]
        [InlineData("shared source")] [InlineData("read overlap")] [InlineData("write overlap")]
        [InlineData("missing timing")] [InlineData("wrong mount reference")] [InlineData("variant")]
        [InlineData("slot")] [InlineData("wrong Data")] [InlineData("missing protection")]
        public void InvalidFanProfileIsContained(string scenario)
        {
            var json = Catalog(); var entry = Entry(json, "Cooling"); var reads = entry["readers"]!.AsArray(); var read = reads[0]!;
            switch (scenario)
            {
                case "missing read": reads.Clear(); break;
                case "wrong type": read["actionType"] = "GetFsmFloat"; break;
                case "wrong field": read["variable"] = "Tightness"; break;
                case "shared source": entry["targetVariable"] = "db_FanBelt"; break;
                case "read overlap": read["actionIndex"] = 3; break;
                case "write overlap":
                    var write = json["guestEngineProtection"]!["writers"]!.AsArray().Single(w => w!["fsm"]!.GetValue<string>() == "Cooling")!["actions"]![0]!;
                    read["state"] = write["state"]!.DeepClone(); read["actionIndex"] = write["index"]!.DeepClone(); break;
                case "missing timing": read.AsObject().Remove("everyFrame"); break;
                case "wrong mount reference": entry["mountVariable"] = "PartBlocking"; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("FANBELT0"); break;
                case "slot": entry["slotIndex"] = 1; break;
                case "wrong Data": entry["inputFsm"] = "Wear"; break;
                case "missing protection":
                    var writers = json["guestEngineProtection"]!["writers"]!.AsArray(); writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == "Cooling")); break;
            }
            AssertContained(json);
        }

        private static void AssertContained(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs);
            Assert.False(string.IsNullOrEmpty(parsed.GuestEngineInputsError)); Assert.NotNull(parsed.ReplacementParts);
            Assert.NotNull(parsed.ShoppingBags);
        }
    }
}
