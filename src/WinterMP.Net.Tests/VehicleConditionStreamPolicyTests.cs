using System.Reflection;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VehicleConditionStreamPolicyTests
{
    private static VehicleCondition State(byte owner = 1, ushort sequence = 1, uint id = 91) => new() {
        Availability = VehicleCondition.AvailableAll,
        VehicleId = id, OwnerPlayerId = owner, Sequence = sequence, TirePressure = 190,
        DrivetrainDamage = 2, HealthFL = 70, HealthFR = 71, HealthRL = 72, HealthRR = 73, Flags = 0x18 };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(40000)] [InlineData(65534)]
    public void FirstSequenceHasNoInventedBaseline(ushort sequence)
    {
        var p = new VehicleConditionStreamPolicy();
        Assert.True(p.Receive(State(sequence: sequence), true, false, 1));
        Assert.False(p.Receive(State(sequence: sequence), true, false, 1));
    }

    [Theory]
    [InlineData(0, 1, true)] [InlineData(0, 32767, true)] [InlineData(0, 32768, false)]
    [InlineData(1, 1, false)] [InlineData(5, 4, false)] [InlineData(65534, 0, true)]
    public void DuplicateStaleAndHalfRangePacketsCannotAdvanceTheOwner(ushort previous, ushort next, bool expected)
    {
        var p = new VehicleConditionStreamPolicy(); Assert.True(p.Receive(State(sequence: previous), true, false, 1));
        Assert.Equal(expected, p.Receive(State(sequence: next), true, false, 1));
    }

    [Theory]
    [InlineData(true, false, 1, 1, true)] [InlineData(true, false, 1, 2, false)]
    [InlineData(true, false, 255, 1, false)] [InlineData(true, false, 255, 0, false)]
    [InlineData(true, false, 0, 0, false)] [InlineData(true, true, 1, 1, false)]
    [InlineData(false, false, 255, 0, true)] [InlineData(false, false, 0, 0, true)]
    [InlineData(false, false, 1, 1, true)] [InlineData(false, false, 1, 0, false)]
    [InlineData(false, false, 1, 2, false)] [InlineData(false, false, 255, 1, false)]
    [InlineData(false, true, 0, 0, false)] [InlineData(false, true, 1, 1, false)]
    public void OnlyCurrentOwnershipAllowsConditionWrites(bool host, bool local, byte owner, byte sender, bool expected)
        => Assert.Equal(expected, new VehicleConditionStreamPolicy().Receive(State(sender), host, local, owner));

    [Theory]
    [InlineData(true, false, 1, 1, false)] [InlineData(true, false, 255, 0, false)]
    [InlineData(false, false, 255, 0, true)] [InlineData(false, false, 0, 0, true)]
    [InlineData(false, false, 1, 0, false)] [InlineData(false, false, 1, 1, false)]
    [InlineData(false, true, 255, 0, false)] [InlineData(false, false, 255, 1, false)]
    public void OnlyHostSnapshotsForUnownedOrHostOwnedCarsAreAccepted(bool host, bool local, byte owner, byte sender, bool expected)
        => Assert.Equal(expected, new VehicleConditionStreamPolicy().Receive(State(sender, VehicleCondition.SnapshotSequence), host, local, owner));

    [Fact]
    public void SnapshotsDoNotAdvanceLiveHistory()
    {
        var p = new VehicleConditionStreamPolicy();
        Assert.True(p.Receive(State(0, VehicleCondition.SnapshotSequence), false, false, 255));
        Assert.True(p.Receive(State(0, 0), false, false, 255));
        Assert.True(p.Receive(State(0, VehicleCondition.SnapshotSequence), false, false, 255));
        Assert.False(p.Receive(State(0, 0), false, false, 255)); Assert.True(p.Receive(State(0, 1), false, false, 255));
    }

    [Fact]
    public void DriverHandoffsKeepEveryReturningSendersHistory()
    {
        var p = new VehicleConditionStreamPolicy();
        Assert.True(p.Receive(State(1, 100), true, false, 1)); Assert.True(p.Receive(State(2, 0), true, false, 2));
        Assert.False(p.Receive(State(1, 9000), true, false, 2));
        Assert.False(p.Receive(State(1, 99), true, false, 1)); Assert.True(p.Receive(State(1, 101), true, false, 1));
        Assert.False(p.Receive(State(2, 0), true, false, 2)); Assert.True(p.Receive(State(2, 1), true, false, 2));
    }

    [Fact]
    public void RejectionsDoNotConsumeAnotherFutureOwnersHistory()
    {
        var p = new VehicleConditionStreamPolicy();
        Assert.False(p.Receive(State(), true, false, 255)); Assert.False(p.Receive(State(), true, true, 1));
        Assert.True(p.Receive(State(), true, false, 1));
    }

    [Fact]
    public void ReadmissionAndSessionResetClearOnlyTheIntendedHistories()
    {
        var p = new VehicleConditionStreamPolicy();
        Assert.True(p.Receive(State(1, 100), true, false, 1)); Assert.True(p.Receive(State(1, 100, 92), true, false, 1));
        Assert.True(p.Receive(State(2, 100), true, false, 2)); p.ForgetPlayer(1);
        Assert.True(p.Receive(State(1, 0), true, false, 1)); Assert.True(p.Receive(State(1, 0, 92), true, false, 1));
        Assert.False(p.Receive(State(2, 0), true, false, 2)); p.Clear(); Assert.True(p.Receive(State(2, 0), true, false, 2));
    }

    [Theory]
    [InlineData(0x11)] [InlineData(0x22)] [InlineData(0x44)] [InlineData(0x88)] [InlineData(0xff)]
    public void SameWheelCannotBeBothPuncturedAndRimOnly(byte flags)
    {
        var p = new VehicleConditionStreamPolicy(); var invalid = State(); invalid.Flags = flags;
        Assert.False(p.Receive(invalid, true, false, 1)); Assert.True(p.Receive(State(), true, false, 1));
    }

    [Fact]
    public void InvalidIdentityAndNullAreRejectedWithoutPoisoningHistory()
    {
        var p = new VehicleConditionStreamPolicy(); Assert.False(p.Receive(null, true, false, 1));
        Assert.False(p.Receive(State(id: 0), true, false, 1)); Assert.False(p.Receive(State(owner: 255), true, false, 255));
        Assert.True(p.Receive(State(), true, false, 1));
    }

    [Fact]
    public void SnapshotCopiesPreserveTheWholeExistingWirePayloadIndependently()
    {
        var original = State(); var copy = VehicleConditionStreamPolicy.Copy(original);
        foreach (var field in typeof(VehicleCondition).GetFields(BindingFlags.Instance | BindingFlags.Public))
            Assert.Equal(field.GetValue(original), field.GetValue(copy));
        original.HealthFR = 0; original.TirePressure = 0; Assert.Equal(71, copy.HealthFR); Assert.Equal(190, copy.TirePressure);
        var bytes = PacketCodec.Encode(copy); Assert.Equal(17, bytes.Length);
        Assert.Equal(bytes, PacketCodec.Encode(Assert.IsType<VehicleCondition>(PacketCodec.Decode(bytes))));
        Assert.Equal((ushort)0, VehicleStateStreamPolicy.NextSequence(65534));
    }
}
