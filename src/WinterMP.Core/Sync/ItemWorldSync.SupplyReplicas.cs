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
        private SupplyItemReplica? _supplyReplica;
        private readonly HashSet<uint> _pendingSupplyStates = new HashSet<uint>();

        internal void OnSupplyState(SupplyItemState state)
        {
            var session = SessionManager.Instance; var c = SyncCatalog.PartsPackages;
            if (session == null || session.IsHost || c == null) return;
            if (_supplyReplica == null)
            {
                var rules = new Dictionary<uint, string>();
                foreach (var rule in c.Factories)
                    if (rule.SupplyContents != null)
                        rules.Add(FactoryItemIdentity.FactoryId(rule.ContentsPath, rule.ContentsFsm), rule.SupplyContents.Prefix);
                _supplyReplica = new SupplyItemReplica(rules, _spawnLifecycle);
            }
            if (_supplyReplica.Receive(state, out uint id)) _pendingSupplyStates.Add(id);
        }

        internal SupplyItemState? BuildSupplyState(uint id)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _spawnLifecycle.IsRetired(id)
                || !_supplies.TryGetValue(id, out var binding) || binding.Replica || binding.Factory.Failed
                || binding.Body == null || binding.Use == null || !_items.ContainsKey(id)
                || binding.Use.FsmVariables.FindFsmBool("Consumed").Value) return null;
            return new SupplyItemState { FactoryId = binding.Factory.Id, NativeId = binding.NativeId,
                Position = binding.Body.position.ToNet(), Rotation = binding.Body.rotation.ToNet() };
        }

        internal IEnumerable<SupplyItemState> BuildSupplyStates()
        {
            foreach (uint id in _supplies.Keys) { var state = BuildSupplyState(id); if (state != null) yield return state; }
        }

        private void ProcessSupplies(SessionManager session)
        {
            ProcessSupplyRetirements();
            var c = SyncCatalog.PartsPackages;
            if (c == null) return;
            foreach (var factory in _supplyFactories.Values)
            {
                try
                {
                    if (!session.IsHost && !factory.Failed && factory.Fsm != null && factory.Fsm.Fsm.Started
                        && !factory.Suppressor.Active && factory.Fsm.ActiveStateName == c["factoryIdleState"])
                        factory.Suppressor.Suppress(factory.Fsm);
                }
                catch (Exception e) { FailSupply(factory, e); }
            }
            if (_unreadySupplies.Count > 0)
            {
                var retry = new List<Rigidbody>(_unreadySupplies); _unreadySupplies.Clear();
                foreach (var body in retry) if (body != null && body.gameObject.activeInHierarchy) TryScanSupply(body);
            }
            if (session.IsHost)
            {
                foreach (var pair in _supplies)
                {
                    if (pair.Value.Published) continue;
                    var state = BuildSupplyState(pair.Key);
                    if (state == null) continue;
                    session.SendWorldMessage(state, Channel.ReliableOrdered); pair.Value.Published = true;
                }
                return;
            }
            var done = new List<uint>();
            foreach (uint id in _pendingSupplyStates)
            {
                var state = _supplyReplica?.Get(id);
                if (state == null) { done.Add(id); continue; }
                if (!_supplyFactories.TryGetValue(state.FactoryId, out var factory) || factory.Failed || !factory.Suppressor.Active) continue;
                try
                {
                    if (!_supplies.TryGetValue(id, out var binding) || binding.Body == null) MaterializeSupply(factory, id, state);
                    done.Add(id);
                }
                catch (Exception e) { FailSupply(factory, e); }
            }
            foreach (uint id in done) _pendingSupplyStates.Remove(id);
        }

        private void MaterializeSupply(SupplyFactory factory, uint id, SupplyItemState state)
        {
            if (_items.TryGetValue(id, out var old))
            {
                if (old.Body != null) throw new InvalidOperationException("Supply item ID collision.");
                RemoveTrackedItem(id, old.Body);
            }
            GameObject clone; bool enabled = factory.TemplateUse.enabled;
            try
            {
                factory.TemplateUse.enabled = false;
                clone = (GameObject)UnityEngine.Object.Instantiate(factory.Prefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { factory.TemplateUse.enabled = enabled; }
            try
            {
                var c = SyncCatalog.PartsPackages!; var rule = factory.Rule.SupplyContents!;
                var body = clone.GetComponent<Rigidbody>(); var use = clone.GetComponent<PlayMakerFSM>();
                use.enabled = false; if (!use.Fsm.Initialized) use.Fsm.Init(use);
                ValidateSupplyUse(use, rule);
                use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value = state.NativeId;
                use.FsmVariables.FindFsmGameObject(c["ownerVariable"]).Value = clone;
                use.FsmVariables.FindFsmBool("Consumed").Value = false;
                use.FsmVariables.FindFsmBool(rule.RetirementVariable).Value = false;
                if (!FsmHook.EnsureRemoteEntry(use, rule.ReadyState)) throw new InvalidOperationException("Cannot initialize supply replica.");
                foreach (string key in new[] { "saveState", "deleteState" })
                    FsmHook.FindState(use, c[key])!.Actions = new FsmStateAction[] { new FsmHookAction(() => FsmHook.FireRemoteEntry(use, rule.ReadyState)) };
                FsmHook.FindState(use, c["garbageState"])!.Actions = new FsmStateAction[] { new FsmHookAction(() =>
                { AnnounceItemDespawn(id, "supply garbage"); FsmHook.FireRemoteEntry(use, rule.ReadyState); }) };
                clone.name = rule.ItemName; clone.transform.localScale = Vector3.one;
                body.isKinematic = false;
                var item = new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform), LastPosition = body.position };
                _items.Add(id, item); _trackedBodies[body] = true;
                ApplySnapshotPose(item, state.Position.ToUnity(), state.Rotation.ToUnity()); _pendingItemPoses.Remove(id);
                _supplies[id] = new SupplyBinding { Body = body, Use = use, Factory = factory, NativeId = state.NativeId, Replica = true };
                use.Fsm.StartState = rule.ReadyState; use.Fsm.RestartOnEnable = true; use.enabled = true; clone.SetActive(true);
                if (!use.Fsm.Started) use.Fsm.Start();
                SyncEventLog.Record("supply-replica", state.NativeId + " " + id.ToString("X8"));
            }
            catch
            {
                var use = clone.GetComponent<PlayMakerFSM>(); if (use != null) use.enabled = false;
                RemoveTrackedItem(id, clone.GetComponent<Rigidbody>()); _supplies.Remove(id); UnityEngine.Object.Destroy(clone); throw;
            }
        }
    }
}
