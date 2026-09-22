using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;
namespace WinterMP.Net.Tests
{
    public sealed class AdvertPhoneTests
    {
        [Fact]
        public void CompletedCallRequiresHostDurationAndRecentConnection()
        {
            var ledger = new AdvertCallLedger();
            Assert.True(ledger.Begin(1, 0, 1, 10, true));
            for (int i = 11; i < 82; i++) Assert.True(ledger.Keep(1, 0, 1, i));
            Assert.False(ledger.Complete(1, 0, 1, 81.99f));
            Assert.True(ledger.Finishing(81.99f));
            Assert.False(ledger.Complete(2, 0, 1, 82));
            Assert.False(ledger.Complete(1, 1, 1, 82));
            Assert.True(ledger.Complete(1, 0, 1, 82));
            Assert.False(ledger.Complete(1, 0, 1, 82));
            Assert.False(ledger.Begin(1, 0, 1, 83, true));
        }
        [Fact]
        public void BusyAndRejectedAttemptsCannotReplayAfterCancellation()
        {
            var l = new AdvertCallLedger();
            Assert.True(l.Begin(1, 0, 1, 1, true));
            Assert.False(l.Begin(2, 2, 1, 2, true));
            l.Cancel(); Assert.False(l.Begin(2, 2, 1, 3, true));
            Assert.False(l.Begin(2, 2, 2, 3, false));
            Assert.False(l.Begin(2, 2, 2, 4, true));
            Assert.True(l.Begin(2, 2, 3, 4, true));
            l.Forget(2); Assert.False(l.Active); Assert.True(l.Begin(2, 2, 1, 5, true));
        }
        [Fact]
        public void DeadOrStaleLeaseCannotBeRevivedOrCompleted()
        {
            var l = new AdvertCallLedger(); Assert.True(l.Begin(1, 0, 1, 0, true));
            Assert.False(l.Finishing(1));
            Assert.False(l.Keep(1, 0, 1, 9)); Assert.False(l.Complete(1, 0, 1, 72));
            l.Clear(); Assert.True(l.Begin(1, 0, 1, 0, true));
            Assert.False(l.Keep(1, 0, 1, -1));
            for (int i = 1; i <= 80; i++) Assert.True(l.Keep(1, 0, 1, i));
            Assert.False(l.Keep(1, 0, 1, 81));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1f)]
        public void InvalidTimeCannotGrantOrCompleteCall(float time)
        {
            var l = new AdvertCallLedger(); Assert.False(l.Begin(1, 0, 1, time, true));
            Assert.True(l.Begin(1, 0, 1, 0, true)); Assert.False(l.Keep(1, 0, 1, time)); Assert.False(l.Complete(1, 0, 1, time));
        }
        [Fact]
        public void SequenceWrapAndSessionReset()
        {
            var l = new AdvertCallLedger(); Assert.True(l.Begin(1, 0, uint.MaxValue, 0, true)); l.Cancel();
            Assert.True(l.Begin(1, 0, 1, 1, true)); l.Cancel(); Assert.False(l.Begin(1, 0, uint.MaxValue, 2, true));
            l.Clear(); Assert.True(l.Begin(1, 0, 1, 2, true));
        }
        [Fact]
        public void MessagesAreStrictAuthenticatedOrderedAndRoundTrip()
        {
            IMessage[] messages = { new AdvertPhoneIntent { Call=41, PlayerId=2, Phone=2, Action=3 }, new AdvertPhoneResult { Call=41, PlayerId=2, Phone=2, Status=2 } };
            foreach (var m in messages)
            {
                byte[] bytes = PacketCodec.Encode(m); Assert.Equal(9, bytes.Length); Assert.Equal(bytes, PacketCodec.Encode(PacketCodec.Decode(bytes)));
                for (int n = 0; n < bytes.Length; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.AsSpan(0, n).ToArray()));
                foreach (int offset in new[] { 6, 7, 8 })
                { var bad = (byte[])bytes.Clone(); bad[offset] = 255; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bad)); }
                Assert.True(SessionMessagePolicy.IsChannelAllowed(m.Id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(m.Id, Channel.ReliableBulk));
            }
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertPhoneIntent, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertPhoneIntent, true, false, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertPhoneIntent, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertPhoneResult, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertPhoneResult, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertPhoneResult, true, true, false, true));
        }
        [Fact]
        public void MissingPhoneCatalogDoesNotDisableSharedAdvertDelivery()
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var c = SyncCatalogJson.Parse(root.ToJsonString()); Assert.NotNull(c.AdvertPhone); Assert.Null(c.AdvertPhoneError);
            root["advertPhone"]!.AsObject().Remove("phone1");
            c = SyncCatalogJson.Parse(root.ToJsonString()); Assert.Null(c.AdvertPhone); Assert.NotNull(c.AdvertPhoneError); Assert.NotNull(c.Adverts);
        }
    }
}
