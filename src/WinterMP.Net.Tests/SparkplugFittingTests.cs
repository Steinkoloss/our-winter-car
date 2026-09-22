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
    public class SparkplugFittingTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Plug(JsonNode json) => json["replacementParts"]!["factories"]!.AsArray()
            .Single(f => f!["prefix"]!.GetValue<string>() == "SPRKPLUG0")!;

        [Fact]
        public void SparkplugsUseTheToolLayerAndOtherFamiliesKeepTheirNativePartLayer()
        {
            var rules = SyncCatalogJson.Parse(Catalog().ToJsonString()).ReplacementParts!.Factories;
            Assert.Equal(12, Assert.Single(rules, f => f.Prefix == "SPRKPLUG0").RemovalLayer);
            Assert.All(rules.Where(f => f.Prefix != "SPRKPLUG0"), f => Assert.Equal(19, f.RemovalLayer));
        }

        [Theory]
        [InlineData("-1")] [InlineData("32")] [InlineData("12.5")] [InlineData("null")]
        [InlineData("true")] [InlineData("\"12\"")]
        public void InvalidLayerCannotSilentlyEnableAnotherPickSurface(string value)
        {
            var json = Catalog(); Plug(json)["removalLayer"] = JsonNode.Parse(value);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void LoosePlugRemovalPreservesTheSocketAndConditionInTheHostReceipt(int slot)
        {
            var rule = SyncCatalogJson.Parse(Catalog().ToJsonString()).ReplacementParts!.Factories.Single(f => f.Prefix == "SPRKPLUG0");
            var state = new ReplacementPartState { FactoryId = rule.Identity.FactoryId, NativeId = "SPRKPLUG0" + slot,
                Revision = 7, Scalars = new[] { 71f, 0f, .75f }, Installed = true, AssemblyId = slot,
                ParentKind = PartParentKind.NativePart, ParentId = 22, ParentPath = "VINP_Sparkplug" + (5 - slot),
                Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity,
                LocalScale = new NetVector3(1, 1, 1), RemovalAllowed = true };
            PartIdentity.TryItemId(state.NativeId, out uint id);
            var request = new PartFitRequest { PlayerId = 1, Token = 8, Sequence = 1, ItemId = id,
                ExpectedRevision = state.Revision, Operation = PartFitOperation.Remove };
            Assert.Equal(PartFitStatus.Pending, PartRemovalPolicy.Check(request, state, rule.Identity.TightnessIndex, true, true, true));
            var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(PacketCodec.Encode(state)));
            Assert.Equal(slot, decoded.AssemblyId); Assert.Equal(state.ParentPath, decoded.ParentPath);
            Assert.Equal(state.Scalars, decoded.Scalars); Assert.True(decoded.RemovalAllowed);
            state.Scalars[1] = 1;
            Assert.Equal(PartFitStatus.Bolted, PartRemovalPolicy.Check(request, state, rule.Identity.TightnessIndex, true, true, true));
            state.Scalars[1] = 0; state.Revision++;
            Assert.Equal(PartFitStatus.Stale, PartRemovalPolicy.Check(request, state, rule.Identity.TightnessIndex, true, true, true));
        }
    }
}
