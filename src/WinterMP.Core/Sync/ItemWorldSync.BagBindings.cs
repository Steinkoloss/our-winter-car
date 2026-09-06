using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class BagFactory
        {
            public uint Id;
            public PlayMakerFSM Fsm = null!, TemplateUse = null!, Contents = null!;
            public GameObject Prefab = null!;
            public string Prefix = string.Empty;
            public ShoppingBagFactoryData Rule = null!;
            public bool Failed;
            public FsmState? CreateState, LoadState;
            public FsmStateAction? CreateHook, LoadHook;
        }

        private sealed class BagBinding
        {
            public uint Id;
            public Rigidbody Body = null!;
            public PlayMakerFSM Use = null!;
            public BagFactory Factory = null!;
            public string NativeId = string.Empty;
            public bool Replica;
            public ushort Remaining;
            public float Condition;
            public readonly BagPublication Publication = new BagPublication();
        }

        private sealed class BagOutput
        {
            public BagFactory Factory = null!;
            public string NativeId = string.Empty;
            public float Deadline;
        }

        private readonly Dictionary<uint, BagFactory> _bagFactories = new Dictionary<uint, BagFactory>();
        private readonly Dictionary<Rigidbody, BagOutput> _bagOutputs = new Dictionary<Rigidbody, BagOutput>();
        private readonly HashSet<Rigidbody> _unreadyBags = new HashSet<Rigidbody>();
        private readonly Dictionary<PlayMakerFSM, GuestPartIsolation> _hiddenBags = new Dictionary<PlayMakerFSM, GuestPartIsolation>();
        private readonly HashSet<int> _bagBindingWarnings = new HashSet<int>();
        private GameObject? _bagStorage;

        internal void RefreshBagFactories()
        {
            var c = SyncCatalog.ShoppingBags;
            if (c == null || _bagFactories.Count == c.Factories.Count) return;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != c["factoryFsm"]) continue;
                string path = ScenePath.Of(fsm.transform);
                foreach (var rule in c.Factories)
                {
                    if (path != rule.Path) continue;
                    uint id = FactoryItemIdentity.FactoryId(path, c["factoryFsm"]);
                    if (_bagFactories.ContainsKey(id)) break;
                    var factory = new BagFactory { Id = id, Fsm = fsm, Rule = rule, Prefix = rule.Prefix };
                    _bagFactories.Add(id, factory);
                    try
                    {
                        var prefab = fsm.FsmVariables.FindFsmGameObject(c["prefabVariable"])?.Value;
                        if (prefab == null || prefab.name != rule.Prefix
                            || fsm.FsmVariables.FindFsmString(c["prefixVariable"]) == null
                            || fsm.FsmVariables.FindFsmString(c["idVariable"]) == null
                            || fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]) == null)
                            throw new InvalidOperationException("Bag factory identity references changed.");
                        factory.Prefab = prefab;
                        var create = PackageStateActions(fsm, c["createState"], "IntAdd", "CreateObject", "SetParent",
                            "SetFsmGameObject", "SetFsmGameObject", "ConvertIntToString", "BuildString", "SetName");
                        var load = PackageStateActions(fsm, c["loadCreateState"], "CreateObject", "SetFsmGameObject", "SetName");
                        var contents = ValidateBagFactoryOutput(factory, create.Actions[1], create.Actions[4], create.Actions[7], c);
                        if (ValidateBagFactoryOutput(factory, load.Actions[0], load.Actions[1], load.Actions[2], c) != contents)
                            throw new InvalidOperationException("Saved and purchased bags use different contents factories.");
                        PlayMakerFSM? logic = null;
                        foreach (var candidate in contents.GetComponents<PlayMakerFSM>())
                            if (candidate.FsmName == c["contentsFsm"])
                            {
                                if (logic != null) throw new InvalidOperationException("Ambiguous bag contents FSM.");
                                logic = candidate;
                            }
                        if (logic == null || !logic.Fsm.Initialized
                            || logic.FsmVariables.FindFsmGameObject(c["currentBagVariable"]) == null)
                            throw new InvalidOperationException("Bag contents FSM is unavailable.");
                        factory.Contents = logic;
                        PackageStateActions(logic, c["contentsIdleState"]);
                        PackageStateActions(logic, c["contentsStartState"]);
                        bool one = false, all = false;
                        foreach (var transition in logic.Fsm.GlobalTransitions)
                        {
                            if (transition.EventName == c["oneEvent"] && FsmHook.FindState(logic, transition.ToState) != null) one = true;
                            if (transition.EventName == c["allEvent"] && FsmHook.FindState(logic, transition.ToState) != null) all = true;
                        }
                        if (!one || !all) throw new InvalidOperationException("Bag contents commands changed.");
                        factory.TemplateUse = ValidateBagTemplate(factory, c);
                        PackageStateActions(fsm, c["factoryIdleState"]);
                        if (create.Transitions.Length != 1 || create.Transitions[0].EventName != "FINISHED"
                            || create.Transitions[0].ToState != c["factoryIdleState"])
                            throw new InvalidOperationException("Bag creation completion changed.");
                        factory.CreateState = create; factory.LoadState = load;
                        factory.CreateHook = new FsmHookAction(() => CaptureBagOutput(factory));
                        factory.LoadHook = new FsmHookAction(() => CaptureBagOutput(factory));
                        AppendBagHook(create, factory.CreateHook); AppendBagHook(load, factory.LoadHook);
                    }
                    catch (Exception e) { FailBagFactory(factory, e.Message); }
                    break;
                }
            }
        }

        private static GameObject ValidateBagFactoryOutput(BagFactory factory, FsmStateAction create,
            FsmStateAction setSpawner, FsmStateAction setName, ShoppingBagsData c)
        {
            var target = PackageField<FsmOwnerDefault>(setSpawner, "gameObject");
            var named = PackageField<FsmOwnerDefault>(setName, "gameObject");
            var contents = PackageField<FsmGameObject>(setSpawner, "setValue")?.Value;
            if (PackageField<FsmGameObject>(create, "gameObject")?.Name != c["prefabVariable"]
                || PackageField<FsmGameObject>(create, "storeObject")?.Name != c["outputVariable"]
                || target == null || target.OwnerOption != OwnerDefaultOption.SpecifyGameObject
                || target.GameObject.Name != c["outputVariable"] || named == null
                || named.OwnerOption != OwnerDefaultOption.SpecifyGameObject || named.GameObject.Name != c["outputVariable"]
                || PackageField<FsmString>(setName, "name")?.Name != c["idVariable"]
                || PackageField<FsmString>(setSpawner, "fsmName")?.Value != c["itemFsm"]
                || PackageField<FsmString>(setSpawner, "variableName")?.Value != c["contentsVariable"]
                || contents == null || ScenePath.Of(contents.transform) != factory.Rule.ContentsPath)
                throw new InvalidOperationException("Bag output or contents binding changed.");
            return contents;
        }

        private static PlayMakerFSM ValidateBagTemplate(BagFactory factory, ShoppingBagsData c)
        {
            var fsms = factory.Prefab.GetComponents<PlayMakerFSM>();
            if (factory.Prefab.GetComponent<Rigidbody>() == null || fsms.Length != 1 || fsms[0].FsmName != c["itemFsm"])
                throw new InvalidOperationException("Bag prefab components changed.");
            var use = fsms[0];
            if (!use.Fsm.Initialized) use.Fsm.Init(use);
            if (use.Fsm.StartState != c["itemInitState"]
                || use.FsmVariables.FindFsmString(c["itemIdVariable"]) == null
                || use.FsmVariables.FindFsmGameObject(c["contentsVariable"]) == null
                || use.FsmVariables.FindFsmGameObject(c["ownerVariable"]) == null
                || use.FsmVariables.FindFsmFloat(c["conditionVariable"]) == null
                || use.FsmVariables.FindFsmBool(c["consumedVariable"]) == null)
                throw new InvalidOperationException("Bag initialization variables changed.");
            var identity = PackageStateActions(use, c["itemIdentityState"], "GetOwner", "GetName", "SetName",
                "SetIsKinematic", "BuildString", "BuildString", "BuildString", "Exists");
            if (PackageField<FsmGameObject>(identity.Actions[0], "storeGameObject")?.Name != c["ownerVariable"]
                || PackageField<FsmGameObject>(identity.Actions[1], "gameObject")?.Name != c["ownerVariable"]
                || PackageField<FsmString>(identity.Actions[1], "storeName")?.Name != c["itemIdVariable"]
                || PackageField<FsmString>(identity.Actions[2], "name")?.Value != c["itemName"])
                throw new InvalidOperationException("Bag native ID no longer survives its display-name change.");
            var confirm = PackageStateActions(use, c["confirmState"], "FloatClamp", "SetFsmGameObject", "SetBoolValue",
                "SetStringValue", "GetButtonUp", "MousePickEvent", "Wait");
            var owner = PackageField<FsmOwnerDefault>(confirm.Actions[1], "gameObject");
            if (owner == null || owner.OwnerOption != OwnerDefaultOption.SpecifyGameObject
                || owner.GameObject.Name != c["contentsVariable"]
                || PackageField<FsmString>(confirm.Actions[1], "fsmName")?.Value != c["contentsFsm"]
                || PackageField<FsmString>(confirm.Actions[1], "variableName")?.Value != c["currentBagVariable"]
                || PackageField<FsmGameObject>(confirm.Actions[1], "setValue")?.Name != c["ownerVariable"])
                throw new InvalidOperationException("Bag confirmation no longer selects its own contents.");
            var one = PackageStateActions(use, c["oneState"], "SendEventByName", "SetBoolValue", "SetStringValue", "Wait");
            var all = PackageStateActions(use, c["allState"], "SendEventByName", "SetBoolValue", "SetStringValue");
            ValidateBagSpawnEvent(one.Actions[0], c["oneEvent"], c);
            ValidateBagSpawnEvent(all.Actions[0], c["allEvent"], c);
            var garbage = PackageStateActions(use, c["garbageState"], "SetBoolValue", "SetParent", "DestroyObject",
                "DestroyObject", "DestroyComponent", "DestroyComponent", "DestroyComponent", "SetPosition");
            if (PackageField<FsmBool>(garbage.Actions[0], "boolVariable")?.Name != c["consumedVariable"]
                || PackageField<FsmBool>(garbage.Actions[0], "boolValue")?.Value != true)
                throw new InvalidOperationException("Bag garbage no longer marks the saved bag consumed.");
            var save = PackageStateActions(use, c["saveState"], "BoolTest", "ArrayListEasySave", "ArrayListEasySave", "SaveFloat", "SaveTransform");
            var delete = PackageStateActions(use, c["deleteState"], "Delete");
            if (PackageField<FsmBool>(save.Actions[0], "boolVariable")?.Name != c["consumedVariable"]
                || PackageField<FsmEvent>(save.Actions[0], "isTrue")?.Name != c["consumedEvent"]
                || PackageField<FsmString>(delete.Actions[0], "uniqueTag")?.Name != c["itemIdVariable"])
                throw new InvalidOperationException("Bag save cleanup no longer deletes its consumed native ID.");
            bool deletes = false, saves = false, consumes = false;
            foreach (var transition in save.Transitions)
                if (transition.EventName == c["consumedEvent"] && transition.ToState == c["deleteState"]) deletes = true;
            foreach (var transition in use.Fsm.GlobalTransitions)
            {
                if (transition.EventName == c["saveEvent"] && transition.ToState == c["saveState"]) saves = true;
                if (transition.EventName == c["garbageEvent"] && transition.ToState == c["garbageState"]) consumes = true;
            }
            if (!deletes || !saves || !consumes) throw new InvalidOperationException("Bag save or garbage transitions changed.");
            if (FsmHook.FindState(use, c["itemIdleState"]) == null || FsmHook.FindState(use, c["itemLoadState"]) == null
                || BagArrayProxy(use.gameObject, c["keysReference"]) == null || BagArrayProxy(use.gameObject, c["valuesReference"]) == null)
                throw new InvalidOperationException("Bag inventory or idle state changed.");
            return use;
        }

        private static void ValidateBagSpawnEvent(FsmStateAction action, string name, ShoppingBagsData c)
        {
            var target = PackageField<FsmEventTarget>(action, "eventTarget");
            if (target == null || target.target != FsmEventTarget.EventTarget.GameObjectFSM
                || target.gameObject.OwnerOption != OwnerDefaultOption.SpecifyGameObject
                || target.gameObject.GameObject.Name != c["contentsVariable"] || target.fsmName.Value != c["contentsFsm"]
                || PackageField<FsmString>(action, "sendEvent")?.Value != name
                || PackageField<FsmFloat>(action, "delay")?.Value != 0f
                || action.GetType().GetField("everyFrame")?.GetValue(action) is not bool everyFrame || everyFrame)
                throw new InvalidOperationException("Bag opening no longer sends one immediate contents command.");
        }

        private static void AppendBagHook(FsmState state, FsmStateAction hook)
        {
            var actions = new FsmStateAction[state.Actions.Length + 1];
            Array.Copy(state.Actions, actions, state.Actions.Length); actions[actions.Length - 1] = hook;
            state.Actions = actions;
        }

        private void CaptureBagOutput(BagFactory factory)
        {
            if (factory.Failed) return;
            try
            {
                var session = SessionManager.Instance; var c = SyncCatalog.ShoppingBags;
                if (session == null || c == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected)) return;
                var obj = factory.Fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]).Value;
                string id = factory.Fsm.FsmVariables.FindFsmString(c["idVariable"]).Value;
                var body = obj != null ? obj.GetComponent<Rigidbody>() : null;
                if (body == null || !FactoryItemIdentity.IsNativeId(id, factory.Prefix))
                    throw new InvalidOperationException("Bag factory produced an invalid persistent ID.");
                _bagOutputs[body] = new BagOutput { Factory = factory, NativeId = id, Deadline = Time.unscaledTime + 10f };
                var use = FindBagUse(body);
                if (use != null && use.Fsm.Initialized) GuardUnboundBag(use);
            }
            catch (Exception e) { FailBagFactory(factory, e.Message); }
        }

        private static PlayMakerFSM? FindBagUse(Rigidbody body)
        {
            if (body == null) return null;
            foreach (var use in body.GetComponents<PlayMakerFSM>())
                if (IsBagUse(use)) return use;
            return null;
        }

        internal static bool IsBagUse(PlayMakerFSM use)
        {
            var c = SyncCatalog.ShoppingBags;
            return use != null && use.FsmName == (c?["itemFsm"] ?? "Use")
                && use.FsmVariables.FindFsmString(c?["itemIdVariable"] ?? "ID") != null
                && use.FsmVariables.FindFsmGameObject(c?["contentsVariable"] ?? "ProductSpawner") != null
                && FsmHook.FindState(use, c?["oneState"] ?? "Spawn one") != null
                && FsmHook.FindState(use, c?["allState"] ?? "Spawn all") != null;
        }

        private bool TryScanBag(Rigidbody body)
        {
            var use = FindBagUse(body);
            if (use == null) return false;
            foreach (var binding in _bags.Values)
                if (binding.Body == body) return true;
            if (_hiddenBags.ContainsKey(use)) return true;
            var c = SyncCatalog.ShoppingBags;
            if (c == null) return true;
            if (use.Fsm.Initialized) GuardUnboundBag(use);
            if (!use.Fsm.Initialized || !use.Fsm.Started || !use.enabled
                || Array.IndexOf(c.ReadyStates, use.ActiveStateName) < 0)
            { _unreadyBags.Add(body); return true; }
            _unreadyBags.Remove(body);
            try
            {
                var session = SessionManager.Instance;
                if (session == null) return true;
                string nativeId = use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value;
                var contents = use.FsmVariables.FindFsmGameObject(c["contentsVariable"]).Value;
                BagFactory? factory = null;
                foreach (var candidate in _bagFactories.Values)
                    if (!candidate.Failed && FactoryItemIdentity.IsNativeId(nativeId, candidate.Prefix)
                        && candidate.Contents != null && candidate.Contents.gameObject == contents)
                    { factory = candidate; break; }
                if (factory == null) { _unreadyBags.Add(body); return true; }
                if (!session.IsHost) { HideLocalBag(use); return true; }
                uint id = FactoryItemIdentity.ItemId(factory.Id, nativeId);
                if (_bagOutputs.TryGetValue(body, out var output) && (output.NativeId != nativeId || output.Factory != factory))
                    throw new InvalidOperationException("Bag initialized with another factory or ID.");
                if (_bags.TryGetValue(id, out var duplicate) && duplicate.Body != null && duplicate.Body != body)
                    throw new InvalidOperationException("Duplicate live bag ID: " + nativeId);
                var bag = new BagBinding { Id = id, Body = body, Use = use, Factory = factory, NativeId = nativeId };
                bag.Remaining = ReadBagRemaining(bag); bag.Condition = ReadBagCondition(bag);
                RegisterBag(bag);
                SyncEventLog.Record("bag-bind", nativeId + " " + id.ToString("X8"));
            }
            catch (Exception e)
            {
                if (_bagBindingWarnings.Add(use.GetInstanceID()))
                {
                    WinterMPPlugin.Log.LogWarning("WorldSync: shopping bag binding unavailable: " + e.Message);
                    SyncEventLog.Record("bag-bind-disabled", e.Message);
                }
            }
            return true;
        }

        private static IList? BagArray(GameObject obj, string reference)
        {
            var proxy = BagArrayProxy(obj, reference);
            return proxy?.GetType().GetProperty("arrayList")?.GetValue(proxy, null) as IList;
        }

        private static MonoBehaviour? BagArrayProxy(GameObject obj, string reference)
        {
            MonoBehaviour? result = null;
            foreach (var component in obj.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "PlayMakerArrayListProxy"
                    || component.GetType().GetField("referenceName")?.GetValue(component) as string != reference) continue;
                if (result != null) throw new InvalidOperationException("Duplicate bag inventory array.");
                result = component;
            }
            return result;
        }

        private static ushort ReadBagRemaining(BagBinding bag)
        {
            var c = SyncCatalog.ShoppingBags!;
            if (bag.Use.FsmVariables.FindFsmBool(c["consumedVariable"])?.Value == true) return 0;
            var keys = BagArray(bag.Use.gameObject, c["keysReference"]);
            var values = BagArray(bag.Use.gameObject, c["valuesReference"]);
            if (keys == null || values == null || keys.Count != values.Count || keys.Count > 256)
                throw new InvalidOperationException("Bag inventory arrays changed.");
            int count = 0; var seen = new HashSet<string>();
            for (int i = 0; i < values.Count; i++)
            {
                if (keys[i] is not string name || string.IsNullOrEmpty(name) || name.Length > 128 || !seen.Add(name)
                    || values[i] is not int quantity || quantity < 0 || quantity > ushort.MaxValue - count)
                    throw new InvalidOperationException("Bag inventory contains invalid products or quantities.");
                count += quantity;
            }
            return (ushort)count;
        }

        private static float ReadBagCondition(BagBinding bag)
        {
            float value = bag.Use.FsmVariables.FindFsmFloat(SyncCatalog.ShoppingBags!["conditionVariable"])?.Value ?? float.NaN;
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Invalid bag food condition.");
            return Mathf.Clamp(value, 0f, 100f);
        }

        private static Dictionary<string, int> ReadBagProducts(BagBinding bag)
        {
            var products = new Dictionary<string, int>(StringComparer.Ordinal);
            if (ReadBagRemaining(bag) == 0) return products;
            var c = SyncCatalog.ShoppingBags!;
            var keys = BagArray(bag.Use.gameObject, c["keysReference"])!;
            var values = BagArray(bag.Use.gameObject, c["valuesReference"])!;
            for (int i = 0; i < keys.Count; i++)
                if ((int)values[i] > 0) products.Add((string)keys[i], (int)values[i]);
            return products;
        }

        private void HideLocalBag(PlayMakerFSM use)
        {
            if (_hiddenBags.ContainsKey(use)) return;
            if (_bagStorage == null) { _bagStorage = new GameObject("WinterMP local shopping bags"); _bagStorage.SetActive(false); }
            var body = use.GetComponent<Rigidbody>();
            uint? trackedId = null;
            foreach (var pair in _items)
                if (pair.Value.Body == body)
                {
                    ReleaseRemoteCargo(pair.Value, body, Time.unscaledTime, seedVelocity: false);
                    RestoreCargoPhysics(pair.Value);
                    if (pair.Value.KinematicSaved) body.isKinematic = pair.Value.OriginalKinematic;
                    trackedId = pair.Key; break;
                }
            ReleaseHeldBag(body);
            _hiddenBags.Add(use, new GuestPartIsolation(use.gameObject, _bagStorage.transform));
            if (trackedId.HasValue) RemoveTrackedItem(trackedId.Value, body);
        }

        private void ProcessBagBindings(SessionManager session)
        {
            if (SyncCatalog.ShoppingBags == null) return;
            if (_unreadyBags.Count > 0)
            {
                var retry = new List<Rigidbody>(_unreadyBags); _unreadyBags.Clear();
                foreach (var body in retry)
                    if (body != null && body.gameObject.activeInHierarchy) TryScanBag(body);
            }
            var done = new List<Rigidbody>();
            foreach (var pair in _bagOutputs)
            {
                var body = pair.Key; var output = pair.Value;
                if (body == null || output.Factory.Failed) { done.Add(pair.Key); continue; }
                TryScanBag(body);
                var use = FindBagUse(body);
                if ((use != null && _hiddenBags.ContainsKey(use)) || _bags.ContainsKey(FactoryItemIdentity.ItemId(output.Factory.Id, output.NativeId)))
                    done.Add(body);
                else if (Time.unscaledTime >= output.Deadline)
                {
                    FailBagFactory(output.Factory, "Bag initialization timed out.");
                    done.Add(body);
                }
            }
            foreach (var body in done) _bagOutputs.Remove(body);
        }

        private static void FailBagFactory(BagFactory factory, string reason)
        {
            if (factory.Failed) return;
            factory.Failed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: shopping bag factory " + factory.Rule.Path + " disabled: " + reason);
            SyncEventLog.Record("bag-factory-disabled", factory.Rule.Path + " " + reason);
        }

        private void ResetBagBindings()
        {
            foreach (var factory in _bagFactories.Values)
            {
                if (factory.Fsm == null) continue;
                if (factory.CreateState != null && factory.CreateHook != null)
                {
                    var actions = new List<FsmStateAction>(factory.CreateState.Actions);
                    actions.Remove(factory.CreateHook); factory.CreateState.Actions = actions.ToArray();
                }
                if (factory.LoadState != null && factory.LoadHook != null)
                {
                    var actions = new List<FsmStateAction>(factory.LoadState.Actions);
                    actions.Remove(factory.LoadHook); factory.LoadState.Actions = actions.ToArray();
                }
            }
            foreach (var hidden in _hiddenBags.Values) hidden.Restore();
            if (_bagStorage != null && _bagStorage.transform.childCount == 0) UnityEngine.Object.Destroy(_bagStorage);
            _bagStorage = null;
            _bagFactories.Clear(); _bagOutputs.Clear(); _unreadyBags.Clear(); _hiddenBags.Clear(); _bagBindingWarnings.Clear();
        }
    }
}
