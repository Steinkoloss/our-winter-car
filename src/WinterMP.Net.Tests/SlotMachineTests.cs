using System;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class SlotMachineTests
    {
        private static readonly int[] Payouts = { 2, 50, 15, 15, 9, 9, 5, 5, 3, 3 };
        private static SlotMachineLedger Ledger(SlotMachineState? state = null, Func<int, int>? random = null)
        {
            return new SlotMachineLedger(state ?? new SlotMachineState(),
                new[] { new[] { 2, 9 }, new[] { 2, 8 }, new[] { 2, 7 } }, Payouts, random ?? (_ => 0));
        }
        private static SlotMachineIntent Request(byte action, ushort seq, byte player = 1, uint round = 0)
            => new SlotMachineIntent { Action = action, Sequence = seq, PlayerId = player, Round = round };

        [Theory]
        [InlineData(1, 1, 1, 50)]
        [InlineData(2, 2, 2, 15)]
        [InlineData(3, 3, 3, 15)]
        [InlineData(4, 4, 4, 9)]
        [InlineData(5, 5, 5, 9)]
        [InlineData(6, 6, 6, 5)]
        [InlineData(7, 7, 7, 5)]
        [InlineData(8, 8, 8, 3)]
        [InlineData(9, 9, 9, 3)]
        [InlineData(2, 3, 4, 0)]
        [InlineData(1, 3, 4, 2)]
        [InlineData(3, 1, 4, 0)]
        [InlineData(3, 4, 1, 2)]
        public void NativePayoutsIncludeTheAsymmetricSmallWin(byte a, byte b, byte c, int expected)
            => Assert.Equal(expected, SlotMachineLedger.PayoutMultiplier(a, b, c, Payouts));

        [Fact]
        public void OneOrTwoWildsPayTheMatchingSymbolForEveryPosition()
        {
            for (byte symbol = 2; symbol <= 9; symbol++)
                for (int mask = 1; mask < 7; mask++)
                    Assert.Equal(Payouts[symbol], SlotMachineLedger.PayoutMultiplier(
                        (mask & 1) != 0 ? (byte)1 : symbol,
                        (mask & 2) != 0 ? (byte)1 : symbol,
                        (mask & 4) != 0 ? (byte)1 : symbol, Payouts));
        }

        [Fact]
        public void InsertUsesCurrentBetAndCashoutUsesAccumulatedWinningsOnly()
        {
            var ledger = Ledger(new SlotMachineState { Bet = 3, Credit = 7, Winnings = 12 });
            Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Pay, 1), 100.25f, 0, true, out float cash));
            Assert.Equal(97.25f, cash);
            Assert.Equal(10, ledger.Snapshot().Credit);
            for (ushort seq = 2; seq <= 4; seq += 2)
            {
                Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Spin, seq), cash, seq, true, out cash));
                Assert.Equal(45, ledger.Snapshot().LastWin);
                Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Finish, (ushort)(seq + 1), round: (uint)(seq / 2)),
                    cash, seq + 2, true, out cash));
            }
            Assert.Equal(102, ledger.Snapshot().Winnings);
            Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Cashout, 6), cash, 7, true, out cash));
            Assert.Equal(199.25f, cash);
            Assert.Equal(4, ledger.Snapshot().Credit);
            Assert.Equal(0, ledger.Snapshot().Winnings);
        }

        [Theory]
        [InlineData(4, 9, 1, 9, SlotMachineResult.Accepted)]
        [InlineData(2, 9, 2, 6, SlotMachineResult.Accepted)]
        [InlineData(2, 2, 2, 2, SlotMachineResult.Funds)]
        public void SpinDebitsOneSourceAndCannotCombineInsufficientSources(int credit, int winnings,
            int remainingCredit, int remainingWinnings, byte result)
        {
            var ledger = Ledger(new SlotMachineState { Bet = 3, Credit = credit, Winnings = winnings });
            Assert.Equal(result, ledger.Apply(Request(SlotMachineIntent.Spin, 1), 0, 0, true, out float cash));
            Assert.Equal(0, cash);
            Assert.Equal(remainingCredit, ledger.Snapshot().Credit);
            Assert.Equal(remainingWinnings, ledger.Snapshot().Winnings);
        }

        [Fact]
        public void DuplicateAndAlteredRequestsCannotDebitAgain()
        {
            var ledger = Ledger();
            var pay = Request(SlotMachineIntent.Pay, 65000);
            Assert.Equal(0, ledger.Apply(pay, 10, 0, true, out float cash));
            Assert.Equal(0, ledger.Apply(pay, cash, 1, false, out cash));
            Assert.Equal(9, cash);
            Assert.Equal(1, ledger.Snapshot().Credit);
            Assert.True(ledger.TryGetReceipt(pay, out byte result, out int debit));
            Assert.Equal(SlotMachineResult.Accepted, result);
            Assert.Equal(-1, debit);
            Assert.Equal(SlotMachineResult.Stale, ledger.Apply(Request(SlotMachineIntent.Cashout, 65000), cash, 2, true, out _));
            Assert.Equal(SlotMachineResult.Stale, ledger.Apply(Request(SlotMachineIntent.Pay, 64999), cash, 2, true, out _));
            Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Pay, 0), cash, 2, true, out cash));
            Assert.Equal(8, cash);
        }

        [Fact]
        public void RejectionIsTerminalEvenAfterFundsArriveOrDistanceChanges()
        {
            var ledger = Ledger();
            var pay = Request(SlotMachineIntent.Pay, 1);
            Assert.Equal(SlotMachineResult.Funds, ledger.Apply(pay, 0, 0, true, out _));
            Assert.Equal(SlotMachineResult.Funds, ledger.Apply(pay, 100, 1, true, out float cash));
            Assert.Equal(100, cash);
            Assert.Equal(SlotMachineResult.Distant, ledger.Apply(Request(SlotMachineIntent.Pay, 2), cash, 2, false, out _));
            Assert.Equal(SlotMachineResult.Distant, ledger.Apply(Request(SlotMachineIntent.Pay, 2), cash, 3, true, out _));
            Assert.Equal(0, ledger.Snapshot().Credit);
        }

        [Fact]
        public void CashoutReceiptRetainsTheActualPayoutAcrossRetries()
        {
            var ledger = Ledger(new SlotMachineState { Winnings = 900 });
            var request = Request(SlotMachineIntent.Cashout, 1);
            Assert.Equal(0, ledger.Apply(request, 10.25f, 0, true, out float cash));
            Assert.Equal(910.25f, cash);
            Assert.Equal(0, ledger.Apply(request, cash, 1, true, out cash));
            Assert.Equal(910.25f, cash);
            Assert.True(ledger.TryGetReceipt(request, out byte result, out int amount));
            Assert.Equal(SlotMachineResult.Accepted, result);
            Assert.Equal(900, amount);
            var receipt = (SlotMachineResult)PacketCodec.Decode(PacketCodec.Encode(new SlotMachineResult
            {
                PlayerId = 1, Sequence = 1, Result = result, CashDelta = amount,
            }));
            Assert.Equal(900, receipt.CashDelta);
        }

        [Fact]
        public void PlayersCanUseDifferentMachinesWithoutSharingSequenceOrLeaseState()
        {
            var first = Ledger();
            var second = Ledger(new SlotMachineState { MachineId = 123 });
            first.Apply(Request(SlotMachineIntent.Pay, 65000), 100, 0, true, out float cash);
            var request = Request(SlotMachineIntent.Pay, 1, 2);
            request.MachineId = 123;
            Assert.Equal(0, second.Apply(request, cash, 1, true, out cash));
            Assert.Equal(98, cash);
            Assert.Equal(1, first.Snapshot().PlayerId);
            Assert.Equal(2, second.Snapshot().PlayerId);
            Assert.Equal(1, first.Snapshot().Credit);
            Assert.Equal(1, second.Snapshot().Credit);
        }

        [Fact]
        public void EarlyFinishRetriesAndTimeoutSettlesExactlyOnce()
        {
            var ledger = Ledger(new SlotMachineState { Credit = 1 });
            ledger.Apply(Request(SlotMachineIntent.Spin, 1), 0, 0, true, out _);
            var finish = Request(SlotMachineIntent.Finish, 2, round: 1);
            Assert.Equal(SlotMachineResult.Retry, ledger.Apply(finish, 0, 1, true, out _));
            Assert.False(ledger.TryGetReceipt(finish, out _));
            Assert.Equal(0, ledger.Snapshot().Winnings);
            Assert.True(ledger.Advance(10));
            Assert.Equal(15, ledger.Snapshot().Winnings);
            Assert.Equal(0, ledger.Apply(finish, 0, 11, true, out _));
            Assert.Equal(0, ledger.Apply(finish, 0, 12, true, out _));
            Assert.False(ledger.Advance(20));
            Assert.Equal(15, ledger.Snapshot().Winnings);
        }

        [Fact]
        public void OtherPlayersCannotStealAnActiveLeaseAndCanUseItAfterExpiry()
        {
            var ledger = Ledger();
            ledger.Apply(Request(SlotMachineIntent.Pay, 1), 100, 0, true, out _);
            Assert.Equal(SlotMachineResult.Busy, ledger.Apply(Request(SlotMachineIntent.Cashout, 1, 2), 99, 14, true, out _));
            Assert.Equal(1, ledger.Snapshot().PlayerId);
            Assert.True(ledger.Advance(15));
            Assert.Equal(SlotMachineState.NoPlayer, ledger.Snapshot().PlayerId);
            Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Spin, 2, 2), 99, 15, true, out _));
            Assert.Equal(SlotMachineResult.Invalid, ledger.Apply(Request(SlotMachineIntent.Finish, 3, 2, 999), 99, 17, true, out _));
            Assert.True(ledger.Snapshot().Spinning);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DisconnectOrSessionEndPreservesAnInFlightWin(bool endSession)
        {
            var ledger = Ledger(new SlotMachineState { Credit = 5, Winnings = 10 });
            ledger.Apply(Request(SlotMachineIntent.Spin, 40000), 0, 0, true, out _);
            if (endSession) ledger.FinishSession(); else ledger.ForgetPlayer(1);
            ledger.FinishSession();
            Assert.False(ledger.Snapshot().Spinning);
            Assert.Equal(4, ledger.Snapshot().Credit);
            Assert.Equal(25, ledger.Snapshot().Winnings);
            Assert.Equal(SlotMachineState.NoPlayer, ledger.Snapshot().PlayerId);
            if (!endSession)
                Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Cashout, 1), 0, 1, true, out _));
        }

        [Fact]
        public void HoldsPreserveRawStopsSkipTheirRandomDrawAndAlternateAfterLosses()
        {
            int draws = 0;
            var ledger = Ledger(new SlotMachineState { Credit = 4, CanHold = true, Reel1 = 4, Reel2 = 5, Reel3 = 6 },
                _ => { draws++; return 1; });
            Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Hold1, 1), 0, 0, true, out _));
            Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Hold2, 2), 0, 0, true, out _));
            Assert.Equal(SlotMachineResult.Invalid, ledger.Apply(Request(SlotMachineIntent.Hold3, 3), 0, 0, true, out _));
            ledger.Apply(Request(SlotMachineIntent.Spin, 4), 0, 0, true, out _);
            Assert.Equal(1, draws);
            Assert.Equal(4, ledger.Snapshot().Reel1);
            Assert.Equal(5, ledger.Snapshot().Reel2);
            Assert.Equal(7, ledger.Snapshot().Reel3);
            Assert.Equal(0, ledger.Apply(Request(SlotMachineIntent.Finish, 5, round: 1), 0, .5f, true, out _));
            Assert.False(ledger.Snapshot().CanHold);
            Assert.Equal(0, ledger.Snapshot().HoldMask);
            ledger.Apply(Request(SlotMachineIntent.Spin, 6), 0, 1, true, out _);
            ledger.Apply(Request(SlotMachineIntent.Finish, 7, round: 2), 0, 3, true, out _);
            Assert.True(ledger.Snapshot().CanHold);
            Assert.Equal(4, draws);
        }

        [Theory]
        [InlineData(4, 5, 0)]
        [InlineData(5, 1, 1)]
        public void BetChangePreservesTheNativeWraparoundHoldRule(byte bet, byte nextBet, byte holds)
        {
            var ledger = Ledger(new SlotMachineState { Bet = bet, CanHold = true, HoldMask = 1, Reel1 = 2, Reel2 = 3, Reel3 = 4 });
            ledger.Apply(Request(SlotMachineIntent.Bet, 1), 0, 0, true, out _);
            Assert.Equal(nextBet, ledger.Snapshot().Bet);
            Assert.Equal(holds, ledger.Snapshot().HoldMask);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(1e20f)]
        public void InvalidOrImpreciseCashCannotMintOrLoseMachineFunds(float cash)
        {
            var ledger = Ledger(new SlotMachineState { Winnings = 10 });
            Assert.Equal(SlotMachineResult.Funds, ledger.Apply(Request(SlotMachineIntent.Pay, 1), cash, 0, true, out _));
            Assert.Equal(SlotMachineResult.Funds, ledger.Apply(Request(SlotMachineIntent.Cashout, 2), cash, 0, true, out _));
            Assert.Equal(0, ledger.Snapshot().Credit);
            Assert.Equal(10, ledger.Snapshot().Winnings);
        }

        [Fact]
        public void BrokenRandomSourceCannotPartiallyDebitASpin()
        {
            int calls = 0;
            var ledger = Ledger(new SlotMachineState { Credit = 2 }, _ => ++calls == 3 ? -1 : 0);
            Assert.Throws<InvalidOperationException>(() => ledger.Apply(Request(SlotMachineIntent.Spin, 1), 5, 0, true, out _));
            Assert.Equal(2, ledger.Snapshot().Credit);
            Assert.Equal(0u, ledger.Snapshot().Round);
            Assert.False(ledger.Snapshot().Spinning);
            Assert.False(ledger.TryGetReceipt(Request(SlotMachineIntent.Spin, 1), out _));
        }

        [Fact]
        public void BalancesAreBoundedAndSnapshotCopiesCannotMutateAuthority()
        {
            var ledger = Ledger(new SlotMachineState { Credit = SlotMachineLedger.MaximumBalance, Winnings = SlotMachineLedger.MaximumBalance });
            Assert.Equal(SlotMachineResult.Funds, ledger.Apply(Request(SlotMachineIntent.Pay, 1), 10, 0, true, out _));
            Assert.Equal(SlotMachineResult.Funds, ledger.Apply(Request(SlotMachineIntent.Spin, 2), 10, 0, true, out _));
            ledger.Snapshot().Winnings = 0;
            Assert.Equal(SlotMachineLedger.MaximumBalance, ledger.Snapshot().Winnings);
            Assert.False(SlotMachineLedger.IsValid(new SlotMachineState { Bet = 0 }));
            Assert.False(SlotMachineLedger.IsValid(new SlotMachineState { HoldMask = 7 }));
            Assert.False(SlotMachineLedger.IsValid(new SlotMachineState { Spinning = true }));
        }

        [Fact]
        public void SlotWirePreservesLargeBalancesAndUsesOnlyTheReliableChannel()
        {
            var state = (SlotMachineState)PacketCodec.Decode(PacketCodec.Encode(new SlotMachineState
            {
                MachineId = uint.MaxValue, Revision = uint.MaxValue, Round = 100000, PlayerId = 3,
                Spinning = true, Bet = 5, HoldMask = 3, CanHold = true,
                Credit = 70000, Winnings = 100000, LastWin = 250, Reel1 = 1, Reel2 = 2, Reel3 = 9,
            }));
            Assert.Equal(70000, state.Credit);
            Assert.Equal(100000, state.Winnings);
            Assert.Equal(100000u, state.Round);
            Assert.Equal(uint.MaxValue, state.Revision);
            Assert.True(SlotMachineLedger.IsValid(state));
            var intent = Request(SlotMachineIntent.Finish, ushort.MaxValue, 3, 100000);
            var decoded = (SlotMachineIntent)PacketCodec.Decode(PacketCodec.Encode(intent));
            Assert.Equal(intent.Sequence, decoded.Sequence);
            Assert.Equal(intent.Round, decoded.Round);
            foreach (var id in new[] { MessageId.SlotMachineState, MessageId.SlotMachineIntent, MessageId.SlotMachineResult })
                foreach (var channel in Enum.GetValues<Channel>())
                    Assert.Equal(channel == Channel.ReliableOrdered, SessionMessagePolicy.IsChannelAllowed(id, channel));
        }
    }
}
