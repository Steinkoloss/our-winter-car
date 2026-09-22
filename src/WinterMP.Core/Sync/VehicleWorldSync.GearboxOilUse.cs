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
        private readonly Dictionary<uint, ushort> _oilUseSequences = new Dictionary<uint, ushort>();
        private readonly GearboxOilUsePolicy _oilUsePolicy = new GearboxOilUsePolicy();
        private float _nextOilUseError;
        private SyncedItem? GearboxUseItem(PlayMakerFSM fsm, bool oil)
        {
            var profile = SyncCatalog.GuestEngineProtection; if (profile == null) return null;
            bool consumer = false;
            foreach (var rule in profile.Writers)
                if (rule.Fsm == fsm.FsmName && rule.Path == ScenePath.Of(fsm.transform))
                {
                    if (oil) consumer |= rule.GearboxOilRead != null;
                    else foreach (var action in rule.Actions) consumer |= action.GearboxWear;
                }
            if (!consumer) return null;
            foreach (var item in _items.Items.Values)
                if (item.IsVehicle && item.Body != null && DrivetrainStateRule(item.Id) != null
                    && item.Path == ScenePath.Of(item.Body.transform) && fsm.transform.IsChildOf(item.Body.transform)) return item;
            return null;
        }
        internal bool CanObserveGearboxOil(PlayMakerFSM fsm) => CanObserveGearboxUse(fsm, true);
        internal bool CanObserveGearboxWear(PlayMakerFSM fsm) => CanObserveGearboxUse(fsm, false);
        private bool CanObserveGearboxUse(PlayMakerFSM fsm, bool oil)
        {
            var session = SessionManager.Instance; var item = GearboxUseItem(fsm, oil);
            return session != null && !session.IsHost && session.State == SessionState.Connected && GuestSaveGuard.ProtectWorld
                && item != null && item.LocallyOwned && _items.IsLocalPlayerDriving(item);
        }
        internal bool ShouldDelegateGearboxOil(PlayMakerFSM fsm) => ShouldDelegateGearboxUse(fsm, true);
        internal bool ShouldDelegateGearboxWear(PlayMakerFSM fsm) => ShouldDelegateGearboxUse(fsm, false);
        private bool ShouldDelegateGearboxUse(PlayMakerFSM fsm, bool oil)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld) return false;
            var item = GearboxUseItem(fsm, oil);
            return item != null && item.RemoteOwner != 0 && item.RemoteOwner != WorldSyncIds.NoOwner
                && !item.LocallyOwned && !_items.IsLocalPlayerDriving(item);
        }
        internal void RecordGearboxOilUse(PlayMakerFSM fsm, byte phase)
        {
            if (!CanObserveGearboxOil(fsm)) return;
            var item = GearboxUseItem(fsm, true); var session = SessionManager.Instance;
            if (item == null || session == null) return;
            _oilUseSequences.TryGetValue(item.Id, out ushort sequence); sequence = unchecked((ushort)(sequence + 1)); _oilUseSequences[item.Id] = sequence;
            var message = new GearboxOilUseRequest { VehicleId = item.Id, PlayerId = session.LocalPlayerId, Sequence = sequence, Phase = phase };
            session.SendWorldMessage(message, Channel.ReliableOrdered);
            SyncEventLog.Record("gearbox-oil-use-sent", "id=" + item.Id + " phase=" + phase + " sequence=" + sequence);
        }
        internal bool OnHostGearboxOilUse(GearboxOilUseRequest request, byte playerId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld
                || request == null || !_items.Items.TryGetValue(request.VehicleId, out var item) || item.Body == null || !item.IsVehicle
                || DrivetrainStateRule(item.Id) == null || !_oilUsePolicy.Receive(request, playerId, item.RemoteOwner,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), Time.unscaledTime)) return false;
            try
            {
                var state = BuildVehicleDrivetrainWearState(item);
                if (state == null || !state.GearboxOilAvailable) throw new InvalidOperationException("Waiting for validated host gearbox oil.");
                PlayMakerFSM? target = null;
                foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (GearboxUseItem(fsm, true) == item)
                    { if (target != null) throw new InvalidOperationException("Ambiguous native automatic gearbox."); target = fsm; }
                if (target == null) throw new InvalidOperationException("Native automatic gearbox is missing.");
                if (!GuestEngineProtection.ApplyHostGearboxOil(target, request.Phase, state.GearboxWear, state.GearboxOilLevel, CurrentGearboxMount(item))) return false;
                _nextDrivetrainStatePoll = 0;
                SyncEventLog.Record("gearbox-oil-use-applied", "id=" + item.Id + " player=" + playerId + " sequence=" + request.Sequence);
                return true;
            }
            catch (Exception error)
            {
                if (Time.unscaledTime >= _nextOilUseError)
                { _nextOilUseError = Time.unscaledTime + 5f; SyncEventLog.Record("gearbox-oil-use-unavailable", error.Message); }
                return false;
            }
        }
        private static GameObject CurrentGearboxMount(SyncedItem item)
        {
            var rule = SyncCatalog.VehicleDrivetrainWear;
            var source = SyncCatalog.VehicleDifferentialSpeed;
            if (rule == null || source == null || rule.Targets.Count != 3)
                throw new InvalidOperationException("Native gearbox mount metadata is unavailable.");
            var producer = BindDifferentialSpeed(item, source);
            var mount = producer.Fsm.FsmVariables.FindFsmGameObject(rule.Targets[1].Variable)?.Value;
            return mount != null ? mount : throw new InvalidOperationException("Native gearbox mount is missing.");
        }
        private void ClearGearboxOilUse() { _oilUseSequences.Clear(); _oilUsePolicy.Clear(); _nextOilUseError = 0; }
    }
}
