using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleDrivetrainWearTests
    {
        private static readonly float[] Divisors = { 42300, 31500, 22800 };
        private static VehicleState State(float speed) => new VehicleState { VehicleId = 91, OwnerPlayerId = 1, Sequence = 0,
            DifferentialSpeedAvailable = true, DifferentialSpeed = speed };
        [Theory]
        [InlineData(0f)] [InlineData(1f)] [InlineData(-1f)] [InlineData(1.01f)] [InlineData(-1.01f)]
        [InlineData(3150f)] [InlineData(-3150f)] [InlineData(22800f)] [InlineData(-22800f)]
        public void ValidNativeInputKeepsMagnitudeAndAllowsKnownZero(float value)
        {
            var state = State(value); var wear = new[] { 90f, .5f, -1f };
            Assert.True(VehicleDrivetrainWearPolicy.TryInput(state, 91, 1, true, Divisors, wear, out float speed));
            Assert.Equal(Math.Abs(value), speed); Assert.Equal(value, state.DifferentialSpeed);
            Assert.Equal(new[] { 90f, .5f, -1f }, wear);
        }
        [Theory]
        [InlineData(22801f)] [InlineData(-22801f)] [InlineData(float.MaxValue)] [InlineData(float.MinValue)]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void AnyPartExceedingOnePointPerCycleRejectsTheWholeInput(float value)
        {
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(State(value), 91, 1, true, Divisors, new[] { 90f, 90f, 90f }, out float speed));
            Assert.Equal(0, speed);
        }
        [Fact]
        public void MissingStaleForeignAndSnapshotInputsCannotDriveWear()
        {
            var state = State(3150); var wear = new[] { 90f, 90f, 90f };
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(null, 91, 1, true, Divisors, wear, out _));
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(state, 91, 1, false, Divisors, wear, out _));
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(state, 92, 1, true, Divisors, wear, out _));
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(state, 91, 2, true, Divisors, wear, out _));
            state.DifferentialSpeedAvailable = false; state.DifferentialSpeed = 0;
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(state, 91, 1, true, Divisors, wear, out _));
            state.DifferentialSpeedAvailable = true; state.Sequence = VehicleState.SnapshotSequence;
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(state, 91, 1, true, Divisors, wear, out _));
        }
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void EveryNativeTargetAndRateMustBeReady(int index)
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var wear = new[] { 90f, 90f, 90f }; wear[index] = value;
                Assert.False(VehicleDrivetrainWearPolicy.TryInput(State(10), 91, 1, true, Divisors, wear, out _));
            }
            foreach (float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                var rates = (float[])Divisors.Clone(); rates[index] = value;
                Assert.False(VehicleDrivetrainWearPolicy.TryInput(State(10), 91, 1, true, rates, new[] { 90f, 90f, 90f }, out _));
            }
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(State(10), 91, 1, true, new float[2], new float[3], out _));
            Assert.False(VehicleDrivetrainWearPolicy.TryInput(State(10), 91, 1, true, Divisors, new float[4], out _));
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        [Fact]
        public void CatalogKeepsNativeTargetOrderPathsAndDivisors()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(data.VehicleDrivetrainWearError);
            var rule = data.VehicleDrivetrainWear!; Assert.Equal("CORRIS", rule.RootPath);
            Assert.Equal(data.VehicleDifferentialSpeed!.Path, rule.Path); Assert.Equal("Wear", rule.Fsm);
            Assert.Equal(3, rule.Targets.Count);
            string[] names = { "Driveshaft", "Gearbox", "RearAxle" };
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal("db_" + names[i], rule.Targets[i].Variable); Assert.Equal("Rate" + names[i], rule.Targets[i].Rate);
                Assert.Equal(Divisors[i], rule.Targets[i].Divisor); Assert.StartsWith("CORRIS/", rule.Targets[i].Path);
            }
        }
        [Theory]
        [InlineData("rootPath")] [InlineData("path")] [InlineData("fsm")] [InlineData("targets")]
        public void RequiredWearMetadataIsIsolated(string key)
        { var json = Catalog(); json["vehicleDrivetrainWear"]!.AsObject().Remove(key); Reject(json); }
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void EachNativeTargetIsRequiredAndMustBeUnique(int index)
        {
            var json = Catalog(); json["vehicleDrivetrainWear"]!["targets"]!.AsArray().RemoveAt(index); Reject(json);
            json = Catalog(); json["vehicleDrivetrainWear"]!["targets"]![index] = json["vehicleDrivetrainWear"]!["targets"]![(index + 1) % 3]!.DeepClone(); Reject(json);
            foreach (string key in new[] { "variable", "path", "rate", "divisor" })
            { json = Catalog(); json["vehicleDrivetrainWear"]!["targets"]![index]!.AsObject().Remove(key); Reject(json); }
        }
        [Theory]
        [InlineData("null")] [InlineData("[]")] [InlineData("{}")] [InlineData("true")]
        public void MalformedProfileDoesNotDisableTelemetryOrOtherWear(string text)
        { var json = Catalog(); json["vehicleDrivetrainWear"] = JsonNode.Parse(text); Reject(json); }
        private static void Reject(JsonNode json)
        {
            var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.VehicleDrivetrainWear); Assert.NotEmpty(data.VehicleDrivetrainWearError!);
            Assert.NotNull(data.VehicleDifferentialSpeed); Assert.NotNull(data.VehicleWearInputs); Assert.NotNull(data.GuestEngineProtection);
        }
    }
}
