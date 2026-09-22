using System;
using System.Globalization;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class GuestClothingProfileTests
    {
        private const string Pose = "123,1,2,3,0,0,0,1";
        private const string Needs = ",11,12,13,14,0,15,16,17,18,0";

        [Theory]
        [InlineData("4,2,1")]
        [InlineData("0,0,0")]
        [InlineData("255,255,2")]
        public void ClothingColumnsRoundTripWithoutChangingOlderColumns(string clothing)
        {
            string row = Pose + Needs + "," + clothing;
            Assert.True(GuestProfile.TryParse(row, out ulong id, out var profile));
            Assert.Equal(row, profile.Serialize(id));
        }

        [Fact]
        public void ClothingWithoutNeedsDoesNotAssertZeroNeeds()
        {
            string row = Pose + new string(',', 11) + "4,2,1";
            Assert.True(GuestProfile.TryParse(row, out ulong id, out var profile));
            Assert.False(profile.Needs.Valid);
            Assert.Equal(row, profile.Serialize(id));
        }

        [Theory]
        [InlineData("")]
        [InlineData(",11,12,13,14,0,15,16,17,18,0")]
        public void LegacyProfilesDoNotAcquireAssertedClothing(string suffix)
        {
            Assert.True(GuestProfile.TryParse(Pose + suffix, out ulong id, out var profile));
            Assert.Equal(Pose + suffix, profile.Serialize(id));
        }

        [Theory]
        [InlineData("NaN,2,1")]
        [InlineData("Infinity,2,1")]
        [InlineData("-1,2,1")]
        [InlineData("256,2,1")]
        [InlineData("4,256,1")]
        [InlineData("4,2,3")]
        [InlineData("4.5,2,1")]
        [InlineData("4,,1")]
        [InlineData("4,2")]
        public void InvalidOrPartialClothingIsNotAsserted(string clothing)
        {
            Assert.True(GuestProfile.TryParse(Pose + Needs + "," + clothing, out ulong id, out var profile));
            Assert.Equal(Pose + Needs, profile.Serialize(id));
        }

        [Fact]
        public void RoundTripIsCultureIndependent()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
                ClothingColumnsRoundTripWithoutChangingOlderColumns("4,2,2");
            }
            finally { CultureInfo.CurrentCulture = old; }
        }
    }
}
