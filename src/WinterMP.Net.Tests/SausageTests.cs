using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class SausageTests
    {
        private static SausageOpenIntent Intent() => new SausageOpenIntent { SourceId = 7, PackageId = 8, PlayerId = 1, Sequence = 1 };
        private static SausageState State() => new SausageState { ItemId = 3, Revision = 1, Condition = 67, Kind = 1, Grilled = true, Position = new NetVector3(1, 2, 3) };
        [Fact]
        public void ConversionAndFoodWireRoundTripWithExactLengths()
        {
            var open = PacketCodec.Encode(Intent()); Assert.Equal(15, open.Length);
            var read = Assert.IsType<SausageOpenIntent>(PacketCodec.Decode(open)); Assert.Equal(8u, read.PackageId); Assert.Equal(7u, read.SourceId); Assert.Equal(1, read.PlayerId);
            var bytes = PacketCodec.Encode(State()); Assert.Equal(44, bytes.Length);
            var state = Assert.IsType<SausageState>(PacketCodec.Decode(bytes)); Assert.True(state.Grilled); Assert.Equal(67, state.Condition); Assert.Equal(3, state.Position.Z);
            Array.Resize(ref bytes, bytes.Length - 1); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void SuccessivePackagesCannotReuseARetiredDisplayNameIdentity()
        {
            uint first = SausagePolicy.PackageId("sausages1"), second = SausagePolicy.PackageId("sausages2");
            var life = new ItemSpawnLifecycle(); life.Retire(first);
            Assert.NotEqual(first, second); Assert.False(life.IsRetired(second));
            Assert.Throws<ArgumentException>(() => SausagePolicy.PackageId("milk1"));
        }
        [Fact]
        public void FourOutputsAreDistinctAndSpecificToTheirPackage()
        {
            var ids = new HashSet<uint>();
            for (uint p = 1; p <= 100; p++) for (int n = 0; n < 4; n++) Assert.True(ids.Add(SausagePolicy.ItemId(p, n)));
            Assert.Throws<ArgumentOutOfRangeException>(() => SausagePolicy.ItemId(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SausagePolicy.ItemId(1, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => SausagePolicy.ItemId(1, -1));
        }
        [Fact]
        public void OpeningNeedsFreshActorUnspentPackageOwnershipAndContact()
        {
            var i = Intent(); Assert.True(SausagePolicy.CanOpen(i, 1, 0, true, true, true, true));
            Assert.False(SausagePolicy.CanOpen(i, 2, 0, true, true, true, true));
            Assert.False(SausagePolicy.CanOpen(i, 1, 1, true, true, true, true));
            Assert.False(SausagePolicy.CanOpen(i, 1, 2, true, true, true, true));
            Assert.False(SausagePolicy.CanOpen(i, 1, 0, false, true, true, true));
            Assert.False(SausagePolicy.CanOpen(i, 1, 0, true, false, true, true));
            Assert.False(SausagePolicy.CanOpen(i, 1, 0, true, true, false, true));
            Assert.False(SausagePolicy.CanOpen(i, 1, 0, true, true, true, false));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)] [InlineData(888)]
        public void InvalidFoodCannotCrossWire(float condition)
        {
            var state = State(); state.Condition = condition; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        }
        [Fact]
        public void FoodStateValidationIncludesAppearancePoseAndIdentity()
        {
            var s = State(); s.Kind = 0; Assert.False(SausagePolicy.Valid(s));
            s = State(); s.Grilled = false; Assert.False(SausagePolicy.Valid(s));
            s = State(); s.Kind = 2; Assert.True(SausagePolicy.Valid(s)); s.Grilled = false; Assert.True(SausagePolicy.Valid(s));
            s.Kind = 3; Assert.True(SausagePolicy.Valid(s));
            s.Kind = 4; Assert.False(SausagePolicy.Valid(s));
            s = State(); s.Rotation.W = float.NaN; Assert.False(SausagePolicy.Valid(s));
            s = State(); s.Position.Z = float.NaN; Assert.False(SausagePolicy.Valid(s));
            s = State(); s.ItemId = 0; Assert.False(SausagePolicy.Valid(s));
        }
        [Fact]
        public void OnlyHostSuppliesFoodAndGuestsRequestOnOrderedChannel()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.SausageState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.SausageState, false, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.SausageState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.SausageOpenIntent, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.SausageOpenIntent, true, true, false, true));
            foreach (var id in new[] { MessageId.SausageOpenIntent, MessageId.SausageState })
            { Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered)); Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced)); }
        }
        [Fact]
        public void MalformedSausageCatalogPreservesOtherSystems()
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var c = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(c.SausagesError); Assert.Equal(8, c.Sausages!.Paths.Count);
            json["sausages"]!.AsObject().Remove("prefab"); c = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.NotNull(c.SausagesError); Assert.Null(c.Sausages); Assert.NotNull(c.TractorTrailer);
        }
    }
}
