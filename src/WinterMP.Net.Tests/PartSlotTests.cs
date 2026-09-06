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
    public class PartSlotTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(32)]
        public void SlotIsAppendedToRequestsAndReceipts(byte slot)
        {
            var request = new PartFitRequest { PlayerId = 1, Token = 2, Sequence = 3, ItemId = 4, ExpectedRevision = 5, SlotIndex = slot };
            var fixedWire = PacketCodec.Encode(PartFitLedger.Copy(request)); fixedWire[24] = 0;
            var wire = PacketCodec.Encode(request);
            Assert.Equal(25, wire.Length); Assert.Equal(slot, wire[24]);
            Assert.Equal(fixedWire.Take(24), wire.Take(24));
            Assert.Equal(slot, Assert.IsType<PartFitRequest>(PacketCodec.Decode(wire)).SlotIndex);
            var receipt = PartFitLedger.Receipt(request, PartFitStatus.Accepted);
            wire = PacketCodec.Encode(receipt);
            Assert.Equal(22, wire.Length); Assert.Equal(slot, wire[21]);
            Assert.Equal(slot, Assert.IsType<PartFitReceipt>(PacketCodec.Decode(wire)).SlotIndex);
            foreach (var message in new IMessage[] { request, receipt })
            {
                wire = PacketCodec.Encode(message);
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(wire.Length - 1).ToArray()));
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[] { 0 }).ToArray()));
                wire[wire.Length - 1] = 33;
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
                wire[wire.Length - 1] = slot; wire[wire.Length - 2] = (byte)PartFitOperation.Remove;
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
            }
        }

        [Theory]
        [InlineData(PartFitOperation.Install, 33)]
        [InlineData(PartFitOperation.Remove, 1)]
        [InlineData(PartFitOperation.Remove, 32)]
        [InlineData((PartFitOperation)4, 0)]
        public void InvalidSlotsCannotEnterTheLedgerOrClient(PartFitOperation operation, byte slot)
        {
            var request = new PartFitRequest { PlayerId = 1, Token = 2, Operation = operation, SlotIndex = slot };
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(PartFitLedger.Receipt(request, PartFitStatus.Failed)));
            Assert.Null(new PartFitLedger().Begin(request, 1, PartFitStatus.Pending));
            Assert.False(new PartFitClient(2).TryBegin(1, 1, 1, operation, slot));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetriedSlotCannotChangeOrRepeatAfterSettlement(bool accepted)
        {
            var client = new PartFitClient(123); var ledger = new PartFitLedger();
            Assert.True(client.TryBegin(1, 2, 3, PartFitOperation.Install, 4));
            var original = client.Poll(0)!;
            ledger.Begin(original, 1, PartFitStatus.Pending);
            var changed = PartFitLedger.Copy(original); changed.SlotIndex = 3;
            Assert.Null(ledger.Inspect(changed, 1, out bool begin)); Assert.False(begin);
            Assert.Null(ledger.Complete(changed, true));
            Assert.Equal((byte)4, client.Poll(.5f)!.SlotIndex);
            var outcome = ledger.Complete(original, accepted)!;
            Assert.False(client.Receive(PartFitLedger.Receipt(changed, outcome.Status)));
            Assert.True(client.Receive(outcome));
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(outcome.Status, ledger.Inspect(original, 1, out begin)!.Status);
                Assert.False(begin);
            }
            Assert.Null(ledger.Inspect(changed, 1, out begin)); Assert.False(begin);
        }

        [Fact]
        public void ASecondSlotCannotReplaceAPendingClick()
        {
            var ledger = new PartFitLedger();
            var request = new PartFitRequest { PlayerId = 1, Token = 2, Sequence = 1, SlotIndex = 4 };
            ledger.Begin(request, 1, PartFitStatus.Pending);
            var next = PartFitLedger.Copy(request); next.Sequence++; next.SlotIndex = 5;
            var busy = ledger.Begin(next, 1, PartFitStatus.Pending)!;
            Assert.Equal(PartFitStatus.Busy, busy.Status); Assert.Equal((byte)5, busy.SlotIndex);
            Assert.Equal((byte)4, ledger.Complete(request, true)!.SlotIndex);
        }

        [Fact]
        public void NativeNearestSlotUsesLaterTiesAndDoesNotSkipOccupiedLocations()
        {
            var points = new NetVector3?[] { null, new NetVector3(-.05f, 0, 0), new NetVector3(.05f, 0, 0), new NetVector3(.2f, 0, 0) };
            Assert.Equal((byte)2, PartSlotPolicy.Nearest(default, points, .1f));
            // Occupancy belongs to admission after selecting the native nearest
            // point. An occupied slot must never become a farther installation.
            points[1] = new NetVector3(.01f, 0, 0);
            Assert.Equal((byte)1, PartSlotPolicy.Nearest(default, points, .1f));
            var state = new ReplacementPartState { NativeId = "VIN1031", Revision = 1 };
            Assert.True(PartIdentity.TryItemId(state.NativeId, out uint id));
            var request = new PartFitRequest { ItemId = id, ExpectedRevision = 1, SlotIndex = 1 };
            Assert.Equal(PartFitStatus.Blocked, PartFitLedger.Check(request, state, true, true, true, false, true, false, 1));
        }

        [Fact]
        public void MovementToAnotherSlotRejectsTheOldClickWithoutRefittingElsewhere()
        {
            var state = new ReplacementPartState { NativeId = "VIN1031", Revision = 7 };
            Assert.True(PartIdentity.TryItemId(state.NativeId, out uint id));
            var points = new NetVector3?[] { null, new NetVector3(-.05f, 0, 0), new NetVector3(.05f, 0, 0) };
            var request = new PartFitRequest { ItemId = id, ExpectedRevision = 7,
                SlotIndex = PartSlotPolicy.Nearest(new NetVector3(-.04f, 0, 0), points, .1f) };
            byte moved = PartSlotPolicy.Nearest(new NetVector3(.04f, 0, 0), points, .1f);
            Assert.Equal((byte)1, request.SlotIndex); Assert.Equal((byte)2, moved);
            Assert.Equal(PartFitStatus.Stale, PartFitLedger.Check(request, state, true, true, true, true, true, false, moved));
            Assert.Equal(PartFitStatus.Stale, PartFitLedger.Check(request, state, true, true, true, true, true, false, 0));
            request.SlotIndex = 0;
            Assert.Equal(PartFitStatus.Stale, PartFitLedger.Check(request, state, true, true, true, true, true, false, moved));
        }

        [Fact]
        public void NullSlotsRetainTheirNativeIndicesAndToleranceIsStrict()
        {
            var points = new NetVector3?[] { null, null, null, new NetVector3(.1f, 0, 0) };
            Assert.Equal((byte)0, PartSlotPolicy.Nearest(default, points, .1f));
            points[3] = new NetVector3(.099f, 0, 0);
            Assert.Equal((byte)3, PartSlotPolicy.Nearest(default, points, .1f));
            points[3] = null;
            Assert.Equal((byte)0, PartSlotPolicy.Nearest(default, points, .1f));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(-1f)]
        [InlineData(0f)]
        [InlineData(1.01f)]
        public void InvalidNativeToleranceRejectsSelection(float tolerance)
            => Assert.Equal((byte)0, PartSlotPolicy.Nearest(default, new NetVector3?[] { null, default(NetVector3) }, tolerance));

        [Fact]
        public void MalformedOrNonfiniteGeometryCannotSelectASlot()
        {
            var origin = default(NetVector3);
            Assert.Equal((byte)0, PartSlotPolicy.Nearest(origin, null!, .1f));
            Assert.Equal((byte)0, PartSlotPolicy.Nearest(origin, new NetVector3?[34], .1f));
            Assert.Equal((byte)0, PartSlotPolicy.Nearest(origin, new NetVector3?[] { origin, origin }, .1f));
            Assert.Equal((byte)0, PartSlotPolicy.Nearest(origin, new NetVector3?[1], .1f));
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var invalid = new NetVector3(bad, 0, 0);
                Assert.Equal((byte)0, PartSlotPolicy.Nearest(invalid, new NetVector3?[] { null, origin }, .1f));
                Assert.Equal((byte)0, PartSlotPolicy.Nearest(origin, new NetVector3?[] { null, origin, invalid }, .1f));
            }
        }

        [Fact]
        public void SlotCatalogRequiresBindingsAndPairsEachFamilyWithItsNativeArray()
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var data = SyncCatalogJson.Parse(text).ReplacementParts!;
            var slots = data.Factories.Where(f => f.SlotCount != 0).ToDictionary(f => f.Prefix);
            Assert.Equal(3, slots.Count);
            Assert.Equal("Pistons", slots["VIN103"].SlotReference); Assert.Equal((byte)4, slots["VIN103"].SlotCount);
            Assert.Equal("MainBearings", slots["VIN104"].SlotReference); Assert.Equal((byte)5, slots["VIN104"].SlotCount);
            Assert.Equal("Rockers", slots["VIN117"].SlotReference); Assert.Equal((byte)8, slots["VIN117"].SlotCount);
            Assert.Equal(27, data.Factories.Count(f => f.SlotCount == 0 && f.SlotReference == string.Empty));
            foreach (string key in ReplacementPartsData.RequiredBindings.Where(k => k.StartsWith("slot", StringComparison.Ordinal)))
            {
                var broken = JsonNode.Parse(text)!; broken["replacementParts"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
            foreach (string key in new[] { "slotCount", "slotReference" })
            {
                var broken = JsonNode.Parse(text)!;
                var factory = broken["replacementParts"]!["factories"]!.AsArray().First(f => (string?)f!["prefix"] == "VIN103")!;
                factory.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
            foreach (int count in new[] { 0, -1, 33 })
            {
                var broken = JsonNode.Parse(text)!;
                broken["replacementParts"]!["factories"]!.AsArray().First(f => (string?)f!["prefix"] == "VIN103")!["slotCount"] = count;
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
        }
    }
}
