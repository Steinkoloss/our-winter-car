using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal BulbState? BuildBulbState(uint id)
        {
            if (SessionManager.Instance?.IsHost != true || _bulbFactory == null || _bulbFactory.Failed
                || !_bulbs.TryGetValue(id, out var b) || b.Replica || b.Body == null || b.Data == null
                || _spawnLifecycle.IsRetired(id) || !_items.ContainsKey(id)) return null;
            var s = new BulbState { ItemId = id, Revision = b.Last?.Revision ?? 1,
                Wear = b.Data.FsmVariables.FindFsmFloat(_bulbFactory.Rule.BulbContents!["wear"]).Value,
                Position = b.Body.position.ToNet(), Rotation = b.Body.rotation.ToNet() };
            if (b.Last != null && b.Last.Wear != s.Wear) { if (++s.Revision == 0) ++s.Revision; }
            if (!BulbPolicy.Valid(s)) { FailBulbs(new InvalidOperationException("Invalid host bulb condition or pose.")); return null; }
            b.Last = s; return s;
        }
        internal IEnumerable<BulbState> BuildBulbStates()
        {
            foreach (uint id in _bulbs.Keys) { var state = BuildBulbState(id); if (state != null) yield return state; }
        }
        internal void OnBulbState(BulbState state)
        {
            if (SessionManager.Instance?.IsHost != false || _spawnLifecycle.IsRetired(state.ItemId)) return;
            var old = _pendingBulbs.TryGetValue(state.ItemId, out var pending) ? pending : _bulbs.TryGetValue(state.ItemId, out var b) ? b.Received : null;
            if (!BulbPolicy.Accept(state, old) || (!_pendingBulbs.ContainsKey(state.ItemId) && _pendingBulbs.Count >= 1024)) return;
            _snapshotSeenIds.Add(state.ItemId); _pendingBulbs[state.ItemId] = state;
        }
        private void ProcessBulbs(SessionManager session)
        {
            var factory = _bulbFactory;
            if (factory == null || factory.Failed) return;
            try
            {
                if (!session.IsHost && !factory.Suppressor.Active && factory.Fsm.Fsm.Started
                    && factory.Fsm.ActiveStateName == factory.Rule.BulbContents!["idle"])
                    if (!factory.Suppressor.Suppress(factory.Fsm)) throw new InvalidOperationException("Cannot pause guest bulb factory.");
                foreach (var body in new List<Rigidbody>(_unreadyBulbs.Keys))
                { if (!body) _unreadyBulbs.Remove(body); else TryScanBulb(body); }
                if (Time.unscaledTime < _bulbTick) return;
                _bulbTick = Time.unscaledTime + .25f;
                if (session.IsHost)
                {
                    foreach (var pair in _bulbs)
                        if (pair.Value.Body == null && !_spawnLifecycle.IsRetired(pair.Key))
                        { AnnounceItemDespawn(pair.Key, "bulb removed"); RecordItemRetirement(pair.Key); }
                    foreach (var state in BuildBulbStates())
                    {
                        var b = _bulbs[state.ItemId];
                        if (b.Sent == state.Revision && Time.unscaledTime < b.NextSend) continue;
                        session.SendWorldMessage(state, Channel.ReliableOrdered); b.Sent = state.Revision; b.NextSend = Time.unscaledTime + 5;
                    }
                }
                else if (factory.Suppressor.Active)
                    foreach (uint id in new List<uint>(_pendingBulbs.Keys))
                    {
                        var state = _pendingBulbs[id];
                        if (!_spawnLifecycle.IsRetired(id))
                        {
                            if (_bulbs.TryGetValue(id, out var b) && b.Body != null)
                            { b.Data.FsmVariables.FindFsmFloat(factory.Rule.BulbContents!["wear"]).Value = state.Wear; b.Received = state; }
                            else MaterializeBulb(factory, state);
                        }
                        _pendingBulbs.Remove(id);
                    }
            }
            catch (Exception e) { FailBulbs(e); }
        }
        private void MaterializeBulb(BulbFactory factory, BulbState state)
        {
            uint id = state.ItemId;
            if (_items.TryGetValue(id, out var old))
            {
                if (old.Body != null) throw new InvalidOperationException("Bulb replica identity collision.");
                RemoveTrackedItem(id, old.Body);
            }
            GameObject clone; bool enabled = factory.Template.enabled;
            try
            {
                factory.Template.enabled = false;
                clone = (GameObject)UnityEngine.Object.Instantiate(factory.Prefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { factory.Template.enabled = enabled; }
            try
            {
                var rule = factory.Rule.BulbContents!;
                var body = clone.GetComponent<Rigidbody>(); var data = clone.GetComponent<PlayMakerFSM>();
                data.enabled = false; if (!data.Fsm.Initialized) data.Fsm.Init(data);
                ValidateBulbData(data, rule);
                // Native initialization rolls condition again. A replica starts in the
                // inert Data state, retaining the host's already settled condition.
                data.FsmVariables.FindFsmFloat(rule["wear"]).Value = state.Wear;
                clone.name = rule["itemName"]; body.isKinematic = false;
                var item = new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform), LastPosition = body.position };
                _items.Add(id, item); _trackedBodies[body] = true;
                _bulbs[id] = new BulbBinding { Body = body, Data = data, Replica = true, Received = state };
                ApplySnapshotPose(item, state.Position.ToUnity(), state.Rotation.ToUnity()); _pendingItemPoses.Remove(id);
                data.Fsm.StartState = rule["ready"]; data.Fsm.RestartOnEnable = true;
                data.enabled = true; clone.SetActive(true); if (!data.Fsm.Started) data.Fsm.Start();
            }
            catch
            {
                var data = clone.GetComponent<PlayMakerFSM>(); if (data != null) data.enabled = false;
                RemoveTrackedItem(id, clone.GetComponent<Rigidbody>()); _bulbs.Remove(id); UnityEngine.Object.Destroy(clone); throw;
            }
        }
    }
}
