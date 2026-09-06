using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VenttiGameTests
{
    private const uint Table = 0x01020304;
    private static VenttiRules Rules() => new(Enumerable.Range(0, 53).Select(i => i == 0 ? 0 : (i - 1) / 4 + 1).ToArray(), 50, 4000, 50, 7000, 6000, 0);
    private static VenttiLedger Ledger(int seed = 0, VenttiWager wager = VenttiWager.Cash) =>
        new(Table, Rules(), 5000, wager == VenttiWager.House ? 1 : 0, 0, wager == VenttiWager.Cash ? 50 : 0, wager, new Random(seed).Next);

    [Fact]
    public void CommandAndReceiptWireOrderIsFixedAndRegistered()
    {
        var request = new VenttiRequest { TableId = Table, Revision = 2, PlayerId = 3, Sequence = 0x0506, Action = VenttiAction.Stand };
        Assert.Equal(new byte[] { 176, 0, 4, 3, 2, 1, 2, 0, 0, 0, 3, 6, 5, 3 }, PacketCodec.Encode(request));
        var read = Assert.IsType<VenttiRequest>(PacketCodec.Decode(PacketCodec.Encode(request)));
        Assert.Equal(request.Action, read.Action);
        Assert.Equal(request.Sequence, read.Sequence);
        var receipt = new VenttiReceipt { TableId = Table, Revision = 2, PlayerId = 3, Sequence = 0x0506,
            Status = VenttiStatus.Accepted, CashDelta = 50, Round = 7, Outcome = VenttiTableState.Win };
        Assert.Equal(new byte[] { 177, 0, 4, 3, 2, 1, 2, 0, 0, 0, 3, 6, 5, 0, 0, 0, 72, 66, 7, 0, 0, 0, 1 }, PacketCodec.Encode(receipt));
        var copy = Assert.IsType<VenttiReceipt>(PacketCodec.Decode(PacketCodec.Encode(receipt)));
        Assert.Equal(receipt.Round, copy.Round);
        Assert.Equal(receipt.CashDelta, copy.CashDelta);
        Assert.Equal(receipt.Outcome, copy.Outcome);
    }

    [Fact]
    public void LedgerSnapshotWireLayoutIsFixedAndContainsOnlyPublicCards()
    {
        var state = new VenttiLedgerState { TableId = Table, Revision = 2, Round = 3, PlayerId = 4,
            Phase = VenttiPhase.Playing, Wager = VenttiWager.Cash, Stake = 50, BetMaximum = 200,
            OpponentLoss = -50, PropertyStage = 1, PlayerTotal = 12, HouseTotal = 2, PlayerCards = new byte[] { 1, 41 }, HouseCards = new byte[] { 5 } };
        var packet = PacketCodec.Encode(state);
        Assert.Equal(new byte[] { 175, 0, 4, 3, 2, 1, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 1, 0,
            0, 0, 72, 66, 0, 0, 72, 67, 0, 0, 72, 194, 0, 0, 0, 0,
            1, 0, 0, 0, 12, 0, 0, 0, 2, 0, 0, 0, 2, 1, 41, 1, 5 }, packet);
        var copy = Assert.IsType<VenttiLedgerState>(PacketCodec.Decode(packet));
        Assert.True(VenttiGameReplica.IsValid(Rules(), copy));
        Assert.Equal(state.PlayerCards, copy.PlayerCards);
        Assert.Equal(state.HouseCards, copy.HouseCards);
        for (int length = 2; length < packet.Length; length++)
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet.Take(length).ToArray()));
        packet[46] = 53;
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        state.PlayerCards = new byte[53];
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
    }

    [Theory]
    [InlineData(VenttiWager.Cash)]
    [InlineData(VenttiWager.Car)]
    [InlineData(VenttiWager.House)]
    public void EverySeededPublicHandValidatesAndSurvivesLateJoinWithoutThePrivateDeck(VenttiWager wager)
    {
        for (int seed = 0; seed < 300; seed++)
        {
            var ledger = Ledger(seed, wager);
            var guest = new VenttiGameReplica();
            float cash = 1000.25f;
            Assert.True(guest.Receive(Table, Rules(), ledger.Snapshot()));
            while (guest.Current!.Phase <= VenttiPhase.Playing)
            {
                var action = guest.Current.Phase == VenttiPhase.Betting || guest.Current.PlayerTotal < 17 ? VenttiAction.Hit : VenttiAction.Stand;
                Assert.True(guest.Queue(1, action));
                var request = guest.Pending!;
                var receipt = ledger.Apply(request, cash, 0, true, out cash);
                Assert.Equal(VenttiStatus.Accepted, receipt.Status);
                var state = Assert.IsType<VenttiLedgerState>(PacketCodec.Decode(PacketCodec.Encode(ledger.Snapshot())));
                Assert.Equal(48 + state.PlayerCards.Length + state.HouseCards.Length, PacketCodec.Encode(state).Length);
                Assert.True(guest.Receive(Table, Rules(), state), "seed " + seed + " phase " + state.Phase);
                Assert.Equal(state.Phase == VenttiPhase.Playing ? VenttiTableState.None : state.Outcome, receipt.Outcome);
                if (receipt.Outcome != 0) Assert.Equal(state.Round, receipt.Round);
                Assert.True(guest.Acknowledge(receipt));
                Assert.False(guest.Acknowledge(receipt));
                Assert.True(new VenttiGameReplica().Receive(Table, Rules(), state));
                float beforeRetry = cash;
                var retry = ledger.Apply(request, cash, 1, true, out cash);
                Assert.Equal(beforeRetry, cash);
                Assert.Equal(receipt.Outcome, retry.Outcome);
                Assert.Equal(receipt.Round, retry.Round);
            }
            Assert.Equal(ledger.Snapshot().Outcome, guest.Current.Outcome);
            if (guest.Current.Phase == VenttiPhase.Resolved)
            {
                Assert.True(ledger.ResetResolved());
                Assert.True(guest.Receive(Table, Rules(), ledger.Snapshot()));
                Assert.Empty(guest.Current.PlayerCards);
                Assert.Equal(0, guest.Current.Outcome);
                Assert.False(ledger.ResetResolved());
            }
        }
    }

    [Fact]
    public void AutomaticResetWaitsForAnOwedWinAndDoesNotClearItsReceipt()
    {
        int draw = 0;
        int[] prefix = { 36, 0, 38 }; // player ten + jack; house ace
        var ledger = new VenttiLedger(Table, Rules(), 200, 0, 0, 50, VenttiWager.Cash,
            count => draw < prefix.Length ? prefix[draw++] : 0);
        var request = new VenttiRequest { TableId = Table, Revision = 1, PlayerId = 1, Sequence = 1, Action = VenttiAction.Hit };
        var receipt = ledger.Apply(request, float.MaxValue, 0, true, out float cash);
        Assert.Equal(VenttiTableState.Win, receipt.Outcome);
        Assert.Equal(float.MaxValue, cash);
        Assert.Equal(100, ledger.Snapshot().PendingCash);
        Assert.False(ledger.ResetResolved());
        Assert.True(ledger.TryCollect(100, out cash));
        Assert.Equal(200, cash);
        Assert.True(ledger.ResetResolved());
        Assert.Equal(0, ledger.Snapshot().Stake);
        Assert.Equal(250, ledger.Snapshot().BetMaximum);
        Assert.True(ledger.TryGetReceipt(request, out var copy));
        Assert.Equal(receipt.Outcome, copy.Outcome);
        Assert.Equal(receipt.Round, copy.Round);
        Assert.True(ledger.TryCollect(cash, out float repeated));
        Assert.Equal(cash, repeated);
    }

    [Fact]
    public void InvalidSnapshotsDoNotPoisonOrderingAndCopiesDoNotAliasHostState()
    {
        var ledger = Ledger();
        var guest = new VenttiGameReplica();
        var state = ledger.Snapshot();
        state.Revision = uint.MaxValue;
        Assert.True(guest.Receive(Table, Rules(), state));
        state.Revision = 0;
        Assert.True(guest.Receive(Table, Rules(), state));
        Assert.False(guest.Receive(Table, Rules(), state));
        state.Revision = 0x80000000;
        Assert.False(guest.Receive(Table, Rules(), state));
        state.Revision = 99; state.Stake = float.NaN;
        Assert.False(guest.Receive(Table, Rules(), state));
        state.Stake = 50; state.TableId++;
        Assert.False(guest.Receive(Table, Rules(), state));
        Assert.Equal(0u, guest.Current!.Revision);
        guest.Clear();
        state = ledger.Snapshot();
        Assert.True(guest.Receive(Table, Rules(), state));
        state.Stake = 0;
        Assert.Equal(50, guest.Current!.Stake);
    }

    [Fact]
    public void RefreshResubmitsTheSameActionAgainstFreshStateWithABoundedNewSequence()
    {
        var guest = new VenttiGameReplica();
        var state = Ledger().Snapshot();
        guest.Receive(Table, Rules(), state);
        Assert.True(guest.Queue(1, VenttiAction.Increase));
        Assert.False(guest.Queue(1, VenttiAction.Increase));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var pending = guest.Pending!;
            state.Revision++;
            Assert.True(guest.Receive(Table, Rules(), state));
            var receipt = new VenttiReceipt { TableId = Table, Revision = state.Revision, Sequence = pending.Sequence,
                PlayerId = 1, Status = VenttiStatus.Refresh };
            Assert.True(guest.Acknowledge(receipt));
            Assert.False(guest.Acknowledge(receipt));
            if (attempt == 2) Assert.Null(guest.Pending);
            else
            {
                Assert.Equal(state.Revision, guest.Pending!.Revision);
                Assert.Equal((ushort)(pending.Sequence + 1), guest.Pending.Sequence);
                Assert.Equal(pending.Action, guest.Pending.Action);
            }
        }
    }

    [Fact]
    public void UnmatchedOrMalformedReceiptsCannotSettleThePendingCommand()
    {
        var guest = new VenttiGameReplica();
        guest.Receive(Table, Rules(), Ledger().Snapshot()); guest.Queue(1, VenttiAction.Hit);
        var receipt = new VenttiReceipt { TableId = Table, Sequence = guest.Pending!.Sequence, PlayerId = 2 };
        Assert.False(guest.Acknowledge(receipt));
        receipt.PlayerId = 1; receipt.TableId++;
        Assert.False(guest.Acknowledge(receipt));
        receipt.TableId = Table; receipt.Status = VenttiStatus.Funds; receipt.CashDelta = 100;
        Assert.False(guest.Acknowledge(receipt));
        receipt.CashDelta = 0; receipt.Outcome = VenttiTableState.Win;
        Assert.False(guest.Acknowledge(receipt));
        receipt.Status = VenttiStatus.Accepted; receipt.Outcome = 7;
        Assert.False(guest.Acknowledge(receipt));
        receipt.Outcome = 0; receipt.CashDelta = float.PositiveInfinity;
        Assert.False(guest.Acknowledge(receipt));
        Assert.NotNull(guest.Pending);
    }

    [Fact]
    public void ImpossibleCardsAndEarlyOrLateHouseStopsAreRejected()
    {
        var state = new VenttiLedgerState { TableId = Table, BetMaximum = 200, Stake = 50, Phase = VenttiPhase.Playing,
            PlayerCards = new byte[] { 1, 5 }, PlayerTotal = 3, HouseCards = new byte[] { 9 }, HouseTotal = 3 };
        Assert.True(VenttiGameReplica.IsValid(Rules(), state));
        state.HouseCards[0] = 1; state.HouseTotal = 1;
        Assert.False(VenttiGameReplica.IsValid(Rules(), state));
        state.HouseCards = new byte[] { 9, 13, 17 }; state.HouseTotal = 12;
        state.Phase = VenttiPhase.Resolved; state.Outcome = VenttiTableState.Lose;
        Assert.False(VenttiGameReplica.IsValid(Rules(), state)); // house should have stopped at seven
        state.HouseCards = new byte[] { 2 }; state.HouseTotal = 1;
        Assert.False(VenttiGameReplica.IsValid(Rules(), state)); // stand must draw a second card
        state.PlayerCards = new byte[] { 37, 41 }; state.PlayerTotal = 21;
        state.HouseCards = new byte[] { 1, 2 }; state.HouseTotal = 2; state.Outcome = VenttiTableState.Win;
        Assert.False(VenttiGameReplica.IsValid(Rules(), state)); // player 21 never runs the house turn
    }

    [Fact]
    public void EveryNewMessageUsesTheAuthenticatedOrderedChannel()
    {
        foreach (var id in new[] { MessageId.VenttiLedgerState, MessageId.VenttiRequest, MessageId.VenttiReceipt })
        {
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, false, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
        }
    }
}
