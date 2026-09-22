using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class FanbeltEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, string consumer) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "FANBELT0" && e["fsm"]!.GetValue<string>() == consumer)!;

        [Theory]
        [InlineData("Oil", "Engine/Oil", "db_AlternatorBelt", "Fan belt:0:Installed1")]
        [InlineData("Valves", "Engine/Valves", "db_Fanbelt", "Fan belt:0:Installed1")]
        [InlineData("Cooling", "Systems/Cooling", "db_FanBelt", "Water Pump 2:0:Installed1|Fan:3:Installed1")]
        [InlineData("Electrics", "Systems/Electrics", "db_Fanbelt", "Check alternator:2:Installed1|State 2:1:Installed2")]
        public void AllSixNativeReadsUseTheSameHostBeltThroughIndependentConsumerSources(string fsm, string path, string target, string reads)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entries = parsed.GuestEngineInputs!.Entries.Where(e => e.FamilyPrefix == "FANBELT0").ToArray();
            Assert.Equal(4, entries.Length); Assert.Equal(6, entries.Sum(e => e.Readers.Count));
            var entry = Assert.Single(entries, e => e.Fsm == fsm); var family = Assert.Single(entry.Families);
            Assert.Equal("FANBELT0", family.Prefix); Assert.Equal(new[] { "Wear", "Tightness" }, family.Scalars);
            Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_FanBelt", entry.MountPath); Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CORRIS/Simulation/" + path, entry.ReaderPath); Assert.Equal(target, entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(reads.Split('|'), entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.Equal("Installed", r.Variable); Assert.Equal("GetFsmBool", r.ActionType); Assert.False(r.EveryFrame); });
            Assert.Contains(parsed.GuestEngineProtection!.Writers, w => w.Path == entry.ReaderPath && w.Fsm == fsm);
        }

        [Theory]
        [InlineData("Oil")] [InlineData("Valves")] [InlineData("Cooling")] [InlineData("Electrics")]
        public void OmittingAnyConsumerFailsOnlyTheInputProfile(string consumer)
        {
            var json = Catalog(); json["guestEngineInputs"]!["entries"]!.AsArray().Remove(Entry(json, consumer)); AssertFailure(json);
        }

        [Theory]
        [InlineData("missing repeat")] [InlineData("wrong field")] [InlineData("wrong type")]
        [InlineData("duplicate action")] [InlineData("shared source")] [InlineData("writer overlap")]
        [InlineData("slot")] [InlineData("variant")] [InlineData("unprotected consumer")]
        public void IncompleteOrConflictingBindingsCannotUseSavedMountInputs(string scenario)
        {
            var json = Catalog(); var entry = Entry(json, "Cooling"); var readers = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing repeat": readers.RemoveAt(1); break;
                case "wrong field": readers[1]!["variable"] = "Wear"; break;
                case "wrong type": readers[1]!["actionType"] = "GetFsmFloat"; break;
                case "duplicate action": readers[1]!["state"] = "Water Pump 2"; readers[1]!["actionIndex"] = 0; break;
                case "shared source": entry["targetVariable"] = "db_Waterpump"; break;
                case "writer overlap": var write = json["guestEngineProtection"]!["writers"]!.AsArray().Single(w => w!["fsm"]!.GetValue<string>() == "Cooling")!["actions"]![0]!;
                    readers[1]!["state"] = write["state"]!.DeepClone(); readers[1]!["actionIndex"] = write["index"]!.DeepClone(); break;
                case "slot": entry["slotIndex"] = 1; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN107"); break;
                case "unprotected consumer": entry["readerPath"] = "CORRIS/Simulation/Engine/TimingData"; break;
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
