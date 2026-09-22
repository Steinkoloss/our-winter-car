using System.Collections.Generic;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VehicleConditionAvailabilityTests
{
    public static IEnumerable<object[]> Masks()
    {
        for (byte mask = 0; mask <= VehicleCondition.AvailableAll; mask++) yield return new object[] { mask };
    }

    private static VehicleCondition State(byte mask) => new() {
        VehicleId = 91, OwnerPlayerId = 1, Sequence = 4, Availability = mask,
        TirePressure = 190, DrivetrainDamage = 2, HealthFL = 70, HealthFR = 71, HealthRL = 72, HealthRR = 73, Flags = 0x18 };

    [Theory, MemberData(nameof(Masks))]
    public void EveryMaskAppendsAfterTheUnchangedConditionFields(byte mask)
    {
        var state = State(mask); var bytes = PacketCodec.Encode(state);
        Assert.Equal(17, bytes.Length); Assert.Equal(190, bytes[9]); Assert.Equal(2, bytes[10]);
        Assert.Equal(new byte[] { 70, 71, 72, 73, 0x18, mask }, bytes[11..]);
        var copy = Assert.IsType<VehicleCondition>(PacketCodec.Decode(bytes));
        Assert.Equal(bytes, PacketCodec.Encode(copy));
        Assert.Equal(mask, VehicleConditionStreamPolicy.Copy(copy).Availability);
        Assert.Equal((mask & 1) != 0, copy.HasPressure); Assert.Equal((mask & 2) != 0, copy.HasDrivetrain);
        for (int i = 0; i < 4; i++) Assert.Equal((mask & (4 << i)) != 0, copy.HasWheel(i));
        Assert.False(copy.HasWheel(-1)); Assert.False(copy.HasWheel(4));
    }

    [Theory]
    [InlineData(64)] [InlineData(128)] [InlineData(127)] [InlineData(255)]
    public void UnknownMaskBitsFailWireAndPolicyWithoutConsumingHistory(byte invalid)
    {
        var bytes = PacketCodec.Encode(State(0)); bytes[16] = invalid;
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        var state = State(invalid);
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        var policy = new VehicleConditionStreamPolicy();
        Assert.False(policy.Receive(state, true, false, 1)); Assert.True(policy.Receive(State(0), true, false, 1));
    }

    [Fact]
    public void MissingAvailabilityByteIsRejectedAndNewMessagesDefaultToUnknown()
    {
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(PacketCodec.Encode(State(63))[..16]));
        var state = new VehicleCondition(); Assert.Equal(0, state.Availability);
        Assert.False(state.HasPressure); Assert.False(state.HasDrivetrain);
        for (int i = 0; i < 4; i++) Assert.False(state.HasWheel(i));
    }

    [Theory, MemberData(nameof(Masks))]
    public void LiveAndParkedObserversOnlyReadDeclaredFieldsIncludingKnownZero(byte mask)
    {
        var state = State(mask); state.TirePressure = 0; state.HealthFL = state.HealthFR = state.HealthRL = state.HealthRR = 0;
        Assert.Equal(state.HasPressure, VehicleConditionStreamPolicy.TryGetObserverPressure(state, 91, false, 1, out byte pressure));
        Assert.Equal(0, pressure);
        var parked = VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1,
            new ItemTransform { ItemId = 91, OwnerPlayerId = 1, Flags = ItemTransform.FlagFinal });
        Assert.NotNull(parked); Assert.Equal(mask, parked.Availability);
        Assert.Equal(state.HasPressure, VehicleConditionStreamPolicy.TryGetParkedObserverPressure(parked, 91, false, 255, out pressure));
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(state.HasWheel(i), VehicleConditionStreamPolicy.TryGetObserverHealth(state, 91, false, 1, i, out byte health));
            Assert.Equal(0, health);
            Assert.Equal(state.HasWheel(i), VehicleConditionStreamPolicy.TryGetParkedObserverHealth(parked, 91, false, 255, i, out health));
            Assert.Equal(0, health);
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverHealth(state, 91, true, 1, i, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverHealth(parked, 91, false, 2, i, out _));
        }
        state.Availability = 0; Assert.Equal(mask, parked.Availability);
    }

    [Fact]
    public void LosingAndRegainingFieldsNeverResetsTheOwnersSequenceHistory()
    {
        var p = new VehicleConditionStreamPolicy(); var state = State(63); state.Sequence = 65534;
        Assert.True(p.Receive(state, true, false, 1)); state.Availability = 0;
        Assert.False(p.Receive(state, true, false, 1)); state.Sequence = 0; Assert.True(p.Receive(state, true, false, 1));
        state.Availability = 63; Assert.False(p.Receive(state, true, false, 1)); state.Sequence = 1;
        Assert.True(p.Receive(state, true, false, 1)); state.Availability = 0; state.Sequence = 0;
        Assert.False(p.Receive(state, true, false, 1));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)] [InlineData(16)] [InlineData(32)]
    public void ChecksumsIgnoreOnlyMissingFieldsAndDistinguishKnownZero(byte bit)
    {
        uint Hash(VehicleCondition? state) => VehicleChecksum.Fold(StableHash.OffsetBasis, 91, 0, 0, 0, state);
        var unknown = new VehicleCondition(); var zero = new VehicleCondition { Availability = bit };
        Assert.Equal(Hash(null), Hash(unknown)); Assert.NotEqual(Hash(null), Hash(zero));
        var baseline = State((byte)(63 & ~bit)); var changed = VehicleConditionStreamPolicy.Copy(baseline);
        switch (bit)
        {
            case 1: changed.TirePressure = 0; break;
            case 2: changed.DrivetrainDamage = 0; break;
            case 4: changed.HealthFL = 0; changed.Flags ^= 0x11; break;
            case 8: changed.HealthFR = 0; changed.Flags ^= 0x22; break;
            case 16: changed.HealthRL = 0; changed.Flags ^= 0x44; break;
            case 32: changed.HealthRR = 0; changed.Flags ^= 0x88; break;
        }
        Assert.Equal(Hash(baseline), Hash(changed)); baseline.Availability = changed.Availability = 63;
        Assert.NotEqual(Hash(baseline), Hash(changed));
    }
}
