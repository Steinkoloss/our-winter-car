// Explicit portable doubles: no Unity/native/network/save execution is claimed.
using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using WinterMP.Net;
using WinterMP.Net.Messages;
namespace UnityEngine
{
    public class Component { public GameObject gameObject = null!; public Transform transform => gameObject.transform; }
    public class GameObject
    {
        public static Dictionary<string, GameObject> Scene = new Dictionary<string, GameObject>();
        public string name; public bool activeInHierarchy = true;
        public Transform transform;
        public Transform[] Children = Array.Empty<Transform>();
        private readonly List<Component> parts = new List<Component>();
        public GameObject(string path) { name = path; transform = new Transform(); Scene[path] = this; }
        public static GameObject? Find(string path) => Scene.TryGetValue(path, out var o) ? o : null;
        public T Add<T>(T p) where T : Component { p.gameObject = this; parts.Add(p); return p; }
        public T[] GetComponents<T>() where T : Component => parts.FindAll(x => x is T).ConvertAll(x => (T)x).ToArray();
        public Transform[] GetComponentsInChildren<T>(bool inactive) => Children;
    }
    public class Transform
    {
        private Vector3 scale = new Vector3(1,1,1);
        public Vector3 position;
        public string name = "";
        public int Writes; public bool ThrowOnWrite; public Action? OnWrite;
        public Vector3 localScale { get => scale; set { if (ThrowOnWrite) throw new InvalidOperationException("native write failure"); scale = value; Writes++; OnWrite?.Invoke(); } }
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 up => new Vector3(0,1,0);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public float sqrMagnitude => x*x+y*y+z*z;
    }
    public static class Time { public static float unscaledTime = 10, deltaTime = .1f; }
    public static class Physics
    {
        public static bool Roof = true;
        public static int Rays;
        public static bool Raycast(Vector3 origin, Vector3 direction, float distance, int mask) {
            if (direction.y != 1 || distance != 100 || mask != (1 << 27)) throw new Exception("wrong native roof query");
            Rays++; return Roof;
        }
    }
    public static class Application { public static string loadedLevelName = "GAME"; }
    public static class Mathf { public static float Clamp(float x,float a,float b) => Math.Max(a,Math.Min(b,x)); }
}
namespace HutongGames.PlayMaker
{
    public class FsmFloat { public string Name = ""; public float Value; }
    public class FsmString { public string Name = ""; public string Value = ""; }
    public class FsmGameObject { public string Name = ""; public UnityEngine.GameObject? Value; }
    public class FsmOwnerDefault { public FsmGameObject GameObject = new FsmGameObject(); }
    public class FsmBool { public bool Value; }
    public class FsmStateAction
    {
        public bool Enabled = true;
        public virtual void Init(FsmState s) { }
        public virtual void OnEnter() { }
        public virtual void OnUpdate() { }
        public virtual void OnLateUpdate() { }
        public virtual void OnExit() { }
        public void Finish() { }
    }
    public class FsmState { public string Name = ""; public bool IsInitialized = true; public FsmStateAction[] Actions = Array.Empty<FsmStateAction>(); }
    public class Fsm { public bool Initialized = true, Started = true; }
    public class FsmVariables
    {
        public Dictionary<string,FsmFloat> Floats = new Dictionary<string,FsmFloat>();
        public Dictionary<string,FsmGameObject> Objects = new Dictionary<string,FsmGameObject>();
        public FsmFloat? FindFsmFloat(string name) => Floats.TryGetValue(name,out var f) ? f : null;
        public FsmGameObject? FindFsmGameObject(string name) => Objects.TryGetValue(name,out var f) ? f : null;
    }
}
public class PlayMakerFSM : UnityEngine.Component
{
    public string FsmName = "Logic", ActiveStateName = "State 3";
    public Fsm Fsm = new Fsm(); public FsmVariables FsmVariables = new FsmVariables();
    public FsmState[] FsmStates = Array.Empty<FsmState>();
}
namespace WinterMP.Core
{
    public static class WinterMPPlugin { public static Logger Log = new Logger(); }
    public class Logger { public void LogInfo(string x) { } public void LogDebug(string x) { } public void LogError(string x) { } }
}
namespace WinterMP.Core.Session
{
    public enum SessionState { Offline, Hosting, Connected }
    public class RemotePlayer { public byte PlayerId; public bool IsDead; public float LastTransformTime = 10; public byte MoveState; public UnityEngine.Vector3 Position; }
    public class SessionManager
    {
        public static SessionManager? Instance;
        public bool IsHost; public byte LocalPlayerId; public SessionState State = SessionState.Hosting;
        public List<RemotePlayer> Players = new List<RemotePlayer>(); public int PlayerCount => Players.Count + 1;
        public List<IMessage> Sent = new List<IMessage>();
        public bool ThrowSend;
        public void SendWorldMessage(IMessage message,Channel channel) { if(channel != Channel.ReliableOrdered) throw new Exception("channel"); if (ThrowSend) throw new Exception("send failed"); Sent.Add(PacketCodec.Decode(PacketCodec.Encode(message))); }
    }
}
