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
    public sealed class GearboxOilUseTests
    {
        private static GearboxOilUseRequest Request(ushort sequence = 0, byte phase = 1, byte player = 1) => new GearboxOilUseRequest {
            VehicleId = 42, PlayerId = player, Sequence = sequence, Phase = phase };
        [Theory] [InlineData(1)] [InlineData(3)]
        public void OneCallbackHasNoClientOilAmount(byte phase)
        {
            var bytes = PacketCodec.Encode(Request(0x1234, phase)); Assert.Equal(10, bytes.Length);
            Assert.Equal(203, BitConverter.ToUInt16(bytes, 0)); Assert.Equal(42u, BitConverter.ToUInt32(bytes, 2));
            Assert.Equal(1, bytes[6]); Assert.Equal(0x1234, BitConverter.ToUInt16(bytes, 7)); Assert.Equal(phase, bytes[9]);
            var result = Assert.IsType<GearboxOilUseRequest>(PacketCodec.Decode(bytes)); Assert.Equal(bytes, PacketCodec.Encode(result));
        }
        [Theory] [InlineData(0)] [InlineData(2)] [InlineData(4)] [InlineData(255)]
        public void UnknownPhasesCannotEnterTheWireOrPolicy(byte phase)
        {
            var value = Request(0, phase); Assert.False(value.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(value));
            Assert.False(new GearboxOilUsePolicy().Receive(value, 1, 1, false, 0));
            var bytes = PacketCodec.Encode(Request()); bytes[9] = phase; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory] [InlineData(0)] [InlineData(2)] [InlineData(6)] [InlineData(7)] [InlineData(9)] [InlineData(11)]
        public void TruncatedAndTrailingPacketsAreRejected(int length)
        { var bytes = PacketCodec.Encode(Request()); Array.Resize(ref bytes, length); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes)); }
        [Theory] [InlineData(0)] [InlineData(255)]
        public void PlayerZeroAndNoOwnerCannotRequestUse(byte player)
        { var value = Request(0, 1, player); Assert.False(value.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(value)); }
        [Fact]
        public void VehicleZeroAndForgedIdentitiesCannotConsumeOrdering()
        {
            var p = new GearboxOilUsePolicy(); var bad = Request(); bad.VehicleId = 0; Assert.False(p.Receive(bad, 1, 1, false, 0));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bad));
            Assert.False(p.Receive(Request(), 2, 1, false, 0)); Assert.False(p.Receive(Request(), 1, 2, false, 0));
            Assert.False(p.Receive(Request(), 1, 255, false, 0)); Assert.False(p.Receive(Request(), 1, 1, true, 0));
            Assert.True(p.Receive(Request(), 1, 1, false, 0)); Assert.False(p.Receive(Request(), 1, 1, false, 1));
        }
        [Fact]
        public void WrapStaleAndHalfRangeAreHandledAcrossHandoffs()
        {
            var p = new GearboxOilUsePolicy(); Assert.True(p.Receive(Request(65535), 1, 1, false, 0));
            Assert.True(p.Receive(Request(0, 3), 1, 1, false, .25f));
            Assert.False(p.Receive(Request(32768), 1, 1, false, 1)); Assert.False(p.Receive(Request(65535), 1, 1, false, 1));
            Assert.True(p.Receive(Request(0, 1, 2), 2, 2, false, 1)); Assert.False(p.Receive(Request(0), 1, 1, false, 2));
            Assert.True(p.Receive(Request(1), 1, 1, false, 2));
        }
        [Fact]
        public void VehicleBudgetSurvivesHandoffAndRejectsReplayAfterRefill()
        {
            var p = new GearboxOilUsePolicy();
            for (ushort n = 0; n < 4; n++) Assert.True(p.Receive(Request(n), 1, 1, false, 0));
            Assert.False(p.Receive(Request(4), 1, 1, false, 0)); Assert.False(p.Receive(Request(0, 3, 2), 2, 2, false, 0));
            Assert.False(p.Receive(Request(4), 1, 1, false, 100)); Assert.False(p.Receive(Request(0, 3, 2), 2, 2, false, 100));
            Assert.True(p.Receive(Request(5), 1, 1, false, .25f)); Assert.False(p.Receive(Request(6), 1, 1, false, .25f));
            Assert.True(p.Receive(Request(7), 1, 1, false, 1)); p.Clear(); Assert.True(p.Receive(Request(), 1, 1, false, 0));
        }
        [Theory] [InlineData(-1f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
        public void InvalidClockCannotSeedOrdering(float now)
        { var p = new GearboxOilUsePolicy(); Assert.False(p.Receive(Request(), 1, 1, false, now)); Assert.True(p.Receive(Request(), 1, 1, false, 0)); }
        [Fact]
        public void DepartedPlayerHistoryClearsWithoutRefillingTheVehicleBudget()
        {
            var p = new GearboxOilUsePolicy();
            for (ushort n = 100; n < 104; n++) Assert.True(p.Receive(Request(n), 1, 1, false, 0));
            p.ForgetPlayer(1);
            Assert.False(p.Receive(Request(0), 1, 1, false, 0));
            Assert.False(p.Receive(Request(0), 1, 1, false, 1));
            Assert.True(p.Receive(Request(1), 1, 1, false, .25f));
        }
        [Fact]
        public void RequestsRequireAuthenticatedHostReceptionOnChannelZero()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxOilUseRequest, true, false, false, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxOilUseRequest, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxOilUseRequest, false, true, true, true));
            foreach (Channel channel in new[] { Channel.ReliableOrdered, Channel.UnreliableSequenced, Channel.ReliableBulk })
                Assert.Equal(channel == Channel.ReliableOrdered, SessionMessagePolicy.IsChannelAllowed(MessageId.GearboxOilUseRequest, channel));
        }
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Writer(JsonNode j) => j["guestEngineProtection"]!["writers"]!.AsArray().Single(x => (string?)x!["fsm"] == "3 speed")!;
        [Theory] [InlineData(0)] [InlineData(1)]
        public void BothNativeOilWritersRequireObservationMetadata(int index)
        {
            var json = Catalog(); var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.All(parsed.GuestEngineProtection!.Writers.Single(x => x.Fsm == "3 speed").Actions, x => Assert.True(x.GearboxOilUse));
            foreach (string invalid in new[] { "false", "null", "1", "\"true\"", "missing" })
            {
                json = Catalog(); var action = Writer(json)["actions"]![index]!.AsObject();
                if (invalid == "missing") action.Remove("gearboxOilUse"); else action["gearboxOilUse"] = JsonNode.Parse(invalid);
                parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!);
                Assert.NotNull(parsed.VehicleDrivetrainWear); Assert.NotEmpty(parsed.Doors);
            }
        }
        [Theory] [InlineData("state")] [InlineData("index")] [InlineData("targetScalar")] [InlineData("targetVariable")]
        public void ForeignOilObserverCannotBeDeclared(string key)
        {
            var json = Catalog(); Writer(json)["actions"]![0]![key] = key == "index" ? JsonValue.Create(2) : JsonValue.Create("Other");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!);
        }
    }
}
