using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private readonly Dictionary<uint, ushort> _gearboxWearSequences = new Dictionary<uint, ushort>();
        // Native Reverse waits 0.6 seconds; this is a mod abuse budget with room for delivery jitter.
        private readonly VehicleCallbackPolicy _gearboxWearPolicy = new VehicleCallbackPolicy(2, 2);
        private float _nextGearboxWearError;

        internal void RecordGearboxWear(PlayMakerFSM fsm)
        {
            if (!CanObserveGearboxWear(fsm)) return;
            var item = GearboxUseItem(fsm, false); var session = SessionManager.Instance;
            if (item == null || session == null) return;
            _gearboxWearSequences.TryGetValue(item.Id, out ushort sequence); sequence = unchecked((ushort)(sequence + 1));
            _gearboxWearSequences[item.Id] = sequence;
            session.SendWorldMessage(new GearboxWearRequest { VehicleId = item.Id, PlayerId = session.LocalPlayerId, Sequence = sequence }, Channel.ReliableOrdered);
            SyncEventLog.Record("gearbox-wear-sent", "id=" + item.Id + " sequence=" + sequence);
        }
        internal bool OnHostGearboxWear(GearboxWearRequest request, byte playerId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld
                || request == null || !request.Valid || !_items.Items.TryGetValue(request.VehicleId, out var item)
                || item.Body == null || !item.IsVehicle || DrivetrainStateRule(item.Id) == null
                || !_gearboxWearPolicy.Receive(request.VehicleId, request.PlayerId, request.Sequence, playerId, item.RemoteOwner,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), Time.unscaledTime)) return false;
            try
            {
                var state = BuildVehicleDrivetrainWearState(item);
                if (state == null || state.Flags != VehicleDrivetrainWearState.Available)
                    throw new InvalidOperationException("Waiting for validated host gearbox wear.");
                PlayMakerFSM? target = null;
                foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (GearboxUseItem(fsm, false) == item)
                    { if (target != null) throw new InvalidOperationException("Ambiguous native gearbox failure consumer."); target = fsm; }
                if (target == null || !GuestEngineProtection.ApplyHostGearboxWear(target, state.GearboxWear, CurrentGearboxMount(item))) return false;
                _nextDrivetrainStatePoll = 0;
                SyncEventLog.Record("gearbox-wear-applied", "id=" + item.Id + " player=" + playerId + " sequence=" + request.Sequence);
                return true;
            }
            catch (Exception error)
            {
                if (Time.unscaledTime >= _nextGearboxWearError)
                { _nextGearboxWearError = Time.unscaledTime + 5f; SyncEventLog.Record("gearbox-wear-unavailable", error.Message); }
                return false;
            }
        }
        private void ClearGearboxWear()
        { _gearboxWearSequences.Clear(); _gearboxWearPolicy.Clear(); _nextGearboxWearError = 0; }
    }
}
