using System;
using System.Collections;
using System.IO;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class BoltStatePolicyTests
    {
        private static BoltState State(int tightness = 4, float total = 17.25f) => BoltStatePolicy.Capture(123, tightness, total)!;

        [Fact]
        public void LiveWireAppendsParentTotalAfterTheLegacyBoltPrefix()
        {
            var state = State(); byte[] bytes = PacketCodec.Encode(state);
            Assert.Equal(14, bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal((ushort)MessageId.BoltState, reader.ReadUInt16()); Assert.Equal(123u, reader.ReadUInt32());
            Assert.Equal(4, reader.ReadUInt16()); Assert.Equal(0, reader.ReadUInt16()); Assert.Equal(17.25f, reader.ReadSingle());
            var decoded = Assert.IsType<BoltState>(PacketCodec.Decode(bytes));
            Assert.True(BoltStatePolicy.Valid(decoded)); Assert.Equal(state.PartTightness, decoded.PartTightness);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        }

        [Fact]
        public void ChunkWirePreservesAllLegacyEntriesBeforeAppendingTheirParentTotals()
        {
            var snapshot = new WorldBoltSnapshot();
            for (int i = 0; i < WorldBoltSnapshot.MaxEntries; i++)
                snapshot.Entries.Add(BoltStatePolicy.SnapshotEntry(BoltStatePolicy.Capture((uint)i, i % 9, i + .125f)!));
            byte[] bytes = PacketCodec.Encode(snapshot); Assert.Equal(4 + 12 * WorldBoltSnapshot.MaxEntries, bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal((ushort)MessageId.WorldBoltSnapshot, reader.ReadUInt16()); Assert.Equal(WorldBoltSnapshot.MaxEntries, reader.ReadUInt16());
            foreach (var entry in snapshot.Entries)
            {
                Assert.Equal(entry.NetId, reader.ReadUInt32()); Assert.Equal(entry.BoltTightness, reader.ReadUInt16());
                Assert.Equal(0, reader.ReadUInt16());
            }
            foreach (var entry in snapshot.Entries) Assert.Equal(entry.PartTightness, reader.ReadSingle());
            var decoded = Assert.IsType<WorldBoltSnapshot>(PacketCodec.Decode(bytes)); Assert.True(BoltStatePolicy.Valid(decoded));
            Assert.Equal(snapshot.Entries.Select(e => e.PartTightness), decoded.Entries.Select(e => e.PartTightness));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
        public void AbsoluteReconciliationRepairsTheNativeSaveArrayAndUsesMillimetrePoseScale(int turns)
        {
            var values = new ArrayList { 8, 8, 8 }; var state = State(turns);
            for (int repeat = 0; repeat < 100; repeat++)
            {
                var wire = Assert.IsType<BoltState>(PacketCodec.Decode(PacketCodec.Encode(state)));
                Assert.True(BoltStatePolicy.ApplyArray(wire, values, 1, -400, out float z));
                Assert.Equal(-turns / 400f, z);
                Assert.Equal(new[] { 8, turns, 8 }, values.Cast<int>());
                Assert.Equal(17.25f, wire.PartTightness);
            }
            Assert.True(BoltStatePolicy.ApplyArray(state, values, 1, 1, out float unscaled));
            Assert.Equal(turns, unscaled); // VIN202 has no FloatDivide/position action.
        }

        [Fact]
        public void FullyLooseBoltAndZeroParentTotalRemainRealSnapshotEntries()
        {
            var snapshot = new WorldBoltSnapshot(); snapshot.Entries.Add(BoltStatePolicy.SnapshotEntry(State(0, 0)));
            var decoded = Assert.IsType<WorldBoltSnapshot>(PacketCodec.Decode(PacketCodec.Encode(snapshot)));
            Assert.Single(decoded.Entries); Assert.True(BoltStatePolicy.Valid(decoded));
            var values = new ArrayList { 8 };
            Assert.True(BoltStatePolicy.ApplyArray(BoltStatePolicy.FromSnapshot(decoded.Entries[0]), values, 0, -400, out float z));
            Assert.Equal(0, values[0]); Assert.Equal(0, z); Assert.Equal(0, decoded.Entries[0].PartTightness);
        }

        [Theory]
        [InlineData(-1, 0)] [InlineData(9, 0)] [InlineData(65535, 0)]
        [InlineData(4, float.NaN)] [InlineData(4, float.PositiveInfinity)] [InlineData(4, float.NegativeInfinity)]
        public void InvalidNativeStateCannotBePublished(int tightness, float total) => Assert.Null(BoltStatePolicy.Capture(1, tightness, total));

        [Fact]
        public void GuestScalarReportsNeverOverwriteTheHostOrBecomeRelativeTurns()
        {
            var host = State(6, 28.5f);
            for (int guest = 0; guest <= 8; guest++)
            {
                var reply = BoltStatePolicy.HostReply(State(guest, -10000), host)!;
                Assert.Equal(6, reply.BoltTightness); Assert.Equal(28.5f, reply.PartTightness);
                reply.PartTightness = 0; Assert.Equal(28.5f, host.PartTightness);
            }
            var bad = State(); bad.ScrewInt = ushort.MaxValue;
            Assert.False(BoltStatePolicy.Valid(bad)); Assert.Null(BoltStatePolicy.HostReply(bad, host));
            bad.ScrewInt = 1; Assert.False(BoltStatePolicy.Valid(bad));
            bad = State(); bad.NetId++; Assert.Null(BoltStatePolicy.HostReply(bad, host));
        }

        [Fact]
        public void ConcurrentGuestPredictionsConvergeToTheCombinedHostResultWithoutDoubleAdding()
        {
            var firstGuest = new ArrayList { 1, 0 }; var secondGuest = new ArrayList { 1, 0 };
            var authoritative = State(2, 2);
            foreach (var peer in new[] { firstGuest, secondGuest })
                for (int delivery = 0; delivery < 20; delivery++)
                {
                    var reply = BoltStatePolicy.HostReply(State(1, 1), authoritative)!;
                    Assert.True(BoltStatePolicy.ApplyArray(reply, peer, 0, -400, out _));
                    Assert.Equal(2, peer[0]); Assert.Equal(2, reply.PartTightness);
                }
        }

        [Theory]
        [InlineData("index-negative")] [InlineData("index-too-large")] [InlineData("wrong-element")]
        [InlineData("readonly")] [InlineData("zero-divisor")] [InlineData("nan-divisor")]
        [InlineData("infinite-divisor")] [InlineData("overflow-position")] [InlineData("bad-total")] [InlineData("bad-tightness")]
        public void InvalidApplyLeavesTheSaveArrayUntouched(string fault)
        {
            IList values = new ArrayList { 3, 5 }; int index = 0; float divisor = -400; var state = State();
            switch (fault)
            {
                case "index-negative": index = -1; break;
                case "index-too-large": index = 2; break;
                case "wrong-element": values[0] = 3f; break;
                case "readonly": values = ArrayList.ReadOnly((ArrayList)values); break;
                case "zero-divisor": divisor = 0; break;
                case "nan-divisor": divisor = float.NaN; break;
                case "infinite-divisor": divisor = float.PositiveInfinity; break;
                case "overflow-position": divisor = float.Epsilon; break;
                case "bad-total": state.PartTightness = float.NaN; break;
                case "bad-tightness": state.BoltTightness = 9; break;
            }
            object[] before = values.Cast<object>().ToArray();
            Assert.False(BoltStatePolicy.ApplyArray(state, values, index, divisor, out _));
            Assert.Equal(before, values.Cast<object>());
        }

        [Fact]
        public void InvalidOrDuplicateSnapshotEntriesRejectTheWholeChunk()
        {
            var snapshot = new WorldBoltSnapshot(); snapshot.Entries.Add(BoltStatePolicy.SnapshotEntry(State()));
            var invalid = State(); invalid.NetId++; invalid.PartTightness = float.NaN;
            snapshot.Entries.Add(BoltStatePolicy.SnapshotEntry(invalid)); Assert.False(BoltStatePolicy.Valid(snapshot));
            snapshot.Entries[1] = snapshot.Entries[0]; Assert.False(BoltStatePolicy.Valid(snapshot));
        }

        [Fact]
        public void ChecksumIncludesParentTotalAndUsesPersistentTightnessInsteadOfLastTurnDirection()
        {
            uint Hash(BoltState state) => BoltStatePolicy.MixChecksum(StableHash.OffsetBasis, state);
            Assert.NotEqual(Hash(State(4, 16)), Hash(State(4, 17)));
            Assert.NotEqual(Hash(State(4, 16)), Hash(State(3, 16)));
            Assert.Equal(Hash(State(4, 16)), Hash(State(4, 16.00001f)));
            Assert.Equal(Hash(State(0, 0)), Hash(State(0, -0f)));
        }

        [Fact]
        public void ADelayedBoltRestoresItsArraySlotWithoutRollingBackANewerSiblingOrPartReport()
        {
            var totals = new PartTightnessReceipts(); var values = new ArrayList { 0, 0 };
            var laterBolt = State(3, 5); var earlierBolt = State(2, 2);
            Assert.True(BoltStatePolicy.ApplyArray(laterBolt, values, 1, -400, out _));
            Assert.Equal(5, totals.Resolve(123, 20, laterBolt.PartTightness));
            Assert.True(BoltStatePolicy.ApplyArray(earlierBolt, values, 0, -400, out _));
            Assert.Equal(5, totals.Resolve(123, 10, earlierBolt.PartTightness));
            Assert.Equal(new[] { 2, 3 }, values.Cast<int>());
            Assert.Equal(5, totals.Resolve(123, 15, 1)); // A deferred PartState also shares the receipt ordering.
            Assert.Equal(0, totals.Resolve(123, 21, 0));
            Assert.Equal(0, totals.Resolve(123, 20, 5));
            Assert.Equal(4, totals.Resolve(999, 1, 4)); // Another part has independent state.
            totals.Clear(); Assert.Equal(2, totals.Resolve(123, 1, 2));
        }

        [Fact]
        public void InvalidAggregateReceiptCannotPoisonTheOrderingBaseline()
        {
            var totals = new PartTightnessReceipts(); Assert.Equal(3, totals.Resolve(1, 1, 3));
            Assert.Throws<ArgumentException>(() => totals.Resolve(1, 2, float.NaN));
            Assert.Throws<ArgumentException>(() => totals.Resolve(1, 0, 0));
            Assert.Equal(4, totals.Resolve(1, 2, 4));
        }
    }
}
