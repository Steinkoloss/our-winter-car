using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VenttiLedgerTests
{
    private const uint Table = 0x21CAFE;
    private static int[] Values() => Enumerable.Range(0, 53).Select(i => i == 0 ? 0 : (i - 1) / 4 + 1).ToArray();
    private static VenttiRules Rules() => new(Values(), 50, 4000, 50, 7000, 6000, 0);
    private static VenttiLedger Ledger(float maximum = 200, float stake = 50, int stage = 0,
        VenttiWager wager = VenttiWager.Cash, float loss = 0, Func<int, int>? random = null) =>
        new(Table, Rules(), maximum, stage, loss, stake, wager, random ?? (_ => 0));

    private static VenttiRequest Request(VenttiLedger ledger, VenttiAction action, ushort sequence = 1, byte player = 1) =>
        new() { TableId = Table, Revision = ledger.Snapshot().Revision, PlayerId = player, Sequence = sequence, Action = action };

    private static VenttiReceipt Apply(VenttiLedger ledger, VenttiAction action, ushort sequence = 1,
        float cash = 1000.25f, float now = 0, byte player = 1) =>
        ledger.Apply(Request(ledger, action, sequence, player), cash, now, true, out _);

    private static Func<int, int> Deck(params byte[] prefix)
    {
        var remaining = Enumerable.Range(1, 52).Select(i => (byte)i).ToList();
        int at = 0;
        return count =>
        {
            Assert.Equal(count, remaining.Count);
            byte card = at < prefix.Length ? prefix[at++] : remaining[0];
            int index = remaining.IndexOf(card);
            Assert.True(index >= 0, "Fixture repeats a card.");
            remaining.RemoveAt(index);
            return index;
        };
    }

    [Fact]
    public void NativeCardsAreOneThroughThirteenWithoutBlackjackAceOrFaceCardConversion()
    {
        var rules = Rules();
        for (byte card = 1; card <= 52; card++) Assert.Equal((card - 1) / 4 + 1, rules.CardValue(card));
        Assert.Equal(1, rules.CardValue(1));
        Assert.Equal(11, rules.CardValue(41));
        Assert.Equal(12, rules.CardValue(45));
        Assert.Equal(13, rules.CardValue(49));
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.CardValue(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.CardValue(53));
    }

    [Fact]
    public void IncreasingAndDecreasingMoveRealEscrowAndPreserveFractionalCash()
    {
        var ledger = Ledger(stake: 0);
        var up = ledger.Apply(Request(ledger, VenttiAction.Increase), 75.25f, 0, true, out float cash);
        Assert.Equal(VenttiStatus.Accepted, up.Status);
        Assert.Equal(-50f, up.CashDelta);
        Assert.Equal(25.25f, cash);
        Assert.Equal(50f, ledger.Snapshot().Stake);
        var down = ledger.Apply(Request(ledger, VenttiAction.Decrease, 2), cash, 1, true, out cash);
        Assert.Equal(VenttiStatus.Accepted, down.Status);
        Assert.Equal(75.25f, cash);
        Assert.Equal(0f, ledger.Snapshot().Stake);
    }

    [Theory]
    [InlineData(200, 1000.25f, 200, 800.25f)]
    [InlineData(200, 123.5f, 100, 23.5f)]
    [InlineData(175, 1000.25f, 200, 800.25f)]
    [InlineData(200, 50, 50, 0)]
    public void RightClickAtZeroWrapsToTheAffordableNativeMaximum(float maximum, float cash, float stake, float remaining)
    {
        var ledger = Ledger(maximum, 0);
        var result = ledger.Apply(Request(ledger, VenttiAction.Decrease), cash, 0, true, out float next);
        Assert.Equal(VenttiStatus.Accepted, result.Status);
        Assert.Equal(stake, ledger.Snapshot().Stake);
        Assert.Equal(remaining, next);
    }

    [Fact]
    public void IncreasingChecksTheCapBeforeAddingAndCannotSpendMissingCash()
    {
        var ledger = Ledger(maximum: 175, stake: 150);
        Assert.Equal(VenttiStatus.Accepted, Apply(ledger, VenttiAction.Increase).Status);
        Assert.Equal(200f, ledger.Snapshot().Stake);
        Assert.Equal(VenttiStatus.Funds, Apply(ledger, VenttiAction.Increase, 2).Status);
        ledger = Ledger(stake: 0);
        Assert.Equal(VenttiStatus.Funds, Apply(ledger, VenttiAction.Increase, cash: 49.99f).Status);
        Assert.Equal(0f, ledger.Snapshot().Stake);
    }

    [Theory]
    [InlineData(0, VenttiWager.Car)]
    [InlineData(1, VenttiWager.House)]
    public void PropertyConversionNeedsStrictlyMoreThanFourThousandAndRefundsItsEscrow(int stage, VenttiWager wager)
    {
        var ledger = Ledger(maximum: 5000, stake: 4000, stage: stage);
        ledger.Apply(Request(ledger, VenttiAction.Increase), 200.25f, 0, true, out float cash);
        Assert.Equal(4050f, ledger.Snapshot().Stake);
        Assert.Equal(VenttiWager.Cash, ledger.Snapshot().Wager);
        var request = Request(ledger, VenttiAction.Increase, 2);
        var result = ledger.Apply(request, cash, 0, true, out cash);
        Assert.Equal(VenttiStatus.Accepted, result.Status);
        Assert.Equal(4200.25f, cash);
        Assert.Equal(4050f, result.CashDelta);
        Assert.Equal(wager, ledger.Snapshot().Wager);
        Assert.Equal(0, ledger.Snapshot().Stake);
        ledger.Apply(request, cash, 1, true, out float repeated);
        Assert.Equal(cash, repeated);
    }

    [Fact]
    public void ATransferredPropertyCannotBeWageredAgainAfterTheSecondStage()
    {
        var ledger = Ledger(maximum: 5000, stake: 4050, stage: 2);
        Assert.Equal(VenttiStatus.Accepted, Apply(ledger, VenttiAction.Increase).Status);
        Assert.Equal(VenttiWager.Cash, ledger.Snapshot().Wager);
        Assert.Equal(4100f, ledger.Snapshot().Stake);
    }

    [Theory]
    [InlineData(VenttiAction.Increase)]
    [InlineData(VenttiAction.Decrease)]
    public void APropertyWagerCanBeCancelledWithoutChargingCashThatDoesNotExist(VenttiAction action)
    {
        var ledger = Ledger(stake: 0, wager: VenttiWager.Car);
        Assert.Equal(VenttiStatus.Accepted, Apply(ledger, action, cash: 0).Status);
        Assert.Equal(VenttiWager.Cash, ledger.Snapshot().Wager);
        Assert.Equal(0, ledger.Snapshot().Stake);
    }

    [Fact]
    public void NonstandardAdoptedStakeRefundsOnlyWhatWasActuallyPaid()
    {
        var ledger = Ledger(stake: 12.25f);
        ledger.Apply(Request(ledger, VenttiAction.Decrease), 100, 0, true, out float cash);
        Assert.Equal(112.25f, cash);
        Assert.Equal(0, ledger.Snapshot().Stake);
        ledger = Ledger(maximum: 5000, stake: 4123.5f);
        ledger.Apply(Request(ledger, VenttiAction.Increase), 100, 0, true, out cash);
        Assert.Equal(4223.5f, cash);
    }

    [Fact]
    public void DealRevealsTwoPlayerCardsAndOneHouseCardWithoutChargingTheStakeAgain()
    {
        var ledger = Ledger(random: Deck(37, 1, 21));
        var result = ledger.Apply(Request(ledger, VenttiAction.Hit), 100.25f, 0, true, out float cash);
        var state = ledger.Snapshot();
        Assert.Equal(VenttiStatus.Accepted, result.Status);
        Assert.Equal(100.25f, cash);
        Assert.Equal(1u, state.Round);
        Assert.Equal(new byte[] { 37, 21 }, state.PlayerCards);
        Assert.Equal(new byte[] { 1 }, state.HouseCards);
        Assert.Equal(16, state.PlayerTotal);
        Assert.Equal(1, state.HouseTotal);
        Assert.Equal(VenttiPhase.Playing, state.Phase);
    }

    [Fact]
    public void HittingTwentyOnePaysTwiceThePaidStakeAndIncreasesTheNextLimitOnce()
    {
        var ledger = Ledger(stake: 50.25f, random: Deck(37, 1, 21, 17));
        Apply(ledger, VenttiAction.Hit);
        var request = Request(ledger, VenttiAction.Hit, 2);
        var result = ledger.Apply(request, 200.25f, 1, true, out float cash);
        Assert.Equal(300.75f, cash);
        Assert.Equal(100.5f, result.CashDelta);
        Assert.Equal(VenttiTableState.Win, ledger.Snapshot().Outcome);
        Assert.Equal(250, ledger.Snapshot().BetMaximum);
        Assert.Equal(50.25f, ledger.Snapshot().OpponentLoss);
        ledger.Apply(request, cash, 2, true, out float repeated);
        Assert.Equal(cash, repeated);
        Assert.Equal(250, ledger.Snapshot().BetMaximum);
    }

    [Fact]
    public void BustLosesTheAlreadyPaidStakeWithoutASecondDebit()
    {
        var ledger = Ledger(random: Deck(49, 1, 45));
        var result = ledger.Apply(Request(ledger, VenttiAction.Hit), 100.25f, 0, true, out float cash);
        Assert.Equal(VenttiStatus.Accepted, result.Status);
        Assert.Equal(100.25f, cash);
        Assert.Equal(25, ledger.Snapshot().PlayerTotal);
        Assert.Equal(VenttiTableState.Lose, ledger.Snapshot().Outcome);
        Assert.Equal(-50, ledger.Snapshot().OpponentLoss);
        Assert.Equal(200, ledger.Snapshot().BetMaximum);
    }

    [Theory]
    [InlineData((byte)9, 21, VenttiTableState.Lose)]
    [InlineData((byte)17, 23, VenttiTableState.Win)]
    public void DealerHitsAnEighteenTieInsteadOfStandingOrPushing(byte thirdHouseCard, int total, byte outcome)
    {
        var ledger = Ledger(random: Deck(37, 33, 29, 34, thirdHouseCard));
        Apply(ledger, VenttiAction.Hit);
        Apply(ledger, VenttiAction.Stand, 2);
        var state = ledger.Snapshot();
        Assert.Equal(18, state.PlayerTotal);
        Assert.Equal(total, state.HouseTotal);
        Assert.Equal(3, state.HouseCards.Length);
        Assert.Equal(outcome, state.Outcome);
    }

    [Fact]
    public void DealerDrawsASecondCardEvenWhenTheFirstAlreadyBeatsThePlayer()
    {
        var ledger = Ledger(random: Deck(1, 49, 2, 5));
        Apply(ledger, VenttiAction.Hit);
        Apply(ledger, VenttiAction.Stand, 2);
        Assert.Equal(15, ledger.Snapshot().HouseTotal);
        Assert.Equal(2, ledger.Snapshot().HouseCards.Length);
    }

    [Theory]
    [InlineData(VenttiWager.Car, 0, true, VenttiTableState.WinCar, VenttiPhase.Resolved)]
    [InlineData(VenttiWager.Car, 0, false, VenttiTableState.LoseCar, VenttiPhase.Resolved)]
    [InlineData(VenttiWager.House, 1, true, VenttiTableState.WinHouse, VenttiPhase.Closed)]
    [InlineData(VenttiWager.House, 1, false, VenttiTableState.LoseHouse, VenttiPhase.Resolved)]
    public void PropertyResultsAdvanceOnceAndDoNotMasqueradeAsCashPayouts(VenttiWager wager, int stage,
        bool win, byte outcome, VenttiPhase phase)
    {
        var ledger = Ledger(stake: 0, stage: stage, wager: wager, random: Deck(49, 1, win ? (byte)29 : (byte)45));
        var request = Request(ledger, VenttiAction.Hit);
        ledger.Apply(request, 100.25f, 0, true, out float cash);
        Assert.Equal(100.25f, cash);
        var state = ledger.Snapshot();
        Assert.Equal(outcome, state.Outcome);
        Assert.Equal(stage + 1, state.PropertyStage);
        Assert.Equal(win ? 6000 : 0, state.OpponentLoss);
        Assert.Equal(phase, state.Phase);
        Assert.Equal(0, state.PendingCash);
        ledger.Apply(request, cash, 1, true, out _);
        Assert.Equal(stage + 1, ledger.Snapshot().PropertyStage);
    }

    [Fact]
    public void ReachingTheNativeOpponentLossLimitClosesTheTableAfterPaying()
    {
        var ledger = Ledger(loss: 6950, random: Deck(49, 1, 29));
        ledger.Apply(Request(ledger, VenttiAction.Hit), 100, 0, true, out float cash);
        Assert.Equal(200, cash);
        Assert.Equal(VenttiPhase.Closed, ledger.Snapshot().Phase);
        Assert.Equal(VenttiStatus.Finished, Apply(ledger, VenttiAction.NextHand, 2).Status);
    }

    [Fact]
    public void ResolvedHandRequiresAnExplicitResetBeforeAnotherStakeOrDraw()
    {
        var ledger = Ledger(random: Deck(49, 1, 29));
        Apply(ledger, VenttiAction.Hit);
        Assert.Equal(VenttiStatus.Invalid, Apply(ledger, VenttiAction.Hit, 2).Status);
        Assert.Equal(VenttiStatus.Accepted, Apply(ledger, VenttiAction.NextHand, 3).Status);
        var state = ledger.Snapshot();
        Assert.Equal(VenttiPhase.Betting, state.Phase);
        Assert.Equal(VenttiTableState.None, state.Outcome);
        Assert.Equal(0, state.Stake);
        Assert.Empty(state.PlayerCards);
        Assert.Empty(state.HouseCards);
        Assert.Equal(1u, state.Round);
        Assert.Equal(250, state.BetMaximum);
    }

    [Fact]
    public void LeasePreventsSimultaneousPlayersFromChangingTheSameHand()
    {
        var ledger = Ledger();
        Apply(ledger, VenttiAction.Increase);
        var before = ledger.Snapshot();
        Assert.Equal(VenttiStatus.Busy, Apply(ledger, VenttiAction.Hit, player: 2).Status);
        Assert.Equal(before.Revision, ledger.Snapshot().Revision);
        Assert.Equal(before.Stake, ledger.Snapshot().Stake);
        Assert.Empty(ledger.Snapshot().PlayerCards);
    }

    [Fact]
    public void TimeoutReleasesControlWithoutRefundingOrRerollingAnAbandonedHand()
    {
        var ledger = Ledger();
        Apply(ledger, VenttiAction.Increase);
        Assert.False(ledger.Advance(14.99f));
        Assert.True(ledger.Advance(15));
        Assert.Equal(100, ledger.Snapshot().Stake);
        Assert.Equal(VenttiStatus.Accepted, Apply(ledger, VenttiAction.Hit, now: 16, player: 2).Status);
        var state = ledger.Snapshot();
        Assert.False(ledger.Advance(315.99f));
        Assert.True(ledger.Advance(316));
        Assert.Equal(state.PlayerCards, ledger.Snapshot().PlayerCards);
        Assert.Equal(state.HouseCards, ledger.Snapshot().HouseCards);
        Assert.Equal(state.Round, ledger.Snapshot().Round);
    }

    [Fact]
    public void DisconnectAndSequenceResetCannotRedealTheSameHand()
    {
        var ledger = Ledger(random: Deck(37, 1, 21, 17));
        Apply(ledger, VenttiAction.Hit);
        ledger.ForgetPlayer(1);
        Assert.Equal(VenttiStatus.Accepted, Apply(ledger, VenttiAction.Hit).Status);
        Assert.Equal(1u, ledger.Snapshot().Round);
        Assert.Equal(new byte[] { 37, 21, 17 }, ledger.Snapshot().PlayerCards);
        Assert.Equal(VenttiTableState.Win, ledger.Snapshot().Outcome);
    }

    [Fact]
    public void DuplicateReceiptsAreCopiesAndCannotBeReusedForDifferentActionsOrRevisions()
    {
        var ledger = Ledger(stake: 0);
        var request = Request(ledger, VenttiAction.Increase);
        var receipt = ledger.Apply(request, 100, 0, true, out float cash);
        receipt.CashDelta = 99999;
        Assert.True(ledger.TryGetReceipt(request, out receipt));
        Assert.Equal(-50, receipt.CashDelta);
        ledger.Apply(request, cash, 1, true, out float repeated);
        Assert.Equal(cash, repeated);
        request.Action = VenttiAction.Decrease;
        Assert.Equal(VenttiStatus.Stale, ledger.Apply(request, cash, 1, true, out _).Status);
        request.Action = VenttiAction.Increase;
        request.Revision++;
        Assert.Equal(VenttiStatus.Stale, ledger.Apply(request, cash, 1, true, out _).Status);
        Assert.Equal(50, ledger.Snapshot().Stake);
    }

    [Fact]
    public void SequenceWrapIsAcceptedAndOldOrHalfRangeRequestsCannotSpendCash()
    {
        var ledger = Ledger(stake: 0);
        Apply(ledger, VenttiAction.Increase, ushort.MaxValue);
        Assert.Equal(VenttiStatus.Accepted, Apply(ledger, VenttiAction.Increase, 0).Status);
        Assert.Equal(VenttiStatus.Stale, Apply(ledger, VenttiAction.Decrease, ushort.MaxValue).Status);
        Assert.Equal(VenttiStatus.Stale, Apply(ledger, VenttiAction.Decrease, 0x8000).Status);
        Assert.Equal(100, ledger.Snapshot().Stake);
    }

    [Fact]
    public void StaleViewsAndDistantPlayersCannotMutateTheTable()
    {
        var ledger = Ledger();
        var request = Request(ledger, VenttiAction.Hit);
        request.Revision--;
        Assert.Equal(VenttiStatus.Refresh, ledger.Apply(request, 100, 0, true, out _).Status);
        request = Request(ledger, VenttiAction.Hit, 2);
        Assert.Equal(VenttiStatus.Distant, ledger.Apply(request, 100, 0, false, out _).Status);
        Assert.Equal(1u, ledger.Snapshot().Revision);
        Assert.Empty(ledger.Snapshot().PlayerCards);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidCashDoesNotConsumeASequence(float cash)
    {
        var ledger = Ledger();
        var request = Request(ledger, VenttiAction.Hit);
        Assert.Equal(VenttiStatus.Invalid, ledger.Apply(request, cash, 0, true, out _).Status);
        Assert.Equal(VenttiStatus.Accepted, ledger.Apply(request, 100, 0, true, out _).Status);
    }

    [Fact]
    public void WrongTableUnknownActionAndReservedPlayerCannotPoisonReceipts()
    {
        var ledger = Ledger();
        var request = Request(ledger, VenttiAction.Hit);
        request.TableId++;
        Assert.Equal(VenttiStatus.Invalid, ledger.Apply(request, 100, 0, true, out _).Status);
        request.TableId = Table; request.Action = (VenttiAction)255;
        Assert.Equal(VenttiStatus.Invalid, ledger.Apply(request, 100, 0, true, out _).Status);
        request.Action = VenttiAction.Hit; request.PlayerId = VenttiLedgerState.NoPlayer;
        Assert.Equal(VenttiStatus.Invalid, ledger.Apply(request, 100, 0, true, out _).Status);
        request.PlayerId = 1;
        Assert.Equal(VenttiStatus.Accepted, ledger.Apply(request, 100, 0, true, out _).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(51)]
    public void FailedRandomSourceDoesNotPartiallyDealOrConsumeAReceipt(int failAt)
    {
        int draws = 0;
        var ledger = Ledger(random: count => draws++ == failAt ? count : 0);
        var request = Request(ledger, VenttiAction.Hit);
        Assert.Throws<InvalidOperationException>(() => ledger.Apply(request, 100, 0, true, out _));
        var state = ledger.Snapshot();
        Assert.Equal(1u, state.Revision);
        Assert.Equal(0u, state.Round);
        Assert.Equal(50, state.Stake);
        Assert.Empty(state.PlayerCards);
        Assert.False(ledger.TryGetReceipt(request, out _));
    }

    [Fact]
    public void SnapshotsCannotModifyThePrivateDeckOrLiveHand()
    {
        var ledger = Ledger(random: Deck(37, 1, 21, 17));
        Apply(ledger, VenttiAction.Hit);
        var snapshot = ledger.Snapshot();
        snapshot.PlayerCards[0] = 52;
        snapshot.HouseCards[0] = 52;
        snapshot.Stake = 9999;
        Apply(ledger, VenttiAction.Hit, 2);
        Assert.Equal(new byte[] { 37, 21, 17 }, ledger.Snapshot().PlayerCards);
        Assert.Equal(50, ledger.Snapshot().Stake);
    }

    [Fact]
    public void UnrepresentableWalletCreditStaysOwedAndCannotRerollOrPayTwice()
    {
        var ledger = Ledger(random: Deck(49, 1, 29));
        var request = Request(ledger, VenttiAction.Hit);
        Assert.Equal(VenttiStatus.Accepted, ledger.Apply(request, float.MaxValue, 0, true, out float cash).Status);
        Assert.Equal(float.MaxValue, cash);
        Assert.Equal(VenttiTableState.Win, ledger.Snapshot().Outcome);
        Assert.Equal(100, ledger.Snapshot().PendingCash);
        Assert.Equal(VenttiStatus.Funds, Apply(ledger, VenttiAction.NextHand, 2).Status);
        Assert.False(ledger.TryCollect(float.MaxValue, out _));
        Assert.True(ledger.TryCollect(100.25f, out cash));
        Assert.Equal(200.25f, cash);
        Assert.True(ledger.TryCollect(cash, out float repeated));
        Assert.Equal(cash, repeated);
        ledger.Apply(request, cash, 1, true, out repeated);
        Assert.Equal(cash, repeated);
        Assert.Equal(0, ledger.Snapshot().PendingCash);
    }

    [Fact]
    public void SeededHandsPreserveCashConservationAndNeverRepeatACard()
    {
        for (int seed = 0; seed < 500; seed++)
        {
            var ledger = Ledger(random: new Random(seed).Next);
            float cash = 950.25f; // Initial 1000.25 minus the already paid 50 mk stake.
            ushort sequence = 1;
            do
            {
                var before = ledger.Snapshot();
                var action = before.Phase == VenttiPhase.Betting || before.PlayerTotal < 17
                    ? VenttiAction.Hit : VenttiAction.Stand;
                var receipt = ledger.Apply(Request(ledger, action, sequence++), cash, 0, true, out cash);
                Assert.Equal(VenttiStatus.Accepted, receipt.Status);
            } while (ledger.Snapshot().Phase == VenttiPhase.Playing);
            var state = ledger.Snapshot();
            bool won = state.Outcome == VenttiTableState.Win;
            Assert.Equal(won ? 1050.25f : 950.25f, cash);
            Assert.Equal(won ? 50 : -50, state.OpponentLoss);
            var allCards = state.PlayerCards.Concat(state.HouseCards).ToArray();
            Assert.Equal(allCards.Length, allCards.Distinct().Count());
            Assert.Equal(state.PlayerTotal, state.PlayerCards.Sum(card => Rules().CardValue(card)));
            Assert.Equal(state.HouseTotal, state.HouseCards.Sum(card => Rules().CardValue(card)));
            Assert.True(ledger.FinishSession(cash, out float repeated));
            Assert.Equal(cash, repeated);
        }
    }

    [Fact]
    public void RuleArraysCannotBeChangedAfterTheLedgerAdoptsThem()
    {
        var cards = Values();
        var rules = new VenttiRules(cards, 50, 4000, 50, 7000, 6000, 0);
        cards[49] = 1;
        Assert.Equal(13, rules.CardValue(49));
        Assert.Throws<ArgumentException>(() => new VenttiRules(new int[52], 50, 4000, 50, 7000, 6000, 0));
        Assert.Throws<ArgumentException>(() => new VenttiRules(Values(), 0, 4000, 50, 7000, 6000, 0));
        Assert.Throws<ArgumentException>(() => new VenttiRules(Values(), 50, float.PositiveInfinity, 50, 7000, 6000, 0));
    }

    [Theory]
    [InlineData(-1f, 0, VenttiWager.Cash)]
    [InlineData(0f, 2, VenttiWager.Car)]
    [InlineData(0f, 0, VenttiWager.House)]
    [InlineData(50f, 0, VenttiWager.Car)]
    [InlineData(0f, 3, VenttiWager.Cash)]
    public void InvalidEscrowAndPropertyStagesCannotBeAdopted(float stake, int stage, VenttiWager wager)
    {
        Assert.Throws<ArgumentException>(() => Ledger(stake: stake, stage: stage, wager: wager));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void InvalidHostClockCannotReleaseControlOrConsumeASequence(float now)
    {
        var ledger = Ledger();
        var request = Request(ledger, VenttiAction.Hit);
        Assert.Equal(VenttiStatus.Invalid, ledger.Apply(request, 100, now, true, out _).Status);
        Assert.False(ledger.Advance(now));
        Assert.Equal(VenttiStatus.Accepted, ledger.Apply(request, 100, 0, true, out _).Status);
    }

    [Fact]
    public void TeardownCancelsAnUndealtPropertyWagerWithoutChangingItsOwnershipStage()
    {
        var ledger = Ledger(stake: 0, stage: 1, wager: VenttiWager.House);
        Assert.True(ledger.FinishSession(100.25f, out float cash));
        Assert.Equal(100.25f, cash);
        Assert.Equal(1, ledger.Snapshot().PropertyStage);
        Assert.Equal(VenttiTableState.None, ledger.Snapshot().Outcome);
    }

    [Fact]
    public void TeardownRefundsAnUndealtStakeExactlyOnceAndDisallowsNewActions()
    {
        var ledger = Ledger(stake: 125.25f);
        Assert.True(ledger.FinishSession(100, out float cash));
        Assert.Equal(225.25f, cash);
        Assert.True(ledger.FinishSession(cash, out float repeated));
        Assert.Equal(cash, repeated);
        Assert.Equal(VenttiStatus.Finished, Apply(ledger, VenttiAction.Increase).Status);
    }

    [Fact]
    public void TeardownStandsOnTheCommittedDeckWithoutAnotherRandomCall()
    {
        int draws = 0;
        var random = Deck(37, 33, 38, 34, 13);
        var ledger = Ledger(random: count => { draws++; return random(count); });
        Apply(ledger, VenttiAction.Hit);
        Assert.Equal(52, draws);
        Assert.True(ledger.FinishSession(100.25f, out float cash));
        Assert.Equal(200.25f, cash);
        Assert.Equal(VenttiTableState.Win, ledger.Snapshot().Outcome);
        Assert.Equal(22, ledger.Snapshot().HouseTotal);
        Assert.True(ledger.FinishSession(cash, out float repeated));
        Assert.Equal(cash, repeated);
        Assert.Equal(52, draws);
    }

    [Fact]
    public void FailedTeardownRefundRemainsClaimableAfterCleanupRetry()
    {
        var ledger = Ledger(stake: 50);
        Assert.False(ledger.FinishSession(float.MaxValue, out _));
        Assert.Equal(50, ledger.Snapshot().PendingCash);
        Assert.True(ledger.FinishSession(100, out float cash));
        Assert.Equal(150, cash);
        Assert.True(ledger.FinishSession(cash, out float repeated));
        Assert.Equal(cash, repeated);
    }
}
