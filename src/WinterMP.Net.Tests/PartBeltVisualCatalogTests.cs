using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PartBeltVisualCatalogTests
    {
        private static JsonNode CatalogJson() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonArray Rules(JsonNode json) => json["replacementParts"]!["factories"]!.AsArray();
        private static JsonNode Belt(JsonNode json) => Rules(json).Single(f => (string?)f!["prefix"] == "FANBELT0")!;
        private static ReplacementPartsData Parse(JsonNode json) => SyncCatalogJson.Parse(json.ToJsonString()).ReplacementParts!;

        [Fact]
        public void OnlyFanbeltSupportsTheAuditedSeparateFittedVisual()
        {
            var rules = Parse(CatalogJson());
            var factory = Assert.Single(rules.Factories, f => f.BeltVisual != null);
            Assert.Equal("FANBELT0", factory.Prefix); Assert.Equal(0, factory.SlotCount);
            Assert.Null(factory.HandScrew); Assert.Null(factory.HandRotation);
            var belt = factory.BeltVisual!;
            Assert.Equal("Mesh", belt.LooseMeshVariable); Assert.Equal("FanBeltMesh", belt.MountVisualVariable);
            Assert.Equal("FanBelt", belt.VisualPath); Assert.Equal("Mesh", belt.MeshPath);
            Assert.Equal("Mesh/FanbeltMesh", belt.RendererPath); Assert.Equal("Mesh/ScaleBone", belt.ScaleBonePath);
            Assert.Equal("Animations", belt.AnimationPath); Assert.Equal("Jumping", belt.Fsm); Assert.Equal("Scale", belt.ScaleVariable);
            Assert.Equal("CORRIS/Simulation/Engine/SymptomsEngine/Animations", belt.ScrollPath);
            Assert.Equal("BeltAnimation", belt.ScrollFsm);
            Assert.Same(factory, Assert.Single(rules.Factories, f => f.Identity.SupportsBeltVisual));
        }

        [Fact]
        public void OptionalBeltVisualCapabilityPreservesEveryFactoryIdentity()
        {
            var json = CatalogJson(); var before = Parse(json);
            Belt(json).AsObject().Remove("beltVisual");
            var after = Parse(json);
            Assert.Equal(before.Factories.Select(f => f.Identity.FactoryId), after.Factories.Select(f => f.Identity.FactoryId));
            Assert.All(after.Factories, f => Assert.False(f.Identity.SupportsBeltVisual));
        }

        [Theory]
        [InlineData("looseMeshVariable")]
        [InlineData("mountVisualVariable")]
        [InlineData("visualPath")]
        [InlineData("meshPath")]
        [InlineData("rendererPath")]
        [InlineData("scaleBonePath")]
        [InlineData("animationPath")]
        [InlineData("fsm")]
        [InlineData("scaleVariable")]
        [InlineData("scrollPath")]
        [InlineData("scrollFsm")]
        public void BeltVisualBindingsRejectMissingOrInvalidNames(string key)
        {
            var json = CatalogJson(); var profile = Belt(json)["beltVisual"]!.AsObject();
            profile.Remove(key); Assert.Throws<FormatException>(() => Parse(json));
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(1), JsonValue.Create(""),
                JsonValue.Create("   "), JsonValue.Create("native\nname"), JsonValue.Create(new string('x', 129)) })
            {
                profile[key] = invalid;
                Assert.Throws<FormatException>(() => Parse(json));
            }
        }

        [Theory]
        [InlineData("visualPath")]
        [InlineData("meshPath")]
        [InlineData("rendererPath")]
        [InlineData("scaleBonePath")]
        [InlineData("animationPath")]
        [InlineData("scrollPath")]
        public void BeltVisualPathsCannotEscapeTheirOwnedHierarchy(string key)
        {
            var json = CatalogJson(); var profile = Belt(json)["beltVisual"]!;
            foreach (string invalid in new[] { "/Mesh", "../Mesh", "Mesh/../Other", "Mesh/./Bone", "Mesh//Bone", "Mesh/", "Mesh\\Bone", "Scene:Mesh" })
            {
                profile[key] = invalid;
                Assert.Throws<FormatException>(() => Parse(json));
            }
        }

        [Theory]
        [InlineData("null-profile")]
        [InlineData("boolean-profile")]
        [InlineData("array-profile")]
        [InlineData("other-family")]
        [InlineData("slots")]
        [InlineData("hand-screw")]
        [InlineData("hand-rotation")]
        [InlineData("external-renderer")]
        [InlineData("external-bone")]
        [InlineData("renderer-is-bone")]
        [InlineData("animation-is-mesh")]
        [InlineData("animation-inside-mesh")]
        [InlineData("mesh-inside-animation")]
        public void IncompatibleBeltVisualProfilesFailBeforeNativeCloning(string fault)
        {
            var json = CatalogJson(); var rule = Belt(json); var profile = rule["beltVisual"]!;
            switch (fault)
            {
                case "null-profile": rule["beltVisual"] = null; break;
                case "boolean-profile": rule["beltVisual"] = false; break;
                case "array-profile": rule["beltVisual"] = new JsonArray(); break;
                case "other-family": Rules(json)[0]!["beltVisual"] = profile.DeepClone(); break;
                case "slots": rule["slotReference"] = "VIN"; rule["slotCount"] = 1; break;
                case "hand-screw": rule["handScrew"] = Rules(json).Single(f => (string?)f!["prefix"] == "OILFILTR0")!["handScrew"]!.DeepClone(); break;
                case "hand-rotation":
                    rule["handRotation"] = Rules(json).Single(f => (string?)f!["prefix"] == "VIN133")!["handRotation"]!.DeepClone();
                    rule["handRotation"]!["scalar"] = "Tightness"; break;
                case "external-renderer": profile["rendererPath"] = "Other/FanbeltMesh"; break;
                case "external-bone": profile["scaleBonePath"] = "Other/ScaleBone"; break;
                case "renderer-is-bone": profile["scaleBonePath"] = "Mesh/FanbeltMesh"; break;
                case "animation-is-mesh": profile["animationPath"] = "Mesh"; break;
                case "animation-inside-mesh": profile["animationPath"] = "Mesh/Animations"; break;
                case "mesh-inside-animation": profile["animationPath"] = "Mesh"; profile["meshPath"] = "Mesh/Child";
                    profile["rendererPath"] = "Mesh/Child/FanbeltMesh"; profile["scaleBonePath"] = "Mesh/Child/ScaleBone"; break;
            }
            Assert.Throws<FormatException>(() => Parse(json));
        }

    }
}
