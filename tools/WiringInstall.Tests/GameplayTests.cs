using System;
using System.Linq;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]

public class GameplayTests
{
    private readonly ItemWorldSync world=new ItemWorldSync();
    private readonly SessionManager session=new SessionManager();
    public GameplayTests() { SessionManager.Instance=session;Publish(); }
    private WiringInstallRequest Request(uint sequence=1) => new WiringInstallRequest { PlayerId=1,Token=123,Sequence=sequence,SourceId=5,ExpectedRevision=world.Revision() };
    private void Publish() { foreach(var state in world.BuildWiringStates())session.SendWorldMessage(state,Channel.ReliableOrdered); }
    private void Unchanged(uint revision=1)
    {
        Assert.False(world.Data.FsmVariables.FindFsmBool("Installed").Value);
        Assert.False(world.Mesh.activeSelf);Assert.True(world.Triggers.activeSelf);
        Assert.Equal((byte)9,world.Flags());Assert.Equal(revision,world.Revision());
        Assert.Equal((byte)9,session.Guest.Get(5)!.Flags);Assert.Equal(revision,session.Guest.Get(5)!.Revision);
    }
    [Theory]
    [InlineData("actor")] [InlineData("host")] [InlineData("no-owner")] [InlineData("token")]
    [InlineData("source")] [InlineData("range")] [InlineData("stale")]
    [InlineData("host-tool")] [InlineData("other-tool-owner")] [InlineData("far-tool")]
    [InlineData("missing-tool")] [InlineData("wrong-tool-tag")] [InlineData("changed-global-tool")]
    [InlineData("nan-tool")] [InlineData("infinite-tool")]
    public void RejectedRequestsNeverTouchEitherPeer(string failure)
    {
        var request=Request();byte actor=1;
        switch(failure)
        {
            case "actor":actor=2;break;
            case "host":request.PlayerId=actor=0;break;
            case "no-owner":request.PlayerId=actor=255;break;
            case "token":request.Token=0;break;
            case "source":request.SourceId=6;break;
            case "range":world.ActorNear=false;break;
            case "stale":request.ExpectedRevision++;break;
            case "host-tool":world.SharedTool.LocallyOwned=true;break;
            case "other-tool-owner":world.SharedTool.RemoteOwner=2;break;
            case "far-tool":world.Tool.transform.position=new Vector3(9,0,0);break;
            case "nan-tool":world.Tool.transform.position=new Vector3(float.NaN,0,0);break;
            case "infinite-tool":world.Tool.transform.position=new Vector3(float.PositiveInfinity,0,0);break;
            case "missing-tool":world.SharedTool.Body=null;break;
            case "wrong-tool-tag":world.Save.FsmVariables.FindFsmString("UniqueTag")!.Value="other";break;
            case "changed-global-tool":HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmGameObject("WiringTool")!.Value=new GameObject("other");break;
        }
        // Exercise the same decoded message and authenticated actor used by SessionManager.
        world.OnHostWireInstall((WiringInstallRequest)PacketCodec.Decode(PacketCodec.Encode(request)),actor);
        world.Tick();Publish();Unchanged();Assert.Equal(0,world.FinishCount);
    }
    [Theory]
    [InlineData("throw")] [InlineData("timeout")] [InlineData("partial")]
    public void FailedNativeWorkIsRestoredBeforeAnyPublicationAndCanRetry(string fault)
    {
        world.Fault=fault;var request=Request();world.OnHostWireInstall(request,1);
        Publish();Assert.Equal((byte)9,session.Guest.Get(5)!.Flags);
        Time.unscaledTime+=3;world.Tick();Publish();Unchanged();
        Assert.Equal(WiringInstallStatus.Failed,session.Messages.OfType<WiringInstallReceipt>().Last().Status);
        int entered=world.NativeEntries.Count;world.OnHostWireInstall(request,1);Assert.Equal(entered,world.NativeEntries.Count);Unchanged();
        world.Fault="";world.OnHostWireInstall(Request(2),1);world.Tick();Assert.Equal((byte)3,world.Flags());
    }
    [Fact]
    public void PendingCannotBeReplacedOrLeakToAFreshJoin()
    {
        world.Fault="partial";var request=Request();world.OnHostWireInstall(request,1);
        int entered=world.NativeEntries.Count;
        world.OnHostWireInstall(request,1);
        var changed=WiringInstallLedger.Copy(request);changed.ExpectedRevision++;world.OnHostWireInstall(changed,1);
        changed=WiringInstallLedger.Copy(request);changed.Token++;world.OnHostWireInstall(changed,1);
        changed=WiringInstallLedger.Copy(request);changed.Sequence++;world.OnHostWireInstall(changed,1);
        Assert.Equal(entered,world.NativeEntries.Count);
        var join=new WiringReplica();foreach(var state in world.BuildWiringStates())Assert.True(join.Receive(state));
        Assert.Equal((byte)9,join.Get(5)!.Flags);Assert.Equal(request.ExpectedRevision,join.Get(5)!.Revision);
        Time.unscaledTime+=3;world.Tick();Unchanged();
    }
    [Fact]
    public void DestroyedWireCannotBeReinstalledByAcceptedOrStaleReplay()
    {
        var request=Request();world.OnHostWireInstall(request,1);world.Tick();
        world.Data.FsmVariables.FindFsmBool("Installed").Value=false;world.Mesh.SetActive(false);world.Triggers.SetActive(true);
        Publish();uint revision=world.Revision();int entered=world.NativeEntries.Count;
        world.OnHostWireInstall(request,1);Unchanged(revision);Assert.Equal(entered,world.NativeEntries.Count);
        request.Sequence++;world.OnHostWireInstall(request,1);Unchanged(revision);Assert.Equal(entered,world.NativeEntries.Count);
        Assert.Equal(WiringInstallStatus.Stale,session.Messages.OfType<WiringInstallReceipt>().Last().Status);
    }
    [Fact]
    public void MissingColumnLeavesUnavailableEndpointsAndSavedStateUnchanged()
    {
        world.MissingColumn();Publish();var request=Request();
        world.OnHostWireInstall(request,1);world.Tick();
        Assert.False(world.Data.FsmVariables.FindFsmBool("Installed").Value);
        Assert.Equal((byte)1,world.Flags());Assert.Equal((byte)1,session.Guest.Get(5)!.Flags);
        Assert.Equal(request.ExpectedRevision,world.Revision());Assert.Equal(0,world.FinishCount);
        Assert.Equal(WiringInstallStatus.Busy,session.Messages.OfType<WiringInstallReceipt>().Last().Status);
    }
    [Fact]
    public void GuestWithoutAnyVehicleOwnershipRunsOneTwoEndpointHandshakeAndFreshJoinAgrees()
    {
        var request=Request();world.OnHostWireInstall(request,1);world.OnHostWireInstall(request,1);world.Tick();
        Assert.Equal(1,world.FinishCount);
        Assert.Contains("first:MP_Sound",world.NativeEntries);Assert.Contains("second:MP_Sound",world.NativeEntries);
        Assert.DoesNotContain("first:MP_Finish assembly",world.NativeEntries);
        Assert.Equal((byte)3,world.Flags());Assert.Equal((byte)3,session.Guest.Get(5)!.Flags);
        Assert.Equal(WiringInstallStatus.Accepted,session.Messages.OfType<WiringInstallReceipt>().Last().Status);
        var join=new WiringReplica();foreach(var state in world.BuildWiringStates())Assert.True(join.Receive((WiringState)PacketCodec.Decode(PacketCodec.Encode(state))));
        Assert.Equal(session.Guest.Get(5)!.Flags,join.Get(5)!.Flags);Assert.Equal(world.Revision(),join.Get(5)!.Revision);
        world.OnHostWireInstall(request,1);Assert.Equal(1,world.FinishCount);
        var stale=Request(0);world.OnHostWireInstall(stale,1);Assert.Equal(1,world.FinishCount);
    }
}
