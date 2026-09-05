using System;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class DebtPaymentTests
    {
        private static DebtPaymentLedger Ledger(float debt = 1050, bool available = true)
        {
            var ledger = new DebtPaymentLedger();
            ledger.Observe(debt, 1.29f, 59, 864, available);
            return ledger;
        }
        private static DebtPaymentIntent Request(uint revision = 1, ushort sequence = 1, byte player = 1)
            => new DebtPaymentIntent { PlayerId = player, Sequence = sequence, Revision = revision };

        [Theory]
        [InlineData(1050, 2277.5f)]
        [InlineData(1575, 2954.75f)]
        [InlineData(0, 0)]
        public void QuoteUsesNativeRentSurchargeAndBothFees(float debt, float expected)
        {
            Assert.True(DebtPaymentLedger.TryQuote(debt, 1.29f, 59, 864, out float total));
            Assert.Equal(expected, total);
            var state = Ledger(debt).Snapshot()!;
            Assert.Equal(expected, state.Total);
            Assert.Equal(debt > 0, state.Available);
        }

        [Fact]
        public void FractionalDebtUsesThreeFloatOperationsInsteadOfRoundingThePrintedPrice()
        {
            Assert.True(DebtPaymentLedger.TryQuote(1050.1234f, 1.29f, 59, 864, out float total));
            float native = 1050.1234f * 1.29f;
            native += 59; native += 864;
            Assert.Equal(native, total);
            Assert.NotEqual((float)Math.Round(total, 1), total);
            var ledger = Ledger(1050.1234f);
            var result = ledger.Apply(Request(), 3000.125f, true, out float cash);
            Assert.Equal(DebtPaymentResult.Accepted, result.Result);
            Assert.Equal(native, result.Paid);
            Assert.InRange(Math.Abs(3000.125 - cash - result.Paid), 0, .005);
        }

        [Fact]
        public void ConcurrentPlayersCanPayAnIssuedDebtOnlyOnce()
        {
            var ledger = Ledger();
            var first = ledger.Apply(Request(), 5000, true, out float cash);
            var second = ledger.Apply(Request(player: 2), cash, true, out cash);
            Assert.Equal(DebtPaymentResult.Accepted, first.Result);
            Assert.Equal(DebtPaymentResult.Changed, second.Result);
            Assert.Equal(2722.5f, cash);
            Assert.Equal(0, ledger.Snapshot()!.Debt);
            Assert.Equal(0, ledger.Snapshot()!.Total);
            Assert.False(ledger.Snapshot()!.Available);
            Assert.Equal(2u, ledger.Snapshot()!.Revision);
        }

        [Fact]
        public void CachedAcceptanceRetainsThePaidAmountWithoutDebitingAgain()
        {
            var ledger = Ledger();
            var request = Request();
            var paid = ledger.Apply(request, 5000, true, out float cash);
            var duplicate = ledger.Apply(request, cash, false, out cash);
            Assert.Equal(PacketCodec.Encode(paid), PacketCodec.Encode(duplicate));
            Assert.Equal(2722.5f, cash);
            duplicate.Paid = 99999;
            Assert.Equal(2277.5f, ledger.Apply(request, cash, true, out _).Paid);
            Assert.Equal(DebtPaymentResult.Stale, ledger.Apply(Request(2), cash, true, out _).Result);
        }

        [Fact]
        public void UnissuedLettersCannotBePaidAndInsufficientCashNeverClearsTheDebt()
        {
            var absent = Ledger(available: false);
            Assert.Equal(DebtPaymentResult.Unavailable, absent.Apply(Request(), 5000, true, out _).Result);
            var ledger = Ledger();
            Assert.Equal(DebtPaymentResult.Funds, ledger.Apply(Request(), 2277.49f, true, out float cash).Result);
            Assert.Equal(2277.49f, cash);
            Assert.Equal(1050, ledger.Snapshot()!.Debt);
            Assert.True(ledger.Snapshot()!.Available);
            Assert.Equal(DebtPaymentResult.Accepted, ledger.Apply(Request(sequence: 2), 2277.5f, true, out cash).Result);
            Assert.Equal(0, cash);
        }

        [Fact]
        public void RejectionsAreTerminalWhenCashOrDistanceLaterChanges()
        {
            var ledger = Ledger();
            Assert.Equal(DebtPaymentResult.Distant, ledger.Apply(Request(), 5000, false, out _).Result);
            Assert.Equal(DebtPaymentResult.Distant, ledger.Apply(Request(), 5000, true, out _).Result);
            Assert.Equal(DebtPaymentResult.Funds, ledger.Apply(Request(sequence: 2), 0, true, out _).Result);
            Assert.Equal(DebtPaymentResult.Funds, ledger.Apply(Request(sequence: 2), 5000, true, out float cash).Result);
            Assert.Equal(5000, cash);
            Assert.True(ledger.Snapshot()!.Available);
        }

        [Fact]
        public void ChangingDebtOrFeesRequiresAQuoteThePlayerHasActuallySeen()
        {
            var ledger = Ledger();
            var shown = Request(ledger.Snapshot()!.Revision);
            Assert.True(ledger.Observe(1575, 1.29f, 59, 864, true));
            Assert.Equal(DebtPaymentResult.Changed, ledger.Apply(shown, 5000, true, out float cash).Result);
            Assert.Equal(5000, cash);
            var revised = Request(ledger.Snapshot()!.Revision, 2);
            // Same numerical total with different fee composition is still a new quote.
            Assert.True(ledger.Observe(1575, 1.29f, 60, 863, true));
            Assert.Equal(DebtPaymentResult.Changed, ledger.Apply(revised, 5000, true, out _).Result);
            Assert.Equal(1575, ledger.Snapshot()!.Debt);
        }

        [Fact]
        public void ANewDebtWithTheSameAmountCannotBeClearedByAnOldRequest()
        {
            var ledger = Ledger();
            ledger.Apply(Request(), 5000, true, out float cash);
            Assert.True(ledger.Observe(1050, 1.29f, 59, 864, true));
            Assert.Equal(3u, ledger.Snapshot()!.Revision);
            Assert.Equal(DebtPaymentResult.Changed, ledger.Apply(Request(sequence: 2), cash, true, out cash).Result);
            Assert.Equal(2722.5f, cash);
            Assert.Equal(1050, ledger.Snapshot()!.Debt);
            Assert.True(ledger.TryGetReceipt(Request(sequence: 2), out var result));
            Assert.Equal(DebtPaymentResult.Changed, result.Result);
        }

        [Fact]
        public void SequenceWrapAndReconnectDoNotReopenAPaidLetter()
        {
            var ledger = Ledger();
            Assert.Equal(DebtPaymentResult.Distant, ledger.Apply(Request(sequence: 65000), 5000, false, out _).Result);
            Assert.Equal(DebtPaymentResult.Stale, ledger.Apply(Request(sequence: 64999), 5000, true, out _).Result);
            Assert.Equal(DebtPaymentResult.Accepted, ledger.Apply(Request(sequence: 0), 5000, true, out float cash).Result);
            ledger.ForgetPlayer(1);
            Assert.Equal(DebtPaymentResult.Unavailable, ledger.Apply(Request(2), cash, true, out cash).Result);
            Assert.Equal(2722.5f, cash);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(-1)]
        [InlineData(1e20f)]
        public void InvalidOrImpreciseCashCannotEraseTheDebt(float cash)
        {
            var ledger = Ledger();
            Assert.Equal(DebtPaymentResult.Funds, ledger.Apply(Request(), cash, true, out _).Result);
            Assert.Equal(1050, ledger.Snapshot()!.Debt);
            Assert.Equal(1u, ledger.Snapshot()!.Revision);
        }

        [Theory]
        [InlineData(float.NaN, 1.29f, 59, 864)]
        [InlineData(-1, 1.29f, 59, 864)]
        [InlineData(1050, 0, 59, 864)]
        [InlineData(1050, 1.29f, -59, 864)]
        [InlineData(1050, 1.29f, 59, float.PositiveInfinity)]
        [InlineData(float.MaxValue, 2, 59, 864)]
        public void InvalidTermsNeverReplaceAValidQuote(float debt, float interest, float cost1, float cost2)
        {
            var ledger = Ledger();
            Assert.False(DebtPaymentLedger.TryQuote(debt, interest, cost1, cost2, out _));
            Assert.Throws<ArgumentException>(() => ledger.Observe(debt, interest, cost1, cost2, true));
            Assert.Equal(2277.5f, ledger.Snapshot()!.Total);
            Assert.Equal(1u, ledger.Snapshot()!.Revision);
        }

        [Fact]
        public void RepeatedObservationDoesNotInvalidateAQuoteAndSnapshotsAreCopies()
        {
            var ledger = Ledger();
            Assert.False(ledger.Observe(1050, 1.29f, 59, 864, true));
            ledger.Snapshot()!.Debt = 999;
            Assert.Equal(1050, ledger.Snapshot()!.Debt);
            Assert.True(ledger.Observe(1050, 1.29f, 59, 864, false));
            Assert.Equal(2u, ledger.Snapshot()!.Revision);
            ledger.Clear();
            Assert.Null(ledger.Snapshot());
            Assert.False(ledger.TryGetReceipt(Request(), out _));
        }

        [Fact]
        public void DebtMessagesUseReliableOrderedAndRejectInvalidDisplayAmounts()
        {
            var state = new DebtLetterState { Revision = uint.MaxValue, Debt = 1050.125f, Total = 2277.6611f, Available = true };
            var roundtrip = (DebtLetterState)PacketCodec.Decode(PacketCodec.Encode(state));
            Assert.Equal(PacketCodec.Encode(state), PacketCodec.Encode(roundtrip));
            Assert.True(DebtPaymentLedger.IsValid(roundtrip));
            state.Total = float.NaN; Assert.False(DebtPaymentLedger.IsValid(state));
            state.Total = 0; Assert.False(DebtPaymentLedger.IsValid(state));
            state.Debt = 0; Assert.False(DebtPaymentLedger.IsValid(state));
            state.Available = false; Assert.True(DebtPaymentLedger.IsValid(state));
            foreach (var id in new[] { MessageId.DebtLetterState, MessageId.DebtPaymentIntent, MessageId.DebtPaymentResult })
                foreach (var channel in Enum.GetValues<Channel>())
                    Assert.Equal(channel == Channel.ReliableOrdered, SessionMessagePolicy.IsChannelAllowed(id, channel));
        }
    }
}
