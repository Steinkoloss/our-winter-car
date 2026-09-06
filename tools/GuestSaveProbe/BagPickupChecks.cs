using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static class BagPickupChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);

        internal static void Run(Action<string, Action> check)
        {
            WinterMP.Core.Catalog.SyncCatalog.EnsureLoaded();
            var session = SessionManager.Instance!;
            var world = WorldSyncManager.Instance ?? throw new InvalidOperationException("World coordinator missing.");
            var playerProperty = typeof(WorldSyncManager).GetProperty("LocalPlayer", Fields);
            var previousPlayer = playerProperty.GetValue(world, null);
            SessionState previousState = session.State; bool previousHost = session.IsHost; byte previousId = session.LocalPlayerId;
            var player = new GameObject("PLAYER"); player.SetActive(false);
            var hand = ChildPath(player, "Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand");
            var pivot = new GameObject("ItemPivot"); pivot.transform.SetParent(hand.transform.parent, false);
            var handBody = hand.AddComponent<Rigidbody>(); handBody.isKinematic = true;
            var joint = hand.AddComponent<FixedJoint>();
            var bagObject = new GameObject("probe bag"); bagObject.SetActive(false);
            var bagBody = bagObject.AddComponent<Rigidbody>(); bagBody.useGravity = false;
            var unbound = new GameObject("probe unbound bag"); unbound.SetActive(false);
            var unboundBody = unbound.AddComponent<Rigidbody>(); unboundBody.useGravity = false;
            var unrelated = new GameObject("probe unrelated item"); unrelated.SetActive(false);
            unrelated.AddComponent<Rigidbody>().useGravity = false;
            var bagUse = BagUse(bagObject); var otherUse = BagUse(unbound);
            var pickup = MakeFsm(hand, "PickUp", "Look for object", "Set pivot 2", "Item picked", "Drop part", "Wait");
            var picked = Variable("PickedObject"); var itemPivot = Variable("ItemPivot", pivot);
            var empty = new FsmBool { Name = "HandEmpty", UseVariable = true, Value = true };
            var nativeJoint = new FsmObject { Name = "Joint", UseVariable = true, Value = joint };
            pickup.FsmVariables.GameObjectVariables = new[] { picked, itemPivot };
            pickup.FsmVariables.BoolVariables = new[] { empty };
            pickup.FsmVariables.ObjectVariables = new[] { nativeJoint };
            var bridgeType = Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true);
            var bridge = Activator.CreateInstance(bridgeType, new object[] { world, new Dictionary<PlayMakerFSM, bool>() });
            var sync = Activator.CreateInstance(Items, new[] { bridge });
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true)
                .GetProperty("ShoppingBags", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null, null);
            Require(catalog != null, "Shopping bag catalog missing.");
            try
            {
                playerProperty.SetValue(world, player.transform, null);
                player.SetActive(true); bagObject.SetActive(true); unbound.SetActive(true); unrelated.SetActive(true);
                var position = new FsmVector3 { Name = "ObjPos", UseVariable = true };
                SetActions(pickup, "Set pivot 2",
                    Native("SetBoolValue", "boolVariable", empty, "boolValue", new FsmBool(false)),
                    Native("GetPosition", "gameObject", Target(picked), "vector", position),
                    Native("SetPosition", "gameObject", Owner(), "vector", position),
                    Native("SetPosition", "gameObject", Target(itemPivot), "vector", position),
                    Native("SetLayer", "gameObject", Target(picked), "layer", 16),
                    Native("SetParent", "gameObject", Target(picked), "parent", itemPivot,
                        "resetLocalPosition", new FsmBool(false), "resetLocalRotation", new FsmBool(false)),
                    Native("SetJointConnectedBody", "joint", Owner(), "rigidBody", picked));
                SetActions(pickup, "Drop part",
                    Native("SetIsKinematic", "gameObject", Target(new FsmGameObject()), "isKinematic", new FsmBool(false)),
                    Native("SetIsKinematic", "gameObject", Target(picked), "isKinematic", new FsmBool(false)),
                    Native("SetParent", "gameObject", Target(picked), "parent", Variable(string.Empty),
                        "resetLocalPosition", new FsmBool(false), "resetLocalRotation", new FsmBool(false)),
                    Native("SetLayer", "gameObject", Target(picked), "layer", 19),
                    Native("SetJointConnectedBody", "joint", Owner(), "rigidBody", Variable(string.Empty)),
                    Native("SetVelocity", "gameObject", Target(picked), "vector", new FsmVector3(Vector3.zero)));
                SetActions(pickup, "Wait", Native("SetGameObject", "variable", picked, "gameObject", Variable(string.Empty)));
                SetActions(pickup, "Look for object", Native("SetBoolValue", "boolVariable", empty, "boolValue", new FsmBool(true)));
                State(pickup, "Set pivot 2").Transitions = new[] { Transition("FINISHED", "Item picked") };
                State(pickup, "Drop part").Transitions = new[] { Transition("FINISHED", "Wait") };
                State(pickup, "Wait").Transitions = new[] { Transition("FINISHED", "Look for object") };
                pickup.Fsm.GlobalTransitions = new[] { Transition("PROBE_PICK", "Set pivot 2"), Transition("DROP_PART", "Drop part") };
                foreach (var fsm in new[] { bagUse, otherUse, pickup }) { fsm.enabled = true; if (!fsm.Fsm.Started) fsm.Fsm.Start(); }
                var originals = State(pickup, "Set pivot 2").Actions;
                Action<GameObject> grab = target => { picked.Value = target; pickup.SendEvent("PROBE_PICK"); };
                Action released = () => Require(joint.connectedBody == null && bagObject.transform.parent == null
                    && picked.Value == null && pickup.ActiveStateName == "Look for object", "Bag or native hand still attached after release.");
                check("bag pickup: native validated actions attach and release a bag", () =>
                {
                    Items.GetMethod("ValidateBagPickup", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { pickup, catalog });
                    grab(bagObject);
                    Require(pickup.Fsm.Started && pickup.ActiveStateName == "Item picked" && bagObject.transform.parent == pivot.transform
                        && joint.connectedBody == bagBody && !empty.Value, "Native pickup baseline did not attach parent and joint.");
                    pickup.SendEvent("DROP_PART"); released();
                });

                var factory = Nested("BagFactory");
                var binding = Nested("BagBinding"); Set(binding, "Id", 42u); Set(binding, "Body", bagBody);
                Set(binding, "Use", bagUse); Set(binding, "Factory", factory);
                ((IDictionary)Get(sync, "_bags")).Add(42u, binding);
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                Set(item, "Id", 42u); Set(item, "Body", bagBody); Set(item, "RemoteOwner", (byte)0);
                ((IDictionary)Get(sync, "_items")).Add(42u, item);
                SetSession(session, SessionState.Connected, false, 1);
                Call(sync, "EnsureBagPickup");
                Require(Get(sync, "_bagPickupGate") != null, "Production pickup guard did not bind.");

                check("bag pickup: remote ownership blocks native parent and joint", () =>
                {
                    grab(bagObject); released();
                    Require(bagObject.layer == 19, "Denied pickup changed the native item layer.");
                });
                check("bag pickup: unbound and retired bags cannot be picked up", () =>
                {
                    grab(unbound);
                    Require(unbound.transform.parent == null && joint.connectedBody == null && picked.Value == null,
                        "Unbound bag entered native pickup.");
                    Set(item, "RemoteOwner", (byte)255);
                    var lifecycle = Get(sync, "_spawnLifecycle"); lifecycle.GetType().GetMethod("Retire").Invoke(lifecycle, new object[] { 42u });
                    grab(bagObject); released(); lifecycle.GetType().GetMethod("Clear").Invoke(lifecycle, null);
                });
                check("bag pickup: winning remote claim releases the exact locally held bag", () =>
                {
                    grab(bagObject);
                    Require(joint.connectedBody == bagBody && bagObject.transform.parent == pivot.transform,
                        "Allowed pickup did not restore native actions after denial.");
                    Set(item, "RemoteOwner", (byte)0); Call(sync, "ReleaseHeldBag", bagBody); released();
                });
                check("bag pickup: releasing a bag preserves an unrelated held item", () =>
                {
                    grab(unrelated); var held = joint.connectedBody;
                    Require(held == unrelated.GetComponent<Rigidbody>() && unrelated.transform.parent == pivot.transform,
                        "Unrelated native pickup baseline failed.");
                    Call(sync, "ReleaseHeldBag", bagBody);
                    Require(joint.connectedBody == held && picked.Value == unrelated && pickup.ActiveStateName == "Item picked"
                        && unrelated.transform.parent == pivot.transform, "Bag release disturbed the unrelated pickup.");
                    pickup.SendEvent("DROP_PART");
                });
                check("bag pickup: cleanup restores the original native actions", () =>
                {
                    Call(sync, "ClearBagPickup");
                    var restored = State(pickup, "Set pivot 2").Actions;
                    Require(restored.Length == originals.Length, "Pickup hook survived cleanup.");
                    for (int i = 0; i < originals.Length; i++) Require(ReferenceEquals(originals[i], restored[i]) && restored[i].Enabled,
                        "Pickup native action or enabled state changed on cleanup.");
                    grab(bagObject); Require(joint.connectedBody == bagBody, "Restored native pickup no longer works.");
                    pickup.SendEvent("DROP_PART"); released();
                });
            }
            finally
            {
                Call(sync, "ClearBagPickup");
                SetSession(session, previousState, previousHost, previousId); playerProperty.SetValue(world, previousPlayer, null);
                joint.connectedBody = null;
                foreach (var root in new[] { bagObject, unbound, unrelated, player }) if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject ChildPath(GameObject root, string path)
        {
            foreach (string name in path.Split('/')) { var child = new GameObject(name); child.transform.SetParent(root.transform, false); root = child; }
            return root;
        }
        private static PlayMakerFSM BagUse(GameObject owner)
        {
            var use = MakeFsm(owner, "Use", "Wait player", "Spawn one", "Spawn all");
            use.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "probe1" } };
            use.FsmVariables.GameObjectVariables = new[] { Variable("ProductSpawner") }; return use;
        }
        private static PlayMakerFSM MakeFsm(GameObject owner, string name, params string[] names)
        {
            var fsm = owner.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", Fields).SetValue(fsm, new Fsm()); fsm.Fsm.Name = name;
            var states = new List<FsmState>();
            foreach (string state in names) states.Add(new FsmState(fsm.Fsm) { Name = state, Actions = new FsmStateAction[0] });
            fsm.Fsm.States = states.ToArray(); fsm.Fsm.StartState = names[0]; return fsm;
        }
        private static FsmState State(PlayMakerFSM fsm, string name)
        {
            foreach (var state in fsm.Fsm.States) if (state.Name == name) return state;
            throw new InvalidOperationException("Fixture state missing: " + name);
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
            // Serialized actions hydrate unused x/y/z outputs as explicit None
            // variables. Reset() leaves them null, which native OnEnter cannot read.
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!typeof(NamedVariable).IsAssignableFrom(field.FieldType) || field.GetValue(action) != null) continue;
                var none = (NamedVariable)Activator.CreateInstance(field.FieldType);
                none.UseVariable = true; none.Name = string.Empty; field.SetValue(action, none);
            }
            for (int i = 0; i < fields.Length; i += 2) type.GetField((string)fields[i]).SetValue(action, fields[i + 1]);
            return action;
        }
        private static FsmGameObject Variable(string name, GameObject? value = null) => new FsmGameObject { Name = name, UseVariable = true, Value = value };
        private static FsmOwnerDefault Target(FsmGameObject value) => new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = value };
        private static FsmOwnerDefault Owner() => new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner };
        private static FsmTransition Transition(string evt, string state) => new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent(evt), ToState = state };
        private static object Nested(string name) => Activator.CreateInstance(Items.GetNestedType(name, BindingFlags.NonPublic), true);
        private static object Get(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
        private static void Set(object value, string name, object? field) => value.GetType().GetField(name, Fields).SetValue(value, field);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Fields).Invoke(value, args);
        private static void SetSession(SessionManager session, SessionState state, bool host, byte id)
        {
            typeof(SessionManager).GetProperty("State").GetSetMethod(true).Invoke(session, new object[] { state });
            typeof(SessionManager).GetProperty("IsHost").GetSetMethod(true).Invoke(session, new object[] { host });
            typeof(SessionManager).GetProperty("LocalPlayerId").GetSetMethod(true).Invoke(session, new object[] { id });
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
