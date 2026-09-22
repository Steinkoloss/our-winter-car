// Portable doubles ONLY: no Unity runtime, native physics, network or save evidence.
using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class GameObject
    {
        public static readonly Dictionary<string, GameObject> Scene = new Dictionary<string, GameObject>();
        public readonly string name;
        public readonly Transform transform;
        public bool activeInHierarchy = true;
        private readonly List<Component> _components = new List<Component>();
        public GameObject(string path) { name = path; transform = new Transform(this); Scene[path] = this; }
        public static GameObject? Find(string path) => Scene.TryGetValue(path, out var o) ? o : null;
        public T Add<T>(T c) where T : Component { c.gameObject = this; _components.Add(c); return c; }
        public T? GetComponent<T>() where T : Component => _components.Find(x => x is T) as T;
        public T[] GetComponents<T>() where T : Component => _components.FindAll(x => x is T).ConvertAll(x => (T)x).ToArray();
    }
    public class Transform
    {
        public readonly GameObject gameObject;
        public Transform? parent;
        public Vector3 position, forward = Vector3.forward;
        public Transform(GameObject o) { gameObject = o; }
        public bool IsChildOf(Transform t) => this == t || (parent != null && parent.IsChildOf(t));
    }
    public class Rigidbody : Component { public Vector3 velocity, angularVelocity; public string name => gameObject.name; }
    public class Collider : Component { public bool enabled = true; public Rigidbody? attachedRigidbody; public Bounds bounds; }
    public struct Bounds { public bool Contains(Vector3 v) => false; }
    public class Material
    {
        public float Cutoff;
        public int Writes;
        public void SetFloat(string name, float value) { if (name != "_Cutoff") throw new Exception(name); Cutoff = value; Writes++; }
    }
    public struct RaycastHit { public Collider collider; public float distance; }
    public static class Physics
    {
        public const int DefaultRaycastLayers = 1;
        public static Collider? Contact;
        public static float Distance = .6f;
        public static int Rays;
        public static bool Raycast(Vector3 eye, Vector3 dir, out RaycastHit hit, float range, int layers)
        { Rays++; hit = new RaycastHit { collider = Contact!, distance = Distance }; return Contact != null && Distance <= range; }
    }
    public static class Time { public static float unscaledTime = 10; }
    public static class Application { public static string loadedLevelName = "GAME"; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero => new Vector3();
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b,a.y*b,a.z*b);
        public static float Angle(Vector3 a, Vector3 b) => (float)(Math.Acos(Math.Clamp((a.x*b.x+a.y*b.y+a.z*b.z)/Math.Sqrt(a.sqrMagnitude*b.sqrMagnitude),-1,1))*180/Math.PI);
    }
    public struct Quaternion { public static Quaternion identity => new Quaternion(); public static Vector3 operator *(Quaternion q, Vector3 v) => v; }
    public static class Mathf { public static float Abs(float f) => Math.Abs(f); }
}
namespace HutongGames.PlayMaker
{
    public class FsmStateAction
    {
        public bool Enabled = true;
        public FsmState? State;
        public bool Finished;
        public virtual void Init(FsmState s) { State = s; }
        public virtual void Reset() { }
        public virtual void OnEnter() { }
        public void Finish() { if (State == null) throw new InvalidOperationException("Uninitialized action"); Finished = true; }
    }
    public class FsmState
    {
        public string Name = "";
        public bool IsInitialized = true;
        public FsmStateAction[] Actions = Array.Empty<FsmStateAction>();
        public void Enter() { foreach (var a in Actions) if (a.Enabled) a.OnEnter(); }
    }
    public class FsmEvent
    {
        public string Name = "";
        public static FsmEvent GetFsmEvent(string n) => new FsmEvent { Name = n };
    }
    public class FsmTransition { public FsmEvent FsmEvent = null!; public string ToState = ""; public string EventName => FsmEvent.Name; }
    public class Fsm
    {
        public bool Initialized = true, Started = true, RestartOnEnable = true;
        public FsmState[] States = Array.Empty<FsmState>();
        public FsmTransition[] GlobalTransitions = Array.Empty<FsmTransition>();
        public FsmEvent[] Events = Array.Empty<FsmEvent>();
    }
    public class FsmFloat { public string Name = ""; public float Value; }
    public class FsmString { public string Value = ""; }
    public class FsmBool { public bool Value; }
    public class FsmGameObject { public UnityEngine.GameObject? Value; }
    public class FsmMaterial { public UnityEngine.Material? Value; }
    public class FsmVariables
    {
        public static readonly FsmVariables GlobalVariables = new FsmVariables();
        public readonly Dictionary<string, object> Vars = new Dictionary<string, object>();
        public FsmFloat? FindFsmFloat(string n) => Vars.TryGetValue(n, out var v) ? v as FsmFloat : null;
        public FsmGameObject? FindFsmGameObject(string n) => Vars.TryGetValue(n, out var v) ? v as FsmGameObject : null;
        public FsmMaterial? FindFsmMaterial(string n) => Vars.TryGetValue(n, out var v) ? v as FsmMaterial : null;
        public FsmBool? FindFsmBool(string n) => Vars.TryGetValue(n, out var v) ? v as FsmBool : null;
    }
}
public class PlayMakerFSM : UnityEngine.MonoBehaviour
{
    public string FsmName = "";
    public Fsm Fsm = new Fsm();
    public FsmVariables FsmVariables = new FsmVariables();
    public string ActiveStateName = "";
    public void SendEvent(string name)
    {
        foreach (var t in Fsm.GlobalTransitions)
            if (t.EventName == name) { ActiveStateName = t.ToState; Array.Find(Fsm.States, x => x.Name == t.ToState)!.Enter(); return; }
    }
}
namespace WinterMP.Core
{
    public static class WinterMPPlugin { public static readonly Logger Log = new Logger(); }
    public class Logger { public void LogError(object m) { } public void LogInfo(object m) { } public void LogDebug(object m) { } }
}
namespace WinterMP.Core.Diagnostics { public static class SyncEventLog { public static void Record(string a, string b) { } } }
namespace WinterMP.Core.Catalog
{
    internal static class SyncCatalog { public static PaneScrapeData? PaneScrape; }
    internal static partial class SyncCatalogJson
    { private static string RequiredString(Dictionary<string, object?> d, string k) => (string)d[k]!; }
    public static class ScenePath { public static string Of(UnityEngine.Transform t) => t.gameObject.name; }
}
namespace WinterMP.Core.Session
{
    public enum SessionState { Idle, Hosting, Connected }
    public class RemotePlayer
    {
        public byte PlayerId;
        public bool IsDead;
        public float LastTransformTime = 10;
        public UnityEngine.Vector3 Position;
        public UnityEngine.Quaternion Rotation = UnityEngine.Quaternion.identity;
        public byte MoveState;
    }
    public class SessionManager
    {
        public static SessionManager? Instance;
        public SessionState State = SessionState.Hosting;
        public bool IsHost = true;
        public byte LocalPlayerId;
        public readonly List<RemotePlayer> Players = new List<RemotePlayer>();
        public event Action<RemotePlayer>? PlayerLeft;
        public readonly List<IMessage> Sent = new List<IMessage>();
        public Action<IMessage>? Sending;
        public void SendWorldMessage(IMessage m, Channel c) { Sent.Add(m); Sending?.Invoke(m); }
        public bool IsPassengerInVehicle(byte a, uint v) => false;
        public void Leave(RemotePlayer p) { Players.Remove(p); PlayerLeft?.Invoke(p); }
    }
}
namespace WinterMP.Core.Sync
{
    public class SyncedItem
    {
        public uint Id;
        public string Path = "";
        public bool IsVehicle, LocallyOwned;
        public byte RemoteOwner = 255;
        public UnityEngine.Rigidbody? Body;
    }
    public class ItemWorldSync { public readonly Dictionary<uint, SyncedItem> Items = new Dictionary<uint, SyncedItem>(); }
    public class WorldSyncManager { public static WorldSyncManager? Instance; public ItemWorldSync ItemSync = new ItemWorldSync(); }
    public class DeathSyncManager { public static DeathSyncManager? Instance; public bool IsLocalDead; }
    public class PlayerSyncManager
    {
        public static PlayerSyncManager? Instance = new PlayerSyncManager();
        public UnityEngine.Vector3 Position;
        public bool TryReadLocalPose(out UnityEngine.Vector3 p, out UnityEngine.Quaternion q)
        { p = Position; q = UnityEngine.Quaternion.identity; return true; }
    }
    public static class Conversions
    {
        public static NetVector3 ToNet(this UnityEngine.Vector3 v) => new NetVector3(v.x,v.y,v.z);
        public static UnityEngine.Vector3 ToUnity(this NetVector3 v) => new UnityEngine.Vector3(v.X,v.Y,v.Z);
    }
}
