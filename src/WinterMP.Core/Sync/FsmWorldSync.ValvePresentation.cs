using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private void PrepareGuestValve(uint id, SyncedValve valve)
        {
            var fsm = valve.Fsm;
            valve.SavedStates = fsm.Fsm.States; valve.SavedTransitions = fsm.Fsm.GlobalTransitions;
            valve.SavedStart = fsm.Fsm.StartState; valve.SavedSetting = valve.Setting.Value; valve.SavedRotation = valve.Rotation.Value;
            valve.SavedPose = valve.Visual.localRotation; valve.SavedPick = valve.Pick.enabled;
            valve.Gate = new ReplicaBoltGate(); valve.Gate.SetAttachment(true, _partReceiptOrder);
            fsm.Fsm.States = new[] {
                ReplicaBoltState(fsm, "Set pos", () => PoseGuestValve(valve)),
                ReplicaBoltState(fsm, "Tight?", () => OnGuestValveTurn(id, valve, 1)),
                ReplicaBoltState(fsm, "Loose?", () => OnGuestValveTurn(id, valve, -1)),
                ReplicaBoltState(fsm, "On", () => UpdateValvePick(valve)),
                ReplicaBoltState(fsm, "Off", () => { if (valve.Pick != null) valve.Pick.enabled = false; }),
            };
            fsm.Fsm.StartState = "Set pos";
            if (!FsmHook.EnsureRemoteEntry(fsm, "Set pos")) throw new InvalidOperationException("Cannot pose guest valve.");
            FsmHook.FireRemoteEntry(fsm, "Set pos");
        }

        private void PoseGuestValve(SyncedValve valve)
        {
            if (valve.Gate?.Seeded == true && valve.Visual != null)
            {
                var rotation = valve.Visual.localEulerAngles; rotation.z = valve.Rotation.Value;
                valve.Visual.localEulerAngles = rotation;
            }
            UpdateValvePick(valve);
        }

        private void UpdateValvePick(SyncedValve valve)
        {
            if (valve.Pick == null) return;
            valve.Pick.enabled = !valve.Failed && valve.Gate?.Seeded == true && ValveReady(valve) && ValveOwnership(valve)
                && FsmVariables.GlobalVariables.FindFsmBool(SyncCatalog.ReplacementParts!["replicaRepairVariable"])?.Value == true;
        }

        private void OnGuestValveTurn(uint id, SyncedValve valve, int direction)
        {
            try
            {
                var session = SessionManager.Instance;
                if (_bridge.ApplyingRemote || session == null || session.IsHost || session.State != SessionState.Connected
                    || valve.Failed || valve.Gate?.Seeded != true || valve.Pick == null || !valve.Pick.enabled
                    || !ValveOwnership(valve) || valve.Data == null
                    || (NativePartIdentity.Phase(valve.Data) != NativePartPhase.Fitted && NativePartIdentity.Phase(valve.Data) != NativePartPhase.Loose)
                    || FsmVariables.GlobalVariables.FindFsmBool(SyncCatalog.ReplacementParts!["replicaRepairVariable"])?.Value != true
                    || !ValveAdjustmentPolicy.CanTurn(valve.Setting.Value, direction) || Time.unscaledTime < valve.NextRequestAt) return;
                valve.NextRequestAt = Time.unscaledTime + .1f;
                session.SendWorldMessage(new FsmRawEvent { NetId = id, EventName = direction > 0 ? "TIGHTEN" : "UNTIGHTEN" }, Channel.ReliableOrdered);
            }
            catch (Exception e) { FailValve(valve, e); }
            finally { if (valve.Fsm != null && valve.Fsm.enabled) FsmHook.FireRemoteEntry(valve.Fsm, "Set pos"); }
        }

        private static void RestoreGuestValve(SyncedValve valve)
        {
            if (valve.SavedStates == null || valve.Fsm == null) return;
            valve.Fsm.Fsm.States = valve.SavedStates; valve.Fsm.Fsm.GlobalTransitions = valve.SavedTransitions!;
            valve.Fsm.Fsm.StartState = valve.SavedStart;
            valve.Setting.Value = valve.SavedSetting; valve.Rotation.Value = valve.SavedRotation;
            if (valve.Visual != null) valve.Visual.localRotation = valve.SavedPose;
            if (valve.Pick != null) valve.Pick.enabled = valve.SavedPick;
            valve.Gate = null; valve.SavedStates = null;
            // Enter only the original visual state; Calc pos would write the array.
            if (valve.Fsm.enabled && valve.Fsm.gameObject.activeInHierarchy && FsmHook.EnsureRemoteEntry(valve.Fsm, "Set pos"))
                FsmHook.FireRemoteEntry(valve.Fsm, "Set pos");
            if (valve.Visual != null) valve.Visual.localRotation = valve.SavedPose;
        }

        private void RestoreGuestValves()
        {
            foreach (var valve in _valves.Values)
                try { RestoreGuestValve(valve); }
                catch (Exception e) { FailValve(valve, e); }
        }
    }
}
