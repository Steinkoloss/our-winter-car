using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PokerTests
    {
        private static readonly int[] Payouts = { 0, 1, 2, 3, 5, 7, 10, 15, 30, 50 };
        private static PokerLedger Ledger(PokerState? state = null, Func<int, int>? random = null)
            => new PokerLedger(state ?? new PokerState { Credit = 10 }, Payouts, 900, random ?? (_ => 0));
        private static PokerIntent Request(byte action, ushort sequence = 1, uint round = 0, byte player = 1)
            => new PokerIntent { Action = action, Sequence = sequence, Round = round, PlayerId = player };
        private static PokerResult Apply(PokerLedger ledger, byte action, ushort sequence, float now = 0, byte player = 1)
            => ledger.Apply(Request(action, sequence, ledger.Snapshot().Round, player), 100, now, true, out _);
        private static ushort HoldAll(PokerLedger ledger, ushort sequence = 2)
        {
            for (byte a = PokerIntent.Hold1; a <= PokerIntent.Hold5; a++)
                Assert.Equal(PokerResult.Accepted, Apply(ledger, a, sequence++).Result);
            return sequence;
        }
        private static PokerLedger Offered(out ushort next, int bet = 1, Func<int, int>? random = null)
        {
            // Drawing the lowest remaining card produces A..5 of spades.
            var ledger = Ledger(new PokerState { Credit = 10, Bet = (byte)bet }, random);
            Apply(ledger, PokerIntent.Deal, 1);
            next = HoldAll(ledger);
            Apply(ledger, PokerIntent.Deal, next++);
            Assert.Equal(PokerState.WinOffer, ledger.Snapshot().Phase);
            return ledger;
        }

        [Theory]
        [InlineData(9, 1, 10, 11, 12, 13)]
        [InlineData(8, 1, 2, 3, 4, 5)]
        [InlineData(7, 1, 14, 27, 40, 5)]
        [InlineData(6, 2, 15, 28, 4, 17)]
        [InlineData(5, 1, 2, 4, 6, 8)]
        [InlineData(4, 1, 15, 29, 43, 5)]
        [InlineData(4, 1, 23, 37, 51, 13)]
        [InlineData(3, 5, 18, 31, 2, 9)]
        [InlineData(2, 2, 15, 9, 22, 5)]
        [InlineData(1, 1, 14, 3, 7, 9)]
        [InlineData(1, 11, 24, 3, 7, 9)]
        [InlineData(1, 12, 25, 3, 7, 9)]
        [InlineData(1, 13, 26, 3, 7, 9)]
        [InlineData(0, 10, 23, 3, 7, 9)]
        [InlineData(0, 12, 26, 1, 15, 3)]
        public void NativeHandCategoriesDoNotDependOnCardOrder(byte expected, params int[] values)
        {
            byte[] cards = values.Select(x => (byte)x).ToArray();
            foreach (var hand in Permutations(cards)) Assert.Equal(expected, PokerLedger.Evaluate(hand));
        }
        private static IEnumerable<byte[]> Permutations(byte[] cards, int at = 0)
        {
            if (at == cards.Length) { yield return cards; yield break; }
            for (int i = at; i < cards.Length; i++)
            {
                (cards[i], cards[at]) = (cards[at], cards[i]);
                foreach (var hand in Permutations(cards, at + 1)) yield return hand;
                (cards[i], cards[at]) = (cards[at], cards[i]);
            }
        }

        [Fact]
        public void EntireDeckHasTheExpectedDisjointFiveCardCategoryCounts()
        {
            int[] counts = new int[10];
            var hand = new byte[5];
            for (byte a = 1; a <= 48; a++)
                for (byte b = (byte)(a + 1); b <= 49; b++)
                    for (byte c = (byte)(b + 1); c <= 50; c++)
                        for (byte d = (byte)(c + 1); d <= 51; d++)
                            for (byte e = (byte)(d + 1); e <= 52; e++)
                            {
                                hand[0] = a; hand[1] = b; hand[2] = c; hand[3] = d; hand[4] = e;
                                counts[PokerLedger.Evaluate(hand)]++;
                            }
            Assert.Equal(new[] { 2062860, 337920, 123552, 54912, 10200, 5108, 3744, 624, 36, 4 }, counts);
            Assert.Equal(2598960, counts.Sum());
        }

        [Theory]
        [InlineData(3, 2, 0, 0)]
        [InlineData(7, 4, 2, 4)]
        [InlineData(0, 9, 0, 4)]
        public void DealsSpendCreditFirstAndCombineBothBalances(int credit, int wins, int remainingCredit, int remainingWins)
        {
            var ledger = Ledger(new PokerState { Bet = 5, Credit = credit, Winnings = wins });
            Assert.Equal(PokerResult.Accepted, Apply(ledger, PokerIntent.Deal, 1).Result);
            Assert.Equal(remainingCredit, ledger.Snapshot().Credit);
            Assert.Equal(remainingWins, ledger.Snapshot().Winnings);
            Assert.Equal(1u, ledger.Snapshot().Round);
        }

        [Fact]
        public void BetsWrapToOneWhenTheNextBetIsUnaffordable()
        {
            var ledger = Ledger(new PokerState { Credit = 3 });
            foreach (var expected in new byte[] { 2, 3, 1 })
            {
                Apply(ledger, PokerIntent.Bet, (ushort)(ledger.Snapshot().Revision + 1));
                Assert.Equal(expected, ledger.Snapshot().Bet);
            }
            Assert.Equal(PokerResult.Funds, Apply(Ledger(new PokerState()), PokerIntent.Bet, 1).Result);
        }

        [Theory]
        [InlineData(5, 0, 5.99f, PokerResult.Funds, 0)]
        [InlineData(5, 499, 6f, PokerResult.Accepted, 504)]
        [InlineData(1, 500, 100f, PokerResult.Funds, 500)]
        public void InsertionUsesVanillaFloorAndPreInsertionLimit(byte bet, int credit, float cash, byte expected, int finalCredit)
        {
            var ledger = Ledger(new PokerState { Bet = bet, Credit = credit });
            var result = ledger.Apply(Request(PokerIntent.Insert), cash, 0, true, out float next);
            Assert.Equal(expected, result.Result);
            Assert.Equal(finalCredit, ledger.Snapshot().Credit);
            Assert.Equal(expected == PokerResult.Accepted ? cash - bet : cash, next);
        }

        [Fact]
        public void HoldsSkipReplacementAndDiscardsCannotReturnInTheSecondDraw()
        {
            int draws = 0;
            var ledger = Ledger(random: _ => { draws++; return 0; });
            Apply(ledger, PokerIntent.Deal, 1);
            Apply(ledger, PokerIntent.Hold1, 2);
            Apply(ledger, PokerIntent.Hold5, 3);
            Apply(ledger, PokerIntent.Deal, 4);
            Assert.Equal(new byte[] { 1, 6, 7, 8, 5 }, ledger.Snapshot().Cards);
            Assert.Equal(8, draws);
            Assert.Equal(0, ledger.Snapshot().HoldMask);
            ledger.Snapshot().Cards[0] = 52;
            Assert.Equal(1, ledger.Snapshot().Cards[0]);
        }

        [Theory]
        [InlineData(PokerIntent.TakeWin)]
        [InlineData(PokerIntent.Deal)]
        public void AcceptingAWinOnlyCollectsItAndCashoutNeedsAnotherPress(byte collect)
        {
            var ledger = Offered(out ushort seq);
            Assert.Equal(30, ledger.Snapshot().PendingWin);
            var accepted = Apply(ledger, collect, seq++);
            Assert.Equal(0, accepted.CashDelta);
            Assert.Equal(1u, ledger.Snapshot().Round);
            Assert.Equal(PokerState.Ready, ledger.Snapshot().Phase);
            Assert.Equal(30, ledger.Snapshot().Winnings);
            var result = ledger.Apply(Request(PokerIntent.TakeWin, seq, 1), 100.25f, 0, true, out float cash);
            Assert.Equal(39, result.CashDelta);
            Assert.Equal(139.25f, cash);
            Assert.Equal(0, ledger.Snapshot().Credit + ledger.Snapshot().Winnings);
        }

        [Theory]
        [InlineData(1, PokerIntent.Low, true)]
        [InlineData(6, PokerIntent.Low, true)]
        [InlineData(7, PokerIntent.Low, false)]
        [InlineData(7, PokerIntent.High, false)]
        [InlineData(8, PokerIntent.High, true)]
        [InlineData(13, PokerIntent.High, true)]
        public void HiddenDoublingCardUsesTheNativeSevenLosesRule(int rank, byte guess, bool wins)
        {
            int draws = 0;
            var ledger = Offered(out ushort seq, random: _ => ++draws <= 5 ? 0 : rank - 1);
            Apply(ledger, PokerIntent.Double, seq++);
            var visible = (PokerState)PacketCodec.Decode(PacketCodec.Encode(ledger.Snapshot()));
            Assert.Equal(PokerState.Guessing, visible.Phase);
            Assert.Equal(0, visible.DoubleCard);
            Assert.Equal(PokerResult.Invalid, Apply(ledger, PokerIntent.TakeWin, seq++).Result);
            Apply(ledger, guess, seq++);
            Assert.Equal(rank, PokerLedger.Rank(ledger.Snapshot().DoubleCard));
            Assert.Equal(wins ? 60 : 0, ledger.Snapshot().PendingWin);
            Assert.Equal(wins ? PokerState.WinOffer : PokerState.Ready, ledger.Snapshot().Phase);
        }

        [Fact]
        public void DoublingUsesASeparateDeckAndAutoCollectsAtFiveHundred()
        {
            var ledger = Offered(out ushort seq, bet: 5);
            Apply(ledger, PokerIntent.Double, seq++);
            Apply(ledger, PokerIntent.Low, seq++);
            Assert.Equal(1, ledger.Snapshot().DoubleCard); // Already in the main hand, valid in the separate deck.
            Assert.Equal(300, ledger.Snapshot().PendingWin);
            Apply(ledger, PokerIntent.Double, seq++);
            Apply(ledger, PokerIntent.Low, seq++);
            Assert.Equal(2, ledger.Snapshot().DoubleCard);
            Assert.Equal(600, ledger.Snapshot().Winnings);
            Assert.Equal(0, ledger.Snapshot().PendingWin);
            Assert.Equal(PokerState.Ready, ledger.Snapshot().Phase);
        }

        [Fact]
        public void ReceiptsPreservePayoutsAndAchievementsWithoutReapplyingCash()
        {
            var ledger = Ledger(new PokerState { Credit = 5, Winnings = 895 });
            var request = Request(PokerIntent.TakeWin, 65000);
            var first = ledger.Apply(request, 10.25f, 0, true, out float cash);
            var repeated = ledger.Apply(request, cash, 1, false, out cash);
            Assert.Equal(910.25f, cash);
            Assert.Equal(900, repeated.CashDelta);
            Assert.Equal(PokerResult.CashoutAchievement, repeated.Achievements);
            Assert.Equal(PacketCodec.Encode(first), PacketCodec.Encode(repeated));
            Assert.Equal(PokerResult.Stale, ledger.Apply(Request(PokerIntent.Insert, 65000), cash, 2, true, out _).Result);
            Assert.Equal(PokerResult.Stale, ledger.Apply(Request(PokerIntent.Insert, 64999), cash, 2, true, out _).Result);
            Assert.Equal(PokerResult.Accepted, ledger.Apply(Request(PokerIntent.Insert, 0), cash, 2, true, out cash).Result);
            Assert.Equal(909.25f, cash);
        }

        [Fact]
        public void RoyalFlushAwardsOnlyTheConfirmedRedrawRequester()
        {
            var draws = new Queue<int>(new[] { 0, 8, 8, 8, 8 });
            var ledger = Ledger(random: _ => draws.Dequeue());
            Apply(ledger, PokerIntent.Deal, 1);
            ushort seq = HoldAll(ledger);
            var request = Request(PokerIntent.Deal, seq, 1);
            var result = ledger.Apply(request, 100, 0, true, out _);
            Assert.Equal(9, ledger.Snapshot().Hand);
            Assert.Equal(50, ledger.Snapshot().PendingWin);
            Assert.Equal(PokerResult.RoyalAchievement, result.Achievements);
            Assert.Equal(0, result.CashDelta);
            Assert.Equal(PacketCodec.Encode(result), PacketCodec.Encode(ledger.Apply(request, 100, 1, true, out _)));
            Assert.Empty(draws);
        }

        [Fact]
        public void TeardownDoesNotDrawNewCardsForAnUnsubmittedRedraw()
        {
            int draws = 0;
            var ledger = Ledger(random: _ => ++draws > 5 ? throw new InvalidOperationException() : 0);
            Apply(ledger, PokerIntent.Deal, 1);
            Apply(ledger, PokerIntent.Hold1, 2);
            Assert.True(ledger.FinishSession(0, out float cash));
            Assert.Equal(39, cash);
            Assert.Equal(5, draws);
        }

        [Fact]
        public void RejectionsAreTerminalAndWrongRoundsCannotStartANewHand()
        {
            var ledger = Ledger(new PokerState());
            var request = Request(PokerIntent.Insert);
            Assert.Equal(PokerResult.Distant, ledger.Apply(request, 100, 0, false, out _).Result);
            Assert.Equal(PokerResult.Distant, ledger.Apply(request, 100, 1, true, out _).Result);
            Assert.Equal(PokerResult.Funds, ledger.Apply(Request(PokerIntent.Insert, 2), 0, 2, true, out _).Result);
            Assert.Equal(PokerResult.Funds, ledger.Apply(Request(PokerIntent.Insert, 2), 100, 3, true, out _).Result);
            Apply(ledger, PokerIntent.Insert, 3);
            Apply(ledger, PokerIntent.Deal, 4);
            Assert.Equal(PokerResult.Invalid, ledger.Apply(Request(PokerIntent.Hold1, 5), 0, 4, true, out _).Result);
            Assert.Equal(0, ledger.Snapshot().HoldMask);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AbandoningADoubleReleasesTheControlsButPreservesTheSecretCard(bool disconnect)
        {
            int draws = 0;
            var ledger = Offered(out ushort seq, random: _ => { draws++; return 0; });
            Apply(ledger, PokerIntent.Double, seq++);
            Assert.Equal(PokerResult.Busy, Apply(ledger, PokerIntent.Low, 1, 299, 2).Result);
            if (disconnect) ledger.ForgetPlayer(1); else Assert.True(ledger.Advance(300));
            Assert.Equal(PokerState.NoPlayer, ledger.Snapshot().PlayerId);
            Apply(ledger, PokerIntent.Low, 2, 301, 2);
            Assert.Equal(6, draws);
            Assert.Equal(1, ledger.Snapshot().DoubleCard);
            Assert.Equal(60, ledger.Snapshot().PendingWin);
        }

        [Theory]
        [InlineData(PokerState.Ready, 10)]
        [InlineData(PokerState.Holding, 39)]
        [InlineData(PokerState.WinOffer, 39)]
        [InlineData(PokerState.Guessing, 9)]
        public void SessionEndSettlesTheCurrentHandAndReturnsBalancesOnlyOnce(byte phase, int refund)
        {
            var ledger = Ledger();
            ushort seq = 1;
            if (phase != PokerState.Ready)
            {
                Apply(ledger, PokerIntent.Deal, seq++);
                seq = HoldAll(ledger, seq);
                if (phase != PokerState.Holding) Apply(ledger, PokerIntent.Deal, seq++);
                if (phase == PokerState.Guessing) Apply(ledger, PokerIntent.Double, seq++);
            }
            Assert.True(ledger.FinishSession(100, out float cash));
            Assert.Equal(100 + refund, cash);
            Assert.True(ledger.FinishSession(cash, out cash));
            Assert.Equal(100 + refund, cash);
            Assert.True(PokerLedger.IsValid(ledger.Snapshot()));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(1e20f)]
        public void InvalidCashCannotMintOrDiscardCredits(float cash)
        {
            var ledger = Ledger();
            Assert.Equal(PokerResult.Funds, ledger.Apply(Request(PokerIntent.Insert), cash, 0, true, out _).Result);
            Assert.Equal(PokerResult.Funds, ledger.Apply(Request(PokerIntent.TakeWin, 2), cash, 0, true, out _).Result);
            Assert.False(ledger.FinishSession(cash, out _));
            Assert.Equal(10, ledger.Snapshot().Credit);
        }

        [Fact]
        public void RngFailureCannotPartiallyDebitOrReplaceCards()
        {
            int draws = 0;
            var ledger = Ledger(random: _ => ++draws == 3 || draws == 10 ? -1 : 0);
            Assert.Throws<InvalidOperationException>(() => Apply(ledger, PokerIntent.Deal, 1));
            Assert.Equal(10, ledger.Snapshot().Credit);
            Assert.Equal(0u, ledger.Snapshot().Round);
            Assert.False(ledger.TryGetReceipt(Request(PokerIntent.Deal), out _));
            Apply(ledger, PokerIntent.Deal, 1);
            var before = PacketCodec.Encode(ledger.Snapshot());
            Assert.Throws<InvalidOperationException>(() => Apply(ledger, PokerIntent.Deal, 2));
            Assert.Equal(before, PacketCodec.Encode(ledger.Snapshot()));
        }

        [Fact]
        public void LargeBalancesCannotBeClampedAwayByASubsequentWin()
        {
            var ledger = Ledger(new PokerState { Winnings = 9999 });
            Assert.Equal(PokerResult.Funds, Apply(ledger, PokerIntent.Deal, 1).Result);
            Assert.Equal(9999, ledger.Snapshot().Winnings);
            var invalid = new PokerState { Phase = PokerState.WinOffer, Winnings = 9999, PendingWin = 1,
                Cards = new byte[] { 1, 2, 3, 4, 5 } };
            Assert.False(PokerLedger.IsValid(invalid));
        }

        [Fact]
        public void InvalidHandsAndWireShapesAreRejected()
        {
            foreach (var cards in new[] { new byte[5], new byte[] { 1, 1, 3, 4, 5 }, new byte[] { 1, 2, 3, 4, 53 }, new byte[4] })
                Assert.Throws<ArgumentException>(() => PokerLedger.Evaluate(cards));
            Assert.False(PokerLedger.IsValid(new PokerState { Bet = 0 }));
            Assert.False(PokerLedger.IsValid(new PokerState { HoldMask = 1 }));
            Assert.False(PokerLedger.IsValid(new PokerState { Phase = PokerState.Holding }));
            foreach (var id in new[] { MessageId.PokerIntent, MessageId.PokerState, MessageId.PokerResult })
                foreach (var channel in Enum.GetValues<Channel>())
                    Assert.Equal(channel == Channel.ReliableOrdered, SessionMessagePolicy.IsChannelAllowed(id, channel));
        }
    }
}
