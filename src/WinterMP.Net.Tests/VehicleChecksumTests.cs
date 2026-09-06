using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VehicleChecksumTests
{
    private static VehicleCondition Condition() => new()
    {
        VehicleId = 1234, OwnerPlayerId = 0, Sequence = 17,
        TirePressure = 230, DrivetrainDamage = 2,
        HealthFL = 88, HealthFR = 87, HealthRL = 86, HealthRR = 85,
    };

    private static uint Checksum(uint damage = 0, VehicleCondition? condition = null) =>
        VehicleChecksum.Fold(StableHash.OffsetBasis, 1234, VehicleState.FlagAccOn, 150,
            damage, condition);

    [Fact]
    public void OwnerAndReceiverDamageBookkeepingProduceTheSameParkedChecksum()
    {
        uint broken = VehicleDamage.Bearing1 | VehicleDamage.Block;
        uint host = VehicleDamagePolicy.Reconcile(broken, 0, 0);
        uint receiver = VehicleDamagePolicy.Reconcile(0 | broken, 0, 0);
        Assert.Equal(Checksum(host), Checksum(receiver));

        // A fitted replacement overrides a receiver's stale applied-failure bit;
        // still-unbound parts retain the last authoritative failure across handoff.
        host = VehicleDamagePolicy.Reconcile(broken, VehicleDamage.Bearing1, 0);
        receiver = VehicleDamagePolicy.Reconcile(VehicleDamage.Block | broken, VehicleDamage.Bearing1, 0);
        Assert.Equal(Checksum(VehicleDamage.Block), Checksum(receiver));
        Assert.Equal(Checksum(host), Checksum(receiver));
        Assert.NotEqual(Checksum(broken), Checksum(receiver));
    }

    [Fact]
    public void EveryConcretePartFailureAndRepairChangesTheChecksum()
    {
        for (int bit = 0; bit < VehicleDamage.PartSlots; bit++)
        {
            uint flag = 1u << bit;
            if ((flag & VehicleDamage.ConcretePartsMask) == 0) continue;
            uint damaged = Checksum(flag);
            Assert.NotEqual(Checksum(), damaged);
            Assert.Equal(Checksum(), Checksum(VehicleDamagePolicy.Reconcile(flag, flag, 0)));
        }
        Assert.Equal(Checksum(), Checksum(VehicleDamage.Seize | VehicleDamage.Camfail | (1u << 30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void ParkedConditionDriftIsDetectedAndTheHostSnapshotHealsIt(int change)
    {
        var host = Condition();
        var guest = Assert.IsType<VehicleCondition>(PacketCodec.Decode(PacketCodec.Encode(host)));
        if (change == 0) guest.TirePressure--;
        else if (change == 1) guest.DrivetrainDamage++;
        else if (change == 2) guest.HealthFL--;
        else if (change == 3) guest.HealthFR--;
        else if (change == 4) guest.HealthRL--;
        else if (change == 5) guest.HealthRR--;
        else guest.Flags |= (byte)(1 << (change - 6));
        Assert.NotEqual(Checksum(condition: host), Checksum(condition: guest));

        guest = Assert.IsType<VehicleCondition>(PacketCodec.Decode(PacketCodec.Encode(host)));
        guest.TirePressure = VehicleConditionPolicy.EncodeTirePressure(
            VehicleConditionPolicy.DecodeTirePressure(guest.TirePressure));
        Assert.Equal(Checksum(condition: host), Checksum(condition: guest));
    }

    [Fact]
    public void HandoffSequenceWrapAndOptionalBindingsDoNotChangeConditionChecksum()
    {
        var host = Condition();
        var nextOwner = Condition();
        nextOwner.OwnerPlayerId = 3;
        nextOwner.Sequence = ushort.MaxValue;
        Assert.Equal(Checksum(condition: host), Checksum(condition: nextOwner));
        nextOwner.Sequence = 0;
        Assert.Equal(Checksum(condition: host), Checksum(condition: nextOwner));
        Assert.Equal(Checksum(), Checksum(condition: new VehicleCondition()));
    }

    [Fact]
    public void EveryWirePressureSurvivesRepeatedApplicationAndOwnershipHandoffs()
    {
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            var host = Condition();
            host.TirePressure = (byte)value;
            uint crc = Checksum(condition: host);
            for (int handoff = 0; handoff < 10; handoff++)
            {
                var packet = Assert.IsType<VehicleCondition>(PacketCodec.Decode(PacketCodec.Encode(host)));
                float livePressure = VehicleConditionPolicy.DecodeTirePressure(packet.TirePressure);
                host.TirePressure = VehicleConditionPolicy.EncodeTirePressure(livePressure);
                Assert.Equal((byte)value, host.TirePressure);
                Assert.Equal(crc, Checksum(condition: host));
            }
        }
    }

    [Theory]
    [InlineData(float.NaN, (byte)0)]
    [InlineData(float.NegativeInfinity, (byte)0)]
    [InlineData(-1f, (byte)0)]
    [InlineData(0.014f, (byte)1)]
    [InlineData(0.016f, (byte)2)]
    [InlineData(2.54f, (byte)254)]
    [InlineData(2.55f, (byte)255)]
    [InlineData(float.MaxValue, (byte)255)]
    [InlineData(float.PositiveInfinity, (byte)255)]
    public void PressureQuantizationRoundsAndClamps(float bar, byte expected)
    {
        Assert.Equal(expected, VehicleConditionPolicy.EncodeTirePressure(bar));
    }
}
