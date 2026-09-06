using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class BagSpillFactory
        {
            public PlayMakerFSM Fsm = null!, Use = null!;
            public Rigidbody Template = null!;
            public FsmGameObject Output = null!;
            public string Path = string.Empty, Failure = string.Empty;
            public bool Package;
            public readonly HashSet<string> Names = new HashSet<string>();
            public readonly Queue<BagSpillRequest> Requests = new Queue<BagSpillRequest>();
        }

        private sealed class BagSpillRequest
        {
            public BagOpening Opening = null!;
            public GameObject? Previous;
        }

        private sealed class BagSpillSource
        {
            public PlayMakerFSM Fsm = null!;
            public FsmState State = null!;
            public FsmEventTarget Target = null!;
            public string Path = string.Empty;
        }

        private readonly Dictionary<PlayMakerFSM, BagSpillFactory> _bagSpillFactories = new Dictionary<PlayMakerFSM, BagSpillFactory>();
        private readonly Dictionary<PlayMakerFSM, List<BagSpillSource>> _bagSpillSources = new Dictionary<PlayMakerFSM, List<BagSpillSource>>();
        private readonly List<BagHook> _bagSpillHooks = new List<BagHook>();
        private readonly Dictionary<Rigidbody, BagSpillFactory> _bagSpillBodies = new Dictionary<Rigidbody, BagSpillFactory>();
        private readonly Dictionary<string, Rigidbody> _bagSpillTemplates = new Dictionary<string, Rigidbody>();
        private readonly HashSet<string> _ambiguousBagSpillTemplates = new HashSet<string>();
        private string _bagSpillFailure = string.Empty;
        private float _nextBagSpillDiscovery;

        private void PrepareBagSpillCapture()
        {
            var c = SyncCatalog.ShoppingBags;
            if (c == null) throw new InvalidOperationException("Shopping bag spill catalog is unavailable.");
            RefreshPackageFactories();
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || !fsm.Fsm.Initialized) continue;
                string path = ScenePath.Of(fsm.transform);
                if (Array.IndexOf(c.SpillFactoryPaths, path) >= 0 && !_bagSpillFactories.ContainsKey(fsm))
                {
                    var factory = new BagSpillFactory { Fsm = fsm, Path = path };
                    _bagSpillFactories.Add(fsm, factory);
                    try { BindBagSpillFactory(factory, c); }
                    catch (Exception e) { factory.Failure = e.Message; }
                }
                if (fsm.FsmName != c["contentsFsm"] || _bagSpillSources.ContainsKey(fsm)) continue;
                foreach (var rule in c.Factories)
                {
                    if (path != rule.ContentsPath) continue;
                    BindBagSpillSources(fsm, c);
                    break;
                }
            }
        }

        private void BindBagSpillFactory(BagSpillFactory factory, ShoppingBagsData c)
        {
            var fsm = factory.Fsm;
            var prefab = fsm.FsmVariables.FindFsmGameObject(c["spillPrefabVariable"])?.Value;
            var output = fsm.FsmVariables.FindFsmGameObject(c["spillOutputVariable"]);
            var state = FsmHook.FindState(fsm, c["spillCreateState"]);
            if (prefab == null || output == null || state == null)
                throw new InvalidOperationException("Native bag product references are unavailable.");
            foreach (var package in _packageFactories.Values)
                if (package.Fsm == fsm)
                {
                    if (package.Failed) throw new InvalidOperationException("Native bag package factory is disabled.");
                    factory.Package = true;
                }
            var actions = new List<FsmStateAction>();
            foreach (var action in state.Actions) if (!(action is FsmHookAction)) actions.Add(action);
            int creates = 0;
            foreach (var action in actions)
            {
                string type = action.GetType().Name;
                if (!action.Enabled || !BagSpillActionAllowed(type)
                    || PackageField<object>(action, "everyFrame") is bool everyFrame && everyFrame)
                    throw new InvalidOperationException("Native bag product actions changed.");
                if (type != "CreateObject") continue;
                creates++;
                if (PackageField<FsmGameObject>(action, "gameObject")?.Name != c["spillPrefabVariable"]
                    || PackageField<FsmGameObject>(action, "storeObject")?.Name != c["spillOutputVariable"]
                    || PackageField<FsmGameObject>(action, "spawnPoint")?.Name != c["spillSpawnPointVariable"])
                    throw new InvalidOperationException("Product is not a direct shopping bag output.");
            }
            if (creates != 1 || state.Transitions.Length != 1 || state.Transitions[0].EventName != "FINISHED"
                || state.Transitions[0].ToState != c["spillIdleState"])
                throw new InvalidOperationException("Native bag output completion changed.");
            if (FsmHook.FindState(fsm, c["spillIdleState"]) == null)
                throw new InvalidOperationException("Native bag product idle state is missing.");
            var template = prefab.GetComponent<Rigidbody>();
            PlayMakerFSM? use = null;
            foreach (var candidate in prefab.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == c["itemFsm"])
                {
                    if (use != null) throw new InvalidOperationException("Ambiguous bag product Use FSM.");
                    use = candidate;
                }
            if (template == null || use == null)
                throw new InvalidOperationException("This bag product needs a dedicated native part adapter.");
            if (!use.Fsm.Initialized) use.Fsm.Init(use);
            factory.Template = template; factory.Use = use; factory.Output = output;
            if (!factory.Package)
            {
                foreach (var itemState in use.Fsm.States)
                    foreach (var action in itemState.Actions)
                    {
                        if (action.GetType().Name != "SetName") continue;
                        var name = PackageField<FsmString>(action, "name");
                        var target = PackageField<FsmOwnerDefault>(action, "gameObject");
                        if (name == null || name.UseVariable || string.IsNullOrEmpty(name.Value) || target == null
                            || (target.OwnerOption != OwnerDefaultOption.UseOwner
                                && !(target.GameObject.UseVariable && target.GameObject.Name == c["ownerVariable"]))) continue;
                        factory.Names.Add(name.Value);
                        CacheBagSpillTemplate(name.Value, template);
                    }
                if (factory.Names.Count == 0) throw new InvalidOperationException("Native bag product names are unavailable.");
            }
            // The verified actions cannot transition before FINISHED. New is captured
            // after its native ID/name has been assigned, before any scanner can run.
            AddBagSpillHook(fsm, state, new FsmHookAction(() => CaptureBagSpillOutput(factory)), false);
        }

        private static bool BagSpillActionAllowed(string type)
        {
            switch (type)
            {
                case "IntAdd": case "CreateObject": case "GetFsmFloat": case "SetFsmFloat":
                case "SetFsmGameObject": case "SetFsmInt": case "SetParent": case "SetVelocity":
                case "ConvertIntToString": case "BuildString": case "BuildStringFast": case "SetName": return true;
                default: return false;
            }
        }

        private void BindBagSpillSources(PlayMakerFSM fsm, ShoppingBagsData c)
        {
            var sources = new List<BagSpillSource>();
            foreach (var state in fsm.Fsm.States)
            {
                foreach (var action in state.Actions)
                {
                    if (action.GetType().Name != "SendEventByName"
                        || PackageField<FsmString>(action, "sendEvent")?.Value != c["spillEvent"]) continue;
                    var target = PackageField<FsmEventTarget>(action, "eventTarget");
                    var delay = PackageField<FsmFloat>(action, "delay");
                    var destination = target != null ? fsm.Fsm.GetOwnerDefaultTarget(target.gameObject) : null;
                    string path = destination != null ? ScenePath.Of(destination.transform) : string.Empty;
                    if (!action.Enabled || target == null || target.target != FsmEventTarget.EventTarget.GameObjectFSM
                        || !target.fsmName.UseVariable || target.fsmName.Name != c["spillProductVariable"]
                        || target.gameObject.OwnerOption != OwnerDefaultOption.SpecifyGameObject || target.gameObject.GameObject.UseVariable
                        || target.sendToChildren.Value || delay == null || delay.UseVariable || delay.Value != 0f
                        || PackageField<object>(action, "everyFrame") is not bool everyFrame || everyFrame
                        || Array.IndexOf(c.SpillFactoryPaths, path) < 0 || state.Actions.Length != 1)
                        throw new InvalidOperationException("Native shopping bag product dispatch changed.");
                    sources.Add(new BagSpillSource { Fsm = fsm, State = state, Target = target, Path = path });
                }
            }
            if (sources.Count == 0) throw new InvalidOperationException("Shopping bag has no supported product dispatch.");
            foreach (var source in sources)
                AddBagSpillHook(fsm, source.State, new FsmHookAction(() => QueueBagSpillOutput(source)), true);
            _bagSpillSources.Add(fsm, sources);
        }

        private void AddBagSpillHook(PlayMakerFSM fsm, FsmState state, FsmStateAction hook, bool prepend)
        {
            var actions = new FsmStateAction[state.Actions.Length + 1];
            Array.Copy(state.Actions, 0, actions, prepend ? 1 : 0, state.Actions.Length);
            actions[prepend ? 0 : actions.Length - 1] = hook;
            state.Actions = actions;
            _bagSpillHooks.Add(new BagHook { Use = fsm, State = state, Action = hook });
        }

        private BagSpillFactory? FindBagSpillFactory(string path, string product)
        {
            BagSpillFactory? found = null;
            foreach (var factory in _bagSpillFactories.Values)
                if (factory.Fsm != null && factory.Path == path && factory.Fsm.FsmName == product)
                {
                    if (found != null) throw new InvalidOperationException("Ambiguous native bag product factory.");
                    found = factory;
                }
            return found;
        }

        private PendingSpawn BeginBagSpill(BagBinding bag, string stateName)
        {
            var c = SyncCatalog.ShoppingBags;
            if (c == null) throw new InvalidOperationException("Shopping bag spill catalog is unavailable.");
            if (!_bagSpillSources.TryGetValue(bag.Factory.Contents, out var sources))
                throw new InvalidOperationException("Shopping bag contents capture is not ready.");
            foreach (var product in ReadBagProducts(bag))
            {
                BagSpillFactory? found = null;
                foreach (var source in sources)
                {
                    var candidate = FindBagSpillFactory(source.Path, product.Key);
                    if (candidate == null) continue;
                    if (found != null && found != candidate) throw new InvalidOperationException("Ambiguous bag inventory product.");
                    found = candidate;
                }
                if (found == null || found.Failure.Length != 0 || !found.Fsm.enabled || !found.Fsm.Fsm.Started
                    || found.Fsm.ActiveStateName != c["spillIdleState"])
                    throw new InvalidOperationException("Shopping bag product is unavailable: " + product.Key
                        + (found != null && found.Failure.Length != 0 ? " (" + found.Failure + ")" : string.Empty));
            }
            foreach (var factory in _bagSpillFactories.Values) factory.Requests.Clear();
            _bagSpillBodies.Clear(); _bagSpillFailure = string.Empty;
            return new PendingSpawn { ContainerId = bag.Id, Epoch = MintSpawnEpoch(bag.Id), StateName = stateName,
                IsHost = true, Near = bag.Body.position, HasNear = true, StableSince = Time.unscaledTime };
        }

        private void QueueBagSpillOutput(BagSpillSource source)
        {
            var opening = _bagOpening; var c = SyncCatalog.ShoppingBags;
            if (opening == null || c == null || opening.Bag.Factory.Contents != source.Fsm) return;
            try
            {
                if (source.Fsm.FsmVariables.FindFsmGameObject(c["currentBagVariable"])?.Value != opening.Bag.Use.gameObject)
                    throw new InvalidOperationException("Native product dispatch belongs to a different bag.");
                var factory = FindBagSpillFactory(source.Path, source.Target.fsmName.Value);
                if (factory == null || factory.Failure.Length != 0)
                    throw new InvalidOperationException("Native bag product factory became unavailable.");
                factory.Requests.Enqueue(new BagSpillRequest { Opening = opening, Previous = factory.Output.Value });
            }
            catch (Exception e) { _bagSpillFailure = e.Message; }
        }

        private void CaptureBagSpillOutput(BagSpillFactory factory)
        {
            if (factory.Requests.Count == 0) return;
            var request = factory.Requests.Dequeue();
            try
            {
                var opening = request.Opening;
                var output = factory.Output.Value;
                var body = output != null ? output.GetComponent<Rigidbody>() : null;
                if (opening != _bagOpening || output == request.Previous || body == null
                    || opening.Capture.Captured.Contains(body))
                    throw new InvalidOperationException("Native bag factory did not produce one new body.");
                if (!factory.Package)
                {
                    if (_trackedBodies.ContainsKey(body)) throw new InvalidOperationException("Bag output already has an item identity.");
                    _trackedBodies[body] = true;
                }
                opening.Capture.Captured.Add(body);
                opening.Capture.StableSince = Time.unscaledTime;
                _bagSpillBodies[body] = factory;
            }
            catch (Exception e) { _bagSpillFailure = e.Message; }
        }

        private bool BagSpillReady(BagOpening opening)
        {
            if (_bagSpillFailure.Length != 0) throw new InvalidOperationException(_bagSpillFailure);
            foreach (var factory in _bagSpillFactories.Values)
                foreach (var request in factory.Requests)
                    if (request.Opening == opening) return false;
            foreach (var body in opening.Capture.Captured)
            {
                if (body == null || !body.gameObject.activeInHierarchy || !_bagSpillBodies.TryGetValue(body, out var factory)) return false;
                if (factory.Package)
                {
                    bool registered = false;
                    foreach (var item in _items.Values) if (item.Body == body) { registered = true; break; }
                    if (!registered) return false;
                    continue;
                }
                if (!factory.Names.Contains(body.gameObject.name)) return false;
                foreach (var use in body.GetComponents<PlayMakerFSM>())
                {
                    if (use.FsmName != factory.Use.FsmName) continue;
                    if (!use.Fsm.Started || use.ActiveStateName == use.Fsm.StartState) return false;
                    var state = FsmHook.FindState(use, use.ActiveStateName);
                    if (state == null) return false;
                    foreach (var action in state.Actions)
                        if (action.GetType().Name.StartsWith("Load", StringComparison.Ordinal)) return false;
                }
            }
            return Time.unscaledTime - opening.Capture.StableSince >= SpawnCaptureStableSeconds;
        }

        private void CacheBagSpillTemplate(string name, Rigidbody template)
        {
            if (_ambiguousBagSpillTemplates.Contains(name)) return;
            if (_bagSpillTemplates.TryGetValue(name, out var existing) && existing != template)
            {
                _bagSpillTemplates.Remove(name); _ambiguousBagSpillTemplates.Add(name);
            }
            else _bagSpillTemplates[name] = template;
        }

        private Rigidbody? FindBagSpillTemplate(string name)
        {
            if (_bagSpillTemplates.TryGetValue(name, out var template) && template != null) return template;
            if (Time.unscaledTime >= _nextBagSpillDiscovery)
            {
                _nextBagSpillDiscovery = Time.unscaledTime + 1f;
                PrepareBagSpillCapture();
            }
            return _bagSpillTemplates.TryGetValue(name, out template) && template != null ? template : null;
        }

        private void ClearBagSpillCapture()
        {
            foreach (var hook in _bagSpillHooks)
            {
                if (hook.Use == null) continue;
                var actions = new List<FsmStateAction>(hook.State.Actions); actions.Remove(hook.Action);
                hook.State.Actions = actions.ToArray();
            }
            _bagSpillHooks.Clear(); _bagSpillFactories.Clear(); _bagSpillSources.Clear();
            _bagSpillBodies.Clear(); _bagSpillTemplates.Clear(); _ambiguousBagSpillTemplates.Clear();
            _bagSpillFailure = string.Empty; _nextBagSpillDiscovery = 0;
        }
    }
}
