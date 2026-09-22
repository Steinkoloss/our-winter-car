using System;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

public class BridgeTests
{
    private static readonly string[] Names = { "Stain1s3", "Stain2s5", "Stain3s7", "Stain4s4", "Stain5s4" };
    private static (PissAreaSync Sync, PlayMakerFSM Fsm, GameObject[] Stains, SessionManager Session) World(bool host)
    {
        GameObject.Scene.Clear();
        Time.unscaledTime = 10;
        Physics.Roof=true;Physics.Rays=0;
        var session = new SessionManager { IsHost = host, LocalPlayerId = host ? (byte)0 : (byte)1,
            State = host ? SessionState.Hosting : SessionState.Connected };
        SessionManager.Instance = session;
        var fsm = new GameObject("YARD/PissAreas").Add(new PlayMakerFSM());
        var stains = new GameObject[5];
        for (int i=0;i<5;i++)
        {
            stains[i] = new GameObject(Names[i]);
            stains[i].transform.localScale = new Vector3(1,1,1);
            stains[i].transform.position = new Vector3(i*5,0,0);
            fsm.FsmVariables.Floats["Scale"+(i+1)] = new FsmFloat { Name="Scale"+(i+1),Value=0 };
            fsm.FsmVariables.Objects[Names[i]] = new FsmGameObject { Name=Names[i],Value=stains[i] };
        }
        var player = new GameObject("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Piss").Add(new PlayMakerFSM { ActiveStateName="Full power" });
        foreach (var name in new[] { "ChangeScale", "Addition", "PissRate", "Clamp" })
            fsm.FsmVariables.Floats[name] = new FsmFloat { Name=name, Value=name == "ChangeScale" ? 1.1f : 1 };
        var current = new FsmGameObject { Name="CurrentArea", Value=stains[0] };
        fsm.FsmVariables.Objects["CurrentArea"] = current;
        var change = fsm.FsmVariables.FindFsmFloat("ChangeScale")!;
        var add = fsm.FsmVariables.FindFsmFloat("Addition")!;
        fsm.FsmStates = new[] { new FsmState { Name="State 2", Actions=new FsmStateAction[] {
            new GetFsmGameObject { gameObject=new FsmOwnerDefault { GameObject=new FsmGameObject { Value=player.gameObject } } },
            new GetScale { xScale=change }, new FloatOperator { float1=fsm.FsmVariables.FindFsmFloat("PissRate")!,storeResult=add },
            new FloatAdd { floatVariable=change,add=add }, new FloatClamp { floatVariable=change,maxValue=fsm.FsmVariables.FindFsmFloat("Clamp")! },
            new SetScale { gameObject=new FsmOwnerDefault { GameObject=current },x=change,y=change }, new GameObjectChanged() } } };
        return (new PissAreaSync(),fsm,stains,session);
    }
    [Fact]
    public void SnapshotReadsLiveStainRatherThanStaleSaveCache()
    {
        var w = World(true);
        w.Stains[0].transform.localScale = new Vector3(1.5f,1.5f,1);
        var snapshot = w.Sync.BuildSnapshot();
        Assert.NotNull(snapshot);
        Assert.Equal((byte)30,snapshot!.Scale1);
        Assert.Equal(0,w.Fsm.FsmVariables.FindFsmFloat("Scale1")!.Value);
    }
    [Fact]
    public void AbsoluteGuestStateChangesVisibleStainWithoutRunningLoadOrSave()
    {
        var w = World(false);
        w.Sync.Apply(new PissAreaState { Epoch=1,Revision=1,Sequence=1,Scale1=30,Scale2=20,Scale3=20,Scale4=20,Scale5=20 });
        Assert.Equal(1.5f,w.Stains[0].transform.localScale.x);
    }

    private static PissAreaIntent Intent(PissAreaState state) => new PissAreaIntent {
        Epoch=state.Epoch,Revision=state.Revision,Admission=state.Admissions.Single().Token,
        Actor=1,Sequence=1,Area=1,Action=1,Contribution=.1f };
    private static PissAreaIntent Copy(PissAreaIntent r) => (PissAreaIntent)PacketCodec.Decode(PacketCodec.Encode(r));

