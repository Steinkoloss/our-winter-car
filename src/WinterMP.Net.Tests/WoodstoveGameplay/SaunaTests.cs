using System;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WoodstoveGameplay.Tests
{
    public sealed partial class SaunaTests
    {
        const string Root = "YARD/Building/SAUNA/Sauna";
        [Fact]
        public void Guest_native_scroll_emits_intent_without_local_timer_mutation()
        {
            var f = new Fixture(false); f.Sync.Update(f.Session);
            f.Knob.Fsm.States.Single(s => s.Name == "Screw").Enter();
            Assert.Equal(0f, f.Timer.Value);
            Assert.Equal(0, f.Add.Calls);
            // Before admission no timer request can be made, but vanilla must not mutate.
        }
        [Fact]
        public void Authenticated_nonowner_and_host_local_share_one_native_authority()
        {
            var host = new Fixture(true); host.Sync.Update(host.Session);
            var initial = host.Session.Sent.Single(m => m.GetType().Name == "SaunaTimerState");
            var request = Request(initial, 1, 1, 10);
            host.Session.Sent.Clear(); Dispatch(host, request, 1);
            Assert.Equal(1, host.Add.Calls); Assert.Equal(10, host.Timer.Value); Assert.Equal(60, host.Time.Value);
            var result = Assert.Single(host.Session.Sent.Where(m => m.GetType().Name == "SaunaTimerState"));
            Assert.Equal((byte)1, Field<byte>(result,"Status"));
            var guest = new Fixture(false); guest.Sync.Update(guest.Session);
            Receive(guest, result); Assert.Equal(host.Timer.Value,guest.Timer.Value); Assert.Equal(host.Time.Value,guest.Time.Value);
            // These peers share process-wide engine doubles, unlike real Unity peers.
            // Restore the host ray target as well as its session before host-local input.
            host.Activate();
            host.Knob.Fsm.States.Single(s => s.Name == "Screw").Enter(); host.Sync.Update(host.Session);
            Assert.Equal(2,host.Add.Calls); Assert.Equal(20,host.Timer.Value); Assert.Equal(120,host.Time.Value);
            Dispatch(host, request, 1); Assert.Equal(2,host.Add.Calls);
        }
        static T Field<T>(object o,string name) => (T)o.GetType().GetField(name)!.GetValue(o)!;
        static object Request(IMessage state,byte actor,uint sequence,float timer)
        {
            var type=typeof(IMessage).Assembly.GetType("WinterMP.Net.Messages.SaunaTimerIntent"); Assert.NotNull(type);
            var request=Activator.CreateInstance(type!)!;
            foreach(var pair in new (string,object)[] {("SourceId",StableHash.Fnv1a32(Root)),("Epoch",Field<uint>(state,"Epoch")),
                ("Actor",actor),("Sequence",sequence),("ExpectedRevision",Field<uint>(state,"Revision")),("Timer",timer)})
                type!.GetField(pair.Item1)!.SetValue(request,pair.Item2);
            type!.GetField("Direction")!.SetValue(request,new NetVector3(0,0,1));
            return request;
        }
        static void Dispatch(Fixture f,object r,byte actor)
        { SessionManager.Instance=f.Session; var method=typeof(HeatSourceSync).GetMethod("OnSaunaIntent",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic); Assert.NotNull(method); method!.Invoke(f.Sync,new[]{r,(object)actor}); }
        static void Receive(Fixture f,IMessage r)
        { SessionManager.Instance=f.Session; typeof(HeatSourceSync).GetMethod("OnSaunaState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(f.Sync,new object[]{r}); }
        sealed class Fixture
        {
            internal readonly HeatSourceSync Sync=new HeatSourceSync();
            internal readonly SessionManager Session;
            internal readonly PlayMakerFSM Knob, Simulation;
            internal readonly Collider Collider;
            internal readonly Camera Camera;
            internal readonly PlayerSyncManager Player = new PlayerSyncManager();
            internal readonly FsmFloat Timer=new FsmFloat{Name="Timer"},Time=new FsmFloat{Name="Time"};
            internal readonly FloatAdd Add;
            internal readonly FloatSubtract Subtract;
            internal Fixture(bool host)
            {
                GameObject.Scene.Clear(); UnityEngine.Time.unscaledTime=10;
                Session=new SessionManager{IsHost=host,LocalPlayerId=host?(byte)0:(byte)1}; Session.Players.Add(new RemotePlayer{PlayerId=1}); SessionManager.Instance=Session;
                PlayerSyncManager.Instance=Player;
                var root=new GameObject(Root); var knob=new GameObject(Root+"/Kiuas/ButtonTime"); root.transform.Children["Kiuas/ButtonTime"]=knob.transform;
                var mesh=new GameObject(Root+"/Kiuas/ButtonTime/mesh"); knob.transform.Children["mesh"]=mesh.transform;
                Collider=knob.Add(new BoxCollider()); Physics.Hit=Collider;
                Camera=new GameObject("test-camera").Add(new Camera()); UnityEngine.Camera.main=Camera;
                Knob=knob.Add(new PlayMakerFSM{FsmName="Screw"}); Knob.FsmVariables.Vars["Timer"]=Timer;
                var step=new FsmFloat{Name="ScrewAmount",Value=10}; Knob.FsmVariables.Vars["ScrewAmount"]=step;
                var math=new FsmFloat{Name="Math1"}; Knob.FsmVariables.Vars["Math1"]=math;
                Knob.FsmVariables.Vars["CapMesh"]=new FsmGameObject{Name="CapMesh",Value=mesh};
                var sim=new GameObject(Root+"/Simulation"); root.transform.Children["Simulation"]=sim.transform;
                Add=new FloatAdd{floatVariable=Timer,add=step};
                Subtract=new FloatSubtract{floatVariable=Timer,subtract=step};
                Knob.Fsm.States=new[]{ State("Screw",Add,new FloatClamp{floatVariable=Timer},new SetRotation{Write=()=>mesh.transform.localEulerAngles=new Vector3(0,Timer.Value,0)}),
                    State("Unscrew",Subtract,new FloatClamp{floatVariable=Timer},new SetRotation{Write=()=>mesh.transform.localEulerAngles=new Vector3(0,Timer.Value,0)}),
                    State("Wait",new FloatOperator{float1=Timer,storeResult=math},new SetFsmFloat{setValue=math,gameObject=new FsmOwnerDefault{GameObject=new FsmGameObject{Value=sim}},Write=()=>Time.Value=math.Value},new Wait()),
                    State("Get scroll"),State("Mouse off 2"),State("Save game"),State("Load game"),State("Check data") };
                var sf=sim.Add(new PlayMakerFSM{FsmName="Time"}); Simulation=sf; sf.FsmVariables.Vars["Time"]=Time;
                sf.FsmVariables.Vars["SaunaHeat"]=new FsmFloat{Value=22}; sf.FsmVariables.Vars["StoveHeat"]=new FsmFloat{Value=22};
                sf.Fsm.States=new[]{State("Heat up",new FloatOperator{float1=Time,storeResult=Time,operation=1},new SetRotation())};
                Activate();
            }
            internal void Activate()
            {
                SessionManager.Instance=Session; Physics.Hit=Collider;
                UnityEngine.Camera.main=Camera; PlayerSyncManager.Instance=Player; DeathSyncManager.Instance=null;
            }
        }
        static FsmState State(string n,params FsmStateAction[] a)=>new FsmState{Name=n,Actions=a};
    }
    public class FloatAdd:FsmStateAction { public FsmFloat floatVariable=null!,add=null!; public bool everyFrame,perSecond; public int Calls; public Action? During; public bool Throw; public override void OnEnter(){Calls++; floatVariable.Value+=add.Value; During?.Invoke(); if(Throw)throw new InvalidOperationException("partial timer");} }
    public class FloatSubtract:FsmStateAction { public FsmFloat floatVariable=null!,subtract=null!; public bool everyFrame,perSecond; public int Calls; public override void OnEnter(){Calls++; floatVariable.Value-=subtract.Value;} }
    public class FloatClamp:FsmStateAction { public FsmFloat floatVariable=null!,minValue=new FsmFloat{Value=1},maxValue=new FsmFloat{Value=120}; public bool everyFrame; public override void OnEnter(){floatVariable.Value=Math.Clamp(floatVariable.Value,minValue.Value,maxValue.Value);} }
    public class SetRotation:FsmStateAction { public Action? Write; public override void OnEnter()=>Write?.Invoke(); }
    public class FloatOperator:FsmStateAction { public FsmFloat float1=null!,float2=new FsmFloat{Value=6},storeResult=null!; public int operation=2; public override void OnEnter(){storeResult.Value=float1.Value*float2.Value;} }
    public class SetFsmFloat:FsmStateAction { public FsmFloat setValue=null!; public FsmString fsmName=new FsmString{Value="Time"},variableName=new FsmString{Value="Time"}; public FsmOwnerDefault gameObject=null!; public Action? Write; public override void OnEnter()=>Write?.Invoke(); }
    public class Wait:FsmStateAction { }
}
