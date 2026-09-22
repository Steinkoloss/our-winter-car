using System;
using System.Collections;
using System.IO;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class ValveAdjustmentPolicyTests
    {
        [Fact]
        public void CatalogTargetsValveControlsAndContainsMalformedProfiles()
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var valid = WinterMP.Core.Catalog.SyncCatalogJson.Parse(json.ToJsonString());
            Assert.NotNull(valid.ValveAdjustment); Assert.Equal("ValveAdjustment/Masked", valid.ValveAdjustment!.ParentPath);
            foreach (string field in new[] { "parentPath", "fsm", "reference", "headPrefix" })
            {
                var copy = json.DeepClone(); copy["valveAdjustment"]![field] = "Changed";
                var bad = WinterMP.Core.Catalog.SyncCatalogJson.Parse(copy.ToJsonString());
                Assert.Null(bad.ValveAdjustment); Assert.NotNull(bad.ValveAdjustmentError);
                Assert.Equal(valid.Bolts.Count, bad.Bolts.Count); Assert.Equal(valid.Doors.Count, bad.Doors.Count);
            }
        }
        [Fact]
        public void OnlyAuthenticatedSelectedHostCanPublishOrderedSettings()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.ValveAdjustmentState, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.ValveAdjustmentState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.ValveAdjustmentState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.ValveAdjustmentState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.ValveAdjustmentState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.ValveAdjustmentState, Channel.ReliableBulk));
        }
        [Fact]
        public void WirePreservesFractionalSavedSettingsAndRejectsMalformedPackets()
        {
            var state = new ValveAdjustmentState { NetId = 123, Setting = 4.137f };
            byte[] bytes = PacketCodec.Encode(state); Assert.Equal(10, bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal(206, reader.ReadUInt16()); Assert.Equal(123u, reader.ReadUInt32()); Assert.Equal(4.137f, reader.ReadSingle());
            Assert.Equal(state.Setting, Assert.IsType<ValveAdjustmentState>(PacketCodec.Decode(bytes)).Setting);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(9).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
            foreach (float value in new[] { 1.99f, 8.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                state.Setting = value; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
                Array.Copy(BitConverter.GetBytes(value), 0, bytes, 6, 4);
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            }
            state.NetId = 0; state.Setting = 4; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        }

        [Theory]
        [InlineData(2f, -1, false)] [InlineData(2f, 1, true)] [InlineData(8f, 1, false)] [InlineData(8f, -1, true)]
        [InlineData(2.01f, -1, true)] [InlineData(7.99f, 1, true)] [InlineData(4.137f, 1, true)]
        [InlineData(4f, 0, false)] [InlineData(4f, 2, false)] [InlineData(float.NaN, 1, false)]
        public void TurnsKeepNativeBoundsAndFractionalBaselines(float value, int direction, bool allowed) =>
            Assert.Equal(allowed, ValveAdjustmentPolicy.CanTurn(value, direction));

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
        public void CapturesExactFloatSlotsWithoutChangingTheArray(int index)
        {
            var values = new ArrayList(Enumerable.Range(0, 8).Select(i => 4.137f + i * .1f).ToArray());
            var before = values.ToArray();
            Assert.True(ValveAdjustmentPolicy.Read(values, index, out float setting));
            Assert.Equal(before[index], setting); Assert.Equal(before, values.ToArray());
            var state = new ValveAdjustmentState { NetId = 123, Setting = setting };
            Assert.NotEqual(ValveAdjustmentPolicy.MixChecksum(0, state),
                ValveAdjustmentPolicy.MixChecksum(0, new ValveAdjustmentState { NetId = 123, Setting = setting + .05f }));
        }

        [Fact]
        public void MalformedArraysCannotBecomeHostSettings()
        {
            foreach (object bad in new object[] { 4, 4d, "4", float.NaN, float.PositiveInfinity })
            {
                var values = new ArrayList(Enumerable.Repeat(4f, 8).ToArray()); values[7] = bad;
                Assert.False(ValveAdjustmentPolicy.Read(values, 0, out _)); Assert.Equal(4f, values[0]);
            }
            foreach (int count in new[] { 0, 7, 9 })
                Assert.False(ValveAdjustmentPolicy.Read(new ArrayList(Enumerable.Repeat(4f, count).ToArray()), 0, out _));
            var valid = new ArrayList(Enumerable.Repeat(4f, 8).ToArray());
            Assert.False(ValveAdjustmentPolicy.Read(valid, -1, out _)); Assert.False(ValveAdjustmentPolicy.Read(valid, 8, out _));
            Assert.False(ValveAdjustmentPolicy.Read(null, 0, out _));
        }

        [Fact]
        public void UninitializedNativeZerosCannotBeSentAsSettings()
        {
            var values = new ArrayList(Enumerable.Repeat(0f, 8).ToArray());
            Assert.False(ValveAdjustmentPolicy.Read(values, 0, out _));
            values[0] = 4.137f;
            Assert.True(ValveAdjustmentPolicy.Read(values, 0, out float actual)); Assert.Equal(4.137f, actual);
            Assert.False(ValveAdjustmentPolicy.Read(values, 1, out _));
        }
    }
}
