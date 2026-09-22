using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PistonEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, int slot = 1, string consumer = "Cylinders") => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "VIN103" && e["slotIndex"]!.GetValue<int>() == slot && e["fsm"]!.GetValue<string>() == consumer)!;

        [Theory]
        [InlineData(1, "Cylinders")] [InlineData(2, "Cylinders")] [InlineData(3, "Cylinders")] [InlineData(4, "Cylinders")]
        [InlineData(1, "Mixture")] [InlineData(2, "Mixture")] [InlineData(3, "Mixture")] [InlineData(4, "Mixture")]
        public void EachConsumerReadsTheCorrectPistonSlotAndNativeOutput(int slot, string consumer)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(102, parsed.GuestEngineInputs!.Entries.Count);
            var entry = Assert.Single(parsed.GuestEngineInputs.Entries, e => e.FamilyPrefix == "VIN103" && e.SlotIndex == slot && e.Fsm == consumer);
            var family = Assert.Single(entry.Families); Assert.Equal(new[] { "Wear", "Tightness" }, family.Scalars);
            Assert.Equal(4, family.SlotCount); Assert.Equal("Pistons", family.SlotReference);
            Assert.Equal("AssemblyDatabase", entry.MountVariable); Assert.Equal("CARPARTS/StartParts/VIN1010/PistonPivots/" + (slot == 1 || slot == 4 ? "1-4" : "2-3") + "/VINP_Piston" + slot, entry.MountPath);
            Assert.Equal("CORRIS/Simulation/Engine/" + (consumer == "Cylinders" ? "Combustion" : "Fuel"), entry.ReaderPath);
            Assert.Equal("Data", entry.InputFsm); Assert.Equal("db_Piston" + slot, entry.TargetVariable);
            var expected = consumer == "Cylinders" ? new[] { "Reset:" + (4 + slot) + ":Wear:Piston" + slot + "Wear", "Cylinder" + slot + ":2:Installed:Installed1" }
                : new[] { "Pistons:" + (2 * (slot - 1)) + ":Wear:Math1" };
            Assert.Equal(expected, entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.False(r.EveryFrame); Assert.Equal(r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType); });
            Assert.Contains(parsed.GuestEngineProtection!.Writers, w => w.Fsm == consumer && w.Path == entry.ReaderPath);
        }

        [Theory]
        [InlineData(1, "Cylinders")] [InlineData(2, "Cylinders")] [InlineData(3, "Cylinders")] [InlineData(4, "Cylinders")]
        [InlineData(1, "Mixture")] [InlineData(2, "Mixture")] [InlineData(3, "Mixture")] [InlineData(4, "Mixture")]
        public void EveryPistonSlotIsRequiredInBothConsumers(int slot, string consumer)
        {
            var json = Catalog(); json["guestEngineInputs"]!["entries"]!.AsArray().Remove(Entry(json, slot, consumer)); AssertFailure(json);
        }

        [Theory]
        [InlineData("zero")] [InlineData("fifth")] [InlineData("missing slot")] [InlineData("duplicate slot")]
        [InlineData("factory mount")] [InlineData("wrong type")] [InlineData("wrong wear field")] [InlineData("missing installation")]
        [InlineData("missing wear")] [InlineData("shared target")] [InlineData("shared read")] [InlineData("protected write")]
        [InlineData("variant")] [InlineData("mixture installation")] [InlineData("wrong consumer")]
        public void PartialOrConflictingPistonMetadataFailsOnlyEngineInputs(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var read = entry["readers"]![0]!;
            switch (scenario)
            {
                case "zero": entry["slotIndex"] = 0; break;
                case "fifth": entry["slotIndex"] = 5; break;
                case "missing slot": entry.AsObject().Remove("slotIndex"); break;
                case "duplicate slot": entry["slotIndex"] = 2; break;
                case "factory mount": entry["mountVariable"] = "VINP"; break;
                case "wrong type": read["actionType"] = "GetFsmBool"; break;
                case "wrong wear field": read["variable"] = "Tightness"; break;
                case "missing installation": entry["readers"]!.AsArray().RemoveAt(1); break;
                case "missing wear": entry["readers"]!.AsArray().RemoveAt(0); break;
                case "shared target": entry["targetVariable"] = "db_Piston2"; break;
                case "shared read": read["actionIndex"] = 6; break;
                case "protected write":
                    var write = json["guestEngineProtection"]!["writers"]!.AsArray().Single(w => w!["fsm"]!.GetValue<string>() == "Cylinders")!["actions"]![0]!;
                    read["state"] = write["state"]!.DeepClone(); read["actionIndex"] = write["index"]!.DeepClone(); break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN104"); break;
                case "mixture installation": var r = Entry(json, 1, "Mixture")["readers"]![0]!; r["variable"] = "Installed"; r["actionType"] = "GetFsmBool"; break;
                case "wrong consumer": entry["fsm"] = "Wearing"; break;
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
