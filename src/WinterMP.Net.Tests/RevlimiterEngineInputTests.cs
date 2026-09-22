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
    public class RevlimiterEngineInputTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "REVLIMITER0")!;

        [Fact]
        public void NativeLimiterUsesVehicleMountAndExistingHostSettingScalar()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.FamilyPrefix == "REVLIMITER0");
            Assert.Equal(102, parsed.GuestEngineInputs.Entries.Count); Assert.Equal(182, parsed.GuestEngineInputs.Entries.Sum(e => e.Readers.Count));
            var family = Assert.Single(entry.Families);
            Assert.Equal("CARPARTS/PARTSYSTEM/SPAWNERS_Fleetari/Revlimiter", family.Path);
            Assert.Equal(new[] { "Tightness", "SettingRPM" }, family.Scalars); Assert.Equal(0, family.Identity.TightnessIndex);
            Assert.Equal("CORRIS/AssembliesTuning/VINP_Revlimiter", entry.MountPath); Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CORRIS/Simulation/Engine/Combustion", entry.ReaderPath); Assert.Equal("Cylinders", entry.Fsm);
            Assert.Equal("db_Revlimiter", entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(new[] { "Limiter:0:GetFsmBool:Installed:Revlimiter", "Limiter:3:GetFsmFloat:SettingRPM:RevlimitRPM" },
                entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.ActionType + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
        }

        [Theory]
        [InlineData("missing source")] [InlineData("missing rpm")] [InlineData("missing installed")]
        [InlineData("wrong field")] [InlineData("wrong type")] [InlineData("shared source")]
        [InlineData("shared read")] [InlineData("shared write")] [InlineData("shared output")]
        [InlineData("wrong mount")] [InlineData("slot")] [InlineData("wrong Data")]
        [InlineData("missing timing")] [InlineData("unprotected consumer")] [InlineData("variant")]
        [InlineData("missing scalar")]
        public void InvalidLimiterBindingFailsWithinItsSubsystem(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var reads = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing source": json["guestEngineInputs"]!["entries"]!.AsArray().Remove(entry); break;
                case "missing rpm": reads.RemoveAt(1); break;
                case "missing installed": reads.RemoveAt(0); break;
                case "wrong field": reads[1]!["variable"] = "Tightness"; break;
                case "wrong type": reads[1]!["actionType"] = "GetFsmBool"; break;
                case "shared source": entry["targetVariable"] = "db_Crankshaft"; break;
                case "shared read": reads[0]!["state"] = "Powertrain"; reads[0]!["actionIndex"] = 2; break;
                case "shared write": reads[1]!["state"] = "Timing belt wear"; reads[1]!["actionIndex"] = 1; break;
                case "shared output": reads[1]!["output"] = "Revlimiter"; break;
                case "wrong mount": entry["mountVariable"] = "PartBlocking"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "wrong Data": entry["inputFsm"] = "Use"; break;
                case "missing timing": reads[1]!.AsObject().Remove("everyFrame"); break;
                case "unprotected consumer": entry["fsm"] = "Other"; break;
                case "variant": entry["alternateFamilies"] = new JsonArray("VIN131"); break;
                case "missing scalar": json["replacementParts"]!["factories"]!.AsArray()
                    .Single(f => f!["prefix"]!.GetValue<string>() == "REVLIMITER0")!["scalars"]!.AsArray().RemoveAt(1); break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs);
            Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotNull(parsed.GuestEngineProtection);
        }

        [Theory]
        [InlineData(0f)] [InlineData(5000f)] [InlineData(6207.625f)] [InlineData(7000f)] [InlineData(9500f)] [InlineData(9999.755f)]
        public void HostSettingSurvivesVehicleFitAdjustmentRemovalAndStaleReplay(float rpm)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            var rule = parsed.ReplacementParts!.Factories.Single(f => f.Prefix == "REVLIMITER0").Identity;
            var replica = new ReplacementPartReplica(new[] { rule }, new ItemSpawnLifecycle());
            var state = State(rule.FactoryId, 7000); Assert.True(rule.CanCreate(state));
            Assert.True(replica.Receive(state, out uint id));
            state.Revision++; state.Installed = true; state.AssemblyId = 1; state.ParentKind = PartParentKind.Vehicle;
            state.ParentId = 902148; state.ParentPath = "AssembliesTuning/VINP_Revlimiter";
            Assert.True(rule.CanCreateFitted(state)); Assert.True(replica.Receive(state, out _));
            state.Revision++; state.Scalars[1] = rpm;
            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state));
            Assert.True(replica.Receive(decoded, out _)); Assert.Equal(rpm, replica.Get(id)!.Scalars[1]);
            var removed = State(rule.FactoryId, rpm); removed.Revision = 4; Assert.True(replica.Receive(removed, out _));
            Assert.False(replica.Receive(decoded, out _)); Assert.Equal(PartParentKind.None, replica.Get(id)!.ParentKind);
            Assert.Equal(rpm, replica.Get(id)!.Scalars[1]);
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteRpmCannotDisplaceAcceptedHostSetting(float rpm)
        {
            var rule = new ReplacementPartRule(1, "REVLIMITER0", 2, 0);
            var replica = new ReplacementPartReplica(new[] { rule }, new ItemSpawnLifecycle());
            Assert.True(replica.Receive(State(1, 7000), out uint id)); var bad = State(1, rpm); bad.Revision = 2;
            Assert.False(replica.Receive(bad, out _)); Assert.Equal(7000f, replica.Get(id)!.Scalars[1]);
        }

        private static ReplacementPartState State(uint factory, float rpm) => new ReplacementPartState { FactoryId = factory,
            NativeId = "REVLIMITER07", Revision = 1, Scalars = new[] { 0f, rpm }, Rotation = NetQuaternion.Identity,
            LocalRotation = NetQuaternion.Identity, LocalScale = new NetVector3(1, 1, 1) };
    }
}
