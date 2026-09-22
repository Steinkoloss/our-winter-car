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
    public sealed class AdvertTests
    {
        private static AdvertJobState Job() => new() { Revision=1, Sheets=30, Stage=2, NextDay=5, Flags=7, Scale=1 };
        [Fact]
        public void JobSheetAndIntentRoundTripEveryTruncation()
        {
            IMessage[] messages={Job(),new AdvertSheetState{ItemId=37},new AdvertIntent{ItemId=37,Sequence=1,ExpectedRevision=1,PlayerId=1,Box=27}};
            int[] sizes={54,34,16};
            for(int i=0;i<messages.Length;i++)
            {
                var bytes=PacketCodec.Encode(messages[i]);Assert.Equal(sizes[i],bytes.Length);Assert.Equal(bytes,PacketCodec.Encode(PacketCodec.Decode(bytes)));
                for(int n=0;n<bytes.Length;n++){var cut=new byte[n];Array.Copy(bytes,cut,n);Assert.Throws<ProtocolException>(()=>PacketCodec.Decode(cut));}
            }
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidNumbersCannotCrossWire(float bad)
        {
            var j=Job();j.Salary=bad;Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(j));
            j=Job();j.Scale=bad;Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(j));
            j=Job();j.Position.X=bad;Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(j));
            j=Job();j.Rotation.W=bad;Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(j));
        }
        [Fact]
        public void ExactMailboxMaskSurvivesReconnectAndRejectsAnotherDelivery()
        {
            var j=Job();j.CompletedMask=(1u<<0)|(1u<<27);j.Sheets=28;j.Delivered=2;
            var after=Assert.IsType<AdvertJobState>(PacketCodec.Decode(PacketCodec.Encode(j)));
            Assert.False(AdvertPolicy.CanDeliver(after,0));Assert.False(AdvertPolicy.CanDeliver(after,27));Assert.True(AdvertPolicy.CanDeliver(after,1));
            Assert.False(AdvertPolicy.CanDeliver(after,28));Assert.Equal(j.CompletedMask,after.CompletedMask);
            j.Sheets=0;Assert.False(AdvertPolicy.CanTake(j));j.Sheets=30;j.Flags=0;Assert.False(AdvertPolicy.CanTake(j));
            j=Job();j.Stage=1;Assert.False(AdvertPolicy.CanTake(j));Assert.False(AdvertPolicy.CanDeliver(j,1));
        }
        [Fact]
        public void NativeResetCanClearMaskWhileSerialOrderingRejectsOldJob()
        {
            Assert.True(AdvertPolicy.Newer(1,uint.MaxValue));Assert.False(AdvertPolicy.Newer(0,uint.MaxValue));
            Assert.False(AdvertPolicy.Newer(3,3));Assert.False(AdvertPolicy.Newer(2,3));Assert.False(AdvertPolicy.Newer(0x80000003,3));
            var a=Job();var b=Job();b.Position.X=10;Assert.True(AdvertPolicy.Same(a,b));b.CompletedMask=1;Assert.False(AdvertPolicy.Same(a,b));
        }
        [Theory]
        [InlineData(3)] [InlineData(4)] [InlineData(6)] [InlineData(255)]
        public void ImpossibleNativeStagesAreRejected(byte stage){var j=Job();j.Stage=stage;Assert.False(AdvertPolicy.Valid(j));}
        [Fact]
        public void MetadataUsesNativeIndexesDespiteDuplicatePaths()
        {
            var root=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sync-catalog.json")))!;
            var c=SyncCatalogJson.Parse(root.ToJsonString());Assert.Null(c.AdvertsError);Assert.Equal(27,c.Adverts!.Boxes.Count);
            Assert.False(c.Adverts.Boxes.ContainsKey(22));Assert.True(c.Adverts.Boxes.Values.Distinct().Count()<27);
            Assert.DoesNotContain(c.Controls,r=>r.PathPrefix=="JOBS/ADs/AdvertSpawn/advert pile(itemx)");
            root["adverts"]!["boxes"]![1]!["index"]=root["adverts"]!["boxes"]![0]!["index"]!.GetValue<int>();
            c=SyncCatalogJson.Parse(root.ToJsonString());Assert.Null(c.Adverts);Assert.NotNull(c.AdvertsError);Assert.NotNull(c.Coffee);
        }
        [Theory]
        [InlineData("boxes")]
        [InlineData("index")]
        public void MissingMailboxMetadataDisablesOnlyAdvertCatalog(string missing)
        {
            var root=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sync-catalog.json")))!;
            var target=missing=="boxes"?root["adverts"]:root["adverts"]!["boxes"]![0];
            target!.AsObject().Remove(missing);
            var catalog=SyncCatalogJson.Parse(root.ToJsonString());
            Assert.Null(catalog.Adverts);Assert.NotNull(catalog.AdvertsError);Assert.NotNull(catalog.Coffee);
        }
        [Fact]
        public void OnlyHostSuppliesResultsAndOnlyAuthenticatedGuestSuppliesIntent()
        {
            foreach(var id in new[]{MessageId.AdvertJobState,MessageId.AdvertSheetState})
            {
                Assert.False(SessionMessagePolicy.IsSenderAllowed(id,true,true,false,true));
                Assert.False(SessionMessagePolicy.IsSenderAllowed(id,false,true,false,true));
                Assert.True(SessionMessagePolicy.IsSenderAllowed(id,false,true,true,true));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id,Channel.UnreliableSequenced));
            }
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertIntent,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertIntent,true,false,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AdvertIntent,false,true,true,true));
        }
    }
}
