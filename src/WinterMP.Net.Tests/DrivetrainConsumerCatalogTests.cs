using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class DrivetrainConsumerCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Writer(JsonNode json, string name) => json["guestEngineProtection"]!["writers"]!.AsArray().Single(x => x!["fsm"]!.GetValue<string>() == name)!;
        private static void Reject(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!);
            Assert.NotNull(parsed.VehicleDrivetrainWear); Assert.NotNull(parsed.VehicleDifferentialSpeed); Assert.NotEmpty(parsed.Doors);
        }
        [Fact]
        public void FourNativeReadsRequireBothFailureGuardsAndBothOilWriters()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineProtectionError);
            var writers = parsed.GuestEngineProtection!.Writers;
            var transmission = Assert.Single(writers, x => x.Fsm == "Transmission"); Assert.Equal(3, transmission.DrivetrainWearReads.Count);
            Assert.Equal(new[] { 0, 1, 2 }, transmission.DrivetrainWearReads.Select(x => x.Part));
            Assert.Equal("SetFsmInt", Assert.Single(transmission.Actions).ActionType);
            Assert.Equal("BREAKOFF", Assert.Single(transmission.EventActions).Event);
            var automatic = Assert.Single(writers, x => x.Fsm == "3 speed"); Assert.Equal(1, Assert.Single(automatic.DrivetrainWearReads).Part);
            Assert.Equal(2, automatic.Actions.Count); Assert.All(automatic.Actions, x => Assert.Equal("OilLevel", x.TargetScalar));
            var saved = Assert.Single(parsed.GuestEngineProtection.PausedFsms, x => x.Path == "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox");
            Assert.True(saved.BlockExternalIntWrites); Assert.True(saved.BlockExternalFloatWrites);
        }
        [Theory]
        [InlineData("Transmission", 0)] [InlineData("Transmission", 1)] [InlineData("Transmission", 2)] [InlineData("3 speed", 0)]
        public void MissingAndDuplicatedReadersAreRejected(string name, int index)
        {
            var json = Catalog(); Writer(json, name)["drivetrainWear"]!.AsArray().RemoveAt(index); Reject(json);
            json = Catalog(); var reads = Writer(json, name)["drivetrainWear"]!.AsArray(); reads.Add(reads[index]!.DeepClone()); Reject(json);
        }
        [Theory]
        [InlineData("state")] [InlineData("index")] [InlineData("part")] [InlineData("variable")] [InlineData("output")]
        public void RequiredReaderIdentityCannotBeOmittedOrChanged(string key)
        {
            foreach (string name in new[] { "Transmission", "3 speed" })
            {
                var json = Catalog(); Writer(json, name)["drivetrainWear"]![0]!.AsObject().Remove(key); Reject(json);
                json = Catalog(); Writer(json, name)["drivetrainWear"]![0]![key] = key == "index" || key == "part" ? JsonValue.Create(255) : JsonValue.Create("Changed"); Reject(json);
            }
        }
        [Theory]
        [InlineData("actions")] [InlineData("eventActions")]
        public void TransmissionReadersCannotLoseTheirDestructiveActionGuards(string key)
        { var json = Catalog(); Writer(json, "Transmission")[key]!.AsArray().Clear(); Reject(json); }
        [Theory]
        [InlineData(0)] [InlineData(1)]
        public void AutomaticReaderCannotLoseEitherOilGuard(int index)
        { var json = Catalog(); Writer(json, "3 speed")["actions"]!.AsArray().RemoveAt(index); Reject(json); }
        [Theory]
        [InlineData("state")] [InlineData("index")] [InlineData("targetVariable")] [InlineData("targetFsm")] [InlineData("event")]
        public void FailureEventRequiresItsCompleteIdentity(string key)
        { var json = Catalog(); Writer(json, "Transmission")["eventActions"]![0]!.AsObject().Remove(key); Reject(json); }
        [Fact]
        public void FailureEventCannotOverlapAReadOrWrite()
        {
            var json = Catalog(); var action = Writer(json, "Transmission")["eventActions"]![0]!;
            action["state"] = "Gearbox damage"; action["index"] = 5; Reject(json);
        }
        [Theory]
        [InlineData("null")] [InlineData("0")] [InlineData("\"true\"")]
        public void IntegerDestinationProtectionRequiresBooleanMetadata(string value)
        {
            var json = Catalog(); var paused = json["guestEngineProtection"]!["pausedFsms"]!.AsArray().Single(x => x!["path"]!.GetValue<string>() == "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox")!;
            paused["blockExternalIntWrites"] = JsonNode.Parse(value); Reject(json);
        }
        [Theory]
        [InlineData("blockExternalIntWrites", true)] [InlineData("blockExternalIntWrites", false)]
        [InlineData("blockExternalFloatWrites", true)] [InlineData("blockExternalFloatWrites", false)]
        public void ConsumerProjectionRequiresBothSavedDestinationGuards(string key, bool remove)
        {
            var json = Catalog(); var paused = json["guestEngineProtection"]!["pausedFsms"]!.AsArray().Single(x => x!["path"]!.GetValue<string>() == "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox")!.AsObject();
            if (remove) paused.Remove(key); else paused[key] = false; Reject(json);
        }
        [Fact]
        public void ReaderCannotAlsoBeDeclaredAsAPoseWrite()
        {
            var json = Catalog(); Writer(json, "Transmission")["poseActions"] = JsonNode.Parse("[{\"state\":\"Driveshaft\",\"index\":2,\"targetVariable\":\"db_Driveshaft\",\"angleVariable\":\"Wear\"}]"); Reject(json);
        }
    }
}
