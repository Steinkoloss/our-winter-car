using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private static bool EnsureConditionProbe(SyncedItem item)
        {
            if (item.Body == null) return false;
            if (item.ClaimedConditionBody != null && !ReferenceEquals(item.ClaimedConditionBody, item.Body)) ClearClaimedCondition(item);
            if (ReferenceEquals(item.ConditionProbeBody, item.Body) && Time.unscaledTime < item.NextConditionProbeAt)
                return UpdateConditionReadiness(item, false);
            item.NextConditionProbeAt = Time.unscaledTime + ConditionProbeIntervalSeconds;
            var wheels = new PlayMakerFSM?[4]; var health = new FsmFloat?[4]; var counts = new int[4];
            PlayMakerFSM? pressure = null, drivetrain = null; int pressureCount = 0, drivetrainCount = 0;
            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm == null) continue;
                string name = fsm.gameObject.name;
                if (fsm.FsmName == "Data" && name == "TirePressure") { pressure = fsm; pressureCount++; }
                if (fsm.FsmName == "Damage" && name == "GearboxDamage") { drivetrain = fsm; drivetrainCount++; }
                if (fsm.FsmName != "Condition") continue;
                for (int i = 0; i < 4; i++)
                    if (name == "WHEELc_" + WheelSuffixes[i]) { wheels[i] = fsm; counts[i]++; }
            }
            if (pressureCount != 1) pressure = null;
            if (drivetrainCount != 1) drivetrain = null;
            var pressureVar = pressure?.FsmVariables.FindFsmFloat("Pressure");
            var drivetrainVar = drivetrain?.FsmVariables.FindFsmInt("DamageType");
            bool changed = !ReferenceEquals(item.ConditionProbeBody, item.Body) || item.TirePressureFsm != pressure
                || item.DrivetrainDamageFsm != drivetrain || !ReferenceEquals(item.TirePressureVar, pressureVar)
                || !ReferenceEquals(item.DrivetrainDamageVar, drivetrainVar);
            for (int i = 0; i < 4; i++)
            {
                if (counts[i] != 1) wheels[i] = null;
                health[i] = wheels[i]?.FsmVariables.FindFsmFloat("Health");
                changed |= item.WheelConditionFsms == null || item.WheelConditionFsms[i] != wheels[i]
                    || item.WheelHealthVars == null || !ReferenceEquals(item.WheelHealthVars[i], health[i]);
            }
            item.ConditionProbeBody = item.Body;
            item.TirePressureFsm = pressure; item.TirePressureVar = pressureVar;
            item.DrivetrainDamageFsm = drivetrain; item.DrivetrainDamageVar = drivetrainVar;
            item.WheelConditionFsms = wheels; item.WheelHealthVars = health;
            return UpdateConditionReadiness(item, changed);
        }

        private static bool UpdateConditionReadiness(SyncedItem item, bool changed)
        {
            byte ready = ConditionAvailability(item);
            if (ready != item.ConditionReadyMask)
            {
                changed = true;
                SyncEventLog.Record("vehicle-condition-availability", item.Path + ": " + item.ConditionReadyMask + " -> " + ready);
            }
            item.ConditionReadyMask = ready;
            item.ConditionNeedsApply |= changed;
            return changed;
        }

        private void UpdateConditionBindings(SyncedItem item)
        {
            EnsureConditionProbe(item);
            if (!item.ConditionNeedsApply) return;
            var session = _bridge.Session;
            if (session == null || (session.IsHost ? session.State != SessionState.Hosting : session.State != SessionState.Connected)) return;
            bool local = item.LocallyOwned || _items.IsLocalPlayerDriving(item);
            var condition = item.ApplyingVehicleCondition ?? item.AcceptedVehicleCondition;
            if (!VehicleConditionStreamPolicy.CanPresent(condition, item.Id, local, item.RemoteOwner))
            {
                condition = item.ApplyingVehicleCondition == null && ReferenceEquals(item.ParkedConditionBody, item.Body)
                    ? item.ParkedVehicleCondition : null;
                if (!VehicleConditionStreamPolicy.CanPresentParked(condition, item.Id, local, item.RemoteOwner)) return;
            }
            var previous = item.ApplyingVehicleCondition;
            try
            {
                // Discovery also runs during capture/checksum reads. Keep the
                // pending application until an observer update can reconcile it.
                item.ApplyingVehicleCondition = condition;
                ApplyConditionValues(item, condition!);
                ApplyNativeTirePressure(item, received: true);
            }
            finally { item.ApplyingVehicleCondition = previous; }
        }

        private void ApplyConditionValues(SyncedItem item, VehicleCondition message)
        {
            byte ready = (byte)(ConditionAvailability(item) & message.Availability);
            if ((ready & VehicleCondition.AvailablePressure) != 0)
                item.TirePressureVar!.Value = VehicleConditionPolicy.DecodeNativeTirePressure(message.TirePressure);
            if ((ready & VehicleCondition.AvailableDrivetrain) != 0)
                item.DrivetrainDamageVar!.Value = message.DrivetrainDamage;
            byte[] healths = { message.HealthFL, message.HealthFR, message.HealthRL, message.HealthRR };
            bool pending = false;
            for (int i = 0; i < 4; i++)
            {
                if ((ready & (VehicleCondition.AvailableFL << i)) == 0) continue;
                item.WheelHealthVars![i]!.Value = healths[i];
                pending |= !ApplyWheelDiscrete(item, i, message.Flags);
            }
            item.ConditionNeedsApply = pending;
        }

        private static bool ConditionFsmLive(SyncedItem item, PlayMakerFSM? fsm) =>
            item.Body != null && fsm != null && fsm.enabled && fsm.gameObject.activeInHierarchy
            && fsm.transform.IsChildOf(item.Body.transform);

        private static bool ConditionFloat(FsmFloat? value)
        {
            if (value == null || float.IsNaN(value.Value) || float.IsInfinity(value.Value)) return false;
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables) if (ReferenceEquals(global, value)) return false;
            return true;
        }

        private static byte ConditionAvailability(SyncedItem item)
        {
            byte available = 0;
            if (ConditionFsmLive(item, item.TirePressureFsm) && item.TirePressureFsm!.FsmName == "Data"
                && ConditionFloat(item.TirePressureVar) && ReferenceEquals(item.TirePressureVar, item.TirePressureFsm.FsmVariables.FindFsmFloat("Pressure")))
                available |= VehicleCondition.AvailablePressure;
            if (ConditionFsmLive(item, item.DrivetrainDamageFsm) && item.DrivetrainDamageFsm!.FsmName == "Damage"
                && item.DrivetrainDamageVar != null && ReferenceEquals(item.DrivetrainDamageVar, item.DrivetrainDamageFsm.FsmVariables.FindFsmInt("DamageType")))
            {
                bool global = false;
                foreach (var scalar in FsmVariables.GlobalVariables.IntVariables) global |= ReferenceEquals(scalar, item.DrivetrainDamageVar);
                if (!global) available |= VehicleCondition.AvailableDrivetrain;
            }
            if (item.WheelConditionFsms != null && item.WheelHealthVars != null)
                for (int i = 0; i < 4; i++)
                {
                    var fsm = item.WheelConditionFsms[i]; string state = ReadWheelState(fsm);
                    // Startup and branch states have not established a tyre input yet.
                    if (ConditionFsmLive(item, fsm) && fsm!.FsmName == "Condition"
                        && (state == "State 1" || state == "Flat friction" || state == "Rim friction")
                        && ConditionFloat(item.WheelHealthVars[i])
                        && GuestEngineProtection.SafeWheelHealthOutput(fsm, item.WheelHealthVars[i]))
                        available |= (byte)(VehicleCondition.AvailableFL << i);
                }
            return available;
        }

        private VehicleCondition? TryReadConditionForChecksum(SyncedItem item)
        {
            var parked = ReadHostParkedCondition(item);
            if (parked != null) return parked;
            var state = TryReadConditionState(item);
            if (state == null || state.Availability == 0) return state;
            var session = _bridge.Session;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return state;
            bool local = item.LocallyOwned || _items.IsLocalPlayerDriving(item);
            var accepted = item.AcceptedVehicleCondition;
            if (!VehicleConditionStreamPolicy.CanPresent(accepted, item.Id, local, item.RemoteOwner))
            {
                accepted = ReferenceEquals(item.ParkedConditionBody, item.Body) ? item.ParkedVehicleCondition : null;
                if (!VehicleConditionStreamPolicy.CanPresentParked(accepted, item.Id, local, item.RemoteOwner)) return state;
            }
            // Keep checking native drift in declared fields, but local data that
            // the authority explicitly withdrew must not request endless repair.
            state.Availability &= accepted!.Availability;
            return state;
        }

        private static VehicleCondition? TryReadConditionState(SyncedItem item)
        {
            if (!item.IsVehicle || item.Body == null) return null;
            try
            {
                EnsureConditionProbe(item);
                var state = new VehicleCondition { VehicleId = item.Id, Availability = ConditionAvailability(item) };
                if (state.HasPressure) state.TirePressure = VehicleConditionPolicy.EncodeNativeTirePressure(item.TirePressureVar!.Value);
                if (state.HasDrivetrain) state.DrivetrainDamage = (byte)Mathf.Clamp(item.DrivetrainDamageVar!.Value, 0, 255);
                for (int i = 0; i < 4; i++)
                {
                    if (!state.HasWheel(i)) continue;
                    byte health = ClampByte(item.WheelHealthVars![i]!.Value);
                    switch (i) { case 0: state.HealthFL = health; break; case 1: state.HealthFR = health; break;
                        case 2: state.HealthRL = health; break; case 3: state.HealthRR = health; break; }
                    string native = ReadWheelState(item.WheelConditionFsms![i]);
                    if (native == "Flat friction") state.Flags |= WheelPunctureFlags[i];
                    else if (native == "Rim friction") state.Flags |= WheelRimFlags[i];
                }
                var claimed = ReadClaimedCondition(item);
                if (claimed != null) VehicleConditionClaimPolicy.ApplyToCapture(state, claimed);
                return state;
            }
            catch (Exception error)
            {
                WinterMPPlugin.Log.LogDebug("VehicleWorldSync: condition capture unavailable for " + item.Path + ": " + error.Message);
                return null;
            }
        }
    }
}
