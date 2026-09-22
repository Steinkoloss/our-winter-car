using System;
using System.IO;
using WinterMP.Core.Catalog;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TaxiPickupTests
    {
        [Theory]
        [InlineData(false, 10, 10, 0, true)]
        [InlineData(false, 10, 8, 64, true)]
        [InlineData(true, 10, 10, 0, false)]
        [InlineData(false, 10, 7.99f, 0, false)]
        [InlineData(false, 10, 10.01f, 0, false)]
        [InlineData(false, 0, 0, 0, false)]
        [InlineData(false, 10, -1, 0, false)]
        [InlineData(false, 10, 10, 64.01f, false)]
        [InlineData(false, 10, 10, -1, false)]
        public void OnlyLiveRecentDriverPosesNearTheirAcceptedSeatAreUsable(bool dead, float now, float last, float distance, bool expected)
        {
            Assert.Equal(expected, TaxiPickupPolicy.CanUseDriver(dead, now, last, distance));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidPoseOrClockCannotEnablePickup(float invalid)
        {
            Assert.False(TaxiPickupPolicy.CanUseDriver(false, invalid, 10, 0));
            Assert.False(TaxiPickupPolicy.CanUseDriver(false, 10, invalid, 0));
            Assert.False(TaxiPickupPolicy.CanUseDriver(false, 10, 10, invalid));
        }

        [Fact]
        public void ShippedCatalogBindsTheNativeCustomerAndTaxiDriverGlobals()
        {
            var data = SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            Assert.Null(data.TaxiPickupError); Assert.NotNull(data.TaxiPickup);
            Assert.Equal("JOBS/TAXIJOB", data.TaxiPickup!["jobPath"]);
            Assert.Equal("TaxiWalker", data.TaxiPickup["walker"]);
            Assert.Equal("SavePlayerCam", data.TaxiPickup["cameraGlobal"]);
            Assert.Equal("PlayerCurrentVehicle", data.TaxiPickup["vehicleGlobal"]);
            Assert.Equal("Taxi", data.TaxiPickup["vehicleName"]);
        }

        [Theory]
        [InlineData("null")] [InlineData("[]")] [InlineData("{}")]
        public void BrokenPickupConfigurationIsContainedToThePickupAdapter(string value)
        {
            var data = SyncCatalogJson.Parse("{\"taxiPickup\":" + value + "}");
            Assert.Null(data.TaxiPickup); Assert.NotNull(data.TaxiPickupError);
        }
    }
}
