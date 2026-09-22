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
    public class IgnitionCoilEngineInputTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "VIN212")!;

        [Fact]
        public void NativeCoilFactorySuppliesOnlyItsOwnIgnitionPrerequisite()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.FamilyPrefix == "VIN212");
            var family = Assert.Single(entry.Families);
            Assert.Equal("CARPARTS/PARTSYSTEM/SPAWNERS_VIN/IgnitionCoil212", family.Path); Assert.Equal("Spawn", family.Fsm);
            Assert.Equal(new[] { "Wear", "Tightness" }, family.Scalars); Assert.Equal(1, family.Identity.TightnessIndex);
            Assert.Equal(new[] { "GetChild", "RandomFloat", "GetOwner", "GetName", "BuildStringFast", "BuildStringFast", "BuildStringFast", "BuildStringFast", "BuildStringFast", "Exists" }, family.InitActions);
            Assert.Equal(new[] { "BuildStringFast", "SetName", "SetScale", "SetIsKinematic", "IntCompare" }, family.StatusActions);
            Assert.Equal("CORRIS/Assemblies/VINP_IgnitionCoil", entry.MountPath); Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CORRIS/Simulation/Engine/Combustion", entry.ReaderPath); Assert.Equal("Cylinders", entry.Fsm);
            Assert.Equal("db_IgnitionCoil", entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            var read = Assert.Single(entry.Readers); Assert.Equal("Ignition", read.State); Assert.Equal(0, read.ActionIndex);
            Assert.Equal("GetFsmBool", read.ActionType); Assert.Equal("Installed", read.Variable); Assert.Equal("Installed1", read.Output); Assert.False(read.EveryFrame);
            Assert.NotNull(Assert.Single(parsed.GuestEngineInputs.Entries, e => e.TargetVariable == "w_IgnitionCoil").WiringSource);
            Assert.DoesNotContain(parsed.PartsPackages!.Factories, p => p.ContentsPath == family.Path && p.ContentsFsm == family.Fsm);
        }

        [Theory]
        [InlineData("missing source")] [InlineData("missing installed")] [InlineData("wrong field")]
        [InlineData("wrong type")] [InlineData("shared source")] [InlineData("shared read")]
        [InlineData("shared write")] [InlineData("wrong mount")] [InlineData("slot")]
        [InlineData("wrong Data")] [InlineData("missing timing")] [InlineData("unprotected consumer")]
        [InlineData("variant")] [InlineData("missing factory")] [InlineData("missing mount reference")]
        public void InvalidCoilBindingIsContained(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var read = entry["readers"]![0]!;
            var families = json["replacementParts"]!["factories"]!.AsArray(); var family = families.Single(f => f!["prefix"]!.GetValue<string>() == "VIN212")!;
            switch (scenario)
            {
                case "missing source": json["guestEngineInputs"]!["entries"]!.AsArray().Remove(entry); break;
                case "missing installed": entry["readers"]!.AsArray().Clear(); break;
                case "wrong field": read["variable"] = "Wear"; break;
                case "wrong type": read["actionType"] = "GetFsmFloat"; break;
                case "shared source": entry["targetVariable"] = "db_Distributor"; break;
                case "shared read": read["actionIndex"] = 1; break;
                case "shared write": read["state"] = "Timing belt wear"; read["actionIndex"] = 1; break;
                case "wrong mount": entry["mountVariable"] = "PartBlocking"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "wrong Data": entry["inputFsm"] = "Wear"; break;
                case "missing timing": read.AsObject().Remove("everyFrame"); break;
                case "unprotected consumer": entry["fsm"] = "Other"; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN131"); break;
                case "missing factory": families.Remove(family); break;
                case "missing mount reference": family["references"]![0]!["target"] = "Other"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs);
            Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotNull(parsed.GuestEngineProtection);
        }

        [Theory]
        [InlineData("VIN2120", 0f)] [InlineData("VIN2121", 99f)] [InlineData("VIN2127", 57.125f)]
        public void SavedAndFreshCoilsRetainConditionThroughVehicleFitAndRemoval(string name, float wear)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); var rule = parsed.ReplacementParts!.Factories.Single(f => f.Prefix == "VIN212").Identity;
            var replica = new ReplacementPartReplica(new[] { rule }, new ItemSpawnLifecycle());
            var state = new ReplacementPartState { FactoryId = rule.FactoryId, NativeId = name, Revision = 1, Scalars = new[] { wear, 0f },
                Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity, LocalScale = new NetVector3(1, 1, 1) };
            Assert.True(rule.CanCreate(state)); Assert.True(replica.Receive(state, out uint id));
            state.Revision++; state.Installed = true; state.AssemblyId = 1; state.ParentKind = PartParentKind.Vehicle;
            state.ParentId = 902148; state.ParentPath = "Assemblies/VINP_IgnitionCoil";
            Assert.True(rule.CanCreateFitted(state)); var fitted = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state));
            Assert.True(replica.Receive(fitted, out _)); Assert.Equal(wear, replica.Get(id)!.Scalars[0]);
            state.Revision++; state.Installed = false; state.AssemblyId = 0; state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = "";
            Assert.True(replica.Receive(state, out _)); Assert.False(replica.Receive(fitted, out _));
            Assert.Equal(new[] { wear, 0f }, replica.Get(id)!.Scalars); Assert.False(replica.Get(id)!.Installed);
        }
    }
}
