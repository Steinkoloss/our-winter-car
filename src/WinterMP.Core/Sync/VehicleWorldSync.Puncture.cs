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
        private readonly Dictionary<uint, ushort> _wheelPunctureSequences = new Dictionary<uint, ushort>();
        private readonly VehicleCallbackPolicy _wheelPuncturePolicy = new VehicleCallbackPolicy(4, 1);

        internal void RecordWheelPuncture(PlayMakerFSM fsm, int wheel)
        {
            var session = _bridge.Session;
            if (session == null || session.IsHost || session.State != SessionState.Connected || !GuestSaveGuard.ProtectWorld) return;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || !item.LocallyOwned || !_items.IsLocalPlayerDriving(item)
                    || WheelHealthRule(item.Id) == null || !fsm.transform.IsChildOf(item.Body.transform)) continue;
                var state = ReadWheelHealthState(item.Id);
                if (state == null || !state.HasWheel(wheel) || state.Health(wheel) <= 0 || state.Epoch(wheel) == 0) return;
                _wheelPunctureSequences.TryGetValue(item.Id, out ushort sequence); sequence = unchecked((ushort)(sequence + 1));
                _wheelPunctureSequences[item.Id] = sequence;
                session.SendWorldMessage(new WheelPunctureRequest { VehicleId = item.Id, PlayerId = session.LocalPlayerId,
                    Wheel = (byte)wheel, Epoch = state.Epoch(wheel), Sequence = sequence }, Channel.ReliableOrdered);
                SyncEventLog.Record("wheel-puncture-sent", "id=" + item.Id + " wheel=" + wheel + " epoch=" + state.Epoch(wheel));
                return;
            }
        }

        internal bool OnHostWheelPuncture(WheelPunctureRequest request, byte playerId)
        {
            var session = _bridge.Session;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld
                || request == null || !request.Valid || !_items.Items.TryGetValue(request.VehicleId, out var item)
                || !item.IsVehicle || item.Body == null || !item.RemoteIsDriver
                || !(Time.unscaledTime - item.LastRemoteAt >= 0 && Time.unscaledTime - item.LastRemoteAt <= 2)
                || !_wheelPuncturePolicy.Receive(item.Id, request.PlayerId, request.Sequence, playerId, item.RemoteOwner,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), Time.unscaledTime)) return false;
            bool alive = false;
            foreach (var player in session.Players) if (player.PlayerId == playerId) alive = !player.IsDead;
            if (!alive) return false;
            try
            {
                var state = BuildVehicleWheelHealthState(item); var rule = WheelHealthRule(item.Id);
                if (state == null || rule == null || !request.Matches(state)) return false;
                foreach (var source in rule.Sources)
                    if (source.Wheel == request.Wheel)
                    {
                        var fsm = FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), source.Path, "Condition");
                        if (!GuestEngineProtection.ApplyHostWheelPuncture(fsm, source, item.Body)) return false;
                        _nextWheelHealthPoll = 0;
                        var result = BuildVehicleWheelHealthState(item);
                        if (result != null) PublishWheelHealthState(session, _wheelHealthPublications[item.Id], result, false);
                        SyncEventLog.Record("wheel-puncture-applied", "id=" + item.Id + " player=" + playerId + " wheel=" + request.Wheel);
                        return true;
                    }
            }
            catch (Exception error) { NoteWheelHealthFailure(item.Id, error); }
            return false;
        }
    }
}
