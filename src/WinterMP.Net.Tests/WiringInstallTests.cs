using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class WiringInstallTests
    {
        private static WiringInstallRequest Request(uint seq = 1) => new WiringInstallRequest { PlayerId = 1, Token = 123, Sequence = seq, SourceId = 5, ExpectedRevision = 7 };
        private static WiringState State(byte flags = 9, uint revision = 7) => new WiringState { SourceId = 5, Revision = revision, Flags = flags };
        [Fact]
        public void RequestAndReceiptRetainTheirOperationIdentity()
        {
            var r = (WiringInstallRequest)PacketCodec.Decode(PacketCodec.Encode(Request(uint.MaxValue)));
            Assert.Equal((uint)5,r.SourceId); Assert.Equal((uint)7,r.ExpectedRevision); Assert.Equal(uint.MaxValue,r.Sequence); Assert.Equal((ulong)123,r.Token); Assert.Equal((byte)1,r.PlayerId);
            var a = (WiringInstallReceipt)PacketCodec.Decode(PacketCodec.Encode(WiringInstallLedger.Receipt(r,WiringInstallStatus.Accepted)));
            Assert.Equal(r.Token,a.Token); Assert.Equal(r.Sequence,a.Sequence); Assert.Equal(r.SourceId,a.SourceId); Assert.Equal(r.PlayerId,a.PlayerId); Assert.Equal(WiringInstallStatus.Accepted,a.Status);
        }
        [Theory]
        [InlineData(0,7,true,true,true,WiringInstallStatus.Unavailable)]
        [InlineData(9,8,true,true,true,WiringInstallStatus.Stale)]
        [InlineData(3,7,true,true,true,WiringInstallStatus.Installed)]
        [InlineData(9,7,false,true,true,WiringInstallStatus.Unavailable)]
        [InlineData(9,7,true,false,true,WiringInstallStatus.TooFar)]
        [InlineData(9,7,true,true,false,WiringInstallStatus.Busy)]
        [InlineData(1,7,true,true,true,WiringInstallStatus.Busy)]
        [InlineData(9,7,true,true,true,WiringInstallStatus.Pending)]
        public void NativeReadinessRevisionAndReachAllGateInstallation(byte flags,uint revision,bool available,bool nearby,bool ready,WiringInstallStatus expected)
            => Assert.Equal(expected,WiringInstallLedger.Check(Request(),State(flags,revision),available,nearby,ready));
        [Fact]
        public void RepeatsCannotReinstallAWireDestroyedAfterSuccess()
        {
            var ledger = new WiringInstallLedger(); var r = Request();
            Assert.Equal(WiringInstallStatus.Pending,ledger.Begin(r,1,WiringInstallStatus.Pending)!.Status);
            Assert.Equal(WiringInstallStatus.Accepted,ledger.Complete(r,true)!.Status);
            Assert.Equal(WiringInstallStatus.Accepted,ledger.Inspect(r,1,out bool begin)!.Status); Assert.False(begin);
            Assert.Equal(WiringInstallStatus.Stale,WiringInstallLedger.Check(Request(2),State(9,9),true,true,true));
            r.ExpectedRevision++; Assert.Null(ledger.Inspect(r,1,out begin)); Assert.False(begin);
        }
        [Fact]
        public void AuthenticationTokenAndPendingOperationStayBound()
        {
            var ledger = new WiringInstallLedger(); var r = Request();
            Assert.Null(ledger.Begin(r,2,WiringInstallStatus.Pending));
            r.SourceId=6; Assert.Null(ledger.Begin(r,1,WiringInstallStatus.Pending)); r.SourceId=5;
            ledger.Begin(r,1,WiringInstallStatus.Pending);
            Assert.Equal(WiringInstallStatus.Busy,ledger.Inspect(Request(2),1,out bool begin)!.Status);Assert.False(begin);
            var changed=Request();changed.Token++; Assert.Null(ledger.Inspect(changed,1,out begin));Assert.False(begin);
            ledger.ForgetPlayer(1);Assert.Null(ledger.Inspect(changed,1,out begin));Assert.True(begin);
        }
        [Fact]
        public void BusyIsRetriedAndSerialNumbersWrapWithoutAcceptingOldRequests()
        {
            var ledger=new WiringInstallLedger();var r=Request(uint.MaxValue);
            Assert.Equal(WiringInstallStatus.Busy,ledger.Begin(r,1,WiringInstallStatus.Busy)!.Status);
            Assert.Null(ledger.Inspect(r,1,out bool begin));Assert.True(begin);
            ledger.Begin(r,1,WiringInstallStatus.Pending);ledger.Complete(r,true);
            r.Sequence=0;Assert.Null(ledger.Inspect(r,1,out begin));Assert.True(begin);
            ledger.Begin(r,1,WiringInstallStatus.Stale);
            Assert.Null(ledger.Inspect(Request(uint.MaxValue),1,out begin));Assert.False(begin);
        }
        [Fact]
        public void ClientRetriesCopiesAndOnlyMatchingTerminalReceiptsReleaseIt()
        {
            var client=new WiringInstallClient(123);Assert.True(client.TryBegin(1,5,7));
            var r=client.Poll(0)!;r.ExpectedRevision=99;Assert.Null(client.Poll(.1f));
            r=client.Poll(.5f)!;Assert.Equal((uint)7,r.ExpectedRevision);Assert.False(client.TryBegin(1,5,8));
            Assert.False(client.Receive(WiringInstallLedger.Receipt(r,WiringInstallStatus.Pending)));
            var wrong=WiringInstallLedger.Receipt(r,WiringInstallStatus.Accepted);wrong.Token++;Assert.False(client.Receive(wrong));
            Assert.True(client.Receive(WiringInstallLedger.Receipt(r,WiringInstallStatus.Stale)));Assert.Null(client.Poll(1));Assert.True(client.TryBegin(1,5,8));
        }
        [Theory]
        [InlineData(1,9)] [InlineData(5,11)] [InlineData(5,13)] [InlineData(5,8)]
        public void EndpointReadinessIsRestrictedToAnAvailableUninstalledIgnitionWire(uint source,byte flags)
        {
            var s=State(flags);s.SourceId=source;Assert.False(new WiringReplica().Receive(s));Assert.Throws<ProtocolException>(()=>PacketCodec.Encode(s));
        }
        [Fact]
        public void ReadinessChangesRevisionAndSurvivesJoinSnapshots()
        {
            var p=new WiringPublication();var missing=p.Observe(5,1);var ready=p.Observe(5,9);Assert.Equal(missing.Revision+1,ready.Revision);
            Assert.Equal(ready.Revision,p.Observe(5,9).Revision);
            var replica=new WiringReplica();Assert.True(replica.Receive((WiringState)PacketCodec.Decode(PacketCodec.Encode(ready))));Assert.Equal((byte)9,replica.Get(5)!.Flags);
            Assert.Equal(ready.Revision+1,p.Observe(5,3).Revision);
        }
        [Fact]
        public void RequestsAndResultsHaveOppositeAuthenticatedDirections()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringInstallRequest,false,true,true,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringInstallRequest,true,false,false,true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringInstallRequest,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringInstallReceipt,true,true,false,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringInstallReceipt,false,true,true,false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.WiringInstallReceipt,false,true,true,true));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.WiringInstallRequest,Channel.UnreliableSequenced));
        }
        [Theory]
        [InlineData("meshPath")] [InlineData("triggersPath")] [InlineData("prerequisitePath")] [InlineData("toolPath")] [InlineData("firstEndpoint")] [InlineData("missing")]
        public void PhysicalConnectionIdentityCannotSilentlyDrift(string key)
        {
            var json=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sync-catalog.json")))!;
            Assert.Null(SyncCatalogJson.Parse(json.ToJsonString()).GuestEngineInputsError);
            var wire=json["guestEngineInputs"]!["wires"]![4]!;
            if(key=="missing")wire.AsObject().Remove("connection");else wire["connection"]![key]="Wrong";
            Assert.NotNull(SyncCatalogJson.Parse(json.ToJsonString()).GuestEngineInputsError);
        }
    }
}
