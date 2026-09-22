using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private sealed class PendingValve
        {
            public ValveAdjustmentState State = null!;
            public ulong Order;
            public float ExpiresAt;
        }
        private readonly Dictionary<uint, SyncedValve> _valves = new Dictionary<uint, SyncedValve>();
        private readonly Dictionary<uint, PendingValve> _pendingValves = new Dictionary<uint, PendingValve>();
        private readonly Dictionary<uint, float> _hostValveReports = new Dictionary<uint, float>();

        private bool RegisterValve(PlayMakerFSM fsm)
        {
            if (!fsm.Fsm.Initialized || !fsm.Fsm.Started || fsm.ActiveStateName == "Init" || !BeginRegistration(fsm)) return false;
            if (!_bridge.PartIdentities.TryFsmId(fsm, ScenePath.Of(fsm.transform), out uint id)) return false;
            var valve = new SyncedValve { Fsm = fsm };
            try
            {
                if (_valves.ContainsKey(id) || _bolts.ContainsKey(id) || _parts.ContainsKey(id) || _doors.ContainsKey(id) || _buys.ContainsKey(id))
                    throw new InvalidOperationException("Valve identity collision.");
                BindValve(valve);
                if (SessionManager.Instance?.IsHost == false)
                    PrepareGuestValve(id, valve);
                else if (!FsmHook.EnsureRemoteEntry(fsm, "Set pos")
                    || !HookState(fsm, "Tight?", () => OnValveTurn(id, valve, 1))
                    || !HookState(fsm, "Loose?", () => OnValveTurn(id, valve, -1))
                    || !HookState(fsm, "Set pos", () => QueueValveReport(id)))
                    throw new InvalidOperationException("Cannot hook valve control.");
                _valves.Add(id, valve); MarkRegistered(fsm); QueueValveReport(id); return true;
            }
            catch (Exception e) { RemoveOwnedHooks(fsm); RestoreGuestValve(valve); FailValve(valve, e); MarkRegistered(fsm); return false; }
        }

        private void QueueValveReport(uint id)
        {
            if (SessionManager.Instance?.IsHost == true) _hostValveReports[id] = Time.unscaledTime + 2f;
        }

        private void OnValveTurn(uint id, SyncedValve valve, int direction)
        {
            try
            {
                var session = SessionManager.Instance;
                if (session == null || !session.IsHost)
                {
                    // Saved originals are isolated before replica creation. Never
                    // let an early-discovered original predict into its save array.
                    FsmHook.FireRemoteEntry(valve.Fsm, "Set pos"); return;
                }
                ValveRequire(!valve.Failed && ValveOwnership(valve)
                    && ValveAdjustmentPolicy.Read(ValveArray(valve), valve.Slot, out _), "live turn binding");
                ValveAdjustmentPolicy.Read(ValveArray(valve), valve.Slot, out float current);
                valve.Setting.Value = current;
                QueueValveReport(id);
            }
            catch (Exception e) { FailValve(valve, e); FsmHook.FireRemoteEntry(valve.Fsm, "Set pos"); }
        }

        private bool TryAcceptGuestValve(FsmRawEvent message, byte playerId, SyncedValve valve)
        {
            var session = SessionManager.Instance;
            if (session?.IsHost != true || (message.EventName != "TIGHTEN" && message.EventName != "UNTIGHTEN")
                || !ValveReady(valve) || valve.Gate != null
                || !IsGuestNearBolt(session, playerId, valve.Fsm.transform.position)) return false;
            try
            {
                ValveRequire(ValveOwnership(valve) && ValveAdjustmentPolicy.Read(ValveArray(valve), valve.Slot, out _), "live request binding");
                ValveAdjustmentPolicy.Read(ValveArray(valve), valve.Slot, out float current);
                QueueValveReport(message.NetId);
                if (!ValveAdjustmentPolicy.CanTurn(current, message.EventName == "TIGHTEN" ? 1 : -1)) return false;
                valve.Setting.Value = current;
                valve.Fsm.SendEvent(message.EventName);
                SyncEventLog.Record("valve-turn", message.NetId.ToString("X8") + " player=" + playerId + " " + message.EventName);
                return true;
            }
            catch (Exception e) { FailValve(valve, e); return false; }
        }

        internal ValveAdjustmentState? BuildValveState(uint id)
        {
            if (!_valves.TryGetValue(id, out var valve) || !ValveReady(valve)) return null;
            try
            {
                if (valve.Gate != null) return valve.Gate.Seeded ? valve.LastState : null;
                ValveRequire(ValveOwnership(valve) && ValveAdjustmentPolicy.Read(ValveArray(valve), valve.Slot, out _), "snapshot binding");
                ValveAdjustmentPolicy.Read(ValveArray(valve), valve.Slot, out float setting);
                return new ValveAdjustmentState { NetId = id, Setting = setting };
            }
            catch (Exception e) { FailValve(valve, e); return null; }
        }

        internal IEnumerable<ValveAdjustmentState> BuildValveStates()
        {
            var ids = new List<uint>(_valves.Keys); ids.Sort();
            foreach (uint id in ids) { var state = BuildValveState(id); if (state != null) yield return state; }
        }

        internal void OnRemoteValveState(ValveAdjustmentState state)
        {
            if (!ValveAdjustmentPolicy.Valid(state) || SessionManager.Instance?.IsHost != false) return;
            var pending = new PendingValve { State = new ValveAdjustmentState { NetId = state.NetId, Setting = state.Setting },
                Order = ++_partReceiptOrder, ExpiresAt = Time.unscaledTime + PendingTtlSeconds };
            if (ApplyValveState(pending)) _pendingValves.Remove(state.NetId);
            else if (_pendingValves.ContainsKey(state.NetId) || _pendingValves.Count < 512) _pendingValves[state.NetId] = pending;
        }

        private bool ApplyValveState(PendingValve pending)
        {
            if (!_valves.TryGetValue(pending.State.NetId, out var valve) || !ValveReady(valve)) return false;
            if (valve.Gate == null) return true;
            if (!valve.Gate.CanReceive(pending.Order)) return true;
            try
            {
                ValveRequire(ValveOwnership(valve), "guest display binding");
                // The guest's native head belongs to its own save. Only display
                // scratch and the isolated visual follow the host's setting.
                valve.Setting.Value = pending.State.Setting;
                valve.Rotation.Value = pending.State.Setting * ValveAdjustmentPolicy.RotationScale;
                valve.LastState = pending.State; valve.Gate.Received(pending.Order);
                PoseGuestValve(valve); return true;
            }
            catch (Exception e) { FailValve(valve, e); return true; }
        }

        private void ProcessValves()
        {
            foreach (uint id in new List<uint>(_pendingValves.Keys))
                if (ApplyValveState(_pendingValves[id]) || Time.unscaledTime >= _pendingValves[id].ExpiresAt) _pendingValves.Remove(id);
            var session = SessionManager.Instance;
            if (session?.IsHost == true)
            {
                foreach (uint id in new List<uint>(_hostValveReports.Keys))
                {
                    var state = BuildValveState(id);
                    if (state != null) { session.SendWorldMessage(state, Channel.ReliableOrdered); _hostValveReports.Remove(id); }
                    else if (Time.unscaledTime >= _hostValveReports[id]) _hostValveReports.Remove(id);
                }
            }
            else
                foreach (var pair in _valves)
                {
                    var valve = pair.Value;
                    if (valve.Gate == null || valve.Failed || valve.Fsm == null) continue;
                    try
                    {
                        UpdateValvePick(valve);
                        if (!valve.Gate.Seeded && ValveReady(valve))
                            _bridge.RequestObjectState(pair.Key);
                    }
                    catch (Exception e) { FailValve(valve, e); }
                }
        }
    }
}
