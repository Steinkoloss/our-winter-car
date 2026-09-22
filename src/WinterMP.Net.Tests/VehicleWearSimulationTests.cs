using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleWearSimulationTests
    {
        [Theory]
        [InlineData(true, false, false, 1, true)]
        [InlineData(true, false, false, 254, true)]
        [InlineData(false, false, false, 1, false)]
        [InlineData(true, true, false, 1, false)]
        [InlineData(true, false, true, 1, false)]
        [InlineData(true, false, false, 0, false)]
        [InlineData(true, false, false, 255, false)]
        public void OnlyHostWearForAnEstablishedGuestSimulatorIsDelegated(bool host, bool savedGuest, bool local, byte owner, bool expected)
            => Assert.Equal(expected, VehicleWearSimulationPolicy.Delegated(host, savedGuest, local, owner));

        [Theory]
        [InlineData(0)]
        [InlineData(180)]
        [InlineData(2000)]
        [InlineData(6500)]
        [InlineData(65535)]
        public void AcceptedStoppedCrankingAndRunningInputsRetainExistingWireUnits(ushort rpm)
        {
            var state = new VehicleState { VehicleId = 91, OwnerPlayerId = 1, Sequence = 0, Rpm = rpm };
            var wire = PacketCodec.Encode(state); Assert.Equal(43, wire.Length);
            var copy = VehicleStateStreamPolicy.Copy((VehicleState)PacketCodec.Decode(wire));
            Assert.Equal(rpm, copy.Rpm); Assert.True(VehicleWearSimulationPolicy.HasSample(copy, 91, 1, true));
            Assert.False(VehicleWearSimulationPolicy.HasSample(copy, 92, 1, true));
            Assert.False(VehicleWearSimulationPolicy.HasSample(copy, 91, 2, true));
            Assert.False(VehicleWearSimulationPolicy.HasSample(copy, 91, 1, false));
        }

        [Fact]
        public void MissingSnapshotsAndMalformedSamplesCannotDriveWear()
        {
            Assert.False(VehicleWearSimulationPolicy.HasSample(null, 91, 1, true));
            foreach (var state in new[] {
                new VehicleState { VehicleId = 91, OwnerPlayerId = 1, Sequence = VehicleState.SnapshotSequence },
                new VehicleState { VehicleId = 0, OwnerPlayerId = 1 },
                new VehicleState { VehicleId = 91, OwnerPlayerId = 1, Flags = 128 } })
                Assert.False(VehicleWearSimulationPolicy.HasSample(state, 91, 1, true));
            foreach (byte owner in new byte[] { 0, 255 })
                Assert.False(VehicleWearSimulationPolicy.HasSample(new VehicleState { VehicleId = 91, OwnerPlayerId = owner }, 91, owner, true));
        }

        [Fact]
        public void RejectedOldAndFormerOwnerSamplesLeaveTheCurrentWearInputIntact()
        {
            var stream = new VehicleStateStreamPolicy();
            var current = new VehicleState { VehicleId = 91, OwnerPlayerId = 1, Sequence = 500, Rpm = 2300 };
            Assert.True(stream.Receive(current, true, false, 1));
            var accepted = VehicleStateStreamPolicy.Copy(current);
            Assert.False(stream.Receive(new VehicleState { VehicleId = 91, OwnerPlayerId = 1, Sequence = 499, Rpm = 65000 }, true, false, 1));
            Assert.Equal(2300, accepted.Rpm);
            Assert.False(VehicleWearSimulationPolicy.HasSample(accepted, 91, 2, true));
            var next = new VehicleState { VehicleId = 91, OwnerPlayerId = 2, Sequence = 0, Rpm = 900 };
            Assert.True(stream.Receive(next, true, false, 2)); Assert.True(VehicleWearSimulationPolicy.HasSample(next, 91, 2, true));
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonArray Readers(JsonNode json) => json["vehicleWearInputs"]!["readers"]!.AsArray();
        private static void Reject(JsonNode json)
        {
            var result = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(result.VehicleWearInputs); Assert.NotEmpty(result.VehicleWearInputsError!);
            Assert.NotNull(result.GuestEngineInputs); Assert.NotNull(result.VehicleTemperature); Assert.NotEmpty(result.Doors);
        }

        [Fact]
        public void ProfileCoversAllSevenNativeRpmReadsAndNoTemperatureOrFuelWriter()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(data.VehicleWearInputsError);
            var rule = data.VehicleWearInputs!;
            Assert.Equal("CORRIS", rule.RootPath); Assert.Equal("CORRIS/Simulation/Engine/Oil", rule.Path);
            Assert.Equal(7, rule.Readers.Count); Assert.All(rule.Readers, r => Assert.Equal("RPM", r.Global));
            Assert.Single(rule.Readers, r => r.Fsm == "Pressure"); Assert.Equal(5, rule.Readers.Count(r => r.Fsm == "Wearing"));
            var oil = Assert.Single(rule.Readers, r => r.Fsm == "Oil");
            Assert.Equal("Oil contamination", oil.State); Assert.Equal(2, oil.Index);
            Assert.Equal("OilContaminationRate", oil.Output); Assert.Equal(250000f, oil.OtherConstant);
            Assert.False(oil.EveryFrame); Assert.Null(oil.OtherVariable);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
        public void OmittingAnyRpmReadRejectsTheWholeProjection(int index)
        { var json = Catalog(); Readers(json).RemoveAt(index); Reject(json); }

        [Theory]
        [InlineData("fsm", "Oil")]
        [InlineData("state", "changed")]
        [InlineData("field", "float3")]
        [InlineData("global", "EngineTemp")]
        [InlineData("operation", "Add")]
        [InlineData("output", "")]
        public void ChangedReaderSignaturesAreIsolated(string field, string value)
        { var json = Catalog(); Readers(json)[0]![field] = value; Reject(json); }

        [Fact]
        public void DuplicateActionOrForeignPathIsRejected()
        {
            var json = Catalog(); var rows = Readers(json); rows[1] = rows[0]!.DeepClone(); Reject(json);
            json = Catalog(); json["vehicleWearInputs"]!["path"] = "SORBET/Engine/Oil"; Reject(json);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("false")]
        [InlineData("[]")]
        [InlineData("{}")]
        public void MalformedProfilesKeepUnrelatedSyncAvailable(string value)
        { var json = Catalog(); json["vehicleWearInputs"] = JsonNode.Parse(value); Reject(json); }

        [Fact]
        public void TimingAndOperandAmbiguityAreRejected()
        {
            var json = Catalog(); Readers(json)[0]!["everyFrame"] = !(bool)Readers(json)[0]!["everyFrame"]!; Reject(json);
            json = Catalog(); Readers(json)[0]!["otherVariable"] = "Math1"; Readers(json)[0]!["otherConstant"] = 0; Reject(json);
        }

        [Fact]
        public void OilInputCannotBecomeRecurringOrReplaceAnotherRead()
        {
            var json = Catalog(); Readers(json)[6]!["everyFrame"] = true; Reject(json);
            json = Catalog(); Readers(json)[0] = Readers(json)[6]!.DeepClone(); Reject(json);
            json = Catalog(); Readers(json).Add(Readers(json)[6]!.DeepClone()); Reject(json);
        }
    }
}
