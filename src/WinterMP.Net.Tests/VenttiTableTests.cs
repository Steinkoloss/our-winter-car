using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VenttiTableTests
{
    private const uint Table = 0x21BEEF;
    private static VenttiTableState State(uint sequence, float stake = 1250.75f, byte outcome = VenttiTableState.None) =>
        new() { TableId = Table, Sequence = sequence, Stake = stake, PlayerTotal = 21, HouseTotal = 20, Outcome = outcome };

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(255.75f)]
    [InlineData(256f)]
    [InlineData(7000.5f)]
    [InlineData(float.MaxValue)]
    public void StakesAndEveryNativeOutcomeRoundTripWithoutByteOrIntegerTruncation(float stake)
    {
        for (byte outcome = VenttiTableState.None; outcome <= VenttiTableState.LoseHouse; outcome++)
        {
            var original = State(uint.MaxValue, stake, outcome);
            var packet = PacketCodec.Encode(original);
            var copy = Assert.IsType<VenttiTableState>(PacketCodec.Decode(packet));
            Assert.Equal((MessageId)174, copy.Id);
            Assert.True(VenttiTableReplica.IsValid(copy));
            Assert.True(VenttiTableReplica.SameTable(original, copy));
            Assert.Equal(uint.MaxValue, copy.Sequence);
        }
    }

    [Fact]
    public void PublishedWireOrderAndWidthsRemainFixed()
    {
        Assert.Equal(new byte[] {
            174, 0, 0xEF, 0xBE, 0x21, 0, 4, 3, 2, 1,
            0, 0x40, 0x80, 0x43, 21, 0, 0, 0, 20, 0, 0, 0, 6,
        }, PacketCodec.Encode(State(0x01020304, 256.5f, VenttiTableState.LoseHouse)));
    }

    [Fact]
    public void ObservationUsesOnlyTheOrderedHostChannel()
    {
        Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiTableState, Channel.ReliableOrdered));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiTableState, Channel.ReliableBulk));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiTableState, Channel.UnreliableSequenced));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.VenttiTableState, false, true, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.VenttiTableState, false, true, true, false));
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.VenttiTableState, false, true, true, true));
    }

    [Fact]
    public void StateReceivedBeforeBindingRetainsTheFullStakeAndPropertyOutcome()
    {
        var guest = new VenttiTableReplica();
        Assert.True(guest.Receive(Table, State(0, 3250.5f, VenttiTableState.WinCar)));
        Assert.Equal(3250.5f, guest.Current!.Stake);
        Assert.Equal(VenttiTableState.WinCar, guest.Current.Outcome);
        Assert.True(guest.Receive(Table, State(1, 9000.25f, VenttiTableState.LoseHouse)));
        Assert.Equal(9000.25f, guest.Current.Stake);
        Assert.Equal(VenttiTableState.LoseHouse, guest.Current.Outcome);
    }

    [Fact]
    public void ResetExplicitlyClearsAnEarlierOutcomeAndHand()
    {
        var guest = new VenttiTableReplica();
        guest.Receive(Table, State(4, outcome: VenttiTableState.WinHouse));
        var reset = State(5, 0f);
        reset.PlayerTotal = reset.HouseTotal = 0;
        Assert.True(guest.Receive(Table, reset));
        Assert.Equal(VenttiTableState.None, guest.Current!.Outcome);
        Assert.Equal(0, guest.Current.PlayerTotal);
        Assert.Equal(0, guest.Current.HouseTotal);
        Assert.Equal(0f, guest.Current.Stake);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-0.25f)]
    public void InvalidStakeCannotPoisonThePendingStateOrSequence(float stake)
    {
        var guest = new VenttiTableReplica();
        guest.Receive(Table, State(1));
        Assert.False(guest.Receive(Table, State(99, stake)));
        Assert.Equal(1u, guest.Current!.Sequence);
        Assert.True(guest.Receive(Table, State(2)));
    }

    [Fact]
    public void WrongTableUnknownOutcomeAndNegativeTotalsCannotReplaceAValidObservation()
    {
        var guest = new VenttiTableReplica();
        guest.Receive(Table, State(10));
        var invalid = State(99);
        invalid.TableId++;
        Assert.False(guest.Receive(Table, invalid));
        invalid = State(99, outcome: 7);
        Assert.False(guest.Receive(Table, invalid));
        invalid = State(99);
        invalid.PlayerTotal = -1;
        Assert.False(guest.Receive(Table, invalid));
        invalid.PlayerTotal = 21;
        invalid.HouseTotal = -1;
        Assert.False(guest.Receive(Table, invalid));
        Assert.Equal(10u, guest.Current!.Sequence);
        Assert.True(guest.Receive(Table, State(11)));
    }

    [Fact]
    public void DelayedJoinSnapshotDuplicatesAndAmbiguousSequenceCannotUndoLiveState()
    {
        var guest = new VenttiTableReplica();
        guest.Receive(Table, State(100, outcome: VenttiTableState.LoseCar));
        Assert.False(guest.Receive(Table, State(99)));
        Assert.False(guest.Receive(Table, State(100)));
        Assert.False(guest.Receive(Table, State(100u + 0x80000000u)));
        Assert.Equal(VenttiTableState.LoseCar, guest.Current!.Outcome);
        Assert.True(guest.Receive(Table, State(101)));
    }

    [Fact]
    public void SequenceWrapAndReconnectAcceptTheFirstFreshHostObservation()
    {
        var guest = new VenttiTableReplica();
        guest.Receive(Table, State(uint.MaxValue));
        Assert.True(guest.Receive(Table, State(0)));
        Assert.False(guest.Receive(Table, State(uint.MaxValue)));
        guest.Clear();
        Assert.Null(guest.Current);
        Assert.True(guest.Receive(Table, State(0, outcome: VenttiTableState.Win)));
    }

    [Fact]
    public void SnapshotComparisonAndReceiptDoNotMutateTheLiveBroadcastBaseline()
    {
        var baseline = State(5);
        var snapshot = State(6, 1251f, VenttiTableState.Lose);
        var guest = new VenttiTableReplica();
        guest.Receive(Table, snapshot);
        Assert.False(VenttiTableReplica.SameTable(baseline, snapshot));
        Assert.True(VenttiTableReplica.SameTable(baseline, State(999)));
        snapshot.Stake = 0;
        snapshot.Outcome = VenttiTableState.None;
        Assert.Equal(1251f, guest.Current!.Stake);
        Assert.Equal(VenttiTableState.Lose, guest.Current.Outcome);
        Assert.Equal(1250.75f, baseline.Stake);
    }

    [Fact]
    public void BothHandTotalsAndSmallStakeChangesRequireANewBroadcast()
    {
        var baseline = State(1);
        var changed = State(2, 1250.875f);
        Assert.False(VenttiTableReplica.SameTable(baseline, changed));
        changed = State(2);
        changed.PlayerTotal++;
        Assert.False(VenttiTableReplica.SameTable(baseline, changed));
        changed = State(2);
        changed.HouseTotal++;
        Assert.False(VenttiTableReplica.SameTable(baseline, changed));
    }
}
