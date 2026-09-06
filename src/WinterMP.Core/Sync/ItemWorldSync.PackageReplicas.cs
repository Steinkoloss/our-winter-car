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
        private PackageReplica? _packageReplica;
        private readonly HashSet<uint> _pendingPackageStates = new HashSet<uint>();
        private float _nextPackagePoll, _nextPackageNotice;

        internal void OnPackageState(PackageState message)
        {
            var session = SessionManager.Instance;
            var c = SyncCatalog.PartsPackages;
            if (session == null || session.IsHost || c == null) return;
            if (_packageReplica == null) _packageReplica = new PackageReplica(c.Identities, _spawnLifecycle);
            if (_packageReplica.Receive(message, out uint id)) _pendingPackageStates.Add(id);
        }

        internal PackageState? BuildPackageState(uint id)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _spawnLifecycle.IsRetired(id)
                || !_packages.TryGetValue(id, out var binding) || binding.Replica || binding.Factory.Failed
                || binding.Body == null || binding.Use == null || !_items.ContainsKey(id)) return null;
            try
            {
                var c = SyncCatalog.PartsPackages!;
                int quantity = binding.Use.FsmVariables.FindFsmInt(c["quantityVariable"]).Value;
                if (quantity < 0 || quantity > binding.Factory.Rule.Capacity)
                    throw new InvalidOperationException("Package quantity exceeds native capacity.");
                return new PackageState { FactoryId = binding.Factory.Id, NativeId = binding.NativeId,
                    Quantity = (ushort)quantity, Revision = binding.Publication.Observe((ushort)quantity),
                    Position = binding.Body.transform.position.ToNet(), Rotation = binding.Body.transform.rotation.ToNet() };
            }
            catch (Exception e) { FailPackageFactory(binding.Factory, e); return null; }
        }

        internal IEnumerable<PackageState> BuildPackageStates()
        {
            foreach (uint id in _packages.Keys)
            {
                var message = BuildPackageState(id);
                if (message != null) yield return message;
            }
        }

        private void ProcessPackages(SessionManager session)
        {
            ProcessPackageOutputs(session);
            if (Time.unscaledTime < _nextPackagePoll) return;
            _nextPackagePoll = Time.unscaledTime + .2f;
            if (session.IsHost)
            {
                foreach (var state in BuildPackageStates())
                {
                    var binding = _packages[FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId)];
                    if (!binding.Publication.NeedsBroadcast) continue;
                    session.SendWorldMessage(state, Channel.ReliableOrdered);
                    binding.Publication.MarkBroadcast(state.Revision);
                }
                return;
            }
            if (_packageReplica == null) return;
            var done = new List<uint>();
            foreach (uint id in _pendingPackageStates)
            {
                var state = _packageReplica.Get(id);
                if (state == null) { done.Add(id); continue; }
                if (!_packageFactories.TryGetValue(state.FactoryId, out var factory) || factory.Failed
                    || !factory.Suppressor.Active) continue;
                try
                {
                    if (_packages.TryGetValue(id, out var binding) && binding.Body != null)
                    {
                        if (!binding.Replica) throw new InvalidOperationException("Host package overlaps a local save object.");
                        ApplyPackageQuantity(binding, state.Quantity);
                    }
                    else MaterializePackage(factory, id, state);
                    done.Add(id);
                }
                catch (Exception e) { FailPackageFactory(factory, e); }
            }
            foreach (uint id in done) _pendingPackageStates.Remove(id);
        }

        private void MaterializePackage(PackageFactory factory, uint id, PackageState state)
        {
            if (_items.TryGetValue(id, out var existing))
            {
                if (existing.Body != null) throw new InvalidOperationException("Package item ID collision.");
                RemoveTrackedItem(id, existing.Body);
            }
            GameObject clone;
            bool enabled = factory.TemplateUse.enabled;
            try
            {
                // Disable on the asset before the synchronous copy: Awake/OnEnable
                // must not read a guest's saved quantity or transform for this ID.
                factory.TemplateUse.enabled = false;
                clone = (GameObject)UnityEngine.Object.Instantiate(factory.Prefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { factory.TemplateUse.enabled = enabled; }
            try
            {
                var c = SyncCatalog.PartsPackages!;
                var body = clone.GetComponent<Rigidbody>();
                var use = clone.GetComponent<PlayMakerFSM>();
                use.enabled = false;
                if (!use.Fsm.Initialized) use.Fsm.Init(use);
                var garbage = ValidatePackageGarbage(use);
                use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value = state.NativeId;
                use.FsmVariables.FindFsmGameObject(c["ownerVariable"]).Value = clone;
                use.FsmVariables.FindFsmGameObject(c["contentsVariable"]).Value = factory.Contents;
                use.FsmVariables.FindFsmInt(c["capacityVariable"]).Value = factory.Rule.Capacity;
                use.FsmVariables.FindFsmInt(c["quantityVariable"]).Value = state.Quantity;
                if (!FsmHook.EnsureRemoteEntry(use, c["itemIdleState"]) || !FsmHook.EnsureRemoteEntry(use, c["emptyState"]))
                    throw new InvalidOperationException("Cannot initialize package interaction.");
                PrepareGuestPackageOpening(use, id, c);
                // Only the host's native opening changes quantity or creates a part.
                foreach (string key in new[] { "saveState", "deleteState" })
                {
                    FsmHook.FindState(use, c[key])!.Actions = new FsmStateAction[] { new FsmHookAction(() => ReturnPackageToIdle(use, c)) };
                }
                var hook = new FsmHookAction(() => { AnnounceItemDespawn(id, "package garbage"); RetirePackage(use); });
                garbage.Actions = new FsmStateAction[] { hook };
                clone.name = state.Quantity == 0 ? c["emptyName"] : c["itemName"];
                clone.transform.localScale = Vector3.one;
                body.isKinematic = false;
                var item = new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform), LastPosition = body.position };
                _items.Add(id, item); _trackedBodies[body] = true;
                ApplySnapshotPose(item, state.Position.ToUnity(), state.Rotation.ToUnity());
                // Join/resync sends PackageState after item poses, with a fresh
                // creation pose; do not revive the earlier parked position.
                _pendingItemPoses.Remove(id);
                _packages[id] = new PackageBinding { Body = body, Use = use, Factory = factory,
                    NativeId = state.NativeId, Replica = true, GarbageState = garbage, Hook = hook };
                use.Fsm.StartState = state.Quantity == 0 ? c["emptyState"] : c["itemIdleState"];
                use.Fsm.RestartOnEnable = true;
                use.enabled = true;
                clone.SetActive(true);
                SyncEventLog.Record("package-replica", state.NativeId + " " + id.ToString("X8"));
            }
            catch
            {
                var use = clone.GetComponent<PlayMakerFSM>(); if (use != null) use.enabled = false;
                var body = clone.GetComponent<Rigidbody>(); RemoveTrackedItem(id, body); _packages.Remove(id);
                UnityEngine.Object.Destroy(clone); throw;
            }
        }

        private static void ApplyPackageQuantity(PackageBinding binding, ushort quantity)
        {
            var c = SyncCatalog.PartsPackages!;
            var value = binding.Use.FsmVariables.FindFsmInt(c["quantityVariable"]);
            if (value.Value == quantity) return;
            value.Value = quantity;
            binding.Body.gameObject.name = quantity == 0 ? c["emptyName"] : c["itemName"];
            FsmHook.FireRemoteEntry(binding.Use, quantity == 0 ? c["emptyState"] : c["itemIdleState"]);
        }

        private bool DestroyPackageReplica(PlayMakerFSM use)
        {
            foreach (var pair in _packages)
            {
                var binding = pair.Value;
                if (!binding.Replica || binding.Use != use) continue;
                RemoveTrackedItem(pair.Key, binding.Body);
                use.enabled = false; use.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(use.gameObject);
                return true;
            }
            return false;
        }
    }
}
