using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class FluidEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, string family = "VIN129", string consumer = "Cooling") => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == family && e["fsm"]!.GetValue<string>() == consumer)!;

        [Theory]
        [InlineData("VIN134", "Cylinders", "VIN1010/VINP_Headgasket", "Engine/Combustion", "db_Headgasket", "Head gasket:0:Installed:Installed1|Gasket damage:0:Wear:Wear")]
        [InlineData("VIN134", "Oil", "VIN1010/VINP_Headgasket", "Engine/Oil", "db_Headgasket", "Headgasket:0:Installed:Installed1")]
        [InlineData("VIN129", "Cooling", "VIN1110/VINP_Thermostat", "Systems/Cooling", "db_Thermostat", "Thermostat:0:Installed:Installed1|Thermostat:5:Wear:Wear")]
        [InlineData("VIN128", "Cooling", "VIN1110/VINP_ThermostatHousing", "Systems/Cooling", "db_ThermostatHousing", "Housing tightness:0:Tightness:Tightness1")]
        [InlineData("OILFILTR0", "Oil", "VIN1010/VINP_Oilfilter", "Engine/Oil", "db_Oilfilter", "Oilfilter leak:0:Tightness:Tightness|Oil filter:1:Dirt:Dirt")]
        public void NativeReadersUseTheirFactoryMountAndExistingHostFields(string family, string consumer, string mount, string path, string target, string reads)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(102, parsed.GuestEngineInputs!.Entries.Count);
            var entry = Assert.Single(parsed.GuestEngineInputs.Entries, e => e.FamilyPrefix == family && e.Fsm == consumer);
            Assert.Equal(new[] { family == "OILFILTR0" ? "Dirt" : "Wear", "Tightness" }, Assert.Single(entry.Families).Scalars);
            Assert.Equal("CARPARTS/StartParts/" + mount, entry.MountPath); Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CORRIS/Simulation/" + path, entry.ReaderPath); Assert.Equal(target, entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(reads.Split('|'), entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.Equal(r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType); Assert.False(r.EveryFrame); });
            Assert.Contains(parsed.GuestEngineProtection!.Writers, w => w.Path == entry.ReaderPath && w.Fsm == consumer);
        }

        [Theory]
        [InlineData("VIN134", "Cylinders")] [InlineData("VIN134", "Oil")] [InlineData("VIN129", "Cooling")]
        [InlineData("VIN128", "Cooling")] [InlineData("OILFILTR0", "Oil")]
        public void EveryAuditedFluidSourceIsRequired(string family, string consumer)
        {
            var json = Catalog(); json["guestEngineInputs"]!["entries"]!.AsArray().Remove(Entry(json, family, consumer)); AssertFailure(json);
        }

        [Theory]
        [InlineData("missing wear")] [InlineData("missing installation")] [InlineData("wrong field")] [InlineData("wrong type")]
        [InlineData("shared source")] [InlineData("shared read")] [InlineData("protected write")] [InlineData("shared output")]
        [InlineData("wrong mount reference")] [InlineData("variant")] [InlineData("slot")]
        [InlineData("filter wear instead of dirt")] [InlineData("missing filter tightness")]
        public void IncompleteOrConflictingBindingsFailOnlyEngineInputs(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var readers = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing wear": readers.RemoveAt(1); break;
                case "missing installation": readers.RemoveAt(0); break;
                case "wrong field": readers[1]!["variable"] = "Tightness"; break;
                case "wrong type": readers[1]!["actionType"] = "GetFsmBool"; break;
                case "shared source": entry["targetVariable"] = "db_Waterpump"; break;
                case "shared read": readers[1]!["state"] = "Housing tightness"; readers[1]!["actionIndex"] = 0; break;
                case "protected write": readers[1]!["actionIndex"] = 4; break;
                case "shared output": readers[1]!["output"] = "Installed1"; break;
                case "wrong mount reference": entry["mountVariable"] = "PartBlocking"; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN128"); break;
                case "slot": entry["slotIndex"] = 1; break;
                case "filter wear instead of dirt": Entry(json, "OILFILTR0", "Oil")["readers"]![1]!["variable"] = "Wear"; break;
                case "missing filter tightness": Entry(json, "OILFILTR0", "Oil")["readers"]!.AsArray().RemoveAt(0); break;
            }
            AssertFailure(json);
        }

        private static void AssertFailure(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs);
            Assert.False(string.IsNullOrEmpty(parsed.GuestEngineInputsError)); Assert.NotNull(parsed.GuestEngineProtection);
            Assert.Null(parsed.GuestEngineProtectionError); Assert.NotNull(parsed.ReplacementParts); Assert.NotNull(parsed.ShoppingBags);
        }
    }
}
