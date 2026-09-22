using System;
using System.Collections.Generic;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class PendingMilk
        {
            internal MilkConditionState State = null!;
            internal float Expires;
        }
        private readonly Dictionary<uint, MilkConditionBinding> _milk = new Dictionary<uint, MilkConditionBinding>();
        private readonly Dictionary<uint, PendingMilk> _pendingMilk = new Dictionary<uint, PendingMilk>();
        private float _nextMilkScan, _nextMilkUpdate, _nextMilkWarning;

        private void TryTrackMilk(SyncedItem item, string? templateName = null)
        {
            var c = SyncCatalog.MilkCondition; var session = SessionManager.Instance;
            if (c == null || session == null || item.Body == null || item.IsVehicle || _milk.ContainsKey(item.Id)) return;
            string name = templateName ?? item.Body.name;
            if (name != c.ItemName && name != c.SpoiledName) return;
            try
            {
                foreach (var fsm in item.Body.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == c.Fsm)
                    {
                        _milk.Add(item.Id, new MilkConditionBinding(fsm, item.Body, c, !session.IsHost)); return;
                    }
            }
            catch (Exception e)
            {
                if (Time.unscaledTime < _nextMilkWarning) return;
                _nextMilkWarning = Time.unscaledTime + 10;
                WinterMPPlugin.Log.LogWarning("Waiting for milk condition binding: " + e.Message);
            }
        }
        private void DiscoverMilk()
        {
            foreach (var item in _items.Values) TryTrackMilk(item);
        }
        internal void ProcessMilk(SessionManager session)
        {
            if (Time.unscaledTime >= _nextMilkScan) { _nextMilkScan = Time.unscaledTime + 1; DiscoverMilk(); }
            foreach (var pair in _milk)
            {
                var binding = pair.Value;
                try
                {
                    binding.CaptureOriginal();
                    if (_pendingMilk.TryGetValue(pair.Key, out var pending) && binding.Apply(pending.State)) _pendingMilk.Remove(pair.Key);
                }
                catch (Exception e) { binding.Fail(e); }
            }
            foreach (uint id in new List<uint>(_pendingMilk.Keys))
                if (_pendingMilk[id].Expires < Time.unscaledTime || _spawnLifecycle.IsRetired(id)) _pendingMilk.Remove(id);
            if (!session.IsHost || session.PlayerCount == 0 || Time.unscaledTime < _nextMilkUpdate) return;
            _nextMilkUpdate = Time.unscaledTime + 1;
            foreach (var state in BuildMilkStates())
            {
                var binding = _milk[state.NetId];
                if (binding.Sent != null && MilkConditionPolicy.Same(binding.Sent, state) && Time.unscaledTime < binding.NextSend) continue;
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                binding.Sent = state; binding.NextSend = Time.unscaledTime + 5;
            }
        }
        internal MilkConditionState? BuildMilkState(uint id)
        {
            if (SessionManager.Instance?.IsHost != true || _spawnLifecycle.IsRetired(id) || !_items.TryGetValue(id, out var item)) return null;
            TryTrackMilk(item);
            if (!_milk.TryGetValue(id, out var binding) || binding.Body != item.Body) return null;
            try { return binding.Capture(id); }
            catch (Exception e) { binding.Fail(e); return null; }
        }
        internal IEnumerable<MilkConditionState> BuildMilkStates()
        {
            if (SessionManager.Instance?.IsHost != true) yield break;
            DiscoverMilk();
            foreach (uint id in new List<uint>(_milk.Keys))
            {
                var state = BuildMilkState(id); if (state != null) yield return state;
            }
        }
        internal void OnMilkCondition(MilkConditionState state)
        {
            if (SessionManager.Instance?.IsHost != false || !MilkConditionPolicy.Valid(state) || _spawnLifecycle.IsRetired(state.NetId)) return;
            MilkConditionState? previous = _pendingMilk.TryGetValue(state.NetId, out var pending) ? pending.State
                : (_milk.TryGetValue(state.NetId, out var binding) ? binding.Received : null);
            if (!MilkConditionPolicy.CanReceive(previous, state) || (!_pendingMilk.ContainsKey(state.NetId) && _pendingMilk.Count >= 256)) return;
            _pendingMilk[state.NetId] = new PendingMilk { State = MilkConditionPolicy.Copy(state), Expires = Time.unscaledTime + 120 };
        }
        private void ForgetMilk(uint id)
        {
            if (_milk.TryGetValue(id, out var binding))
            {
                try { binding.Restore(); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("Milk condition cleanup: " + e.Message); }
                _milk.Remove(id);
            }
            _pendingMilk.Remove(id);
        }
        private void ClearMilk()
        {
            foreach (uint id in new List<uint>(_milk.Keys)) ForgetMilk(id);
            _pendingMilk.Clear(); _nextMilkScan = _nextMilkUpdate = _nextMilkWarning = 0;
        }
    }
}
