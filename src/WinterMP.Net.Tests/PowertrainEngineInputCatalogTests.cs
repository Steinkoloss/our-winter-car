using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PowertrainEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, string family = "VIN102", string consumer = "Cylinders") => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == family && e["fsm"]!.GetValue<string>() == consumer)!;

        [Theory]
        [InlineData("VIN102", "Cylinders", "CrankshaftParent/VINP_CrankPulley/VINP_Crankshaft", "Engine/Combustion", "db_Crankshaft", "Powertrain:2:Installed:Installed3|Crank:0:Wear:Wear")]
        [InlineData("VIN102", "Oil", "CrankshaftParent/VINP_CrankPulley/VINP_Crankshaft", "Engine/Oil", "db_Crankshaft", "Crank wear:0:Wear:Wear")]
        [InlineData("VIN102", "Wearing", "CrankshaftParent/VINP_CrankPulley/VINP_Crankshaft", "Engine/Oil", "db_Crankshaft", "Pressure leak:1:Wear:Condition")]
        [InlineData("VIN105", "Cylinders", "CrankshaftParent/VINP_CrankPulley", "Engine/Combustion", "db_CrankPulley", "Powertrain:6:Installed:Installed7")]
        [InlineData("VIN109", "Cylinders", "VINP_AuxShaftSprocket", "Engine/Combustion", "db_AuxshaftSprocket", "Powertrain:5:Installed:Installed6")]
        [InlineData("VIN110", "Cylinders", "VINP_AuxShaft", "Engine/Combustion", "db_Auxshaft", "Powertrain:4:Installed:Installed5")]
        [InlineData("VIN110", "FuelLine", "VINP_AuxShaft", "Engine/Fuel", "db_AuxShaft", "Fuel Usage:9:Wear:AuxShaftWear")]
        public void EveryNativeSourceMatchesItsFactoryMountAndExistingHostScalars(string family, string consumer, string mount, string path, string target, string reads)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.FamilyPrefix == family && e.Fsm == consumer);
            Assert.Equal(new[] { "Wear", "Tightness" }, Assert.Single(entry.Families).Scalars);
            Assert.Equal("CARPARTS/StartParts/VIN1010/" + mount, entry.MountPath); Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CORRIS/Simulation/" + path, entry.ReaderPath); Assert.Equal(target, entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(reads.Split('|'), entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.Equal(r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType); Assert.False(r.EveryFrame); });
            Assert.Contains(parsed.GuestEngineProtection!.Writers, w => w.Path == entry.ReaderPath && w.Fsm == consumer);
        }

        [Theory]
        [InlineData("VIN102", "Cylinders")] [InlineData("VIN102", "Oil")] [InlineData("VIN102", "Wearing")]
        [InlineData("VIN105", "Cylinders")] [InlineData("VIN109", "Cylinders")]
        [InlineData("VIN110", "Cylinders")] [InlineData("VIN110", "FuelLine")]
        public void MissingAnyAuditedSourceFailsOnlyInputProjection(string family, string consumer)
        {
            var json = Catalog(); json["guestEngineInputs"]!["entries"]!.AsArray().Remove(Entry(json, family, consumer)); AssertFailure(json);
        }

        [Theory]
        [InlineData("missing wear")] [InlineData("missing installation")] [InlineData("wrong field")]
        [InlineData("wrong type")] [InlineData("shared source")] [InlineData("shared read")]
        [InlineData("shared write")] [InlineData("shared output")] [InlineData("wrong mount reference")]
        [InlineData("variant")] [InlineData("slot")]
        public void IncompleteOrConflictingPowertrainBindingsAreRejected(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var readers = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing wear": readers.RemoveAt(1); break;
                case "missing installation": readers.RemoveAt(0); break;
                case "wrong field": readers[1]!["variable"] = "Tightness"; break;
                case "wrong type": readers[1]!["actionType"] = "GetFsmBool"; break;
                case "shared source": entry["targetVariable"] = "db_Camshaft"; break;
                case "shared read": readers[0]!["actionIndex"] = 3; break;
                case "shared write": readers[1]!["state"] = "Timing belt wear"; readers[1]!["actionIndex"] = 1; break;
                case "shared output": readers[1]!["output"] = "Installed3"; break;
                case "wrong mount reference": entry["mountVariable"] = "PartBlocking"; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN105"); break;
                case "slot": entry["slotIndex"] = 1; break;
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
