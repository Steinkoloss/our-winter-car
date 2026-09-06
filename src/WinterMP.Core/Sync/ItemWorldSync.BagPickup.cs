using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class BagPickupGate : FsmStateAction
        {
            private readonly ItemWorldSync _owner;
            private readonly FsmStateAction[] _native;
            private readonly bool[] _enabled;
            public BagPickupGate(ItemWorldSync owner, FsmStateAction[] native)
            {
                _owner = owner; _native = native; _enabled = new bool[native.Length];
                for (int i = 0; i < native.Length; i++) _enabled[i] = native[i].Enabled;
            }
            public override void OnEnter()
            {
                bool blocked = false;
                try { blocked = _owner.ShouldBlockBagPickup(); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: bag pickup check failed: " + e.Message); }
                for (int i = 0; i < _native.Length; i++) _native[i].Enabled = !blocked && _enabled[i];
                if (!blocked) { Finish(); return; }
                // Retain disabled actions through the synchronous state change.
                // Restoring them on exit could let the old entry loop attach the bag.
                try { _owner.CancelBagPickup(); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: bag pickup cancellation failed: " + e.Message); }
            }
            public void Restore()
            {
                Enabled = false;
                for (int i = 0; i < _native.Length; i++) _native[i].Enabled = _enabled[i];
            }
        }

        private PlayMakerFSM? _bagPickup;
        private FsmGameObject? _bagPickedObject, _bagPickupPivot;
        private FsmObject? _bagPickupJoint;
        private FsmBool? _bagHandEmpty;
        private FsmState? _bagPickupEntry;
        private BagPickupGate? _bagPickupGate;
        private float _nextBagPickupDiscovery;
        private bool _bagPickupWarned;

        private void UpdateBagPickup(SessionManager session)
        {
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected) return;
            EnsureBagPickup();
            if (_bagPickup == null || _bagPickedObject == null) return;
            var body = FindPickedBagBody(_bagPickedObject.Value);
            if (body != null && ShouldBlockBagPickup()) ReleaseHeldBag(body);
        }

        private void EnsureBagPickup()
        {
            if (_bagPickup != null && _bagPickupGate != null) return;
            if (Time.unscaledTime < _nextBagPickupDiscovery) return;
            _nextBagPickupDiscovery = Time.unscaledTime + 1f;
            var c = SyncCatalog.ShoppingBags;
            if (c == null) return;
            _bridge.FindLocalPlayer();
            var player = _bridge.LocalPlayer;
            if (player == null) return;
            string path = c["pickupPath"], prefix = ScenePath.Of(player) + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return;
            var hand = ScenePath.FindRelative(player, path.Substring(prefix.Length));
            if (hand == null) return;
            PlayMakerFSM? pickup = null;
            foreach (var fsm in hand.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == c["pickupFsm"]) { if (pickup != null) return; pickup = fsm; }
            if (pickup == null || !pickup.Fsm.Initialized || !pickup.Fsm.Started) return;
            try
            {
                ClearBagPickup();
                var entry = ValidateBagPickup(pickup, c);
                if (!FsmHook.EnsureRemoteEntry(pickup, c["pickupIdleState"]))
                    throw new InvalidOperationException("Pickup idle state missing.");
                var gate = new BagPickupGate(this, entry.Actions);
                var actions = new List<FsmStateAction>(entry.Actions); actions.Insert(0, gate);
                _bagPickup = pickup; _bagPickupEntry = entry; _bagPickupGate = gate;
                _bagPickedObject = pickup.FsmVariables.FindFsmGameObject(c["pickupObjectVariable"]);
                _bagPickupPivot = pickup.FsmVariables.FindFsmGameObject(c["pickupPivotVariable"]);
                _bagPickupJoint = pickup.FsmVariables.FindFsmObject(c["pickupJointVariable"]);
                _bagHandEmpty = pickup.FsmVariables.FindFsmBool(c["pickupEmptyVariable"]);
                entry.Actions = actions.ToArray();
            }
            catch (Exception e)
            {
                ClearBagPickup();
                _nextBagPickupDiscovery = Time.unscaledTime + 5f;
                if (!_bagPickupWarned)
                {
                    _bagPickupWarned = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: shopping bag pickup guard unavailable: " + e.Message);
                }
            }
        }

        private static FsmState ValidateBagPickup(PlayMakerFSM pickup, ShoppingBagsData c)
        {
            var vars = pickup.FsmVariables;
            if (vars.FindFsmGameObject(c["pickupObjectVariable"]) == null || vars.FindFsmGameObject(c["pickupPivotVariable"]) == null
                || vars.FindFsmObject(c["pickupJointVariable"]) == null || vars.FindFsmBool(c["pickupEmptyVariable"]) == null)
                throw new InvalidOperationException("Pickup references changed.");
            var entry = PackageStateActions(pickup, c["pickupEntryState"], "SetBoolValue", "GetPosition", "SetPosition",
                "SetPosition", "SetLayer", "SetParent", "SetJointConnectedBody");
            var drop = PackageStateActions(pickup, c["pickupDropState"], "SetIsKinematic", "SetIsKinematic",
                "SetParent", "SetLayer", "SetJointConnectedBody", "SetVelocity");
            if (PackageField<FsmBool>(entry.Actions[0], "boolVariable")?.Name != c["pickupEmptyVariable"]
                || PackageField<FsmBool>(entry.Actions[0], "boolValue")?.Value != false
                || !BagPickupTarget(entry.Actions[1], "gameObject", c["pickupObjectVariable"])
                || !BagPickupTarget(entry.Actions[3], "gameObject", c["pickupPivotVariable"])
                || !BagPickupTarget(entry.Actions[4], "gameObject", c["pickupObjectVariable"])
                || !BagPickupTarget(entry.Actions[5], "gameObject", c["pickupObjectVariable"])
                || PackageField<FsmGameObject>(entry.Actions[5], "parent")?.Name != c["pickupPivotVariable"]
                || PackageField<FsmOwnerDefault>(entry.Actions[6], "joint")?.OwnerOption != OwnerDefaultOption.UseOwner
                || PackageField<FsmGameObject>(entry.Actions[6], "rigidBody")?.Name != c["pickupObjectVariable"])
                throw new InvalidOperationException("Pickup attachment targets changed.");
            foreach (int index in new[] { 1, 2, 3, 5 })
                if (!BagPickupTarget(drop.Actions[index], "gameObject", c["pickupObjectVariable"]))
                    throw new InvalidOperationException("Pickup release targets changed.");
            var parent = PackageField<FsmGameObject>(drop.Actions[2], "parent");
            var body = PackageField<FsmGameObject>(drop.Actions[4], "rigidBody");
            if (parent == null || !parent.IsNone || body == null || !body.IsNone
                || PackageField<FsmBool>(drop.Actions[1], "isKinematic")?.Value != false
                || PackageField<FsmOwnerDefault>(drop.Actions[4], "joint")?.OwnerOption != OwnerDefaultOption.UseOwner)
                throw new InvalidOperationException("Pickup release no longer clears its parent and joint.");
            bool dropEvent = false, heldTransition = false;
            foreach (var transition in pickup.Fsm.GlobalTransitions)
                if (transition.EventName == c["pickupDropEvent"] && transition.ToState == c["pickupDropState"]) dropEvent = true;
            foreach (var transition in entry.Transitions)
                if (transition.EventName == "FINISHED" && transition.ToState == c["pickupHeldState"]) heldTransition = true;
            if (!dropEvent || !heldTransition || !FsmHook.HasState(pickup, c["pickupIdleState"]))
                throw new InvalidOperationException("Pickup entry/release transitions changed.");
            return entry;
        }

        private static bool BagPickupTarget(FsmStateAction action, string field, string name)
        {
            var target = PackageField<FsmOwnerDefault>(action, field);
            return target != null && target.OwnerOption == OwnerDefaultOption.SpecifyGameObject
                && target.GameObject.UseVariable && target.GameObject.Name == name;
        }

        private static Rigidbody? FindPickedBagBody(GameObject? picked)
        {
            if (picked == null) return null;
            for (var target = picked.transform; target != null; target = target.parent)
            {
                var body = target.GetComponent<Rigidbody>();
                if (body != null && FindBagUse(body) != null) return body;
            }
            return null;
        }

        private bool ShouldBlockBagPickup()
        {
            var session = SessionManager.Instance;
            if (session == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected)) return false;
            var body = FindPickedBagBody(_bagPickedObject?.Value);
            if (body == null) return false;
            foreach (var bag in _bags.Values)
            {
                if (bag.Body != body) continue;
                return bag.Factory.Failed || _spawnLifecycle.IsRetired(bag.Id)
                    || !_items.TryGetValue(bag.Id, out var item) || item.Body != body
                    || (item.RemoteOwner != WorldSyncIds.NoOwner && item.RemoteOwner != session.LocalPlayerId);
            }
            return true;
        }

        private void CancelBagPickup()
        {
            var body = FindPickedBagBody(_bagPickedObject?.Value);
            if (body != null && IsHeldByLocalPlayer(body)) { ReleaseHeldBag(body); return; }
            if (_bagPickedObject != null) _bagPickedObject.Value = null;
            var c = SyncCatalog.ShoppingBags;
            if (c != null && _bagPickup != null) FsmHook.FireRemoteEntry(_bagPickup, c["pickupIdleState"]);
        }

        private bool IsHeldByLocalPlayer(Rigidbody body)
        {
            if (body == null) return false;
            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer != null && body.transform.IsChildOf(_bridge.LocalPlayer)) return true;
            EnsureBagPickup();
            if (_bagPickup == null || _bagHandEmpty == null || _bagHandEmpty.Value
                || FindPickedBagBody(_bagPickedObject?.Value) != body) return false;
            var joint = _bagPickupJoint?.Value as Joint;
            var pivot = _bagPickupPivot?.Value;
            return (joint != null && joint.connectedBody == body)
                || (pivot != null && body.transform.IsChildOf(pivot.transform));
        }

        private void ReleaseHeldBag(Rigidbody body)
        {
            if (body == null) return;
            EnsureBagPickup();
            var pickup = _bagPickup; var c = SyncCatalog.ShoppingBags;
            if (pickup == null || c == null || !pickup.enabled || !pickup.Fsm.Started
                || FindPickedBagBody(_bagPickedObject?.Value) != body || !IsHeldByLocalPlayer(body)) return;
            bool previous = _bridge.ApplyingRemote;
            try { _bridge.ApplyingRemote = true; pickup.SendEvent(c["pickupDropEvent"]); }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: bag release failed: " + e.Message); }
            finally { _bridge.ApplyingRemote = previous; }
        }

        private void ClearBagPickup()
        {
            _bagPickupGate?.Restore();
            if (_bagPickup != null && _bagPickupEntry != null && _bagPickupGate != null)
            {
                var actions = new List<FsmStateAction>(_bagPickupEntry.Actions);
                actions.Remove(_bagPickupGate); _bagPickupEntry.Actions = actions.ToArray();
            }
            _bagPickup = null; _bagPickupEntry = null; _bagPickupGate = null;
            _bagPickedObject = null; _bagPickupPivot = null; _bagPickupJoint = null; _bagHandEmpty = null;
        }
    }
}
