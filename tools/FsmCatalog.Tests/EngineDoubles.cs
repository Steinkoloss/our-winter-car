using System;
using System.Collections.Generic;

// Portable metadata fixtures, not native PlayMaker/Unity execution.
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin : Attribute
    { public BepInPlugin(string guid, string name, string version) { } }
    public class BaseUnityPlugin
    {
        public Logging.ManualLogSource Logger = new Logging.ManualLogSource();
        public Configuration.ConfigFile Config = new Configuration.ConfigFile();
    }
    public static class Paths { public static string GameRootPath = "unused"; }
}
namespace BepInEx.Logging
{
    public class ManualLogSource { public void LogInfo(object v) { } public void LogWarning(object v) { } public void LogError(object v) { } }
}
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value = default!; }
    public class ConfigFile { public ConfigEntry<T> Bind<T>(string s, string k, T v, string d) => new ConfigEntry<T> { Value = v }; }
}
namespace WinterMP.Tools
{
    public static class MyPluginInfo
    { public const string PLUGIN_GUID = "fixture", PLUGIN_NAME = "fixture", PLUGIN_VERSION = "fixture"; }
}
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public class Object { public string name = ""; }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
    }
    public class GameObject : Object
    {
        public bool activeInHierarchy;
        public Transform transform;
        public readonly List<Component> Components = new List<Component>();
        public GameObject(string n) { name = n; transform = new Transform { name = n, gameObject = this }; Components.Add(transform); }
        public Component[] GetComponents(Type t) => Components.FindAll(c => t.IsAssignableFrom(c.GetType())).ToArray();
    }
    public class Transform : Component
    {
        public Transform? parent;
        public readonly List<Transform> Children = new List<Transform>();
        public int childCount => Children.Count;
        public Transform GetChild(int i) => Children[i];
    }
    public class Rigidbody : Component { public float mass; public bool isKinematic; }
    public static class Resources
    {
        public static Object[] Objects = Array.Empty<Object>();
        public static Object[] FindObjectsOfTypeAll(Type t) => Array.FindAll(Objects, o => t.IsAssignableFrom(o.GetType()));
    }
    public static class Application { public static string loadedLevelName = "fixture", unityVersion = "fixture"; }
    public static class Time { public static float realtimeSinceStartup; }
    public enum KeyCode { F9 }
    public static class Input { public static bool GetKeyDown(KeyCode k) => false; }
}
public class PlayMakerFSM : UnityEngine.Component
{
    public string FsmName = "PickUp";
    public FixtureFsm Fsm = new FixtureFsm();
}
public class FixtureFsm
{
    public string ActiveStateName = "Check item";
    public object[] States = Array.Empty<object>();
    public object[] Events = Array.Empty<object>();
    public object[] GlobalTransitions = Array.Empty<object>();
    public object Variables = new { GameObjectVariables = new object[] { new { Name = "Item" } } };
}
public class PlayMakerGlobals
{
    public static PlayMakerGlobals Instance = new PlayMakerGlobals();
    public object Variables = new { FloatVariables = new object[] { new SecretScalar() } };
}
public class SecretScalar
{
    public static int Reads;
    public string Name => "Water";
    public float Value { get { Reads++; return 123456.75f; } }
}
namespace HutongGames.PlayMaker
{
    public class FsmStateAction { public bool Enabled = true; public string RuntimeCache = "do-not-dump"; }
    public class FsmString
    {
        public string Name = "";
        public bool UseVariable;
        public bool IsNone;
        public virtual string Value => "dipper(itemx)";
    }
    public class FsmFloat
    {
        public string Name = "Water";
        public bool UseVariable = true;
        public bool IsNone;
        public float Value => throw new Exception("LIVE_VALUE_MUST_NOT_BE_READ");
    }
    public class FsmBool { public string Name = ""; public bool UseVariable; public bool IsNone; public bool Value = true; }
    public class FsmGameObject
    {
        public string Name = "";
        public bool UseVariable;
        public bool IsNone;
        public UnityEngine.GameObject Value = null!;
    }
    public class FsmObject
    {
        public string Name = "";
        public bool UseVariable;
        public bool IsNone;
        public UnityEngine.Object Value = null!;
    }
    public class FsmOwnerDefault { public OwnerDefaultOption OwnerOption = OwnerDefaultOption.SpecifyGameObject; public FsmGameObject GameObject = new FsmGameObject(); }
    public enum OwnerDefaultOption { UseOwner, SpecifyGameObject }
    public class FsmEvent { public string Name = "SAUNADIPPER"; }
    public class FsmProperty
    {
        public FsmObject TargetObject = new FsmObject();
        public string TargetTypeName = "PlayMakerFSM";
        public string PropertyName = "enabled";
        public bool setProperty = true;
        public FsmBool BoolParameter = new FsmBool();
        public string ArbitrarySaveData => throw new Exception("never call unknown properties");
    }
}
namespace HutongGames.PlayMaker.Actions
{
    using HutongGames.PlayMaker;
    public class StringCompare : FsmStateAction
    {
        public FsmString string2 = new FsmString();
        public FsmString string1 = new FsmString { Name = "ItemName", UseVariable = true };
        public FsmEvent equalEvent = new FsmEvent();
    }
    public class ActivateGameObject : FsmStateAction
    {
        public FsmOwnerDefault gameObject = new FsmOwnerDefault();
        public FsmBool activate = new FsmBool();
        public bool recursive = true;
        public FsmFloat water = new FsmFloat();
        public object opaque = new HostileObject();
        public string saveContents = "PRIVATE_SAVE_PAYLOAD";
        [NonSerialized] public float cachedResource = 987654;
        [UnityEngine.SerializeField] private FsmBool resetOnExit = new FsmBool();

        public FsmProperty targetProperty = new FsmProperty();
        public UnityEngine.GameObject[] targets = Array.Empty<UnityEngine.GameObject>();
    }
    public sealed class HostileObject { public override string ToString() => throw new Exception("no arbitrary ToString"); }
    public sealed class ThrowingString : FsmString { public override string Value => throw new Exception("PRIVATE_EXCEPTION_MESSAGE"); }
    public class SendEvent : FsmStateAction
    {
        public SendEvent() { Enabled = false; }
        public double delay = double.NaN;
        public bool everyFrame = true;
        public string sendEvent = new string('x', 513);
        public object[] recursive = new object[1];
        public object[] many = new object[300];
        public object opaque = new HostileObject();
    }
}
