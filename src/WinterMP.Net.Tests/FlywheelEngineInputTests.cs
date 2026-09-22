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
    public class FlywheelEngineInputTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json, string consumer = "Cylinders") => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "VIN120" && e["fsm"]!.GetValue<string>() == consumer)!;

        [Theory]
        [InlineData("VIN120", "SPAWNERS_VIN/Flywheel120")]
        [InlineData("FLYWHEELa0", "SPAWNERS_Aftermarket/FlywheelA")]
        [InlineData("FLYWHEELb0", "SPAWNERS_Aftermarket/FlywheelB")]
        [InlineData("VIN138", "SPAWNERS_VIN/Flexplate138")]
        public void NativeVariantsPublishInertiaAndShareTheAuditedFlywheelMount(string prefix, string path)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.FamilyPrefix == "VIN120" && e.Fsm == "Cylinders");
            Assert.Equal(4, entry.Families.Count);
            var family = Assert.Single(entry.Families, f => f.Prefix == prefix);
            Assert.Equal("CARPARTS/PARTSYSTEM/" + path, family.Path);
            Assert.Equal(new[] { "Wear", "Tightness", "InertiaFactor" }, family.Scalars); Assert.Equal(2, family.Identity.InertiaIndex);
            Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CARPARTS/StartParts/VIN1010/CrankshaftParent/VINP_CrankPulley/VINP_FlywheelFlexplate", entry.MountPath);
            Assert.Equal("CORRIS/Simulation/Engine/Combustion", entry.ReaderPath); Assert.Equal("Cylinders", entry.Fsm);
            Assert.Equal("db_Flywheel", entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(new[] { "Flywheel:0:GetFsmBool:Installed:Installed1", "Flywheel:2:GetFsmFloat:InertiaFactor:EngineInertia" },
                entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.ActionType + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
        }

        [Theory]
        [InlineData("missing source")] [InlineData("missing variant")] [InlineData("duplicate variant")]
        [InlineData("foreign variant")] [InlineData("missing inertia")] [InlineData("missing installed")]
        [InlineData("wrong field")] [InlineData("wrong type")] [InlineData("shared source")]
        [InlineData("shared read")] [InlineData("shared write")] [InlineData("shared output")]
        [InlineData("wrong mount")] [InlineData("slot")] [InlineData("wrong Data")]
        [InlineData("missing timing")] [InlineData("unprotected consumer")]
        public void InvalidInputBindingFailsWithinItsSubsystem(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var reads = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing source": json["guestEngineInputs"]!["entries"]!.AsArray().Remove(entry); break;
                case "missing variant": entry["alternateFamilies"]!.AsArray().RemoveAt(1); break;
                case "duplicate variant": entry["alternateFamilies"]![1] = "FLYWHEELa0"; break;
                case "foreign variant": entry["alternateFamilies"]![1] = "VIN105"; break;
                case "missing inertia": reads.RemoveAt(1); break;
                case "missing installed": reads.RemoveAt(0); break;
                case "wrong field": reads[1]!["variable"] = "Wear"; break;
                case "wrong type": reads[1]!["actionType"] = "GetFsmBool"; break;
                case "shared source": entry["targetVariable"] = "db_Crankshaft"; break;
                case "shared read": reads[0]!["state"] = "Powertrain"; reads[0]!["actionIndex"] = 2; break;
                case "shared write": reads[1]!["state"] = "Timing belt wear"; reads[1]!["actionIndex"] = 1; break;
                case "shared output": reads[1]!["output"] = "Installed1"; break;
                case "wrong mount": entry["mountVariable"] = "PartBlocking"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "wrong Data": entry["inputFsm"] = "Wear"; break;
                case "missing timing": reads[1]!.AsObject().Remove("everyFrame"); break;
                case "unprotected consumer": entry["fsm"] = "Other"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs);
            Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotNull(parsed.GuestEngineProtection);
        }

        [Theory]
        [InlineData("VIN120")] [InlineData("FLYWHEELa0")] [InlineData("FLYWHEELb0")] [InlineData("VIN138")]
        public void EveryVariantMustPublishTheNativeInertiaField(string prefix)
        {
            var json = Catalog(); var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == prefix)!;
            family["scalars"]!.AsArray().RemoveAt(2);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
        }

        [Theory]
        [InlineData(0f)] [InlineData(-.1f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)] [InlineData(float.Epsilon)]
        public void UnsafeInertiaCannotReplaceAcceptedStateOrMaterialize(float inertia)
        {
            var rule = Rule(); var replica = new ReplacementPartReplica(new[] { rule }, new ItemSpawnLifecycle());
            var good = State(.1f); Assert.True(replica.Receive(good, out uint id));
            var bad = State(inertia); bad.Revision = 2;
            Assert.False(replica.Receive(bad, out _)); Assert.Equal(.1f, replica.Get(id)!.Scalars[2]);
            Assert.False(rule.CanCreate(bad)); Fit(bad); Assert.False(rule.CanCreateFitted(bad));
        }

        [Theory]
        [InlineData(.11f)] [InlineData(.1f)] [InlineData(.085f)] [InlineData(.072f)] [InlineData(.093f)]
        public void HostInertiaSurvivesLooseFittedAndRemovedPacketReplay(float inertia)
        {
            var rule = Rule(); var replica = new ReplacementPartReplica(new[] { rule }, new ItemSpawnLifecycle());
            var state = State(inertia); Assert.True(rule.CanCreate(state));
            Assert.True(replica.Receive((ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state)), out uint id));
            state.Revision++; Fit(state); Assert.True(rule.CanCreateFitted(state));
            Assert.True(replica.Receive((ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state)), out _));
            var removed = State(inertia); removed.Revision = 3; Assert.True(replica.Receive(removed, out _));
            Assert.False(replica.Receive(state, out _)); Assert.Equal(new[] { 90f, 0f, inertia }, replica.Get(id)!.Scalars);
        }

        [Theory]
        [InlineData(-2)] [InlineData(1)] [InlineData(3)]
        public void InvalidInertiaIndexIsRejected(int index) => Assert.Throws<ArgumentException>(() => new ReplacementPartRule(1, "VIN120", 3, 1, inertiaIndex: index));

        [Fact]
        public void DirectNativeFactoriesDoNotInventPackageBindings()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            var direct = parsed.ReplacementParts!.Factories.Where(f => !f.BagOutput
                && !parsed.PartsPackages!.Factories.Any(p => p.ContentsPath == f.Path && p.ContentsFsm == f.Fsm));
            Assert.Equal(new[] { "FLYWHEELa0", "FLYWHEELb0", "VIN120", "VIN137", "VIN138", "VIN212" }, direct.Select(f => f.Prefix).OrderBy(p => p));
        }

        [Theory]
        [InlineData("VIN120")] [InlineData("FLYWHEELa0")] [InlineData("FLYWHEELb0")] [InlineData("VIN138")]
        public void StarterAndCombustionRequireTheSameFourHostFlywheelVariants(string prefix)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entries = parsed.GuestEngineInputs!.Entries.Where(e => e.FamilyPrefix == "VIN120").ToArray();
            Assert.Equal(2, entries.Length);
            var starter = Assert.Single(entries, e => e.Fsm == "Starter"); var combustion = Assert.Single(entries, e => e.Fsm == "Cylinders");
            Assert.Equal("CORRIS/Simulation/STARTERxCorris", starter.ReaderPath);
            Assert.Equal(combustion.MountPath, starter.MountPath); Assert.Equal(combustion.MountVariable, starter.MountVariable);
            Assert.Equal("db_Flywheel", starter.TargetVariable); Assert.Equal("Data", starter.InputFsm);
            Assert.Same(Assert.Single(combustion.Families, f => f.Prefix == prefix), Assert.Single(starter.Families, f => f.Prefix == prefix));
            var read = Assert.Single(starter.Readers);
            Assert.Equal("Check Flywheel", read.State); Assert.Equal(0, read.ActionIndex); Assert.Equal("GetFsmBool", read.ActionType);
            Assert.Equal("Installed", read.Variable); Assert.Equal("Installed2", read.Output); Assert.False(read.EveryFrame);
            Assert.Null(starter.WiringSource); Assert.Equal(0, starter.SlotIndex);
            Assert.Equal(102, parsed.GuestEngineInputs.Entries.Count); Assert.Equal(182, parsed.GuestEngineInputs.Entries.Sum(e => e.Readers.Count));
            Assert.Equal(39, parsed.ReplacementParts!.Factories.Count); Assert.Equal(34, parsed.PartsPackages!.Factories.Count);
        }

        [Theory]
        [InlineData("missing starter")] [InlineData("duplicate consumer")] [InlineData("missing variant")]
        [InlineData("duplicate variant")] [InlineData("foreign variant")] [InlineData("missing reader")]
        [InlineData("wrong field")] [InlineData("wrong type")] [InlineData("shared target")]
        [InlineData("shared read")] [InlineData("protected write")] [InlineData("mount reference")]
        [InlineData("wrong Data")] [InlineData("missing timing")] [InlineData("slot")]
        public void StarterFlywheelCannotSilentlyFallBackToGuestSavedInputs(string fault)
        {
            var json = Catalog(); var entry = Entry(json, "Starter"); var read = entry["readers"]![0]!;
            switch (fault)
            {
                case "missing starter": json["guestEngineInputs"]!["entries"]!.AsArray().Remove(entry); break;
                case "duplicate consumer": entry["fsm"] = "Cylinders"; break;
                case "missing variant": entry["alternateFamilies"]!.AsArray().RemoveAt(0); break;
                case "duplicate variant": entry["alternateFamilies"]![1] = "FLYWHEELa0"; break;
                case "foreign variant": entry["alternateFamilies"]![1] = "VIN130"; break;
                case "missing reader": entry["readers"]!.AsArray().RemoveAt(0); break;
                case "wrong field": read["variable"] = "Bolted"; break;
                case "wrong type": read["actionType"] = "GetFsmFloat"; break;
                case "shared target": entry["targetVariable"] = "db_Starter"; break;
                case "shared read": read["state"] = "Wiring"; read["actionIndex"] = 3; break;
                case "protected write": read["state"] = "Fuel Mixture"; read["actionIndex"] = 11; break;
                case "mount reference": entry["mountVariable"] = "PartBlocking"; break;
                case "wrong Data": entry["inputFsm"] = "Other"; break;
                case "missing timing": read.AsObject().Remove("everyFrame"); break;
                case "slot": entry["slotIndex"] = 1; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.NotNull(parsed.ReplacementParts);
            Assert.NotNull(parsed.VehicleEngineRpm); Assert.NotEmpty(parsed.Doors);
        }

        private static ReplacementPartRule Rule() => new ReplacementPartRule(1, "VIN120", 3, 1, inertiaIndex: 2);
        private static ReplacementPartState State(float inertia) => new ReplacementPartState { FactoryId = 1, NativeId = "VIN1207", Revision = 1,
            Scalars = new[] { 90f, 0f, inertia }, Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity, LocalScale = new NetVector3(1, 1, 1) };
        private static void Fit(ReplacementPartState state)
        { state.Installed = true; state.AssemblyId = 1; state.ParentKind = PartParentKind.NativePart; state.ParentId = 123; state.ParentPath = "CrankshaftParent/VINP_CrankPulley/VINP_FlywheelFlexplate"; }
    }
}
