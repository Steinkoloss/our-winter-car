using System;
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
        private sealed class PackageBinding
        {
            public Rigidbody Body = null!;
            public PlayMakerFSM Use = null!;
            public FsmState GarbageState = null!;
            public FsmStateAction Hook = null!;
            public PackageFactory Factory = null!;
            public string NativeId = string.Empty;
            public bool Replica;
            public bool OpenFailed;
            public FsmState? OpenState;
            public FsmStateAction? OpenGuard, OpenTail;
            public readonly PackagePublication Publication = new PackagePublication();
        }

        private sealed class HiddenPackage
        {
            public Rigidbody Body = null!;
            public bool Active, Kinematic;
            public string ResumeState = string.Empty;
            public readonly FsmSuppressor Suppressor = new FsmSuppressor();
        }

        private readonly Dictionary<uint, PackageBinding> _packages = new Dictionary<uint, PackageBinding>();
        private readonly HashSet<PlayMakerFSM> _pendingPackageRemovals = new HashSet<PlayMakerFSM>();
        private readonly HashSet<int> _packageWarnings = new HashSet<int>();
        private readonly Dictionary<PlayMakerFSM, HiddenPackage> _hiddenPackages = new Dictionary<PlayMakerFSM, HiddenPackage>();

        private static PlayMakerFSM? FindPackageUse(Rigidbody body)
        {
            var c = SyncCatalog.PartsPackages;
            if (c == null || body == null) return null;
            foreach (var use in body.GetComponents<PlayMakerFSM>())
            {
                if (use.FsmName == c["itemFsm"] && use.FsmVariables.FindFsmString(c["itemIdVariable"]) != null
                    && use.FsmVariables.FindFsmGameObject(c["contentsVariable"]) != null
                    && use.FsmVariables.FindFsmInt(c["quantityVariable"]) != null
                    && FsmHook.FindState(use, c["openState"]) != null) return use;
            }
            return null;
        }

        private bool TryScanPackage(Rigidbody body)
        {
            var use = FindPackageUse(body);
            if (use == null) return false;
            foreach (var package in _packages.Values)
                if (package.Replica && package.Body == body) return true;
            // A box before native GetName/Load completes must not acquire a
            // transient package(Clone)#ordinal identity that survives initialization.
            var c = SyncCatalog.PartsPackages!;
            if (!use.Fsm.Initialized || !use.Fsm.Started || !use.enabled
                || Array.IndexOf(c.ReadyStates, use.ActiveStateName) < 0)
            { _unreadyPackages.Add(body); return true; }
            _unreadyPackages.Remove(body);
            try
            {
                var session = SessionManager.Instance;
                if (session == null) return true;
                if (!session.IsHost) { HideLocalPackage(use); return true; }
                string nativeId = use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value;
                var contents = use.FsmVariables.FindFsmGameObject(c["contentsVariable"]).Value;
                if (contents == null || !c.Identities.TryResolve(nativeId, ScenePath.Of(contents.transform), out uint id))
                    throw new InvalidOperationException("Unknown persistent ID or mismatched contents factory: " + nativeId);

                PackageFactory? factory = null;
                foreach (var candidate in _packageFactories.Values)
                    if (!candidate.Failed && FactoryItemIdentity.IsNativeId(nativeId, candidate.Rule.Prefix))
                    { factory = candidate; break; }
                if (factory == null) return true;

                if (_spawnLifecycle.IsRetired(id))
                {
                    RetirePackage(use);
                    return true;
                }
                if (_packages.TryGetValue(id, out var binding))
                {
                    if (binding.Body == body) return true;
                    if (binding.Body != null) throw new InvalidOperationException("Duplicate live package ID: " + nativeId);
                    RemovePackageHook(binding); _packages.Remove(id);
                }
                if (_items.TryGetValue(id, out var existing))
                {
                    if (existing.Body != null && existing.Body != body)
                        throw new InvalidOperationException("Package item ID collision: " + nativeId);
                    if (existing.Body == null) RemoveTrackedItem(id, existing.Body);
                }
                else if (_trackedBodies.ContainsKey(body))
                    throw new InvalidOperationException("Package already has a transient item identity: " + nativeId);

                var garbage = ValidatePackageGarbage(use);
                var originalActions = garbage.Actions;
                var hook = new FsmHookAction(() => AnnounceItemDespawn(id, "package garbage"));
                var actions = new FsmStateAction[originalActions.Length + 1];
                actions[0] = hook;
                Array.Copy(originalActions, 0, actions, 1, originalActions.Length);
                garbage.Actions = actions;
                binding = new PackageBinding { Body = body, Use = use, GarbageState = garbage, Hook = hook,
                    Factory = factory, NativeId = nativeId };
                _packages.Add(id, binding);
                RegisterPackageOpening(id, binding);
                _trackedBodies[body] = true;
                if (!_items.ContainsKey(id))
                    _items.Add(id, new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform),
                        LastPosition = body.transform.position });
                if (_pendingItemPoses.TryGetValue(id, out var pose))
                {
                    _pendingItemPoses.Remove(id);
                    if (Time.unscaledTime < pose.ExpiresAt) ApplySnapshotPose(_items[id], pose.Position, pose.Rotation);
                }
                SyncEventLog.Record("package-bind", nativeId + " " + id.ToString("X8"));
            }
            catch (Exception e) { WarnPackage(use, e.Message); }
            return true;
        }

        private static FsmState ValidatePackageGarbage(PlayMakerFSM use)
        {
            var c = SyncCatalog.PartsPackages!;
            var state = FsmHook.FindState(use, c["garbageState"]);
            string[] types = { "SetIntValue", "DestroyComponent", "DestroyComponent", "DestroyComponent", "SetPosition" };
            if (state == null || state.Actions.Length != types.Length)
                throw new InvalidOperationException("Package garbage state changed.");
            for (int i = 0; i < types.Length; i++)
                if (!state.Actions[i].Enabled || state.Actions[i].GetType().Name != types[i])
                    throw new InvalidOperationException("Package garbage actions changed.");
            var variable = PackageField<FsmInt>(state.Actions[0], "intVariable");
            var value = PackageField<FsmInt>(state.Actions[0], "intValue");
            if (variable == null || variable.Name != c["quantityVariable"] || value == null || value.UseVariable || value.Value != 0)
                throw new InvalidOperationException("Package garbage no longer clears quantity.");
            var save = FsmHook.FindState(use, c["saveState"]);
            var delete = FsmHook.FindState(use, c["deleteState"]);
            if (save == null || save.Actions.Length != 3 || save.Actions[0].GetType().Name != "IntCompare"
                || !save.Actions[0].Enabled || delete == null || delete.Actions.Length != 1
                || delete.Actions[0].GetType().Name != "Delete" || !delete.Actions[0].Enabled)
                throw new InvalidOperationException("Package save cleanup changed.");
            var quantity = PackageField<FsmInt>(save.Actions[0], "integer1");
            var threshold = PackageField<FsmInt>(save.Actions[0], "integer2");
            if (quantity == null || quantity.Name != c["quantityVariable"] || threshold == null
                || threshold.UseVariable || threshold.Value != 0
                || PackageField<FsmEvent>(save.Actions[0], "equal")?.Name != c["deleteEvent"]
                || PackageField<FsmEvent>(save.Actions[0], "lessThan")?.Name != c["deleteEvent"]
                || PackageField<FsmString>(delete.Actions[0], "uniqueTag")?.Name != c["itemIdVariable"])
                throw new InvalidOperationException("Package save cleanup no longer deletes its persistent ID at zero quantity.");
            bool deletes = false, saves = false;
            foreach (var transition in save.Transitions)
                if (transition.EventName == c["deleteEvent"] && transition.ToState == c["deleteState"]) deletes = true;
            foreach (var transition in use.Fsm.GlobalTransitions)
                if (transition.EventName == c["saveEvent"] && transition.ToState == c["saveState"]) saves = true;
            if (!deletes || !saves) throw new InvalidOperationException("Package save cleanup transitions changed.");
            foreach (var transition in use.Fsm.GlobalTransitions)
                if (transition.EventName == c["garbageEvent"] && transition.ToState == c["garbageState"]) return state;
            throw new InvalidOperationException("Package garbage event changed.");
        }

        private static T? PackageField<T>(FsmStateAction action, string name) where T : class =>
            action.GetType().GetField(name)?.GetValue(action) as T;

        private bool TryRetirePackage(Rigidbody body)
        {
            var use = FindPackageUse(body);
            if (use == null) return false;
            if (SessionManager.Instance != null && !SessionManager.Instance.IsHost)
            {
                if (DestroyPackageReplica(use)) return true;
                // Capture the item's original physics before the despawn receiver
                // removes its ownership/cargo record. A deferred capture would keep
                // the temporary remote-owner kinematic flag on disconnect.
                try { HideLocalPackage(use); return true; }
                catch (Exception e) { WarnPackage(use, "Guest retirement deferred: " + e.Message); }
            }
            RetirePackage(use);
            return true;
        }

        private void RetirePackage(PlayMakerFSM use)
        {
            // Native GARBAGE removes the physical box but leaves Use alive to
            // delete its saved ID on SAVEGAME. Destroy(GameObject) resurrects it.
            _pendingPackageRemovals.Add(use);
        }

        private void ProcessPackageRemovals()
        {
            if (_pendingPackageRemovals.Count == 0) return;
            var done = new List<PlayMakerFSM>();
            foreach (var use in _pendingPackageRemovals)
            {
                if (!use) { done.Add(use); continue; }
                if (!use.Fsm.Initialized || !use.Fsm.Started || !use.enabled || !use.gameObject.activeInHierarchy) continue;
                try
                {
                    bool bound = false;
                    foreach (var binding in _packages.Values)
                        if (binding.Use == use) { bound = true; break; }
                    if (!bound) ValidatePackageGarbage(use);
                    var session = SessionManager.Instance;
                    if (session == null) continue;
                    if (!session.IsHost)
                    {
                        if (!DestroyPackageReplica(use)) HideLocalPackage(use);
                        done.Add(use);
                        continue;
                    }
                    bool applying = _bridge.ApplyingRemote;
                    _bridge.ApplyingRemote = true;
                    try { use.SendEvent(SyncCatalog.PartsPackages!["garbageEvent"]); }
                    finally { _bridge.ApplyingRemote = applying; }
                    done.Add(use);
                }
                catch (Exception e)
                {
                    WarnPackage(use, "Retirement deferred: " + e.Message);
                }
            }
            foreach (var use in done) _pendingPackageRemovals.Remove(use);
        }

        private void HideLocalPackage(PlayMakerFSM use)
        {
            if (_hiddenPackages.ContainsKey(use)) return;
            var body = use.GetComponent<Rigidbody>();
            if (body == null) return;
            var c = SyncCatalog.PartsPackages!;
            var hidden = new HiddenPackage { Body = body, Active = use.gameObject.activeSelf, Kinematic = body.isKinematic };
            if (use.ActiveStateName == c["garbageState"])
            {
                // The guest's garbage override did not run native component deletion.
                // Resume a normal interaction state on disconnect, never GetName/Load.
                hidden.ResumeState = use.FsmVariables.FindFsmInt(c["quantityVariable"]).Value <= 0
                    ? c["emptyState"] : c["itemIdleState"];
                if (!FsmHook.EnsureRemoteEntry(use, hidden.ResumeState))
                    throw new InvalidOperationException("Cannot restore hidden package interaction.");
            }
            uint? trackedId = null;
            foreach (var pair in _items)
                if (pair.Value.Body == body)
                {
                    if (pair.Value.KinematicSaved) hidden.Kinematic = pair.Value.OriginalKinematic;
                    ReleaseRemoteCargo(pair.Value, body, Time.unscaledTime, seedVelocity: false);
                    RestoreCargoPhysics(pair.Value);
                    trackedId = pair.Key; break;
                }
            if (!hidden.Suppressor.Suppress(use)) throw new InvalidOperationException("Cannot preserve guest package.");
            _hiddenPackages.Add(use, hidden);
            use.gameObject.SetActive(false);
            if (trackedId.HasValue) RemoveTrackedItem(trackedId.Value, body);
        }

        private void WarnPackage(PlayMakerFSM use, string reason)
        {
            if (!_packageWarnings.Add(use.GetInstanceID())) return;
            WinterMPPlugin.Log.LogWarning("WorldSync: package sync unavailable: " + reason);
            SyncEventLog.Record("package-disabled", reason);
        }

        private static void RemovePackageHook(PackageBinding binding)
        {
            RemovePackageOpeningHooks(binding);
            if (binding.Use == null || binding.Replica) return;
            var actions = new List<FsmStateAction>(binding.GarbageState.Actions);
            actions.Remove(binding.Hook); binding.GarbageState.Actions = actions.ToArray();
        }

        private void ClearPackages()
        {
            ClearPackageOpenings();
            foreach (var pair in _packages)
            {
                var binding = pair.Value;
                RemovePackageHook(binding);
                if (binding.Replica && binding.Use != null)
                { binding.Use.enabled = false; UnityEngine.Object.Destroy(binding.Use.gameObject); }
                if (_items.TryGetValue(pair.Key, out var item) && item.Body == binding.Body)
                    RemoveTrackedItem(pair.Key, binding.Body);
            }
            foreach (var pair in _hiddenPackages)
            {
                if (pair.Key == null) continue;
                var hidden = pair.Value;
                if (hidden.Body != null) hidden.Body.isKinematic = hidden.Kinematic;
                pair.Key.gameObject.SetActive(hidden.Active);
                hidden.Suppressor.Restore();
                if (hidden.ResumeState.Length != 0) FsmHook.FireRemoteEntry(pair.Key, hidden.ResumeState);
            }
            _packages.Clear(); _pendingPackageRemovals.Clear(); _packageWarnings.Clear();
            _hiddenPackages.Clear();
            _packageReplica?.Clear(); _packageReplica = null; _pendingPackageStates.Clear();
            _nextPackagePoll = 0; _nextPackageNotice = 0;
            ClearPackageFactories();
        }
    }
}
