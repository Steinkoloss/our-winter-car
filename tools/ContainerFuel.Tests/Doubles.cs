using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace UnityEngine
{
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero=>new Vector3(); public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public sealed class Transform {
        public string Path=""; public Vector3 position; public readonly Dictionary<string,Transform> Children=new();
        public Transform? Find(string path)=>Children.TryGetValue(path,out var t)?t:null;
    }
    public sealed class GameObject {
        public static GameObject? Player; public bool activeInHierarchy=true; public Transform transform=new();
        public static GameObject? Find(string name)=>Player;
    }
    public sealed class Rigidbody {
        public GameObject gameObject=new(); public Transform transform=>gameObject.transform;
        public Vector3 position { get=>transform.position; set=>transform.position=value; }
        public Vector3 velocity; public PlayMakerFSM[] Fsms=Array.Empty<PlayMakerFSM>();
        public T[] GetComponentsInChildren<T>(bool active)=>Fsms.Cast<T>().ToArray();
    }
    public static class Time { public static float unscaledTime=10; }
    public static class Mathf {
        public static float Abs(float x)=>Math.Abs(x); public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
    }
    public enum KeyCode { LeftAlt,R }
    public static class Input { public static bool Press; public static bool GetKey(KeyCode key)=>Press; public static bool GetKeyDown(KeyCode key)=>Press; }
}
namespace HutongGames.PlayMaker {
    public sealed class FsmFloat {
        private float _value; public bool FailNextSet;
        public float Value { get=>_value; set { if(FailNextSet) { FailNextSet=false; throw new InvalidOperationException("injected setter"); } _value=value; } }
    }
    public sealed class FsmBool { public bool Value; }
    public sealed class Fsm { public bool Started=true; }
    public sealed class FsmVariables {
        public readonly Dictionary<string,FsmFloat> Floats=new(); public readonly Dictionary<string,FsmBool> Bools=new();
        public FsmFloat? FindFsmFloat(string n)=>Floats.TryGetValue(n,out var f)?f:null;
        public FsmBool? FindFsmBool(string n)=>Bools.TryGetValue(n,out var f)?f:null;
    }
}
public sealed class PlayMakerFSM {
    public string FsmName=""; public UnityEngine.Transform transform=new(); public Fsm Fsm=new(); public FsmVariables FsmVariables=new();
}
namespace WinterMP.Core {
    public static class WinterMPPlugin { public static Logger Log=new(); }
    public sealed class Logger { public readonly List<string> Errors=new(); public void LogInfo(object v) {} public void LogDebug(object v) {} public void LogError(object v) { Errors.Add(v.ToString()!); } }
}
namespace WinterMP.Core.Session {
    public sealed class Player { public byte PlayerId; public float LastTransformTime=10; public UnityEngine.Vector3 Position; }
    public sealed class SessionManager {
        public static SessionManager? Instance; public bool IsHost; public byte LocalPlayerId; public int PlayerCount=2;
        public List<Player> Players=new(); public List<IMessage> Sent=new();
        public void SendWorldMessage(IMessage message,Channel channel) {
            if(channel!=Channel.ReliableOrdered) throw new Exception("wrong channel");
            Sent.Add(PacketCodec.Decode(PacketCodec.Encode(message)));
        }
    }
}
namespace WinterMP.Core.Sync {
    internal static class WorldSyncIds { internal const byte NoOwner=255; }
    internal static class WorldSyncBridge { internal const string PlayerObjectName="PLAYER"; }
    internal static class ScenePath { internal static string Of(UnityEngine.Transform t)=>t.Path; }
    internal sealed class SyncedItem {
        internal uint Id,FuelRevision; internal string Path=""; internal UnityEngine.Rigidbody Body=new();
        internal bool IsVehicle,LocallyOwned,DespawnSent,LocalDriveActive,RemoteIsDriver,RemoteEngineOn,RemoteAccOn,Held,Ignition;
        internal byte RemoteOwner=255,LastRemoteFluidOwner=255,RemoteFuelLevel;
        internal float LastRemoteAt=10,NextFluidProbeAt,NextFluidSendAt,LastSentFluidLevel=float.NaN,AcceptedFluidLevel=float.NaN;
        internal UnityEngine.Vector3 TargetPosition; internal ushort OutFluidSequence,LastRemoteFluidSequence;
        internal bool LastSentFluidPouring;
        internal FsmFloat? FluidLevelVar,FluidCapacityVar,FuelTankLevelVar,FuelTankCapacityVar,GaugeFuelLevelVar;
        internal FsmBool? FluidPouringVar; internal VehicleState? AcceptedVehicleState;
    }
    internal sealed class ItemWorldSync {
        internal Dictionary<uint,SyncedItem> Items=new(); internal bool Driving;
        internal bool IsHeldForFuel(SyncedItem item)=>item.Held;
        internal bool IsLocalPlayerDriving(SyncedItem item)=>Driving;
    }
    internal sealed partial class VehicleWorldSync {
        private static void EnsureVehicleSystemsProbe(SyncedItem item) {}
        private static bool HasLocalIgnitionActivity(SyncedItem item)=>item.Ignition;
        private static byte NormalizeFuelLevel(float value,float capacity)=>(byte)Math.Clamp(Math.Round(value/capacity*255),0,255);
    }
}
