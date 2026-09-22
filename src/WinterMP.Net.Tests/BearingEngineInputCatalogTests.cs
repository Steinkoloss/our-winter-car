using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class BearingEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, int slot = 1) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "VIN104" && e["slotIndex"]!.GetValue<int>() == slot)!;

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void EveryBearingSlotReadsHostWearAtItsNativePressureCheckpoint(int slot)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(102, parsed.GuestEngineInputs!.Entries.Count);
            var entries = parsed.GuestEngineInputs.Entries.Where(e => e.Fsm == "Wearing").ToArray(); Assert.Equal(10, entries.Length);
            var entry = Assert.Single(entries, e => e.FamilyPrefix == "VIN104" && e.SlotIndex == slot);
            var family = Assert.Single(entry.Families); Assert.Equal(new[] { "Wear", "Tightness" }, family.Scalars);
            Assert.Equal(5, family.SlotCount); Assert.Equal("MainBearings", family.SlotReference);
            Assert.Equal("AssemblyDatabase", entry.MountVariable); Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_Mainbearing" + slot, entry.MountPath);
            Assert.Equal("CORRIS/Simulation/Engine/Oil", entry.ReaderPath); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal("db_MainBearing" + slot, entry.TargetVariable);
            var reader = Assert.Single(entry.Readers); Assert.Equal("Pressure leak", reader.State); Assert.Equal(1 + 2 * slot, reader.ActionIndex);
            Assert.Equal("GetFsmFloat", reader.ActionType); Assert.Equal("Wear", reader.Variable); Assert.Equal("Condition", reader.Output); Assert.False(reader.EveryFrame);
            Assert.Contains(parsed.GuestEngineProtection!.Writers, w => w.Fsm == entry.Fsm && w.Path == entry.ReaderPath);
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void MissingAnyBearingSlotRejectsTheIncompleteInputProfile(int slot)
        {
            var json = Catalog(); json["guestEngineInputs"]!["entries"]!.AsArray().Remove(Entry(json, slot)); AssertFailure(json);
        }

        [Theory]
        [InlineData("zero")] [InlineData("sixth")] [InlineData("missing slot")] [InlineData("duplicate slot")]
        [InlineData("factory mount instead of database")] [InlineData("bolted instead of wear")] [InlineData("wrong type")]
        [InlineData("shared mount reference")] [InlineData("shared read")] [InlineData("protected write")]
        [InlineData("variant")] [InlineData("missing read")] [InlineData("wrong consumer")]
        public void InvalidOrConflictingBearingBindingsFailOnlyEngineInputs(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var reader = entry["readers"]![0]!;
            switch (scenario)
            {
                case "zero": entry["slotIndex"] = 0; break;
                case "sixth": entry["slotIndex"] = 6; break;
                case "missing slot": entry.AsObject().Remove("slotIndex"); break;
                case "duplicate slot": entry["slotIndex"] = 2; break;
                case "factory mount instead of database": entry["mountVariable"] = "VINP"; break;
                case "bolted instead of wear": reader["variable"] = "Bolted"; reader["actionType"] = "GetFsmBool"; break;
                case "wrong type": reader["actionType"] = "GetFsmBool"; break;
                case "shared mount reference": entry["targetVariable"] = "db_MainBearing2"; break;
                case "shared read": reader["actionIndex"] = 1; break;
                case "protected write":
                    var write = json["guestEngineProtection"]!["writers"]!.AsArray().Single(w => w!["fsm"]!.GetValue<string>() == "Wearing")!["actions"]![0]!;
                    reader["state"] = write["state"]!.DeepClone(); reader["actionIndex"] = write["index"]!.DeepClone(); break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN103"); break;
                case "missing read": entry["readers"]!.AsArray().Clear(); break;
                case "wrong consumer": entry["fsm"] = "Cylinders"; break;
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
