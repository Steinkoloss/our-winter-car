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
        private readonly GearboxReplica _gearboxReplica = new GearboxReplica();
        private GearboxPublication _gearboxPublication = new GearboxPublication();
        private bool _gearboxCaptureFailed;
        private float _nextGearboxPoll, _nextGearboxKeepalive;

        internal void OnGearboxState(GearboxState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return;
            var old = _gearboxReplica.Get();
            if (!_gearboxReplica.Receive(state)) { SyncEventLog.Record("gearbox-rejected", "revision=" + state.Revision); return; }
            if (old == null || old.Revision != state.Revision)
                SyncEventLog.Record("gearbox-received", "revision=" + state.Revision + " flags=" + state.Flags + " type=" + state.Type);
        }

        internal GearboxState? BuildGearboxState()
        {
            var session = SessionManager.Instance; var rule = SyncCatalog.GuestEngineInputs?.Gearbox;
            if (session == null || !session.IsHost || rule == null) return null;
            byte flags = 0; int type = 0;
            try
            {
                CaptureGearbox(rule, out flags, out type); _gearboxCaptureFailed = false;
            }
            catch (Exception error)
            {
                flags = 0; type = 0;
                if (!_gearboxCaptureFailed)
                {
                    _gearboxCaptureFailed = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: gearbox input unavailable: " + error.Message);
                    SyncEventLog.Record("gearbox-unavailable", error.Message);
                }
            }
            return _gearboxPublication.Observe(flags, type);
        }

        private static void CaptureGearbox(EngineGearboxData rule, out byte flags, out int type)
        {
            flags = 0; type = 0;
            var mount = GameObject.Find(rule.Path);
            if (mount == null || !mount.activeInHierarchy) return;
            if (ScenePath.Of(mount.transform) != rule.Path) throw new InvalidOperationException("Gearbox mount path changed.");
            var data = GearboxDataFsm(mount, rule.Fsm);
            var installed = data.FsmVariables.FindFsmBool("Installed");
            var value = data.FsmVariables.FindFsmInt("Type");
            var active = data.FsmVariables.FindFsmGameObject("ActivePart");
            if (installed == null || value == null || active == null)
                throw new InvalidOperationException("Missing gearbox inputs.");
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started) return;
            if (!installed.Value)
            {
                if (data.ActiveStateName == rule.IdleState) { flags = GearboxState.Available; type = value.Value; }
                return;
            }
            // Installed is set before the native parent/body transition finishes.
            // Only the steady update state exposes the fitted transmission type.
            var part = active.Value;
            if (data.ActiveStateName != rule.ReadyState || part == null || !part.activeInHierarchy
                || part.transform.parent != mount.transform) return;
            var partData = GearboxDataFsm(part, rule.Fsm);
            if (partData.FsmVariables.FindFsmInt("AssemblyID")?.Value != 1
                || partData.FsmVariables.FindFsmInt("Type")?.Value != value.Value) return;
            flags = GearboxState.Available;
            type = value.Value;
        }

        private static PlayMakerFSM GearboxDataFsm(GameObject obj, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var candidate in obj.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == name)
                { if (found != null) throw new InvalidOperationException("Ambiguous gearbox Data."); found = candidate; }
            return found ?? throw new InvalidOperationException("Missing gearbox Data.");
        }

        private void ProcessGearbox(SessionManager session)
        {
            if (!session.IsHost || Time.unscaledTime < _nextGearboxPoll) return;
            _nextGearboxPoll = Time.unscaledTime + .2f;
            var state = BuildGearboxState(); if (state == null) return;
            if (!_gearboxPublication.NeedsBroadcast && Time.unscaledTime < _nextGearboxKeepalive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            if (_gearboxPublication.NeedsBroadcast)
                SyncEventLog.Record("gearbox-published", "revision=" + state.Revision + " flags=" + state.Flags + " type=" + state.Type);
            _gearboxPublication.MarkBroadcast(state.Revision); _nextGearboxKeepalive = Time.unscaledTime + 5f;
        }

        private void ClearGearbox()
        {
            _gearboxReplica.Clear(); _gearboxPublication = new GearboxPublication(); _gearboxCaptureFailed = false;
            _nextGearboxPoll = _nextGearboxKeepalive = 0;
        }
    }
}
