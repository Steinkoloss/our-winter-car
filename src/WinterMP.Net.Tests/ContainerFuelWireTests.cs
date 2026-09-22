using System;
using System.Linq;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class ContainerFuelWireTests
    {
        [Fact]
        public void IntentAndResultUseDocumentedOrderAndStrictPayloadLengths()
        {
            var intent=new ContainerFuelIntent { SourceId=10,VehicleId=20,PlayerId=2,Sequence=3,Amount=.25f };
            byte[] bytes=PacketCodec.Encode(intent); Assert.Equal(19,bytes.Length);
            var r=new NetReader(bytes); Assert.Equal(267,r.ReadUInt16());Assert.Equal(10u,r.ReadUInt32());
            Assert.Equal(20u,r.ReadUInt32());Assert.Equal(2,r.ReadByte());Assert.Equal(3u,r.ReadUInt32());
            Assert.Equal(.25f,r.ReadSingle());r.RequireEnd();
            var result=new ContainerFuelResult { SourceId=10,VehicleId=20,PlayerId=2,Sequence=3,Revision=7,
                AcceptedAmount=.25f,SourceLevel=9.75f,DestinationLevel=20.25f };
            bytes=PacketCodec.Encode(result);Assert.Equal(31,bytes.Length);r=new NetReader(bytes);
            Assert.Equal(268,r.ReadUInt16());Assert.Equal(10u,r.ReadUInt32());Assert.Equal(20u,r.ReadUInt32());
            Assert.Equal(2,r.ReadByte());Assert.Equal(3u,r.ReadUInt32());Assert.Equal(7u,r.ReadUInt32());
            Assert.Equal(.25f,r.ReadSingle());Assert.Equal(9.75f,r.ReadSingle());Assert.Equal(20.25f,r.ReadSingle());r.RequireEnd();
            foreach(var message in new IMessage[]{intent,result}) {
                var wire=PacketCodec.Encode(message);
                for(int length=0;length<wire.Length;length++)
                    Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(wire.Take(length).ToArray()));
                Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(wire.Concat(new byte[]{0}).ToArray()));
            }
        }
        [Fact]
        public void AppendOnlySnapshotBarrierAndExactFuelSurviveWireAndOwnershipCopies()
        {
            var source=new FluidContainerState { ItemId=10,Level=9.75f,Capacity=20,FuelRevision=17 };
            var bytes=PacketCodec.Encode(source);Assert.Equal(22,bytes.Length);
            Assert.Equal(17u,BitConverter.ToUInt32(bytes,18));
            var state=new VehicleState { VehicleId=20,OwnerPlayerId=0,Sequence=VehicleState.SnapshotSequence,
                FuelLevel=129,FuelRevision=17,FuelLiters=20.25f };
            bytes=PacketCodec.Encode(state);Assert.Equal(43,bytes.Length);
            Assert.Equal(17u,BitConverter.ToUInt32(bytes,35));Assert.Equal(20.25f,BitConverter.ToSingle(bytes,39));
            var copy=VehicleStateStreamPolicy.Copy((VehicleState)PacketCodec.Decode(bytes));
            Assert.Equal(17u,copy.FuelRevision);Assert.Equal(20.25f,copy.FuelLiters);
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1f)]
        public void InvalidAbsoluteVehicleFuelIsRejectedOnBothReadAndWrite(float invalid)
        {
            var state=new VehicleState { VehicleId=20,FuelRevision=1,FuelLiters=invalid };
            Assert.False(VehicleStateStreamPolicy.IsValid(state));
            Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(state));
            state.FuelLiters=20;var wire=PacketCodec.Encode(state);
            Array.Copy(BitConverter.GetBytes(invalid),0,wire,39,4);
            Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(wire));
        }
    }
}
