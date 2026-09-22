using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class BagSpillChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);

        internal static void Run(Action<string, Action> check)
        {
            WinterMP.Core.Catalog.SyncCatalog.EnsureLoaded();
            var roots = new List<GameObject>();
            var sync = Activator.CreateInstance(Items, Fields, null, new object?[] { null }, null);
            var c = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("ShoppingBags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            Require(c != null, "Shopping bag catalog missing.");
            var spawner = Root(roots, "Spawner");
            var products = Root(roots, "CreateItems"); products.transform.SetParent(spawner.transform, false);
            var sourceObject = Root(roots, "BagContentsStore"); sourceObject.transform.SetParent(spawner.transform, false);
            var bagObject = Root(roots, "probe bag");
            var bagBody = bagObject.AddComponent<Rigidbody>(); bagBody.useGravity = false;
            var prefab = Root(roots, "chips"); var prefabBody = prefab.AddComponent<Rigidbody>(); prefabBody.useGravity = false;
            var partPrefab = Root(roots, "FANBELT0"); partPrefab.AddComponent<Rigidbody>().useGravity = false;
            var partData = MakeFsm(partPrefab, "Data", "Idle");
            var use = MakeFsm(prefab, "Use", "Idle", "Identity");
            var bagUse = MakeFsm(bagObject, "Use", "Idle");
            var factory = MakeFsm(products, "Chips", "Idle", "Create product");
            var partFactory = MakeFsm(products, "Fanbelt", "Idle", "Create product");
            var source = MakeFsm(sourceObject, "Logic", "Idle", "Dispatch", "Garbage");
            var consumed = new FsmBool { Name = "Consumed", UseVariable = true };
            bagUse.FsmVariables.BoolVariables = new[] { consumed };
            var output = new FsmGameObject { Name = "New", UseVariable = true };
            factory.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "Prefab", UseVariable = true, Value = prefab },
                output, new FsmGameObject { Name = "ShoppingBagSpawn", UseVariable = true, Value = bagObject } };
            var partOutput = new FsmGameObject { Name = "New", UseVariable = true };
            var partNativeId = new FsmString { Name = "ID", UseVariable = true, Value = "FANBELT01" };
            partFactory.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "Prefab", UseVariable = true, Value = partPrefab },
                partOutput, new FsmGameObject { Name = "ShoppingBagSpawn", UseVariable = true, Value = bagObject } };
            partFactory.FsmVariables.StringVariables = new[] { partNativeId };
            source.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "CurrentBag", UseVariable = true, Value = bagObject } };
            var product = new FsmString { Name = "ProductName", UseVariable = true, Value = "Chips" };
            source.FsmVariables.StringVariables = new[] { product };
            try
            {
                foreach (var root in roots) root.SetActive(true);
                SetActions(use, "Identity", Native("SetName", "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    "name", new FsmString { Value = "potato chips(itemx)" }));
                var create = Native("CreateObject", "gameObject", factory.FsmVariables.FindFsmGameObject("Prefab"),
                    "storeObject", output, "spawnPoint", factory.FsmVariables.FindFsmGameObject("ShoppingBagSpawn"));
                SetActions(factory, "Create product", create,
                    Native("SetName", "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = output },
                        "name", new FsmString { Value = "probe native product" }));
                State(factory, "Create product").Transitions = new[] { Transition("FINISHED", "Idle") };
                factory.Fsm.GlobalTransitions = new[] { Transition("SPAWNITEM", "Create product") };
                SetActions(partFactory, "Create product", Native("CreateObject", "gameObject", partFactory.FsmVariables.FindFsmGameObject("Prefab"),
                    "storeObject", partOutput, "spawnPoint", partFactory.FsmVariables.FindFsmGameObject("ShoppingBagSpawn")),
                    Native("SetName", "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = partOutput },
                        "name", new FsmString { Value = "Fan belt(VINXX)" }));
                State(partFactory, "Create product").Transitions = new[] { Transition("FINISHED", "Idle") };
                partFactory.Fsm.GlobalTransitions = new[] { Transition("SPAWNITEM", "Create product") };
                var target = new FsmEventTarget { target = FsmEventTarget.EventTarget.GameObjectFSM,
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = products } },
                    fsmName = product, sendToChildren = new FsmBool(false) };
                var send = Native("SendEventByName", "eventTarget", target, "sendEvent", new FsmString { Value = "SPAWNITEM" },
                    "delay", new FsmFloat(0), "everyFrame", false);
                SetActions(source, "Dispatch", send);
                State(source, "Dispatch").Transitions = new[] { Transition("FINISHED", "Idle") };
                var retireTarget = new FsmEventTarget { target = FsmEventTarget.EventTarget.GameObjectFSM,
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = source.FsmVariables.FindFsmGameObject("CurrentBag") },
                    fsmName = new FsmString { Value = "Use" }, sendToChildren = new FsmBool(false) };
                var retire = Native("SendEventByName", "eventTarget", retireTarget, "sendEvent", new FsmString { Value = "GARBAGE" },
                    "delay", new FsmFloat(0), "everyFrame", false);
                SetActions(source, "Garbage", retire);
                source.Fsm.GlobalTransitions = new[] { Transition("PROBE_SPILL", "Dispatch"),
                    Transition("PROBE_EMPTY", "Garbage"), Transition("PROBE_IDLE", "Idle") };
                foreach (var fsm in new[] { use, bagUse, factory, source, partData, partFactory })
                { fsm.enabled = true; if (!fsm.Fsm.Started) fsm.Fsm.Start(); }

                var replacement = Nested("ReplacementFactory"); var partRule = BagPartRule("Fanbelt");
                Set(replacement, "Rule", partRule); Set(replacement, "Fsm", partFactory);
                Set(replacement, "TemplateData", partData); Set(replacement, "Prefab", partPrefab);
                ((IDictionary)Get(sync, "_replacementFactories")).Add(Get(Get(partRule, "Identity"), "FactoryId"), replacement);

                var bound = Nested("BagSpillFactory"); Set(bound, "Fsm", factory); Set(bound, "Path", "Spawner/CreateItems");
                check("bag spill: native product binding accepts direct CreateObject output", () => Call(sync, "BindBagSpillFactory", bound, c!));
                var factories = (IDictionary)Get(sync, "_bagSpillFactories"); factories.Add(factory, bound);
                var partBound = Nested("BagSpillFactory"); Set(partBound, "Fsm", partFactory); Set(partBound, "Path", "Spawner/CreateItems");
                check("bag spill: Data-only part output uses its registered replacement adapter", () =>
                {
                    Call(sync, "BindBagSpillFactory", partBound, c!);
                    Require(ReferenceEquals(Get(partBound, "Replacement"), replacement) && Get(partBound, "Use") == null,
                        "Native part output was mistaken for a generic Use item.");
                });
                factories.Add(partFactory, partBound);
                check("bag spill: native dispatch with a delay is refused before hooks", () =>
                {
                    send.GetType().GetField("delay").SetValue(send, new FsmFloat(.5f));
                    try { Reject(() => Call(sync, "BindBagSpillSources", source, c!)); }
                    finally { send.GetType().GetField("delay").SetValue(send, new FsmFloat(0)); }
                });
                check("bag spill: native ProductName dispatch binds", () => Call(sync, "BindBagSpillSources", source, c!));
                check("bag spill: prefab alias resolves before any local spill", () =>
                {
                    Require(prefab.name == "chips" && (Rigidbody)Call(sync, "FindBagSpillTemplate", "potato chips(itemx)") == prefabBody,
                        "Prefab and live item names did not map.");
                });
                RunSavedProductChecks(check, sync, roots);

                var bagFactory = Nested("BagFactory"); Set(bagFactory, "Contents", source);
                var bag = Nested("BagBinding"); Set(bag, "Use", bagUse); Set(bag, "Factory", bagFactory); Set(bag, "Body", bagBody);
                var capture = Nested("PendingSpawn"); var captured = (IList)Get(capture, "Captured");
                var opening = Nested("BagOpening"); Set(opening, "Bag", bag); Set(opening, "Capture", capture);
                Set(sync, "_bagOpening", opening);
                Set(opening, "Entered", true);
                check("bag spill: completed native Garbage state releases an empty opening", () =>
                {
                    consumed.Value = true; source.SendEvent("PROBE_EMPTY");
                    Require(source.ActiveStateName == "Garbage" && (bool)Call(sync, "BagSpillNativeIdle", opening),
                        "Completed terminal contents state kept the opening pending.");
                });
                check("bag spill: unfinished terminal action still blocks publication", () =>
                {
                    var active = State(source, "Garbage").ActiveActions; active.Add(retire);
                    try { Require(!(bool)Call(sync, "BagSpillNativeIdle", opening), "Pending retirement was treated as complete."); }
                    finally { active.Remove(retire); }
                });
                check("bag spill: terminal completion requires the exact consumed bag", () =>
                {
                    consumed.Value = false;
                    Require(!(bool)Call(sync, "BagSpillNativeIdle", opening), "Unconsumed bag was completed.");
                    consumed.Value = true;
                    source.FsmVariables.FindFsmGameObject("CurrentBag").Value = prefab;
                    try { Require(!(bool)Call(sync, "BagSpillNativeIdle", opening), "Another bag's retirement completed this opening."); }
                    finally { source.FsmVariables.FindFsmGameObject("CurrentBag").Value = bagObject; }
                });
                check("bag spill: completed terminal contents allows the next bag", () =>
                    Require((bool)Call(sync, "BagContentsIdle", bag), "Next bag required a nonexistent transition back to Idle."));
                check("bag spill: delayed retirement is rejected", () =>
                {
                    retire.GetType().GetField("delay").SetValue(retire, new FsmFloat(.5f));
                    try { Reject(() => Call(sync, "BagContentsIdle", bag)); }
                    finally { retire.GetType().GetField("delay").SetValue(retire, new FsmFloat(0)); }
                });
                check("bag spill: retirement targeting another object is rejected", () =>
                {
                    var current = retireTarget.gameObject.GameObject;
                    retireTarget.gameObject.GameObject = new FsmGameObject { Value = prefab };
                    try { Reject(() => Call(sync, "BagContentsIdle", bag)); }
                    finally { retireTarget.gameObject.GameObject = current; }
                });
                source.SendEvent("PROBE_IDLE"); consumed.Value = false;
                check("bag spill: exact native output is captured and scanner-locked", () =>
                {
                    source.SendEvent("PROBE_SPILL");
                    var body = output.Value != null ? output.Value.GetComponent<Rigidbody>() : null;
                    Require(body != null && captured.Count == 1 && (Rigidbody)captured[0] == body
                        && ((IDictionary)Get(sync, "_trackedBodies")).Contains(body), "Native output was not captured exactly once.");
                });
                check("bag spill: repeated same-product dispatch keeps distinct outputs", () =>
                {
                    var first = captured.Count > 0 ? captured[0] : null;
                    source.SendEvent("PROBE_SPILL");
                    Require(captured.Count == 2 && !ReferenceEquals(captured[1], first)
                        && (Rigidbody)captured[1] == output.Value.GetComponent<Rigidbody>(), "Repeated output was lost or aliased.");
                });
                check("bag spill: unrelated output cannot enter the active bag manifest", () =>
                {
                    factory.SendEvent("SPAWNITEM");
                    var unrelated = output.Value;
                    try { Require(captured.Count == 2 && !((IDictionary)Get(sync, "_trackedBodies")).Contains(unrelated.GetComponent<Rigidbody>()),
                        "Unrequested nearby output was captured."); }
                    finally { if (unrelated != null) UnityEngine.Object.DestroyImmediate(unrelated); }
                });
                check("bag spill: dispatch outside an opening does not claim outputs", () =>
                {
                    Set(sync, "_bagOpening", null);
                    source.SendEvent("PROBE_SPILL");
                    var outside = output.Value;
                    try { Require(captured.Count == 2 && !((IDictionary)Get(sync, "_trackedBodies")).Contains(outside.GetComponent<Rigidbody>()),
                        "Output from outside the opening was captured."); }
                    finally { if (outside != null) UnityEngine.Object.DestroyImmediate(outside); }
                });
                foreach (Rigidbody body in captured) if (body != null) UnityEngine.Object.DestroyImmediate(body.gameObject);
                SetBagArray(bagObject, "Keys", new ArrayList { "Chips", "Fanbelt" });
                SetBagArray(bagObject, "Values", new ArrayList { 1, 2 });
                check("bag spill: failed part adapter refuses a mixed bag before spawning", () =>
                {
                    var previous = partOutput.Value; Set(replacement, "Failed", true);
                    try
                    {
                        Reject(() => Call(sync, "BeginBagSpill", bag, "Spawn all"));
                        Require(partOutput.Value == previous, "Refused opening created a native part.");
                    }
                    finally { Set(replacement, "Failed", false); }
                });
                capture = Call(sync, "BeginBagSpill", bag, "Spawn all"); captured = (IList)Get(capture, "Captured");
                Set(opening, "Capture", capture); Set(sync, "_bagOpening", opening);
                check("bag spill: mixed groceries and parts count without reserving part bodies", () =>
                {
                    product.Value = "Chips"; source.SendEvent("PROBE_SPILL");
                    product.Value = "Fanbelt"; source.SendEvent("PROBE_SPILL");
                    var grocery = output.Value.GetComponent<Rigidbody>(); var part = partOutput.Value.GetComponent<Rigidbody>();
                    var tracked = (IDictionary)Get(sync, "_trackedBodies");
                    Require(captured.Count == 2 && (Rigidbody)captured[0] == grocery && (Rigidbody)captured[1] == part
                        && tracked.Contains(grocery) && !tracked.Contains(part), "Part identity was reserved by the generic spill path.");
                });
                check("bag spill: repeated parts retain distinct native output bodies", () =>
                {
                    var first = partOutput.Value; partNativeId.Value = "FANBELT02"; source.SendEvent("PROBE_SPILL");
                    var part = partOutput.Value.GetComponent<Rigidbody>();
                    Require(captured.Count == 3 && first != partOutput.Value && (Rigidbody)captured[2] == part
                        && !((IDictionary)Get(sync, "_trackedBodies")).Contains(part), "Repeated parts were aliased or generically reserved.");
                });
                check("bag spill: retired part settles after its native body disappears", () =>
                {
                    var part = (Rigidbody)captured[1]; var owner = part.gameObject;
                    var parts = (IDictionary)Get(sync, "_bagSpillParts");
                    var identity = Get(parts[part], "Id");
                    var lifecycle = Get(sync, "_spawnLifecycle");
                    int hashBefore = part.GetHashCode(); bool containsBefore = parts.Contains(part);
                    Call(lifecycle, "Retire", identity);
                    UnityEngine.Object.DestroyImmediate(part);
                    int hashAfter = part.GetHashCode(); bool containsAfter = parts.Contains(part);
                    bool retired = (bool)Call(lifecycle, "IsRetired", identity);
                    var args = new object?[] { part, replacement, null };
                    bool ready = (bool)Items.GetMethod("TryBagReplacementState", Fields).Invoke(sync, args);
                    var retiredCapture = Nested("PendingSpawn"); ((IList)Get(retiredCapture, "Captured")).Add(part);
                    Set(retiredCapture, "StableSince", Time.unscaledTime - 1f);
                    var retiredOpening = Nested("BagOpening"); Set(retiredOpening, "Capture", retiredCapture);
                    bool bagReady = (bool)Call(sync, "BagSpillReady", retiredOpening);
                    bool factoryMapped = ((IDictionary)Get(sync, "_bagSpillBodies")).Contains(part);
                    Require(part == null && ready && args[2] == null && bagReady && factoryMapped,
                        "Retired native part kept the bag waiting for a removed body."
                        + " hash=" + hashBefore + "/" + hashAfter + " contains=" + containsBefore + "/" + containsAfter
                        + " retired=" + retired + " destroyed=" + (part == null) + " ready=" + ready + " nullState=" + (args[2] == null)
                        + " bagReady=" + bagReady + " factoryMapped=" + factoryMapped);
                    UnityEngine.Object.DestroyImmediate(owner);
                });
                check("bag spill: failed part publication clears the pending bag without deleting the host output", () =>
                {
                    var part = (Rigidbody)captured[2]; var failedCapture = Nested("PendingSpawn");
                    ((IList)Get(failedCapture, "Captured")).Add(part);
                    var failedOpening = Nested("BagOpening"); Set(failedOpening, "Capture", failedCapture); Set(failedOpening, "Applied", true);
                    Set(replacement, "Failed", true);
                    try
                    {
                        Call(sync, "PublishBagSpill", null!, failedOpening);
                        Require(((IList)Get(failedCapture, "Captured")).Count == 0 && !(bool)Get(failedOpening, "Applied")
                            && part != null && part.gameObject.activeInHierarchy, "Failed part publication remained pending or destroyed the host part.");
                    }
                    finally { Set(replacement, "Failed", false); }
                });
                foreach (Rigidbody body in captured) if (body != null) UnityEngine.Object.DestroyImmediate(body.gameObject);
                check("bag spill: cleanup restores only native actions", () =>
                {
                    Call(sync, "ClearBagSpillCapture");
                    Require(State(source, "Dispatch").Actions.Length == 1 && State(factory, "Create product").Actions.Length == 2
                        && State(partFactory, "Create product").Actions.Length == 2,
                        "Capture hooks survived cleanup.");
                });
                CheckDrinkInitialization(check, roots);
            }
            finally
            {
                Call(sync, "ClearBagSpillCapture");
                for (int i = roots.Count - 1; i >= 0; i--) if (roots[i] != null) UnityEngine.Object.DestroyImmediate(roots[i]);
            }
        }

        private static void CheckDrinkInitialization(Action<string, Action> check, List<GameObject> roots)
        {
            var root = Root(roots, "probe milk");
            var other = Root(roots, "probe unrelated drink");
            var fsm = MakeFsm(root, "Use", "Check drink", "State 1", "State 2", "State 5", "State 6", "Destroy");
            var owner = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner };
            SetActions(fsm, "State 2", Native("Wait", "time", new FsmFloat(.2f), "finishEvent", FsmEvent.Finished));
            State(fsm, "State 2").Transitions = new[] { Transition("FINISHED", "State 1") };
            var removal = Native("DestroyComponent", "gameObject", owner, "component", new FsmString { Value = "Rigidbody" });
            SetActions(fsm, "State 5", removal);
            SetActions(fsm, "State 6", Native("DestroySelf"));
            var config = Activator.CreateInstance(Core.GetType("WinterMP.Core.Catalog.ConsumableConfig", true), true);
            var states = new List<string>();
            check("bag spill: milk startup delay is not a drink consumption hook", () =>
            {
                Call(config, "CollectDespawnStates", fsm, states);
                Require(!states.Contains("State 2") && states.Contains("State 5") && states.Contains("State 6")
                    && states.Contains("Destroy"), "Startup was treated as empty, or real removal lost its hook.");
            });
            check("bag spill: another object's removal does not retire this drink", () =>
            {
                owner.OwnerOption = OwnerDefaultOption.SpecifyGameObject; owner.GameObject = new FsmGameObject { Value = other };
                states.Clear(); Call(config, "CollectDespawnStates", fsm, states);
                Require(!states.Contains("State 5"), "Unrelated object's removal retired this drink.");
            });
            check("bag spill: disabled removal does not retire this drink", () =>
            {
                owner.OwnerOption = OwnerDefaultOption.UseOwner; removal.Enabled = false;
                states.Clear(); Call(config, "CollectDespawnStates", fsm, states);
                Require(!states.Contains("State 5"), "Disabled removal retired this drink.");
            });
        }

        private static object BagPartRule(string fsm)
        {
            var data = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("ReplacementParts",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
            foreach (var rule in (IEnumerable)Get(data, "Factories"))
                if ((string)Get(rule, "Fsm") == fsm && (bool)Get(rule, "BagOutput")) return rule;
            throw new InvalidOperationException("Native bag part rule missing.");
        }

        private static void SetBagArray(GameObject bag, string reference, ArrayList values)
        {
            Type? type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((type = assembly.GetType("PlayMakerArrayListProxy")) != null) break;
            var proxy = bag.AddComponent(type ?? throw new InvalidOperationException("Native array proxy missing."));
            type.GetField("referenceName").SetValue(proxy, reference);
            type.GetField("_arrayList").SetValue(proxy, values);
        }

        private static GameObject Root(List<GameObject> roots, string name)
        {
            var result = new GameObject(name); result.SetActive(false); roots.Add(result); return result;
        }
        private static PlayMakerFSM MakeFsm(GameObject owner, string name, params string[] names)
        {
            var fsm = owner.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", Fields).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = name;
            var states = new List<FsmState>();
            foreach (string state in names) states.Add(new FsmState(fsm.Fsm) { Name = state, Actions = new FsmStateAction[0] });
            fsm.Fsm.States = states.ToArray(); fsm.Fsm.StartState = names[0]; return fsm;
        }
        private static FsmState State(PlayMakerFSM fsm, string name)
        {
            foreach (var state in fsm.Fsm.States) if (state.Name == name) return state;
            throw new InvalidOperationException("Fixture state missing.");
        }
        private static void SetActions(PlayMakerFSM fsm, string name, params FsmStateAction[] actions)
        {
            var state = State(fsm, name); state.Actions = actions;
            foreach (var action in actions) action.Init(state);
        }
        private static FsmStateAction Native(string name, params object[] fields)
        {
            Type? type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((type = assembly.GetType("HutongGames.PlayMaker.Actions." + name)) != null) break;
            var action = (FsmStateAction)Activator.CreateInstance(type ?? throw new InvalidOperationException("Native action missing: " + name));
            action.Reset(); action.Enabled = true;
            for (int i = 0; i < fields.Length; i += 2) type.GetField((string)fields[i]).SetValue(action, fields[i + 1]);
            return action;
        }
        private static FsmTransition Transition(string evt, string state) => new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent(evt), ToState = state };
        private static object Nested(string name) => Activator.CreateInstance(Items.GetNestedType(name, BindingFlags.NonPublic), true);
        private static object Get(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
        private static void Set(object value, string name, object? field) => value.GetType().GetField(name, Fields).SetValue(value, field);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Fields).Invoke(value, args);
        private static void Reject(Action action)
        {
            try { action(); }
            catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
            throw new InvalidOperationException("Changed native dispatch was accepted.");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
