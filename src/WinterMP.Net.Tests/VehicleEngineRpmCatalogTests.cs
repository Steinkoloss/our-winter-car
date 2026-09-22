using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleEngineRpmCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonObject Profile(JsonNode json) => json["vehicleEngineRpm"]!.AsObject();
        private static JsonObject Source(JsonNode json) => Profile(json)["sources"]![0]!.AsObject();
        private static JsonObject Producer(JsonNode json, int index = 0) => Source(json)["producers"]![index]!.AsObject();

        private static void AssertIsolatedFailure(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VehicleEngineRpm);
            Assert.False(string.IsNullOrEmpty(parsed.VehicleEngineRpmError));
            Assert.NotEmpty(parsed.Doors);
            Assert.NotNull(parsed.VehicleDamage);
            Assert.NotNull(parsed.GuestEngineProtection);
            Assert.Null(parsed.GuestEngineProtectionError);
            Assert.Equal(39, parsed.ReplacementParts!.Factories.Count);
        }

        [Fact]
        public void CatalogBindsAllEightAuditedCorrisStarterOutputs()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.Null(parsed.VehicleEngineRpmError);
            var source = Assert.Single(parsed.VehicleEngineRpm!.Sources);
            Assert.Equal("CORRIS", source.RootPath);
            Assert.Equal("CORRIS/Simulation/STARTERxCorris", source.ProducerPath);
            Assert.Equal("Starter", source.Fsm);
            Assert.Equal("CarDrivetrain", source.ObjectVariable);
            Assert.Equal("Drivetrain", source.ComponentType);
            Assert.Equal("rpm", source.RpmMember);
            Assert.Equal("RPM", source.GlobalVariable);
            Assert.Equal(8, source.Producers.Count);
            Assert.Equal(new[] { "Crank up:1", "Start engine:0", "Stall engine:0", "Running:7" },
                source.Producers.Where(p => p.ActionType == "GetProperty").Select(p => p.State + ":" + p.Index));
            Assert.All(source.Producers.Take(4), p => {
                Assert.True(p.EveryFrame); Assert.Null(p.SourceVariable); Assert.Null(p.SourceConstant);
            });
            Assert.Equal(new[] { "Turn key:5", "Fuel Mixture:9", "Start or not:8" },
                source.Producers.Where(p => p.SourceVariable != null).Select(p => p.State + ":" + p.Index));
            Assert.All(source.Producers.Skip(4).Take(3), p => {
                Assert.Equal("SetFloatValue", p.ActionType); Assert.True(p.EveryFrame);
                Assert.Equal("StarterSpeed", p.SourceVariable); Assert.Null(p.SourceConstant);
            });
            var reset = source.Producers[7];
            Assert.Equal("Wait", reset.State); Assert.Equal(18, reset.Index);
            Assert.Equal("SetFloatValue", reset.ActionType); Assert.False(reset.EveryFrame);
            Assert.Null(reset.SourceVariable); Assert.Equal(0f, reset.SourceConstant);
        }

        [Fact]
        public void MissingOptionalProfilePreservesCatalogWithoutError()
        {
            var json = Catalog(); json.AsObject().Remove("vehicleEngineRpm");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VehicleEngineRpm); Assert.Null(parsed.VehicleEngineRpmError);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.NotNull(parsed.VehicleDamage);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("{}")]
        public void InvalidProfileCannotDiscardOtherSyncRules(string value)
        {
            var json = Catalog(); json["vehicleEngineRpm"] = JsonNode.Parse(value); AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("sources")]
        [InlineData("producers")]
        public void DiscoveryListsRequireNonemptyObjectEntries(string key)
        {
            var json = Catalog(); var obj = key == "sources" ? Profile(json) : Source(json);
            obj.Remove(key); AssertIsolatedFailure(json);
            foreach (string invalid in new[] { "[]", "null", "{}", "[null]", "[1]", "[true]" })
            {
                json = Catalog(); obj = key == "sources" ? Profile(json) : Source(json);
                obj[key] = JsonNode.Parse(invalid); AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("rootPath")]
        [InlineData("producerPath")]
        public void SourcePathsMustBeExactScenePaths(string key)
        {
            var json = Catalog(); Source(json).Remove(key); AssertIsolatedFailure(json);
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(3), JsonValue.Create(""),
                JsonValue.Create("/CORRIS"), JsonValue.Create("CORRIS/"), JsonValue.Create("CORRIS//Part"),
                JsonValue.Create("CORRIS/../Part"), JsonValue.Create("CORRIS/./Part"), JsonValue.Create("CORRIS/ /Part"),
                JsonValue.Create("CORRIS\\Part"), JsonValue.Create("CORRIS:Part"), JsonValue.Create("CORRIS\tPart"),
                JsonValue.Create(new string('x', 513)) })
            {
                json = Catalog(); Source(json)[key] = invalid?.DeepClone(); AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("fsm")]
        [InlineData("objectVariable")]
        [InlineData("componentType")]
        [InlineData("rpmMember")]
        [InlineData("globalVariable")]
        public void SourceSignaturesRequireUsableExactNames(string key) => AssertInvalidNames(key, -1);

        [Theory]
        [InlineData("state", 0)]
        [InlineData("actionType", 0)]
        [InlineData("sourceVariable", 4)]
        public void ProducerSignaturesRequireUsableExactNames(string key, int index) => AssertInvalidNames(key, index);

        private static void AssertInvalidNames(string key, int index)
        {
            var json = Catalog(); (index < 0 ? Source(json) : Producer(json, index)).Remove(key); AssertIsolatedFailure(json);
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(3), JsonValue.Create(""),
                JsonValue.Create(" "), JsonValue.Create("Part/Data"), JsonValue.Create("Part\\Data"),
                JsonValue.Create("Part:Data"), JsonValue.Create("Part\tData"), JsonValue.Create(new string('x', 129)) })
            {
                json = Catalog(); (index < 0 ? Source(json) : Producer(json, index))[key] = invalid?.DeepClone();
                AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        public void ProducerIndicesAreBounded(int value)
        {
            var json = Catalog(); Producer(json)["index"] = value; AssertIsolatedFailure(json);
        }

        [Fact]
        public void ProducerIndexAndTimingCannotBeInferredFromMissingOrWrongTypes()
        {
            foreach (string key in new[] { "index", "everyFrame" })
            {
                var json = Catalog(); Producer(json).Remove(key); AssertIsolatedFailure(json);
                foreach (string value in new[] { "null", "\"0\"", "0.5", "{}" })
                {
                    json = Catalog(); Producer(json)[key] = JsonNode.Parse(value); AssertIsolatedFailure(json);
                }
            }
            var timing = Catalog(); Producer(timing)["everyFrame"] = 1; AssertIsolatedFailure(timing);
            var indexType = Catalog(); Producer(indexType)["index"] = true; AssertIsolatedFailure(indexType);
        }

        [Fact]
        public void SourceDiscoveryIsBoundedAndCannotBindAnotherVehicleOrSharedGlobal()
        {
            var json = Catalog(); Source(json)["producerPath"] = "CORRIS2/Starter"; AssertIsolatedFailure(json);
            json = Catalog(); Source(json)["producerPath"] = "CORRIS"; AssertIsolatedFailure(json);
            foreach (string duplicate in new[] { "root", "producer", "global" })
            {
                json = Catalog(); var other = Source(json).DeepClone();
                if (duplicate != "root") other["rootPath"] = "OTHER";
                if (duplicate != "producer") other["producerPath"] = other["rootPath"]!.GetValue<string>() + "/Starter";
                if (duplicate != "global") other["globalVariable"] = "OtherRPM";
                Profile(json)["sources"]!.AsArray().Add(other); AssertIsolatedFailure(json);
            }
            json = Catalog(); var list = Profile(json)["sources"]!.AsArray(); var example = list[0]!.DeepClone(); list.Clear();
            for (int i = 0; i < 17; i++)
            {
                var entry = example.DeepClone(); entry["rootPath"] = "CAR" + i;
                entry["producerPath"] = "CAR" + i + "/Starter"; entry["globalVariable"] = "RPM" + i; list.Add(entry);
            }
            AssertIsolatedFailure(json);
        }

        [Fact]
        public void ProducerListIsBoundedAndActionsCannotOverlap()
        {
            var json = Catalog(); var list = Source(json)["producers"]!.AsArray(); list.Add(list[0]!.DeepClone()); AssertIsolatedFailure(json);
            json = Catalog(); list = Source(json)["producers"]!.AsArray(); var example = list[0]!.DeepClone(); list.Clear();
            for (int i = 0; i < 65; i++) { var entry = example.DeepClone(); entry["state"] = "State " + i; list.Add(entry); }
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("SetProperty")]
        [InlineData("GetFsmFloat")]
        [InlineData("HutongGames.PlayMaker.Actions.GetProperty")]
        public void OnlyAuditedProducerActionKindsAreSupported(string actionType)
        {
            var json = Catalog(); Producer(json)["actionType"] = actionType; AssertIsolatedFailure(json);
        }

        [Fact]
        public void FloatSourcesRequireExactlyOneVariableOrFiniteNumericConstant()
        {
            var json = Catalog(); Producer(json, 4)["sourceConstant"] = 0; AssertIsolatedFailure(json);
            json = Catalog(); Producer(json, 4).Remove("sourceVariable"); AssertIsolatedFailure(json);
            json = Catalog(); Producer(json, 7)["sourceVariable"] = "StarterSpeed"; AssertIsolatedFailure(json);
            json = Catalog(); Producer(json, 7).Remove("sourceConstant"); AssertIsolatedFailure(json);
            foreach (string invalid in new[] { "null", "true", "\"0\"", "{}", "1" + new string('0', 100) + ".0", "-1" + new string('0', 100) + ".0" })
            {
                json = Catalog(); Producer(json, 7)["sourceConstant"] = JsonNode.Parse(invalid); AssertIsolatedFailure(json);
            }
            json = Catalog(); Producer(json, 7)["sourceConstant"] = 0.25;
            Assert.Equal(0.25f, SyncCatalogJson.Parse(json.ToJsonString()).VehicleEngineRpm!.Sources[0].Producers[7].SourceConstant);
        }

        [Theory]
        [InlineData("sourceVariable")]
        [InlineData("sourceConstant")]
        public void PropertyProducerCannotCarryEvenANullFloatValueBinding(string key)
        {
            var json = Catalog(); Producer(json)[key] = null; AssertIsolatedFailure(json);
        }

        [Fact]
        public void SourceRequiresAPropertyProducerToEstablishDrivetrainIdentity()
        {
            var json = Catalog(); var list = Source(json)["producers"]!.AsArray();
            for (int i = 0; i < 4; i++) list.RemoveAt(0);
            AssertIsolatedFailure(json);
        }
    }
}
