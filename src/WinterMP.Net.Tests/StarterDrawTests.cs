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
    public class StarterDrawTests
    {
        private static StarterDrawRequest Request(ushort sequence = 1, ushort count = 1) => new StarterDrawRequest {
            VehicleId = 42, PlayerId = 1, Sequence = sequence, Kind = StarterDrawRequest.Loaded, Count = count };

        [Theory]
        [InlineData(1, 1)] [InlineData(1, 512)] [InlineData(2, 1)] [InlineData(2, 512)]
        public void ExactNativeKindAndBoundedCountRoundTrip(byte kind, ushort count)
        {
            var request = Request(65535, count); request.Kind = kind;
            byte[] packet = PacketCodec.Encode(request); Assert.Equal(12, packet.Length);
            var read = Assert.IsType<StarterDrawRequest>(PacketCodec.Decode(packet));
            Assert.Equal(request.VehicleId, read.VehicleId); Assert.Equal(request.PlayerId, read.PlayerId);
            Assert.Equal(request.Sequence, read.Sequence); Assert.Equal(kind, read.Kind); Assert.Equal(count, read.Count);
            var fields = new NetReader(packet); Assert.Equal((ushort)198, fields.ReadUInt16());
            Assert.Equal(42u, fields.ReadUInt32()); Assert.Equal((byte)1, fields.ReadByte());
            Assert.Equal(ushort.MaxValue, fields.ReadUInt16()); Assert.Equal(kind, fields.ReadByte()); Assert.Equal(count, fields.ReadUInt16());
        }

        [Theory]
        [InlineData(0,1,1,1)] [InlineData(42,0,1,1)] [InlineData(42,255,1,1)]
        [InlineData(42,1,0,1)] [InlineData(42,1,3,1)] [InlineData(42,1,1,0)] [InlineData(42,1,1,513)]
        public void InvalidRequestsFailCodecAndPolicy(uint vehicle, byte player, byte kind, ushort count)
        {
            var r = Request(); r.VehicleId = vehicle; r.PlayerId = player; r.Kind = kind; r.Count = count;
            Assert.False(r.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(r));
            var w = new NetWriter(); w.WriteUInt32(vehicle); w.WriteByte(player); w.WriteUInt16(1); w.WriteByte(kind); w.WriteUInt16(count);
            Assert.Throws<ProtocolException>(() => new StarterDrawRequest().Read(new NetReader(w.ToArray())));
            Assert.False(new StarterDrawPolicy().Receive(r, player, player, false, 1));
        }

        [Theory]
        [InlineData(2,1,false)] [InlineData(1,2,false)] [InlineData(1,255,false)] [InlineData(1,1,true)]
        public void WrongActorOwnerOrLocalDriverCannotSpend(byte actor, byte owner, bool local)
        {
            var policy = new StarterDrawPolicy();
            Assert.False(policy.Receive(Request(), actor, owner, local, 1));
            Assert.True(policy.Receive(Request(), 1, 1, false, 1));
        }

        [Fact]
        public void DuplicateConflictStaleAndReturningOwnerCannotReplay()
        {
            var policy = new StarterDrawPolicy(); Assert.True(policy.Receive(Request(100),1,1,false,1));
            Assert.False(policy.Receive(Request(100),1,1,false,2)); var conflict=Request(100);conflict.Kind=2;
            Assert.False(policy.Receive(conflict,1,1,false,2)); Assert.False(policy.Receive(Request(99),1,1,false,2));
            Assert.False(policy.Receive(Request(101),1,2,false,2)); Assert.False(policy.Receive(Request(100),1,1,false,3));
            Assert.True(policy.Receive(Request(101),1,1,false,3));
            policy.ForgetPlayer(1); Assert.True(policy.Receive(Request(1),1,1,false,3));
            policy.Clear(); Assert.True(policy.Receive(Request(0),1,1,false,3));
        }

        [Fact]
        public void SequenceWrapAndIndependentVehiclesRemainValid()
        {
            var p = new StarterDrawPolicy(); Assert.True(p.Receive(Request(65535),1,1,false,1));
            Assert.True(p.Receive(Request(0),1,1,false,1)); Assert.False(p.Receive(Request(32768),1,1,false,1));
            var other=Request(0);other.VehicleId=43;Assert.True(p.Receive(other,1,1,false,1));
            other.PlayerId=2;Assert.True(p.Receive(other,2,2,false,1));
        }

        [Fact]
        public void WorkBudgetConsumesExcessSequenceAndRecoversWithoutDeferredDrain()
        {
            var p=new StarterDrawPolicy(); Assert.True(p.Receive(Request(1,512),1,1,false,1));
            Assert.False(p.Receive(Request(2,1),1,1,false,1));
            Assert.False(p.Receive(Request(2,1),1,1,false,2));
            Assert.True(p.Receive(Request(3,512),1,1,false,1.25f));
            Assert.False(p.Receive(Request(4),1,1,false,.5f));
            Assert.True(p.Receive(Request(5,512),1,1,false,1.5f));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1f)]
        public void InvalidClockCannotConsumeOrApply(float now)
        {
            var p=new StarterDrawPolicy(); Assert.False(p.Receive(Request(),1,1,false,now));
            Assert.True(p.Receive(Request(),1,1,false,1));
        }

        [Fact]
        public void RequestsAreAuthenticatedHostBoundAndReliableOnly()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.StarterDrawRequest,true,false,false,true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.StarterDrawRequest,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.StarterDrawRequest,false,true,true,true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.StarterDrawRequest,Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.StarterDrawRequest,Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.StarterDrawRequest,Channel.ReliableBulk));
        }

        [Theory]
        [InlineData(null)] [InlineData("true")] [InlineData("0")] [InlineData("\"other\"")] [InlineData("\"unloaded\"")]
        public void BatteryAdmissionRequiresCorrectStarterObservation(string? annotation)
        {
            var j=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sync-catalog.json")))!;
            var writer=j["guestEngineProtection"]!["writers"]!.AsArray().Single(w=>w!["fsm"]!.GetValue<string>()=="Starter")!;
            var action=writer["actions"]!.AsArray().Single(a=>a!["state"]!.GetValue<string>()=="Turn key")!.AsObject();
            if(annotation==null)action.Remove("starterDraw");else action["starterDraw"]=JsonNode.Parse(annotation);
            var parsed=SyncCatalogJson.Parse(j.ToJsonString());Assert.Null(parsed.GuestEngineInputs);Assert.NotEmpty(parsed.Doors);
        }
    }
}
