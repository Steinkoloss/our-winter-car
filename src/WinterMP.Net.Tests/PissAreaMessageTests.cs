using System;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PissAreaMessageTests
    {
        [Fact]
        public void IntentAndAbsoluteAdmissionsRoundTripWithStrictFraming()
        {
            var intent = new PissAreaIntent { Epoch=12,Revision=34,Sequence=56,Admission=ulong.MaxValue,
                Actor=2,Area=5,Action=2,Contribution=.125f };
            byte[] packet=PacketCodec.Encode(intent);
            Assert.Equal(29,packet.Length);
            var read=Assert.IsType<PissAreaIntent>(PacketCodec.Decode(packet));
            Assert.Equal(intent.Epoch,read.Epoch);Assert.Equal(intent.Revision,read.Revision);
            Assert.Equal(intent.Sequence,read.Sequence);Assert.Equal(intent.Admission,read.Admission);
            Assert.Equal(intent.Actor,read.Actor);Assert.Equal(intent.Area,read.Area);
            Assert.Equal(intent.Action,read.Action);Assert.Equal(intent.Contribution,read.Contribution);
            for(int n=0;n<packet.Length;n++) {
                var truncated=new byte[n];Array.Copy(packet,truncated,n);
                Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(truncated));
            }
            var state=new PissAreaState { Sequence=ushort.MaxValue,Scale1=11,Scale2=22,Scale3=33,Scale4=44,Scale5=55,
                Epoch=123,Revision=456,Admissions=new[] {new PissAreaAdmission {Actor=2,Token=1234,HighWater=5678}} };
            byte[] encoded=PacketCodec.Encode(state);
            var copy=Assert.IsType<PissAreaState>(PacketCodec.Decode(encoded));
            Assert.Equal(31,encoded.Length);
            Assert.Equal(state.Epoch,copy.Epoch);Assert.Equal(state.Revision,copy.Revision);
            Assert.Equal(state.Sequence,copy.Sequence);
            Assert.Equal(new byte[]{11,22,33,44,55},new[]{copy.Scale1,copy.Scale2,copy.Scale3,copy.Scale4,copy.Scale5});
            Assert.Equal((ulong)1234,Assert.Single(copy.Admissions).Token);
            Assert.Equal((uint)5678,copy.Admissions[0].HighWater);
            for(int n=0;n<encoded.Length;n++) {
                var truncated=new byte[n];Array.Copy(encoded,truncated,n);
                Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(truncated));
            }
        }
        [Fact]
        public void StainDirectionsAuthenticateTransportAndNeverUseMotionChannels()
        {
            foreach(bool host in new[]{false,true}) foreach(bool authenticated in new[]{false,true})
            foreach(bool selected in new[]{false,true}) foreach(bool complete in new[]{false,true}) {
                Assert.Equal(host && authenticated,SessionMessagePolicy.IsSenderAllowed(MessageId.PissAreaIntent,host,authenticated,selected,complete));
                Assert.Equal(!host && selected && complete,SessionMessagePolicy.IsSenderAllowed(MessageId.PissAreaState,host,authenticated,selected,complete));
            }
            foreach(var id in new[]{MessageId.PissAreaIntent,MessageId.PissAreaState}) {
                Assert.True(SessionMessagePolicy.IsChannelAllowed(id,Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id,Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id,Channel.ReliableBulk));
            }
        }
        [Fact]
        public void OversizedAdmissionListsCannotOverflowTheWireCount()
        {
            Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(new PissAreaState {Admissions=new PissAreaAdmission[255]}));
        }
    }
}
