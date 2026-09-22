using System;
using System.Linq;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class PhoneBillTests
    {
        private static PhoneBillQuote Quote() => new PhoneBillQuote { Minutes = 100, MinutesLong = 20, Connects = 3,
            ConnectsLong = 2, Base = 128, ConnectionRate = .48f, MinuteRate = .07f, LongMinuteRate = .13f };

        [Fact]
        public void NativePhoneTariffsIncludeBaseAndBothKindsOfCall()
        {
            var q = Quote();
            Assert.True(q.Valid); Assert.Equal(140, q.Total);
            q.Minutes = q.MinutesLong = q.Connects = q.ConnectsLong = 0;
            Assert.Equal(128, q.Total);
            q.Minutes = 20000000; Assert.Equal(999999, q.Total);
            q.Base = 256; q.Minutes = 0; Assert.Equal(256, q.Total);
        }

        [Theory]
        [InlineData(2)] [InlineData(3)]
        public void PhoneWireCarriesAuthoritativeUsageAndRatesSeparatelyFromDebt(byte meter)
        {
            var state = new UtilityBillState { Meter = meter, UnpaidBills = 500, Flags = 2, Revision = 42, Phone = Quote() };
            var wire = PacketCodec.Encode(state); Assert.Equal(44, wire.Length);
            var copy = Assert.IsType<UtilityBillState>(PacketCodec.Decode(wire));
            Assert.True(UtilityBillPolicy.Same(state, copy)); Assert.Equal(140, copy.PaymentTotal);
            Assert.Equal(500, copy.UnpaidBills); Assert.True(copy.BillVisible); Assert.False(copy.PowerOn);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(43).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[] { 0 }).ToArray()));
            state.Phone!.Base = 100; Assert.Equal(128, copy.Phone!.Base);
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1)]
        public void EveryPhoneInputRejectsInvalidValuesInBothDirections(float invalid)
        {
            string[] fields = { "Minutes", "MinutesLong", "Connects", "ConnectsLong", "Base", "ConnectionRate", "MinuteRate", "LongMinuteRate" };
            for (int i = 0; i < fields.Length; i++)
            {
                var state = new UtilityBillState { Meter = 2, Phone = Quote() };
                var bytes = PacketCodec.Encode(state);
                typeof(PhoneBillQuote).GetField(fields[i])!.SetValue(state.Phone, invalid);
                Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
                Array.Copy(BitConverter.GetBytes(invalid), 0, bytes, 12 + i * 4, 4);
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            }
        }

        [Fact]
        public void MissingAndMisplacedQuotesAndOverflowAreRejected()
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new UtilityBillState { Meter = 2 }));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new UtilityBillState { Meter = 0, Phone = Quote() }));
            var q = Quote(); q.Minutes = float.MaxValue; q.MinuteRate = 100;
            Assert.False(q.Valid);
        }

        [Theory]
        [InlineData(2)] [InlineData(3)]
        public void TwoPlayersAndRepeatedPhoneReceiptsDebitTheInvoiceOnce(byte meter)
        {
            var ledger = new UtilityPaymentLedger(); ledger.Observe(Quote().Total, true);
            var first = new UtilityPaymentIntent { PlayerId = 1, Meter = meter, Sequence = 1, Revision = ledger.Revision };
            var competing = new UtilityPaymentIntent { PlayerId = 2, Meter = meter, Sequence = 1, Revision = ledger.Revision };
            Assert.IsType<UtilityPaymentIntent>(PacketCodec.Decode(PacketCodec.Encode(first)));
            var receipt = ledger.Apply(first, 1000, true, out float cash);
            Assert.Equal(UtilityPaymentResult.Accepted, receipt.Result); Assert.Equal(860, cash); Assert.Equal(140, receipt.Paid);
            Assert.IsType<UtilityPaymentResult>(PacketCodec.Decode(PacketCodec.Encode(receipt)));
            Assert.Equal(UtilityPaymentResult.Accepted, ledger.Apply(first, cash, true, out cash).Result); Assert.Equal(860, cash);
            Assert.Equal(UtilityPaymentResult.Changed, ledger.Apply(competing, cash, true, out cash).Result); Assert.Equal(860, cash);
        }

        [Fact]
        public void ChangedCallDetailsInvalidateAnOlderQuoteEvenWhenTotalIsEqual()
        {
            var a = Quote(); var b = Quote(); b.Connects++; b.ConnectsLong--;
            Assert.Equal(a.Total, b.Total); Assert.False(a.Same(b));
            var ledger = new UtilityPaymentLedger(); ledger.Observe(a.Total, true);
            var old = new UtilityPaymentIntent { PlayerId = 1, Meter = 2, Revision = ledger.Revision };
            ledger.Observe(b.Total, true, true);
            Assert.Equal(UtilityPaymentResult.Changed, ledger.Apply(old, 1000, true, out var cash).Result); Assert.Equal(1000, cash);
            var state = new UtilityBillState { Meter = 2, Phone = a };
            var copy = UtilityBillPolicy.Copy(state); a.Connects++;
            Assert.False(UtilityBillPolicy.Same(state, copy)); Assert.Equal(3, copy.Phone!.Connects);
        }

        [Fact]
        public void BrokenPhoneCatalogIsContainedToPhonePayments()
        {
            var parsed = SyncCatalogJson.Parse("{\"phonePayments\":{}}");
            Assert.Null(parsed.PhonePayments); Assert.NotNull(parsed.PhonePaymentsError); Assert.Null(parsed.UtilityPaymentsError);
        }
    }
}
