using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class GearboxOilTests
    {
        private static VehicleDrivetrainWearState State(float oil = 6.3f) => new VehicleDrivetrainWearState {
            VehicleId = 71, Revision = 9, Flags = 1, DriveshaftWear = 88, GearboxWear = 14, RearAxleWear = 7,
            GearboxOilAvailable = true, GearboxOilLevel = oil };
        [Theory]
        [InlineData(-1f)] [InlineData(0f)] [InlineData(.01999f)] [InlineData(.02f)] [InlineData(.02001f)]
        [InlineData(3.15f)] [InlineData(6.29999f)] [InlineData(6.3f)] [InlineData(6.30001f)] [InlineData(100f)]
        [InlineData(float.MinValue)] [InlineData(float.MaxValue)]
        public void AppendedOilPreservesExactNativeValues(float oil)
        {
            var state = State(oil); var bytes = PacketCodec.Encode(state);
            Assert.Equal(28, bytes.Length); Assert.Equal(202, BitConverter.ToUInt16(bytes, 0));
            Assert.Equal(1, bytes[23]); Assert.Equal(oil, BitConverter.ToSingle(bytes, 24));
            Assert.Equal(88f, BitConverter.ToSingle(bytes, 11)); Assert.Equal(14f, BitConverter.ToSingle(bytes, 15));
            Assert.Equal(7f, BitConverter.ToSingle(bytes, 19));
            var received = Assert.IsType<VehicleDrivetrainWearState>(PacketCodec.Decode(bytes));
            Assert.True(state.SameWear(received)); Assert.Equal(bytes, PacketCodec.Encode(received));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidOilCannotEnterWirePublicationOrReplica(float oil)
        {
            var state = State(oil); Assert.False(state.Valid);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Assert.Throws<ArgumentException>(() => new VehicleDrivetrainWearPublication().Observe(state));
            Assert.False(new VehicleDrivetrainWearReplica().Receive(state));
            var bytes = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(oil), 0, bytes, 24, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(2)] [InlineData(128)] [InlineData(255)]
        public void OilAvailabilityIsStrictlyBoolean(byte value)
        {
            var bytes = PacketCodec.Encode(State()); bytes[23] = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void UnavailableOilCannotHideAValueOrOutliveTheWearSource()
        {
            var state = State(); state.GearboxOilAvailable = false; Assert.False(state.Valid);
            var bytes = PacketCodec.Encode(State()); bytes[23] = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            state = State(0); state.Flags = 0; state.DriveshaftWear = state.GearboxWear = state.RearAxleWear = 0;
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            state.GearboxOilAvailable = false; Assert.True(state.Valid);
        }
        [Fact]
        public void OilOnlyChangesWithdrawalsAndRefillsAdvanceTheSameHostRevision()
        {
            var p = new VehicleDrivetrainWearPublication(); var first = p.Observe(State()); p.MarkBroadcast(first.Revision);
            var changed = State(3.15f); var next = p.Observe(changed); Assert.Equal(first.Revision + 1, next.Revision); Assert.True(p.NeedsBroadcast);
            var r = new VehicleDrivetrainWearReplica(); Assert.True(r.Receive(next));
            changed = next.Copy(); changed.GearboxOilLevel = 0; Assert.False(r.Receive(changed));
            changed.GearboxOilAvailable = false; var withdrawn = p.Observe(changed); Assert.True(r.Receive(withdrawn));
            Assert.False(r.Get()!.GearboxOilAvailable); Assert.Equal(1, r.Get()!.Flags); Assert.Equal(88f, r.Get()!.DriveshaftWear);
            Assert.False(r.Receive(next)); var knownEmpty = p.Observe(State(0)); Assert.True(r.Receive(knownEmpty));
            Assert.True(r.Get()!.GearboxOilAvailable); Assert.NotEqual(withdrawn.Revision, knownEmpty.Revision);
            var refill = p.Observe(State()); Assert.True(r.Receive(refill)); Assert.Equal(6.3f, r.Get()!.GearboxOilLevel);
            refill.GearboxOilLevel = 9; r.Get()!.GearboxOilLevel = 10; Assert.Equal(6.3f, r.Get()!.GearboxOilLevel);
            Assert.Equal(6.3f, p.Observe(State()).GearboxOilLevel);
        }
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonObject Automatic(JsonNode json) => json["guestEngineProtection"]!["writers"]!.AsArray().Single(x => (string?)x!["fsm"] == "3 speed")!.AsObject();
        private static void Reject(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!);
            Assert.NotNull(parsed.VehicleDrivetrainWear); Assert.NotEmpty(parsed.Doors);
        }
        [Fact]
        public void OilReadUsesTheAuditedCanonicalScalarAndRetainsBothDrainGuards()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineProtectionError);
            var writer = Assert.Single(parsed.GuestEngineProtection!.Writers, x => x.Fsm == "3 speed"); var read = writer.GearboxOilRead!;
            Assert.NotNull(read); Assert.Equal("Set stall speed", read.State); Assert.Equal(0, read.Index); Assert.Equal(1, read.Part);
            Assert.Equal("OilLevel", read.Scalar); Assert.Equal("db_Gearbox", read.Variable); Assert.Equal("Oil", read.Output);
            Assert.Equal(2, writer.Actions.Count); Assert.All(writer.Actions, x => Assert.Equal("OilLevel", x.TargetScalar));
        }
        [Theory]
        [InlineData("state")] [InlineData("index")] [InlineData("variable")] [InlineData("output")]
        public void OilReaderMetadataCannotBeMissingOrChanged(string field)
        {
            var json = Catalog(); Automatic(json)["gearboxOil"]!.AsObject().Remove(field); Reject(json);
            json = Catalog(); Automatic(json)["gearboxOil"]![field] = field == "index" ? JsonValue.Create(3) : JsonValue.Create("Other"); Reject(json);
        }
        [Theory]
        [InlineData("missing")] [InlineData("no wear")] [InlineData("foreign consumer")] [InlineData("pose overlap")]
        public void OilProjectionRequiresItsCompleteProtectionProfile(string fault)
        {
            var json = Catalog(); var writer = Automatic(json);
            if (fault == "missing") writer.Remove("gearboxOil");
            if (fault == "no wear") writer.Remove("drivetrainWear");
            if (fault == "foreign consumer") writer["fsm"] = "Other";
            if (fault == "pose overlap") writer["poseActions"] = JsonNode.Parse("[{\"state\":\"Set stall speed\",\"index\":0,\"targetVariable\":\"db_Gearbox\",\"angleVariable\":\"Oil\"}]");
            Reject(json);
        }
    }
}
