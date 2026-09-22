using System;
using System.IO;
using System.Linq;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class FirewoodBuyerTests
    {
        private static FirewoodBuyerState Offer(uint revision = 1) => new FirewoodBuyerState { NetId = 42, Revision = revision, Flags = 3, Amount = 500 };
        [Fact]
        public void PacketCarriesAnAbsoluteOfferWithoutAnyPaymentAction()
        {
            var state = Offer(); var bytes = PacketCodec.Encode(state); Assert.Equal(43, bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal(209, reader.ReadUInt16()); Assert.Equal(42u, reader.ReadUInt32()); Assert.Equal(1u, reader.ReadUInt32());
            Assert.Equal(3, reader.ReadByte()); Assert.Equal(500, reader.ReadSingle());
            Assert.True(FirewoodBuyerPolicy.Same(state, Assert.IsType<FirewoodBuyerState>(PacketCodec.Decode(bytes))));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        }
        [Theory]
        [InlineData(0)] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidOffersCannotBeEncodedOrReceived(float amount)
        {
            var state = Offer(); var bytes = PacketCodec.Encode(state); state.Amount = amount;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Array.Copy(BitConverter.GetBytes(amount), 0, bytes, 11, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(0, 0, true)] [InlineData(1, 0, true)] [InlineData(2, 500, false)]
        [InlineData(0, 500, false)] [InlineData(1, 500, false)] [InlineData(7, 500, false)]
        public void HiddenOrWaitingBuyersCannotCarryAnOffer(byte flags, float amount, bool valid)
        {
            var state = Offer(); state.Flags = flags; state.Amount = amount;
            Assert.Equal(valid, FirewoodBuyerPolicy.Valid(state));
            state.NetId = 0; Assert.False(FirewoodBuyerPolicy.Valid(state));
        }
        [Fact]
        public void LateOffersCannotResurrectCollectedMoneyButAnIdenticalSeedCanRepairLocalLod()
        {
            var old = Offer(1); var collected = Offer(2); collected.Flags = 1; collected.Amount = 0;
            Assert.True(FirewoodBuyerPolicy.CanReceive(old, collected));
            Assert.False(FirewoodBuyerPolicy.CanReceive(collected, old));
            Assert.True(FirewoodBuyerPolicy.CanReceive(collected, FirewoodBuyerPolicy.Copy(collected)));
            var conflicting = Offer(2); Assert.False(FirewoodBuyerPolicy.CanReceive(collected, conflicting));
            Assert.True(FirewoodBuyerPolicy.CanReceive(Offer(uint.MaxValue), Offer(0)));
            Assert.False(FirewoodBuyerPolicy.CanReceive(Offer(0), Offer(uint.MaxValue)));
            Assert.False(FirewoodBuyerPolicy.CanReceive(Offer(0), Offer(0x80000000)));
            conflicting.NetId++; Assert.False(FirewoodBuyerPolicy.CanReceive(null, new FirewoodBuyerState()));
            Assert.False(FirewoodBuyerPolicy.CanReceive(old, conflicting));
        }
        [Fact]
        public void RetainedOfferDoesNotAliasTheIncomingPacket()
        {
            var state = Offer(); var copy = FirewoodBuyerPolicy.Copy(state); state.Amount = 1000; state.Flags = 0;
            Assert.Equal(500, copy.Amount); Assert.Equal(3, copy.Flags);
        }
        [Theory]
        [InlineData(3, false, 0, 3)] [InlineData(3, false, 2, 3)] [InlineData(3, false, 2.01f, 600)]
        [InlineData(3, true, 0, 600)] [InlineData(3, false, -1, 600)] [InlineData(-1, false, 0, 600)]
        [InlineData(float.NaN, false, 0, 600)] [InlineData(700, false, 0, 600)]
        public void OnlyFreshLivingGuestsCanKeepNativeBuyerChecksNearby(float distance, bool dead, float age, float expected)
            => Assert.Equal(expected, FirewoodBuyerPolicy.IncludeGuestDistance(600, distance, dead, age));
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(100001)]
        public void InvalidBuyerPosesAreRejected(float coordinate)
        {
            var state = Offer(); state.Position.X = coordinate; Assert.False(FirewoodBuyerPolicy.Valid(state));
            state = Offer(); state.Rotation = new NetQuaternion(); Assert.False(FirewoodBuyerPolicy.Valid(state));
        }
        [Fact]
        public void RelocatedBuyerPoseParticipatesInRevisionAndCopy()
        {
            var state = Offer(); state.Position = new NetVector3(12, 3, -45); state.Rotation = new NetQuaternion(0, 1, 0, 0);
            var copy = FirewoodBuyerPolicy.Copy(state);
            Assert.True(FirewoodBuyerPolicy.Same(state, Assert.IsType<FirewoodBuyerState>(PacketCodec.Decode(PacketCodec.Encode(copy)))));
            copy.Position.X++; Assert.False(FirewoodBuyerPolicy.CanReceive(state, copy));
            copy.Revision++; Assert.True(FirewoodBuyerPolicy.CanReceive(state, copy));
        }
        [Fact]
        public void OnlyTheAdmittedHostCanPublishBuyerState()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodBuyerState, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodBuyerState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodBuyerState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.FirewoodBuyerState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.FirewoodBuyerState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.FirewoodBuyerState, Channel.UnreliableSequenced));
        }
        [Fact]
        public void CatalogBindsFourSeparateBuyersAndContainsMalformedMetadata()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var data = SyncCatalogJson.Parse(json); Assert.Null(data.FirewoodBuyersError); Assert.Equal(4, data.FirewoodBuyers!.Count);
            Assert.Equal(4, data.FirewoodBuyers.Select(b => b.PaymentPath).Distinct().Count());
            var changed = SyncCatalogJson.Parse(json.Replace("\"lodPath\": \"JOBS/HouseWood2\"", "\"lodPath\": \"JOBS/HouseWood1\""));
            Assert.Null(changed.FirewoodBuyers); Assert.NotNull(changed.FirewoodBuyersError); Assert.NotNull(changed.ShoppingBags);
        }
    }
}
