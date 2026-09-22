using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private readonly Dictionary<uint, FirewoodBuyerBinding> _firewoodBuyers = new Dictionary<uint, FirewoodBuyerBinding>();
        private readonly Dictionary<uint, FirewoodBuyerState> _pendingFirewoodBuyers = new Dictionary<uint, FirewoodBuyerState>();
        private readonly HashSet<uint> _failedFirewoodBuyers = new HashSet<uint>();
        private float _nextFirewoodProbe, _nextFirewoodTick, _nextFirewoodKeepAlive;

        private void LocateFirewoodBuyers(bool force = false)
        {
            if (SyncCatalog.FirewoodBuyers == null || (!force && Time.unscaledTime < _nextFirewoodProbe)) return;
            _nextFirewoodProbe = Time.unscaledTime + 5;
            var root = GameObject.Find("JOBS");
            if (root == null) return;
            foreach (var config in SyncCatalog.FirewoodBuyers)
            {
                uint id = StableHash.Fnv1a32(config.PaymentPath + "::Use");
                if (_firewoodBuyers.ContainsKey(id) || _failedFirewoodBuyers.Contains(id)) continue;
                try
                {
                    if (!_controls.TryGetValue(id, out var control))
                    {
                        var payment = FirewoodBuyerBinding.PaymentAt(root.transform, config);
                        if (payment == null) continue;
                        if (!payment.Fsm.Initialized) payment.Fsm.Init(payment);
                        var rule = SyncCatalog.TryMatchControl(payment);
                        if (rule == null || rule.HostPayment != "firewood" || !RegisterControl(payment, rule)) continue;
                        control = _controls[id];
                    }
                    if (control.Payment == null || control.Fsm == null) continue;
                    var binding = FirewoodBuyerBinding.TryCreate(id, control.Fsm, control.Payment, config, SessionManager.Instance?.IsHost != true);
                    if (binding == null) continue;
                    _firewoodBuyers.Add(id, binding);
                    if (_pendingFirewoodBuyers.TryGetValue(id, out var pending))
                    { binding.Receive(pending); _pendingFirewoodBuyers.Remove(id); }
                    WinterMPPlugin.Log.LogInfo("Firewood buyer sync: bound " + config.BuyerPath);
                }
                catch (Exception e)
                {
                    _failedFirewoodBuyers.Add(id);
                    WinterMPPlugin.Log.LogWarning("Firewood buyer sync disabled for " + config.BuyerPath + ": " + e.Message);
                }
            }
        }

        internal void UpdateFirewoodBuyers(SessionManager session)
        {
            LocateFirewoodBuyers();
            if (Time.unscaledTime < _nextFirewoodTick) return;
            _nextFirewoodTick = Time.unscaledTime + .25f;
            bool keepAlive = Time.unscaledTime >= _nextFirewoodKeepAlive;
            if (keepAlive) _nextFirewoodKeepAlive = Time.unscaledTime + 3;
            foreach (var pair in _firewoodBuyers)
            {
                if (_failedFirewoodBuyers.Contains(pair.Key)) continue;
                try
                {
                    if (session.IsHost)
                    {
                        var state = pair.Value.Capture();
                        if (session.PlayerCount > 0 && pair.Value.ShouldBroadcast(state, keepAlive))
                            session.SendWorldMessage(state, Channel.ReliableOrdered);
                    }
                    else pair.Value.Present();
                }
                catch (Exception e)
                {
                    _failedFirewoodBuyers.Add(pair.Key);
                    pair.Value.Fail();
                    WinterMPPlugin.Log.LogWarning("Firewood buyer sync disabled for " + pair.Key.ToString("X8") + ": " + e.Message);
                }
            }
        }

        internal IEnumerable<FirewoodBuyerState> BuildFirewoodBuyerSnapshots()
        {
            LocateFirewoodBuyers(true);
            foreach (var pair in _firewoodBuyers)
            {
                if (_failedFirewoodBuyers.Contains(pair.Key)) continue;
                FirewoodBuyerState? state = null;
                try { state = pair.Value.Capture(); }
                catch (Exception e)
                {
                    _failedFirewoodBuyers.Add(pair.Key); pair.Value.Fail();
                    WinterMPPlugin.Log.LogWarning("Firewood buyer snapshot disabled for " + pair.Key.ToString("X8") + ": " + e.Message);
                }
                if (state != null) yield return state;
            }
        }

        internal void OnFirewoodBuyer(FirewoodBuyerState message)
        {
            if (SessionManager.Instance?.IsHost != false || !FirewoodBuyerPolicy.Valid(message) || SyncCatalog.FirewoodBuyers == null) return;
            bool known = false;
            foreach (var config in SyncCatalog.FirewoodBuyers)
                if (message.NetId == StableHash.Fnv1a32(config.PaymentPath + "::Use")) { known = true; break; }
            if (!known || _failedFirewoodBuyers.Contains(message.NetId)) return;
            LocateFirewoodBuyers();
            if (_firewoodBuyers.TryGetValue(message.NetId, out var buyer)) buyer.Receive(message);
            else
            {
                _pendingFirewoodBuyers.TryGetValue(message.NetId, out var previous);
                if (FirewoodBuyerPolicy.CanReceive(previous, message))
                    _pendingFirewoodBuyers[message.NetId] = FirewoodBuyerPolicy.Copy(message);
            }
        }

        private void ClearFirewoodBuyers()
        {
            foreach (var buyer in _firewoodBuyers.Values)
                try { buyer.Restore(); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("Firewood buyer cleanup: " + e.Message); }
            _firewoodBuyers.Clear(); _pendingFirewoodBuyers.Clear(); _failedFirewoodBuyers.Clear();
            _nextFirewoodProbe = _nextFirewoodTick = _nextFirewoodKeepAlive = 0;
        }
    }
}
