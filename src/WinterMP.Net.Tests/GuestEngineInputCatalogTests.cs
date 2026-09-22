using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class GuestEngineInputCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonObject Profile(JsonNode json) => json["guestEngineInputs"]!.AsObject();
        private static JsonObject Entry(JsonNode json) => Profile(json)["entries"]![0]!.AsObject();
        private static JsonObject Reader(JsonNode json, int index = 0) => Entry(json)["readers"]![index]!.AsObject();
        private static JsonObject Family(JsonNode json) => json["replacementParts"]!["factories"]!.AsArray()
            .Single(f => f!["prefix"]!.GetValue<string>() == "VIN131")!.AsObject();

        private static void AssertIsolatedFailure(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs);
            Assert.False(string.IsNullOrEmpty(parsed.GuestEngineInputsError));
            Assert.NotEmpty(parsed.Doors);
            Assert.NotNull(parsed.VehicleDamage);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.Null(parsed.GuestEngineProtectionError);
            Assert.NotNull(parsed.VehicleEngineRpm); Assert.Null(parsed.VehicleEngineRpmError);
            Assert.NotNull(parsed.ShoppingBags);
        }

        [Theory]
        [InlineData("Cylinders", "Combustion", "Powertrain:3:GetFsmBool:Installed:Installed4|Break 2:1:GetFsmFloat:ValveTolerance:ValveTolerance|Cam wear:0:GetFsmFloat:Wear:Wear")]
        [InlineData("Wearing", "Oil", "State 4:0:GetFsmFloat:Durability:DurabilityCamshaft")]
        [InlineData("Valves", "Valves", "Get cam profile:0:GetFsmFloat:ValveTolerance:ValveTolerance|Get cam profile:1:GetFsmString:CamProfile:CamProfile")]
        public void AllCamshaftConsumersUseEveryVariantAtTheNestedHeadMount(string consumer, string path, string slots)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entries = parsed.GuestEngineInputs!.Entries.Where(e => e.FamilyPrefix == "VIN115").ToArray(); Assert.Equal(3, entries.Length);
            var entry = Assert.Single(entries, e => e.Fsm == consumer);
            Assert.Equal(new[] { "VIN115", "CAMTUNEa0", "CAMTUNEb0", "CAMTUNEc0", "CAMTUNEd0" }, entry.Families.Select(f => f.Prefix));
            Assert.All(entry.Families, family => {
                Assert.Equal(new[] { "Wear", "Tightness", "Durability", "ValveTolerance" }, family.Scalars);
                Assert.Equal("CamProfile", family.CamProfileVariable); Assert.True(family.Identity.SupportsCamProfile);
                Assert.Contains(family.References, r => r.Target == "InstallPoint" && r.Source == "VINP");
            });
            Assert.Equal("CARPARTS/StartParts/VIN1110/CamParent/VINP_CamshaftSprocket/VINP_CamShaft", entry.MountPath);
            Assert.Equal("CORRIS/Simulation/Engine/" + path, entry.ReaderPath);
            Assert.Equal("VINP", entry.MountVariable); Assert.Equal("db_Camshaft", entry.TargetVariable); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(slots.Split('|'), entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.ActionType + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
        }

        [Theory]
        [InlineData("VIN115")] [InlineData("CAMTUNEa0")] [InlineData("CAMTUNEb0")] [InlineData("CAMTUNEc0")] [InlineData("CAMTUNEd0")]
        public void EveryCamVariantMustPublishItsFloatsProfileAndFactoryMount(string prefix)
        {
            foreach (string field in new[] { "Durability", "ValveTolerance", "CamProfile", "mount" })
            {
                var json = Catalog(); var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == prefix)!;
                if (field == "CamProfile") family.AsObject().Remove("camProfileVariable");
                else if (field == "mount") family["references"]![0]!["source"] = "Wrong";
                else family["scalars"]!.AsArray().Remove(family["scalars"]!.AsArray().Single(v => v!.GetValue<string>() == field));
                AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("missing consumer")] [InlineData("missing variant")] [InlineData("duplicate variant")]
        [InlineData("foreign variant")] [InlineData("missing reader")] [InlineData("wrong string type")]
        [InlineData("wrong string field")] [InlineData("missing protection")] [InlineData("shared target")] [InlineData("write overlap")]
        public void CamshaftInputsCannotLoadWithPartialOrConflictingBindings(string scenario)
        {
            var json = Catalog(); var entries = Profile(json)["entries"]!.AsArray();
            var cam = entries[8]!; var valves = entries[10]!;
            switch (scenario)
            {
                case "missing consumer": entries.RemoveAt(9); break;
                case "missing variant": cam["alternateFamilies"]!.AsArray().RemoveAt(3); break;
                case "duplicate variant": cam["alternateFamilies"]![3] = "CAMTUNEa0"; break;
                case "foreign variant": cam["alternateFamilies"]![3] = "VIN132"; break;
                case "missing reader": valves["readers"]!.AsArray().RemoveAt(1); break;
                case "wrong string type": valves["readers"]![1]!["actionType"] = "GetFsmFloat"; break;
                case "wrong string field": valves["readers"]![1]!["variable"] = "Other"; break;
                case "missing protection":
                    var writers = json["guestEngineProtection"]!["writers"]!.AsArray(); writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == "Valves")); break;
                case "shared target": cam["targetVariable"] = "db_Distributor"; break;
                case "write overlap": valves["readers"]![1]!["state"] = "Cyl1 power 9"; valves["readers"]![1]!["actionIndex"] = 2; break;
            }
            AssertIsolatedFailure(json);
        }

        [Fact]
        public void CatalogProjectsExactlyTheFourAuditedDistributorReads()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(102, parsed.GuestEngineInputs!.Entries.Count);
            var entry = Assert.Single(parsed.GuestEngineInputs.Entries, e => e.FamilyPrefix == "VIN131");
            Assert.Equal("VIN131", entry.FamilyPrefix);
            var family = Assert.Single(parsed.ReplacementParts!.Factories, f => f.Prefix == "VIN131");
            Assert.Equal(family.Identity.FactoryId, entry.FactoryId);
            Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_Distributor", entry.MountPath);
            Assert.Equal("VINP", entry.MountVariable);
            Assert.Contains(family.References, r => r.Source == entry.MountVariable && r.Target == "InstallPoint");
            Assert.Equal("CORRIS/Simulation/Engine/Combustion", entry.ReaderPath);
            Assert.Equal("Cylinders", entry.Fsm); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal("db_Distributor", entry.TargetVariable);
            Assert.Equal(new[] { "Ignition:1:GetFsmBool:Installed:Installed2", "Spark angle?:1:GetFsmFloat:SparkAngle:Angle",
                "Distributor tight?:0:GetFsmFloat:Tightness:Tightness", "Damage?:0:GetFsmFloat:Wear:Wear" },
                entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.ActionType + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
            Assert.All(entry.Readers.Where(r => r.ActionType == "GetFsmFloat"), r => Assert.Contains(r.Variable, family.Scalars));
        }

        [Fact]
        public void MissingOptionalProfileKeepsTheCatalogUsable()
        {
            var json = Catalog(); json.AsObject().Remove("guestEngineInputs");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs); Assert.Null(parsed.GuestEngineInputsError);
            Assert.Equal(39, parsed.ReplacementParts!.Factories.Count);
            Assert.NotNull(parsed.GuestEngineProtection);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("true")]
        [InlineData("1")]
        [InlineData("{}")]
        public void InvalidProfileCannotDisableUnrelatedCatalogRules(string value)
        {
            var json = Catalog(); json["guestEngineInputs"] = JsonNode.Parse(value); AssertIsolatedFailure(json);
        }

        [Fact]
        public void AllCompleteConsumerEntriesAreRequired()
        {
            var json = Catalog(); Profile(json).Remove("entries"); AssertIsolatedFailure(json);
            foreach (string invalid in new[] { "null", "{}", "[]", "[null]", "[1]", "[true]" })
            {
                json = Catalog(); Profile(json)["entries"] = JsonNode.Parse(invalid); AssertIsolatedFailure(json);
            }
            json = Catalog(); Profile(json)["entries"]!.AsArray().Add(Entry(json).DeepClone()); AssertIsolatedFailure(json);
            json = Catalog(); Profile(json)["entries"]!.AsArray().RemoveAt(1); AssertIsolatedFailure(json);
            json = Catalog(); Profile(json)["entries"]![1] = Entry(json).DeepClone(); AssertIsolatedFailure(json);
        }

        [Fact]
        public void StarterInputsUseTheAppendedHostDurabilityAndIndependentConsumer()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.FamilyPrefix == "VIN130");
            var family = Assert.Single(parsed.ReplacementParts!.Factories, f => f.Prefix == "VIN130");
            Assert.Equal(new[] { "Wear", "Tightness", "Durability" }, family.Scalars);
            Assert.Equal(family.Identity.FactoryId, entry.FactoryId);
            Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_Starter", entry.MountPath);
            Assert.Equal("VINP", entry.MountVariable);
            Assert.Equal("CORRIS/Simulation/STARTERxCorris", entry.ReaderPath);
            Assert.Equal("Starter", entry.Fsm); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal("db_Starter", entry.TargetVariable);
            Assert.Equal(new[] { "Wiring:3:GetFsmBool:Installed:Installed5", "Starter damage:0:GetFsmFloat:Wear:Wear",
                "Starter damage:1:GetFsmFloat:Durability:StarterDurability" }, entry.Readers.Select(r =>
                r.State + ":" + r.ActionIndex + ":" + r.ActionType + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
        }

        [Theory]
        [InlineData("Oil", "CORRIS/Simulation/Engine/Oil", "Water Pump:1:Installed:Installed1|Water Pump:3:Wear:Wear|Starting engine:4:Durability:DurabilityWaterPump")]
        [InlineData("Cooling", "CORRIS/Simulation/Systems/Cooling", "Water Pump 2:3:Installed:Installed1|Water Pump 2:6:Wear:Wear|Pump tightness:0:Tightness:Tightness1|Water Pump 2:5:Efficiency:WaterPumpEfficiency")]
        public void WaterpumpConsumersShareTheHostFamilyWithCompleteIndependentReaders(string consumer, string path, string slots)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.Null(parsed.GuestEngineInputsError);
            var family = Assert.Single(parsed.ReplacementParts!.Factories, f => f.Prefix == "VIN126");
            Assert.Equal(new[] { "Wear", "Tightness", "Durability", "Efficiency" }, family.Scalars);
            var entries = parsed.GuestEngineInputs!.Entries.Where(e => e.FamilyPrefix == "VIN126").ToArray();
            Assert.Equal(2, entries.Length);
            var entry = Assert.Single(entries, e => e.Fsm == consumer);
            Assert.Equal(family.Identity.FactoryId, entry.FactoryId);
            Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_Waterpump", entry.MountPath);
            Assert.Equal("VINP", entry.MountVariable); Assert.Equal("db_Waterpump", entry.TargetVariable);
            Assert.Equal(path, entry.ReaderPath); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal(slots.Split('|'), entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => {
                Assert.False(r.EveryFrame);
                Assert.Equal(r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType);
            });
        }

        [Theory]
        [InlineData("missing oil")]
        [InlineData("missing cooling")]
        [InlineData("duplicate oil")]
        [InlineData("swapped consumers")]
        [InlineData("missing durability")]
        [InlineData("missing efficiency")]
        [InlineData("unpublished durability")]
        [InlineData("unpublished efficiency")]
        [InlineData("duplicate wear")]
        [InlineData("wrong efficiency type")]
        [InlineData("missing oil protection")]
        [InlineData("missing cooling protection")]
        public void IncompleteWaterpumpCoverageCannotLeaveAPartiallyEnabledInputProfile(string scenario)
        {
            var json = Catalog(); var entries = Profile(json)["entries"]!.AsArray();
            var oil = entries[2]!; var cooling = entries[3]!;
            switch (scenario)
            {
                case "missing oil": entries.RemoveAt(2); break;
                case "missing cooling": entries.RemoveAt(3); break;
                case "duplicate oil": entries[3] = oil.DeepClone(); break;
                case "swapped consumers": oil["fsm"] = "Cooling"; cooling["fsm"] = "Oil"; break;
                case "missing durability": oil["readers"]!.AsArray().RemoveAt(2); break;
                case "missing efficiency": cooling["readers"]!.AsArray().RemoveAt(3); break;
                case "unpublished durability": case "unpublished efficiency":
                    var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == "VIN126")!;
                    family["scalars"]!.AsArray().RemoveAt(scenario == "unpublished durability" ? 2 : 3); break;
                case "duplicate wear": cooling["readers"]![3]!["variable"] = "Wear"; break;
                case "wrong efficiency type": cooling["readers"]![3]!["actionType"] = "GetFsmBool"; break;
                case "missing oil protection": case "missing cooling protection":
                    var writers = json["guestEngineProtection"]!["writers"]!.AsArray();
                    writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == (scenario == "missing oil protection" ? "Oil" : "Cooling"))); break;
            }
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("FuelLine", "CORRIS/Simulation/Engine/Fuel", "Fuel Pump:0:Installed:Installed1|Fuel Usage:8:Wear:FuelpumpWear|State 2:1:OutputRate:PumpRate")]
        [InlineData("Wearing", "CORRIS/Simulation/Engine/Oil", "State 4:1:Durability:DurabilityFuelpump")]
        public void FuelpumpInputsResolveBothVariantsAndTheExactNativeConsumers(string consumer, string path, string slots)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.Fsm == consumer && e.FamilyPrefix == "VIN125");
            Assert.Equal("VIN125", entry.FamilyPrefix); Assert.Equal("db_Fuelpump", entry.TargetVariable);
            Assert.Equal("VINP", entry.MountVariable); Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_Fuelpump", entry.MountPath);
            Assert.Equal("Data", entry.InputFsm); Assert.Equal(path, entry.ReaderPath);
            Assert.Equal(new[] { "VIN125", "FUELPUMP0" }, entry.Families.Select(f => f.Prefix));
            Assert.All(entry.Families, f => {
                Assert.Same(parsed.ReplacementParts!.Factories.Single(candidate => candidate.Prefix == f.Prefix), f);
                Assert.Equal(new[] { "Wear", "Tightness", "Durability", "OutputRate" }, f.Scalars);
            });
            Assert.Equal(slots.Split('|'), entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.False(r.EveryFrame); Assert.Equal(r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType); });
        }

        [Theory]
        [InlineData("missing fuel")]
        [InlineData("missing wearing")]
        [InlineData("duplicate fuel")]
        [InlineData("unpublished stock output")]
        [InlineData("unpublished racing output")]
        [InlineData("unpublished stock durability")]
        [InlineData("unpublished racing durability")]
        [InlineData("missing racing family")]
        [InlineData("wrong racing mount")]
        [InlineData("extra wearing reader")]
        [InlineData("wrong wearing type")]
        [InlineData("missing wearing protection")]
        public void IncompleteFuelpumpVariantCoverageFailsAsOneProfile(string scenario)
        {
            var json = Catalog(); var entries = Profile(json)["entries"]!.AsArray();
            var factories = json["replacementParts"]!["factories"]!.AsArray();
            var racing = factories.Single(f => f!["prefix"]!.GetValue<string>() == "FUELPUMP0")!;
            if (scenario.StartsWith("unpublished ", StringComparison.Ordinal))
            {
                var family = scenario.Contains("racing") ? racing : factories.Single(f => f!["prefix"]!.GetValue<string>() == "VIN125")!;
                family["scalars"]!.AsArray().RemoveAt(scenario.EndsWith("output", StringComparison.Ordinal) ? 3 : 2);
            }
            else switch (scenario)
            {
                case "missing fuel": entries.RemoveAt(4); break;
                case "missing wearing": entries.RemoveAt(5); break;
                case "duplicate fuel": entries[5] = entries[4]!.DeepClone(); break;
                case "missing racing family": factories.Remove(racing); break;
                case "wrong racing mount": racing["references"]![0]!["source"] = "OtherMount"; break;
                case "extra wearing reader": entries[5]!["readers"]!.AsArray().Add(entries[4]!["readers"]![0]!.DeepClone()); break;
                case "wrong wearing type": entries[5]!["readers"]![0]!["actionType"] = "GetFsmBool"; break;
                case "missing wearing protection":
                    var writers = json["guestEngineProtection"]!["writers"]!.AsArray();
                    writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == "Wearing")); break;
            }
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("\"FUELPUMP0\"")]
        [InlineData("[1]")]
        [InlineData("[\"VIN125\"]")]
        [InlineData("[\"VIN126\"]")]
        [InlineData("[\"FUELPUMP0\",\"FUELPUMP0\"]")]
        public void AlternateFamiliesMustBeExactlyTheAuditedRacingVariant(string value)
        {
            foreach (int index in new[] { 4, 5 })
            {
                var json = Catalog(); Profile(json)["entries"]![index]!["alternateFamilies"] = JsonNode.Parse(value); AssertIsolatedFailure(json);
            }
        }

        [Fact]
        public void RequiredAlternativesCannotBeOmittedOrAddedToOtherParts()
        {
            foreach (int index in new[] { 4, 5 })
            {
                var json = Catalog(); Profile(json)["entries"]![index]!.AsObject().Remove("alternateFamilies"); AssertIsolatedFailure(json);
            }
            var unrelated = Catalog(); Entry(unrelated)["alternateFamilies"] = new JsonArray("FUELPUMP0"); AssertIsolatedFailure(unrelated);
        }

        [Theory]
        [InlineData("Oil", "Oil pump?:0:Installed:Installed1|Oil pump?:2:Wear:Wear")]
        [InlineData("Wearing", "State 4:2:Durability:DurabilityOilpump")]
        public void OilPumpSharesConsumersWithOtherPartsUsingDistinctSourcesAndActions(string consumer, string slots)
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var shared = parsed.GuestEngineInputs!.Entries.Where(e => e.Fsm == consumer).ToArray(); Assert.Equal(10, shared.Length);
            var entry = Assert.Single(shared, e => e.FamilyPrefix == "VIN132");
            var family = Assert.Single(entry.Families); Assert.Equal("VIN132", family.Prefix);
            Assert.Equal(new[] { "Wear", "Tightness", "Durability" }, family.Scalars);
            Assert.Equal("CORRIS/Simulation/Engine/Oil", entry.ReaderPath); Assert.Equal("Data", entry.InputFsm);
            Assert.Equal("CARPARTS/StartParts/VIN1010/VINP_Oilpump", entry.MountPath);
            Assert.Equal("VINP", entry.MountVariable); Assert.Equal("db_Oilpump", entry.TargetVariable);
            Assert.Equal(slots.Split('|'), entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
            Assert.Equal(shared.Length, shared.Select(e => e.TargetVariable).Distinct().Count());
            if (consumer == "Oil")
            {
                // The native graph intentionally reuses these locals in different states.
                Assert.All(shared.Where(e => e.FamilyPrefix != "FANBELT0" && e.FamilyPrefix != "VIN134" && e.FamilyPrefix != "OILFILTR0" && e.RockerCoverSource == null), e => Assert.Contains(e.Readers, r => r.Output == "Wear"));
                Assert.All(shared.Where(e => e.FamilyPrefix != "VIN102" && e.FamilyPrefix != "OILFILTR0" && e.FamilyPrefix != "EngineBlock" && e.OilpanSource == null && e.RockerCoverSource == null), e => Assert.Contains(e.Readers, r => r.Output == "Installed1"));
            }
        }

        [Theory]
        [InlineData("missing oil")]
        [InlineData("missing wearing")]
        [InlineData("duplicate oil")]
        [InlineData("missing durability")]
        [InlineData("wrong mount")]
        [InlineData("duplicate source oil")]
        [InlineData("duplicate source wearing")]
        [InlineData("overlap action oil")]
        [InlineData("overlap action wearing")]
        [InlineData("missing protection")]
        public void IncompleteOrOverlappingOilPumpSourcesFailTheWholeInputProfile(string scenario)
        {
            var json = Catalog(); var entries = Profile(json)["entries"]!.AsArray();
            var oil = entries[6]!; var wearing = entries[7]!;
            switch (scenario)
            {
                case "missing oil": entries.RemoveAt(6); break;
                case "missing wearing": entries.RemoveAt(7); break;
                case "duplicate oil": entries[7] = oil.DeepClone(); break;
                case "missing durability":
                    json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == "VIN132")!["scalars"]!.AsArray().RemoveAt(2); break;
                case "wrong mount": oil["mountVariable"] = "Other"; break;
                case "duplicate source oil": oil["targetVariable"] = "db_Waterpump"; break;
                case "duplicate source wearing": wearing["targetVariable"] = "db_Fuelpump"; break;
                case "overlap action oil":
                    oil["readers"]![0]!["state"] = entries[2]!["readers"]![0]!["state"]!.DeepClone();
                    oil["readers"]![0]!["actionIndex"] = entries[2]!["readers"]![0]!["actionIndex"]!.DeepClone(); break;
                case "overlap action wearing": wearing["readers"]![0]!["actionIndex"] = 1; break;
                case "missing protection":
                    var writers = json["guestEngineProtection"]!["writers"]!.AsArray(); writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == "Oil")); break;
            }
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("missing durability")]
        [InlineData("unpublished durability")]
        [InlineData("duplicate wear")]
        [InlineData("wrong type")]
        [InlineData("extra field")]
        [InlineData("duplicate consumer")]
        [InlineData("missing protection")]
        [InlineData("wrong mount")]
        public void IncompleteStarterProjectionFailsWithoutRemovingOtherCatalogSystems(string scenario)
        {
            var json = Catalog(); var starter = Profile(json)["entries"]![1]!;
            var readers = starter["readers"]!.AsArray();
            switch (scenario)
            {
                case "missing durability": readers.RemoveAt(2); break;
                case "unpublished durability":
                    var family = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == "VIN130")!;
                    family["scalars"]!.AsArray().RemoveAt(2); break;
                case "duplicate wear": readers[2]!["variable"] = "Wear"; break;
                case "wrong type": readers[2]!["actionType"] = "GetFsmBool"; break;
                case "extra field": readers.Add(readers[1]!.DeepClone()); break;
                case "duplicate consumer": starter["readerPath"] = Entry(json)["readerPath"]!.DeepClone(); starter["fsm"] = "Cylinders"; break;
                case "missing protection":
                    var writers = json["guestEngineProtection"]!["writers"]!.AsArray();
                    writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == "Starter")); break;
                case "wrong mount": starter["mountVariable"] = "PartBlocking"; break;
            }
            AssertIsolatedFailure(json);
        }

        [Fact]
        public void AllFourReaderEntriesMustBePresentAndTyped()
        {
            var json = Catalog(); Entry(json).Remove("readers"); AssertIsolatedFailure(json);
            foreach (string invalid in new[] { "null", "{}", "[]", "[null,null,null,null]", "[1,1,1,1]" })
            {
                json = Catalog(); Entry(json)["readers"] = JsonNode.Parse(invalid); AssertIsolatedFailure(json);
            }
            json = Catalog(); Entry(json)["readers"]!.AsArray().RemoveAt(0); AssertIsolatedFailure(json);
            json = Catalog(); Entry(json)["readers"]!.AsArray().Add(Reader(json).DeepClone()); AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("mountPath")]
        [InlineData("readerPath")]
        public void InputPathsMustBeUsableScenePaths(string key)
        {
            var json = Catalog(); Entry(json).Remove(key); AssertIsolatedFailure(json);
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(3), JsonValue.Create(""),
                JsonValue.Create("/CARPARTS"), JsonValue.Create("CARPARTS/"), JsonValue.Create("CARPARTS//Part"),
                JsonValue.Create("CARPARTS/../Part"), JsonValue.Create("CARPARTS/./Part"), JsonValue.Create("CARPARTS/ /Part"),
                JsonValue.Create("CARPARTS\\Part"), JsonValue.Create("CARPARTS:Part"), JsonValue.Create("CARPARTS\tPart"),
                JsonValue.Create(new string('x', 513)) })
            {
                json = Catalog(); Entry(json)[key] = invalid?.DeepClone(); AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("familyPrefix", false)]
        [InlineData("mountVariable", false)]
        [InlineData("fsm", false)]
        [InlineData("inputFsm", false)]
        [InlineData("targetVariable", false)]
        [InlineData("state", true)]
        [InlineData("actionType", true)]
        [InlineData("variable", true)]
        [InlineData("output", true)]
        public void NativeNamesCannotBeMissingEmptyOrAmbiguous(string key, bool reader)
        {
            var json = Catalog(); (reader ? Reader(json) : Entry(json)).Remove(key); AssertIsolatedFailure(json);
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(3), JsonValue.Create(""), JsonValue.Create(" "),
                JsonValue.Create("Part/Data"), JsonValue.Create("Part\\Data"), JsonValue.Create("Part:Data"),
                JsonValue.Create("Part\tData"), JsonValue.Create(new string('x', 129)) })
            {
                json = Catalog(); (reader ? Reader(json) : Entry(json))[key] = invalid?.DeepClone(); AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("VIN133")]
        [InlineData("CARB2BRLa0")]
        [InlineData("VIN1310")]
        public void UnrelatedFamiliesCannotExpandProjectionImplicitly(string prefix)
        {
            var json = Catalog(); Entry(json)["familyPrefix"] = prefix; AssertIsolatedFailure(json);
        }

        [Fact]
        public void FamilyMustExistAndSupplyItsExactMountReference()
        {
            var json = Catalog(); json["replacementParts"]!["factories"]!.AsArray().Remove(Family(json)); AssertIsolatedFailure(json);
            json = Catalog(); Entry(json)["mountVariable"] = "OtherMount"; AssertIsolatedFailure(json);
            json = Catalog(); Family(json)["references"]![0]!["source"] = "OtherMount"; AssertIsolatedFailure(json);
            json = Catalog(); Family(json)["references"]![0]!["target"] = "OtherTarget"; AssertIsolatedFailure(json);
            json = Catalog(); json.AsObject().Remove("replacementParts"); AssertIsolatedFailure(json);
        }

        [Fact]
        public void InputFsmAndInstalledFieldMustAgreeWithMountBindings()
        {
            var json = Catalog(); Entry(json)["inputFsm"] = "OtherData"; AssertIsolatedFailure(json);
            json = Catalog(); json["replacementParts"]!["mountInstalledVariable"] = "OtherInstalled"; AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("Wear")]
        [InlineData("SparkAngle")]
        public void FloatsMustAlreadyBePublishedByTheFamily(string missing)
        {
            var json = Catalog(); var family = Family(json); family.Remove("distributorTiming");
            var scalars = family["scalars"]!.AsArray(); scalars.Remove(scalars.Single(s => s!.GetValue<string>() == missing));
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("GetFsmFloat")]
        [InlineData("SetFsmBool")]
        [InlineData("GetFsmGameObject")]
        public void InstalledRequiresABooleanReader(string actionType)
        {
            var json = Catalog(); Reader(json)["actionType"] = actionType; AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("GetFsmBool")]
        [InlineData("SetFsmFloat")]
        [InlineData("HutongGames.PlayMaker.Actions.GetFsmFloat")]
        public void ScalarsRequireNativeFloatReaders(string actionType)
        {
            var json = Catalog(); Reader(json, 1)["actionType"] = actionType; AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("Bolted", 0)]
        [InlineData("ActivePart", 0)]
        [InlineData("Wear", 1)]
        public void UnsupportedOrDuplicateFieldsCannotReplaceAnAuthoritativeInput(string variable, int index)
        {
            var json = Catalog(); Reader(json, index)["variable"] = variable; AssertIsolatedFailure(json);
        }

        [Fact]
        public void ReaderActionsAndOutputsCannotOverlap()
        {
            var json = Catalog(); Reader(json, 1)["state"] = Reader(json)["state"]!.DeepClone();
            Reader(json, 1)["actionIndex"] = Reader(json)["actionIndex"]!.DeepClone(); AssertIsolatedFailure(json);
            json = Catalog(); Reader(json, 1)["output"] = Reader(json)["output"]!.DeepClone(); AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        public void ReaderActionIndicesAreBounded(int index)
        {
            var json = Catalog(); Reader(json)["actionIndex"] = index; AssertIsolatedFailure(json);
        }

        [Fact]
        public void IndexAndUpdateTimingMustBeExplicitAndTyped()
        {
            foreach (string key in new[] { "actionIndex", "everyFrame" })
            {
                var json = Catalog(); Reader(json).Remove(key); AssertIsolatedFailure(json);
                foreach (string invalid in new[] { "null", "\"0\"", "0.5", "{}" })
                {
                    json = Catalog(); Reader(json)[key] = JsonNode.Parse(invalid); AssertIsolatedFailure(json);
                }
            }
            var value = Catalog(); Reader(value)["everyFrame"] = 0; AssertIsolatedFailure(value);
            value = Catalog(); Reader(value)["actionIndex"] = false; AssertIsolatedFailure(value);
        }

        [Theory]
        [InlineData("readerPath", "CORRIS/Simulation/Engine/Other")]
        [InlineData("fsm", "OtherCylinders")]
        public void ConsumerMustBeTrackedByTheNativeProtectionEntryHook(string field, string value)
        {
            var json = Catalog(); Entry(json)[field] = value; AssertIsolatedFailure(json);
        }

        [Fact]
        public void RemovingConsumerProtectionCannotLeaveAnApparentlyValidInputProfile()
        {
            var json = Catalog(); var writers = json["guestEngineProtection"]!["writers"]!.AsArray();
            writers.Remove(writers.Single(w => w!["fsm"]!.GetValue<string>() == "Cylinders"));
            AssertIsolatedFailure(json);
        }

        [Fact]
        public void MissingProtectionProfileFailsOnlyTheDependentInputProfile()
        {
            var json = Catalog(); json.AsObject().Remove("guestEngineProtection");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtection); Assert.Null(parsed.GuestEngineProtectionError);
            Assert.Null(parsed.GuestEngineInputs); Assert.False(string.IsNullOrEmpty(parsed.GuestEngineInputsError));
            Assert.NotNull(parsed.VehicleEngineRpm); Assert.Equal(39, parsed.ReplacementParts!.Factories.Count);
        }

        [Theory]
        [InlineData("actions")]
        [InlineData("poseActions")]
        public void ReadersCannotOccupySuppressedWriterOrPoseSlots(string listName)
        {
            var json = Catalog(); var writer = json["guestEngineProtection"]!["writers"]!.AsArray()
                .Single(w => w!["fsm"]!.GetValue<string>() == "Cylinders")!;
            var protectedAction = writer[listName]![0]!;
            Reader(json)["state"] = protectedAction["state"]!.DeepClone();
            Reader(json)["actionIndex"] = protectedAction["index"]!.DeepClone();
            AssertIsolatedFailure(json);
        }
    }
}
