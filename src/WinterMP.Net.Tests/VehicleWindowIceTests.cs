using System;
using System.Linq;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleWindowIceTests
    {
        [Theory]
        [InlineData("CORRIS/", "/Simulation/CarTempCorris")]
        [InlineData("SORBET(190-200psi)/", "/Simulation/CarTempSorbet")]
        [InlineData("JOBS/TAXIJOB/MACHTWAGEN/", "/Simulation/CarTempTaxi")]
        public void ShippedCatalogRetainsEveryAuditedClimateVehicle(string root, string temperature)
        {
            var catalog = WinterMP.Core.Catalog.SyncCatalogJson.Parse(System.IO.File.ReadAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            Assert.NotNull(catalog.VehicleClimate);
            Assert.Contains(root, catalog.VehicleClimate!.PathPrefixes);
            Assert.Contains(temperature, catalog.VehicleClimate.CarTempPathContains);
        }

        [Fact]
        public void AllPanesKeepTheirOwnBytesAfterTheOriginalMessagePrefix()
        {
            var message = new VehicleClimate { VehicleId = 123, OwnerPlayerId = 2, Sequence = 41,
                Frost = 201, Fog = 202, CabinTemp = 203, Ice = 11, IceSideLeft = 22,
                IceSideRight = 33, IceDoorLeft = 44, IceDoorRight = 55, IceRear = 66, IceMask = 63 };
            var bytes = PacketCodec.Encode(message);
            Assert.Equal(28, bytes.Length);
            Assert.Equal(new byte[] { 11, 22, 33, 44, 55, 66, 63 }, bytes.Skip(16).Take(7));
            var copy = Assert.IsType<VehicleClimate>(PacketCodec.Decode(bytes));
            Assert.Equal(201, copy.Frost); Assert.Equal(202, copy.Fog); Assert.Equal(203, copy.CabinTemp);
            Assert.Equal(bytes, PacketCodec.Encode(copy));
            for (int n = 0; n < bytes.Length; n++)
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(n).ToArray()));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
        [InlineData(16)] [InlineData(32)] [InlineData(63)]
        public void AvailableZeroMeansIcedWhileAbsentPanesRemainDistinct(byte mask)
        {
            var message = new VehicleClimate { IceMask = mask };
            var copy = Assert.IsType<VehicleClimate>(PacketCodec.Decode(PacketCodec.Encode(message)));
            Assert.Equal(mask, copy.IceMask); Assert.True(copy.ValidIce);
        }

        [Theory]
        [InlineData(64)] [InlineData(128)] [InlineData(255)]
        public void ReservedAvailabilityBitsFailBothWireBoundaries(byte mask)
        {
            var message = new VehicleClimate { IceMask = mask };
            Assert.False(message.ValidIce);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(message));
            var bytes = PacketCodec.Encode(new VehicleClimate()); bytes[22] = mask;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void AValueWithoutItsAvailabilityBitFailsBothBoundaries(int pane)
        {
            var bytes = PacketCodec.Encode(new VehicleClimate()); bytes[16 + pane] = 123;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            var fields = new[] { "Ice", "IceSideLeft", "IceSideRight", "IceDoorLeft", "IceDoorRight", "IceRear" };
            var message = new VehicleClimate(); typeof(VehicleClimate).GetField(fields[pane])!.SetValue(message, (byte)123);
            Assert.False(message.ValidIce); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(message));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteSourceIsUnavailable(float value)
        { Assert.False(VehicleClimate.TryQuantizeIce(value, out var wire)); Assert.Equal(0, wire); }

        [Theory]
        [InlineData(-float.MaxValue, 0)] [InlineData(-.4f, 0)] [InlineData(0f, 0)]
        [InlineData(.2f, 51)] [InlineData(.5f, 128)] [InlineData(1f, 255)] [InlineData(float.MaxValue, 255)]
        public void CutoffKeepsNativeDirectionAndClampsOnlyTheVisualRange(float value, byte expected)
        { Assert.True(VehicleClimate.TryQuantizeIce(value, out var wire)); Assert.Equal(expected, wire); }

        [Fact]
        public void EveryWireStepSurvivesRepeatedCaptureAndPresentation()
        {
            for (int n = 0; n <= 255; n++)
            { Assert.True(VehicleClimate.TryQuantizeIce(VehicleClimate.DequantizeIce((byte)n), out var wire)); Assert.Equal(n, wire); }
            for (int n = 0; n <= 10000; n++)
            {
                float value = n / 10000f;
                Assert.True(VehicleClimate.TryQuantizeIce(value, out var wire));
                Assert.InRange(Math.Abs(value - VehicleClimate.DequantizeIce(wire)), 0, 0.5f / 255 + 0.000001f);
            }
        }
    }
}
