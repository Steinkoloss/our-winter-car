using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static class SparkplugOpeningChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
        private static readonly Type World = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);

        internal static void Run(Action<string, Action> check)
        {
            using (var f = new Fixture())
            {
                check("sparkplug box: native fixed capacity and direct contents template validate", () =>
                {
                    Require(!(bool)Get(f.BoxFactory, "Failed") && !(bool)Get(f.PartFactory, "Failed"), "A native factory was rejected.");
                    Require(f.BoxTemplate.FsmVariables.FindFsmInt("QuantityMax") == null, "Fixture invented a quantity maximum variable.");
                    Require(f.PartTemplate.FsmVariables.FindFsmBool("Installed") == null, "Fixture invented a plug Installed variable.");
                });
                foreach (string field in new[] { "fsmName", "variableName", "setValue", "everyFrame" })
                {
                    string changed = field;
                    check("sparkplug box: changed direct spawn " + changed + " is refused", () =>
                    {
                        var action = State(f.BoxTemplate, "Create Plug").Actions[3]; var original = Get(action, changed);
                        object bad = changed == "everyFrame" ? (object)true : changed == "setValue" ? new FsmGameObject { Name = "Other", UseVariable = true } : new FsmString { Value = "Other" };
                        Set(action, changed, bad);
                        try { Reject(() => CallStatic("ValidatePackageOpening", f.BoxTemplate, f.Packages, Get(f.BoxFactory, "Rule"))); }
                        finally { Set(action, changed, original); }
                    });
                }
                check("sparkplug box: changed fixed load capacity is refused", () =>
                {
                    var clamp = State(f.BoxTemplate, "Load").Actions[1]; var original = Get(clamp, "maxValue"); Set(clamp, "maxValue", new FsmInt(5));
                    try { Reject(() => CallStatic("ValidatePackageTemplate", f.BoxFactory, f.Packages)); }
                    finally { Set(clamp, "maxValue", original); }
                });
                check("sparkplug: individual factory refuses a shopping-bag spawn point", () =>
                {
                    var create = State(f.PartSpawner, "Create product").Actions[1]; var original = Get(create, "spawnPoint");
                    Set(create, "spawnPoint", new FsmGameObject { Name = "ShoppingBagSpawn", UseVariable = true });
                    // Production hooks have already been appended; validate the native action list only.
                    try { f.ValidatePartOutputRejects(); }
                    finally { Set(create, "spawnPoint", original); }
                });
                check("sparkplug box: native creation and initialization retain persistent identity", () =>
                {
                    f.CreateBox(); Require(f.Box.NativeId == "sparkplugbox01" && f.Box.State.Quantity == 4, "Native box identity or initial quantity changed.");
                    Require(f.Box.Use.gameObject.name == "spark plug box(Clone)" && f.Box.Use.FsmVariables.FindFsmGameObject("CreateItemsDB").Value == f.PartSpawner.gameObject,
                        "Native box display or contents target changed.");
                });
                for (int index = 1; index <= 4; index++)
                {
                    int number = index;
                    check("sparkplug box: opening " + number + " creates one native plug and replay creates none", () =>
                    {
                        var request = f.Request((uint)number); f.Open(request);
                        Require(f.Box.State.Quantity == 4 - number && f.Parts.Count == number, "Opening quantity/output count diverged.");
                        var part = f.Parts[number - 1]; Require(part.NativeId == "SPRKPLUG0" + number
                            && part.Scalars.Length == 3 && part.Scalars[0] >= 90 && part.Scalars[0] <= 99 && part.Scalars[1] == 0
                            && part.Scalars[2] == .75f, "Native plug identity/condition changed.");
                        var output = f.PartSpawner.FsmVariables.FindFsmGameObject("New").Value;
                        for (int retry = 0; retry < 3; retry++) Call(f.Sync, "OnHostPackageOpen", request, (byte)1);
                        Require(f.PartSpawner.FsmVariables.FindFsmGameObject("New").Value == output && f.Box.State.Quantity == 4 - number, "Retried opening created another plug.");
                        var receipt = ((PackageOpenLedger)Get(f.Sync, "_packageOpenLedger")).Inspect(request, 1, out bool begin);
                        PartIdentity.TryItemId(part.NativeId, out uint id);
                        Require(!begin && receipt != null && receipt.Status == PackageOpenStatus.Accepted && receipt.ProducedItemId == id, "Opening receipt lost the native output identity.");
                    });
                }
                check("sparkplug box: empty opening leaves the native counter unchanged", () =>
                {
                    var request = f.Request(5); Call(f.Sync, "OnHostPackageOpen", request, (byte)1);
                    var receipt = ((PackageOpenLedger)Get(f.Sync, "_packageOpenLedger")).Inspect(request, 1, out _);
                    Require(receipt != null && receipt.Status == PackageOpenStatus.Empty
                        && f.PartSpawner.FsmVariables.FindFsmInt("ObjectNumberInt").Value == 4 && f.Box.Use.ActiveStateName == "Empty", "Empty box produced a plug.");
                });
                check("sparkplug: native saved-output path retains the requested persistent ID", () =>
                {
                    f.PartSpawner.FsmVariables.FindFsmString("ID").Value = "SPRKPLUG042"; Fire(f.PartSpawner, "Create");
                    var output = f.PartSpawner.FsmVariables.FindFsmGameObject("New").Value; f.Created.Add(output);
                    Require(output.name == "SPRKPLUG042" && output.GetComponent<PlayMakerFSM>().FsmVariables.FindFsmGameObject("InstallPoint").Value == null,
                        "Saved plug creation changed ID or invented a mount."); Fire(f.PartSpawner, "Idle");
                });
                f.CheckGuest(check);
            }
        }

        private sealed class HostBox
        {
            internal PlayMakerFSM Use = null!;
            internal string NativeId = "";
            internal uint Id;
            internal object Sync = null!;
            internal PackageState State => (PackageState)Call(Sync, "BuildPackageState", Id)!;
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root, Spawner, Controller, Templates;
            internal readonly PlayMakerFSM BoxTemplate, PartTemplate, BoxSpawner, PartSpawner;
            internal readonly object Sync, Bridge, Packages, Replacements, BoxFactory, PartFactory;
            internal readonly SessionManager Session;
            internal readonly List<GameObject> Created = new List<GameObject>();
            internal readonly List<ReplacementPartState> Parts = new List<ReplacementPartState>();
            internal HostBox Box = null!;
            private readonly object _world;
            private readonly object? _savedSession, _savedWorld;
            private readonly List<object> _rows;
            private readonly RemotePlayer _player;

            internal Fixture()
            {
                _savedSession = SessionManager.Instance; _savedWorld = World.GetProperty("Instance", Static).GetValue(null, null);
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
                Packages = catalog.GetProperty("PartsPackages", Static).GetValue(null, null); Replacements = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null);
                var reader = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var input = Activator.CreateInstance(reader, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../sparkplug-opening-probe.json")) }, null);
                _rows = (List<object>)((Dictionary<string, object>)reader.GetMethod("ReadObject", Members).Invoke(input, null))["fsms"];
                Root = new GameObject("sparkplug test"); Root.SetActive(false); Templates = Child(Root, "templates");
                Spawner = new GameObject("Spawner"); Spawner.SetActive(false); var items = Child(Spawner, "CreateItems");
                Controller = Child(Root, "controller"); Session = Controller.AddComponent<SessionManager>(); Session.enabled = false;
                _world = Controller.AddComponent(World); ((Behaviour)_world).enabled = false;
                Bridge = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true), Members, null, new object[] { _world, new Dictionary<PlayMakerFSM, bool>() }, null);
                Sync = Activator.CreateInstance(Items, Members, null, new[] { Bridge }, null);
                var vehicles = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true), Members, null, new[] { Bridge, Sync }, null);
                var fsms = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.FsmWorldSync", true), Members, null, new[] { Bridge, vehicles }, null);
                Call(Sync, "BindVehicles", vehicles); Call(Bridge, "BindItems", Sync); Call(Bridge, "BindFsms", fsms);
                Set(_world, "_bridge", Bridge); Set(_world, "_items", Sync); Set(_world, "_vehicles", vehicles); Set(_world, "_fsm", fsms);
                BoxTemplate = Make(Body("sparkplugbox0"), "sparkplugbox0", "Use"); PartTemplate = Make(Body("SPRKPLUG0"), "SPRKPLUG0", "Data");
                BoxSpawner = Make(items, "Spawner/CreateItems", "Sparkplugs"); PartSpawner = Make(items, "Spawner/CreateItems", "Sparkplug");
                Root.SetActive(true); Spawner.SetActive(true);
                Load(BoxTemplate, "sparkplugbox0", "Use", "State 1", "Load", "Save", "State 6", "Is garbage", "Create Plug", "Empty", "Check quantity");
                Load(PartTemplate, "SPRKPLUG0", "Data", "Init", "Status", "Far");
                Load(BoxSpawner, "Spawner/CreateItems", "Sparkplugs", "Create product", "Create");
                Load(PartSpawner, "Spawner/CreateItems", "Sparkplug", "Create product", "Create");
                foreach (string state in new[] { "Wait player 2", "Wait button 2", "Delay" })
                    State(BoxTemplate, state).Transitions = new FsmTransition[0];
                State(BoxSpawner, "Add ID").Transitions = new FsmTransition[0];
                State(PartSpawner, "Add ID").Transitions = new FsmTransition[0];
                // File probes use a disposable, nonexistent test path. The guest
                // materializer replaces Exists/Load before enabling either copy.
                var save = new FsmString { UseVariable = false, Value = Path.Combine(Application.dataPath, "../guest-save-probe/sparkplug-unused.dat") };
                foreach (string name in new[] { "State 1", "Load", "Save", "State 6" })
                    foreach (var action in State(BoxTemplate, name).Actions)
                        if (action.GetType().GetField("saveFile") != null) Set(action, "saveFile", save);
                BoxTemplate.Fsm.StartState = "State 2";
                BoxSpawner.FsmVariables.FindFsmGameObject("Prefab").Value = BoxTemplate.gameObject;
                BoxSpawner.FsmVariables.FindFsmGameObject("ShoppingBagSpawn").Value = Child(Root, "bag spawn");
                BoxSpawner.FsmVariables.FindFsmString("SaveID").Value = "sparkplugbox0";
                foreach (var action in new[] { State(BoxSpawner, "Create product").Actions[5], State(BoxSpawner, "Create").Actions[1] })
                    ((FsmGameObject)Get(action, "setValue")).Value = items;
                PartSpawner.FsmVariables.FindFsmGameObject("Prefab").Value = PartTemplate.gameObject;
                PartSpawner.FsmVariables.FindFsmString("SaveID").Value = "SPRKPLUG0";
                // Unity clones serialized ActionData, not the imported runtime
                // Actions array. Preserve the native actions on both test assets.
                foreach (var template in new[] { BoxTemplate, PartTemplate })
                    foreach (var state in template.Fsm.States) state.SaveActions();
                StaticProperty(typeof(SessionManager), "Instance", Session); StaticProperty(World, "Instance", _world);
                Property(Session, "IsHost", true); Property(Session, "State", SessionState.Hosting); Property(Session, "LocalPlayerId", (byte)0);
                _player = new RemotePlayer { PlayerId = 1, Peer = new PeerId(333), LastTransformTime = Time.unscaledTime };
                ((IDictionary)Get(Session, "_playersByPeer")).Add(_player.Peer, _player);
                NativeBagPartChecks.Start(BoxSpawner); Fire(BoxSpawner, "Idle"); NativeBagPartChecks.Start(PartSpawner); Fire(PartSpawner, "Idle");
                Call(Sync, "RefreshReplacementFactories"); Call(Sync, "RefreshPackageFactories");
                BoxFactory = ((IDictionary)Get(Sync, "_packageFactories"))[FactoryItemIdentity.FactoryId("Spawner/CreateItems", "Sparkplugs")];
                PartFactory = ((IDictionary)Get(Sync, "_replacementFactories"))[FactoryItemIdentity.FactoryId("Spawner/CreateItems", "Sparkplug")];
            }
            private GameObject Body(string name) { var obj = Child(Templates, name); obj.AddComponent<Rigidbody>().useGravity = false; obj.AddComponent<BoxCollider>(); return obj; }
            private PlayMakerFSM Make(GameObject obj, string path, string fsm) => NativeBagPartChecks.MakeFsm(obj, NativeBagPartChecks.Find(_rows, path, fsm));
            private void Load(PlayMakerFSM fsm, string path, string name, params string[] states) => NativeBagPartChecks.LoadActions(fsm, NativeBagPartChecks.Find(_rows, path, name), states);
            internal void ValidatePartOutputRejects()
            {
                var state = State(PartSpawner, "Create product"); var original = state.Actions; var native = new FsmStateAction[7]; Array.Copy(original, native, 7); state.Actions = native;
                try { Reject(() => CallStatic("ValidateReplacementOutput", PartFactory, Replacements, true)); } finally { state.Actions = original; }
            }
            internal void CreateBox()
            {
                Fire(BoxSpawner, "Create product"); var obj = BoxSpawner.FsmVariables.FindFsmGameObject("New").Value;
                Created.Add(obj); var use = obj.GetComponent<PlayMakerFSM>(); use.Fsm.StartState = "Probe idle";
                NativeBagPartChecks.Start(use); Fire(use, "State 1");
                Require(use.FsmVariables.FindFsmString("ID").Value == "sparkplugbox01", "Native GetName did not initialize box ID: " + use.FsmVariables.FindFsmString("ID").Value);
                Call(Sync, "ProcessPackageOutputs", Session);
                string nativeId = use.FsmVariables.FindFsmString("ID").Value;
                uint id = FactoryItemIdentity.ItemId(FactoryItemIdentity.FactoryId("Spawner/CreateItems", "Sparkplugs"), nativeId);
                Box = new HostBox { Sync = Sync, Use = use, NativeId = nativeId, Id = id };
                Require(Box.State != null, "Native box did not register.");
                // Audio is outside this isolated fixture; retain native quantity,
                // target assignment, dispatch and empty-box transitions unchanged.
                var open = State(use, "Create Plug"); open.Actions[1] = new Quiet(); open.Actions[1].Init(open);
            }
            internal PackageOpenRequest Request(uint sequence) => new PackageOpenRequest { PlayerId = 1, Token = 333, Sequence = sequence, ItemId = Box.Id, ExpectedRevision = Box.State.Revision };
            internal void Open(PackageOpenRequest request)
            {
                Fire(Box.Use, "Wait player 2"); _player.Position = Box.Use.transform.position; _player.LastTransformTime = Time.unscaledTime;
                Call(Sync, "OnHostPackageOpen", request, (byte)1);
                var output = PartSpawner.FsmVariables.FindFsmGameObject("New").Value; if (output == null) throw new InvalidOperationException("Opening did not create a plug."); Created.Add(output);
                var data = output.GetComponent<PlayMakerFSM>(); NativeBagPartChecks.Start(data); Fire(data, "Init");
                Require(Vector3.Distance(output.transform.position, Box.Use.transform.position) < .001f, "Plug spawned at another box or bag.");
                Call(Sync, "ProcessReplacementOutputs", Session); Call(Sync, "ProcessPackageOpening", Session);
                PartIdentity.TryItemId(data.FsmVariables.FindFsmString("ID").Value, out uint id);
                var state = (ReplacementPartState?)Call(Sync, "BuildReplacementPartState", id); Require(state != null, "Host output did not become a replacement state."); Parts.Add(state!);
            }
            internal void CheckGuest(Action<string, Action> check)
            {
                var hostBox = Box.State; var saved = Box.Use; var savedQuantity = saved.FsmVariables.FindFsmInt("Quantity").Value;
                foreach (var obj in Created)
                {
                    var data = obj != null ? obj.GetComponent<PlayMakerFSM>() : null;
                    if (data == null || data.FsmName != "Data") continue;
                    if (PartIdentity.TryItemId(data.FsmVariables.FindFsmString("ID").Value, out uint id))
                    {
                        Call(Sync, "RemoveTrackedItem", id, obj!.GetComponent<Rigidbody>());
                        Call(Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(Bridge, null), "Forget", data);
                    }
                    obj!.SetActive(false);
                }
                Call(Sync, "ClearPackages"); Call(Sync, "ClearReplacementParts");
                Property(Session, "IsHost", false); Property(Session, "State", SessionState.Connected); Property(Session, "LocalPlayerId", (byte)1);
                Call(Sync, "HideLocalPackage", saved);
                Call(Sync, "RefreshReplacementFactories"); Call(Sync, "RefreshPackageFactories");
                Call(Sync, "ProcessReplacementOutputs", Session); Call(Sync, "ProcessPackageOutputs", Session);
                check("sparkplug box: late join creates an empty replica with the host identity", () =>
                {
                    Call(Sync, "OnPackageState", hostBox); Set(Sync, "_nextPackagePoll", 0f); Call(Sync, "ProcessPackages", Session);
                    var bindings = (IDictionary)Get(Sync, "_packages"); Require(bindings.Contains(Box.Id), "Guest empty box was not materialized.");
                    var use = (PlayMakerFSM)Get(bindings[Box.Id], "Use"); Created.Add(use.gameObject);
                    Require(use != saved && use.Fsm.Started && use.FsmVariables.FindFsmString("ID").Value == hostBox.NativeId
                        && use.FsmVariables.FindFsmInt("Quantity").Value == 0 && use.FsmVariables.FindFsmInt("QuantityMax") == null
                        && use.gameObject.name == "empty(itemx)", "Guest box used local quantity, capacity or identity.");
                });
                check("sparkplug box: authoritative quantity update reuses its replica and guest opening cannot spawn", () =>
                {
                    var bindings = (IDictionary)Get(Sync, "_packages"); var use = (PlayMakerFSM)Get(bindings[Box.Id], "Use");
                    hostBox.Revision++; hostBox.Quantity = 2; Call(Sync, "OnPackageState", hostBox); Set(Sync, "_nextPackagePoll", 0f); Call(Sync, "ProcessPackages", Session);
                    int counter = PartSpawner.FsmVariables.FindFsmInt("ObjectNumberInt").Value; Fire(use, "Create Plug");
                    Require(use == (PlayMakerFSM)Get(bindings[Box.Id], "Use") && use.FsmVariables.FindFsmInt("Quantity").Value == 2
                        && use.gameObject.name == "spark plug box(Clone)" && PartSpawner.FsmVariables.FindFsmInt("ObjectNumberInt").Value == counter,
                        "Guest opening changed authoritative quantity or ran native creation: quantity=" + use.FsmVariables.FindFsmInt("Quantity").Value
                        + " name=" + use.gameObject.name + " counter=" + PartSpawner.FsmVariables.FindFsmInt("ObjectNumberInt").Value + "/" + counter
                        + " state=" + use.ActiveStateName + " action=" + State(use, "Create Plug").Actions[0].GetType().Name);
                    Require(Get(Sync, "_packageOpenClient") != null, "Guest did not queue the shared opening intent.");
                });
                check("sparkplug box: guest save and deletion entries preserve authoritative quantity", () =>
                {
                    var use = (PlayMakerFSM)Get(((IDictionary)Get(Sync, "_packages"))[Box.Id], "Use");
                    foreach (string state in new[] { "Save", "State 6" })
                    {
                        Fire(use, state); Require(use.FsmVariables.FindFsmInt("Quantity").Value == 2, "Guest persistence entry changed box quantity.");
                    }
                    Require(!File.Exists(Path.Combine(Application.dataPath, "../guest-save-probe/sparkplug-unused.dat")), "Native fixture unexpectedly wrote a save file.");
                });
                check("sparkplug: all four host outputs materialize once and preserve independent condition", () =>
                {
                    Require(Parts.Count == 4, "The native host did not produce all four plugs.");
                    var replicas = (IDictionary)Get(Sync, "_replacementParts");
                    for (int i = 0; i < Parts.Count; i++)
                    {
                        var state = Parts[i]; state.Revision++; state.Scalars[0] = 20 + i; state.Scalars[2] = .61f + i * .03f;
                        Call(Sync, "OnReplacementPartState", state); Set(Sync, "_nextReplacementPoll", 0f); Call(Sync, "ProcessReplacementParts", Session);
                        PartIdentity.TryItemId(state.NativeId, out uint id); Require(replicas.Contains(id), "Guest plug did not materialize: " + state.NativeId);
                        var data = (PlayMakerFSM)Get(replicas[id], "Data"); Created.Add(data.gameObject);
                        Require(data.FsmVariables.FindFsmFloat("Wear").Value == state.Scalars[0] && data.FsmVariables.FindFsmFloat("Durability").Value == state.Scalars[2]
                            && data.FsmVariables.FindFsmString("UTAssemblyID").Value == state.NativeId + "AID", "Replica initialization replaced host condition or identity.");
                        Call(Sync, "OnReplacementPartState", state); Set(Sync, "_nextReplacementPoll", 0f); Call(Sync, "ProcessReplacementParts", Session);
                        Require(data == (PlayMakerFSM)Get(replicas[id], "Data"), "Duplicate receipt created another plug.");
                    }
                    Require(saved.FsmVariables.FindFsmInt("Quantity").Value == savedQuantity, "Guest projection changed retained original box quantity.");
                });
            }
            public void Dispose()
            {
                Call(Sync, "ClearPackages"); Call(Sync, "ClearReplacementParts");
                foreach (var obj in Created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
                UnityEngine.Object.DestroyImmediate(Spawner); UnityEngine.Object.DestroyImmediate(Root);
                StaticProperty(typeof(SessionManager), "Instance", _savedSession); StaticProperty(World, "Instance", _savedWorld);
            }
        }
        private sealed class Quiet : FsmStateAction { public override void OnEnter() { Finish(); } }
        private static FsmState State(PlayMakerFSM fsm, string name) => NativeBagPartChecks.State(fsm, name);
        private static void Fire(PlayMakerFSM fsm, string state) => NativeBagPartChecks.Fire(fsm, state);
        private static GameObject Child(GameObject parent, string name) { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Members).Invoke(target, args);
        private static object? CallStatic(string name, params object?[] args) => Items.GetMethod(name, Static).Invoke(null, args);
        private static void Property(object target, string name, object value) => target.GetType().GetProperty(name, Members).SetValue(target, value, null);
        private static void StaticProperty(Type type, string name, object? value) => type.GetProperty(name, Static).SetValue(null, value, null);
        private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        private static void Reject(Action action)
        {
            try { action(); } catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
            throw new InvalidOperationException("Changed native binding was accepted.");
        }
    }
}
