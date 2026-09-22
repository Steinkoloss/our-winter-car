using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class SparkplugFittingChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);

        internal static void Run(Action<string, Action> check)
        {
            using (var f = new Fixture())
            {
                check("sparkplug fitting: native generic installer and part entry are accepted", () =>
                {
                    CallStatic("ValidatePartSlotInstaller", f.Installer, f.Catalog);
                    CallStatic("ValidatePartFitEntry", f.Part);
                    Require((bool)Get(f.Part, "FitValidated") && !(bool)Get(f.Part, "FitFailed"), "Native part entry was rejected.");
                });
                for (int i = 1; i <= 4; i++)
                {
                    int slot = i;
                    check("sparkplug fitting: native socket " + slot + " maps to cylinder " + (5 - slot), () =>
                    {
                        f.Loose(slot);
                        CallStatic("ValidatePartFitMount", f.Mounts[slot - 1], f.Catalog, true, null);
                        var args = new object?[] { f.Part, (byte)0 };
                        var mount = Call(f.Sync, "GetPartFitMount", args);
                        Require(ReferenceEquals(mount, f.Mounts[slot - 1]) && (byte)args[1]! == slot
                            && f.Mounts[slot - 1].gameObject.name == "VINP_Sparkplug" + (5 - slot), "Native socket selection disagreed with its array.");
                    });
                }
                check("sparkplug fitting: occupied closest socket is retained for host rejection", () =>
                {
                    f.Loose(2); f.Mounts[1].FsmVariables.FindFsmBool("Installed").Value = true;
                    var args = new object?[] { f.Part, (byte)0 };
                    Require(ReferenceEquals(Call(f.Sync, "GetPartFitMount", args), f.Mounts[1]) && (byte)args[1]! == 2,
                        "Selection skipped an occupied socket and chose a different cylinder.");
                });
                check("sparkplug fitting: inactive nearest socket does not select another cylinder", () =>
                {
                    f.Loose(3); var obj = f.Mounts[2].gameObject; obj.SetActive(false);
                    try { Require(Call(f.Sync, "GetPartFitMount", new object?[] { f.Part, (byte)0 }) == null, "Inactive socket was accepted."); }
                    finally { obj.SetActive(true); }
                });
                check("sparkplug fitting: duplicate socket array disables only that part", () =>
                {
                    f.Loose(1); object original = f.Slots[2]; f.Slots[2] = f.Slots[1];
                    try
                    {
                        Require(Call(f.Sync, "GetPartFitMount", new object?[] { f.Part, (byte)0 }) == null
                            && (bool)Get(f.Part, "FitFailed") && !(bool)Get(f.Factory, "Failed"), "Ambiguous slots did not fail locally.");
                    }
                    finally { f.Slots[2] = original; Set(f.Part, "FitFailed", false); }
                });
                check("sparkplug removal: native layer-12 mouse bindings are accepted", () =>
                {
                    CallStatic("ValidatePartRemoval", f.Part);
                    Require((bool)Get(f.Part, "RemovalValidated") && !(bool)Get(f.Part, "RemovalFailed"), "Native removal was rejected.");
                });
                foreach (string name in new[] { "Mouse off", "Mouse over" })
                {
                    string state = name;
                    check("sparkplug removal: wrong " + state + " pick layer is rejected", () =>
                    {
                        var pick = State(f.Data, state).Actions[1]; var original = Get(pick, "layerMask");
                        try
                        {
                            Set(pick, "layerMask", new[] { new FsmInt(19) }); f.ResetRemoval(); CallStatic("ValidatePartRemoval", f.Part);
                            Require((bool)Get(f.Part, "RemovalFailed") && !(bool)Get(f.Part, "RemovalValidated"), "Ordinary part layer was accepted for a plug.");
                        }
                        finally { Set(pick, "layerMask", original); f.ResetRemoval(); }
                    });
                }
                for (int i = 1; i <= 4; i++)
                {
                    int slot = i;
                    check("sparkplug removal: fitted loose socket " + slot + " becomes available on its native tool layer", () =>
                    {
                        f.Fitted(slot, 0);
                        Require(f.Data.gameObject.layer == 12 && f.Ready(), "Loose fitted plug remained unavailable.");
                        Require(f.Mounts[slot - 1].FsmVariables.FindFsmFloat("Tightness").Value == 0
                            && !f.Mounts[slot - 1].FsmVariables.FindFsmBool("Bolted").Value, "Native BOLTING disagreed with the loose part.");
                    });
                }
                check("sparkplug removal: tightened plug is blocked and native loosening restores availability", () =>
                {
                    f.Fitted(1, 1); Require(!f.Ready(), "Tight plug was removable.");
                    // Refreshing from Data matters: Screw.Set temporarily turns
                    // its Tightness scratch into a negative visual offset.
                    NativeBagPartChecks.Fire(f.Screw, "Idle"); f.Screw.SendEvent("UNTIGHTEN");
                    Require(f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 0 && f.Ready(), "Native loosening did not restore removal.");
                });
                check("sparkplug removal: native tightening withdraws removal availability", () =>
                {
                    f.Fitted(1, 0); f.Screw.SendEvent("TIGHTEN");
                    Require(f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 1 && !f.Ready(), "Native tightening left removal enabled.");
                });
                check("sparkplug removal: wrong current layer stays blocked", () =>
                { f.Fitted(1, 0); f.Data.gameObject.layer = 19; Require(!f.Ready(), "Plug on another layer was accepted."); });
                check("sparkplug removal: disabled collider stays blocked", () =>
                { f.Fitted(1, 0); f.Pick.enabled = false; Require(!f.Ready(), "Disabled pick was accepted."); });
                check("sparkplug removal: non-trigger collider stays blocked", () =>
                { f.Fitted(1, 0); f.Pick.isTrigger = false; Require(!f.Ready(), "Solid pick was accepted."); });
                check("sparkplug removal: another part occupying the socket stays blocked", () =>
                { f.Fitted(1, 0); f.Mounts[0].FsmVariables.FindFsmGameObject("ActivePart").Value = f.Mounts[1].gameObject; Require(!f.Ready(), "Another part's socket was accepted."); });
                check("sparkplug removal: an empty socket stays blocked", () =>
                { f.Fitted(1, 0); f.Mounts[0].FsmVariables.FindFsmBool("Installed").Value = false; Require(!f.Ready(), "Uninstalled socket was accepted."); });
                check("sparkplug removal: a different parent stays blocked", () =>
                { f.Fitted(1, 0); f.Data.transform.SetParent(f.Mounts[1].transform, true); Require(!f.Ready(), "Mismatched parent was accepted."); });
                check("sparkplug removal: guest replica cannot authorize its own removal", () =>
                { f.Fitted(1, 0); Set(f.Part, "Replica", true); try { Require(!f.Ready(), "Replica authorized removal."); } finally { Set(f.Part, "Replica", false); } });
                check("sparkplug removal: native removal request still traverses the mount prerequisite", () =>
                {
                    f.Fitted(1, 0); var mount = f.Mounts[0];
                    mount.FsmVariables.FindFsmGameObject("db_Installed1").Value = f.Prerequisite.gameObject;
                    f.Prerequisite.FsmVariables.FindFsmBool("Installed").Value = true;
                    NativeBagPartChecks.Fire(f.Data, "Remove");
                    Require(mount.ActiveStateName == "Update 2" && mount.FsmVariables.FindFsmBool("Installed").Value
                        && f.Data.transform.parent == mount.transform && f.Data.GetComponent<Rigidbody>() == null,
                        "Native removal bypassed the obstructing assembly.");
                });
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root, Car;
            internal readonly PlayMakerFSM Data, Screw, Installer, Prerequisite;
            internal readonly PlayMakerFSM[] Mounts = new PlayMakerFSM[4];
            internal readonly BoxCollider Pick;
            internal readonly object Catalog, Rule, Factory, Part, Sync;
            internal readonly IList Slots;
            private readonly FsmGameObject[] _globals;
            private readonly FsmGameObject _reference;
            private readonly GameObject? _savedDatabase;
            private readonly object? _savedWorld;
            private int _slot = 1;

            internal Fixture(string file = "sparkplug-fitting-probe.json")
            {
                _savedWorld = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true).GetProperty("Instance", Static).GetValue(null, null);
                var catalogType = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); catalogType.GetMethod("EnsureLoaded", Static).Invoke(null, null);
                Catalog = catalogType.GetProperty("ReplacementParts", Static).GetValue(null, null); object? selectedRule = null;
                foreach (object rule in (IEnumerable)Get(Catalog, "Factories")) if ((string)Get(rule, "Prefix") == "SPRKPLUG0") selectedRule = rule;
                Rule = selectedRule ?? throw new InvalidOperationException("Missing spark-plug factory.");
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../" + file)) }, null);
                var json = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
                var rows = (List<object>)json["fsms"];
                Root = new GameObject("sparkplug fitting probe"); Root.SetActive(false); Car = new GameObject("CORRIS"); Car.SetActive(false);
                var database = Child(Car, "AssembyDatabase");
                Installer = NativeBagPartChecks.MakeFsm(database, NativeBagPartChecks.Find(rows, "CORRIS/AssembyDatabase", "Installer"));
                var globals = FsmVariables.GlobalVariables; _globals = globals.GameObjectVariables;
                _reference = globals.FindFsmGameObject("AssemblyDatabase");
                if (_reference == null)
                {
                    _reference = new FsmGameObject { Name = "AssemblyDatabase", UseVariable = true };
                    var values = new List<FsmGameObject>(_globals) { _reference }; globals.GameObjectVariables = values.ToArray();
                }
                _savedDatabase = _reference.Value; _reference.Value = database;
                var dataRow = NativeBagPartChecks.Find(rows, "SPRKPLUG0", "Data");
                foreach (Dictionary<string, object> state in (IEnumerable)dataRow["states"])
                    foreach (Dictionary<string, object> action in (IEnumerable)state["actions"])
                        if (!(bool)action["enabled"]) action["parameters"] = new List<object>();
                var partObject = Child(Root, "SPRKPLUG071"); Pick = partObject.AddComponent<BoxCollider>();
                Data = NativeBagPartChecks.MakeFsm(partObject, dataRow);
                Data.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, Value = Pick } };
                var objectVars = new List<FsmGameObject>(Data.FsmVariables.GameObjectVariables) { _reference }; Data.FsmVariables.GameObjectVariables = objectVars.ToArray();
                Data.FsmVariables.FindFsmGameObject("Owner").Value = partObject;
                Data.FsmVariables.FindFsmString("ID").Value = "SPRKPLUG071";
                Data.FsmVariables.FindFsmString("UTAssemblyID").Value = "SPRKPLUG071AID";
                Data.FsmVariables.FindFsmString("UTPos").Value = "SPRKPLUG071POS";
                Screw = NativeBagPartChecks.MakeFsm(partObject, NativeBagPartChecks.Find(rows, "SPRKPLUG0", "Screw"));
                Type? proxyType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if (assembly.GetName().Name == "Assembly-CSharp") proxyType = assembly.GetType("PlayMakerArrayListProxy");
                Require(proxyType != null, "Native slot array type unavailable.");
                Car.SetActive(true);
                var proxy = database.AddComponent(proxyType!); Set(proxy, "referenceName", "Sparkplugs");
                Slots = (IList)proxyType!.GetProperty("arrayList").GetValue(proxy, null); Slots.Clear(); Slots.Add(null);
                var arrayRow = (Dictionary<string, object>)((List<object>)json["arrayLists"])[0];
                var points = (List<object>)((Dictionary<string, object>)arrayRow["defaults"])["preFillGameObjectList"];
                Require(points.Count == 5 && Convert.ToInt32(((Dictionary<string, object>)points[0])["m_PathID"]) == 0, "Native slot table changed.");
                for (int i = 1; i <= 4; i++)
                {
                    string path = (string)((Dictionary<string, object>)points[i])["scenePath"];
                    var obj = Child(Root, path.Substring(path.LastIndexOf('/') + 1)); obj.transform.localPosition = new Vector3(i * .07f, 0, 0);
                    Mounts[i - 1] = NativeBagPartChecks.MakeFsm(obj, NativeBagPartChecks.Find(rows, path, "Data"));
                    Mounts[i - 1].FsmVariables.FindFsmGameObject("AssemblyPoint").Value = obj;
                    Slots.Add(obj);
                }
                Prerequisite = NativeBagPartChecks.MakeFsm(Child(Root, "prerequisite"), NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1110/VINP_Sparkplug1", "Data"));
                var world = Root.AddComponent(Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true)); ((Behaviour)world).enabled = false;
                var bridge = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true), Members, null,
                    new object[] { world, new Dictionary<PlayMakerFSM, bool>() }, null);
                Sync = Activator.CreateInstance(Items, Members, null, new[] { bridge }, null);
                Factory = Nested("ReplacementFactory"); Set(Factory, "Rule", Rule);
                Part = Nested("ReplacementBinding"); Set(Part, "Factory", Factory); Set(Part, "Data", Data); Set(Part, "NativeId", "SPRKPLUG071");
                Root.SetActive(true); Car.SetActive(true);
                NativeBagPartChecks.LoadActions(Data, dataRow, "Far", "Tightness?", "Bolted", "Unbolted", "Mouse off", "Mouse over", "Remove");
                NativeBagPartChecks.LoadActions(Screw, NativeBagPartChecks.Find(rows, "SPRKPLUG0", "Screw"), "Idle", "Set", "Screw 2", "Unscrew 2");
                NativeBagPartChecks.LoadActions(Installer, NativeBagPartChecks.Find(rows, "CORRIS/AssembyDatabase", "Installer"), "State 1", "Far", "Near");
                for (int i = 1; i <= 4; i++)
                {
                    string path = (string)((Dictionary<string, object>)points[i])["scenePath"];
                    NativeBagPartChecks.LoadActions(Mounts[i - 1], NativeBagPartChecks.Find(rows, path, "Data"), "Idle", "Allow install?", "Far", "Near", "Install 1", "Allow removal?");
                    NativeBagPartChecks.Start(Mounts[i - 1]);
                }
                NativeBagPartChecks.Start(Data); NativeBagPartChecks.Start(Screw); NativeBagPartChecks.Start(Installer); NativeBagPartChecks.Start(Prerequisite);
                NativeBagPartChecks.Fire(Installer, "State 1");
            }
            internal void Loose(int slot)
            {
                _slot = slot; Set(Part, "FitMount", null); Set(Part, "FitFailed", false);
                Data.FsmVariables.FindFsmInt("AssemblyID").Value = 0; Data.gameObject.layer = 19;
                Data.transform.SetParent(Root.transform, true); Data.transform.position = Mounts[slot - 1].transform.position;
                foreach (var mount in Mounts)
                {
                    mount.FsmVariables.FindFsmBool("Installed").Value = false;
                    mount.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = mount.gameObject;
                    NativeBagPartChecks.Fire(mount, "Idle");
                }
                NativeBagPartChecks.Fire(Data, "Stop");
            }
            internal void Fitted(int slot, float tightness)
            {
                _slot = slot; var mount = Mounts[slot - 1]; Pick.enabled = true; Pick.isTrigger = true;
                Data.transform.SetParent(mount.transform, false); Data.gameObject.tag = "Untagged";
                Data.FsmVariables.FindFsmGameObject("InstallPoint").Value = mount.gameObject;
                Data.FsmVariables.FindFsmInt("AssemblyID").Value = slot;
                Data.FsmVariables.FindFsmFloat("Tightness").Value = tightness;
                mount.FsmVariables.FindFsmBool("Installed").Value = true;
                mount.FsmVariables.FindFsmGameObject("ActivePart").Value = Data.gameObject;
                mount.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = mount.gameObject;
                NativeBagPartChecks.Fire(mount, "Update 2");
                Data.SendEvent("BOLTING"); NativeBagPartChecks.Fire(Screw, "Set");
            }
            internal void ResetRemoval() { Set(Part, "RemovalValidated", false); Set(Part, "RemovalFailed", false); }
            internal bool Ready() => (bool)Call(Sync, "NativeRemovalReady", Part, Mounts[_slot - 1], false)!;
            public void Dispose()
            {
                _reference.Value = _savedDatabase; FsmVariables.GlobalVariables.GameObjectVariables = _globals;
                UnityEngine.Object.DestroyImmediate(Root); UnityEngine.Object.DestroyImmediate(Car);
                Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true).GetProperty("Instance", Static).GetSetMethod(true).Invoke(null, new[] { _savedWorld });
            }
        }

        private static FsmState State(PlayMakerFSM fsm, string name) => NativeBagPartChecks.State(fsm, name);
        private static GameObject Child(GameObject parent, string name) { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
        private static object Nested(string name) => Activator.CreateInstance(Items.GetNestedType(name, BindingFlags.NonPublic), true);
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Members).Invoke(target, args);
        private static object? CallStatic(string name, params object?[] args) => Items.GetMethod(name, Static).Invoke(null, args);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
