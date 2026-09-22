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
    internal sealed partial class VehicleWorldSync
    {
        private readonly Dictionary<uint, VehicleWheelHealthPublication> _wheelHealthPublications = new Dictionary<uint, VehicleWheelHealthPublication>();
        private readonly Dictionary<uint, VehicleWheelHealthReplica> _wheelHealthReplicas = new Dictionary<uint, VehicleWheelHealthReplica>();
        private readonly HashSet<PlayMakerFSM> _hostWheelHealthConsumers = new HashSet<PlayMakerFSM>();
        private readonly Dictionary<uint, GameObject?[]> _wheelHealthTargets = new Dictionary<uint, GameObject?[]>();
        private float _nextWheelHealthPoll, _nextWheelHealthKeepalive, _nextWheelHealthError;

        private static VehicleWheelHealthData? WheelHealthRule(uint id)
        {
            var rule = SyncCatalog.VehicleWheelHealth;
            return rule != null && StableHash.Fnv1a32("vehicle:" + rule.RootPath) == id ? rule : null;
        }

        internal bool OnVehicleWheelHealthState(VehicleWheelHealthState state)
        {
            var session = _bridge.Session;
            if (session == null || session.IsHost || session.State != SessionState.Connected
                || state == null || !state.Valid || WheelHealthRule(state.VehicleId) == null && !_wheelHealthReplicas.ContainsKey(state.VehicleId)) return false;
            if (!_wheelHealthReplicas.TryGetValue(state.VehicleId, out var replica))
                _wheelHealthReplicas[state.VehicleId] = replica = new VehicleWheelHealthReplica();
            _hostWheelHealthConsumers.RemoveWhere(fsm => fsm == null);
            var previous = replica.Get();
            if (!replica.Receive(state)) return false;
            if (previous == null || previous.Revision != state.Revision)
                SyncEventLog.Record("vehicle-wheel-health-received", "id=" + state.VehicleId + " revision=" + state.Revision + " availability=" + state.Availability);
            if (previous == null || previous.Availability != state.Availability) GuestEngineProtection.Prepare(force: true);
            return true;
        }

        internal bool TryReadHostWheelHealth(PlayMakerFSM fsm, int wheel, out float health, out bool ready)
        {
            health = 0; ready = false;
            var session = _bridge.Session;
            if (session == null || session.IsHost || session.State != SessionState.Connected || wheel < 0 || wheel > 3) return false;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || item.Id != StableHash.Fnv1a32("vehicle:" + item.Path)
                    || !fsm.transform.IsChildOf(item.Body.transform)) continue;
                _hostWheelHealthConsumers.Add(fsm);
                var rule = WheelHealthRule(item.Id);
                // Registered wheels with missing metadata wait for repair; they
                // cannot resume an old driver's report or the guest's own save.
                if (rule == null || item.Path != rule.RootPath) return true;
                bool selected = false;
                foreach (var source in rule.Sources)
                    if (source.Wheel == wheel && source.Path == ScenePath.Of(fsm.transform)) selected = true;
                if (!selected) return true;
                var state = ReadWheelHealthState(item.Id); ready = state != null && state.HasWheel(wheel);
                if (ready) health = state!.Health(wheel);
                return true;
            }
            // A once-registered wheel cannot fall back to personal save data
            // while its body is temporarily unregistered or being replaced.
            return _hostWheelHealthConsumers.Contains(fsm);
        }

        internal VehicleWheelHealthState? ReadWheelHealthState(uint id)
        {
            var session = _bridge.Session;
            return session != null && !session.IsHost && session.State == SessionState.Connected && WheelHealthRule(id) != null
                && _wheelHealthReplicas.TryGetValue(id, out var replica) ? replica.Get() : null;
        }

        internal VehicleWheelHealthState? BuildVehicleWheelHealthState(SyncedItem item)
        {
            var session = _bridge.Session;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld) return null;
            var rule = WheelHealthRule(item.Id);
            if (rule == null && !_wheelHealthPublications.ContainsKey(item.Id)) return null;
            var state = new VehicleWheelHealthState { VehicleId = item.Id };
            if (!_wheelHealthTargets.TryGetValue(item.Id, out var targets)) _wheelHealthTargets[item.Id] = targets = new GameObject?[4];
            byte changedTargets = 0;
            if (rule != null && item.IsVehicle && item.Body != null && item.Path == rule.RootPath && ScenePath.Of(item.Body.transform) == rule.RootPath)
                foreach (var source in rule.Sources)
                    try
                    {
                        var fsm = FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), source.Path, "Condition");
                        float health = GuestEngineProtection.CaptureHostWheelHealth(fsm, source, item.Body);
                        var mount = fsm.FsmVariables.FindFsmGameObject("ThisTire").Value;
                        var data = FindTemperatureFsm(mount.GetComponents<PlayMakerFSM>(), source.TargetPath, "Data");
                        var target = data.FsmVariables.FindFsmGameObject("ActivePart")?.Value ?? mount;
                        if (!ReferenceEquals(targets[source.Wheel], target)) { changedTargets |= (byte)(1 << source.Wheel); targets[source.Wheel] = target; }
                        switch (source.Wheel) { case 0: state.HealthFL = health; break; case 1: state.HealthFR = health; break;
                            case 2: state.HealthRL = health; break; case 3: state.HealthRR = health; break; }
                        state.Availability |= (byte)(1 << source.Wheel);
                    }
                    catch (Exception error) { NoteWheelHealthFailure(item.Id, error, source.Path); }
            if (!_wheelHealthPublications.TryGetValue(item.Id, out var publication))
                _wheelHealthPublications[item.Id] = publication = new VehicleWheelHealthPublication();
            return publication.Observe(state, changedTargets);
        }

        internal void UpdateWheelHealthStates(SessionManager session)
        {
            if (!session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld || Time.unscaledTime < _nextWheelHealthPoll) return;
            _nextWheelHealthPoll = Time.unscaledTime + .5f;
            bool keepalive = Time.unscaledTime >= _nextWheelHealthKeepalive;
            foreach (var item in _items.Items.Values)
                try
                {
                    var state = BuildVehicleWheelHealthState(item);
                    if (state != null && session.PlayerCount > 0) PublishWheelHealthState(session, _wheelHealthPublications[item.Id], state, keepalive);
                }
                catch (Exception error) { NoteWheelHealthFailure(item.Id, error); }
            if (session.PlayerCount > 0)
                foreach (var pair in _wheelHealthPublications)
                    if (!_items.Items.ContainsKey(pair.Key))
                        try { PublishWheelHealthState(session, pair.Value, pair.Value.Observe(new VehicleWheelHealthState { VehicleId = pair.Key }), keepalive); }
                        catch (Exception error) { NoteWheelHealthFailure(pair.Key, error); }
            if (keepalive) _nextWheelHealthKeepalive = Time.unscaledTime + 5f;
        }

        private static void PublishWheelHealthState(SessionManager session, VehicleWheelHealthPublication publication, VehicleWheelHealthState state, bool keepalive)
        {
            if (!publication.NeedsBroadcast && !keepalive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered); publication.MarkBroadcast(state.Revision);
            SyncEventLog.Record("vehicle-wheel-health-published", "id=" + state.VehicleId + " revision=" + state.Revision + " availability=" + state.Availability);
        }
        private void NoteWheelHealthFailure(uint id, Exception error, string? source = null)
        {
            if (Time.unscaledTime < _nextWheelHealthError) return;
            _nextWheelHealthError = Time.unscaledTime + 10f;
            SyncEventLog.Record("vehicle-wheel-health-unavailable", "id=" + id + " " + source + ": " + error.Message);
        }
        private void ClearWheelHealthStates()
        {
            _wheelHealthPublications.Clear(); _wheelHealthReplicas.Clear();
            _hostWheelHealthConsumers.Clear();
            _wheelHealthTargets.Clear(); _wheelPunctureSequences.Clear(); _wheelPuncturePolicy.Clear();
            _nextWheelHealthPoll = _nextWheelHealthKeepalive = _nextWheelHealthError = 0;
        }
    }
}
