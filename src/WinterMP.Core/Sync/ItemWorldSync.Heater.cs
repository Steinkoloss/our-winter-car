using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly HeaterReplica _heaterReplica = new HeaterReplica();
        private HeaterPublication _heaterPublication = new HeaterPublication();
        private bool _heaterCaptureFailed, _rearWindowCaptureFailed;
        private float _nextHeaterPoll, _nextHeaterKeepalive;
        internal HeaterState? ReadHeaterInputs() => _heaterReplica.Get();

        internal void OnHeaterState(HeaterState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return;
            var old = _heaterReplica.Get();
            if (!_heaterReplica.Receive(state)) { SyncEventLog.Record("heater-rejected", "revision=" + state.Revision); return; }
            if (old == null || old.Revision != state.Revision)
                SyncEventLog.Record("heater-received", "revision=" + state.Revision + " flags=" + state.Flags + " wear=" + state.Wear + " rear=" + state.RearWindowFlags);
        }

        internal HeaterState? BuildHeaterState()
        {
            var session = SessionManager.Instance; var rule = SyncCatalog.GuestEngineInputs?.Heater;
            if (session == null || !session.IsHost || rule == null) return null;
            byte flags = 0; float wear = 0;
            try { CaptureHeater(rule, out flags, out wear); _heaterCaptureFailed = false; }
            catch (Exception error)
            {
                flags = 0; wear = 0;
                if (!_heaterCaptureFailed)
                {
                    _heaterCaptureFailed = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: heater input unavailable: " + error.Message);
                    SyncEventLog.Record("heater-unavailable", error.Message);
                }
            }
            byte rear = 0;
            try { rear = CaptureRearWindowHeater(rule.RearWindow); _rearWindowCaptureFailed = false; }
            catch (Exception error)
            {
                if (!_rearWindowCaptureFailed) SyncEventLog.Record("rear-window-heater-unavailable", error.Message);
                _rearWindowCaptureFailed = true;
            }
            return _heaterPublication.Observe(flags, wear, rear);
        }

        private static byte CaptureRearWindowHeater(RearWindowHeaterData rule)
        {
            var body = GameObject.Find(rule.Path); if (body == null || !body.activeInHierarchy) return 0;
            if (ScenePath.Of(body.transform) != rule.Path) throw new InvalidOperationException("Rear-window body source moved.");
            var data = HeaterDataFsm(body, rule.Fsm);
            var element = data.FsmVariables.FindFsmBool(rule.Variable);
            if (element == null) throw new InvalidOperationException("Missing native rear-window element input.");
            // BODY derives this option from its VIN after loading. Read settled
            // body/save states, without activating the guest's saved glass strips.
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started
                || Array.IndexOf(rule.ReadyStates, data.ActiveStateName) < 0) return 0;
            return (byte)(HeaterState.Available | (element.Value ? HeaterState.Installed : 0));
        }

        private static void CaptureHeater(EngineHeaterData rule, out byte flags, out float wear)
        {
            flags = 0; wear = 0;
            var mount = GameObject.Find(rule.Path); if (mount == null || !mount.activeInHierarchy) return;
            if (ScenePath.Of(mount.transform) != rule.Path) throw new InvalidOperationException("Heater mount path changed.");
            var data = HeaterDataFsm(mount, rule.Fsm);
            var installed = data.FsmVariables.FindFsmBool("Installed"); var value = data.FsmVariables.FindFsmFloat("Wear");
            var active = data.FsmVariables.FindFsmGameObject("ActivePart");
            if (installed == null || value == null || active == null) throw new InvalidOperationException("Missing heater inputs.");
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started) return;
            if (!installed.Value) { if (data.ActiveStateName == rule.IdleState) flags = HeaterState.Available; return; }
            // Installed becomes true before parenting/initial wear import finishes.
            // Only the settled native mount may power the guest blower.
            var part = active.Value;
            if (data.ActiveStateName != rule.ReadyState || part == null || !part.activeInHierarchy || part.transform.parent != mount.transform) return;
            var partData = HeaterDataFsm(part, rule.Fsm);
            if (partData.FsmVariables.FindFsmInt("AssemblyID")?.Value != 1 || partData.FsmVariables.FindFsmFloat("Wear") == null) return;
            if (float.IsNaN(value.Value) || float.IsInfinity(value.Value)) throw new InvalidOperationException("Invalid heater wear.");
            flags = HeaterState.Available | HeaterState.Installed; wear = value.Value;
        }

        private static PlayMakerFSM HeaterDataFsm(GameObject obj, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var candidate in obj.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == name) { if (found != null) throw new InvalidOperationException("Ambiguous heater Data."); found = candidate; }
            return found ?? throw new InvalidOperationException("Missing heater Data.");
        }

        private void ProcessHeater(SessionManager session)
        {
            if (!session.IsHost || Time.unscaledTime < _nextHeaterPoll) return;
            _nextHeaterPoll = Time.unscaledTime + .2f;
            var state = BuildHeaterState(); if (state == null) return;
            if (!_heaterPublication.NeedsBroadcast && Time.unscaledTime < _nextHeaterKeepalive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            if (_heaterPublication.NeedsBroadcast)
                SyncEventLog.Record("heater-published", "revision=" + state.Revision + " flags=" + state.Flags + " wear=" + state.Wear + " rear=" + state.RearWindowFlags);
            _heaterPublication.MarkBroadcast(state.Revision); _nextHeaterKeepalive = Time.unscaledTime + 5f;
        }

        private void ClearHeater()
        {
            _heaterReplica.Clear(); _heaterPublication = new HeaterPublication(); _heaterCaptureFailed = false; _rearWindowCaptureFailed = false;
            _nextHeaterPoll = _nextHeaterKeepalive = 0;
        }
    }
}
