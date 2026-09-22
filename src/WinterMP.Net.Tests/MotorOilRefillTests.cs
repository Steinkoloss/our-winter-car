using System;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;
namespace WinterMP.Net.Tests
{
    public sealed class MotorOilRefillTests
    {
        private static MotorOilFillerState State() => new() { Revision=1,Epoch=1,HeadId=2,PanId=3,Oil=2,Contamination=10,Viscosity=.6f };
        [Fact]
        public void WireRoundTripAndEveryTruncation()
        {
            IMessage[] messages={State(),new MotorOilRefillIntent { Epoch=1,BottleId=2,PlayerId=1,Action=1,Sequence=7 }};
            int[] sizes={62,14};
            for(int i=0;i<messages.Length;i++)
            {
                var bytes=PacketCodec.Encode(messages[i]);Assert.Equal(sizes[i],bytes.Length);Assert.Equal(bytes,PacketCodec.Encode(PacketCodec.Decode(bytes)));
                for(int n=0;n<bytes.Length;n++){var cut=new byte[n];Array.Copy(bytes,cut,n);Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(cut));}
            }
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidScalarsAndPoseCannotCrossWire(float bad)
        {
            foreach(int offset in new[]{18,22,26,30,34,58})
            {var bytes=PacketCodec.Encode(State());Array.Copy(BitConverter.GetBytes(bad),0,bytes,offset,4);Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(bytes));}
            Assert.Equal(0,MotorOilRefillPolicy.Amount(bad,2,.1f));Assert.Equal(0,MotorOilRefillPolicy.Amount(4,2,bad));
        }
        [Theory]
        [InlineData(4f,2f,.1f)] [InlineData(.001f,3.6999f,.25f)] [InlineData(.015f,2f,.25f)]
        public void EveryTransferConservesSourceAndTarget(float bottle,float pan,float dt)
        {
            float a=MotorOilRefillPolicy.Amount(bottle,pan,dt);
            Assert.InRange(a,0,Math.Min(bottle,MotorOilRefillPolicy.Capacity-pan));
            Assert.Equal((double)bottle+pan,(double)(bottle-a)+(pan+a),5);
            Assert.InRange(MotorOilRefillPolicy.Mix(.4f,.7f,a),.4f,.7f);
            Assert.InRange(MotorOilRefillPolicy.Mix(.9f,.5f,a),.5f,.9f);
            Assert.Equal(Math.Max(.01f,10-32*a),MotorOilRefillPolicy.Clean(10,a),5);
        }
        [Fact]
        public void CapacityEmptyBottleAndLongFramesCannotCreateOil()
        {
            Assert.Equal(0,MotorOilRefillPolicy.Amount(0,2,.1f));Assert.Equal(0,MotorOilRefillPolicy.Amount(4,3.7f,.1f));
            Assert.Equal(0,MotorOilRefillPolicy.Amount(4,2,0));Assert.Equal(0,MotorOilRefillPolicy.Amount(4,2,-1));
            Assert.Equal(.025f,MotorOilRefillPolicy.Amount(4,2,10));
            Assert.Equal(.01f,MotorOilRefillPolicy.Clean(.02f,.025f));
            Assert.Equal(.5f,MotorOilRefillPolicy.Mix(.499f,.5f,.025f));Assert.Equal(.5f,MotorOilRefillPolicy.Mix(.501f,.5f,.025f));
        }
        [Fact]
        public void OldAndContradictoryStatesAreRejectedButPoseMayRefresh()
        {
            var a=State();var b=State();b.CapPosition.X=3;Assert.True(MotorOilRefillPolicy.Accept(a,b));
            b.Oil=3;Assert.False(MotorOilRefillPolicy.Accept(a,b));b.Revision=2;Assert.True(MotorOilRefillPolicy.Accept(a,b));
            Assert.False(MotorOilRefillPolicy.Accept(b,a));b=State();b.PanId=0;Assert.False(MotorOilRefillPolicy.Valid(b));
            b.HeadId=0;Assert.True(MotorOilRefillPolicy.Valid(b));a.Revision=uint.MaxValue;Assert.True(MotorOilRefillPolicy.Accept(a,State()));
        }
        [Fact]
        public void RejectedAttemptsAreSpentAndSequenceWrapsWithoutReplay()
        {
            var l=new MotorOilIntentLedger();var i=new MotorOilRefillIntent { Epoch=1,BottleId=2,PlayerId=1,Action=1,Sequence=65535 };
            Assert.False(l.Accept(i,false));Assert.False(l.Accept(i,true));i.Sequence=0;Assert.True(l.Accept(i,true));Assert.False(l.Accept(i,true));
            i.Sequence=0x8000;Assert.False(l.Accept(i,true));l.Forget(1);Assert.True(l.Accept(i,true));
        }
        [Fact]
        public void OnlyHostResultsAndAuthenticatedGuestIntentsAreAdmitted()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilFillerState,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilFillerState,false,true,false,true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilFillerState,false,true,true,true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilRefillIntent,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilRefillIntent,true,false,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilRefillIntent,false,true,true,true));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.MotorOilRefillIntent,Channel.UnreliableSequenced));
        }
    }
}
