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
    public class PartFitTests
    {
        private static ReplacementPartState Part() => new ReplacementPartState { NativeId = "VIN1331", Revision = 10 };
        private static PartFitRequest Request(byte actor = 1, uint sequence = 1)
        {
            Assert.True(PartIdentity.TryItemId(Part().NativeId, out uint id));
            return new PartFitRequest { PlayerId = actor, Token = 0x0123456789ABCDEF, Sequence = sequence,
                ItemId = id, ExpectedRevision = 10 };
        }

        [Fact]
        public void LayoutNamesAnObservedPartWithoutGuestMountPoseOrConditionAuthority()
        {
            var request = Request(sequence: 0x12345678); byte[] bytes = PacketCodec.Encode(request);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal(188, reader.ReadUInt16()); Assert.Equal(request.PlayerId, reader.ReadByte());
            Assert.Equal(request.Token, reader.ReadUInt64()); Assert.Equal(request.Sequence, reader.ReadUInt32());
            Assert.Equal(request.ItemId, reader.ReadUInt32()); Assert.Equal(request.ExpectedRevision, reader.ReadUInt32());
            Assert.Equal(0, reader.ReadByte()); Assert.Equal(0, reader.ReadByte()); Assert.Equal(25, bytes.Length);
            var receipt = PartFitLedger.Receipt(request, PartFitStatus.Accepted);
            bytes = PacketCodec.Encode(receipt);
            using var result = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal(189, result.ReadUInt16()); Assert.Equal(request.PlayerId, result.ReadByte());
            Assert.Equal(request.Token, result.ReadUInt64()); Assert.Equal(request.Sequence, result.ReadUInt32());
            Assert.Equal(request.ItemId, result.ReadUInt32()); Assert.Equal((byte)PartFitStatus.Accepted, result.ReadByte());
            Assert.Equal(0, result.ReadByte()); Assert.Equal(0, result.ReadByte()); Assert.Equal(22, bytes.Length);
            foreach (var message in new IMessage[] { request, receipt })
            {
                var wire = PacketCodec.Encode(message);
                Assert.Equal(wire, PacketCodec.Encode(PacketCodec.Decode(wire)));
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(wire.Length - 1).ToArray()));
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[] { 0 }).ToArray()));
                Assert.True(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.ReliableBulk));
            }
            bytes[bytes.Length - 1] = 255;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            receipt.Status = (PartFitStatus)255;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(receipt));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LostReceiptsNeverRepeatAnInstallationOrAPartialFailure(bool success)
        {
            var ledger = new PartFitLedger(); var request = Request(); int nativeCalls = 0;
            for (int retry = 0; retry < 100; retry++)
            {
                var receipt = ledger.Inspect(request, 1, out bool begin);
                if (begin) { ledger.Begin(request, 1, PartFitStatus.Pending); nativeCalls++; }
                else Assert.Equal(retry < 50 ? PartFitStatus.Pending : success ? PartFitStatus.Accepted : PartFitStatus.Failed, receipt!.Status);
                if (retry == 49) ledger.Complete(request, success);
            }
            Assert.Equal(1, nativeCalls);
            Assert.Equal(success ? PartFitStatus.Accepted : PartFitStatus.Failed, ledger.Complete(request, !success)!.Status);
        }

        [Theory]
        [InlineData(0, 0, 1UL)]
        [InlineData(255, 255, 1UL)]
        [InlineData(1, 2, 1UL)]
        [InlineData(1, 1, 0UL)]
        public void IdentityAndConnectionTokenAreRequired(byte claimed, byte actor, ulong token)
        {
            var request = Request(claimed); request.Token = token; var ledger = new PartFitLedger();
            Assert.Null(ledger.Begin(request, actor, PartFitStatus.Pending));
            Assert.Null(ledger.Inspect(request, actor, out bool begin)); Assert.False(begin);
        }

        [Theory]
        [InlineData("item")]
        [InlineData("revision")]
        [InlineData("token")]
        public void ARepeatedSequenceCannotChangeTheFittingTarget(string change)
        {
            var ledger = new PartFitLedger(); var original = Request(); ledger.Begin(original, 1, PartFitStatus.Pending);
            var changed = Request();
            if (change == "item") changed.ItemId++; else if (change == "revision") changed.ExpectedRevision++; else changed.Token++;
            Assert.Null(ledger.Inspect(changed, 1, out bool begin)); Assert.False(begin);
            Assert.Null(ledger.Complete(changed, true));
            original.ItemId++;
            Assert.Equal(PartFitStatus.Pending, ledger.Inspect(Request(), 1, out _)!.Status);
        }

        [Fact]
        public void BusyMountRequiresAnotherClickInsteadOfAnAutomaticFutureInstallation()
        {
            var ledger = new PartFitLedger(); var first = Request(); var second = Request(2);
            ledger.Begin(first, 1, PartFitStatus.Pending);
            ledger.Begin(second, 2, PartFitStatus.Busy);
            ledger.Complete(first, true);
            Assert.Equal(PartFitStatus.Busy, ledger.Inspect(second, 2, out bool begin)!.Status); Assert.False(begin);
            second.Sequence++;
            Assert.Null(ledger.Inspect(second, 2, out begin)); Assert.True(begin);
            var fitted = Part(); fitted.Revision++; fitted.Installed = true; fitted.AssemblyId = 1;
            Assert.Equal(PartFitStatus.Stale, PartFitLedger.Check(second, fitted, true, true, true, false, true, false));
            second.ExpectedRevision++;
            Assert.Equal(PartFitStatus.NotLoose, PartFitLedger.Check(second, fitted, true, true, true, false, true, false));
        }

        [Theory]
        [InlineData(false, true, true, true, true, false, PartFitStatus.Unavailable)]
        [InlineData(true, false, true, true, true, false, PartFitStatus.TooFar)]
        [InlineData(true, true, false, true, true, false, PartFitStatus.TooFar)]
        [InlineData(true, true, true, false, true, false, PartFitStatus.Blocked)]
        [InlineData(true, true, true, true, false, false, PartFitStatus.Busy)]
        [InlineData(true, true, true, true, true, true, PartFitStatus.Busy)]
        [InlineData(true, true, true, true, true, false, PartFitStatus.Pending)]
        public void PartProximityMountProximityOccupancyAndOwnershipAllGateFitting(bool available, bool nearby,
            bool inRange, bool mountFree, bool authority, bool busy, PartFitStatus expected)
            => Assert.Equal(expected, PartFitLedger.Check(Request(), Part(), available, nearby, inRange, mountFree, authority, busy));

        [Fact]
        public void MissingUnrelatedRetiredAndTransitioningPartsCannotBeFitted()
        {
            var request = Request();
            Assert.Equal(PartFitStatus.Unavailable, PartFitLedger.Check(request, null, true, true, true, true, true, false));
            var part = Part(); part.NativeId = "VIN1332";
            Assert.Equal(PartFitStatus.Unavailable, PartFitLedger.Check(request, part, true, true, true, true, true, false));
            part = Part(); part.AssemblyId = -1;
            Assert.Equal(PartFitStatus.NotLoose, PartFitLedger.Check(request, part, true, true, true, true, true, false));
            part.AssemblyId = 0; part.Installed = true;
            Assert.Equal(PartFitStatus.NotLoose, PartFitLedger.Check(request, part, true, true, true, true, true, false));
        }

        [Fact]
        public void MountDistanceUsesTheNativeStrictToleranceAndRejectsInvalidCoordinates()
        {
            var origin = new NetVector3();
            Assert.True(PartFitLedger.WithinMount(new NetVector3(.05f, .05f, .05f), origin, .1f));
            Assert.False(PartFitLedger.WithinMount(new NetVector3(.1f, 0, 0), origin, .1f));
            Assert.False(PartFitLedger.WithinMount(new NetVector3(.09f, .09f, 0), origin, .1f));
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue })
            {
                Assert.False(PartFitLedger.WithinMount(new NetVector3(bad, 0, 0), origin, .1f));
                Assert.False(PartFitLedger.WithinMount(origin, new NetVector3(0, bad, 0), .1f));
                Assert.False(PartFitLedger.WithinMount(origin, origin, bad));
            }
            foreach (float bad in new[] { 0f, -1f, 1.001f }) Assert.False(PartFitLedger.WithinMount(origin, origin, bad));
        }

        [Fact]
        public void SequenceWraparoundAndReadmissionCannotReplayOldRequests()
        {
            var ledger = new PartFitLedger(); var request = Request(sequence: uint.MaxValue);
            ledger.Begin(request, 1, PartFitStatus.Pending); ledger.Complete(request, true);
            request.Sequence = 0; ledger.Begin(request, 1, PartFitStatus.Pending);
            Assert.Null(ledger.Inspect(Request(sequence: uint.MaxValue), 1, out bool begin)); Assert.False(begin);
            var fresh = Request(); fresh.Token++;
            Assert.Null(ledger.Begin(fresh, 1, PartFitStatus.Pending));
            ledger.ForgetPlayer(1); ledger.Begin(fresh, 1, PartFitStatus.Pending);
            Assert.Null(ledger.Complete(request, true));
            Assert.Equal(PartFitStatus.Pending, ledger.Inspect(fresh, 1, out _)!.Status);
            ledger.Clear(); Assert.Null(ledger.Inspect(fresh, 1, out begin)); Assert.True(begin);
        }

        [Theory]
        [InlineData(false, 1, 1, 100f, true)]
        [InlineData(true, 1, 1, 0f, false)]
        [InlineData(false, 2, 1, 0f, false)]
        [InlineData(false, 255, 1, 0f, true)]
        [InlineData(false, 255, 1, .5f, true)]
        [InlineData(false, 255, 1, .501f, false)]
        [InlineData(false, 255, 1, -1f, false)]
        [InlineData(false, 255, 2, 0f, false)]
        [InlineData(false, 255, 1, float.NaN, false)]
        [InlineData(false, 255, 1, float.PositiveInfinity, false)]
        public void FittingClickMayFollowItsOwnFreshReleaseButCannotStealAPart(bool hostOwns,
            byte remoteOwner, byte lastOwner, float releaseAge, bool expected)
            => Assert.Equal(expected, PartFitLedger.HasAuthority(1, hostOwns, remoteOwner, lastOwner, releaseAge));

        [Fact]
        public void AnotherClickCannotReplaceAnUnfinishedOperation()
        {
            var ledger = new PartFitLedger(); var first = Request(); var next = Request(sequence: 2);
            ledger.Begin(first, 1, PartFitStatus.Pending);
            Assert.Equal(PartFitStatus.Busy, ledger.Begin(next, 1, PartFitStatus.Pending)!.Status);
            Assert.Equal(PartFitStatus.Accepted, ledger.Complete(first, true)!.Status);
            Assert.Equal(PartFitStatus.Pending, ledger.Begin(next, 1, PartFitStatus.Pending)!.Status);
        }

        [Fact]
        public void ClientRetriesOnlyTheOriginalClickAndMatchesAllReceiptFields()
        {
            var client = new PartFitClient(99); Assert.True(client.TryBegin(1, 123, 4));
            Assert.False(client.TryBegin(1, 456, 5)); Assert.True(client.Pending);
            var request = client.Poll(0)!; request.ItemId = 999;
            Assert.Null(client.Poll(.49f)); request = client.Poll(.5f)!; Assert.Equal(123u, request.ItemId);
            Assert.False(client.Receive(PartFitLedger.Receipt(request, PartFitStatus.Pending)));
            foreach (string field in new[] { "player", "token", "sequence", "item", "status" })
            {
                var bad = PartFitLedger.Receipt(request, PartFitStatus.Accepted);
                if (field == "player") bad.PlayerId++;
                else if (field == "token") bad.Token++;
                else if (field == "sequence") bad.Sequence++;
                else if (field == "item") bad.ItemId++;
                else bad.Status = (PartFitStatus)255;
                Assert.False(client.Receive(bad));
            }
            var busy = PartFitLedger.Receipt(request, PartFitStatus.Busy);
            Assert.True(client.Receive(busy)); Assert.False(client.Pending); Assert.Null(client.Poll(2));
            Assert.False(client.Receive(busy)); Assert.True(client.TryBegin(1, 123, 4));
            Assert.Equal(2u, client.Poll(2)!.Sequence);
            Assert.Null(client.Poll(float.NaN)); Assert.Null(client.Poll(float.PositiveInfinity));
        }

        [Fact]
        public void FittingCatalogRequiresEveryNativeEntryBinding()
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var catalog = SyncCatalogJson.Parse(text).ReplacementParts!;
            Assert.Equal("ASSEMBLING", catalog["fitEvent"]); Assert.Equal("PROCEED", catalog["fitConfirmEvent"]);
            foreach (string key in ReplacementPartsData.RequiredBindings.Where(k => k.StartsWith("fit", StringComparison.Ordinal)))
            {
                var broken = JsonNode.Parse(text)!; broken["replacementParts"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
        }
    }
}
