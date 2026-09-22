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
    public class StarterWearTests
    {
        private static StarterWearRequest Request(ushort sequence = 1, float seconds = .25f) => new StarterWearRequest {
            VehicleId = 42, PlayerId = 1, Sequence = sequence, Seconds = seconds };

        [Theory]
        [InlineData(0, .001f)] [InlineData(65535, 1f)]
        public void DurationRoundTripsInDocumentedWireOrder(ushort sequence, float seconds)
        {
            var request = Request(sequence, seconds); var bytes = PacketCodec.Encode(request); Assert.Equal(13, bytes.Length);
            var read = Assert.IsType<StarterWearRequest>(PacketCodec.Decode(bytes));
            Assert.Equal(request.VehicleId, read.VehicleId); Assert.Equal(request.PlayerId, read.PlayerId);
            Assert.Equal(sequence, read.Sequence); Assert.Equal(seconds, read.Seconds);
            var fields = new NetReader(bytes); Assert.Equal((ushort)199, fields.ReadUInt16());
            Assert.Equal(42u, fields.ReadUInt32()); Assert.Equal((byte)1, fields.ReadByte());
            Assert.Equal(sequence, fields.ReadUInt16()); Assert.Equal(seconds, fields.ReadSingle());
            for (int length = 0; length < bytes.Length; length++)
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(length).ToArray()));
        }

        [Theory]
        [InlineData(0,1,.1f)] [InlineData(42,0,.1f)] [InlineData(42,255,.1f)]
        [InlineData(42,1,0)] [InlineData(42,1,-.1f)] [InlineData(42,1,1.001f)]
        [InlineData(42,1,float.NaN)] [InlineData(42,1,float.PositiveInfinity)] [InlineData(42,1,float.NegativeInfinity)]
        public void InvalidRequestsFailEncodingDecodingAndAdmission(uint vehicle, byte player, float seconds)
        {
            var r = Request(); r.VehicleId = vehicle; r.PlayerId = player; r.Seconds = seconds;
            Assert.False(r.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(r));
            var writer = new NetWriter(); writer.WriteUInt32(vehicle); writer.WriteByte(player); writer.WriteUInt16(1); writer.WriteSingle(seconds);
            Assert.Throws<ProtocolException>(() => new StarterWearRequest().Read(new NetReader(writer.ToArray())));
            Assert.False(new StarterWearPolicy().Receive(r, player, player, false, 1));
        }

        [Theory]
        [InlineData(2,1,false)] [InlineData(1,2,false)] [InlineData(1,255,false)] [InlineData(1,1,true)]
        public void RejectedActorOwnerAndLocalDriverDoNotConsumeSequence(byte actor, byte owner, bool local)
        {
            var policy = new StarterWearPolicy(); Assert.False(policy.Receive(Request(),actor,owner,local,1));
            Assert.True(policy.Receive(Request(),1,1,false,1));
        }

        [Fact]
        public void DuplicatesConflictsStalePacketsAndReturningOwnersCannotReplay()
        {
            var p = new StarterWearPolicy(); Assert.True(p.Receive(Request(100),1,1,false,1));
            Assert.False(p.Receive(Request(100),1,1,false,2)); Assert.False(p.Receive(Request(100,.5f),1,1,false,2));
            Assert.False(p.Receive(Request(99),1,1,false,2)); Assert.False(p.Receive(Request(101),1,2,false,2));
            Assert.False(p.Receive(Request(100),1,1,false,3)); Assert.True(p.Receive(Request(101),1,1,false,3));
            p.ForgetPlayer(1); Assert.True(p.Receive(Request(1),1,1,false,3));
            p.Clear(); Assert.True(p.Receive(Request(0),1,1,false,3));
        }

        [Fact]
        public void SequenceWrapHalfRangeAndIndependentSenders()
        {
            var p = new StarterWearPolicy(); Assert.True(p.Receive(Request(65535),1,1,false,1));
            Assert.True(p.Receive(Request(0),1,1,false,1)); Assert.False(p.Receive(Request(32768),1,1,false,1));
            var other = Request(0); other.VehicleId = 43; Assert.True(p.Receive(other,1,1,false,1));
            other.PlayerId = 2; Assert.True(p.Receive(other,2,2,false,1));
        }

        [Fact]
        public void TimeBudgetConsumesExcessWithoutReplayingItLater()
        {
            var p = new StarterWearPolicy(); Assert.True(p.Receive(Request(1,1),1,1,false,1));
            Assert.False(p.Receive(Request(2),1,1,false,1)); Assert.False(p.Receive(Request(2),1,1,false,2));
            Assert.True(p.Receive(Request(3),1,1,false,1.25f)); Assert.False(p.Receive(Request(4),1,1,false,.5f));
            Assert.True(p.Receive(Request(5),1,1,false,1.5f));
            Assert.True(p.Receive(Request(6,1),1,1,false,10)); Assert.False(p.Receive(Request(7),1,1,false,10));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1f)]
        public void InvalidHostTimeCannotConsumeSequence(float now)
        {
            var p = new StarterWearPolicy(); Assert.False(p.Receive(Request(),1,1,false,now)); Assert.True(p.Receive(Request(),1,1,false,1));
        }

        [Fact]
        public void OnlyAuthenticatedGuestsMaySendToHostOnOrderedChannel()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.StarterWearRequest,true,false,false,true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.StarterWearRequest,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.StarterWearRequest,false,true,true,true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.StarterWearRequest,Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.StarterWearRequest,Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.StarterWearRequest,Channel.ReliableBulk));
        }

        [Theory]
        [InlineData(null)] [InlineData("false")] [InlineData("1")] [InlineData("\"true\"")]
        public void MissingOrChangedObservationPreventsBatteryInputAdmission(string? annotation)
        {
            var j = Catalog(); var action = Wear(j);
            if (annotation == null) action.Remove("starterWear"); else action["starterWear"] = JsonNode.Parse(annotation);
            var parsed = SyncCatalogJson.Parse(j.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.Doors);
        }

        [Theory]
        [InlineData("index","10")] [InlineData("state","\"Turn key\"")]
        [InlineData("targetVariable","\"db_Battery\"")] [InlineData("actionType","\"AddFsmFloat\"")]
        public void AnnotationCannotSelectAnotherNativeWrite(string field, string value)
        {
            var j = Catalog(); Wear(j)[field] = JsonNode.Parse(value);
            Assert.Null(SyncCatalogJson.Parse(j.ToJsonString()).GuestEngineProtection);
        }
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sync-catalog.json")))!;
        private static JsonObject Wear(JsonNode j) => j["guestEngineProtection"]!["writers"]!.AsArray()
            .Single(w => w!["fsm"]!.GetValue<string>() == "Starter")!["actions"]!.AsArray()
            .Single(a => a!["state"]!.GetValue<string>() == "Fuel Mixture" && a["index"]!.GetValue<int>() == 11)!.AsObject();
    }
}
