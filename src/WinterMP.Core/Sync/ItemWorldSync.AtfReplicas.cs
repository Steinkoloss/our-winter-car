using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal void OnAtfState(AtfBottleState state)
        {
            var c = SyncCatalog.AtfRefill;
            if (SessionManager.Instance?.IsHost != false || c == null || !AtfPolicy.Valid(state)
                || !FactoryItemIdentity.IsNativeId(state.NativeId, c["prefix"]) || _spawnLifecycle.IsRetired(state.ItemId)) return;
            AtfBottleState? old = _pendingAtf.TryGetValue(state.ItemId, out var pending) ? pending
                : (_receivedAtf.TryGetValue(state.ItemId, out var received) ? received : null);
            if (!AtfPolicy.CanReceive(old, state) || (!_pendingAtf.ContainsKey(state.ItemId) && _pendingAtf.Count >= 1024)) return;
            foreach (var pair in _receivedAtf)
                if (pair.Key != state.ItemId && pair.Value.NativeId == state.NativeId) return;
            _snapshotSeenIds.Add(state.ItemId);
            _pendingAtf[state.ItemId] = AtfPolicy.Copy(state);
            _receivedAtf[state.ItemId] = AtfPolicy.Copy(state);
        }

        private void ApplyPendingAtf()
        {
            if (_atfPrefab == null) return;
            foreach (uint id in new List<uint>(_pendingAtf.Keys))
            {
                if (_spawnLifecycle.IsRetired(id)) { _pendingAtf.Remove(id); continue; }
                if (_atfReplicaErrors.TryGetValue(id, out float next) && Time.unscaledTime < next) continue;
                try
                {
                    var state = _pendingAtf[id];
                    if (_atf.TryGetValue(id, out var binding) && binding.Body != null)
                    {
                        if (!binding.Replica || binding.NativeId != state.NativeId)
                            throw new InvalidOperationException("ATF receipt overlaps a native save object.");
                        binding.Apply(state); binding.Received = AtfPolicy.Copy(state);
                    }
                    else MaterializeAtf(state);
                    _pendingAtf.Remove(id); _atfReplicaErrors.Remove(id);
                }
                catch (Exception e)
                {
                    _atfReplicaErrors[id] = Time.unscaledTime + 5;
                    WinterMPPlugin.Log.LogWarning("ATF replica " + id.ToString("X8") + " waiting: " + e.Message);
                }
            }
        }

        private void MaterializeAtf(AtfBottleState state)
        {
            var c = SyncCatalog.AtfRefill!;
            if (_atfPrefab == null) throw new InvalidOperationException("ATF template is not ready.");
            uint id = state.ItemId;
            if (_items.TryGetValue(id, out var existing))
            {
                if (existing.Body != null)
                {
                    if (!IsNativeAtf(existing.Body)) throw new InvalidOperationException("ATF item ID collision.");
                    HideLocalAtf(existing.Body);
                }
                else RemoveTrackedItem(id, existing.Body);
            }
            if (_atf.TryGetValue(id, out var previous))
            {
                if (!previous.Replica) throw new InvalidOperationException("ATF creation overlaps a native bottle.");
                previous.Restore();
                if (previous.Use != null) UnityEngine.Object.Destroy(previous.Use.gameObject);
                _atf.Remove(id);
            }
            var templates = _atfPrefab.GetComponentsInChildren<PlayMakerFSM>(true);
            var enabled = new bool[templates.Length];
            GameObject clone;
            try
            {
                // Use has no startup wait. Disable both prefab FSMs before cloning
                // so this guest can never load its own bottle's save into a replica.
                for (int i = 0; i < templates.Length; i++) { enabled[i] = templates[i].enabled; templates[i].enabled = false; }
                clone = (GameObject)UnityEngine.Object.Instantiate(_atfPrefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { for (int i = 0; i < templates.Length; i++) templates[i].enabled = enabled[i]; }
            try
            {
                clone.name = c["itemName"]; clone.transform.localScale = Vector3.one;
                PlayMakerFSM? use = null;
                foreach (var fsm in clone.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    fsm.enabled = false;
                    if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                    if (fsm.gameObject == clone && fsm.FsmName == c["use"]) use = fsm;
                }
                if (use == null) throw new InvalidOperationException("ATF replica root Use missing.");
                use.FsmVariables.FindFsmString(c["id"]).Value = "wintermp-atf-" + id.ToString("X8");
                use.FsmVariables.FindFsmGameObject(c["owner"]).Value = clone;
                var body = clone.GetComponent<Rigidbody>();
                if (body == null) throw new InvalidOperationException("ATF replica body missing.");
                body.isKinematic = false;
                var binding = new AtfBottleBinding(body, use, state.NativeId, id, true, c);
                // Disabled FSMs can receive global events. Clear only the replica's
                // global transitions after validating the native persistence shape.
                foreach (var fsm in clone.GetComponentsInChildren<PlayMakerFSM>(true))
                    fsm.Fsm.GlobalTransitions = new FsmTransition[0];
                _atf[id] = binding; _trackedBodies[body] = true;
                BindSpawnedBody(body, new ItemSpawn.Entry { NetId = id, TemplateName = c["itemName"],
                    Position = state.Position, Rotation = state.Rotation }, 0, Time.unscaledTime);
                if (_pendingItemPoses.TryGetValue(id, out var pose))
                {
                    _pendingItemPoses.Remove(id);
                    if (Time.unscaledTime < pose.ExpiresAt) ApplySnapshotPose(_items[id], pose.Position, pose.Rotation);
                }
                binding.Apply(state); binding.Received = AtfPolicy.Copy(state);
                clone.SetActive(true);
                SyncEventLog.Record("atf-bottle-replica", state.NativeId + " " + id.ToString("X8"));
            }
            catch
            {
                foreach (var fsm in clone.GetComponentsInChildren<PlayMakerFSM>(true)) fsm.enabled = false;
                RemoveTrackedItem(id, clone.GetComponent<Rigidbody>()); _atf.Remove(id);
                UnityEngine.Object.Destroy(clone); throw;
            }
        }
    }
}
