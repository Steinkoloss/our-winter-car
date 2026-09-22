using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class SparkplugEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, int slot = 1) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "SPRKPLUG0" && e["slotIndex"]!.GetValue<int>() == slot)!;

        [Theory]
        [InlineData(1, 4)] [InlineData(2, 3)] [InlineData(3, 2)] [InlineData(4, 1)]
        public void NativeArrayOrderMapsEachSlotToItsCylinder(int slot, int cylinder)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(102, parsed.GuestEngineInputs!.Entries.Count); Assert.Equal(182, parsed.GuestEngineInputs.Entries.Sum(e => e.Readers.Count));
            var entry = Assert.Single(parsed.GuestEngineInputs.Entries, e => e.FamilyPrefix == "SPRKPLUG0" && e.SlotIndex == slot);
            var family = Assert.Single(entry.Families); Assert.Equal(new[] { "Wear", "Tightness", "Durability" }, family.Scalars);
            Assert.Equal("Sparkplugs", family.SlotReference); Assert.Equal(4, family.SlotCount);
            Assert.Equal("AssemblyDatabase", entry.MountVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal("CARPARTS/StartParts/VIN1110/VINP_Sparkplug" + cylinder, entry.MountPath);
            Assert.Equal("CORRIS/Simulation/Engine/Combustion", entry.ReaderPath); Assert.Equal("Cylinders", entry.Fsm);
            Assert.Equal("db_SparkPlug" + cylinder, entry.TargetVariable);
            Assert.Equal(new[] { "Reset:" + (8 + cylinder) + ":Wear:Sparkplug" + cylinder + "Wear",
                "Cylinder" + cylinder + ":3:Installed:Sparkplug" + cylinder, "Add to power " + cylinder + ":2:Tightness:Tightness",
                "Plug data:" + (6 + cylinder) + ":Durability:Sparkplug" + cylinder + "Durability" },
                entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.False(r.EveryFrame); Assert.Equal(r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType); });
            var writer = Assert.Single(parsed.GuestEngineProtection!.Writers, w => w.Fsm == "Cylinders");
            Assert.Equal(2, writer.Actions.Count(a => a.TargetVariable == entry.TargetVariable && a.TargetScalar == "Wear"));
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void MissingSlotDisablesOnlyEngineInputs(int slot)
        { var json = Catalog(); json["guestEngineInputs"]!["entries"]!.AsArray().Remove(Entry(json, slot)); AssertContained(json); }

        [Theory]
        [InlineData("zero slot")] [InlineData("fifth slot")] [InlineData("missing slot")] [InlineData("duplicate slot")]
        [InlineData("shared source")] [InlineData("shared read")] [InlineData("protected write")]
        [InlineData("missing wear")] [InlineData("missing installed")] [InlineData("missing tightness")] [InlineData("missing durability")]
        [InlineData("wrong type")] [InlineData("wrong field")] [InlineData("missing timing")]
        [InlineData("wrong array")] [InlineData("wrong consumer")] [InlineData("wrong Data")] [InlineData("factory reference")]
        [InlineData("variant")] [InlineData("missing protection")]
        public void IncompleteOrConflictingBindingsAreContained(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var reads = entry["readers"]!.AsArray(); var read = reads[0]!;
            switch (scenario)
            {
                case "zero slot": entry["slotIndex"] = 0; break;
                case "fifth slot": entry["slotIndex"] = 5; break;
                case "missing slot": entry.AsObject().Remove("slotIndex"); break;
                case "duplicate slot": entry["slotIndex"] = 2; break;
                case "shared source": entry["targetVariable"] = Entry(json, 2)["targetVariable"]!.DeepClone(); break;
                case "shared read": read["actionIndex"] = Entry(json, 2)["readers"]![0]!["actionIndex"]!.DeepClone(); break;
                case "protected write": read["state"] = "Plug wear 4"; read["actionIndex"] = 1; break;
                case "missing wear": reads.RemoveAt(0); break;
                case "missing installed": reads.RemoveAt(1); break;
                case "missing tightness": reads.RemoveAt(2); break;
                case "missing durability": reads.RemoveAt(3); break;
                case "wrong type": read["actionType"] = "GetFsmBool"; break;
                case "wrong field": read["variable"] = "Dirt"; break;
                case "missing timing": read.AsObject().Remove("everyFrame"); break;
                case "wrong array": json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == "SPRKPLUG0")!["slotReference"] = "Pistons"; break;
                case "wrong consumer": entry["fsm"] = "Mixture"; break;
                case "wrong Data": entry["inputFsm"] = "Wear"; break;
                case "factory reference": entry["mountVariable"] = "VINP"; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN103"); break;
                case "missing protection": var writers = json["guestEngineProtection"]!["writers"]!.AsArray(); writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == "Cylinders")); break;
            }
            AssertContained(json);
        }

        [Fact]
        public void HostPlugScalarsRetainTheirWireOrder()
        {
            var state = new ReplacementPartState { FactoryId = 1, NativeId = "SPRKPLUG071", Revision = 9,
                Scalars = new[] { 7.5f, 3f, .75f }, Rotation = NetQuaternion.Identity };
            var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(PacketCodec.Encode(state)));
            Assert.Equal(state.Scalars, decoded.Scalars);
        }

        private static void AssertContained(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs);
            Assert.False(string.IsNullOrEmpty(parsed.GuestEngineInputsError)); Assert.NotNull(parsed.ShoppingBags);
        }
    }
}
