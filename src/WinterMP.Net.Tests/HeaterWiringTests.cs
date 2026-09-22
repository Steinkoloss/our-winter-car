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
    public class HeaterWiringTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sync-catalog.json")))!;
        private static WiringState State(uint id,uint revision,byte flags) => new WiringState { SourceId=id,Revision=revision,Flags=flags };
        [Theory]
        [InlineData(9,"WiringHeatercontrolFusebox")] [InlineData(10,"WiringHeater")] [InlineData(11,"WiringFuseboxWindow")]
        public void IndependentUnboltedCircuitsUseExistingElevenByteLayout(uint id,string name)
        {
            Assert.Equal(name,WiringPolicy.Name(id));Assert.False(WiringPolicy.SupportsBolted(id));Assert.True(WiringPolicy.ProjectsNativeReads(id));
            foreach(byte flags in new byte[]{0,1,3})
            {
                var bytes=PacketCodec.Encode(State(id,uint.MaxValue,flags));Assert.Equal(11,bytes.Length);
                var value=Assert.IsType<WiringState>(PacketCodec.Decode(bytes));Assert.Equal(id,value.SourceId);Assert.Equal(uint.MaxValue,value.Revision);Assert.Equal(flags,value.Flags);
                Assert.True(new WiringReplica().Receive(value));
            }
        }
        [Theory]
        [InlineData(9,5)] [InlineData(9,7)] [InlineData(10,5)] [InlineData(10,7)] [InlineData(11,5)] [InlineData(11,7)]
        public void HeaterCircuitsCannotInventBoltedState(uint id,byte flags)
        {Assert.False(new WiringReplica().Receive(State(id,1,flags)));Assert.Throws<ArgumentException>(()=>new WiringPublication().Observe(id,flags));}

        [Fact]
        public void SourceHistoriesStayIndependentAcrossRepairConflictWrapAndReset()
        {
            var replica=new WiringReplica();
            foreach(uint id in new uint[]{9,10,11})Assert.True(replica.Receive(State(id,uint.MaxValue,3)));
            Assert.True(replica.Receive(State(9,0,1)));Assert.Equal((byte)3,replica.Get(10)!.Flags);Assert.Equal((byte)3,replica.Get(11)!.Flags);
            Assert.False(replica.Receive(State(9,uint.MaxValue,3)));Assert.False(replica.Receive(State(9,0,3)));Assert.False(replica.Receive(State(9,0x80000000,3)));
            Assert.True(replica.Receive(State(9,1,3)));var copy=replica.Get(9)!;copy.Flags=0;Assert.Equal((byte)3,replica.Get(9)!.Flags);
            replica.Clear();foreach(uint id in new uint[]{9,10,11})Assert.Null(replica.Get(id));
        }
        [Fact]
        public void OnlyNewCircuitsUseNativeDestinationProjection()
        {
            var profile=SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineInputs!;
            Assert.Equal(new uint[]{9,10,11},profile.Wires.Where(w=>w.ProjectNativeReads).Select(w=>w.Id).ToArray());
            Assert.Equal(11,profile.Wires.Count);Assert.Equal(102,profile.Entries.Count);
            foreach(var wire in profile.Wires)
            {Assert.Equal(WiringPolicy.ProjectsNativeReads(wire.Id),wire.ProjectNativeReads);Assert.Equal(WiringPolicy.Name(wire.Id),wire.Name);}
        }
        [Theory]
        [InlineData(9,null)] [InlineData(10,null)] [InlineData(11,null)]
        [InlineData(9,"false")] [InlineData(10,"false")] [InlineData(11,"false")]
        [InlineData(9,"1")] [InlineData(9,"\"true\"")] [InlineData(9,"null")]
        public void MissingOrInvalidNativeReadPolicyBlocksAdmission(uint id,string? value)
        {
            var json=Catalog();var wire=json["guestEngineInputs"]!["wires"]!.AsArray().Single(w=>w!["id"]!.GetValue<uint>()==id)!.AsObject();
            if(value==null)wire.Remove("projectNativeReads");else wire["projectNativeReads"]=JsonNode.Parse(value);
            var parsed=SyncCatalogJson.Parse(json.ToJsonString());Assert.Null(parsed.GuestEngineInputs);Assert.NotNull(parsed.GuestEngineProtection);Assert.NotEmpty(parsed.Doors);
        }
        [Theory]
        [InlineData(9,"path","\"CORRIS/Other\"")] [InlineData(10,"name","\"WiringHeatercontrolFusebox\"")]
        [InlineData(11,"supportsBolted","true")] [InlineData(9,"settledState","\"Load game\"")]
        [InlineData(9,"id","12")] [InlineData(1,"projectNativeReads","true")]
        public void NewSourcesCannotChangeIdentityLoadBoundaryOrOlderProjectionPolicy(uint id,string field,string value)
        {
            var json=Catalog();var wire=json["guestEngineInputs"]!["wires"]!.AsArray().Single(w=>w!["id"]!.GetValue<uint>()==id)!;
            wire[field]=JsonNode.Parse(value);Assert.Null(SyncCatalogJson.Parse(json.ToJsonString()).GuestEngineInputs);
        }
    }
}
