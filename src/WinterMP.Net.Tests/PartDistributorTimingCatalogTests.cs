using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PartDistributorTimingCatalogTests
    {
        private static JsonNode CatalogJson() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonArray Rules(JsonNode json) => json["replacementParts"]!["factories"]!.AsArray();
        private static JsonNode Distributor(JsonNode json) => Rules(json).Single(f => (string?)f!["prefix"] == "VIN131")!;
        private static ReplacementPartsData Parse(JsonNode json) => SyncCatalogJson.Parse(json.ToJsonString()).ReplacementParts!;

        [Fact]
        public void OnlyDistributorBindsTheAuditedRootControlAndSeparateMeshPose()
        {
            var rules = Parse(CatalogJson());
            var factory = Assert.Single(rules.Factories, f => f.DistributorTiming != null);
            Assert.Equal("VIN131", factory.Prefix); Assert.Equal(0, factory.SlotCount);
            Assert.Null(factory.HandRotation); Assert.Null(factory.HandScrew); Assert.Null(factory.BeltVisual);
            var timing = factory.DistributorTiming!;
            Assert.Equal("HandRotate", timing.Fsm); Assert.Equal("SparkAngle", timing.Scalar);
            Assert.Equal(2, Array.IndexOf(factory.Scalars, timing.Scalar));
            Assert.Equal("Pivot/mesh", timing.MeshPath); Assert.Equal("MeshRotate", timing.MeshVariable);
            Assert.Equal("VINP", timing.MountVariable); Assert.Equal("Rotation", timing.RotationVariable);
            Assert.Equal("Scroll", timing.ScrollVariable); Assert.Equal("Tightness", timing.TightnessVariable);
            Assert.Equal("Clockwise", timing.ClockwiseState); Assert.Equal("Counterwise", timing.CounterwiseState);
            Assert.Equal("Wait", timing.WaitState); Assert.Equal("Input", timing.InputState);
            Assert.Equal("Wait Player", timing.PickState); Assert.Equal("Wait Player 2", timing.TightnessState);
            Assert.Equal("Collider", timing.BindState); Assert.Equal("Bolt loose?", timing.ToolState);
            Assert.Equal("Delay", timing.DelayState); Assert.Equal("Install 2", timing.InstalledPoseState);
            Assert.Equal(new[] { "Wait Player", "Bolt loose?", "Input" }, timing.ReadyStates);
            Assert.Equal(.01f, timing.Cooldown);
            Assert.Equal(new[] { "VIN133", "ALTERNATOR0" }, rules.Factories.Where(f => f.HandRotation != null).Select(f => f.Prefix));
        }

        [Fact]
        public void OptionalDistributorTimingPreservesEveryFactoryAndNativeItemIdentity()
        {
            var json = CatalogJson(); var before = Parse(json);
            Distributor(json).AsObject().Remove("distributorTiming");
            var after = Parse(json);
            Assert.Equal(39, before.Factories.Count); Assert.Equal(39, after.Factories.Count);
            for (int i = 0; i < before.Factories.Count; i++)
            {
                var oldRule = before.Factories[i]; var newRule = after.Factories[i];
                Assert.Equal(oldRule.Identity.FactoryId, newRule.Identity.FactoryId);
                Assert.True(oldRule.Identity.TryId(oldRule.Prefix + "1", out uint oldId));
                Assert.True(newRule.Identity.TryId(newRule.Prefix + "1", out uint newId));
                Assert.Equal(oldId, newId);
            }
            Assert.All(after.Factories, f => Assert.Null(f.DistributorTiming));
        }

        [Theory]
        [InlineData("fsm")]
        [InlineData("scalar")]
        [InlineData("meshPath")]
        [InlineData("meshVariable")]
        [InlineData("mountVariable")]
        [InlineData("rotationVariable")]
        [InlineData("scrollVariable")]
        [InlineData("tightnessVariable")]
        [InlineData("clockwiseState")]
        [InlineData("counterwiseState")]
        [InlineData("waitState")]
        [InlineData("inputState")]
        [InlineData("pickState")]
        [InlineData("tightnessState")]
        [InlineData("bindState")]
        [InlineData("toolState")]
        [InlineData("delayState")]
        [InlineData("installedPoseState")]
        public void NativeBindingsRejectMissingOrInvalidNames(string key)
        {
            var json = CatalogJson(); var timing = Distributor(json)["distributorTiming"]!.AsObject();
            timing.Remove(key); Assert.Throws<FormatException>(() => Parse(json));
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(1), JsonValue.Create(""),
                JsonValue.Create("   "), JsonValue.Create("native\nstate"), JsonValue.Create(new string('x', 129)) })
            {
                timing[key] = invalid;
                Assert.Throws<FormatException>(() => Parse(json));
            }
        }

        [Theory]
        [InlineData("/Pivot/mesh")]
        [InlineData("../mesh")]
        [InlineData("Pivot/../mesh")]
        [InlineData("Pivot/./mesh")]
        [InlineData("Pivot//mesh")]
        [InlineData("Pivot/mesh/")]
        [InlineData("Pivot\\mesh")]
        [InlineData("Scene:mesh")]
        public void PoseTargetCannotEscapeTheOwnedPart(string path)
        {
            var json = CatalogJson(); Distributor(json)["distributorTiming"]!["meshPath"] = path;
            Assert.Throws<FormatException>(() => Parse(json));
        }

        [Theory]
        [InlineData("counterwiseState")]
        [InlineData("waitState")]
        [InlineData("inputState")]
        [InlineData("pickState")]
        [InlineData("tightnessState")]
        [InlineData("bindState")]
        [InlineData("toolState")]
        [InlineData("delayState")]
        public void MutationStateCannotAliasAnotherControlState(string key)
        {
            var json = CatalogJson(); Distributor(json)["distributorTiming"]![key] = "Clockwise";
            Assert.Throws<FormatException>(() => Parse(json));
        }

        [Theory]
        [InlineData("null-profile")]
        [InlineData("boolean-profile")]
        [InlineData("array-profile")]
        [InlineData("other-family")]
        [InlineData("slots")]
        [InlineData("hand-rotation")]
        [InlineData("hand-screw")]
        [InlineData("belt-visual")]
        [InlineData("wear-scalar")]
        [InlineData("tightness-scalar")]
        [InlineData("missing-scalar")]
        [InlineData("missing-scalar-reference")]
        [InlineData("mesh-mount-alias")]
        [InlineData("rotation-scroll-alias")]
        [InlineData("rotation-tightness-alias")]
        [InlineData("scroll-tightness-alias")]
        [InlineData("missing-cooldown")]
        [InlineData("text-cooldown")]
        [InlineData("zero-cooldown")]
        [InlineData("different-cooldown")]
        [InlineData("infinite-cooldown")]
        [InlineData("missing-ready")]
        [InlineData("duplicate-ready")]
        [InlineData("cooldown-ready")]
        [InlineData("mutation-ready")]
        [InlineData("init-ready")]
        [InlineData("extra-ready")]
        public void IncompatibleTimingProfilesFailBeforeNativeBinding(string fault)
        {
            var json = CatalogJson(); var rule = Distributor(json); var timing = rule["distributorTiming"]!.AsObject();
            switch (fault)
            {
                case "null-profile": rule["distributorTiming"] = null; break;
                case "boolean-profile": rule["distributorTiming"] = true; break;
                case "array-profile": rule["distributorTiming"] = new JsonArray(); break;
                case "other-family": Rules(json)[0]!["distributorTiming"] = timing.DeepClone(); break;
                case "slots": rule["slotReference"] = "VIN"; rule["slotCount"] = 1; break;
                case "hand-rotation": rule["handRotation"] = Rules(json).Single(f => (string?)f!["prefix"] == "VIN133")!["handRotation"]!.DeepClone(); break;
                case "hand-screw": rule["handScrew"] = Rules(json).Single(f => (string?)f!["prefix"] == "OILFILTR0")!["handScrew"]!.DeepClone(); break;
                case "belt-visual": rule["beltVisual"] = Rules(json).Single(f => (string?)f!["prefix"] == "FANBELT0")!["beltVisual"]!.DeepClone(); break;
                case "wear-scalar": timing["scalar"] = "Wear"; break;
                case "tightness-scalar": timing["scalar"] = "Tightness"; break;
                case "missing-scalar": rule["scalars"]!.AsArray().RemoveAt(2); break;
                case "missing-scalar-reference": timing["scalar"] = "Unknown"; break;
                case "mesh-mount-alias": timing["mountVariable"] = "MeshRotate"; break;
                case "rotation-scroll-alias": timing["scrollVariable"] = "Rotation"; break;
                case "rotation-tightness-alias": timing["tightnessVariable"] = "Rotation"; break;
                case "scroll-tightness-alias": timing["tightnessVariable"] = "Scroll"; break;
                case "missing-cooldown": timing.Remove("cooldown"); break;
                case "text-cooldown": timing["cooldown"] = "0.01"; break;
                case "zero-cooldown": timing["cooldown"] = 0; break;
                case "different-cooldown": timing["cooldown"] = .1; break;
                case "infinite-cooldown": timing["cooldown"] = 1e100; break;
                case "missing-ready": timing.Remove("readyStates"); break;
                case "duplicate-ready": timing["readyStates"]![0] = "Input"; break;
                case "cooldown-ready": timing["readyStates"]![0] = "Wait"; break;
                case "mutation-ready": timing["readyStates"]![0] = "Clockwise"; break;
                case "init-ready": timing["readyStates"]![0] = "Wait Player 2"; break;
                case "extra-ready": timing["readyStates"]!.AsArray().Add("Delay"); break;
            }
            Assert.Throws<FormatException>(() => Parse(json));
        }
    }
}
