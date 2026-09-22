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
        private sealed class PendingStarterDraw
        {
            internal ushort Sequence, Count;
            internal byte Kind;
            internal float SendAt;
        }
        private readonly Dictionary<uint, PendingStarterDraw> _starterDraws = new Dictionary<uint, PendingStarterDraw>();
        private readonly StarterDrawPolicy _starterDrawPolicy = new StarterDrawPolicy();
        private float _nextStarterErrorAt;

        internal bool CanObserveStarterDraw(PlayMakerFSM fsm)
        {
            var session = SessionManager.Instance;
            return session != null && !session.IsHost && SyncCatalog.GuestEngineInputs?.Battery != null
                && FindStarterItem(fsm) != null;
        }

        private SyncedItem? FindStarterItem(PlayMakerFSM fsm)
        {
            foreach (var item in _items.Items.Values)
                if (item.IsVehicle && item.Body != null && ScenePath.Of(item.Body.transform) == "CORRIS"
                    && fsm.transform.IsChildOf(item.Body.transform)) return item;
            return null;
        }

        internal void RecordStarterDraw(PlayMakerFSM fsm, byte kind)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected || !GuestSaveGuard.ProtectWorld
                || SyncCatalog.GuestEngineInputs?.Battery == null) return;
            var item = FindStarterItem(fsm); if (item == null || !item.LocallyOwned) return;
            if (!_starterDraws.TryGetValue(item.Id, out var pending))
            { pending = new PendingStarterDraw(); _starterDraws.Add(item.Id, pending); }
            if (pending.Count != 0 && (pending.Kind != kind || pending.Count == StarterDrawRequest.MaximumCount))
                FlushStarterDraw(session, item);
            if (pending.Count == 0) { pending.Kind = kind; pending.SendAt = Time.unscaledTime + .1f; }
            pending.Count++;
        }

        internal void UpdateStarterDraws(SessionManager session)
        {
            foreach (var pair in _starterDraws)
            {
                if (!_items.Items.TryGetValue(pair.Key, out var item) || item.Body == null || !item.LocallyOwned
                    || session.IsHost || session.State != SessionState.Connected)
                { pair.Value.Count = 0; continue; }
                if (Time.unscaledTime >= pair.Value.SendAt) FlushStarterDraw(session, item);
            }
        }

        private void FlushStarterDraw(SessionManager session, SyncedItem item)
        {
            if (!_starterDraws.TryGetValue(item.Id, out var pending) || pending.Count == 0) return;
            var request = new StarterDrawRequest { VehicleId = item.Id, PlayerId = session.LocalPlayerId,
                Kind = pending.Kind, Count = pending.Count, Sequence = unchecked(++pending.Sequence) };
            pending.Count = 0;
            if (!session.IsHost && session.State == SessionState.Connected && item.LocallyOwned)
            {
                session.SendWorldMessage(request, Channel.ReliableOrdered);
                SyncEventLog.Record("starter-draw-sent", "vehicle=" + request.VehicleId + " sequence=" + request.Sequence
                    + " kind=" + request.Kind + " count=" + request.Count);
            }
        }

        internal bool OnHostStarterDraw(StarterDrawRequest request, byte playerId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.State != SessionState.Hosting || GuestSaveGuard.ProtectWorld
                || request == null || !_items.Items.TryGetValue(request.VehicleId, out var item) || !item.IsVehicle || item.Body == null
                || ScenePath.Of(item.Body.transform) != "CORRIS"
                || !_starterDrawPolicy.Receive(request, playerId, item.RemoteOwner,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), Time.unscaledTime)) return false;
            try
            {
                // Consume even rejected native work. A repaired/replaced battery
                // must not later pay for a request rejected while it was absent.
                if (_items.BuildBatteryState()?.Flags != (BatteryState.Available | BatteryState.Installed)) return false;
                var starter = FindHostStarter(item);
                return starter != null && GuestEngineProtection.ApplyHostStarterDraw(starter, request);
            }
            catch (Exception error)
            {
                if (Time.unscaledTime >= _nextStarterErrorAt)
                {
                    _nextStarterErrorAt = Time.unscaledTime + 5f;
                    WinterMPPlugin.Log.LogWarning("WorldSync: starter draw unavailable: " + error.Message);
                    SyncEventLog.Record("starter-draw-unavailable", error.Message);
                }
                return false;
            }
        }

        private static PlayMakerFSM? FindHostStarter(SyncedItem item)
        {
            var profile = SyncCatalog.GuestEngineProtection;
            if (profile == null) return null;
            PlayMakerFSM? starter = null;
            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                foreach (var writer in profile.Writers)
                    if (writer.Fsm == fsm.FsmName && writer.Path == ScenePath.Of(fsm.transform))
                    {
                        bool draw = false; foreach (var action in writer.Actions) if (action.StarterDraw != 0) draw = true;
                        if (!draw) continue;
                        if (starter != null) throw new InvalidOperationException("Ambiguous native starter.");
                        starter = fsm;
                    }
            return starter;
        }

        private void ClearStarterDraws()
        {
            ClearStarterWear();
            _starterDraws.Clear(); _starterDrawPolicy.Clear(); _nextStarterErrorAt = 0;
        }
    }
}
