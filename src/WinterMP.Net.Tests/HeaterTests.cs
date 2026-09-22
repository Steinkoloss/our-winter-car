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
    public class HeaterTests
    {
        private static HeaterState State(uint revision, byte flags, float wear = 0) => new HeaterState { Revision = revision, Flags = flags, Wear = wear };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Theory]
        [InlineData(0,0)] [InlineData(1,0)] [InlineData(3,-.25f)] [InlineData(3,0)] [InlineData(3,3)] [InlineData(3,97.75f)]
        public void NativeConditionAndAvailabilityUseDocumentedWireOrder(byte flags, float wear)
        {
            var bytes = PacketCodec.Encode(State(uint.MaxValue, flags, wear)); Assert.Equal(12, bytes.Length);
            var state = Assert.IsType<HeaterState>(PacketCodec.Decode(bytes));
            Assert.Equal(uint.MaxValue,state.Revision); Assert.Equal(flags,state.Flags); Assert.Equal(wear,state.Wear);
            var reader = new NetReader(bytes); Assert.Equal((ushort)200,reader.ReadUInt16()); Assert.Equal(uint.MaxValue,reader.ReadUInt32());
            Assert.Equal(flags,reader.ReadByte()); Assert.Equal(wear,reader.ReadSingle()); Assert.Equal((byte)0,reader.ReadByte());
            for (int n=0;n<bytes.Length;n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(n).ToArray()));
        }

        [Theory]
        [InlineData(2,0)] [InlineData(4,0)] [InlineData(7,0)] [InlineData(255,0)] [InlineData(0,90)] [InlineData(1,90)]
        [InlineData(3,float.NaN)] [InlineData(3,float.PositiveInfinity)] [InlineData(3,float.NegativeInfinity)]
        public void UnknownFlagsUnavailableWearAndInvalidScalarsFailAllBoundaries(byte flags, float wear)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(1,flags,wear)));
            var writer = new NetWriter(); writer.WriteUInt32(1); writer.WriteByte(flags); writer.WriteSingle(wear); writer.WriteByte(0);
            Assert.Throws<ProtocolException>(() => new HeaterState().Read(new NetReader(writer.ToArray())));
            Assert.False(new HeaterReplica().Receive(State(1,flags,wear)));
            Assert.Throws<ArgumentException>(() => new HeaterPublication().Observe(flags,wear));
        }

        [Fact]
        public void ReplicaCopiesAndRejectsConflictsStaleAndHalfRangeWithWrap()
        {
            var r=new HeaterReplica();var state=State(uint.MaxValue,3,80);Assert.True(r.Receive(state));state.Wear=1;
            var copy=r.Get()!;copy.Wear=2;Assert.Equal(80,r.Get()!.Wear);
            Assert.True(r.Receive(State(uint.MaxValue,3,80)));Assert.False(r.Receive(State(uint.MaxValue,3,79)));
            Assert.False(r.Receive(State(uint.MaxValue,1)));Assert.True(r.Receive(State(0,3,79)));
            Assert.False(r.Receive(State(uint.MaxValue,3,80)));Assert.False(r.Receive(State(0x80000000,3,80)));
            Assert.True(r.Receive(State(1,0)));r.Clear();Assert.Null(r.Get());Assert.True(r.Receive(State(0,3,80)));
        }

        [Fact]
        public void SnapshotsAndOldAcknowledgementsPreservePendingPublication()
        {
            var p=new HeaterPublication();Assert.False(p.NeedsBroadcast);
            var first=p.Observe(3,80);p.MarkBroadcast(first.Revision);Assert.False(p.NeedsBroadcast);
            var snapshot=p.Observe(3,79);Assert.Equal(first.Revision+1,snapshot.Revision);snapshot.Wear=1;
            var next=p.Observe(3,79);Assert.Equal(snapshot.Revision,next.Revision);Assert.Equal(79,next.Wear);
            p.MarkBroadcast(first.Revision);Assert.True(p.NeedsBroadcast);p.MarkBroadcast(next.Revision);Assert.False(p.NeedsBroadcast);
            var absent=p.Observe(1,0);Assert.Equal(next.Revision+1,absent.Revision);Assert.True(p.NeedsBroadcast);
        }

        [Fact]
        public void HostOnlyStateRequiresHandshakeAndReliableChannel()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.HeaterState,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.HeaterState,false,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.HeaterState,false,true,true,false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.HeaterState,false,true,true,true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.HeaterState,Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.HeaterState,Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.HeaterState,Channel.ReliableBulk));
        }

        [Fact]
        public void CatalogRequiresNativeHeaterSourceWithoutChangingReplacementFamilies()
        {
            var parsed=SyncCatalogJson.Parse(Catalog().ToJsonString());Assert.NotNull(parsed.GuestEngineInputs);
            Assert.Equal("CORRIS/Assemblies/VINP_Heaterbox",parsed.GuestEngineInputs!.Heater!.Path);
            Assert.Equal(new[]{"Installed","Wear"},parsed.GuestEngineInputs.Heater.ReadVariables);
            Assert.Equal(39,parsed.ReplacementParts!.Factories.Count);
        }

        [Theory]
        [InlineData("path","\"CORRIS/Changed\"")] [InlineData("fsm","\"Other\"")]
        [InlineData("idleState","\"Installed\"")] [InlineData("readyState","\"Install 2\"")]
        [InlineData("readVariables","[\"Installed\"]")] [InlineData("readVariables","[\"Installed\",\"Charge\"]")]
        public void ChangedSourceMetadataBlocksInputAdmission(string field,string value)
        {
            var j=Catalog();j["guestEngineInputs"]!["heater"]![field]=JsonNode.Parse(value);
            var parsed=SyncCatalogJson.Parse(j.ToJsonString());Assert.Null(parsed.GuestEngineInputs);Assert.NotEmpty(parsed.Doors);
        }
        [Theory]
        [InlineData("source")] [InlineData("mount")] [InlineData("external writes")] [InlineData("ready state")] [InlineData("removal state")]
        public void MissingSourceOrWriteProtectionBlocksInputAdmission(string fault)
        {
            var j=Catalog();var paused=j["guestEngineProtection"]!["pausedFsms"]!.AsArray();
            var mount=paused.Single(x=>x!["path"]!.GetValue<string>()=="CORRIS/Assemblies/VINP_Heaterbox")!;
            if(fault=="source")j["guestEngineInputs"]!.AsObject().Remove("heater");
            if(fault=="mount")paused.Remove(mount);
            if(fault=="external writes")mount["blockExternalFloatWrites"]=false;
            if(fault=="ready state" || fault=="removal state")
            {var states=mount["requiredStates"]!.AsArray();states.Remove(states.Single(x=>x!.GetValue<string>()==(fault=="ready state"?"Update 2":"Remove part")));}
            Assert.Null(SyncCatalogJson.Parse(j.ToJsonString()).GuestEngineInputs);
        }
    }
}
