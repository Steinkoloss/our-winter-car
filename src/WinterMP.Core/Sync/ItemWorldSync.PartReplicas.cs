using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class ReplacementBinding
        {
            public ReplacementFactory Factory = null!;
            public PlayMakerFSM Data = null!;
            public Rigidbody Body => Data.GetComponent<Rigidbody>();
            public string NativeId = string.Empty;
            public readonly ReplacementPartPublication Publication = new ReplacementPartPublication();
            public bool Replica;
            public bool FitValidated, FitFailed;
            public bool RemovalValidated, RemovalFailed;
            public BoxCollider? RemovalCollider;
            public PlayMakerFSM? FitMount;
            public PlayMakerFSM? SlotInstaller;
            public int FitHeldFrame = -10;
            public bool FittedPresentation;
            public Vector3 LooseScale;
            public string LooseTag = string.Empty;
            public readonly List<ReplacementCollider> Colliders = new List<ReplacementCollider>();
            public readonly List<ReplacementBody> Bodies = new List<ReplacementBody>();
            public bool ObservedBodySet;
            public Rigidbody? ObservedBody;
            public FsmState? GarbageState;
            public FsmStateAction? GarbageHook;
        }
        private readonly Dictionary<uint, ReplacementBinding> _replacementParts = new Dictionary<uint, ReplacementBinding>();
        private readonly HashSet<uint> _pendingReplacements = new HashSet<uint>();
        private readonly HashSet<PlayMakerFSM> _replacementGarbage = new HashSet<PlayMakerFSM>();
        private ReplacementPartReplica? _replacementReplica;
        private float _nextReplacementPoll;

        private void TrackReplacementPart(PlayMakerFSM data, uint id)
        {
            var session = SessionManager.Instance; var c = SyncCatalog.ReplacementParts;
            if (session == null || !session.IsHost || c == null || _replacementParts.ContainsKey(id)) return;
            string nativeId = data.FsmVariables.FindFsmString(c["itemIdVariable"]).Value;
            foreach (var factory in _replacementFactories.Values)
            {
                if (factory.Failed || !factory.Rule.Identity.TryId(nativeId, out uint candidate) || candidate != id) continue;
                try
                {
                    var garbage = FsmHook.FindState(data, c["garbageState"])
                        ?? throw new InvalidOperationException("Missing replacement disposal state.");
                    // Retain Data for the game's later deletion of its native save key.
                    var hook = new FsmHookAction(() => AnnounceNativePartDespawn(id, "replacement garbage"));
                    var actions = new List<FsmStateAction>(garbage.Actions); actions.Insert(0, hook); garbage.Actions = actions.ToArray();
                    _replacementParts.Add(id, new ReplacementBinding { Factory = factory, Data = data,
                        NativeId = nativeId, GarbageState = garbage, GarbageHook = hook });
                }
                catch (Exception e) { FailReplacementFactory(factory, e); }
                return;
            }
        }

        internal ReplacementPartState? BuildReplacementPartState(uint id)
        {
            var session = SessionManager.Instance; var c = SyncCatalog.ReplacementParts;
            if (session == null || !session.IsHost || c == null || _spawnLifecycle.IsRetired(id)
                || !_replacementParts.TryGetValue(id, out var binding) || binding.Replica || binding.Factory.Failed
                || binding.Data == null) return null;
            try
            {
                var vars = binding.Data.FsmVariables;
                var phase = NativePartIdentity.Phase(binding.Data);
                if (phase != NativePartPhase.Loose && phase != NativePartPhase.Fitted) return null;
                var state = new ReplacementPartState { FactoryId = binding.Factory.Rule.Identity.FactoryId, NativeId = binding.NativeId,
                    AssemblyId = vars.FindFsmInt(c["assemblyVariable"]).Value, Installed = phase == NativePartPhase.Fitted,
                    Scalars = new float[binding.Factory.Rule.Scalars.Length], Position = binding.Data.transform.position.ToNet(), Rotation = binding.Data.transform.rotation.ToNet() };
                for (int i = 0; i < state.Scalars.Length; i++)
                {
                    float value = vars.FindFsmFloat(binding.Factory.Rule.Scalars[i]).Value;
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Invalid replacement scalar.");
                    state.Scalars[i] = value;
                }
                if (state.AssemblyId < 0) throw new InvalidOperationException("Invalid replacement assembly ID.");
                CaptureReplacementAttachment(binding, state);
                state.RemovalAllowed = PublishRemovalAllowed(binding, state);
                var body = binding.Body;
                state.Revision = binding.Publication.Observe(state,
                    binding.ObservedBodySet && !object.ReferenceEquals(binding.ObservedBody, body));
                binding.ObservedBody = body; binding.ObservedBodySet = true;
                return state;
            }
            catch (Exception e) { FailReplacementFactory(binding.Factory, e); return null; }
        }

        internal IEnumerable<ReplacementPartState> BuildReplacementPartStates()
        {
            foreach (uint id in _replacementParts.Keys)
            {
                var state = BuildReplacementPartState(id); if (state != null) yield return state;
            }
        }

        internal void OnReplacementPartState(ReplacementPartState state)
        {
            var c = SyncCatalog.ReplacementParts; var session = SessionManager.Instance;
            if (c == null || session == null || session.IsHost) return;
            if (_replacementReplica == null) _replacementReplica = new ReplacementPartReplica(c.IdentityRules(), _spawnLifecycle);
            if (_replacementReplica.Receive(state, out uint id)) _pendingReplacements.Add(id);
        }

        private void ProcessReplacementParts(SessionManager session)
        {
            ProcessReplacementOutputs(session);
            ProcessReplacementGarbage();
            if (Time.unscaledTime < _nextReplacementPoll) return;
            _nextReplacementPoll = Time.unscaledTime + .2f;
            ProcessNativeParts(session);
            if (session.IsHost)
            {
                foreach (var state in BuildReplacementPartStates())
                {
                    PartIdentity.TryItemId(state.NativeId, out uint id);
                    var binding = _replacementParts[id];
                    if (!binding.Publication.NeedsBroadcast) continue;
                    session.SendWorldMessage(state, Channel.ReliableOrdered);
                    binding.Publication.MarkBroadcast(state.Revision);
                }
                return;
            }
            if (_replacementReplica == null || _unreadyNativeParts.Count != 0 || _replacementOutputs.Count != 0) return;
            RefreshReplacementAttachmentReadiness();
            var done = new List<uint>();
            foreach (uint id in _pendingReplacements)
            {
                var state = _replacementReplica.Get(id);
                if (state == null) { done.Add(id); continue; }
                if (!_replacementFactories.TryGetValue(state.FactoryId, out var factory) || factory.Failed || !factory.Suppressor.Active) continue;
                try
                {
                    if (_replacementParts.TryGetValue(id, out var binding) && binding.Replica && binding.Data != null && binding.Body != null)
                    {
                        if (!ApplyReplacementState(binding, id, state)) continue;
                    }
                    else if (_nativeParts.TryGetValue(id, out var data) && data != null)
                    {
                        if (data.FsmVariables.FindFsmString(SyncCatalog.ReplacementParts!["itemIdVariable"]).Value != state.NativeId)
                            throw new InvalidOperationException("Replacement overlaps an unrelated item.");
                        // Already present native parts retain their existing assembly/bolt adapter.
                    }
                    else if (factory.Rule.Identity.CanCreate(state)
                        || (factory.Rule.Identity.CanCreateFitted(state) && ResolveReplacementParent(state) != null))
                    {
                        if (!MaterializeReplacement(factory, id, state)) continue;
                    }
                    else continue;
                    done.Add(id);
                }
                catch (Exception e) { FailReplacementFactory(factory, e); }
            }
            foreach (uint id in done) _pendingReplacements.Remove(id);
        }

        private bool MaterializeReplacement(ReplacementFactory factory, uint id, ReplacementPartState state)
        {
            var c = SyncCatalog.ReplacementParts!;
            var templates = factory.Prefab.GetComponentsInChildren<PlayMakerFSM>(true);
            var enabled = new bool[templates.Length];
            GameObject clone;
            for (int i = 0; i < templates.Length; i++) enabled[i] = templates[i].enabled;
            try
            {
                foreach (var fsm in templates) fsm.enabled = false;
                clone = (GameObject)UnityEngine.Object.Instantiate(factory.Prefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { for (int i = 0; i < templates.Length; i++) templates[i].enabled = enabled[i]; }
            try
            {
                clone.name = state.NativeId;
                var data = FindReplacementData(clone, c); var body = clone.GetComponent<Rigidbody>();
                foreach (var fsm in clone.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    fsm.enabled = false;
                    if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                }
                var binding = new ReplacementBinding { Factory = factory, Data = data, NativeId = state.NativeId, Replica = true };
                foreach (var reference in factory.Rule.References)
                    data.FsmVariables.FindFsmGameObject(reference.Target).Value = factory.Fsm.FsmVariables.FindFsmGameObject(reference.Source).Value;
                ValidatePartFitEntry(binding);
                ValidatePartRemoval(binding);
                var init = FsmHook.FindState(data, c["itemInitState"])!;
                var initActions = (FsmStateAction[])init.Actions.Clone();
                // Native identity builders run, but Exists/Load never consult a guest save.
                initActions[initActions.Length - 1] = new FsmHookAction(() =>
                {
                    ApplyReplacementScalars(binding, state);
                    data.FsmVariables.FindFsmInt(c["assemblyVariable"]).Value = 0;
                    // Array-slot parts have AssemblyID but no Installed scratch flag.
                    var installed = data.FsmVariables.FindFsmBool(c["installedVariable"]);
                    if (installed != null) installed.Value = false;
                    data.SendEvent(c["freshEvent"]);
                });
                init.Actions = initActions;
                var status = FsmHook.FindState(data, c["itemStatusState"])!;
                var presentation = new List<FsmStateAction>();
                foreach (var action in status.Actions)
                    if (action.GetType().Name != "EnableFSM") presentation.Add(action);
                status.Actions = presentation.ToArray();
                // Replicas have no assembly authority. Keep the native model,
                // labels and adjustment pose; do not activate external mount/blocking FSMs.
                FsmHook.FindState(data, c["itemIdleState"])!.Actions = new FsmStateAction[] { new FsmHookAction(() =>
                {
                    body.isKinematic = false;
                    foreach (var collider in clone.GetComponents<Collider>()) { collider.enabled = true; collider.isTrigger = false; }
                    foreach (var variable in data.FsmVariables.GameObjectVariables)
                        if (variable.Name == c["boltsVariable"] || variable.Name == c["boltsVariable"] + "2")
                        {
                            var bolts = variable.Value;
                            if (bolts != null && ScenePath.RelativeTo(bolts.transform, clone.transform) != null) bolts.SetActive(false);
                        }
                }) };
                foreach (string key in new[] { "saveState", "deleteState", "loadState" })
                    FsmHook.FindState(data, c[key])!.Actions = new FsmStateAction[0];
                var globals = new List<FsmTransition>();
                foreach (var transition in data.Fsm.GlobalTransitions)
                    if (transition.EventName == c["garbageEvent"]) globals.Add(transition);
                data.Fsm.GlobalTransitions = globals.ToArray();
                FsmHook.FindState(data, c["garbageState"])!.Actions = new FsmStateAction[] { new FsmHookAction(() =>
                {
                    // The accepting host echoes removal; a rejected request leaves the part intact.
                    if (_replacementReplica?.AllowsLooseMotion(id) == true)
                        SessionManager.Instance?.SendWorldMessage(new ItemDespawn { ItemId = id }, Channel.ReliableOrdered);
                }) };
                _bridge.PartIdentities.MarkReplica(data);
                _replacementParts[id] = binding;
                data.Fsm.StartState = c["itemInitState"]; data.Fsm.RestartOnEnable = false;
                clone.SetActive(true); data.enabled = true;
                if (!data.Fsm.Started) data.Fsm.Start();
                if (!_bridge.PartIdentities.TryRootId(data, out uint actual) || actual != id)
                    throw new InvalidOperationException("Replacement initialization did not preserve identity.");
                TrackNativePart(data, id);
                PrepareReplacementPresentation(binding);
                bool applied = ApplyReplacementState(binding, id, state);
                SyncEventLog.Record("replacement-replica", state.NativeId + " " + id.ToString("X8"));
                return applied;
            }
            catch
            {
                var data = FindReplacementData(clone, c); data.enabled = false; _bridge.PartIdentities.Forget(data);
                RemoveTrackedItem(id, clone.GetComponent<Rigidbody>()); _replacementParts.Remove(id); _nativeParts.Remove(id);
                clone.SetActive(false); UnityEngine.Object.Destroy(clone); throw;
            }
        }

        private static void ApplyReplacementScalars(ReplacementBinding binding, ReplacementPartState state)
        {
            for (int i = 0; i < state.Scalars.Length; i++)
                binding.Data.FsmVariables.FindFsmFloat(binding.Factory.Rule.Scalars[i]).Value = state.Scalars[i];
        }

        private void RetireHiddenReplacement(uint id)
        {
            if (_items.ContainsKey(id) || !_replacementParts.TryGetValue(id, out var binding)
                || !binding.Replica || binding.Data == null || binding.Body == null) return;
            TryRetireReplacement(binding.Body);
        }

        private bool CanGuestRetireReplacement(uint id)
        {
            if (!_replacementParts.TryGetValue(id, out var binding)) return true;
            var state = BuildReplacementPartState(id);
            return state != null && binding.Factory.Rule.Identity.CanCreate(state);
        }

        private bool TryRetireReplacement(Rigidbody body)
        {
            foreach (var pair in _replacementParts)
            {
                var binding = pair.Value;
                if (binding.Data == null || binding.Body != body) continue;
                if (binding.Replica)
                {
                    binding.Data.enabled = false; body.gameObject.SetActive(false);
                    _bridge.PartIdentities.Forget(binding.Data);
                    UnityEngine.Object.Destroy(body.gameObject);
                }
                else
                {
                    binding.Data.FsmVariables.FindFsmBool(SyncCatalog.ReplacementParts!["consumedVariable"]).Value = true;
                    _replacementGarbage.Add(binding.Data);
                }
                return true;
            }
            return false;
        }

        private void ProcessReplacementGarbage()
        {
            if (_replacementGarbage.Count == 0) return;
            var pending = new List<PlayMakerFSM>(_replacementGarbage);
            foreach (var data in pending)
            {
                if (!data) { _replacementGarbage.Remove(data); continue; }
                if (!data.Fsm.Started || !data.enabled || !data.gameObject.activeInHierarchy) continue;
                bool applying = _bridge.ApplyingRemote;
                try { _bridge.ApplyingRemote = true; data.SendEvent(SyncCatalog.ReplacementParts!["garbageEvent"]); _replacementGarbage.Remove(data); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: replacement disposal deferred: " + e.Message); }
                finally { _bridge.ApplyingRemote = applying; }
            }
        }

        private void ClearReplacementParts()
        {
            ClearPartFitting();
            ProcessReplacementGarbage();
            // Owned replicas may be nested; detach before any deferred Destroy call.
            foreach (var binding in _replacementParts.Values)
                if (binding.Replica && binding.Data != null) binding.Data.transform.SetParent(null, true);
            foreach (var pair in _replacementParts)
            {
                var binding = pair.Value;
                if (binding.Replica && binding.Data != null)
                {
                    binding.Data.enabled = false; binding.Data.gameObject.SetActive(false);
                    _bridge.PartIdentities.Forget(binding.Data);
                    RemoveTrackedItem(pair.Key, binding.Body); UnityEngine.Object.Destroy(binding.Data.gameObject);
                }
                else if (binding.GarbageState != null && binding.GarbageHook != null)
                    RemoveReplacementHook(binding.GarbageState, binding.GarbageHook);
            }
            foreach (var factory in _replacementFactories.Values)
            {
                foreach (var hook in factory.Hooks) RemoveReplacementHook(hook.Key, hook.Value);
                factory.Suppressor.Restore();
            }
            _replacementParts.Clear(); _replacementFactories.Clear(); _replacementOutputs.Clear(); _unreadyNativeParts.Clear();
            _pendingReplacements.Clear(); _replacementReplica?.Clear(); _replacementReplica = null; _replacementGarbage.Clear();
            _nextReplacementPoll = 0;
            _nativeParts.Clear();
        }

        private static void RemoveReplacementHook(FsmState state, FsmStateAction hook)
        {
            hook.Enabled = false;
            try { var actions = new List<FsmStateAction>(state.Actions); actions.Remove(hook); state.Actions = actions.ToArray(); }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: replacement hook cleanup: " + e.Message); }
        }
    }
}
