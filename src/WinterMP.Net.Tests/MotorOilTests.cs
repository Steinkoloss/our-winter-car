using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;
namespace WinterMP.Net.Tests
{
    public sealed class MotorOilTests
    {
        private static MotorOilBottleState Bottle() => new() { ItemId=MotorOilPolicy.ItemId("motormoil11"),NativeId="motormoil11",Revision=1,Fluid=4,Viscosity=.6f,Grade=2 };
        [Fact]
        public void WireLayoutAndEveryTruncation()
        {
            var s=Bottle();var bytes=PacketCodec.Encode(s);Assert.Equal(61,bytes.Length);
            Assert.Equal(bytes,PacketCodec.Encode(PacketCodec.Decode(bytes)));
            using var r=new BinaryReader(new MemoryStream(bytes));
            Assert.Equal((ushort)253,r.ReadUInt16());Assert.Equal(s.ItemId,r.ReadUInt32());Assert.Equal(1u,r.ReadUInt32());
            Assert.Equal((ushort)11,r.ReadUInt16());Assert.Equal("motormoil11",System.Text.Encoding.UTF8.GetString(r.ReadBytes(11)));
            Assert.Equal(4f,r.ReadSingle());Assert.Equal(.6f,r.ReadSingle());Assert.Equal(2,r.ReadByte());Assert.Equal(0,r.ReadByte());
            for(int n=0;n<bytes.Length;n++){var cut=new byte[n];Array.Copy(bytes,cut,n);Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(cut));}
            bytes[32]=2;Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteValuesRejectedOnBothBoundaries(float bad)
        {
            foreach(int offset in new[]{23,27,33,57})
            {var bytes=PacketCodec.Encode(Bottle());Array.Copy(BitConverter.GetBytes(bad),0,bytes,offset,4);Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(bytes));}
            var s=Bottle();s.Fluid=bad;Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(s));
            s=Bottle();s.Viscosity=bad;Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(s));
        }
        [Theory]
        [InlineData("motormoil1")] [InlineData("motormoil10")] [InlineData("motormoil101")]
        [InlineData("motormoil1-1")] [InlineData("motormoil12147483648")] [InlineData("oil1")] [InlineData("motormoil11 ")]
        public void SavedIdentityMustBeCanonical(string native)
        {var s=Bottle();s.NativeId=native;s.ItemId=MotorOilPolicy.ItemId(native);Assert.False(MotorOilPolicy.Valid(s));}
        [Fact]
        public void GradesQuantitiesAndIdentityAreBounded()
        {
            var s=Bottle();for(byte grade=0;grade<3;grade++){s.Grade=grade;Assert.True(MotorOilPolicy.Valid(s));}
            s.Grade=3;Assert.False(MotorOilPolicy.Valid(s));s=Bottle();s.Fluid=4.01f;Assert.False(MotorOilPolicy.Valid(s));
            s.Fluid=-.01f;Assert.False(MotorOilPolicy.Valid(s));s.Fluid=.1f;s.Empty=true;Assert.False(MotorOilPolicy.Valid(s));
            s.Fluid=.019f;Assert.True(MotorOilPolicy.Valid(s));s.Viscosity=10.01f;Assert.False(MotorOilPolicy.Valid(s));
            s=Bottle();s.ItemId++;Assert.False(MotorOilPolicy.Valid(s));s=Bottle();s.Revision=0;Assert.False(MotorOilPolicy.Valid(s));
        }
        [Fact]
        public void ReplaysCannotRestoreFluidOrChangeGradeButMayRefreshCreationPose()
        {
            var full=Bottle();var partial=Bottle();partial.Revision=2;partial.Fluid=1.25f;
            Assert.True(MotorOilPolicy.Accept(full,partial));Assert.False(MotorOilPolicy.Accept(partial,full));
            var same=Bottle();same.Position.X=10;Assert.True(MotorOilPolicy.Accept(full,same));
            same.Fluid=3;Assert.False(MotorOilPolicy.Accept(full,same));same=Bottle();same.Grade=1;Assert.False(MotorOilPolicy.Accept(full,same));
            same=Bottle();same.Viscosity=.5f;Assert.False(MotorOilPolicy.Accept(full,same));
            full.Revision=uint.MaxValue;Assert.True(MotorOilPolicy.Accept(full,Bottle()));
            full=Bottle();same=Bottle();same.Revision=0x80000001;Assert.False(MotorOilPolicy.Accept(full,same));
        }
        [Fact]
        public void MetadataAndAuthorityRemainIsolated()
        {
            var root=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sync-catalog.json")))!;
            var c=SyncCatalogJson.Parse(root.ToJsonString());Assert.Null(c.MotorOilError);Assert.Equal("Type",c.MotorOil!["grade"]);
            root["motorOil"]!["prefix"]="oil";c=SyncCatalogJson.Parse(root.ToJsonString());Assert.Null(c.MotorOil);Assert.NotNull(c.MotorOilError);Assert.NotNull(c.Adverts);
            root["motorOil"]!.AsObject().Remove("prefix");c=SyncCatalogJson.Parse(root.ToJsonString());Assert.Null(c.MotorOil);Assert.NotNull(c.MotorOilError);
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilBottleState,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilBottleState,false,true,false,true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.MotorOilBottleState,false,true,true,true));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.MotorOilBottleState,Channel.UnreliableSequenced));
        }
    }
}
