using System;
using System.IO;
using WinterMP.Core.Catalog;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class HostPaymentPolicyTests
    {
        [Fact]
        public void CatalogOptsOnlyFirewoodIntoThePaymentGuard()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var payment = Assert.Single(SyncCatalogJson.Parse(json).Controls, x => x.HostPayment != null);
            Assert.Equal("firewood", payment.HostPayment); Assert.Equal("JOBS/HouseWood", payment.PathPrefix);
            Assert.Equal(new[] { "State 1" }, payment.States);
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.Replace("\"hostPayment\": \"firewood\"", "\"hostPayment\": \"unknown\"")));
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.Replace("\"hostPayment\": \"firewood\"", "\"hostPayment\": true")));
        }
        [Theory]
        [InlineData("Wait player", true)] [InlineData("Wait button", true)]
        [InlineData("State 1", false)] [InlineData("State 2", false)] [InlineData("State 3", false)]
        [InlineData("State 4", false)] [InlineData("State 5", false)] [InlineData("Pay for car", false)]
        [InlineData("", false)]
        public void OnlyAnOfferedPaymentCanBeCollected(string state, bool expected) =>
            Assert.Equal(expected, HostPaymentPolicy.CanCollect(true, true, false, state, 500));

        [Theory]
        [InlineData(0f)] [InlineData(-1f)] [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidAmountsCannotCreditTheWallet(float amount) =>
            Assert.False(HostPaymentPolicy.CanCollect(true, true, false, "Wait player", amount));

        [Fact]
        public void ClaimedAndUnavailableOffersAreRefused()
        {
            Assert.False(HostPaymentPolicy.CanCollect(false, true, false, "Wait player", 500));
            Assert.False(HostPaymentPolicy.CanCollect(true, false, false, "Wait player", 500));
            Assert.False(HostPaymentPolicy.CanCollect(true, true, true, "Wait player", 500));
            Assert.True(HostPaymentPolicy.CanCollect(true, true, false, "Wait player", .5f));
        }
    }
}
