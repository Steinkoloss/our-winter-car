using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VenttiPropertyTests
{
    private static VenttiPropertyState State(uint seq, byte keys = 7, byte known = 7, byte access = 7) =>
        new() { Sequence = seq, Keys = keys, KnownAccess = known, Access = access };

    [Fact]
    public void EveryOwnershipAndAccessCombinationRoundTripsWithoutLoss()
    {
        for (byte keys = 0; keys <= 7; keys++)
        for (byte known = 0; known <= 7; known++)
        for (byte access = 0; access <= 7; access++)
        {
            if ((access & ~known) != 0) continue;
            var state = State(uint.MaxValue, keys, known, access);
            var copy = Assert.IsType<VenttiPropertyState>(PacketCodec.Decode(PacketCodec.Encode(state)));
            Assert.True(VenttiPropertyReplica.IsValid(copy));
            Assert.True(VenttiPropertyReplica.SameProperties(state, copy));
            Assert.Equal(uint.MaxValue, copy.Sequence);
        }
        Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiPropertyState, Channel.ReliableOrdered));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiPropertyState, Channel.UnreliableSequenced));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiPropertyState, Channel.ReliableBulk));
    }

    [Fact]
    public void CarAndHouseLossRetirePreviouslyGrantedKeysAndAccess()
    {
        var guest = new VenttiPropertyReplica();
        Assert.True(guest.Receive(State(1)));
        Assert.True(guest.Receive(State(2, keys: 1, access: 1)));
        Assert.Equal((byte)1, guest.Current!.Keys);
        Assert.Equal((byte)1, guest.Current.Access);
        Assert.True(guest.Receive(State(3, keys: 0, access: 0)));
        Assert.Equal((byte)0, guest.Current.Keys);
        Assert.Equal((byte)0, guest.Current.Access);
    }

    [Fact]
    public void InactiveAndLateBindingsRetainKnownStateUntilAnExplicitRevocation()
    {
        var guest = new VenttiPropertyReplica();
        guest.Receive(State(1, known: 1, access: 1));
        guest.Receive(State(2, known: 0, access: 0));
        Assert.Equal((byte)1, guest.Current!.KnownAccess);
        Assert.Equal((byte)1, guest.Current.Access);
        guest.Receive(State(3, known: 6, access: 6));
        Assert.Equal((byte)7, guest.Current.KnownAccess);
        Assert.Equal((byte)7, guest.Current.Access);
        guest.Receive(State(4, known: 2, access: 0));
        Assert.Equal((byte)7, guest.Current.KnownAccess);
        Assert.Equal((byte)5, guest.Current.Access);
    }

    [Fact]
    public void StaleSnapshotOrDuplicateCannotUndoANewerLoss()
    {
        var guest = new VenttiPropertyReplica();
        guest.Receive(State(99, keys: 0, access: 0));
        Assert.False(guest.Receive(State(98)));
        Assert.False(guest.Receive(State(99)));
        Assert.Equal((byte)0, guest.Current!.Keys);
        Assert.Equal((byte)0, guest.Current.Access);
    }

    [Theory]
    [InlineData((byte)8, (byte)7, (byte)7)]
    [InlineData((byte)7, (byte)8, (byte)0)]
    [InlineData((byte)7, (byte)1, (byte)2)]
    [InlineData((byte)7, (byte)7, (byte)128)]
    public void InvalidFlagsDoNotPoisonSequenceOrPendingState(byte keys, byte known, byte access)
    {
        var guest = new VenttiPropertyReplica();
        guest.Receive(State(5, keys: 0, access: 0));
        Assert.False(guest.Receive(State(100, keys, known, access)));
        Assert.Equal(5u, guest.Current!.Sequence);
        Assert.True(guest.Receive(State(6)));
    }

    [Fact]
    public void SequenceWrapAndNewSessionBothAcceptTheFirstFreshState()
    {
        var guest = new VenttiPropertyReplica();
        guest.Receive(State(uint.MaxValue));
        Assert.True(guest.Receive(State(0, keys: 0)));
        Assert.False(guest.Receive(State(uint.MaxValue)));
        guest.Clear();
        Assert.Null(guest.Current);
        Assert.True(guest.Receive(State(0, keys: 1, known: 0, access: 0)));
        Assert.Equal((byte)0, guest.Current!.KnownAccess);
    }

    [Fact]
    public void ReceivingAndComparingSnapshotsDoesNotMutateTheSendersBaseline()
    {
        var baseline = State(2, known: 1, access: 1);
        var snapshot = State(3, known: 2, access: 0);
        var guest = new VenttiPropertyReplica();
        guest.Receive(baseline);
        guest.Receive(snapshot);
        Assert.Equal((byte)2, snapshot.KnownAccess);
        Assert.Equal((byte)3, guest.Current!.KnownAccess);
        Assert.False(VenttiPropertyReplica.SameProperties(baseline, snapshot));
        Assert.True(VenttiPropertyReplica.SameProperties(baseline, State(999, known: 1, access: 1)));
        snapshot.Access = 2;
        Assert.Equal((byte)1, guest.Current.Access);
    }
}
