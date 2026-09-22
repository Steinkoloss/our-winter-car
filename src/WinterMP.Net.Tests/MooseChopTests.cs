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
    public class MooseChopTests
    {
        private static MooseChopIntent Request() => new MooseChopIntent { PlayerId = 1, Corpse = 9, Section = 0, ExpectedPieces = 0 };
        private static MooseCorpseState State(bool dead = true)
        {
            var s = new MooseCorpseState { Corpse = 9, Revision = 10, Dead = dead };
            if (dead)
            {
                s.Positions = new NetVector3[11]; s.Rotations = new NetQuaternion[11];
                for (int i = 0; i < 11; i++) { s.Positions[i] = new NetVector3(i, i + 1, i + 2); s.Rotations[i] = NetQuaternion.Identity; }
            }
            return s;
        }
        [Theory] [InlineData(false, 13)] [InlineData(true, 321)]
        public void CorpseRoundTripsEveryBodyWithoutVariableLengthAllocation(bool dead, int length)
        {
            var s = State(dead); var bytes = PacketCodec.Encode(s);
            Assert.Equal(length, bytes.Length);
            Assert.Equal(bytes, PacketCodec.Encode(Assert.IsType<MooseCorpseState>(PacketCodec.Decode(bytes))));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new ArraySegment<byte>(bytes, 0, bytes.Length - 1).ToArray()));
        }
        [Fact]
        public void ChopWireIsBoundToPlayerCorpseSectionAndObservedCount()
        {
            var request = Request(); request.Section = 1; request.ExpectedPieces = 3;
            Assert.Equal(new byte[] { 215, 0, 1, 9, 0, 0, 0, 1, 3 }, PacketCodec.Encode(request));
            Assert.Equal(PacketCodec.Encode(request), PacketCodec.Encode(PacketCodec.Decode(PacketCodec.Encode(request))));
        }
        [Fact]
        public void RetriesAndRacingPlayersConsumeTheObservedPieceOnce()
        {
            var request = Request(); int pieces = 0, outputs = 0;
            for (int i = 0; i < 5; i++)
            {
                request.PlayerId = (byte)(i % 2 + 1);
                if (MooseChopPolicy.CanChop(request, 9, pieces, true, true, true, 4)) { pieces++; outputs++; }
            }
            Assert.Equal(1, pieces); Assert.Equal(1, outputs);
            request.ExpectedPieces = 1;
            Assert.True(MooseChopPolicy.CanChop(request, 9, pieces, true, true, true, 16));
        }
        [Theory]
        [InlineData("far")] [InlineData("stale")] [InlineData("alive")] [InlineData("busy")]
        [InlineData("epoch")] [InlineData("count")] [InlineData("nan")] [InlineData("negative")]
        public void HostRejectsUnavailableOrMismatchedRequests(string fault)
        {
            var request = Request();
            Assert.False(MooseChopPolicy.CanChop(request, fault == "epoch" ? 10u : 9u, fault == "count" ? 1 : 0,
                fault != "alive", fault != "busy", fault != "stale", fault == "nan" ? float.NaN : fault == "negative" ? -1 : fault == "far" ? 16.01f : 4));
        }
        [Theory] [InlineData("player")] [InlineData("corpse")] [InlineData("section")] [InlineData("limit")]
        public void MalformedChopsFailBeforeGameplay(string fault)
        {
            var r = Request();
            if (fault == "player") r.PlayerId = 255;
            if (fault == "corpse") r.Corpse = 0;
            if (fault == "section") r.Section = 2;
            if (fault == "limit") r.ExpectedPieces = 4;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(r));
        }
        [Theory] [InlineData("count")] [InlineData("living-count")] [InlineData("missing-body")]
        [InlineData("nan")] [InlineData("rotation")] [InlineData("epoch")]
        public void MalformedCorpsesFailBeforeGameplay(string fault)
        {
            var s = State(fault != "living-count");
            if (fault == "count") s.FrontPieces = 5;
            if (fault == "living-count") s.RearPieces = 1;
            if (fault == "missing-body") s.Rotations = new NetQuaternion[10];
            if (fault == "nan") s.Positions[1].Y = float.NaN;
            if (fault == "rotation") s.Rotations[10] = new NetQuaternion();
            if (fault == "epoch") s.Corpse = 0;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
        }
        [Fact]
        public void ReceiptsCannotResurrectAnAnimalOrRestoreConsumedMeat()
        {
            var old = State(); old.FrontPieces = 2;
            var next = State(); next.Revision++;
            Assert.False(MooseChopPolicy.CanReceive(old, next));
            next.FrontPieces = 2; Assert.True(MooseChopPolicy.CanReceive(old, next));
            Assert.False(MooseChopPolicy.CanReceive(next, old));
            Assert.False(MooseChopPolicy.CanReceive(old, old));
            var live = State(false); live.Revision = 11; Assert.False(MooseChopPolicy.CanReceive(old, live));
            live.Corpse++; Assert.True(MooseChopPolicy.CanReceive(old, live));
            old.Corpse = uint.MaxValue; live.Corpse = 1; Assert.True(MooseChopPolicy.CanReceive(old, live));
            old = State(); old.Revision = uint.MaxValue; next = State(); next.Revision = 0;
            Assert.True(MooseChopPolicy.CanReceive(old, next));
            next.Revision = 0x7fffffffu; Assert.False(MooseChopPolicy.CanReceive(old, next));
        }
        [Fact]
        public void ReadRejectsUnknownDeadFlagAndInvalidChopFields()
        {
            var bytes = PacketCodec.Encode(State()); bytes[10] = 2;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            bytes = PacketCodec.Encode(Request()); bytes[8] = 4;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void OnlyAdmittedPeersCanRequestAndOnlyHostCanPublishCorpse()
        {
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseChopIntent, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseChopIntent, true, false, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseChopIntent, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseCorpseState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseCorpseState, false, true, true, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseCorpseState, false, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseCorpseState, false, true, true, true));
            foreach (var id in new[] { MessageId.MooseChopIntent, MessageId.MooseCorpseState })
            {
                Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
            }
        }
        [Fact]
        public void MissingChopBindingsLeaveExistingMeatAndOtherSystemsAvailable()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            Assert.NotNull(SyncCatalogJson.Parse(json).MooseChop);
            foreach (string key in MooseChopData.Required)
            {
                var root = JsonNode.Parse(json)!; root["mooseChop"]!.AsObject().Remove(key);
                var data = SyncCatalogJson.Parse(root.ToJsonString());
                Assert.Null(data.MooseChop); Assert.NotNull(data.MooseChopError);
                Assert.NotNull(data.MooseMeat); Assert.NotNull(data.ShoppingBags);
            }
        }
    }
}