    [Fact]
    public void NativeGuestCallbackToHostOnceAndOneAbsoluteResultConvergesTwoPeers()
    {
        var host=World(true);
        host.Session.Players.Add(new RemotePlayer { PlayerId=1 });
        host.Sync.Update(host.Session);
        var baseline=Assert.IsType<PissAreaState>(Assert.Single(host.Session.Sent));
        host.Session.Sent.Clear();
        int writes=host.Stains[0].transform.Writes;
        var guest=World(false);
        guest.Sync.Update(guest.Session);
        guest.Sync.Apply(baseline);
        guest.Fsm.ActiveStateName="State 2";
        guest.Fsm.FsmStates[0].Actions[5].OnLateUpdate();
        var request=Assert.IsType<PissAreaIntent>(Assert.Single(guest.Session.Sent));
        Assert.Equal(1,guest.Stains[0].transform.localScale.x); // no speculation
        SessionManager.Instance=host.Session;
        Assert.True(host.Sync.OnIntent(request,1));
        Assert.False(host.Sync.OnIntent(request,1));
        Assert.Equal(writes+1,host.Stains[0].transform.Writes);
        var result=Assert.IsType<PissAreaState>(Assert.Single(host.Session.Sent));
        Assert.Equal((uint)1,result.Admissions.Single().HighWater);
        Assert.Equal((byte)22,result.Scale1);
        Assert.Equal(new byte[] {20,20,20,20},new[] {result.Scale2,result.Scale3,result.Scale4,result.Scale5});
        SessionManager.Instance=guest.Session;
        guest.Sync.Apply(result);
        Assert.Equal(host.Stains[0].transform.localScale.x,guest.Stains[0].transform.localScale.x);
        guest.Sync.Apply(baseline); // delayed baseline cannot undo result
        Assert.Equal(1.1f,guest.Stains[0].transform.localScale.x);
    }

