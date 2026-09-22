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
    public sealed class BulbTests
    {
        private static BulbState Bulb(uint revision = 1, float wear = 97) => new() { ItemId = BulbPolicy.BoxOutput(123), Revision = revision, Wear = wear };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Box(JsonNode root) => root["partsPackages"]!["factories"]!.AsArray().Single(f => f!["fsm"]!.GetValue<string>() == "LightbulbBox")!;
        [Fact]
        public void WireRoundTripsAndRejectsEveryTruncation()
        {
            var bytes = PacketCodec.Encode(Bulb()); Assert.Equal(42, bytes.Length);
            Assert.Equal(bytes, PacketCodec.Encode(PacketCodec.Decode(bytes)));
            for (int n = 0; n < bytes.Length; n++)
            { var cut = new byte[n]; Array.Copy(bytes, cut, n); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(cut)); }
            Array.Clear(bytes, 2, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonFiniteConditionOrPoseRejected(float bad)
        {
            var s = Bulb(); s.Wear = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Bulb(); s.Position.X = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Bulb(); s.Rotation.W = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
        }
        [Theory]
        [InlineData(-1)] [InlineData(101)]
        public void InvalidConditionRejected(float wear) => Assert.False(BulbPolicy.Valid(Bulb(1, wear)));
        [Fact]
        public void OlderAndConflictingConditionCannotUndoHostResult()
        {
            var current = Bulb(9, 80);
            Assert.True(BulbPolicy.Accept(Bulb(10, 70), current));
            Assert.True(BulbPolicy.Accept(Bulb(9, 80), current));
            Assert.False(BulbPolicy.Accept(Bulb(9, 99), current));
            Assert.False(BulbPolicy.Accept(Bulb(8, 99), current));
            Assert.True(BulbPolicy.Accept(Bulb(1), Bulb(uint.MaxValue)));
            Assert.False(BulbPolicy.Accept(Bulb(0x80000009), current));
            var wrong = Bulb(10); wrong.ItemId++; Assert.False(BulbPolicy.Accept(wrong, current));
        }
        [Fact]
        public void OneBulbBoxUsesExistingOpeningReceiptAndBecomesEmpty()
        {
            var c = SyncCatalogJson.Parse(Catalog().ToJsonString()).PartsPackages!;
            var rule = Assert.Single(c.Factories, f => f.BulbContents != null);
            Assert.True(rule.FixedCapacity); Assert.Equal(1, rule.Capacity); Assert.Equal(1, rule.LoadClampIndex);
            Assert.Equal("Data", rule.BulbContents!["fsm"]); Assert.Equal("Wear", rule.BulbContents["wear"]);
            Assert.Null(rule.SupplyContents);
            var state = new PackageState { FactoryId = FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm), NativeId = "lightbulbbox01", Quantity = 1, Revision = 1 };
            uint boxId = FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId), output = BulbPolicy.BoxOutput(boxId);
            Assert.NotEqual(boxId, output); Assert.NotEqual(output, BulbPolicy.BoxOutput(boxId + 1));
            var request = new PackageOpenRequest { ItemId = boxId, PlayerId = 1, Sequence = 1, Token = 71, ExpectedRevision = 1 };
            var ledger = new PackageOpenLedger(); ledger.Begin(request, 1, PackageOpenStatus.Pending); ledger.Complete(request, true, output);
            Assert.Equal(output, ledger.Inspect(request, 1, out bool begin)!.ProducedItemId); Assert.False(begin);
            state.Quantity = 0; state.Revision++;
            Assert.Equal(PackageOpenStatus.Empty, PackageOpenLedger.Check(new() { ItemId = boxId, ExpectedRevision = 2 }, state, true, true, true));
        }
        [Theory]
        [InlineData("capacity")] [InlineData("fixedCapacity")] [InlineData("ready")] [InlineData("duplicate")]
        public void UnsupportedBulbCatalogFailsClosed(string mutation)
        {
            var root = Catalog(); var box = Box(root);
            if (mutation == "capacity") box["capacity"] = 2;
            if (mutation == "fixedCapacity") box["fixedCapacity"] = false;
            if (mutation == "ready") box["bulbContents"]!["ready"] = "State 1";
            if (mutation == "duplicate") { var other = box.DeepClone(); other["fsm"] = "Other"; other["prefix"] = "other0"; other["contentsFsm"] = "OtherBulb"; root["partsPackages"]!["factories"]!.AsArray().Add(other); }
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(root.ToJsonString()));
        }
    }
}
