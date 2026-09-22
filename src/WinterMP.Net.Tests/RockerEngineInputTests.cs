using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class RockerEngineInputTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static ReplacementPartState State(int slot) => new ReplacementPartState {
            AssemblyId = slot, Installed = true, ParentKind = PartParentKind.NativePart, ParentId = 7, ParentPath = "Rockers/Slot" };

        [Theory]
        [InlineData(0, false)] [InlineData(.999f, false)] [InlineData(1, true)] [InlineData(1.001f, true)] [InlineData(8, true)]
        [InlineData(-1, false)] [InlineData(float.NaN, false)] [InlineData(float.PositiveInfinity, false)] [InlineData(float.NegativeInfinity, false)]
        public void AllSlotsFollowTheNativeFiniteTightnessThreshold(float tightness, bool expected)
        {
            for (byte slot = 1; slot <= 8; slot++) Assert.Equal(expected, PartSlotPolicy.RockerBoltedInput(State(slot), slot, tightness));
        }

        [Theory]
        [InlineData("missing")] [InlineData("uninstalled")] [InlineData("no assembly")] [InlineData("wrong assembly")]
        [InlineData("unresolved")] [InlineData("zero slot")] [InlineData("ninth slot")]
        public void UnavailableOrMisassignedRockersCannotSatisfyAnotherSlot(string scenario)
        {
            ReplacementPartState? state = State(3); byte slot = 3;
            switch (scenario)
            {
                case "missing": state = null; break;
                case "uninstalled": state.Installed = false; break;
                case "no assembly": state.AssemblyId = 0; break;
                case "wrong assembly": state.AssemblyId = 4; break;
                case "unresolved": state.ParentKind = PartParentKind.None; break;
                case "zero slot": slot = 0; break;
                case "ninth slot": slot = 9; state.AssemblyId = 9; break;
            }
            Assert.False(PartSlotPolicy.RockerBoltedInput(state, slot, 8));
        }

        [Fact]
        public void EveryCylinderRequiresTwoDistinctAuditedRockerSlots()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(102, parsed.GuestEngineInputs!.Entries.Count);
            var entries = parsed.GuestEngineInputs.Entries.Where(e => e.FamilyPrefix == "VIN117").ToArray(); Assert.Equal(8, entries.Length);
            Assert.Equal(Enumerable.Range(1, 8).Select(i => (byte)i), entries.Select(e => e.SlotIndex));
            var family = Assert.Single(parsed.ReplacementParts!.Factories, f => f.Prefix == "VIN117");
            Assert.Equal(new[] { "Wear", "Tightness" }, family.Scalars); Assert.Equal(8, family.SlotCount); Assert.Equal("Rockers", family.SlotReference);
            foreach (var entry in entries)
            {
                int slot = entry.SlotIndex, cylinder = (slot + 1) / 2; bool exhaust = slot % 2 == 1;
                Assert.Same(family, Assert.Single(entry.Families)); Assert.Equal("AssemblyDatabase", entry.MountVariable);
                Assert.Equal("CORRIS/Simulation/Engine/Combustion", entry.ReaderPath); Assert.Equal("Cylinders", entry.Fsm);
                Assert.Equal("Rocker" + slot, entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
                Assert.Equal("CARPARTS/StartParts/VIN1110/Valves" + (exhaust ? "Exhaust/Cyl" + cylinder + "Exh" : "Intake/Cyl" + cylinder + "In")
                    + "/VINP_Rocker" + slot + (exhaust ? "Exh" : "In"), entry.MountPath);
                var reader = Assert.Single(entry.Readers); Assert.Equal("Cylinder" + cylinder, reader.State);
                Assert.Equal(exhaust ? 4 : 5, reader.ActionIndex); Assert.Equal(exhaust ? "Installed3" : "Installed4", reader.Output);
                Assert.Equal("GetFsmBool", reader.ActionType); Assert.Equal("Bolted", reader.Variable); Assert.False(reader.EveryFrame);
            }
        }

        [Theory]
        [InlineData("missing slot")] [InlineData("duplicate slot")] [InlineData("zero slot")] [InlineData("ninth slot")]
        [InlineData("fractional slot")] [InlineData("string slot")] [InlineData("missing index")] [InlineData("wrong database")]
        [InlineData("wrong family slots")] [InlineData("wrong slot array")] [InlineData("missing tightness")]
        [InlineData("wrong type")] [InlineData("wrong bool")] [InlineData("duplicate action")] [InlineData("duplicate source")]
        [InlineData("slotted distributor")]
        public void InvalidSlotMetadataCannotLeavePartiallyEnabledCylinderInputs(string scenario)
        {
            var json = Catalog(); var entries = json["guestEngineInputs"]!["entries"]!.AsArray(); var first = entries[11]!;
            var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == "VIN117")!;
            switch (scenario)
            {
                case "missing slot": entries.RemoveAt(18); break;
                case "duplicate slot": entries[18]!["slotIndex"] = 1; break;
                case "zero slot": first["slotIndex"] = 0; break;
                case "ninth slot": first["slotIndex"] = 9; break;
                case "fractional slot": first["slotIndex"] = 1.5; break;
                case "string slot": first["slotIndex"] = "1"; break;
                case "missing index": first.AsObject().Remove("slotIndex"); break;
                case "wrong database": first["mountVariable"] = "VINP"; break;
                case "wrong family slots": family["slotCount"] = 7; break;
                case "wrong slot array": family["slotReference"] = "Mainbearings"; break;
                case "missing tightness": family["scalars"]![1] = "Other"; break;
                case "wrong type": first["readers"]![0]!["actionType"] = "GetFsmFloat"; break;
                case "wrong bool": first["readers"]![0]!["variable"] = "Installed"; break;
                case "duplicate action": entries[12]!["readers"]![0]!["actionIndex"] = 4; break;
                case "duplicate source": entries[12]!["targetVariable"] = "Rocker1"; break;
                case "slotted distributor": entries[0]!["slotIndex"] = 1; break;
            }
            if (scenario == "missing tightness")
            {
                // Tightness is mandatory for the base replacement identity too.
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString())); return;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.ShoppingBags);
        }
    }
}
