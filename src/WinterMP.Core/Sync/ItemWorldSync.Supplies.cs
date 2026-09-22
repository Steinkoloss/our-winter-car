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
        private sealed class SupplyBinding
        {
            public Rigidbody Body = null!;
            public PlayMakerFSM Use = null!;
            public SupplyFactory Factory = null!;
            public string NativeId = string.Empty;
            public bool Replica, Published;
            public FsmState? Garbage;
            public FsmStateAction? Hook;
        }
        private readonly Dictionary<uint, SupplyBinding> _supplies = new Dictionary<uint, SupplyBinding>();
        private readonly Dictionary<PlayMakerFSM, HiddenPackage> _hiddenSupplies = new Dictionary<PlayMakerFSM, HiddenPackage>();
        private readonly HashSet<PlayMakerFSM> _pendingSupplyRetirements = new HashSet<PlayMakerFSM>();

        private static PackageFactoryData? SupplyRule(Rigidbody body, out PlayMakerFSM? use)
        {
            use = null;
            var c = SyncCatalog.PartsPackages;
            if (c == null || body == null) return null;
            foreach (var candidate in body.GetComponents<PlayMakerFSM>())
            {
                if (candidate.FsmName != c["itemFsm"]) continue;
                string? nativeId = candidate.FsmVariables.FindFsmString(c["itemIdVariable"])?.Value;
                foreach (var rule in c.Factories)
                {
                    var supply = rule.SupplyContents;
                    if (supply != null && (body.name == supply.ItemName
                        || FactoryItemIdentity.IsNativeId(body.name, supply.Prefix)
                        || FactoryItemIdentity.IsNativeId(nativeId ?? string.Empty, supply.Prefix)))
                    { use = candidate; return rule; }
                }
            }
            return null;
        }

        private bool TryScanSupply(Rigidbody body)
        {
            var rule = SupplyRule(body, out var use);
            if (rule == null || use == null) return false;
            foreach (var binding in _supplies.Values) if (binding.Body == body) return true;
            uint factoryId = FactoryItemIdentity.FactoryId(rule.ContentsPath, rule.ContentsFsm);
            if (!_supplyFactories.TryGetValue(factoryId, out var factory) || factory.Failed) return true;
            if (!use.Fsm.Initialized || !use.Fsm.Started || !use.enabled || use.ActiveStateName != rule.SupplyContents!.ReadyState)
            { _unreadySupplies.Add(body); return true; }
            _unreadySupplies.Remove(body);
            try
            {
                var session = SessionManager.Instance; var c = SyncCatalog.PartsPackages!;
                if (session == null) return true;
                if (!session.IsHost) { HideLocalSupply(use); return true; }
                string nativeId = use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value;
                if (!FactoryItemIdentity.IsNativeId(nativeId, rule.SupplyContents.Prefix))
                    throw new InvalidOperationException("Supply initialized with an invalid native ID.");
                uint id = FactoryItemIdentity.ItemId(factoryId, nativeId);
                if (_spawnLifecycle.IsRetired(id)) { _pendingSupplyRetirements.Add(use); return true; }
                if (_items.TryGetValue(id, out var existing) && existing.Body != null && existing.Body != body)
                    throw new InvalidOperationException("Duplicate supply identity: " + nativeId);
                if (_trackedBodies.ContainsKey(body) && (existing == null || existing.Body != body))
                    throw new InvalidOperationException("Supply already has a transient identity.");
                ValidateSupplyUse(use, rule.SupplyContents);
                var garbage = FsmHook.FindState(use, c["garbageState"])!;
                var hook = new FsmHookAction(() => AnnounceItemDespawn(id, "supply garbage"));
                var actions = new List<FsmStateAction>(garbage.Actions); actions.Insert(0, hook); garbage.Actions = actions.ToArray();
                _supplies[id] = new SupplyBinding { Body = body, Use = use, Factory = factory, NativeId = nativeId,
                    Garbage = garbage, Hook = hook };
                _items[id] = new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform), LastPosition = body.position };
                _trackedBodies[body] = true;
                SyncEventLog.Record("supply-bind", nativeId + " " + id.ToString("X8"));
            }
            catch (Exception e) { FailSupply(factory, e); }
            return true;
        }

        private void HideLocalSupply(PlayMakerFSM use)
        {
            if (_hiddenSupplies.ContainsKey(use)) return;
            var body = use.GetComponent<Rigidbody>();
            if (body == null) return;
            var hidden = new HiddenPackage { Body = body, Active = use.gameObject.activeSelf, Kinematic = body.isKinematic };
            if (!hidden.Suppressor.Suppress(use)) throw new InvalidOperationException("Cannot preserve guest supply.");
            _hiddenSupplies.Add(use, hidden); use.gameObject.SetActive(false);
        }

        private bool TryRetireSupply(Rigidbody body)
        {
            var rule = SupplyRule(body, out var use);
            if (rule == null || use == null) return false;
            if (SessionManager.Instance != null && !SessionManager.Instance.IsHost)
            {
                foreach (var binding in _supplies.Values)
                    if (binding.Replica && binding.Use == use)
                    { use.enabled = false; UnityEngine.Object.Destroy(use.gameObject); return true; }
                HideLocalSupply(use);
            }
            else _pendingSupplyRetirements.Add(use);
            return true;
        }

        private void ProcessSupplyRetirements()
        {
            var done = new List<PlayMakerFSM>();
            foreach (var use in _pendingSupplyRetirements)
            {
                if (!use) { done.Add(use); continue; }
                if (!use.Fsm.Started || !use.enabled) continue;
                bool applying = _bridge.ApplyingRemote; _bridge.ApplyingRemote = true;
                try { use.SendEvent(SyncCatalog.PartsPackages!["garbageEvent"]); done.Add(use); }
                finally { _bridge.ApplyingRemote = applying; }
            }
            foreach (var use in done) _pendingSupplyRetirements.Remove(use);
        }

        private void ClearSupplies()
        {
            foreach (var pair in _supplies)
            {
                var binding = pair.Value;
                if (binding.Use != null)
                {
                    if (binding.Replica) { binding.Use.enabled = false; UnityEngine.Object.Destroy(binding.Use.gameObject); }
                    else if (binding.Garbage != null && binding.Hook != null) RemoveReplacementHook(binding.Garbage, binding.Hook);
                }
                if (_items.TryGetValue(pair.Key, out var item) && item.Body == binding.Body) RemoveTrackedItem(pair.Key, binding.Body);
            }
            foreach (var pair in _hiddenSupplies)
            {
                if (pair.Key == null) continue;
                if (pair.Value.Body != null) pair.Value.Body.isKinematic = pair.Value.Kinematic;
                pair.Key.gameObject.SetActive(pair.Value.Active); pair.Value.Suppressor.Restore();
            }
            foreach (var factory in _supplyFactories.Values)
            {
                if (factory.Fsm != null)
                {
                    if (factory.Create != null && factory.CreateHook != null) RemoveReplacementHook(factory.Create, factory.CreateHook);
                    if (factory.Load != null && factory.LoadHook != null) RemoveReplacementHook(factory.Load, factory.LoadHook);
                }
                factory.Suppressor.Restore();
            }
            _supplies.Clear(); _hiddenSupplies.Clear(); _supplyFactories.Clear(); _unreadySupplies.Clear();
            _pendingSupplyRetirements.Clear(); _supplyReplica = null; _pendingSupplyStates.Clear();
        }
    }
}
