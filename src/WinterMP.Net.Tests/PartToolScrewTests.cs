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
    public class PartToolScrewTests
    {
        [Theory]
        [InlineData(PartFitOperation.ToolTighten, 6, 1)] [InlineData(PartFitOperation.ToolLoosen, 7, -1)]
        public void ToolTurnsHaveDistinctWireOperationsAndExactBounds(PartFitOperation operation, byte wireValue, int direction)
        {
            var request = new PartFitRequest { PlayerId = 1, Token = 9, Sequence = 1, ItemId = 3, Operation = operation };
            foreach (var message in new IMessage[] { request, PartFitLedger.Receipt(request, PartFitStatus.Accepted) })
            {
                var wire = PacketCodec.Encode(message); Assert.Equal(wireValue, wire[wire.Length - 2]);
                Assert.Equal(wire, PacketCodec.Encode(PacketCodec.Decode(wire)));
                wire[wire.Length - 1] = 11; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
                wire[wire.Length - 1] = 0; wire[wire.Length - 2] = 8; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
            }
            for (int i = 0; i <= 8; i++)
            {
                bool valid = i + direction >= 0 && i + direction <= 8;
                Assert.Equal(valid, PartToolScrewPolicy.TryTurn(i, operation, out float value)); Assert.Equal(valid ? i + direction : i, value);
            }
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)] [InlineData(1.5f)] [InlineData(9)]
        public void InvalidHostTightnessCannotEnterNativeTurns(float value) =>
            Assert.False(PartToolScrewPolicy.TryTurn(value, PartFitOperation.ToolTighten, out _));

        [Theory]
        [InlineData(0, false)] [InlineData(.5f, false)] [InlineData(.55f, true)] [InlineData(.6f, false)] [InlineData(float.NaN, false)]
        public void OnlyTheNativeSparkplugToolSizeIsAccepted(float value, bool expected) => Assert.Equal(expected, PartToolScrewPolicy.MatchesTool(value));

        [Fact]
        public void OnlySparkplugsDeclareTheNativeToolProfile()
        {
            var rules = SyncCatalogJson.Parse(Catalog().ToJsonString()).ReplacementParts!.Factories;
            var plug = Assert.Single(rules, f => f.ToolScrew != null); Assert.Equal("SPRKPLUG0", plug.Prefix);
            Assert.Equal(4, plug.SlotCount); Assert.Equal(12, plug.RemovalLayer); Assert.Null(plug.HandScrew);
            Assert.Equal("Tightness", plug.ToolScrew!.Scalar); Assert.Equal("Screw 2", plug.ToolScrew.TightenState);
            Assert.Equal("Unscrew 2", plug.ToolScrew.LoosenState); Assert.Equal("Check", plug.ToolScrew.ToolFsm);
        }

        [Theory]
        [InlineData("family")] [InlineData("layer")] [InlineData("slots")] [InlineData("scalar")] [InlineData("path")]
        [InlineData("state alias")] [InlineData("fsm path")]
        public void ChangedToolProfileDoesNotFallBackToHandControls(string fault)
        {
            var json = Catalog(); var plug = json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == "SPRKPLUG0")!;
            var tool = plug["toolScrew"]!;
            switch (fault)
            {
                case "family": plug["prefix"] = "UNKNOWN0"; break;
                case "layer": plug["removalLayer"] = 19; break;
                case "slots": plug["slotCount"] = 3; break;
                case "scalar": tool["scalar"] = "Wear"; break;
                case "path": tool["toolPath"] = "PLAYER/../Other"; break;
                case "state alias": tool["loosenState"] = tool["tightenState"]!.GetValue<string>(); break;
                case "fsm path": tool["fsm"] = "Other/Screw"; break;
            }
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void RetriedTurnKeepsItsReceiptAndAnotherOperationCannotReuseIt()
        {
            var ledger = new PartFitLedger(); var request = new PartFitRequest { PlayerId = 1, Token = 9, Sequence = 1, ItemId = 3, Operation = PartFitOperation.ToolTighten };
            Assert.NotNull(ledger.Begin(request, 1, PartFitStatus.Pending)); ledger.Complete(request, true);
            Assert.Equal(PartFitStatus.Accepted, ledger.Inspect(request, 1, out bool begin)!.Status); Assert.False(begin);
            request.Operation = PartFitOperation.HandTighten; Assert.Null(ledger.Inspect(request, 1, out begin)); Assert.False(begin);
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
    }
}
