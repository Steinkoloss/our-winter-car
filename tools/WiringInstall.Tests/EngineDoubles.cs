// Engine doubles, not native evidence. The request/publication/transaction code is source-linked Core.
using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace UnityEngine
{
    public static class Time { public static float unscaledTime; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public class Transform
    {
        public GameObject gameObject = null!;
        public Vector3 position;
        public Transform? parent;
        public Transform? Find(string path) => GameObject.Find(gameObject.name+"/"+path)?.transform;
    }
    public class GameObject
    {
        internal static readonly Dictionary<string,GameObject> Objects = new Dictionary<string,GameObject>();
        public string name;
        public bool activeSelf=true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public Transform transform;
        internal readonly List<object> Components = new List<object>();
        public GameObject(string path) { name=path; transform=new Transform { gameObject=this };Objects[path]=this; }
        public void SetActive(bool value) => activeSelf=value;
        public T? GetComponent<T>() where T:class => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>() => Components.OfType<T>().ToArray();
        public static GameObject? Find(string path) => Objects.TryGetValue(path.TrimStart('/'),out var obj)?obj:null;
    }
    public class Rigidbody { public GameObject gameObject=null!; }
}
namespace HutongGames.PlayMaker
{
    public class FsmBool { public bool Value; }
    public class FsmString { public string Value=""; }
    public class FsmGameObject { public UnityEngine.GameObject? Value; }
    public class FsmStateAction { }
    public class FsmState { public FsmStateAction[] Actions=new FsmStateAction[0]; }
    public class FsmVariables
    {
        public static FsmVariables GlobalVariables=new FsmVariables();
        public Dictionary<string,FsmBool> Bools=new Dictionary<string,FsmBool>();
        public Dictionary<string,FsmString> Strings=new Dictionary<string,FsmString>();
        public Dictionary<string,FsmGameObject> Objects=new Dictionary<string,FsmGameObject>();
        public FsmBool FindFsmBool(string name) => Bools.TryGetValue(name,out var value)?value:null!;
        public FsmString? FindFsmString(string name) => Strings.TryGetValue(name,out var value)?value:null;
        public FsmGameObject? FindFsmGameObject(string name) => Objects.TryGetValue(name,out var value)?value:null;
    }
    public class Fsm { public bool Started=true,Initialized=true; }
}
public class PlayMakerFSM
{
    public string FsmName="Data",ActiveStateName="Basic state";
    public bool enabled=true;
    public HutongGames.PlayMaker.Fsm Fsm=new HutongGames.PlayMaker.Fsm();
    public HutongGames.PlayMaker.FsmVariables FsmVariables=new HutongGames.PlayMaker.FsmVariables();
    public UnityEngine.GameObject gameObject=null!;
    public UnityEngine.Transform transform => gameObject.transform;
    public Action<string>? Dispatch;
    public void SendEvent(string name) => Dispatch?.Invoke(name);
}
namespace WinterMP.Core.Catalog
{
    internal class WireConnectionData
    {
        internal Dictionary<string,string> Names=new Dictionary<string,string>();
        internal string this[string key] => Names[key];
    }
    internal class EngineWireData
    {
        public uint Id=5;
        public string Name="WiringIgnitionFusebox",Path="CORRIS/Wiring/DatabaseWiring/WiringIgnitionFusebox",Fsm="Data",SettledState="Basic state";
        public bool SupportsBolted;
        public WireConnectionData? Connection;
    }
    internal class Profile { internal List<EngineWireData> Wires=new List<EngineWireData>(); }
    internal static class SyncCatalog { internal static Profile? GuestEngineInputs; }
}
namespace WinterMP.Core.Diagnostics { internal static class SyncEventLog { internal static void Record(string kind,string value) { } } }
namespace WinterMP.Core
{
    internal static class WinterMPPlugin { internal static Logger Log=new Logger(); }
    internal class Logger { internal void LogWarning(string message) { } }
}
namespace WinterMP.Core.Session
{
    internal enum SessionState { Connected }
    internal class SessionManager
    {
        internal static SessionManager? Instance;
        internal bool IsHost=true;
        internal byte LocalPlayerId=1;
        internal SessionState State=SessionState.Connected;
        internal readonly List<IMessage> Messages=new List<IMessage>();
        internal readonly WiringReplica Guest=new WiringReplica();
        internal void SendWorldMessage(IMessage message,Channel channel)
        {
            if(channel!=Channel.ReliableOrdered)throw new Exception("Wrong channel");
            var copy=PacketCodec.Decode(PacketCodec.Encode(message));Messages.Add(copy);
            if(copy is WiringState state) Guest.Receive(state);
        }
        internal void AddSystemChat(string message) { }
    }
}
namespace WinterMP.Core.Sync
{
    internal static class WorldSyncIds { internal const byte NoOwner=255; }
    internal static class ScenePath { internal static string Of(UnityEngine.Transform t) => t.gameObject.name; }
    internal class EngineSourceLookup { internal UnityEngine.GameObject? Find(string path) => UnityEngine.GameObject.Find(path); }
    internal class FsmSuppressor { internal bool Suppress(PlayMakerFSM fsm)=>true; internal void Restore() { } }
    internal class FsmHookAction:HutongGames.PlayMaker.FsmStateAction { internal FsmHookAction(Action a) { } }
    internal static class FsmHook
    {
        internal static HutongGames.PlayMaker.FsmState? FindState(PlayMakerFSM f,string s)=>new HutongGames.PlayMaker.FsmState();
        internal static bool EnsureRemoteEntry(PlayMakerFSM f,string s)=>true;
        internal static void FireRemoteEntry(PlayMakerFSM f,string s)=>f.SendEvent("MP_"+s);
    }
    internal sealed partial class ItemWorldSync
    {
        internal class SyncedItem { internal UnityEngine.Rigidbody? Body; internal bool LocallyOwned; internal byte RemoteOwner=WorldSyncIds.NoOwner; }
        private readonly Dictionary<uint,SyncedItem> _items=new Dictionary<uint,SyncedItem>();
        internal bool ActorNear=true;
        private static bool GuestNearPackage(Session.SessionManager session,byte actor,UnityEngine.Vector3 position) => Current!.ActorNear;
        private static ItemWorldSync? Current;
        private static void ValidateWireConnection(WireConnection b) { }
        internal PlayMakerFSM Data=null!,First=null!,Second=null!,Save=null!;
        internal UnityEngine.GameObject Mesh=null!,Triggers=null!,Tool=null!;
        internal SyncedItem SharedTool=null!;
        internal readonly List<string> NativeEntries=new List<string>();
        internal int FinishCount;
        internal string Fault="";
        internal ItemWorldSync()
        {
            Current=this; UnityEngine.GameObject.Objects.Clear();UnityEngine.Time.unscaledTime=10;
            HutongGames.PlayMaker.FsmVariables.GlobalVariables=new HutongGames.PlayMaker.FsmVariables();
            var rule=new Catalog.EngineWireData { Connection=new Catalog.WireConnectionData() };
            foreach(var pair in new[]{new[]{"finishState","Finish assembly"},new[]{"resetState","Init"},new[]{"resetEvent","RESETWIRING"},new[]{"prerequisiteVariable","Installed"},new[]{"toolPath","EQUIPMENTS/wiring mess(itemx)"}})rule.Connection.Names[pair[0]]=pair[1];
            Catalog.SyncCatalog.GuestEngineInputs=new Catalog.Profile();Catalog.SyncCatalog.GuestEngineInputs.Wires.Add(rule);
            Data=Make(rule.Path,"Data");Data.FsmVariables.Bools["Installed"]=new HutongGames.PlayMaker.FsmBool();
            Data.FsmVariables.Bools["Trigger"]=new HutongGames.PlayMaker.FsmBool { Value=true };
            var prerequisite=Make("column","Data");prerequisite.FsmVariables.Bools["Installed"]=new HutongGames.PlayMaker.FsmBool { Value=true };
            Mesh=new UnityEngine.GameObject("mesh");Mesh.SetActive(false);Triggers=new UnityEngine.GameObject("triggers");
            First=Make("first","Assemble");Second=Make("second","Assemble");First.transform.parent=Second.transform.parent=Triggers.transform;
            Tool=new UnityEngine.GameObject("wiring mess(itemx)");var body=new UnityEngine.Rigidbody { gameObject=Tool };Tool.Components.Add(body);
            Save=new PlayMakerFSM { gameObject=Tool,FsmName="Save" };Save.FsmVariables.Strings["UniqueTag"]=new HutongGames.PlayMaker.FsmString { Value=Tool.name };Tool.Components.Add(Save);
            Tool.Components.Add(new PlayMakerFSM { gameObject=Tool,FsmName="Use" });
            HutongGames.PlayMaker.FsmVariables.GlobalVariables.Objects["WiringTool"]=new HutongGames.PlayMaker.FsmGameObject { Value=Tool };
            SharedTool=new SyncedItem { Body=body,RemoteOwner=1 };_items[1]=SharedTool;
            var binding=new WireConnection { Rule=rule,Data=Data,Prerequisite=prerequisite,Mesh=Mesh,Triggers=Triggers,Tool=Tool,Ends=new[]{First,Second} };
            _wireConnection=binding;
            First.Dispatch=s=>Native(binding,First,s);Second.Dispatch=s=>Native(binding,Second,s);
        }
        private static PlayMakerFSM Make(string path,string name)
        {
            var obj=new UnityEngine.GameObject(path);var fsm=new PlayMakerFSM { gameObject=obj,FsmName=name };obj.Components.Add(fsm);return fsm;
        }
        private void Native(WireConnection binding,PlayMakerFSM end,string entry)
        {
            NativeEntries.Add((end==First?"first:":"second:")+entry);
            if(entry=="MP_Init" || entry=="RESETWIRING") { end.ActiveStateName="Init";return; }
            if(entry=="MP_Sound")
            {
                end.ActiveStateName="Sound";
                var other=end==First?Second:First;
                if(other.ActiveStateName!="Sound")return;
                end=other;
            }
            else if(entry!="MP_Finish assembly")return;
            if(Fault=="timeout") { Mesh.SetActive(true);return; }
            OnWireFinish(binding,end);FinishCount++;
            Data.FsmVariables.Bools["Installed"].Value=true;
            if(Fault=="throw")throw new InvalidOperationException("Injected native failure after saved write");
            if(Fault=="partial")return;
            Mesh.SetActive(true);Triggers.SetActive(false);
        }
        internal void Tick() => ProcessWireConnection(Session.SessionManager.Instance!);
        internal byte Flags() => BuildWiringStates().Single().Flags;
        internal uint Revision() => BuildWiringStates().Single().Revision;
        internal void MissingColumn() => _wireConnection!.Prerequisite.FsmVariables.Bools["Installed"].Value=false;
    }
}
