using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PartHandScrewCatalogTests
    {
        private static JsonNode CatalogJson() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonArray Rules(JsonNode json) => json["replacementParts"]!["factories"]!.AsArray();
        private static JsonNode Oilfilter(JsonNode json) => Rules(json).Single(f => (string?)f!["prefix"] == "OILFILTR0")!;
        private static ReplacementPartsData Parse(JsonNode json) => SyncCatalogJson.Parse(json.ToJsonString()).ReplacementParts!;

        [Fact]
        public void OnlyOilfilterUsesTheAuditedNativeHandScrewControls()
        {
            var factory = Assert.Single(Parse(CatalogJson()).Factories, f => f.HandScrew != null);
            Assert.Equal("OILFILTR0", factory.Prefix); Assert.Equal(0, factory.SlotCount); Assert.Null(factory.HandRotation);
            var screw = factory.HandScrew!;
            Assert.Equal("Screw", screw.Fsm); Assert.Equal("Tightness", screw.Scalar);
            Assert.Equal(factory.Identity.TightnessIndex, Array.IndexOf(factory.Scalars, screw.Scalar));
            Assert.Equal("Tightness", screw.ScratchVariable); Assert.Equal("Rot", screw.RotationVariable);
            Assert.Equal("Screw 2", screw.TightenState); Assert.Equal("Unscrew 2", screw.LoosenState);
            Assert.Equal("Set", screw.PoseState); Assert.Equal("Get scroll", screw.InputState);
            Assert.Equal("Wait1 2", screw.WaitState); Assert.Equal("Check tool", screw.ToolState);
            Assert.Equal("Setup 2", screw.InitState); Assert.Equal("Mouse off 2", screw.PickState);
            Assert.Equal(new[] { "Mouse off 2", "Check tool", "Get scroll" }, screw.ReadyStates);
            Assert.Equal(.2f, screw.Cooldown);
        }

        [Fact]
        public void OptionalHandScrewMetadataDoesNotChangeFactoryIdentity()
        {
            var json = CatalogJson(); var before = Parse(json).Factories.Single(f => f.Prefix == "OILFILTR0");
            Oilfilter(json).AsObject().Remove("handScrew");
            var after = Parse(json).Factories.Single(f => f.Prefix == "OILFILTR0");
            Assert.Null(after.HandScrew); Assert.Equal(before.Identity.FactoryId, after.Identity.FactoryId);
        }

        [Theory]
        [InlineData("fsm")]
        [InlineData("scalar")]
        [InlineData("scratchVariable")]
        [InlineData("rotationVariable")]
        [InlineData("tightenState")]
        [InlineData("loosenState")]
        [InlineData("poseState")]
        [InlineData("inputState")]
        [InlineData("waitState")]
        [InlineData("toolState")]
        [InlineData("initState")]
        [InlineData("pickState")]
        public void RequiredNativeBindingsRejectMissingOrInvalidNames(string key)
        {
            var json = CatalogJson(); var screw = Oilfilter(json)["handScrew"]!.AsObject();
            screw.Remove(key); Assert.Throws<FormatException>(() => Parse(json));
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(1), JsonValue.Create(""),
                JsonValue.Create("   "), JsonValue.Create("native\nstate"), JsonValue.Create(new string('x', 129)) })
            {
                screw[key] = invalid;
                Assert.Throws<FormatException>(() => Parse(json));
            }
        }

        [Theory]
        [InlineData("loosenState")]
        [InlineData("poseState")]
        [InlineData("inputState")]
        [InlineData("waitState")]
        [InlineData("toolState")]
        [InlineData("initState")]
        [InlineData("pickState")]
        public void NativeMutationStateCannotAliasAnotherHandScrewState(string key)
        {
            var json = CatalogJson(); var screw = Oilfilter(json)["handScrew"]!;
            screw[key] = "Screw 2";
            Assert.Throws<FormatException>(() => Parse(json));
        }

        [Theory]
        [InlineData("null-profile")]
        [InlineData("boolean-profile")]
        [InlineData("array-profile")]
        [InlineData("wrong-scalar")]
        [InlineData("missing-scalar-reference")]
        [InlineData("aliased-scratch")]
        [InlineData("missing-cooldown")]
        [InlineData("text-cooldown")]
        [InlineData("zero-cooldown")]
        [InlineData("different-cooldown")]
        [InlineData("infinite-cooldown")]
        [InlineData("missing-ready")]
        [InlineData("duplicate-ready")]
        [InlineData("cooldown-ready")]
        [InlineData("mutation-ready")]
        [InlineData("extra-ready")]
        [InlineData("slotted-profile")]
        [InlineData("rotating-profile")]
        public void IncompatibleHandScrewMetadataFailsBeforeNativeHooks(string fault)
        {
            var json = CatalogJson(); var rule = Oilfilter(json); var screw = rule["handScrew"]!.AsObject();
            switch (fault)
            {
                case "null-profile": rule["handScrew"] = null; break;
                case "boolean-profile": rule["handScrew"] = true; break;
                case "array-profile": rule["handScrew"] = new JsonArray(); break;
                case "wrong-scalar": screw["scalar"] = "Dirt"; break;
                case "missing-scalar-reference": screw["scalar"] = "Unknown"; break;
                case "aliased-scratch": screw["rotationVariable"] = "Tightness"; break;
                case "missing-cooldown": screw.Remove("cooldown"); break;
                case "text-cooldown": screw["cooldown"] = "0.2"; break;
                case "zero-cooldown": screw["cooldown"] = 0; break;
                case "different-cooldown": screw["cooldown"] = .1; break;
                case "infinite-cooldown": screw["cooldown"] = 1e100; break;
                case "missing-ready": screw.Remove("readyStates"); break;
                case "duplicate-ready": screw["readyStates"]![0] = "Check tool"; break;
                case "cooldown-ready": screw["readyStates"]![0] = "Wait1 2"; break;
                case "mutation-ready": screw["readyStates"]![0] = "Screw 2"; break;
                case "extra-ready": screw["readyStates"]!.AsArray().Add("Wait1 2"); break;
                case "slotted-profile": Rules(json).Single(f => (string?)f!["prefix"] == "VIN103")!["handScrew"] = screw.DeepClone(); break;
                case "rotating-profile": Rules(json).Single(f => (string?)f!["prefix"] == "VIN133")!["handScrew"] = screw.DeepClone(); break;
            }
            Assert.Throws<FormatException>(() => Parse(json));
        }
    }
}
