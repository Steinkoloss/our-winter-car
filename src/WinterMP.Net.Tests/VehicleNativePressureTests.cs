using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VehicleNativePressureTests
{
    [Theory]
    [InlineData(190f, 190)] [InlineData(230f, 230)]
    [InlineData(0f, 0)] [InlineData(1f, 1)] [InlineData(2.55f, 3)]
    [InlineData(189.49f, 189)] [InlineData(189.5f, 190)]
    [InlineData(254.49f, 254)] [InlineData(254.5f, 255)]
    [InlineData(255f, 255)] [InlineData(300f, 255)]
    [InlineData(-1f, 0)] [InlineData(float.NaN, 0)]
    [InlineData(float.NegativeInfinity, 0)] [InlineData(float.PositiveInfinity, 255)]
    public void NativeKilopascalsKeepTheWireUnit(float native, byte expected)
        => Assert.Equal(expected, VehicleConditionPolicy.EncodeNativeTirePressure(native));

    [Fact]
    public void DefaultNativePressureIsOnePointNineBarOnTheExistingWire()
    {
        var message = new VehicleCondition { Availability = VehicleCondition.AvailableAll, VehicleId = 123, OwnerPlayerId = 1, Sequence = 2,
            TirePressure = VehicleConditionPolicy.EncodeNativeTirePressure(190),
            DrivetrainDamage = 3, HealthFL = 81, HealthFR = 82, HealthRL = 83, HealthRR = 84, Flags = 0x18 };
        var bytes = PacketCodec.Encode(message);
        Assert.Equal(17, bytes.Length); Assert.Equal(190, bytes[9]);
        var received = Assert.IsType<VehicleCondition>(PacketCodec.Decode(bytes));
        Assert.Equal(1.9f, VehicleConditionPolicy.DecodeTirePressure(received.TirePressure));
        Assert.Equal(190f, VehicleConditionPolicy.DecodeNativeTirePressure(received.TirePressure));
        Assert.Equal(bytes, PacketCodec.Encode(received));
    }

    [Fact]
    public void EveryNativeWirePressureSurvivesRecaptureWithoutDrift()
    {
        for (int n = 0; n <= 255; n++)
        {
            byte encoded = (byte)n;
            for (int handoff = 0; handoff < 10; handoff++)
            {
                float native = VehicleConditionPolicy.DecodeNativeTirePressure(encoded);
                Assert.Equal((float)n, native);
                encoded = VehicleConditionPolicy.EncodeNativeTirePressure(native);
                Assert.Equal(n, encoded);
                Assert.Equal(encoded, VehicleConditionPolicy.EncodeTirePressure(native / 100f));
            }
        }
    }

    [Fact]
    public void NativeRoundingHasAtMostHalfAKilopascalError()
    {
        for (int n = 0; n <= 25500; n++)
        {
            float native = n / 100f;
            float received = VehicleConditionPolicy.DecodeNativeTirePressure(VehicleConditionPolicy.EncodeNativeTirePressure(native));
            Assert.InRange(System.Math.Abs(received - native), 0, .500001f);
        }
    }
}
