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
        private sealed class PendingStarterWear
        {
            internal ushort Sequence;
            internal float Seconds, SendAt;
        }
        private readonly Dictionary<uint, PendingStarterWear> _starterWear = new Dictionary<uint, PendingStarterWear>();
        private readonly StarterWearPolicy _starterWearPolicy = new StarterWearPolicy();
        private float _nextStarterWearErrorAt;

        internal void RecordStarterWear(PlayMakerFSM fsm, float seconds)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected || !GuestSaveGuard.ProtectWorld
                || !CanObserveStarterDraw(fsm) || !(seconds > 0 && seconds <= StarterWearRequest.MaximumSeconds)) return;
            var item = FindStarterItem(fsm); if (item == null || !item.LocallyOwned) return;
            if (!_starterWear.TryGetValue(item.Id, out var pending))
            { pending = new PendingStarterWear(); _starterWear.Add(item.Id, pending); }
            if (pending.Seconds + seconds > StarterWearRequest.MaximumSeconds) FlushStarterWear(session, item);
            if (pending.Seconds == 0) pending.SendAt = Time.unscaledTime + .1f;
            pending.Seconds += seconds;
        }

        internal void UpdateStarterWear(SessionManager session)
        {
            foreach (var pair in _starterWear)
            {
                if (!_items.Items.TryGetValue(pair.Key, out var item) || item.Body == null || !item.LocallyOwned
                    || session.IsHost || session.State != SessionState.Connected)
                { pair.Value.Seconds = 0; continue; }
                if (Time.unscaledTime >= pair.Value.SendAt) FlushStarterWear(session, item);
            }
        }

        private void FlushStarterWear(SessionManager session, SyncedItem item)
        {
            if (!_starterWear.TryGetValue(item.Id, out var pending) || pending.Seconds == 0) return;
            var request = new StarterWearRequest { VehicleId = item.Id, PlayerId = session.LocalPlayerId,
                Seconds = pending.Seconds, Sequence = unchecked(++pending.Sequence) };
            pending.Seconds = 0;
            if (!session.IsHost && session.State == SessionState.Connected && item.LocallyOwned)
            {
                session.SendWorldMessage(request, Channel.ReliableOrdered);
                SyncEventLog.Record("starter-wear-sent", "vehicle=" + request.VehicleId + " sequence=" + request.Sequence + " seconds=" + request.Seconds);
            }
        }

        internal bool OnHostStarterWear(StarterWearRequest request, byte playerId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld
                || request == null || !_items.Items.TryGetValue(request.VehicleId, out var item) || !item.IsVehicle || item.Body == null
                || ScenePath.Of(item.Body.transform) != "CORRIS"
                || !_starterWearPolicy.Receive(request, playerId, item.RemoteOwner,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), Time.unscaledTime)) return false;
            try
            {
                if (_items.BuildBatteryState()?.Flags != (BatteryState.Available | BatteryState.Installed)) return false;
                var starter = FindHostStarter(item);
                return starter != null && GuestEngineProtection.ApplyHostStarterWear(starter, request);
            }
            catch (Exception error)
            {
                if (Time.unscaledTime >= _nextStarterWearErrorAt)
                {
                    _nextStarterWearErrorAt = Time.unscaledTime + 5f;
                    WinterMPPlugin.Log.LogWarning("WorldSync: starter wear unavailable: " + error.Message);
                    SyncEventLog.Record("starter-wear-unavailable", error.Message);
                }
                return false;
            }
        }

        private void ClearStarterWear()
        {
            _starterWear.Clear(); _starterWearPolicy.Clear(); _nextStarterWearErrorAt = 0;
        }
    }
}