    [Theory]
    [InlineData("actor")][InlineData("unauthorized")][InlineData("epoch")][InlineData("admission")]
    [InlineData("area0")][InlineData("area6")][InlineData("wrongArea")][InlineData("action")]
    [InlineData("zero")][InlineData("negative")][InlineData("tooMuch")][InlineData("nan")]
    [InlineData("infinity")][InlineData("sequence0")][InlineData("revision")][InlineData("stale")]
    [InlineData("dead")][InlineData("absent")][InlineData("oldPose")][InlineData("futurePose")]
    [InlineData("badPosition")][InlineData("far")][InlineData("driving")][InlineData("save")]
    [InlineData("uncovered")][InlineData("noPiss")]
    public void InvalidRequestsLeaveNativeAndAcceptedSequenceUntouched(string reason)
    {
        var w=World(true); var player=new RemotePlayer { PlayerId=1 }; w.Session.Players.Add(player);
        w.Sync.Update(w.Session); var request=Intent((PissAreaState)w.Session.Sent.Single());
        w.Session.Sent.Clear(); int writes=w.Stains[0].transform.Writes; byte sender=1;
        switch(reason) {
            case "actor": request.Actor=2;break; case "unauthorized":sender=2;break;
            case "epoch":request.Epoch++;break;case "admission":request.Admission++;break;
            case "area0":request.Area=0;break;case "area6":request.Area=6;break;case "wrongArea":request.Area=2;break;
            case "action":request.Action=0;break;case "zero":request.Contribution=0;break;
            case "negative":request.Contribution=-.1f;break;case "tooMuch":request.Contribution=.251f;break;
            case "nan":request.Contribution=float.NaN;break;case "infinity":request.Contribution=float.PositiveInfinity;break;
            case "sequence0":request.Sequence=0;break;case "revision":request.Revision+=100;break;
            case "stale":Time.unscaledTime+=1.01f;player.LastTransformTime=Time.unscaledTime;break;
            case "dead":player.IsDead=true;break;case "absent":w.Session.Players.Clear();break;
            case "oldPose":player.LastTransformTime=9;break;case "futurePose":player.LastTransformTime=11;break;
            case "badPosition":player.Position=new Vector3(float.NaN,0,0);break;
            case "far":player.Position=new Vector3(100,100,100);break;
            case "driving":player.MoveState=PlayerMoveState.Driving;break;
            case "save":w.Fsm.ActiveStateName="State 4";break;
            case "uncovered":Physics.Roof=false;break;
            case "noPiss":w.Fsm.gameObject.Children=new[]{new Transform {name="NOPISS",position=player.Position}};break;
        }
        Assert.False(w.Sync.OnIntent(request,sender));
        Assert.Equal(writes,w.Stains[0].transform.Writes);
        Assert.Equal(1,w.Stains[0].transform.localScale.x);
        Assert.Empty(w.Session.Sent);
        // Inspect high water without manufacturing a fresh authority/snapshot.
        var entries=(System.Collections.IDictionary)typeof(PissAreaSync).GetField("_admissions",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(w.Sync)!;
        object admission=entries[(byte)1]!;
        Assert.Equal((uint)0,(uint)admission.GetType().GetField("Sequence",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(admission)!);
    }
    [Fact]
    public void NativeFailureDoesNotConsumeRequestAndReentryCannotMutate()
    {
        var w=World(true);w.Session.Players.Add(new RemotePlayer {PlayerId=1});w.Sync.Update(w.Session);
        var r=Intent((PissAreaState)w.Session.Sent.Single());w.Session.Sent.Clear();
        w.Stains[0].transform.ThrowOnWrite=true;
        Assert.False(w.Sync.OnIntent(r,1));Assert.Empty(w.Session.Sent);
        w.Stains[0].transform.ThrowOnWrite=false;
        var nested=Copy(r);nested.Sequence=2;
        w.Stains[0].transform.OnWrite=()=> { Assert.False(w.Sync.OnIntent(nested,1));Assert.Null(w.Sync.BuildSnapshot()); };
        Assert.True(w.Sync.OnIntent(r,1));
        Assert.Single(w.Session.Sent);
        Assert.Equal((uint)1,((PissAreaState)w.Session.Sent[0]).Admissions.Single().HighWater);
    }
    [Fact]
    public void ResetAndReturningPlayerInvalidateOldAdmissionWithoutReplayingContributions()
    {
        var w=World(true);w.Session.Players.Add(new RemotePlayer {PlayerId=1});w.Sync.Update(w.Session);
        var r=Intent((PissAreaState)w.Session.Sent.Single());Assert.True(w.Sync.OnIntent(r,1));
        w.Session.Players.Clear();w.Session.Players.Add(new RemotePlayer {PlayerId=1});
        var fresh=w.Sync.BuildSnapshot()!;
        Assert.NotEqual(r.Admission,fresh.Admissions.Single().Token);
        r.Sequence=2;Assert.False(w.Sync.OnIntent(r,1));
        Assert.Equal((uint)0,fresh.Admissions.Single().HighWater);
        w.Sync.Clear();w.Sync.Update(w.Session);
        var reset=w.Sync.BuildSnapshot()!;
        Assert.NotEqual(r.Epoch,reset.Epoch);
        Assert.False(w.Sync.OnIntent(r,1));
        Assert.Equal(1.1f,w.Stains[0].transform.localScale.x);
    }
    [Fact]
    public void GuestGateSuppressesBeforeAdmissionAndRestoresExactNativeActionOnClear()
    {
        var w=World(false);var native=w.Fsm.FsmStates[0].Actions[5];w.Fsm.ActiveStateName="State 2";
        w.Sync.Update(w.Session);var gate=w.Fsm.FsmStates[0].Actions[5];Assert.NotSame(native,gate);
        gate.OnEnter();gate.OnUpdate();gate.OnLateUpdate();Assert.Empty(w.Session.Sent);
        Assert.Equal(1,w.Stains[0].transform.localScale.x);
        w.Sync.Clear();Assert.Same(native,w.Fsm.FsmStates[0].Actions[5]);
        w.Session.State=SessionState.Offline;native.OnLateUpdate();
        Assert.Equal(1.1f,w.Stains[0].transform.localScale.x);
    }
    [Fact]
    public void HostLocalNativeWriteAndSnapshotDoNotTouchSaveCacheOrRequireGuest()
    {
        var w=World(true);w.Sync.Update(w.Session);
        w.Fsm.FsmStates[0].Actions[5].OnLateUpdate();
        Assert.Equal(1.1f,w.Stains[0].transform.localScale.x);
        Assert.Equal((byte)22,w.Sync.BuildSnapshot()!.Scale1);
        Assert.Equal(0,w.Fsm.FsmVariables.FindFsmFloat("Scale1")!.Value);
    }
    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public void EverySelectedNativeAreaIsIndependentlyWritableWithoutVehicleOwnership(int area)
    {
        var w=World(true);w.Session.Players.Add(new RemotePlayer {PlayerId=1,Position=w.Stains[area-1].transform.position});
        w.Sync.Update(w.Session);var r=Intent((PissAreaState)w.Session.Sent.Single());r.Area=(byte)area;
        Assert.True(w.Sync.OnIntent(r,1));
        for(int i=0;i<5;i++) Assert.Equal(i == area-1 ? 1.1f : 1,w.Stains[i].transform.localScale.x);
        Assert.Equal(1,Physics.Rays);
    }
    [Fact]
    public void OutOfOrderThrottledAndWrappedSequencesCannotBecomeMutations()
    {
        var w=World(true);var player=new RemotePlayer {PlayerId=1};w.Session.Players.Add(player);
        w.Sync.Update(w.Session);var r=Intent((PissAreaState)w.Session.Sent.Single());r.Sequence=3;
        Assert.True(w.Sync.OnIntent(r,1));w.Session.Sent.Clear();
        r.Sequence=2;Assert.False(w.Sync.OnIntent(r,1));
        r.Sequence=4;Assert.False(w.Sync.OnIntent(r,1));
        Time.unscaledTime+=.2f;player.LastTransformTime=Time.unscaledTime;
        Assert.True(w.Sync.OnIntent(r,1));
        r.Sequence=0;Assert.False(w.Sync.OnIntent(r,1));
        Assert.Equal((uint)4,((PissAreaState)Assert.Single(w.Session.Sent)).Admissions.Single().HighWater);
    }
    [Fact]
    public void NativeApplyAfterWriteFailureRollsBackAndKeepsSequenceRetryable()
    {
        var w=World(true);w.Session.Players.Add(new RemotePlayer {PlayerId=1});w.Sync.Update(w.Session);
        var r=Intent((PissAreaState)w.Session.Sent.Single());w.Session.Sent.Clear();
        w.Stains[0].transform.OnWrite=()=> { w.Stains[0].transform.OnWrite=null; throw new Exception("after write"); };
        Assert.False(w.Sync.OnIntent(r,1));Assert.Equal(1,w.Stains[0].transform.localScale.x);Assert.Empty(w.Session.Sent);
        Assert.True(w.Sync.OnIntent(r,1));Assert.Single(w.Session.Sent);
    }
    [Fact]
    public void SendFailureRetainsCommitAndPeriodicAbsoluteStateRepairsWithoutReplay()
    {
        var w=World(true);w.Session.Players.Add(new RemotePlayer {PlayerId=1});w.Sync.Update(w.Session);
        var r=Intent((PissAreaState)w.Session.Sent.Single());w.Session.Sent.Clear();w.Session.ThrowSend=true;
        Assert.True(w.Sync.OnIntent(r,1));Assert.False(w.Sync.OnIntent(r,1));
        Assert.Equal(1.1f,w.Stains[0].transform.localScale.x);Assert.Empty(w.Session.Sent);
        w.Session.ThrowSend=false;Time.unscaledTime+=.6f;w.Sync.Update(w.Session);
        var recovered=(PissAreaState)Assert.Single(w.Session.Sent);
        Assert.Equal((byte)22,recovered.Scale1);Assert.Equal((uint)1,recovered.Admissions.Single().HighWater);
    }
    [Fact]
    public void PendingHostNativeLateWriteDoesNotEraseGuestContribution()
    {
        var w=World(true);w.Session.Players.Add(new RemotePlayer {PlayerId=1});w.Sync.Update(w.Session);
        var r=Intent((PissAreaState)w.Session.Sent.Single());w.Fsm.ActiveStateName="State 2";
        Assert.True(w.Sync.OnIntent(r,1));
        w.Fsm.FsmStates[0].Actions[5].OnLateUpdate();
        Assert.Equal(1.2f,w.Stains[0].transform.localScale.x,5);
    }
    [Fact]
    public void ProducerSignatureDriftKeepsGuestWriteSuppressed()
    {
        var w=World(false);((FloatOperator)w.Fsm.FsmStates[0].Actions[2]).operation=2;
        w.Sync.Update(w.Session);w.Fsm.ActiveStateName="State 2";
        w.Sync.Apply(new PissAreaState {Epoch=1,Revision=1,Scale1=20,Scale2=20,Scale3=20,Scale4=20,Scale5=20,
            Admissions=new[]{new PissAreaAdmission {Actor=1,Token=42}}});
        w.Fsm.FsmStates[0].Actions[5].OnLateUpdate();Assert.Empty(w.Session.Sent);
        Assert.Equal(1,w.Stains[0].transform.localScale.x);
    }
    [Fact]
    public void SnapshotApplyFailureDoesNotConsumeRevisionAndClearRestoresGuestWorld()
    {
        var w=World(false);var state=new PissAreaState {Epoch=1,Revision=1,Scale1=30,Scale2=40,Scale3=20,Scale4=20,Scale5=20};
        w.Stains[1].transform.ThrowOnWrite=true;w.Sync.Apply(state);
        Assert.Equal(1,w.Stains[0].transform.localScale.x);w.Stains[1].transform.ThrowOnWrite=false;
        w.Sync.Apply(state);Assert.Equal(1.5f,w.Stains[0].transform.localScale.x);
        w.Sync.Clear();Assert.Equal(1,w.Stains[0].transform.localScale.x);Assert.Equal(1,w.Stains[1].transform.localScale.x);
        state.Epoch=2;w.Sync.Apply(state);Assert.Equal(1.5f,w.Stains[0].transform.localScale.x);
    }
    [Fact]
    public void MissingNativeInitializationDefersSnapshotAndAdmission()
    {
        var w=World(true);w.Fsm.ActiveStateName="1";Assert.Null(w.Sync.BuildSnapshot());
        w=World(true);w.Fsm.FsmVariables.Objects.Remove("Stain5s4");
        Assert.Null(w.Sync.BuildSnapshot());
    }
}
