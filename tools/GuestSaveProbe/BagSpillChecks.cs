using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static class BagSpillChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);

        internal static void Run(Action<string, Action> check)
        {
            var roots = new List<GameObject>();
            var sync = Activator.CreateInstance(Items, Fields, null, new object?[] { null }, null);
            var c = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("ShoppingBags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            Require(c != null, "Shopping bag catalog missing.");
            var spawner = Root(roots, "Spawner");
            var products = Root(roots, "CreateItems"); products.transform.SetParent(spawner.transform, false);
            var sourceObject = Root(roots, "BagContentsStore"); sourceObject.transform.SetParent(spawner.transform, false);
            var bagObject = Root(roots, "probe bag");
            var prefab = Root(roots, "chips"); var prefabBody = prefab.AddComponent<Rigidbody>(); prefabBody.useGravity = false;
            var use = MakeFsm(prefab, "Use", "Idle", "Identity");
            var bagUse = MakeFsm(bagObject, "Use", "Idle");
            var factory = MakeFsm(products, "Chips", "Idle", "Create product");
            var source = MakeFsm(sourceObject, "Logic", "Idle", "Dispatch");
            var output = new FsmGameObject { Name = "New", UseVariable = true };
            factory.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "Prefab", UseVariable = true, Value = prefab },
                output, new FsmGameObject { Name = "ShoppingBagSpawn", UseVariable = true, Value = bagObject } };
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
                var target = new FsmEventTarget { target = FsmEventTarget.EventTarget.GameObjectFSM,
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = products } },
                    fsmName = product, sendToChildren = new FsmBool(false) };
                var send = Native("SendEventByName", "eventTarget", target, "sendEvent", new FsmString { Value = "SPAWNITEM" },
                    "delay", new FsmFloat(0), "everyFrame", false);
                SetActions(source, "Dispatch", send);
                State(source, "Dispatch").Transitions = new[] { Transition("FINISHED", "Idle") };
                source.Fsm.GlobalTransitions = new[] { Transition("PROBE_SPILL", "Dispatch") };
                foreach (var fsm in new[] { use, bagUse, factory, source })
                { fsm.enabled = true; if (!fsm.Fsm.Started) fsm.Fsm.Start(); }

                var bound = Nested("BagSpillFactory"); Set(bound, "Fsm", factory); Set(bound, "Path", "Spawner/CreateItems");
                check("bag spill: native product binding accepts direct CreateObject output", () => Call(sync, "BindBagSpillFactory", bound, c!));
                var factories = (IDictionary)Get(sync, "_bagSpillFactories"); factories.Add(factory, bound);
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

                var bagFactory = Nested("BagFactory"); Set(bagFactory, "Contents", source);
                var bag = Nested("BagBinding"); Set(bag, "Use", bagUse); Set(bag, "Factory", bagFactory);
                var capture = Nested("PendingSpawn"); var captured = (IList)Get(capture, "Captured");
                var opening = Nested("BagOpening"); Set(opening, "Bag", bag); Set(opening, "Capture", capture);
                Set(sync, "_bagOpening", opening);
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
                check("bag spill: cleanup restores only native actions", () =>
                {
                    Call(sync, "ClearBagSpillCapture");
                    Require(State(source, "Dispatch").Actions.Length == 1 && State(factory, "Create product").Actions.Length == 2,
                        "Capture hooks survived cleanup.");
                });
            }
            finally
            {
                Call(sync, "ClearBagSpillCapture");
                for (int i = roots.Count - 1; i >= 0; i--) if (roots[i] != null) UnityEngine.Object.DestroyImmediate(roots[i]);
            }
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
