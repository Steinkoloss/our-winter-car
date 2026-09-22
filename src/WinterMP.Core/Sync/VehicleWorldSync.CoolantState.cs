using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private readonly Dictionary<uint, VehicleCoolantPublication> _coolantPublications = new Dictionary<uint, VehicleCoolantPublication>();
        private readonly Dictionary<uint, VehicleCoolantReplica> _coolantReplicas = new Dictionary<uint, VehicleCoolantReplica>();
        private float _nextCoolantPoll, _nextCoolantKeepalive, _nextCoolantRetireError;

        private static VehicleTemperatureSourceData? HostCoolantRule(uint id)
        {
            var profile = SyncCatalog.VehicleTemperature;
            if (profile != null)
                foreach (var rule in profile.Sources)
                    if (rule.HostAuthoritative && StableHash.Fnv1a32("vehicle:" + rule.RootPath) == id) return rule;
            return null;
        }

        internal void OnVehicleCoolantState(VehicleCoolantState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected
                || state == null || !state.Valid || HostCoolantRule(state.VehicleId) == null) return;
            if (!_coolantReplicas.TryGetValue(state.VehicleId, out var replica))
                _coolantReplicas[state.VehicleId] = replica = new VehicleCoolantReplica();
            if (!replica.Receive(state))
            {
                SyncEventLog.Record("vehicle-coolant-rejected", "id=" + state.VehicleId + " revision=" + state.Revision);
                return;
            }
            // Keep accepted state before scene discovery; driver changes do not
            // establish a new temperature stream or reset its revision baseline.
            if (_items.Items.TryGetValue(state.VehicleId, out var item))
            { item.HostCoolant = replica.Get(); EnsureEngineTemperatureInputs(item); EnsureCabinTemperatureInputs(item); EnsureElectricalTemperatureInputs(item); }
        }

        internal VehicleCoolantState? BuildVehicleCoolantState(SyncedItem item)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || GuestSaveGuard.ProtectWorld
                || (session.State != SessionState.Hosting && session.State != SessionState.Connected) || !item.IsVehicle) return null;
            var rule = HostCoolantRule(item.Id);
            if (rule == null && !_coolantPublications.ContainsKey(item.Id)) return null;
            byte flags = 0; float celsius = 0, engineCelsius = 0;
            if (rule != null && item.Path == rule.RootPath && item.Body != null)
            {
                EnsureTemperatureProbe(item);
                var binding = item.NativeTemperature;
                if (binding != null && ReferenceEquals(binding.Rule, rule))
                {
                    if (binding.Source.ActiveStateName == binding.Source.Fsm.StartState) binding.SourceReady = false;
                    if (binding.Source.Fsm.Started && ReferenceEquals(binding.Source.Fsm.ActiveState, binding.ReadyState))
                        binding.SourceReady = true;
                    if (binding.SourceReady && FiniteTemperature(binding.Celsius.Value)
                        && binding.EngineCelsius != null && FiniteTemperature(binding.EngineCelsius.Value))
                    { flags = VehicleCoolantState.Available; celsius = binding.Celsius.Value; engineCelsius = binding.EngineCelsius.Value; }
                }
            }
            if (!_coolantPublications.TryGetValue(item.Id, out var publication))
                _coolantPublications[item.Id] = publication = new VehicleCoolantPublication();
            return publication.Observe(item.Id, flags, celsius, engineCelsius);
        }

        internal void UpdateVehicleCoolant(SessionManager session)
        {
            if (Time.unscaledTime < _nextCoolantPoll) return;
            _nextCoolantPoll = Time.unscaledTime + .5f;
            bool keepalive = Time.unscaledTime >= _nextCoolantKeepalive;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle) continue;
                try
                {
                    if (session.IsHost)
                    {
                        var state = BuildVehicleCoolantState(item);
                        if (state == null || session.PlayerCount == 0) continue;
                        PublishVehicleCoolant(session, _coolantPublications[item.Id], state, keepalive);
                    }
                    else if (session.State == SessionState.Connected && HostCoolantRule(item.Id)?.RootPath == item.Path)
                    {
                        if (_coolantReplicas.TryGetValue(item.Id, out var replica)) item.HostCoolant = replica.Get();
                        EnsureEngineTemperatureInputs(item); EnsureCabinTemperatureInputs(item); EnsureElectricalTemperatureInputs(item);
                        ApplyRemoteTemperature(item, 0f);
                    }
                }
                catch (Exception error)
                {
                    if (Time.unscaledTime < item.NextTemperatureErrorAt) continue;
                    item.NextTemperatureErrorAt = Time.unscaledTime + 10f;
                    SyncEventLog.Record("vehicle-coolant-unavailable", item.Path + ": " + error.Message);
                }
            }
            if (session.IsHost && !GuestSaveGuard.ProtectWorld && session.PlayerCount > 0
                && (session.State == SessionState.Hosting || session.State == SessionState.Connected))
                foreach (var pair in _coolantPublications)
                    if (!_items.Items.ContainsKey(pair.Key))
                        try { PublishVehicleCoolant(session, pair.Value, pair.Value.Observe(pair.Key, 0, 0), keepalive); }
                        catch (Exception error)
                        {
                            if (Time.unscaledTime < _nextCoolantRetireError) continue;
                            _nextCoolantRetireError = Time.unscaledTime + 10f;
                            SyncEventLog.Record("vehicle-coolant-retire-unavailable", "id=" + pair.Key + ": " + error.Message);
                        }
            if (keepalive) _nextCoolantKeepalive = Time.unscaledTime + 5f;
        }

        private static void PublishVehicleCoolant(SessionManager session, VehicleCoolantPublication publication,
            VehicleCoolantState state, bool keepalive)
        {
            if (!publication.NeedsBroadcast && !keepalive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            publication.MarkBroadcast(state.Revision);
        }

        private static bool UsesHostCoolant(SyncedItem item)
        {
            var profile = SyncCatalog.VehicleTemperature;
            if (profile != null)
                foreach (var rule in profile.Sources)
                    if (rule.RootPath == item.Path && rule.HostAuthoritative) item.RequiresHostCoolant = true;
            return item.RequiresHostCoolant;
        }

        private static bool TryGetHostCoolant(SyncedItem item, out float celsius)
        {
            celsius = 0f;
            var session = SessionManager.Instance;
            if (!UsesHostCoolant(item) || session == null || session.IsHost || session.State != SessionState.Connected) return false;
            var state = item.HostCoolant;
            if (state != null && state.Valid && state.VehicleId == item.Id && state.Flags == VehicleCoolantState.Available)
                celsius = state.Celsius;
            // An unseeded/unavailable host source displays the native cold stop.
            // Never substitute the guest's saved or locally simulated heat.
            return true;
        }

        private void ClearVehicleCoolant()
        {
            _coolantPublications.Clear(); _coolantReplicas.Clear();
            _nextCoolantPoll = _nextCoolantKeepalive = _nextCoolantRetireError = 0;
            foreach (var item in _items.Items.Values)
            {
                item.HostCoolant = null; item.RequiresHostCoolant = false;
                ClearEngineTemperatureInputs(item); ClearCabinTemperatureInputs(item); ClearElectricalTemperatureInputs(item);
                if (item.NativeTemperature != null) item.NativeTemperature.SourceReady = false;
            }
        }
    }
}
