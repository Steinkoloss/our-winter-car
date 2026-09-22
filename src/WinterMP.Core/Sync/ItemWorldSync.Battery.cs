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
        private readonly BatteryReplica _batteryReplica = new BatteryReplica();
        private BatteryPublication _batteryPublication = new BatteryPublication();
        private bool _batteryCaptureFailed;
        private float _nextBatteryPoll, _nextBatteryKeepalive;

        internal BatteryState? ReadBatteryInputs() => _batteryReplica.Get();

        internal void OnBatteryState(BatteryState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return;
            var old = _batteryReplica.Get();
            if (!_batteryReplica.Receive(state)) { SyncEventLog.Record("battery-rejected", "revision=" + state.Revision); return; }
            if (old == null || old.Revision != state.Revision)
                SyncEventLog.Record("battery-received", "revision=" + state.Revision + " flags=" + state.Flags + " charge=" + state.Charge);
        }

        internal BatteryState? BuildBatteryState()
        {
            var session = SessionManager.Instance; var rule = SyncCatalog.GuestEngineInputs?.Battery;
            if (session == null || !session.IsHost || rule == null) return null;
            byte flags = 0; float charge = 0, chargeMax = 0;
            try
            {
                CaptureBattery(rule, out flags, out charge, out chargeMax); _batteryCaptureFailed = false;
            }
            catch (Exception error)
            {
                flags = 0; charge = 0; chargeMax = 0;
                if (!_batteryCaptureFailed)
                {
                    _batteryCaptureFailed = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: battery input unavailable: " + error.Message);
                    SyncEventLog.Record("battery-unavailable", error.Message);
                }
            }
            return _batteryPublication.Observe(flags, charge, chargeMax);
        }

        private static void CaptureBattery(EngineBatteryData rule, out byte flags, out float charge, out float chargeMax)
        {
            flags = 0; charge = 0; chargeMax = 0;
            var mount = GameObject.Find(rule.Path);
            if (mount == null || !mount.activeInHierarchy) return;
            if (ScenePath.Of(mount.transform) != rule.Path) throw new InvalidOperationException("Battery mount path changed.");
            PlayMakerFSM? data = null;
            foreach (var candidate in mount.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == rule.Fsm)
                {
                    if (data != null) throw new InvalidOperationException("Ambiguous battery mount.");
                    data = candidate;
                }
            if (data == null) throw new InvalidOperationException("Missing battery mount Data.");
            var installed = data.FsmVariables.FindFsmBool("Installed");
            var value = data.FsmVariables.FindFsmFloat("Charge");
            var maximum = data.FsmVariables.FindFsmFloat("ChargeMax");
            var active = data.FsmVariables.FindFsmGameObject("ActivePart");
            if (installed == null || value == null || maximum == null || active == null) throw new InvalidOperationException("Missing battery inputs.");
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started) return;
            if (!installed.Value)
            {
                if (data.ActiveStateName == rule.IdleState) flags = BatteryState.Available;
                return;
            }
            // Install 2 imports charge before physics finishes attaching the battery.
            // Neither an intermediate mount nor a stale ActivePart can power guests.
            var part = active.Value;
            if (Array.IndexOf(rule.ReadyStates, data.ActiveStateName) < 0 || part == null || !part.activeInHierarchy
                || part.transform.parent != mount.transform) return;
            PlayMakerFSM? partData = null;
            foreach (var candidate in part.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == rule.Fsm)
                { if (partData != null) throw new InvalidOperationException("Ambiguous battery part Data."); partData = candidate; }
            if (partData == null || partData.FsmVariables.FindFsmInt("AssemblyID")?.Value != 1
                || partData.FsmVariables.FindFsmFloat("Charge") == null || partData.FsmVariables.FindFsmFloat("ChargeMax") == null
                || partData.FsmVariables.FindFsmFloat("DischargeRate") == null) return;
            if (float.IsNaN(value.Value) || float.IsInfinity(value.Value) || float.IsNaN(maximum.Value) || float.IsInfinity(maximum.Value))
                throw new InvalidOperationException("Invalid battery charge or maximum.");
            flags = BatteryState.Available | BatteryState.Installed; charge = value.Value; chargeMax = maximum.Value;
        }

        private void ProcessBattery(SessionManager session)
        {
            if (!session.IsHost || Time.unscaledTime < _nextBatteryPoll) return;
            _nextBatteryPoll = Time.unscaledTime + .2f;
            var state = BuildBatteryState(); if (state == null) return;
            if (!_batteryPublication.NeedsBroadcast && Time.unscaledTime < _nextBatteryKeepalive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            if (_batteryPublication.NeedsBroadcast)
                SyncEventLog.Record("battery-published", "revision=" + state.Revision + " flags=" + state.Flags + " charge=" + state.Charge);
            _batteryPublication.MarkBroadcast(state.Revision); _nextBatteryKeepalive = Time.unscaledTime + 5f;
        }

        private void ClearBattery()
        {
            _batteryReplica.Clear(); _batteryPublication = new BatteryPublication(); _batteryCaptureFailed = false;
            _nextBatteryPoll = _nextBatteryKeepalive = 0;
        }
    }
}
