using System;
using System.IO;
using System.Linq;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class MilkConditionTests
    {
        private static MilkConditionState Fresh(uint revision = 1) => new MilkConditionState { NetId = 12, Revision = revision, Condition = 87 };
        [Fact]
        public void PacketCarriesConditionAndNativeSpoiledPhase()
        {
            var state = Fresh(); var bytes = PacketCodec.Encode(state); Assert.Equal(15, bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal(208, reader.ReadUInt16()); Assert.Equal(12u, reader.ReadUInt32()); Assert.Equal(1u, reader.ReadUInt32());
            Assert.Equal(87, reader.ReadSingle()); Assert.Equal(0, reader.ReadByte());
            Assert.True(MilkConditionPolicy.Same(state, Assert.IsType<MilkConditionState>(PacketCodec.Decode(bytes))));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        [InlineData(-1f)] [InlineData(100.01f)] [InlineData(888f)]
        public void InvalidConditionAndConsumptionSentinelAreRejected(float value)
        {
            var state = Fresh(); var bytes = PacketCodec.Encode(state); state.Condition = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Array.Copy(BitConverter.GetBytes(value), 0, bytes, 10, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void InvalidIdentityAndSpoiledFlagAreRejected()
        {
            var state = Fresh(); state.NetId = 0; Assert.False(MilkConditionPolicy.Valid(state));
            state = Fresh(); state.Spoiled = 1; Assert.False(MilkConditionPolicy.Valid(state));
            state.Condition = 1; Assert.True(MilkConditionPolicy.Valid(state));
            state.Condition = 0; Assert.True(MilkConditionPolicy.Valid(state));
            state.Spoiled = 2; Assert.False(MilkConditionPolicy.Valid(state));
            var bytes = PacketCodec.Encode(Fresh()); bytes[14] = 2;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void DuplicateRepairsButStaleOrContradictoryStateCannotRefreshMilk()
        {
            var state = Fresh(2); var next = Fresh(3); next.Condition = 86;
            Assert.True(MilkConditionPolicy.CanReceive(null, state));
            Assert.True(MilkConditionPolicy.CanReceive(state, Fresh(2)));
            Assert.True(MilkConditionPolicy.CanReceive(state, next));
            Assert.False(MilkConditionPolicy.CanReceive(next, state));
            next.Revision = 2; Assert.False(MilkConditionPolicy.CanReceive(state, next));
            next = Fresh(3); next.NetId = 13; Assert.False(MilkConditionPolicy.CanReceive(state, next));
            Assert.True(MilkConditionPolicy.CanReceive(Fresh(uint.MaxValue), Fresh(0)));
            Assert.False(MilkConditionPolicy.CanReceive(Fresh(0), Fresh(uint.MaxValue)));
            Assert.False(MilkConditionPolicy.CanReceive(Fresh(0), Fresh(0x80000000)));
        }
        [Fact]
        public void RetainedStateDoesNotAliasThePacket()
        {
            var state = Fresh(); var copy = MilkConditionPolicy.Copy(state); state.Condition = 2; state.Spoiled = 1;
            Assert.Equal(87, copy.Condition); Assert.Equal(0, copy.Spoiled);
        }
        [Fact]
        public void OnlyTheAdmittedHostMayPublishCondition()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MilkConditionState, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.MilkConditionState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MilkConditionState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MilkConditionState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.MilkConditionState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.MilkConditionState, Channel.UnreliableSequenced));
        }
        [Fact]
        public void CatalogSelectsMilkAndContainsUnsupportedFoodMetadata()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var data = SyncCatalogJson.Parse(json); Assert.NotNull(data.MilkCondition); Assert.Null(data.MilkConditionError);
            Assert.Equal("Spoil 2", data.MilkCondition!.Spoil); Assert.Equal("Condition", data.MilkCondition.Condition);
            var changed = SyncCatalogJson.Parse(json.Replace("\"itemName\": \"milk(itemx)\"", "\"itemName\": \"other(itemx)\""));
            Assert.Null(changed.MilkCondition); Assert.NotNull(changed.MilkConditionError); Assert.NotNull(changed.ShoppingBags);
        }
    }
}
