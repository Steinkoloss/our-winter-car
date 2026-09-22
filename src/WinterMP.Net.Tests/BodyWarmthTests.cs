using System;
using System.Globalization;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class BodyWarmthTests
    {
        [Theory]
        [InlineData(-10f)] [InlineData(0f)] [InlineData(.00001f)] [InlineData(53.875f)] [InlineData(100f)]
        public void ReportAppendsAvailabilityWithoutMovingTheExistingNeed(float warmth)
        {
            var value = new PlayerNeedsReport { PlayerId = 7, Sequence = 65535, BodyTemp = warmth, HasBodyTemp = true,
                Hunger = 1, Fatigue = 2, Thirst = 3, Urine = 4, Stress = 5, Drunk = 6,
                Dirtiness = 7, HasDirtiness = true, PlayerAlco = 8, HasAlco = true };
            var bytes = PacketCodec.Encode(value);
            Assert.Equal(44, bytes.Length); Assert.Equal(warmth, BitConverter.ToSingle(bytes, 19));
            Assert.Equal((ushort)65535, BitConverter.ToUInt16(bytes, 31)); Assert.Equal(1, bytes[43]);
            var copy = Assert.IsType<PlayerNeedsReport>(PacketCodec.Decode(bytes));
            Assert.True(copy.HasBodyTemp); Assert.Equal(warmth, copy.BodyTemp);
            Assert.Equal(bytes, PacketCodec.Encode(copy));
        }

        [Theory]
        [InlineData(-10f)] [InlineData(0f)] [InlineData(61.25f)]
        public void SpawnReusesTheExistingBodyFieldAndAppendsOnlyFlagSemantics(float warmth)
        {
            var spawn = new GuestSpawn { Flags = GuestSpawn.FlagHasSavedNeeds | GuestSpawn.FlagHasSavedBodyTemp, BodyTemp = warmth };
            var bytes = PacketCodec.Encode(spawn);
            // v264 appends a 13-byte clothing recipient/availability payload;
            // all v189 warmth offsets stay unchanged.
            Assert.Equal(108, bytes.Length); Assert.Equal(18, bytes[58]);
            Assert.All(bytes.Skip(95), value => Assert.Equal(0, value));
            Assert.Equal(warmth, BitConverter.ToSingle(bytes, 75));
            var copy = Assert.IsType<GuestSpawn>(PacketCodec.Decode(bytes));
            Assert.True(copy.HasSavedBodyTemp); Assert.Equal(warmth, copy.BodyTemp);
            Assert.Equal(bytes, PacketCodec.Encode(copy));
        }

        [Fact]
        public void UnavailableWarmthIsZeroAndAllTruncationsAreRejected()
        {
            foreach (IMessage message in new IMessage[] { new PlayerNeedsReport(), new GuestSpawn() })
            {
                var bytes = PacketCodec.Encode(message);
                for (int n = 0; n < bytes.Length; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(n).ToArray()));
            }
            Assert.False(((PlayerNeedsReport)PacketCodec.Decode(PacketCodec.Encode(new PlayerNeedsReport()))).HasBodyTemp);
            Assert.False(((GuestSpawn)PacketCodec.Decode(PacketCodec.Encode(new GuestSpawn()))).HasSavedBodyTemp);
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void KnownNonfiniteWarmthIsRejectedAtBothBoundaries(float warmth)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new PlayerNeedsReport { HasBodyTemp = true, BodyTemp = warmth }));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new GuestSpawn { Flags = 18, BodyTemp = warmth }));
            var report = PacketCodec.Encode(new PlayerNeedsReport { HasBodyTemp = true });
            Array.Copy(BitConverter.GetBytes(warmth), 0, report, 19, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(report));
            var spawn = PacketCodec.Encode(new GuestSpawn { Flags = 18 });
            Array.Copy(BitConverter.GetBytes(warmth), 0, spawn, 75, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(spawn));
        }

        [Theory]
        [InlineData(2)] [InlineData(128)] [InlineData(255)]
        public void UnknownReportAvailabilityCannotMasqueradeAsKnown(byte flag)
        {
            var bytes = PacketCodec.Encode(new PlayerNeedsReport()); bytes[43] = flag;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void HiddenWarmthAndSpawnWithoutNeedsCannotRestoreValues()
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new PlayerNeedsReport { BodyTemp = 5 }));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new GuestSpawn { BodyTemp = 5 }));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new GuestSpawn { Flags = 16 }));
            var report = PacketCodec.Encode(new PlayerNeedsReport { HasBodyTemp = true, BodyTemp = 5 }); report[43] = 0;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(report));
            var spawn = PacketCodec.Encode(new GuestSpawn { Flags = 18, BodyTemp = 5 }); spawn[58] = 2;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(spawn));
            spawn[58] = 16; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(spawn));
        }

        [Theory]
        [InlineData(8)] [InlineData(12)] [InlineData(13)] [InlineData(14)]
        [InlineData(15)] [InlineData(16)] [InlineData(17)]
        public void AllLegacyProfileWidthsRetireAmbientWithoutLosingOtherNeeds(int columns)
        {
            var cells = "123,1,2,3,0,0,0,1,11,12,13,14,55,15,16,17,18".Split(',').Take(columns);
            Assert.True(GuestProfile.TryParse(string.Join(",", cells), out ulong id, out var profile));
            Assert.Equal(123ul, id); Assert.Equal(1, profile.Position.X); Assert.Equal(columns >= 12, profile.Needs.Valid);
            Assert.False(profile.Needs.HasBodyTemp); Assert.Equal(0, profile.Needs.BodyTemp);
            Assert.Equal(columns >= 16, profile.Needs.HasDirtiness); Assert.Equal(columns >= 17, profile.Needs.HasAlco);
            Assert.True(GuestProfile.TryParse(profile.Serialize(id), out _, out var copy));
            Assert.False(copy.Needs.HasBodyTemp); Assert.Equal(profile.Needs.Hunger, copy.Needs.Hunger);
            Assert.Equal(profile.Needs.Stress, copy.Needs.Stress); Assert.Equal(profile.Needs.PlayerAlco, copy.Needs.PlayerAlco);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
        public void OptionalNeedsPersistIndependentlyAcrossEveryAvailabilityCombination(int flags)
        {
            var p = new GuestProfile { Needs = new GuestProfile.NeedsSnapshot { Valid = true, Hunger = 7,
                HasDirtiness = (flags & 1) != 0, Dirtiness = 12.75f, HasAlco = (flags & 2) != 0, PlayerAlco = 1.25f,
                HasBodyTemp = (flags & 4) != 0, BodyTemp = (flags & 4) != 0 ? 0.00001f : 0 } };
            var row = p.Serialize(123); Assert.Equal(18, row.Split(',').Length); Assert.Equal("0", row.Split(',')[12]);
            Assert.True(GuestProfile.TryParse(row, out _, out var copy));
            Assert.Equal(p.Needs.HasDirtiness, copy.Needs.HasDirtiness); Assert.Equal(p.Needs.HasAlco, copy.Needs.HasAlco);
            Assert.Equal(p.Needs.HasBodyTemp, copy.Needs.HasBodyTemp); Assert.Equal(p.Needs.BodyTemp, copy.Needs.BodyTemp);
            Assert.Equal(p.Needs.HasAlco ? 1.25f : 0, copy.Needs.PlayerAlco);
            Assert.Equal(row, copy.Serialize(123));
        }

        [Theory]
        [InlineData("")] [InlineData("bad")] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("-Infinity")]
        public void DamagedNewWarmthDoesNotReviveTheLegacyAirSample(string warmth)
        {
            Assert.True(GuestProfile.TryParse("123,1,2,3,0,0,0,1,11,12,13,14,55,15,16,17,18," + warmth, out _, out var p));
            Assert.True(p.Needs.Valid); Assert.False(p.Needs.HasBodyTemp); Assert.Equal(0, p.Needs.BodyTemp);
            Assert.Equal(17, p.Needs.Dirtiness); Assert.Equal(18, p.Needs.PlayerAlco);
        }

        [Fact]
        public void KnownZeroSurvivesAColdLocaleRoundtrip()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var p = new GuestProfile { Needs = new GuestProfile.NeedsSnapshot { Valid = true, HasBodyTemp = true, BodyTemp = 0, Hunger = 1.25f } };
                Assert.True(GuestProfile.TryParse(p.Serialize(123), out _, out var copy));
                Assert.True(copy.Needs.HasBodyTemp); Assert.Equal(0, copy.Needs.BodyTemp); Assert.Equal(1.25f, copy.Needs.Hunger);
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void InvalidWarmthCannotBePersistedAndPoseOnlyRemainsPoseOnly()
        {
            var p = new GuestProfile { Needs = new GuestProfile.NeedsSnapshot { Valid = true, HasBodyTemp = true, BodyTemp = float.NaN } };
            Assert.Throws<ArgumentException>(() => p.Serialize(123));
            p.Needs = default; Assert.Equal(8, p.Serialize(123).Split(',').Length);
        }
    }
}
