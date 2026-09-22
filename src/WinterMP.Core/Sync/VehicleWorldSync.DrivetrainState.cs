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
        private readonly Dictionary<uint, VehicleDrivetrainWearPublication> _drivetrainPublications = new Dictionary<uint, VehicleDrivetrainWearPublication>();
        private readonly Dictionary<uint, VehicleDrivetrainWearReplica> _drivetrainReplicas = new Dictionary<uint, VehicleDrivetrainWearReplica>();
        private float _nextDrivetrainStatePoll, _nextDrivetrainStateKeepalive, _nextDrivetrainStateError;

        private static VehicleDrivetrainWearData? DrivetrainStateRule(uint id)
        {
            var rule = SyncCatalog.VehicleDrivetrainWear;
            return rule != null && StableHash.Fnv1a32("vehicle:" + rule.RootPath) == id ? rule : null;
        }

        internal bool OnVehicleDrivetrainWearState(VehicleDrivetrainWearState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected
                || state == null || !state.Valid || DrivetrainStateRule(state.VehicleId) == null) return false;
            if (!_drivetrainReplicas.TryGetValue(state.VehicleId, out var replica))
                _drivetrainReplicas[state.VehicleId] = replica = new VehicleDrivetrainWearReplica();
            var previous = replica.Get();
            if (!replica.Receive(state))
            {
                SyncEventLog.Record("vehicle-drivetrain-state-rejected", "id=" + state.VehicleId + " revision=" + state.Revision);
                return false;
            }
            if (previous == null || previous.Revision != state.Revision)
                SyncEventLog.Record("vehicle-drivetrain-state-received", "id=" + state.VehicleId + " revision=" + state.Revision + " flags=" + state.Flags);
            if (previous == null || previous.Flags != state.Flags || previous.GearboxOilAvailable != state.GearboxOilAvailable)
                GuestEngineProtection.Prepare(force: true);
            return true;
        }

        internal bool TryReadDrivetrainWearInput(PlayMakerFSM fsm, int part, out float wear, out bool ready)
            => TryReadDrivetrainInput(fsm, part, false, out wear, out ready);

        internal bool TryReadGearboxOilInput(PlayMakerFSM fsm, out float oil, out bool ready)
            => TryReadDrivetrainInput(fsm, 1, true, out oil, out ready);

        private bool TryReadDrivetrainInput(PlayMakerFSM fsm, int part, bool oil, out float wear, out bool ready)
        {
            wear = 0; ready = false;
            var session = SessionManager.Instance; var rule = SyncCatalog.VehicleDrivetrainWear;
            if (session == null || session.IsHost || session.State != SessionState.Connected || part < 0 || part > 2) return false;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || item.Id != StableHash.Fnv1a32("vehicle:" + item.Path)
                    || !fsm.transform.IsChildOf(item.Body.transform)) continue;
                // A registered consumer with missing catalog metadata must wait;
                // falling back to its saved guest wear would revive stale input.
                if (rule == null || item.Path != rule.RootPath || DrivetrainStateRule(item.Id) == null) return true;
                var state = ReadDrivetrainWearState(item.Id); ready = state != null && state.Flags == VehicleDrivetrainWearState.Available;
                if (oil) ready &= state != null && state.GearboxOilAvailable;
                if (ready) wear = oil ? state!.GearboxOilLevel : part == 0 ? state!.DriveshaftWear : part == 1 ? state!.GearboxWear : state!.RearAxleWear;
                return true;
            }
            return false;
        }

        internal VehicleDrivetrainWearState? ReadDrivetrainWearState(uint vehicleId)
        {
            var session = SessionManager.Instance;
            return session != null && !session.IsHost && session.State == SessionState.Connected && DrivetrainStateRule(vehicleId) != null
                && _drivetrainReplicas.TryGetValue(vehicleId, out var replica) ? replica.Get() : null;
        }

        internal VehicleDrivetrainWearState? BuildVehicleDrivetrainWearState(SyncedItem item)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld) return null;
            var rule = DrivetrainStateRule(item.Id);
            if (rule == null && !_drivetrainPublications.ContainsKey(item.Id)) return null;
            var state = new VehicleDrivetrainWearState { VehicleId = item.Id };
            try
            {
                if (rule != null && item.IsVehicle && item.Body != null && item.Path == rule.RootPath)
                    CaptureDrivetrainWearState(item, rule, state);
            }
            catch (Exception error)
            {
                state = new VehicleDrivetrainWearState { VehicleId = item.Id };
                NoteDrivetrainStateFailure(item.Id, error);
            }
            if (!_drivetrainPublications.TryGetValue(item.Id, out var publication))
                _drivetrainPublications[item.Id] = publication = new VehicleDrivetrainWearPublication();
            return publication.Observe(state);
        }

        private static void CaptureDrivetrainWearState(SyncedItem item, VehicleDrivetrainWearData rule, VehicleDrivetrainWearState state)
        {
            var sourceRule = SyncCatalog.VehicleDifferentialSpeed;
            if (sourceRule == null) return;
            // A snapshot must not call EnsureDrivetrainWear: recovering its paused
            // entry can execute a saved write. This binding only validates and reads.
            var source = BindDifferentialSpeed(item, sourceRule);
            var b = new NativeDrivetrainWear { Item = item, Body = item.Body!, Rule = rule, Fsm = source.Fsm, Source = source,
                Sample = HeatState(source.Fsm, sourceRule.State), Wear = HeatState(source.Fsm, sourceRule.WearState),
                Wait = HeatState(source.Fsm, sourceRule.WaitState), Rate = source.Fsm.FsmVariables.FindFsmFloat("Rate") };
            if (!b.Fsm.enabled || !b.Fsm.gameObject.activeInHierarchy) return;
            ValidateDrivetrainWear(b);
            state.Flags = VehicleDrivetrainWearState.Available;
            state.DriveshaftWear = b.SavedWear[0].Value; state.GearboxWear = b.SavedWear[1].Value; state.RearAxleWear = b.SavedWear[2].Value;
            CaptureGearboxOil(b, state);
            if (!state.Valid) throw new InvalidOperationException("Native drivetrain wear is nonfinite.");
        }

        private static void CaptureGearboxOil(NativeDrivetrainWear b, VehicleDrivetrainWearState state)
        {
            // Wear validation has already identified the canonical, initialized
            // gearbox Data. A missing oil scalar withdraws only this extra input.
            var target = b.Fsm.FsmVariables.FindFsmGameObject(b.Rule.Targets[1].Variable).Value;
            PlayMakerFSM? data = null;
            foreach (var candidate in target.GetComponents<PlayMakerFSM>()) if (candidate.FsmName == "Data") data = candidate;
            if (data == null) return;
            FsmFloat? oil = null;
            foreach (var scalar in data.FsmVariables.FloatVariables)
                if (scalar.Name == "OilLevel") { if (oil != null) return; oil = scalar; }
            if (oil == null || oil.IsNone || !oil.UseVariable || float.IsNaN(oil.Value) || float.IsInfinity(oil.Value)) return;
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables) if (ReferenceEquals(global, oil)) return;
            foreach (var scalar in b.Fsm.FsmVariables.FloatVariables) if (ReferenceEquals(scalar, oil)) return;
            foreach (var scalar in b.SavedWear) if (ReferenceEquals(scalar, oil)) return;
            state.GearboxOilAvailable = true; state.GearboxOilLevel = oil.Value;
        }

        internal void UpdateDrivetrainWearStates(SessionManager session)
        {
            if (!session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld
                || Time.unscaledTime < _nextDrivetrainStatePoll) return;
            _nextDrivetrainStatePoll = Time.unscaledTime + .5f;
            bool keepalive = Time.unscaledTime >= _nextDrivetrainStateKeepalive;
            foreach (var item in _items.Items.Values)
                try
                {
                    var state = BuildVehicleDrivetrainWearState(item);
                    if (state != null && session.PlayerCount > 0)
                        PublishDrivetrainWearState(session, _drivetrainPublications[item.Id], state, keepalive);
                }
                catch (Exception error) { NoteDrivetrainStateFailure(item.Id, error); }
            if (session.PlayerCount > 0)
                foreach (var pair in _drivetrainPublications)
                    if (!_items.Items.ContainsKey(pair.Key))
                        try { PublishDrivetrainWearState(session, pair.Value, pair.Value.Observe(new VehicleDrivetrainWearState { VehicleId = pair.Key }), keepalive); }
                        catch (Exception error) { NoteDrivetrainStateFailure(pair.Key, error); }
            if (keepalive) _nextDrivetrainStateKeepalive = Time.unscaledTime + 5f;
        }

        private static void PublishDrivetrainWearState(SessionManager session, VehicleDrivetrainWearPublication publication,
            VehicleDrivetrainWearState state, bool keepalive)
        {
            if (!publication.NeedsBroadcast && !keepalive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            publication.MarkBroadcast(state.Revision);
            SyncEventLog.Record("vehicle-drivetrain-state-published", "id=" + state.VehicleId + " revision=" + state.Revision + " flags=" + state.Flags);
        }

        private void NoteDrivetrainStateFailure(uint id, Exception error)
        {
            if (Time.unscaledTime < _nextDrivetrainStateError) return;
            _nextDrivetrainStateError = Time.unscaledTime + 10f;
            SyncEventLog.Record("vehicle-drivetrain-state-unavailable", "id=" + id + ": " + error.Message);
        }

        private void ClearDrivetrainWearStates()
        {
            ClearGearboxOilUse(); ClearGearboxWear();
            _drivetrainPublications.Clear(); _drivetrainReplicas.Clear();
            _nextDrivetrainStatePoll = _nextDrivetrainStateKeepalive = _nextDrivetrainStateError = 0;
        }
    }
}
