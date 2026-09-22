// Counted portable boundaries only. These do not execute Unity, physics, Steam or saves.
using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using WinterMP.Net;
using WinterMP.Net.Messages;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        private static readonly List<Object> Pending = new List<Object>();
        public static void Destroy(Object obj) { if (!Pending.Contains(obj)) Pending.Add(obj); }
        public static void CompleteDestruction() { foreach (var o in Pending) o.Destroyed = true; Pending.Clear(); }
        public static void Reset() { Pending.Clear(); }
        public static bool operator ==(Object? a, Object? b) => ReferenceEquals(a, b)
            || (ReferenceEquals(a, null) && b!.Destroyed) || (ReferenceEquals(b, null) && a!.Destroyed);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class GameObject : Object
    {
        public static readonly Dictionary<string, GameObject> Scene = new Dictionary<string, GameObject>();
        public string name, tag = "PART";
        public readonly Transform transform;
        public bool activeInHierarchy = true, activeSelf = true;
        private readonly List<Component> _components = new List<Component>();
        public GameObject(string path) { name = path.Split('/').Last(); transform = new Transform(this); Scene[path] = this; }
        public static GameObject? Find(string path) => Scene.TryGetValue(path, out var o) ? o : null;
        public T AddComponent<T>() where T : Component, new() => Add(new T());
        public T Add<T>(T component) where T : Component { component.gameObject = this; _components.Add(component); return component; }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault()!;
        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();
        public void SetActive(bool value) { activeSelf = activeInHierarchy = value; }
    }
    public class Transform : Component
    {
        public Transform? parent;
        public Vector3 position;
        public Vector3 localEulerAngles;
        public Vector3 forward = new Vector3(0,0,1);
        public Quaternion rotation;
        public readonly Dictionary<string, Transform> Children = new Dictionary<string, Transform>();
        public Transform(GameObject obj) { gameObject = obj; }
        public Transform? Find(string name) => Children.TryGetValue(name, out var t) ? t : null;
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
    }
    public class Rigidbody : Component { }
    public class Camera : Component { public static Camera? main; }
    public struct RaycastHit { public Collider collider; }
    public static class Physics
    {
        public const int DefaultRaycastLayers = -5;
        public static Collider? Hit;
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask)
        { Xunit.Assert.Equal(1f,distance); hit = new RaycastHit { collider=Hit! }; return Hit != null; }
    }
    public class FixedJoint : Component { }
    public class Collider : Component { public bool enabled = true; public Bounds bounds = new Bounds(); }
    public class BoxCollider : Collider { public bool isTrigger = true; }
    public class Bounds { public bool Overlaps = true; public bool Intersects(Bounds b) => Overlaps && b.Overlaps; }
    public static class Time { public static float unscaledTime = 10; public static int frameCount = 10; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero => new Vector3();
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
    }
    public struct Quaternion { }
    public static class Mathf { public static float Clamp(float f, float min, float max) => Math.Clamp(f, min, max); }
}
namespace HutongGames.PlayMaker
{
    public class FsmStateAction
    {
        public bool Enabled = true;
        public virtual void Reset() { }
        public virtual void OnEnter() { }
        public void Finish() { }
    }
    public class FsmState
    {
        public string Name = "";
        public bool IsInitialized = true;
        public FsmStateAction[] Actions = Array.Empty<FsmStateAction>();
        public void Enter() { foreach (var a in Actions) if (a.Enabled) a.OnEnter(); }
    }
    public class FsmEvent { public string Name = ""; public static FsmEvent GetFsmEvent(string n) => new FsmEvent { Name = n }; }
    public class FsmTransition { public FsmEvent FsmEvent = null!; public string ToState = ""; public string EventName => FsmEvent.Name; }
    public class Fsm
    {
        public bool Initialized = true, Started = true, RestartOnEnable = true;
        public FsmState[] States = Array.Empty<FsmState>();
        public FsmTransition[] GlobalTransitions = Array.Empty<FsmTransition>();
        public FsmEvent[] Events = Array.Empty<FsmEvent>();
        public void Init(PlayMakerFSM owner) { Initialized = true; }
    }
    public class FsmFloat { public string Name = ""; public float Value; }
    public class FsmString { public string Name = ""; public string Value = ""; }
    public class FsmOwnerDefault { public FsmGameObject GameObject = new FsmGameObject(); }
    public class FsmInt { public string Name = ""; public int Value; }
    public class FsmBool { public string Name = ""; public bool Value; }
    public class FsmGameObject { public string Name = ""; public UnityEngine.GameObject? Value; }
    public class FsmVariables
    {
        public readonly Dictionary<string, object> Vars = new Dictionary<string, object>();
        public FsmFloat FindFsmFloat(string n) => Vars.TryGetValue(n, out var v) ? (v as FsmFloat)! : null!;
        public FsmInt FindFsmInt(string n) => Vars.TryGetValue(n, out var v) ? (v as FsmInt)! : null!;
        public FsmBool FindFsmBool(string n) => Vars.TryGetValue(n, out var v) ? (v as FsmBool)! : null!;
        public FsmGameObject FindFsmGameObject(string n) => Vars.TryGetValue(n, out var v) ? (v as FsmGameObject)! : null!;
    }
}
public class PlayMakerFSM : UnityEngine.MonoBehaviour
{
    public string FsmName = "";
    public string ActiveStateName = "Wait";
    public Fsm Fsm = new Fsm();
    public FsmVariables FsmVariables = new FsmVariables();
    public readonly List<string> Events = new List<string>();
    public void SendEvent(string name)
    {
        Events.Add(name);
        foreach (var t in Fsm.GlobalTransitions)
            if (t.EventName == name) { Array.Find(Fsm.States, x => x.Name == t.ToState)!.Enter(); return; }
    }
}
namespace WoodstoveGameplay.Tests
{
    // Audited action names/fields, but counted doubles, never claimed as native execution.
    public class GetRandomChild : FsmStateAction { }
    public class ActivateGameObject : FsmStateAction { }
    public class IntCompare : FsmStateAction { public FsmInt integer1 = null!, integer2 = new FsmInt { Value = 4 }; }
    public class IntAdd : FsmStateAction
    {
        public FsmInt intVariable = null!, add = new FsmInt { Value = 1 };
        public bool everyFrame;
        public override void OnEnter() { intVariable.Value += add.Value; }
    }
    public class SetFsmInt : FsmStateAction { public Action? Write; public override void OnEnter() => Write?.Invoke(); }
    public class DestroyObject : FsmStateAction
    {
        public FsmGameObject gameObject = null!;
        public FsmFloat delay = new FsmFloat();
        public FsmBool detachChildren = new FsmBool();
        public int Calls;
        public bool Throw, OmitDestruction;
        public Action? During;
        public readonly List<UnityEngine.GameObject> Retired = new List<UnityEngine.GameObject>();
        public override void OnEnter()
        {
            Calls++; During?.Invoke();
            if (!OmitDestruction) { Retired.Add(gameObject.Value!); UnityEngine.Object.Destroy(gameObject.Value!); }
            if (Throw) throw new InvalidOperationException("counted partial native boundary failure");
        }
    }
}
namespace WinterMP.Core
{
    public static class WinterMPPlugin { public static readonly Logger Log = new Logger(); }
    public class Logger
    {
        public readonly List<string> Errors = new List<string>();
        public void LogError(object m) { Errors.Add(m.ToString()!); }
        public void LogInfo(object m) { }
        public void LogDebug(object m) { }
        public void LogWarning(object m) { }
    }
}
namespace WinterMP.Core.Diagnostics { public static class SyncEventLog { public static void Record(string a, string b) { } } }
namespace WinterMP.Core.Catalog
{
    internal static class SyncCatalog { public static WoodstoveFuelData? WoodstoveFuel = new WoodstoveFuelData(); }
    internal static partial class SyncCatalogJson
    { private static string RequiredString(Dictionary<string, object?> d, string k) => (string)d[k]!; }
}
namespace WinterMP.Core.Session
{
    public class RemotePlayer
    {
        public byte PlayerId;
        public bool IsDead;
        public float LastTransformTime = 10;
        public UnityEngine.Vector3 Position;
    }
    public class SessionManager
    {
        public static SessionManager? Instance;
        public bool IsHost = true;
        public byte LocalPlayerId;
        public readonly List<RemotePlayer> Players = new List<RemotePlayer>();
        public int PlayerCount => Players.Count;
        public readonly List<IMessage> Sent = new List<IMessage>();
        public Action<IMessage>? Sending;
        public void SendWorldMessage(IMessage m, Channel c)
        {
            Xunit.Assert.Equal(Channel.ReliableOrdered, c);
            var wire = PacketCodec.Decode(PacketCodec.Encode(m));
            Sent.Add(wire); Sending?.Invoke(wire);
        }
    }
}
namespace WinterMP.Core.Sync
{
    internal static class WorldSyncIds { public const byte NoOwner = 255; }
    public class SyncedItem
    {
        public byte RemoteOwner = 255, LastRemoteSequenceOwner = 255;
        public float LastRemoteAt, LastRemoteReleaseAt;
        public UnityEngine.Rigidbody Body = null!;
    }
    public class ItemWorldSync
    {
        public readonly List<uint> Unbound = new List<uint>();
        public void UnbindCabinWood(uint id) { Unbound.Add(id); }
    }
    public class WorldSyncManager { public static WorldSyncManager? Instance; public ItemWorldSync ItemSync = new ItemWorldSync(); }
    public class DeathSyncManager { public static DeathSyncManager? Instance; public bool IsLocalDead; }
    public class PlayerSyncManager
    {
        public static PlayerSyncManager? Instance = new PlayerSyncManager();
        public UnityEngine.Vector3 Feet;
        public bool Present = true;
        public bool TryReadLocalPose(out UnityEngine.Vector3 p, out UnityEngine.Quaternion q)
        { p = Feet; q = new UnityEngine.Quaternion(); return Present; }
    }
    public static class Conversions
    {
        public static NetVector3 ToNet(this UnityEngine.Vector3 v) => new NetVector3(v.x,v.y,v.z);
        public static NetQuaternion ToNet(this UnityEngine.Quaternion v) => NetQuaternion.Identity;
    }
    internal sealed partial class HeatSourceSync
    {
        // The finite-resource factory/materialization boundary is not exercised here.
        // Tests supply already-admitted pieces; the actual binding, host observation,
        // native-adapter entry, pending poll, publication and guest application ARE linked.
        private static UnityEngine.GameObject FindCabinLogPrefab(Catalog.WoodstoveFuelData c) => new UnityEngine.GameObject("test-prefab");
        private void ScanCabinWood() { }
        private void MaterializeCabinWood() { }
        internal void AdmitTestPiece(uint id, UnityEngine.GameObject piece, SyncedItem item)
            => _cabinWood.Add(id, new CabinWood { Id = id, Shape = 1, Piece = piece, Item = item, Replica = false });
        internal WoodstoveFuelAuthorityAccess Test => new WoodstoveFuelAuthorityAccess(this);
        internal sealed class WoodstoveFuelAuthorityAccess
        {
            private readonly HeatSourceSync _owner;
            internal WoodstoveFuelAuthorityAccess(HeatSourceSync owner) { _owner = owner; }
            internal uint Epoch => _owner._fuelEpoch;
            internal bool Faulted => _owner._cabinFailed || _owner._fuelAuthority?.Faulted == true;
            internal WinterMP.Net.Sync.WoodstoveFuelClient? Client => _owner._fuelClient;
        }
    }
}
