using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class AlternatorEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Entry(JsonNode json) => json["guestEngineInputs"]!["entries"]!.AsArray()
            .Single(e => e!["familyPrefix"]?.GetValue<string>() == "VIN133" && e["fsm"]!.GetValue<string>() == "Oil")!;

        [Fact]
        public void BothAlternatorsSupplyTheThreeAuditedMechanicalInputs()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.FamilyPrefix == "VIN133" && e.Fsm == "Oil");
            Assert.Equal(new[] { "VIN133", "ALTERNATOR0" }, entry.Families.Select(f => f.Prefix));
            Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_Alternator", entry.MountPath);
            Assert.Equal("VINP", entry.MountVariable); Assert.Equal("db_Alternator", entry.TargetVariable);
            Assert.Equal("CORRIS/Simulation/Engine/Oil", entry.ReaderPath); Assert.Equal("Oil", entry.Fsm);
            Assert.Equal("Data", entry.InputFsm); Assert.Equal(0, entry.SlotIndex);
            Assert.All(entry.Families, f => {
                Assert.Equal(new[] { "Wear", "Tightness", "SettingRotation", "Friction", "Durability", "Efficiency" }, f.Scalars);
                Assert.Equal(6, f.Identity.ScalarCount); Assert.Equal(1, f.Identity.TightnessIndex);
                Assert.Contains(f.References, r => r.Target == "InstallPoint" && r.Source == "VINP");
                Assert.NotNull(f.HandRotation);
            });
            Assert.Equal(new[] { "Starting engine:3:GetFsmFloat:Friction:AlternatorFrictionRate", "Alternator:1:GetFsmBool:Installed:Installed1",
                "Alternator:3:GetFsmFloat:Wear:Wear" }, entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.ActionType + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
            Assert.Equal(10, parsed.GuestEngineInputs.Entries.Count(e => e.Fsm == "Oil"));
        }

        [Theory]
        [InlineData("VIN133", "Friction")] [InlineData("ALTERNATOR0", "Friction")]
        [InlineData("VIN133", "mount")] [InlineData("ALTERNATOR0", "mount")]
        public void EachVariantMustPublishFrictionAndUseTheSameFactoryMount(string prefix, string missing)
        {
            var json = Catalog(); var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == prefix)!;
            if (missing == "mount") family["references"]![0]!["source"] = "OtherMount";
            else family["scalars"]!.AsArray().Remove(family["scalars"]!.AsArray().Single(v => v!.GetValue<string>() == missing));
            AssertFailure(json);
        }

        [Theory]
        [InlineData("missing entry")] [InlineData("missing variant")] [InlineData("wrong variant")]
        [InlineData("duplicate variant")] [InlineData("missing reader")] [InlineData("wrong field")]
        [InlineData("wrong type")] [InlineData("shared source")] [InlineData("write overlap")]
        [InlineData("shared action")] [InlineData("shared output")] [InlineData("slot")]
        public void PartialOrConflictingAlternatorMetadataFailsOnlyItsInputProfile(string scenario)
        {
            var json = Catalog(); var entry = Entry(json); var readers = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing entry": json["guestEngineInputs"]!["entries"]!.AsArray().Remove(entry); break;
                case "missing variant": entry.AsObject().Remove("alternateFamilies"); break;
                case "wrong variant": entry["alternateFamilies"]![0] = "FUELPUMP0"; break;
                case "duplicate variant": entry["alternateFamilies"]!.AsArray().Add("ALTERNATOR0"); break;
                case "missing reader": readers.RemoveAt(0); break;
                case "wrong field": readers[0]!["variable"] = "Efficiency"; break;
                case "wrong type": readers[0]!["actionType"] = "GetFsmBool"; break;
                case "shared source": entry["targetVariable"] = "db_Waterpump"; break;
                case "write overlap": readers[0]!["state"] = "Wearing 2"; readers[0]!["actionIndex"] = 4; break;
                case "shared action": readers[0]!["state"] = "Water Pump"; readers[0]!["actionIndex"] = 3; break;
                case "shared output": readers[0]!["output"] = "Wear"; break;
                case "slot": entry["slotIndex"] = 1; break;
            }
            AssertFailure(json);
        }

        [Fact]
        public void ElectricalConsumerRequiresBothRepeatedBoolReadsAndActualHostScalars()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.Fsm == "Electrics" && e.FamilyPrefix == "VIN133");
            Assert.Equal("VIN133", entry.FamilyPrefix); Assert.Equal("CORRIS/Simulation/Systems/Electrics", entry.ReaderPath);
            Assert.Equal(new[] { "VIN133", "ALTERNATOR0" }, entry.Families.Select(f => f.Prefix));
            Assert.All(entry.Families, f => { Assert.Equal("Damaged", f.AlternatorDamageVariable); Assert.True(f.Identity.SupportsAlternatorDamage); });
            Assert.Equal(new[] { "Check alternator:1:Efficiency:Efficiency", "Check alternator:3:Installed:Installed2",
                "Alternator damage:0:Damaged:Damaged", "State 1:1:Durability:DurabilityAlternator", "Alternator eff:0:Wear:AlternatorCondition",
                "Run on alternator:2:Damaged:Damaged", "State 2:0:Installed:Installed1" }, entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.False(r.EveryFrame); Assert.Equal(r.Variable == "Damaged" || r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType); });
        }

        [Theory]
        [InlineData("VIN133", "Durability")] [InlineData("ALTERNATOR0", "Durability")]
        [InlineData("VIN133", "Efficiency")] [InlineData("ALTERNATOR0", "Efficiency")]
        [InlineData("VIN133", "Damaged")] [InlineData("ALTERNATOR0", "Damaged")]
        public void ElectricalInputsCannotOmitAnyVariantValue(string prefix, string missing)
        {
            var json = Catalog(); var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == prefix)!;
            if (missing == "Damaged") family.AsObject().Remove("alternatorDamageVariable");
            else family["scalars"]!.AsArray().Remove(family["scalars"]!.AsArray().Single(v => v!.GetValue<string>() == missing));
            AssertFailure(json);
        }

        [Theory]
        [InlineData("missing repeat")] [InlineData("extra damaged")] [InlineData("wrong bool type")]
        [InlineData("unrelated shared output")] [InlineData("duplicate action")] [InlineData("writer overlap")]
        public void RepeatedReadsStillRequireCompleteNonConflictingElectricalBindings(string scenario)
        {
            var json = Catalog(); var entry = json["guestEngineInputs"]!["entries"]!.AsArray().Single(e => e!["fsm"]!.GetValue<string>() == "Electrics" && e["familyPrefix"]?.GetValue<string>() == "VIN133")!;
            var readers = entry["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing repeat": readers.RemoveAt(5); break;
                case "extra damaged": readers[6]!["variable"] = "Damaged"; break;
                case "wrong bool type": readers[5]!["actionType"] = "GetFsmFloat"; break;
                case "unrelated shared output": readers[3]!["output"] = "AlternatorCondition"; break;
                case "duplicate action": readers[5]!["state"] = "Alternator damage"; readers[5]!["actionIndex"] = 0; break;
                case "writer overlap": readers[5]!["state"] = "Wear"; readers[5]!["actionIndex"] = 1; break;
            }
            AssertFailure(json);
        }

        [Theory]
        [InlineData("wrong variable")] [InlineData("wrong family")] [InlineData("wrong type")]
        public void MountDamageMetadataIsRestrictedToTheAuditedAlternatorFamilies(string scenario)
        {
            var json = Catalog(); var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == (scenario == "wrong family" ? "VIN132" : "VIN133"))!;
            family["alternatorDamageVariable"] = scenario == "wrong variable" ? "Installed" : "Damaged";
            if (scenario == "wrong type") family["alternatorDamageVariable"] = true;
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        private static void AssertFailure(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs); Assert.False(string.IsNullOrEmpty(parsed.GuestEngineInputsError));
            Assert.NotNull(parsed.GuestEngineProtection); Assert.Null(parsed.GuestEngineProtectionError);
            Assert.NotNull(parsed.ReplacementParts); Assert.NotNull(parsed.ShoppingBags); Assert.NotEmpty(parsed.Doors);
        }
    }
}
