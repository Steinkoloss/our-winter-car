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
        private readonly Dictionary<uint, BagState> _bagStates = new Dictionary<uint, BagState>();
        private readonly HashSet<uint> _pendingBagStates = new HashSet<uint>();
        private BagOpenRequest? _pendingBagRequest;
        private float _nextBagRequest, _nextBagNotice;

        internal void OnBagState(BagState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !BagStatePolicy.IsValidState(state) || _spawnLifecycle.IsRetired(state.ItemId)) return;
            if (_bagStates.TryGetValue(state.ItemId, out var previous))
            {
                if (state.FactoryId != previous.FactoryId || state.NativeId != previous.NativeId) return;
                if (unchecked(state.Revision - previous.Revision) > int.MaxValue) return;
                if (state.Revision == previous.Revision && (state.Remaining != previous.Remaining
                    || state.Condition != previous.Condition)) return;
            }
            _bagStates[state.ItemId] = BagStatePolicy.Copy(state);
            _snapshotSeenIds.Add(state.ItemId);
            _pendingBagStates.Add(state.ItemId);
        }

        private void ProcessBagReplicas(SessionManager session)
        {
            var c = SyncCatalog.ShoppingBags;
            if (c == null) return;
            var done = new List<uint>();
            foreach (uint id in _pendingBagStates)
            {
                if (_spawnLifecycle.IsRetired(id)) { done.Add(id); continue; }
                var state = _bagStates[id];
                if (!_bagFactories.TryGetValue(state.FactoryId, out var factory) || factory.Failed
                    || !FactoryItemIdentity.IsNativeId(state.NativeId, factory.Prefix)) continue;
                try
                {
                    if (state.Remaining == 0)
                    {
                        // A zero state may precede the ordered despawn frame.
                        OnRemoteItemDespawn(new ItemDespawn { ItemId = id }); done.Add(id); continue;
                    }
                    if (_bags.TryGetValue(id, out var bag) && bag.Body != null)
                    {
                        if (!bag.Replica) throw new InvalidOperationException("Host bag overlaps a guest save bag.");
                        bag.Use.FsmVariables.FindFsmFloat(c["conditionVariable"]).Value = state.Condition;
                    }
                    else MaterializeBag(factory, state);
                    done.Add(id);
                }
                catch (Exception e)
                {
                    if (Time.unscaledTime >= _nextBagNotice)
                    {
                        _nextBagNotice = Time.unscaledTime + 10;
                        WinterMPPlugin.Log.LogWarning("WorldSync: waiting to create shopping bag: " + e.Message);
                    }
                }
            }
            foreach (uint id in done) _pendingBagStates.Remove(id);
            if (_pendingBagRequest != null && Time.unscaledTime >= _nextBagRequest)
            {
                _nextBagRequest = Time.unscaledTime + 1f;
                session.SendWorldMessage(_pendingBagRequest, Channel.ReliableOrdered);
            }
        }

        private void MaterializeBag(BagFactory factory, BagState state)
        {
            var c = SyncCatalog.ShoppingBags!;
            GameObject clone;
            bool enabled = factory.TemplateUse.enabled;
            try
            {
                // A replica must never load the guest's inventory for the host ID.
                factory.TemplateUse.enabled = false;
                clone = (GameObject)UnityEngine.Object.Instantiate(factory.Prefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { factory.TemplateUse.enabled = enabled; }
            try
            {
                var use = clone.GetComponent<PlayMakerFSM>(); var body = clone.GetComponent<Rigidbody>();
                use.enabled = false;
                if (!use.Fsm.Initialized) use.Fsm.Init(use);
                use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value = state.NativeId;
                use.FsmVariables.FindFsmGameObject(c["ownerVariable"]).Value = clone;
                use.FsmVariables.FindFsmGameObject(c["contentsVariable"]).Value = factory.Contents.gameObject;
                use.FsmVariables.FindFsmFloat(c["conditionVariable"]).Value = state.Condition;
                use.FsmVariables.FindFsmBool(c["consumedVariable"]).Value = false;
                // Keep native hold/release input and its opening text, with all
                // inventory, save and spoil mutations owned by the host.
                var confirm = FsmHook.FindState(use, c["confirmState"])!;
                var confirmActions = new List<FsmStateAction>();
                foreach (var action in confirm.Actions)
                    if (action.GetType().Name != "SetFsmGameObject") confirmActions.Add(action);
                confirm.Actions = confirmActions.ToArray();
                foreach (string key in new[] { "oneState", "allState", "saveState", "deleteState", "garbageState" })
                    FsmHook.FindState(use, c[key])!.Actions = new FsmStateAction[0];
                foreach (string name in new[] { "Spoil 2", "In Fridge" })
                {
                    var spoil = FsmHook.FindState(use, name);
                    if (spoil != null) spoil.Actions = new FsmStateAction[] { new FsmHookAction(() => ReturnBagToIdle(use)) };
                }
                foreach (string key in new[] { "saveState", "deleteState" })
                    FsmHook.FindState(use, c[key])!.Actions = new FsmStateAction[] { new FsmHookAction(() => ReturnBagToIdle(use)) };
                clone.name = c["itemName"]; clone.transform.localScale = Vector3.one; body.isKinematic = false;
                RegisterBag(new BagBinding { Id = state.ItemId, NativeId = state.NativeId, Body = body, Use = use, Factory = factory, Replica = true });
                ApplySnapshotPose(_items[state.ItemId], state.Position.ToUnity(), state.Rotation.ToUnity());
                _pendingItemPoses.Remove(state.ItemId);
                use.Fsm.StartState = c["itemIdleState"]; use.Fsm.RestartOnEnable = true;
                use.enabled = true; clone.SetActive(true);
                SyncEventLog.Record("bag-replica", state.NativeId + " " + state.ItemId.ToString("X8"));
            }
            catch
            {
                var body = clone.GetComponent<Rigidbody>(); RemoveTrackedItem(state.ItemId, body); _bags.Remove(state.ItemId);
                var use = clone.GetComponent<PlayMakerFSM>(); if (use != null) use.enabled = false;
                UnityEngine.Object.Destroy(clone); throw;
            }
        }

        private void QueueBagOpen(BagBinding bag, bool all)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !bag.Replica || _pendingBagRequest != null
                || !_bagStates.TryGetValue(bag.Id, out var state) || state.Remaining == 0 || _spawnLifecycle.IsRetired(bag.Id)) return;
            _pendingBagRequest = new BagOpenRequest { PlayerId = session.LocalPlayerId, Sequence = ++_outBagSequence,
                ItemId = bag.Id, ExpectedRevision = state.Revision, OpenAll = all };
            _nextBagRequest = Time.unscaledTime + 1;
            session.SendWorldMessage(_pendingBagRequest, Channel.ReliableOrdered);
        }

        internal void OnBagOpenReceipt(BagOpenReceipt receipt)
        {
            var request = _pendingBagRequest; var session = SessionManager.Instance;
            if (session == null || session.IsHost || request == null || receipt.PlayerId != session.LocalPlayerId
                || receipt.Sequence != request.Sequence || receipt.ItemId != request.ItemId
                || receipt.ExpectedRevision != request.ExpectedRevision || receipt.OpenAll != request.OpenAll
                || !BagOpenLedger.Terminal(receipt.Status)) return;
            _pendingBagRequest = null;
            if (receipt.Status == BagOpenStatus.Applied) return;
            if (Time.unscaledTime >= _nextBagNotice)
            {
                _nextBagNotice = Time.unscaledTime + 3;
                session.AddSystemChat(receipt.Status == BagOpenStatus.Stale ? "The bag changed. Try opening it again."
                    : receipt.Status == BagOpenStatus.NotOwner ? "The other player is holding this bag."
                    : "The bag could not be opened yet. Try again in a moment.");
            }
        }
    }
}
