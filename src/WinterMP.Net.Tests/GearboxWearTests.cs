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
    public sealed class GearboxWearTests
    {
        private static GearboxWearRequest Request(ushort sequence = 0, byte player = 1) => new GearboxWearRequest {
            VehicleId = 42, PlayerId = player, Sequence = sequence };
        private static bool Receive(VehicleCallbackPolicy policy, GearboxWearRequest request, float now = 0, byte owner = 1)
            => request.Valid && policy.Receive(request.VehicleId, request.PlayerId, request.Sequence, request.PlayerId, owner, false, now);
        [Fact]
        public void OneFailureHasNoGuestSuppliedAmountOrTime()
        {
            var data = PacketCodec.Encode(Request(0x1234)); Assert.Equal(9, data.Length);
            Assert.Equal(204, BitConverter.ToUInt16(data, 0)); Assert.Equal(42u, BitConverter.ToUInt32(data, 2));
            Assert.Equal(1, data[6]); Assert.Equal(0x1234, BitConverter.ToUInt16(data, 7));
            var decoded = Assert.IsType<GearboxWearRequest>(PacketCodec.Decode(data)); Assert.Equal(data, PacketCodec.Encode(decoded));
        }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(10)]
        public void TruncatedAndTrailingPacketsAreRejected(int length)
        { var data = PacketCodec.Encode(Request()); Array.Resize(ref data, length); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(data)); }
        [Theory] [InlineData(0)] [InlineData(255)]
        public void ReservedPlayerIdsCannotRequestWear(byte player)
        {
            var request = Request(0, player); Assert.False(request.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            var data = PacketCodec.Encode(Request()); data[6] = player; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(data));
        }
        [Fact]
        public void VehicleZeroCannotBeSentOrDecoded()
        {
            var request = Request(); request.VehicleId = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            var data = PacketCodec.Encode(Request()); Array.Clear(data, 2, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(data));
        }
        [Fact]
        public void CurrentAuthenticatedOwnerAloneCanConsumeOrdering()
        {
            var p = new VehicleCallbackPolicy(2, 2);
            Assert.False(p.Receive(42, 1, 0, 2, 1, false, 0)); Assert.False(p.Receive(42, 1, 0, 1, 2, false, 0));
            Assert.False(p.Receive(42, 1, 0, 1, 1, true, 0)); Assert.True(Receive(p, Request())); Assert.False(Receive(p, Request(), 5));
        }
        [Fact]
        public void WearBudgetSurvivesHandoffsAndRejectedEventsDoNotReplay()
        {
            var p = new VehicleCallbackPolicy(2, 2);
            Assert.True(Receive(p, Request(10))); Assert.True(Receive(p, Request(11)));
            Assert.False(Receive(p, Request(0, 2), owner: 2)); Assert.False(Receive(p, Request(12)));
            Assert.False(Receive(p, Request(12), 10)); Assert.False(Receive(p, Request(0, 2), 10, 2));
            Assert.False(Receive(p, Request(13), .49f)); Assert.True(Receive(p, Request(14), .5f));
            Assert.False(Receive(p, Request(15), .5f));
        }
        [Fact]
        public void WrappedSequencesRejectStaleAndHalfRangeValues()
        {
            var p = new VehicleCallbackPolicy(2, 2); Assert.True(Receive(p, Request(65535))); Assert.True(Receive(p, Request(0)));
            Assert.False(Receive(p, Request(32768), 1)); Assert.False(Receive(p, Request(65535), 1)); Assert.True(Receive(p, Request(1), 1));
        }
        [Fact]
        public void DepartureForgetsOnlySenderHistoryAndClockRollbackDoesNotRefill()
        {
            var p = new VehicleCallbackPolicy(2, 2); Assert.True(Receive(p, Request(500), 1)); Assert.True(Receive(p, Request(501), 1));
            p.ForgetPlayer(1); Assert.False(Receive(p, Request(), 1)); Assert.False(Receive(p, Request(1), .5f));
            Assert.True(Receive(p, Request(2), 1.5f)); p.Clear(); Assert.True(Receive(p, Request()));
        }
        [Theory] [InlineData(0, 2)] [InlineData(float.NaN, 2)] [InlineData(float.PositiveInfinity, 2)]
        [InlineData(2, 0)] [InlineData(2, float.NaN)] [InlineData(2, float.PositiveInfinity)]
        public void InvalidBudgetsCannotAdmitCallbacks(float burst, float rate)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new VehicleCallbackPolicy(burst, rate));
        [Theory] [InlineData(-1f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
        public void InvalidClockDoesNotSeedHistory(float now)
        { var p = new VehicleCallbackPolicy(2, 2); Assert.False(Receive(p, Request(), now)); Assert.True(Receive(p, Request())); }
        [Fact]
        public void FailureIntentsRequireHostAuthenticationAndReliableOrdering()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxWearRequest, true, false, false, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxWearRequest, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxWearRequest, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.GearboxWearRequest, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.GearboxWearRequest, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.GearboxWearRequest, Channel.ReliableBulk));
        }
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Writer(JsonNode j) => j["guestEngineProtection"]!["writers"]!.AsArray().Single(x => (string?)x!["fsm"] == "Damage")!;
        [Theory] [InlineData("false")] [InlineData("null")] [InlineData("1")] [InlineData("\"true\"")] [InlineData("missing")]
        public void FailureConsumerRequiresTheBooleanObserverTag(string invalid)
        {
            var json = Catalog(); var action = Writer(json)["actions"]![0]!.AsObject();
            if (invalid == "missing") action.Remove("gearboxWear"); else action["gearboxWear"] = JsonNode.Parse(invalid);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!);
            Assert.NotNull(parsed.VehicleDrivetrainWear); Assert.NotEmpty(parsed.Doors);
        }
        [Theory] [InlineData("state")] [InlineData("index")] [InlineData("targetScalar")] [InlineData("targetVariable")]
        public void OnlyTheAuditedFailureWriterCanObserve(string field)
        {
            var json = Catalog(); Writer(json)["actions"]![0]![field] = field == "index" ? JsonValue.Create(5) : JsonValue.Create("Other");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!);
        }
    }
}
