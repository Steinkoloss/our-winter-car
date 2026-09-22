using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ReplacementFactoryCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Factory(JsonNode json, string prefix) => json["replacementParts"]!["factories"]!.AsArray()
            .Single(f => (string?)f!["prefix"] == prefix)!;
        private static ReplacementPartsData Parse(JsonNode json) => SyncCatalogJson.Parse(json.ToJsonString()).ReplacementParts!;

        [Fact]
        public void OnlyTheAuditedPistonLookupAndStarterReferenceAreDisabled()
        {
            var data = Parse(Catalog());
            var piston = Assert.Single(data.Factories, f => f.DisabledInitActions.Length != 0);
            Assert.Equal("VIN103", piston.Prefix); Assert.Equal(new[] { 0 }, piston.DisabledInitActions);
            Assert.Equal("GetChild", piston.InitActions[0]);
            var starter = Assert.Single(data.Factories, f => f.References.Any(r => !r.Enabled));
            Assert.Equal("VIN130", starter.Prefix);
            var reference = Assert.Single(starter.References, r => !r.Enabled);
            Assert.Equal("PartBlocking", reference.Target); Assert.Equal("PartBlocking", reference.Source);
        }

        [Fact]
        public void OmittedEnableMetadataDefaultsToEnabledWithoutChangingIdentity()
        {
            var json = Catalog(); var before = Parse(json).Factories.Single(f => f.Prefix == "VIN130").Identity.FactoryId;
            Factory(json, "VIN103").AsObject().Remove("disabledInitActions");
            Factory(json, "VIN130")["references"]![1]!.AsObject().Remove("enabled");
            var data = Parse(json);
            Assert.All(data.Factories, f => { Assert.Empty(f.DisabledInitActions); Assert.All(f.References, r => Assert.True(r.Enabled)); });
            Assert.Equal(before, data.Factories.Single(f => f.Prefix == "VIN130").Identity.FactoryId);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("false")]
        [InlineData("{}")]
        [InlineData("[0,0]")]
        [InlineData("[-1]")]
        [InlineData("[100]")]
        [InlineData("[0.5]")]
        [InlineData("[true]")]
        [InlineData("[\"0\"]")]
        [InlineData("[1]")]
        [InlineData("[9]")]
        public void InvalidOrIdentityChangingDisabledInitializationIsRejected(string value)
        {
            var json = Catalog(); Factory(json, "VIN103")["disabledInitActions"] = JsonNode.Parse(value);
            Assert.Throws<FormatException>(() => Parse(json));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("0")]
        [InlineData("\"false\"")]
        [InlineData("[]")]
        public void ReferenceEnableFlagRequiresABoolean(string value)
        {
            var json = Catalog(); Factory(json, "VIN130")["references"]![1]!["enabled"] = JsonNode.Parse(value);
            Assert.Throws<FormatException>(() => Parse(json));
        }

        [Fact]
        public void InstallPointReferenceCannotBeDisabled()
        {
            var json = Catalog(); Factory(json, "VIN130")["references"]![0]!["enabled"] = false;
            Assert.Throws<FormatException>(() => Parse(json));
        }
    }
}
