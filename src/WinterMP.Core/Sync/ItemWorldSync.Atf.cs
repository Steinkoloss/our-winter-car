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
        private sealed class LocalAtf
        {
            internal bool Active;
            internal readonly List<FsmSuppressor> Pauses = new List<FsmSuppressor>();
        }
        private readonly Dictionary<uint, AtfBottleBinding> _atf = new Dictionary<uint, AtfBottleBinding>();
        private readonly Dictionary<uint, AtfBottleState> _pendingAtf = new Dictionary<uint, AtfBottleState>();
        private readonly Dictionary<uint, AtfBottleState> _receivedAtf = new Dictionary<uint, AtfBottleState>();
        private readonly Dictionary<GameObject, LocalAtf> _localAtf = new Dictionary<GameObject, LocalAtf>();
        private readonly HashSet<Rigidbody> _failedAtf = new HashSet<Rigidbody>();
        private readonly Dictionary<uint, float> _atfReplicaErrors = new Dictionary<uint, float>();
        private GameObject? _atfPrefab;
        private float _nextAtfPrefab, _nextAtfSend;

        internal IEnumerable<AtfBottleBinding> AtfBottles => _atf.Values;
        internal bool TryGetAtf(uint id, out AtfBottleBinding binding)
        {
            if (_atf.TryGetValue(id, out var found) && found.Body != null && !_failedAtf.Contains(found.Body) && !_spawnLifecycle.IsRetired(id))
            { binding = found; return true; }
            binding = null!; return false;
        }
        internal bool IsAtfHeldLocally(uint id)
        {
            if (!TryGetAtf(id, out var binding)) return false;
            if (IsHeldByLocalPlayer(binding.Body)) return true;
            EnsureBagPickup();
            if (_bagPickup == null || _bagHandEmpty == null || _bagHandEmpty.Value || _bagPickedObject?.Value == null) return false;
            bool picked = false;
            for (var target = _bagPickedObject.Value.transform; target != null; target = target.parent)
                if (target == binding.Body.transform) { picked = true; break; }
            if (!picked) return false;
            var joint = _bagPickupJoint?.Value as Joint;
            var pivot = _bagPickupPivot?.Value;
            return (joint != null && joint.connectedBody == binding.Body)
                || (pivot != null && binding.Body.transform.IsChildOf(pivot.transform));
        }
        private void ReleaseHeldAtf(uint id)
        {
            var c = SyncCatalog.ShoppingBags;
            if (c == null || !IsAtfHeldLocally(id) || _bagPickup == null || !_bagPickup.enabled || !_bagPickup.Fsm.Started) return;
            bool old = _bridge.ApplyingRemote;
            try { _bridge.ApplyingRemote = true; _bagPickup.SendEvent(c["pickupDropEvent"]); }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("ATF hand release failed: " + e.Message); }
            finally { _bridge.ApplyingRemote = old; }
        }

        private static PlayMakerFSM? AtfUse(Rigidbody body)
        {
            var c = SyncCatalog.AtfRefill;
            if (body == null || c == null) return null;
            foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == c["use"]) return fsm;
            return null;
        }
        private static bool IsNativeAtf(Rigidbody body)
        {
            var c = SyncCatalog.AtfRefill; var use = AtfUse(body);
            return c != null && use != null && use.Fsm.Initialized
                && FactoryItemIdentity.IsNativeId(use.FsmVariables.FindFsmString(c["id"])?.Value ?? "", c["prefix"]);
        }
        private bool IsAtfManifest(ItemSpawn.Entry entry)
        {
            var c = SyncCatalog.AtfRefill;
            return c != null && (entry.TemplateName == c["itemName"] || _atf.ContainsKey(entry.NetId) || _pendingAtf.ContainsKey(entry.NetId));
        }

        private void RefreshAtfPrefab()
        {
            var c = SyncCatalog.AtfRefill;
            if (c == null || _atfPrefab != null || Time.unscaledTime < _nextAtfPrefab) return;
            _nextAtfPrefab = Time.unscaledTime + 2;
            try
            {
                var template = FindBagSpillTemplate(c["itemName"]);
                if (template == null) return;
                if (template.name != c["prefabName"] || !IsAtfPrefabShape(template.gameObject, c))
                    throw new InvalidOperationException("ATF native prefab shape changed.");
                _atfPrefab = template.gameObject;
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("ATF prefab waiting: " + e.Message); }
        }

        private static bool IsAtfPrefabShape(GameObject prefab, AtfRefillData c)
        {
            var fsms = prefab.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms.Length != 2 || prefab.GetComponent<Rigidbody>() == null) return false;
            bool root = false, source = false;
            foreach (var fsm in fsms)
            {
                if (fsm.gameObject == prefab && fsm.FsmName == c["use"]) root = true;
                else if (fsm.transform.parent == prefab.transform && fsm.name == c["trigger"] && fsm.FsmName == c["data"]) source = true;
                else return false;
            }
            return root && source;
        }

        private bool TryScanAtf(Rigidbody body)
        {
            var c = SyncCatalog.AtfRefill; var session = SessionManager.Instance;
            if (c == null || session == null) return false;
            foreach (var binding in _atf.Values) if (binding.Body == body) return true;
            if (_failedAtf.Contains(body) || _localAtf.ContainsKey(body.gameObject)) return true;
            string name = body.name;
            if (name != c["itemName"] && name != c["emptyName"] && name != c["prefabName"] + "(Clone)"
                && !FactoryItemIdentity.IsNativeId(name, c["prefix"])) return false;
            var use = AtfUse(body);
            string nativeId = use != null && use.Fsm.Initialized ? use.FsmVariables.FindFsmString(c["id"])?.Value ?? "" : "";
            bool identity = FactoryItemIdentity.IsNativeId(nativeId, c["prefix"]);
            if (!identity && name != c["itemName"] && name != c["prefabName"] + "(Clone)"
                && !FactoryItemIdentity.IsNativeId(name, c["prefix"])) return false;
            // Captured bag outputs already have a pending mint. Never allocate a
            // second saved-item identity before FinalizeHostSpawn assigns theirs.
            if (use == null || !use.Fsm.Started || !identity) return true;
            if (use.ActiveStateName != c["copyToChild"] && use.ActiveStateName != c["empty"]
                && use.ActiveStateName != c["save"] && use.ActiveStateName != c["emptyCheck"]) return true;
            try
            {
                if (!session.IsHost)
                {
                    HideLocalAtf(body); return true;
                }
                uint itemId = 0;
                foreach (var item in _items.Values) if (item.Body == body) { itemId = item.Id; break; }
                if (itemId == 0 && _bagSpillBodies.ContainsKey(body)) return true;
                if (itemId == 0) itemId = FactoryItemIdentity.ItemId(c.FactoryId, nativeId);
                if (_spawnLifecycle.IsRetired(itemId)) return true;
                foreach (var other in _atf.Values)
                    if (other.NativeId == nativeId && other.Body != null && other.Body != body)
                        throw new InvalidOperationException("ATF native identity collision.");
                if (_atf.TryGetValue(itemId, out var stale))
                {
                    if (stale.Body != null) throw new InvalidOperationException("ATF item identity collision.");
                    ForgetAtf(itemId, true);
                }
                if (_items.TryGetValue(itemId, out var tracked) && tracked.Body != body)
                {
                    if (tracked.Body != null) throw new InvalidOperationException("ATF item identity collision.");
                    RemoveTrackedItem(itemId, tracked.Body);
                }
                var source = body.transform.Find(c["trigger"]);
                if (source != null)
                {
                    PlayMakerFSM? data = null;
                    foreach (var fsm in source.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == c["data"]) data = fsm;
                    if (data != null && (!data.Fsm.Initialized || !data.Fsm.Started)) return true;
                }
                var binding = new AtfBottleBinding(body, use, nativeId, itemId, false, c);
                _atf.Add(itemId, binding);
                BindFactoryBodyAsOwner(body, itemId, Time.unscaledTime); _trackedBodies[body] = true;
                binding.TickPresentation();
                SyncEventLog.Record("atf-bottle-bind", nativeId + " " + itemId.ToString("X8"));
            }
            catch (Exception e) { FailAtfBody(body, e); }
            return true;
        }

        private void HideLocalAtf(Rigidbody body)
        {
            if (_localAtf.ContainsKey(body.gameObject)) return;
            var local = new LocalAtf { Active = body.gameObject.activeSelf };
            _localAtf.Add(body.gameObject, local);
            foreach (var fsm in body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                var pause = new FsmSuppressor(); local.Pauses.Add(pause);
                if (!pause.Suppress(fsm)) throw new InvalidOperationException("Could not pause guest saved ATF.");
            }
            foreach (var pair in new List<KeyValuePair<uint, SyncedItem>>(_items))
                if (pair.Value.Body == body) RemoveTrackedItem(pair.Key, body);
            _trackedBodies[body] = true; body.gameObject.SetActive(false);
        }

        internal AtfBottleState? BuildAtfState(uint id)
        {
            if (SessionManager.Instance?.IsHost != true || !TryGetAtf(id, out var binding) || binding.Replica
                || !_items.ContainsKey(id) || _failedAtf.Contains(binding.Body)) return null;
            try
            {
                var old = binding.Observed;
                var state = new AtfBottleState { ItemId = id, Revision = 1, NativeId = binding.NativeId,
                    Fluid = binding.Fluid, Empty = binding.Empty,
                    Position = binding.Body.position.ToNet(), Rotation = binding.Body.rotation.ToNet() };
                if (!AtfPolicy.Valid(state)) throw new InvalidOperationException("Invalid native ATF bottle state.");
                if (old != null)
                {
                    state.Revision = old.Revision;
                    if (!AtfPolicy.Same(old, state)) { state.Revision = unchecked(old.Revision + 1); if (state.Revision == 0) state.Revision = 1; }
                }
                binding.Observed = state;
                // Revision describes fluid/identity; snapshots use the current pose
                // only when first creating a replica. Existing motion is separate.
                return AtfPolicy.Copy(state);
            }
            catch (Exception e) { FailAtfBody(binding.Body, e); return null; }
        }

        internal IEnumerable<AtfBottleState> BuildAtfStates()
        {
            foreach (uint id in new List<uint>(_atf.Keys))
            { var state = BuildAtfState(id); if (state != null) yield return state; }
        }

        internal void UpdateAtf(SessionManager session)
        {
            if (SyncCatalog.AtfRefill == null) return;
            RefreshAtfPrefab();
            foreach (var pair in new List<KeyValuePair<uint, AtfBottleBinding>>(_atf))
            {
                var binding = pair.Value;
                if (binding.Body == null || binding.Use == null || _spawnLifecycle.IsRetired(pair.Key))
                {
                    var repair = !_spawnLifecycle.IsRetired(pair.Key) && _pendingAtf.TryGetValue(pair.Key, out var pending)
                        ? AtfPolicy.Copy(pending) : null;
                    ForgetAtf(pair.Key, true);
                    if (repair != null) _pendingAtf[pair.Key] = repair;
                    continue;
                }
                if (_failedAtf.Contains(binding.Body)) continue;
                try { binding.TickPresentation(); }
                catch (Exception e) { FailAtfBody(binding.Body, e); }
            }
            if (!session.IsHost) { ApplyPendingAtf(); return; }
            if (Time.unscaledTime < _nextAtfSend) return;
            _nextAtfSend = Time.unscaledTime + .2f;
            foreach (var state in BuildAtfStates())
            {
                var binding = _atf[state.ItemId];
                if (binding.Sent && binding.SentRevision == state.Revision && Time.unscaledTime < binding.NextSend) continue;
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                binding.Sent = true; binding.SentRevision = state.Revision; binding.NextSend = Time.unscaledTime + 5;
            }
        }

        private void FailAtfBody(Rigidbody body, Exception error)
        {
            if (!_failedAtf.Add(body)) return;
            foreach (var binding in _atf.Values) if (binding.Body == body) binding.Restore();
            WinterMPPlugin.Log.LogWarning("ATF bottle sync unavailable for '" + body.name + "': " + error.Message);
            SyncEventLog.Record("atf-bottle-disabled", error.Message);
        }
        private void ForgetAtf(uint id, bool destroyReplica)
        {
            _pendingAtf.Remove(id);
            if (!_atf.TryGetValue(id, out var binding)) return;
            _atf.Remove(id); binding.Restore();
            if (_items.TryGetValue(id, out var item) && item.Body == binding.Body)
            {
                if (binding.Body != null && item.KinematicSaved) binding.Body.isKinematic = item.OriginalKinematic;
                RemoveTrackedItem(id, binding.Body);
            }
            if (destroyReplica && binding.Replica && binding.Use != null) UnityEngine.Object.Destroy(binding.Use.gameObject);
        }
        private void ClearAtf()
        {
            foreach (uint id in new List<uint>(_atf.Keys)) ForgetAtf(id, true);
            foreach (var pair in _localAtf)
            {
                if (pair.Key == null) continue;
                _trackedBodies.Remove(pair.Key.GetComponent<Rigidbody>());
                pair.Key.SetActive(pair.Value.Active);
                foreach (var pause in pair.Value.Pauses) pause.Restore();
            }
            _atf.Clear(); _pendingAtf.Clear(); _receivedAtf.Clear(); _localAtf.Clear(); _failedAtf.Clear(); _atfReplicaErrors.Clear();
            _atfPrefab = null; _nextAtfPrefab = _nextAtfSend = 0;
        }
    }
}
