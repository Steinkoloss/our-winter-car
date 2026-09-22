using System;
using System.Linq;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class UtilityPaymentTests
    {
        private static UtilityPaymentIntent Request(UtilityPaymentLedger ledger, byte player = 1, ushort sequence = 1)
            => new UtilityPaymentIntent { PlayerId = player, Sequence = sequence, Revision = ledger.Revision };

        [Fact]
        public void OneInvoiceCanBePaidOnlyOnceAcrossPlayersAndRetries()
        {
            var ledger = new UtilityPaymentLedger(); ledger.Observe(123.25f, true);
            var a = Request(ledger); var b = Request(ledger, 2);
            var paid = ledger.Apply(a, 1000, true, out float cash);
            Assert.Equal(UtilityPaymentResult.Accepted, paid.Result); Assert.Equal(876.75f, cash);
            Assert.Equal(123.25f, paid.Paid);
            paid.Paid = 999;
            Assert.True(ledger.TryReceipt(a, out var copy)); Assert.Equal(123.25f, copy.Paid);
            Assert.Equal(UtilityPaymentResult.Accepted, ledger.Apply(a, cash, true, out float repeated).Result);
            Assert.Equal(cash, repeated);
            Assert.Equal(UtilityPaymentResult.Changed, ledger.Apply(b, cash, true, out repeated).Result);
            Assert.Equal(cash, repeated);
            Assert.Equal(UtilityPaymentResult.Unavailable, ledger.Apply(Request(ledger, 2, 2), cash, true, out repeated).Result);
            Assert.Equal(cash, repeated);
        }

        [Theory]
        [InlineData(100, true, 4)] [InlineData(1000, false, 3)]
        [InlineData(float.NaN, true, 4)] [InlineData(float.PositiveInfinity, true, 4)]
        [InlineData(1e30f, true, 4)]
        public void DeclinedPaymentsLeaveTheInvoiceAndCashUntouched(float cash, bool nearby, byte code)
        {
            var ledger = new UtilityPaymentLedger(); ledger.Observe(123.25f, true);
            uint revision = ledger.Revision;
            Assert.Equal(code, ledger.Apply(Request(ledger), cash, nearby, out float next).Result);
            Assert.Equal(cash, next); Assert.Equal(revision, ledger.Revision);
            Assert.Equal(UtilityPaymentResult.Accepted, ledger.Apply(Request(ledger, 1, 2), 123.25f, true, out next).Result);
            Assert.Equal(0, next);
        }

        [Fact]
        public void AChangedInvoiceMustBeReviewedAndSequenceReuseCannotAlterAReceipt()
        {
            var ledger = new UtilityPaymentLedger(); ledger.Observe(123.25f, true);
            var old = Request(ledger); ledger.Observe(200, true);
            Assert.Equal(UtilityPaymentResult.Changed, ledger.Apply(old, 1000, true, out _).Result);
            old.Revision = ledger.Revision;
            Assert.Equal(UtilityPaymentResult.Stale, ledger.Apply(old, 1000, true, out _).Result);
            Assert.Equal(UtilityPaymentResult.Accepted, ledger.Apply(Request(ledger, 1, 2), 1000, true, out float cash).Result);
            Assert.Equal(800, cash);
        }

        [Fact]
        public void OldRequestsCannotPayTheNextInvoiceAndSequenceWrapIsSupported()
        {
            var ledger = new UtilityPaymentLedger(); ledger.Observe(10, true);
            var first = Request(ledger, 1, ushort.MaxValue);
            ledger.Apply(first, 100, true, out float cash); ledger.Observe(20, true);
            Assert.Equal(UtilityPaymentResult.Accepted, ledger.Apply(first, cash, true, out float repeated).Result);
            Assert.Equal(cash, repeated);
            Assert.Equal(UtilityPaymentResult.Accepted, ledger.Apply(Request(ledger, 1, 0), cash, true, out cash).Result);
            Assert.Equal(70, cash);
            Assert.Equal(UtilityPaymentResult.Stale, ledger.Apply(first, cash, true, out _).Result);
            ledger.ForgetPlayer(1); ledger.Observe(5, true);
            Assert.Equal(UtilityPaymentResult.Accepted, ledger.Apply(Request(ledger), cash, true, out cash).Result);
            Assert.Equal(65, cash);
        }

        [Theory]
        [InlineData(0, true)] [InlineData(100, false)]
        public void MissingOrEmptyEnvelopeCannotCharge(float debt, bool visible)
        {
            var ledger = new UtilityPaymentLedger(); ledger.Observe(debt, visible);
            Assert.Equal(UtilityPaymentResult.Unavailable, ledger.Apply(Request(ledger), 1000, true, out float cash).Result);
            Assert.Equal(1000, cash);
        }

        [Fact]
        public void PaymentWireIsExactAndRejectsUnknownMetersAndInvalidReceipts()
        {
            var request = new UtilityPaymentIntent { PlayerId = 1, Meter = 1, Sequence = 123, Revision = 0x12345678 };
            var bytes = PacketCodec.Encode(request); Assert.Equal(10, bytes.Length); Assert.Equal(211, BitConverter.ToUInt16(bytes, 0));
            var copy = Assert.IsType<UtilityPaymentIntent>(PacketCodec.Decode(bytes));
            Assert.Equal(request.Revision, copy.Revision); Assert.Equal(request.Sequence, copy.Sequence); Assert.Equal(request.Meter, copy.Meter);
            bytes[3] = 4; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            request.Meter = 4; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            var receipt = new UtilityPaymentResult { PlayerId = 1, Meter = 0, Sequence = 123, Paid = 123.25f };
            bytes = PacketCodec.Encode(receipt); Assert.Equal(11, bytes.Length); Assert.Equal(212, BitConverter.ToUInt16(bytes, 0));
            Assert.Equal(123.25f, Assert.IsType<UtilityPaymentResult>(PacketCodec.Decode(bytes)).Paid);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(10).ToArray()));
            bytes[6] = UtilityPaymentResult.Funds; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            receipt.Paid = float.NaN; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(receipt));
        }

        [Fact]
        public void PaymentMessagesHaveAuthenticatedDirectionsAndOrderedDelivery()
        {
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.UtilityPaymentIntent, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.UtilityPaymentIntent, true, false, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.UtilityPaymentIntent, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.UtilityPaymentResult, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.UtilityPaymentResult, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.UtilityPaymentResult, false, true, false, true));
            foreach (var id in new[] { MessageId.UtilityPaymentIntent, MessageId.UtilityPaymentResult })
            {
                Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
            }
        }

        [Fact]
        public void CatalogRejectsIncompletePaymentBindingsWithoutDiscardingOtherSubsystems()
        {
            var data = SyncCatalogJson.Parse("{\"utilityPayments\":{}}");
            Assert.Null(data.UtilityPayments); Assert.NotNull(data.UtilityPaymentsError);
        }
    }
}
