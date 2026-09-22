using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class WheelHealthReadTests
    {
        private static VehicleCondition State(byte owner = 1) => new VehicleCondition { Availability = VehicleCondition.AvailableAll, VehicleId = 12, OwnerPlayerId = owner,
            HealthFL = 0, HealthFR = 70, HealthRL = 100, HealthRR = 255 };

        [Theory]
        [InlineData(0, 0)] [InlineData(1, 70)] [InlineData(2, 100)] [InlineData(3, 255)]
        public void ObserverReadsEachWheelIncludingKnownZeroAndFullByteRange(int wheel, byte expected)
        {
            Assert.True(VehicleConditionStreamPolicy.TryGetObserverHealth(State(), 12, false, 1, wheel, out byte health));
            Assert.Equal(expected, health);
        }

        [Theory]
        [InlineData(0, 0, true)] [InlineData(0, 255, true)] [InlineData(1, 1, true)]
        [InlineData(1, 0, false)] [InlineData(1, 255, false)] [InlineData(0, 1, false)] [InlineData(1, 2, false)]
        public void OnlyCurrentOwnerStateSuppliesObserverHealth(byte source, byte owner, bool allowed)
        {
            Assert.Equal(allowed, VehicleConditionStreamPolicy.TryGetObserverHealth(State(source), 12, false, owner, 1, out byte health));
            Assert.Equal(allowed ? 70 : 0, health);
        }

        [Theory]
        [InlineData(-1)] [InlineData(4)] [InlineData(int.MaxValue)]
        public void UnknownWheelDoesNotReadAnotherTyre(int wheel) =>
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverHealth(State(), 12, false, 1, wheel, out _));

        [Fact]
        public void MissingInvalidWrongCarAndLocalDriverStateCannotProject()
        {
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverHealth(null, 12, false, 1, 1, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverHealth(State(), 13, false, 1, 1, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverHealth(State(), 12, true, 1, 1, out _));
            var state = State(); state.Flags = 17;
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverHealth(state, 12, false, 1, 1, out _));
        }

        [Fact]
        public void HostSnapshotCanProjectWithoutChangingItsSequenceOrContents()
        {
            var state = State(0); state.Sequence = VehicleCondition.SnapshotSequence;
            var before = PacketCodec.Encode(state);
            Assert.True(VehicleConditionStreamPolicy.TryGetObserverHealth(state, 12, false, 255, 2, out byte health));
            Assert.Equal(100, health); Assert.Equal(before, PacketCodec.Encode(state));
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonObject Wheel(JsonNode root) => root["guestEngineProtection"]!["writers"]!.AsArray()
            .Single(w => (string?)w!["fsm"] == "Condition" && ((string?)w["path"])!.EndsWith("WHEELc_FL", StringComparison.Ordinal))!.AsObject();

        [Fact]
        public void AllFourCatalogWheelsHaveDistinctNativeHealthyAndFlatReaders()
        {
            var profile = SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineProtection!;
            var wheels = profile.Writers.Where(w => w.WheelHealthIndex >= 0).OrderBy(w => w.WheelHealthIndex).ToArray();
            Assert.Equal(4, wheels.Length);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(i, wheels[i].WheelHealthIndex); Assert.EndsWith("WHEELc_" + new[] { "FL", "FR", "RL", "RR" }[i], wheels[i].Path);
                Assert.Equal(2, wheels[i].WheelHealthReads.Count);
                Assert.Contains(wheels[i].WheelHealthReads, r => r.State == "State 1" && r.Index == 12);
                Assert.Contains(wheels[i].WheelHealthReads, r => r.State == "Flat friction" && r.Index == 3);
            }
        }

        [Theory]
        [InlineData("null")] [InlineData("[]")] [InlineData("true")] [InlineData("{}")]
        [InlineData("{\"wheel\":4,\"reads\":[]}")]
        [InlineData("{\"wheel\":0,\"reads\":[{\"state\":\"State 1\",\"index\":12}]}")]
        [InlineData("{\"wheel\":0,\"reads\":[{\"state\":\"State 1\",\"index\":11},{\"state\":\"Flat friction\",\"index\":3}]}")]
        [InlineData("{\"wheel\":0,\"reads\":[{\"state\":\"State 1\",\"index\":12},{\"state\":\"State 1\",\"index\":3}]}")]
        public void InvalidReaderMetadataCannotPretendToProtectItsSavedInputs(string value)
        {
            var json = Catalog(); Wheel(json)["wheelHealth"] = JsonNode.Parse(value);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtection); Assert.NotNull(parsed.GuestEngineProtectionError);
            Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.VehicleDamage);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)]
        public void EitherMissingSavedTyreWriterGuardRejectsTheReader(int remove)
        {
            var json = Catalog(); Wheel(json)["actions"]!.AsArray().RemoveAt(remove);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtection); Assert.NotNull(parsed.GuestEngineProtectionError);
        }

        [Fact]
        public void RimPresentationUsesOnlyTheAuditedPhysicsStateAndQuietActionForEveryWheel()
        {
            var wheels = SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineProtection!.Writers.Where(w => w.WheelHealthIndex >= 0).ToArray();
            Assert.Equal(4, wheels.Length);
            foreach (var wheel in wheels)
            {
                var rim = Assert.IsType<WheelRimData>(wheel.WheelRim);
                Assert.Equal("Rim friction", rim.State); Assert.Equal("Wheel", rim.WheelObject);
                Assert.Equal("RimRadius", rim.RadiusVariable); Assert.Equal("FlatSound", rim.SoundVariable);
                Assert.Equal("State 1", rim.QuietState); Assert.Equal(0, rim.RadiusIndex);
                Assert.Equal(1, rim.FrictionIndex); Assert.Equal(0, rim.QuietIndex); Assert.Equal(.1f, rim.Friction);
            }
        }

        [Theory]
        [InlineData("state", "\"State 1\"")] [InlineData("state", "null")]
        [InlineData("wheelObject", "\"FlatSound\"")] [InlineData("radiusVariable", "\"\"")]
        [InlineData("radiusIndex", "1")] [InlineData("frictionIndex", "0")]
        [InlineData("radiusIndex", "-1")] [InlineData("frictionIndex", "2")]
        [InlineData("quietIndex", "256")] [InlineData("quietIndex", "1.5")]
        [InlineData("friction", "0")] [InlineData("friction", "1.1")]
        [InlineData("friction", "null")] [InlineData("friction", "\"0.1\"")]
        public void MalformedRimProfilesCannotReplayUnrelatedActions(string key, string value)
        {
            var json = Catalog(); Wheel(json)["wheelHealth"]!["rim"]![key] = JsonNode.Parse(value);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtection); Assert.NotNull(parsed.GuestEngineProtectionError);
            Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.VehicleDamage);
        }

        [Fact]
        public void MissingRimMetadataKeepsSavedHealthProtectionWithoutInventingAReplayBinding()
        {
            var json = Catalog(); Wheel(json)["wheelHealth"]!.AsObject().Remove("rim");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.NotNull(parsed.GuestEngineProtection); Assert.Null(parsed.GuestEngineProtectionError);
            var wheel = parsed.GuestEngineProtection!.Writers.Single(w => w.WheelHealthIndex == 0);
            Assert.Null(wheel.WheelRim); Assert.Equal(2, wheel.WheelHealthReads.Count); Assert.Equal(2, wheel.Actions.Count);
        }
    }
}
