using System;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        // ------------------------------------------------------------------ engine & ignition

        /// <summary>
        /// Stream ignition/engine state for vehicles we are driving *or* whose engine
        /// is running locally (parked idling — pose ownership may already be released).
        /// </summary>
        public void UpdateVehicleStates(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            float now = Time.unscaledTime;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null) continue;
                if (!item.LocallyOwned && !HasLocalIgnitionActivity(item)) continue;
                SendVehicleState(session, item, now);
            }
        }

        private static bool HasLocalIgnitionActivity(SyncedItem item)
        {
            EnsureVehicleSystemsProbe(item);
            if (!item.SystemsReady) return false;
            return ReadAccOn(item) || ReadEngineRevs(item) > EngineRunningRevs;
        }

        private void SendVehicleState(SessionManager session, SyncedItem item, float now)
        {
            if (now < item.NextVehicleStateAt) return;
            item.NextVehicleStateAt = now + 1f / VehicleStateRateHz;

            EnsureVehicleSystemsProbe(item);
            if (!item.SystemsReady) return;

            float revs = ReadBestRpm(item);
            bool engineOn = revs > EngineRunningRevs;
            bool accOn = ReadAccOn(item) || engineOn;
            float speedKmh = item.GaugeSpeedVar != null ? item.GaugeSpeedVar.Value : 0f;

            byte flags = 0;
            if (engineOn) flags |= VehicleState.FlagEngineOn;
            if (accOn) flags |= VehicleState.FlagAccOn;
            if (ReadBlinkerLeft(item)) flags |= VehicleState.FlagBlinkerLeft;
            if (ReadBlinkerRight(item)) flags |= VehicleState.FlagBlinkerRight;
            if (ReadHazardOn(item)) flags |= VehicleState.FlagHazard;

            if (!item.LoggedEngineSend && (engineOn || accOn))
            {
                item.LoggedEngineSend = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: streaming '{item.Path}' ignition — engine={engineOn} ACC={accOn} revs={revs:0} speed={speedKmh:0.0} km/h.");
            }

            session.SendWorldMessage(new VehicleState
            {
                VehicleId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = ++item.OutVehicleStateSequence,
                Flags = flags,
                Rpm = (ushort)Mathf.Clamp(revs, 0f, ushort.MaxValue),
                SpeedTenthsKmh = (ushort)Mathf.Clamp(speedKmh * 10f, 0f, ushort.MaxValue),
                FuelLevel = ReadFuelLevelByte(item),
                CoolantTemp = ReadCoolantTempByte(item),
            }, Channel.UnreliableSequenced);
        }

        private static float ReadEngineRevs(SyncedItem item)
        {
            return item.EngineRevsVar != null ? item.EngineRevsVar.Value : 0f;
        }

        private static float ReadBestRpm(SyncedItem item)
        {
            // The dashboard's RPM float is closest to what the driver sees; fall
            // back to engine sim variables when the gauge object is inactive.
            if (item.GaugeRpmVar != null && item.GaugeRpmVar.Value > 1f)
                return item.GaugeRpmVar.Value;
            return ReadEngineRevs(item);
        }

        private static bool ReadAccOn(SyncedItem item)
        {
            if (item.ElectricityPowerFsm == null) return false;

            var acc = item.ElectricityPowerFsm.FsmVariables.FindFsmBool("ACC");
            if (acc != null && acc.Value) return true;

            try
            {
                if (item.ElectricityPowerFsm.Fsm.ActiveStateName == "ON") return true;
            }
            catch
            {
                // FSM not initialized yet.
            }

            return false;
        }

        public void OnRemoteVehicleState(VehicleState message)
        {
            if (!_items.Items.TryGetValue(message.VehicleId, out var item) || item.Body == null || !item.IsVehicle)
                return;

            // Join/resync snapshots carry SnapshotSequence and bypass the live-stream
            // dedup (otherwise a fresh guest drops the Sequence-0 snapshot and never
            // sees a parked car's engine/electrics). The sentinel does not advance the
            // baseline, so the live stream's dedup is unaffected.
            if (message.Sequence != VehicleState.SnapshotSequence)
            {
                ushort diff = (ushort)(message.Sequence - item.LastVehicleStateSequence);
                if (diff == 0 || diff > short.MaxValue)
                {
                    ConnectionQuality.Instance.NoteUnreliableDropped();
                    return;
                }

                ConnectionQuality.Instance.NoteUnreliableReceived();
                item.LastVehicleStateSequence = message.Sequence;
            }

            bool electricsOn = message.AccOn || message.EngineOn;
            bool electricsChanged = electricsOn != item.RemoteElectricsApplied;
            item.RemoteEngineOn = message.EngineOn;
            item.RemoteAccOn = message.AccOn;
            item.RemoteRpm = message.Rpm;
            item.RemoteSpeedKmh = message.SpeedTenthsKmh * 0.1f;
            item.RemoteFuelLevel = message.FuelLevel;
            item.RemoteCoolantTemp = message.CoolantTemp;
            item.RemoteEngineUntil = Time.unscaledTime + EngineAudioHoldSeconds;

            bool blinkersChanged = message.BlinkerLeft != item.RemoteBlinkerLeft
                || message.BlinkerRight != item.RemoteBlinkerRight;
            bool hazardChanged = message.HazardOn != item.RemoteHazard;
            item.RemoteBlinkerLeft = message.BlinkerLeft;
            item.RemoteBlinkerRight = message.BlinkerRight;
            item.RemoteHazard = message.HazardOn;
            item.RemoteDashDirty = true;

            if (!item.LocallyOwned && electricsChanged)
                ApplyRemoteElectricity(item, electricsOn);

            if (!item.LocallyOwned && (blinkersChanged || hazardChanged))
                ApplyRemoteLights(item, message.BlinkerLeft, message.BlinkerRight, message.HazardOn);
        }

        /// <summary>
        /// Replay the Electricity :: Power FSM into ON/OFF on the remote copy so
        /// dash lights, gauge power and child simulators wake the same way as
        /// locally — SetActive on PowerON alone does not run the state's actions.
        /// </summary>
        private static void ApplyRemoteElectricity(SyncedItem item, bool on)
        {
            EnsureVehicleSystemsProbe(item);
            item.RemoteElectricsApplied = on;
            if (on) item.RemoteDashDirty = true; // power restored — re-present gauges/lights once

            if (item.ElectricityPowerFsm == null)
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: no Electricity FSM on '{item.Path}' — remote ignition skipped.");
                return;
            }

            string state = on ? "ON" : "OFF";
            if (!FsmHook.EnsureRemoteEntry(item.ElectricityPowerFsm, state))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: Electricity '{state}' missing on '{item.Path}'.");
                return;
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: remote electricity '{item.Path}' -> {state}.");
            FsmHook.FireRemoteEntry(item.ElectricityPowerFsm, state);
        }

        private static void ApplyRemoteGauges(SyncedItem item)
        {
            if (item.GaugeSpeedVar != null)
                item.GaugeSpeedVar.Value = item.RemoteSpeedKmh;
            if (item.GaugeSpeedAngleVar != null)
                item.GaugeSpeedAngleVar.Value = SpeedAngleForKmh(item.RemoteSpeedKmh);

            if (item.GaugeRpmVar != null)
                item.GaugeRpmVar.Value = item.RemoteRpm;

            if (item.GaugeTachRevsVar != null)
                item.GaugeTachRevsVar.Value = item.RemoteRpm;
            if (item.GaugeTachRotationVar != null)
                item.GaugeTachRotationVar.Value = TachRotationForRpm(item.RemoteRpm);

            if (item.GaugeTachNeedle != null)
            {
                var euler = item.GaugeTachNeedle.transform.localEulerAngles;
                euler.z = TachRotationForRpm(item.RemoteRpm);
                item.GaugeTachNeedle.transform.localEulerAngles = euler;
            }

            float fuel01 = item.RemoteFuelLevel / 255f;
            if (item.GaugeFuelLevelVar != null)
                item.GaugeFuelLevelVar.Value = fuel01;

            if (item.GaugeFuelAngleVar != null)
                item.GaugeFuelAngleVar.Value = FuelAngleForLevel(fuel01);

            float coolantC = item.RemoteCoolantTemp / 255f * CoolantTempMaxC;
            if (item.GaugeCoolantVar != null)
                item.GaugeCoolantVar.Value = coolantC;
            if (item.GaugeCoolantAngleVar != null)
                item.GaugeCoolantAngleVar.Value = CoolantAngleForTemp(coolantC);
        }

        private static void ApplyRemoteLights(SyncedItem item, bool left, bool right, bool hazard)
        {
            EnsureVehicleSystemsProbe(item);

            if (hazard)
            {
                ApplyRemoteHazardButton(item, true);
                if (item.BlinkerHazardsVar != null)
                    item.BlinkerHazardsVar.Value = true;

                if (item.TurnSignalsFsm != null)
                {
                    try { item.TurnSignalsFsm.SendEvent("BLINKERS"); }
                    catch { /* FSM not ready yet */ }
                }

                return;
            }

            ApplyRemoteHazardButton(item, false);
            if (item.BlinkerHazardsVar != null)
                item.BlinkerHazardsVar.Value = false;

            if (item.TurnSignalStalkFsm != null)
            {
                string state = right ? "On" : left ? "On 2" : "Check player";
                // Guarded: the turn-signal stalk is a catalog SyncedControl, so its state-entry
                // hook would otherwise re-broadcast this remote apply as a local change (echo).
                if (FsmHook.EnsureRemoteEntry(item.TurnSignalStalkFsm, state))
                    FireKnobCommitState(item.TurnSignalStalkFsm, state);
                return;
            }

            if (item.TurnSignalsFsm == null) return;

            if (item.BlinkerLeftVar != null) item.BlinkerLeftVar.Value = left;
            if (item.BlinkerRightVar != null) item.BlinkerRightVar.Value = right;

            try
            {
                if (left && !right)
                    item.TurnSignalsFsm.SendEvent("LEFT");
                else if (right && !left)
                    item.TurnSignalsFsm.SendEvent("RIGHT");
                else
                    item.TurnSignalsFsm.SendEvent("OFF");
            }
            catch
            {
                // FSM not ready yet.
            }
        }

        private static void ApplyRemoteHazardButton(SyncedItem item, bool on)
        {
            if (item.HazardButtonFsm == null) return;

            string state = on ? "On" : "Off";
            if (!FsmHook.EnsureRemoteEntry(item.HazardButtonFsm, state)) return;
            // Guarded: hazard button is a catalog SyncedControl (echo guard — see FireKnobCommitState).
            FireKnobCommitState(item.HazardButtonFsm, state);
            SetHazardVisual(item.HazardButtonFsm, on);
        }

        private static void UpdateRemoteLightPresentation(SyncedItem item)
        {
            if (item.HazardButtonFsm != null)
                SetHazardVisual(item.HazardButtonFsm, item.RemoteHazard);

            if (item.BlinkerHazardsVar != null)
                item.BlinkerHazardsVar.Value = item.RemoteHazard;

            if (item.RemoteHazard) return;

            if (item.BlinkerLeftVar != null)
                item.BlinkerLeftVar.Value = item.RemoteBlinkerLeft;
            if (item.BlinkerRightVar != null)
                item.BlinkerRightVar.Value = item.RemoteBlinkerRight;
        }

        private static void SetHazardVisual(PlayMakerFSM fsm, bool on)
        {
            var hazardsOn = fsm.FsmVariables.FindFsmBool("HazardsOn");
            if (hazardsOn != null)
                hazardsOn.Value = on;

            var buttonOn = fsm.FsmVariables.FindFsmBool("ButtonOn");
            if (buttonOn != null)
                buttonOn.Value = on;

            var light = fsm.FsmVariables.GetFsmGameObject("Light");
            if (light != null && light.Value != null)
                light.Value.SetActive(on);
        }

        private static bool ReadBlinkerLeft(SyncedItem item)
        {
            if (item.BlinkerLeftVar != null) return item.BlinkerLeftVar.Value;
            if (item.TurnSignalStalkFsm == null) return false;

            try { return item.TurnSignalStalkFsm.Fsm.ActiveStateName == "On 2"; }
            catch { return false; }
        }

        private static bool ReadBlinkerRight(SyncedItem item)
        {
            if (item.BlinkerRightVar != null) return item.BlinkerRightVar.Value;
            if (item.TurnSignalStalkFsm == null) return false;

            try { return item.TurnSignalStalkFsm.Fsm.ActiveStateName == "On"; }
            catch { return false; }
        }

        private static byte ReadFuelLevelByte(SyncedItem item)
        {
            if (item.GaugeFuelLevelVar != null)
            {
                float level = item.GaugeFuelLevelVar.Value;
                if (level <= 1.01f)
                    return (byte)Mathf.Clamp(Mathf.RoundToInt(level * 255f), 0, 255);
            }

            if (item.FuelTankLevelVar != null)
            {
                float capacity = item.FuelTankCapacityVar?.Value ?? FuelTankDefaultLiters;
                if (capacity > 0f)
                {
                    return (byte)Mathf.Clamp(
                        Mathf.RoundToInt(item.FuelTankLevelVar.Value / capacity * 255f), 0, 255);
                }
            }

            return 0;
        }

        private static float TachRotationForRpm(float rpm)
        {
            // Approximate visible sweep for the SORBET gauge. This is deliberately
            // conservative: status lights/ignition are authoritative; the tach is
            // presentation, and over-rotation looks much worse than under-rotation.
            return Mathf.Lerp(0f, -235f, Mathf.Clamp01(rpm / TachMaxRpm));
        }

        private static float SpeedAngleForKmh(float kmh) =>
            Mathf.Lerp(0f, -220f, Mathf.Clamp01(kmh / SpeedoMaxKmh));

        private static float FuelAngleForLevel(float level01) =>
            Mathf.Lerp(0f, -90f, 1f - Mathf.Clamp01(level01));

        public static void UpdateRemoteEngineAudio(SyncedItem item, float now)
        {
            if (!item.LocallyOwned && now >= item.RemoteEngineUntil
                && (item.RemoteEngineOn || item.RemoteAccOn || item.RemoteElectricsApplied))
            {
                item.RemoteEngineOn = false;
                item.RemoteAccOn = false;
                if (item.RemoteElectricsApplied)
                    ApplyRemoteElectricity(item, false);
            }

            if (!item.LocallyOwned && item.RemoteElectricsApplied && item.RemoteDashDirty)
            {
                // Re-write the ~10 gauge FSM vars + light state only when a new VehicleState packet
                // (or an electrics toggle) actually changed them — not unconditionally every frame.
                ApplyRemoteGauges(item);
                UpdateRemoteLightPresentation(item);
                item.RemoteDashDirty = false;
            }

            bool shouldPlay = !item.LocallyOwned && item.RemoteEngineOn && now < item.RemoteEngineUntil;
            if (!shouldPlay)
            {
                if (item.RemoteEngineAudio != null && item.RemoteEngineAudio.isPlaying)
                    item.RemoteEngineAudio.Stop();
                return;
            }

            var source = EnsureRemoteEngineAudio(item);
            if (source == null) return;

            float targetPitch = Mathf.Clamp(
                EnginePitchBase + item.RemoteRpm * EnginePitchPerRpm,
                EnginePitchMin,
                EnginePitchMax);
            source.pitch = Mathf.Lerp(source.pitch, targetPitch, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (!source.isPlaying)
                source.Play();
        }

        public static void EnsureVehicleSystemsProbe(SyncedItem item)
        {
            if (item.Body == null) return;

            if (item.SystemsReady)
            {
                EnsureClimateProbe(item);
                return;
            }

            if (Time.unscaledTime < item.NextSystemsProbeAt) return;
            item.NextSystemsProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;

            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (item.ElectricityPowerFsm == null
                    && fsm.FsmName == "Power"
                    && fsm.gameObject.name == "Electricity")
                {
                    item.ElectricityPowerFsm = fsm;
                }

                if (item.EngineRevsVar == null)
                {
                    var revs = fsm.FsmVariables.FindFsmFloat("Revs");
                    if (revs != null && (fsm.FsmName == "FuelLine" || fsm.FsmName == "RotateEngine"))
                        item.EngineRevsVar = revs;
                }

                string path = ScenePath.Of(fsm.transform);
                if (item.GaugeSpeedVar == null && fsm.FsmName == "Speedo")
                {
                    var speed = fsm.FsmVariables.FindFsmFloat("Speed");
                    if (speed != null)
                    {
                        item.GaugeSpeedVar = speed;
                        item.GaugeSpeedAngleVar = fsm.FsmVariables.FindFsmFloat("Angle");
                        item.GaugeRpmVar = fsm.FsmVariables.FindFsmFloat("RPM")
                            ?? fsm.FsmVariables.FindFsmFloat("Revs");
                    }
                }

                if (item.GaugeFuelLevelVar == null && fsm.FsmName == "Fuel")
                {
                    var level = fsm.FsmVariables.FindFsmFloat("Level");
                    if (level != null)
                    {
                        item.GaugeFuelLevelVar = level;
                        item.GaugeFuelAngleVar = fsm.FsmVariables.FindFsmFloat("Angle");
                    }
                }

                if (item.FuelTankLevelVar == null && fsm.FsmName == "Data")
                {
                    var tankLevel = fsm.FsmVariables.FindFsmFloat("FuelLevel");
                    var capacity = fsm.FsmVariables.FindFsmFloat("MaxCapacity");
                    if (tankLevel != null && capacity != null)
                    {
                        item.FuelTankLevelVar = tankLevel;
                        item.FuelTankCapacityVar = capacity;
                    }
                }

                if (item.TurnSignalStalkFsm == null && fsm.FsmName == "Usage"
                    && fsm.gameObject.name == "TurnSignalsX")
                {
                    item.TurnSignalStalkFsm = fsm;
                }

                if (item.TurnSignalsFsm == null && fsm.FsmName == "TurnSignals"
                    && path.IndexOf("/PowerON/Systems", StringComparison.Ordinal) >= 0)
                {
                    item.TurnSignalsFsm = fsm;
                    item.BlinkerLeftVar = fsm.FsmVariables.FindFsmBool("BlinkerLeft");
                    item.BlinkerRightVar = fsm.FsmVariables.FindFsmBool("BlinkerRight");
                    item.BlinkerHazardsVar = fsm.FsmVariables.FindFsmBool("BlinkerHazards");
                }

                if (item.HazardButtonFsm == null && fsm.FsmName == "Use"
                    && fsm.gameObject.name == "ButtonHazard"
                    && FsmWorldSync.HasAllStates(fsm, "On", "Off"))
                {
                    item.HazardButtonFsm = fsm;
                }

                if (item.GaugeCoolantAngleVar == null && fsm.FsmName == "Temp"
                    && (path.IndexOf("GaugeData", StringComparison.Ordinal) >= 0
                        || path.IndexOf("StandardGaugesData", StringComparison.Ordinal) >= 0))
                {
                    var rotation = fsm.FsmVariables.FindFsmFloat("Rotation");
                    var angle = fsm.FsmVariables.FindFsmFloat("Angle");
                    if (rotation != null || angle != null)
                    {
                        item.GaugeCoolantAngleVar = rotation ?? angle;
                        item.GaugeCoolantVar = fsm.FsmVariables.FindFsmFloat("Temp");
                    }
                }

                if (item.GaugeTachDataFsm == null && fsm.FsmName == "Tach"
                    && (fsm.FsmVariables.FindFsmFloat("Revs") != null
                        || fsm.FsmVariables.FindFsmFloat("Rotation") != null
                        || fsm.FsmVariables.FindFsmFloat("Angle") != null))
                {
                    item.GaugeTachDataFsm = fsm;
                    item.GaugeTachRevsVar = fsm.FsmVariables.FindFsmFloat("Revs");
                    item.GaugeTachRotationVar = fsm.FsmVariables.FindFsmFloat("Rotation")
                        ?? fsm.FsmVariables.FindFsmFloat("Angle");
                    item.GaugeTachNeedle = fsm.FsmVariables.GetFsmGameObject("Needle")?.Value;
                }
            }

            foreach (var transform in item.Body.GetComponentsInChildren<Transform>(true))
            {
                if (item.AudioEngine != null) break;
                if (transform.name != "AudioEngine") continue;
                item.AudioEngine = transform.gameObject;
            }

            // Electricity + revs are the minimum; audio/gauges are nice-to-have.
            if (item.ElectricityPowerFsm != null && item.EngineRevsVar != null)
            {
                item.SystemsReady = true;
                WinterMPPlugin.Log.LogInfo($"WorldSync: vehicle systems ready on '{item.Path}'.");
            }

            EnsureClimateProbe(item);
        }

    }
}
