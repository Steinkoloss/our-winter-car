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
        private const float ConditionProbeIntervalSeconds = 5f;
        private const float ConditionKeepAliveSeconds = 20f;
        private const float TirePressureScale = 100f;

        // Wheel index order used on the wire (FL, FR, RL, RR).
        private static readonly string[] WheelSuffixes = { "FL", "FR", "RL", "RR" };
        private static readonly byte[] WheelPunctureFlags =
        {
            VehicleCondition.FlagPunctureFL, VehicleCondition.FlagPunctureFR,
            VehicleCondition.FlagPunctureRL, VehicleCondition.FlagPunctureRR,
        };
        private static readonly byte[] WheelRimFlags =
        {
            VehicleCondition.FlagRimFL, VehicleCondition.FlagRimFR,
            VehicleCondition.FlagRimRL, VehicleCondition.FlagRimRR,
        };

        /// <summary>
        /// Drivetrain wear + per-wheel tire condition, owner-authoritative
        /// (COVERAGE-ROADMAP 2.2). The owner streams the condition; non-owners apply it
        /// (health/pressure written, PUNCTURE/RIM/FIXED fired) so a flat tire and wear agree.
        /// </summary>
        public void UpdateVehicleCondition(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            float now = Time.unscaledTime;

            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || !item.LocallyOwned) continue;
                EnsureConditionProbe(item);
                if (item.TirePressureVar == null && item.WheelConditionFsms == null) continue;
                OwnerBroadcastCondition(session, item, now);
            }
        }

        private static void EnsureConditionProbe(SyncedItem item)
        {
            if (item.Body == null || item.ConditionProbed) return;
            if (Time.unscaledTime < item.NextConditionProbeAt) return;
            item.NextConditionProbeAt = Time.unscaledTime + ConditionProbeIntervalSeconds;

            var wheels = new PlayMakerFSM?[4];
            var health = new FsmFloat?[4];

            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm == null) continue;
                string goName = fsm.gameObject.name;

                if (item.TirePressureVar == null && fsm.FsmName == "Data" && goName == "TirePressure")
                    item.TirePressureVar = fsm.FsmVariables.FindFsmFloat("Pressure");

                if (item.DrivetrainDamageVar == null && fsm.FsmName == "Damage" && goName == "GearboxDamage")
                    item.DrivetrainDamageVar = fsm.FsmVariables.FindFsmInt("DamageType");

                if (fsm.FsmName == "Condition" && goName.StartsWith("WHEELc_", System.StringComparison.Ordinal))
                {
                    for (int i = 0; i < WheelSuffixes.Length; i++)
                    {
                        if (goName.EndsWith(WheelSuffixes[i], System.StringComparison.Ordinal) && wheels[i] == null)
                        {
                            wheels[i] = fsm;
                            health[i] = fsm.FsmVariables.FindFsmFloat("Health");
                        }
                    }
                }
            }

            bool anyWheel = wheels[0] != null || wheels[1] != null || wheels[2] != null || wheels[3] != null;
            if (anyWheel) { item.WheelConditionFsms = wheels; item.WheelHealthVars = health; }

            // Probed "enough" once we found the pressure var or at least one wheel.
            if (item.TirePressureVar != null || anyWheel)
            {
                item.ConditionProbed = true;
                WinterMPPlugin.Log.LogInfo($"VehicleWorldSync: condition probe ready for '{item.Path}'.");
            }
        }

        private void OwnerBroadcastCondition(SessionManager session, SyncedItem item, float now)
        {
            byte pressure, drivetrain, flags = 0;
            byte hfl = 0, hfr = 0, hrl = 0, hrr = 0;
            try
            {
                pressure = ClampByte((item.TirePressureVar != null ? item.TirePressureVar.Value : 0f) * TirePressureScale);
                drivetrain = (byte)Mathf.Clamp(item.DrivetrainDamageVar != null ? item.DrivetrainDamageVar.Value : 0, 0, 255);
                if (item.WheelConditionFsms != null && item.WheelHealthVars != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var fsm = item.WheelConditionFsms[i];
                        byte health = ClampByte(item.WheelHealthVars[i] != null ? item.WheelHealthVars[i]!.Value : 0f);
                        switch (i)
                        {
                            case 0: hfl = health; break;
                            case 1: hfr = health; break;
                            case 2: hrl = health; break;
                            case 3: hrr = health; break;
                        }
                        string state = ReadWheelState(fsm);
                        if (state == "Flat friction") flags |= WheelPunctureFlags[i];
                        else if (state == "Rim friction") flags |= WheelRimFlags[i];
                    }
                }
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("VehicleWorldSync: condition read failed for " + item.Path + ": " + e.Message);
                return;
            }

            bool keepAlive = now >= item.NextConditionKeepAliveAt;
            bool changed = !item.HasSentCondition
                || item.LastCondPressure != pressure || item.LastCondDrivetrain != drivetrain
                || item.LastCondFlags != flags
                || item.LastCondHFL != hfl || item.LastCondHFR != hfr
                || item.LastCondHRL != hrl || item.LastCondHRR != hrr;
            if (!changed && !keepAlive) return;
            if (now < item.NextConditionTickAt && !changed) return;

            item.NextConditionTickAt = now + 1f;
            if (keepAlive) item.NextConditionKeepAliveAt = now + ConditionKeepAliveSeconds;
            item.HasSentCondition = true;
            item.LastCondPressure = pressure; item.LastCondDrivetrain = drivetrain; item.LastCondFlags = flags;
            item.LastCondHFL = hfl; item.LastCondHFR = hfr; item.LastCondHRL = hrl; item.LastCondHRR = hrr;

            session.SendWorldMessage(new VehicleCondition
            {
                VehicleId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = ++item.OutConditionSequence,
                TirePressure = pressure,
                DrivetrainDamage = drivetrain,
                HealthFL = hfl, HealthFR = hfr, HealthRL = hrl, HealthRR = hrr,
                Flags = flags,
            }, Channel.ReliableOrdered);
        }

        /// <summary>Host gate: only the authenticated current owner may drive a vehicle's condition.</summary>
        public bool TryAcceptGuestVehicleCondition(VehicleCondition message, byte playerId)
        {
            if (message.OwnerPlayerId != playerId
                || !_items.Items.TryGetValue(message.VehicleId, out var item)
                || !item.IsVehicle)
                return false;
            if (item.RemoteOwner != playerId && item.RemoteOwner != WorldSyncIds.NoOwner) return false;
            return true;
        }

        /// <summary>Apply an owner's condition onto a locally non-owned vehicle.</summary>
        public void ApplyVehicleCondition(VehicleCondition message)
        {
            if (!_items.Items.TryGetValue(message.VehicleId, out var item) || !item.IsVehicle || item.Body == null)
                return;
            if (item.LocallyOwned) return;
            EnsureConditionProbe(item);

            ushort diff = (ushort)(message.Sequence - item.LastConditionSequence);
            if (item.LastConditionSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            item.LastConditionSequence = message.Sequence;

            try
            {
                if (item.TirePressureVar != null) item.TirePressureVar.Value = message.TirePressure / TirePressureScale;
                if (item.DrivetrainDamageVar != null) item.DrivetrainDamageVar.Value = message.DrivetrainDamage;

                if (item.WheelConditionFsms != null && item.WheelHealthVars != null)
                {
                    byte[] healths = { message.HealthFL, message.HealthFR, message.HealthRL, message.HealthRR };
                    for (int i = 0; i < 4; i++)
                    {
                        if (item.WheelHealthVars[i] != null) item.WheelHealthVars[i]!.Value = healths[i];
                        ApplyWheelDiscrete(item, i, message.Flags);
                    }
                }
                item.AppliedCondFlags = message.Flags;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("VehicleWorldSync: condition apply failed for " + item.Path + ": " + e.Message);
            }
        }

        private void ApplyWheelDiscrete(SyncedItem item, int wheel, byte desiredFlags)
        {
            var fsm = item.WheelConditionFsms![wheel];
            if (fsm == null) return;

            bool wantPuncture = (desiredFlags & WheelPunctureFlags[wheel]) != 0;
            bool wantRim = (desiredFlags & WheelRimFlags[wheel]) != 0;
            bool hadPuncture = (item.AppliedCondFlags & WheelPunctureFlags[wheel]) != 0;
            bool hadRim = (item.AppliedCondFlags & WheelRimFlags[wheel]) != 0;

            try
            {
                if (wantRim && !hadRim) fsm.SendEvent("RIM");
                else if (wantPuncture && !hadPuncture) fsm.SendEvent("PUNCTURE");
                else if (!wantPuncture && !wantRim && (hadPuncture || hadRim)) fsm.SendEvent("FIXED");
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"VehicleWorldSync: wheel {WheelSuffixes[wheel]} event failed on {item.Path}: {e.Message}");
            }
        }

        private static string ReadWheelState(PlayMakerFSM? fsm)
        {
            if (fsm == null) return string.Empty;
            try { return fsm.Fsm != null ? (fsm.Fsm.ActiveStateName ?? string.Empty) : string.Empty; }
            catch { return string.Empty; }
        }

        private static byte ClampByte(float value) => (byte)Mathf.Clamp(value, 0f, 255f);
    }
}
