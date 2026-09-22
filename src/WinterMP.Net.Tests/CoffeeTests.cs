using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class CoffeeTests
    {
        private static CoffeeState Pot() => new CoffeeState { ItemId = 4, Revision = 7, Flags = 3, Water = 1.3f, Ground = 12, Coffee = .6f, Caffeine = .7f, BoilVolume = .4f };
        [Fact]
        public void CoffeeWirePreservesExactLengthsAndRejectsTruncation()
        {
            IMessage[] messages = { new CoffeeIntent { ItemId = 4, Sequence = 6, PlayerId = 1, Action = CoffeeAction.Drink }, Pot(),
                new CoffeeDrinkResult { ItemId = 5, Sequence = 8, PlayerId = 2, Amount = .23f, Caffeine = .7f } };
            int[] lengths = { 12, 60, 19 };
            for (int i = 0; i < messages.Length; i++)
            {
                var bytes = PacketCodec.Encode(messages[i]); Assert.Equal(lengths[i], bytes.Length);
                Assert.Equal(bytes, PacketCodec.Encode(PacketCodec.Decode(bytes)));
                Array.Resize(ref bytes, bytes.Length - 1); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            }
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1)] [InlineData(101)]
        public void InvalidContentsCannotCrossWire(float bad)
        {
            var s = Pot(); s.Water = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Pot(); s.Ground = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Pot(); s.Coffee = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Pot(); s.Caffeine = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Pot(); s.BoilVolume = bad; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            Assert.Equal(0, CoffeePolicy.Transfer(bad, 0, .1f));
            Assert.Equal(0, CoffeePolicy.Transfer(1, bad, .1f));
            Assert.Equal(0, CoffeePolicy.Transfer(1, 0, bad));
        }
        [Fact]
        public void CupAndPacketHaveDifferentCapacitiesAndFields()
        {
            var s = new CoffeeState { ItemId = 1, Revision = 1, Kind = 1, Coffee = .3f, Caffeine = 1.5f };
            Assert.True(CoffeePolicy.Valid(s)); s.Coffee = .301f; Assert.False(CoffeePolicy.Valid(s));
            s.Coffee = .3f; s.Flags = 1; Assert.False(CoffeePolicy.Valid(s)); s.Flags = 0; s.Ground = 1; Assert.False(CoffeePolicy.Valid(s));
            s = new CoffeeState { ItemId = 1, Revision = 1, Kind = 2, Ground = 100 };
            Assert.True(CoffeePolicy.Valid(s)); s.Ground = 101; Assert.False(CoffeePolicy.Valid(s));
            s.Ground = 100; s.Coffee = .1f; Assert.False(CoffeePolicy.Valid(s));
            s = Pot(); s.Position.Y = float.NaN; Assert.False(CoffeePolicy.Valid(s));
            s = Pot(); s.Rotation.W = 0; Assert.False(CoffeePolicy.Valid(s));
        }
        [Theory]
        [InlineData(2f, 0f)] [InlineData(.11f, .1f)] [InlineData(1f, .299f)] [InlineData(.1f, 0f)] [InlineData(1f, .3f)]
        public void TransferConservesContentsAndStopsAtEitherBoundary(float water, float cup)
        {
            float total = water + cup;
            for (int n = 0; n < 200; n++)
            {
                float amount = CoffeePolicy.Transfer(water, cup, .1f);
                Assert.InRange(amount, 0, .016001f);
                water -= amount; cup += amount;
                Assert.InRange(Math.Abs(water + cup - total), 0, .00001f);
                Assert.InRange(water, .099999f, 2); Assert.InRange(cup, 0, .300001f);
            }
            Assert.Equal(0, CoffeePolicy.Transfer(water, cup, .1f));
        }
        [Fact]
        public void IdentityAndRolesExcludeInventedPacketsAndGuestReceipts()
        {
            Assert.NotEqual(CoffeePolicy.PackageId("groundcoffee01"), CoffeePolicy.PackageId("groundcoffee02"));
            Assert.Throws<ArgumentException>(() => CoffeePolicy.PackageId("ground coffee(itemx)"));
            Assert.Throws<ArgumentException>(() => CoffeePolicy.PackageId("groundcoffee1"));
            foreach (var id in new[] { MessageId.CoffeeState, MessageId.CoffeeDrinkResult })
            {
                Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, false, true));
                Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
                Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
            }
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.CoffeeIntent, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.CoffeeIntent, true, true, false, true));
            foreach (var id in new[] { MessageId.CoffeeIntent, MessageId.CoffeeState, MessageId.CoffeeDrinkResult })
            { Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered)); Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced)); }
        }
        [Fact]
        public void MalformedCoffeeCatalogDisablesOnlyCoffee()
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var c = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(c.CoffeeError); Assert.NotNull(c.Coffee);
            json["coffee"]!.AsObject().Remove("pot"); c = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.NotNull(c.CoffeeError); Assert.Null(c.Coffee); Assert.NotNull(c.Sausages);
        }
    }
}
