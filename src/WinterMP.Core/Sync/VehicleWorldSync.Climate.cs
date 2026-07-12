using System;
using System.Collections.Generic;
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
        private static void EnsureClimateProbe(SyncedItem item)
        {
            if (item.ClimateReady || item.Body == null) return;
            if (Time.unscaledTime < item.NextClimateProbeAt) return;
            item.NextClimateProbeAt = Time.unscaledTime + ClimateProbeIntervalSeconds;

            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                string path = ScenePath.Of(fsm.transform);
                if (!SyncCatalog.IsClimateVehicleFsmPath(path)) continue;

                bool carTempRoot = SyncCatalog.IsCarTempFsmPath(path);

                if (item.GlassFrostingFsm == null && fsm.FsmName == "GlassFrosting" && carTempRoot)
                {
                    item.GlassFrostingFsm = fsm;
                    item.FrostVar = fsm.FsmVariables.FindFsmFloat("Frost");
                    item.SweatRateVar = fsm.FsmVariables.FindFsmFloat("SweatRate");
                    item.GlassTempVar = fsm.FsmVariables.FindFsmFloat("Temp");
                    item.PlayerInVar = fsm.FsmVariables.FindFsmBool("PlayerIn");
                    item.FrostColorVar = fsm.FsmVariables.FindFsmColor("Color");
                    item.FrostGlassMat = fsm.FsmVariables.GetFsmMaterial("FrostGlass");
                }

                if (item.FreezingFsm == null && fsm.FsmName == "Freezing" && carTempRoot)
                {
                    item.FreezingFsm = fsm;
                    item.CutoffWindshieldVar = fsm.FsmVariables.FindFsmFloat("CutoffWindshield");
                    item.CutoffSideLeftVar = fsm.FsmVariables.FindFsmFloat("CutoffSideLeft");
                    item.CutoffSideRightVar = fsm.FsmVariables.FindFsmFloat("CutoffSideRight");
                    item.CutoffDoorLeftVar = fsm.FsmVariables.FindFsmFloat("CutoffDoorleft");
                    item.CutoffDoorRightVar = fsm.FsmVariables.FindFsmFloat("CutoffDoorright");
                    item.CutoffRearVar = fsm.FsmVariables.FindFsmFloat("CutoffRear");
                }

                if (item.CarTempDataFsm == null && fsm.FsmName == "Data" && carTempRoot
                    && fsm.FsmVariables.FindFsmFloat("InteriorTemp") != null)
                {
                    item.CarTempDataFsm = fsm;
                    item.InteriorTempVar = fsm.FsmVariables.FindFsmFloat("InteriorTemp");
                }

                if (item.HeaterUnitFsm == null && fsm.FsmName == "Function"
                    && SyncCatalog.IsHeaterFsmPath(path))
                {
                    item.HeaterUnitFsm = fsm;
                    item.HeaterSettingTemp = fsm.FsmVariables.FindFsmFloat("SettingTemp");
                    item.HeaterSettingBlower = fsm.FsmVariables.FindFsmFloat("SettingBlower");
                    item.HeaterSettingDirection = fsm.FsmVariables.FindFsmFloat("SettingDirection");
                    item.GlassDefrostingVar = fsm.FsmVariables.FindFsmBool("GlassDefrosting");
                }

                if (fsm.FsmName != "Use") continue;

                string name = fsm.gameObject.name;
                if (name == "ButtonHeaterTemp")
                {
                    item.KnobTempFsm = fsm;
                    item.KnobTempSetting = fsm.FsmVariables.FindFsmFloat("Setting");
                    item.KnobTempAngle = fsm.FsmVariables.FindFsmFloat("Angle");
                }
                else if (name == "ButtonHeaterBlower")
                {
                    item.KnobBlowerFsm = fsm;
                    item.KnobBlowerSetting = fsm.FsmVariables.FindFsmFloat("Setting");
                    item.KnobBlowerAngle = fsm.FsmVariables.FindFsmFloat("Angle");
                    item.KnobBlowerCommitState = FsmHook.HasState(fsm, "Set angle") ? "Set angle" : "Set";
                }
                else if (name == "ButtonHeaterDirection")
                {
                    item.KnobDirectionFsm = fsm;
                    item.KnobDirectionSetting = fsm.FsmVariables.FindFsmFloat("Setting");
                    item.KnobDirectionAngle = fsm.FsmVariables.FindFsmFloat("Angle");
                }
                else if (name == "ButtonWindowHeater")
                {
                    item.WindowHeaterButtonFsm = fsm;
                    item.WindowHeaterOnVar = fsm.FsmVariables.FindFsmBool("ButtonOn");
                }
            }

            if (item.GlassFrostingFsm != null && item.FrostVar != null)
            {
                item.ClimateReady = true;
                WinterMPPlugin.Log.LogInfo($"WorldSync: climate ready on '{item.Path}'.");
            }
        }


        public void UpdateVehicleClimate(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            float now = Time.unscaledTime;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null) continue;
                EnsureClimateProbe(item);
                if (!ShouldStreamVehicleClimate(item)) continue;
                SendVehicleClimate(session, item, now);
            }
        }

        private bool ShouldStreamVehicleClimate(SyncedItem item)
        {
            // Whoever is actively operating or occupying the car is its live climate
            // authority (a guest driving their own car, or the host driving).
            if (item.LocallyOwned || HasLocalIgnitionActivity(item)) return true;

            var passenger = PassengerController.Instance;
            if (passenger != null && passenger.IsLocalSeatedInVehicle(item.Id))
                return true;

            // A remote player owns/drives it — mirror them, never fight their stream.
            if (item.RemoteOwner != WorldSyncIds.NoOwner) return false;

            // Nobody owns it (parked). Frost/fog is slow world state like weather, and
            // the game re-simulates it locally on every machine — so if it free-runs
            // unsynced the two windshields drift apart and one player's car ends up
            // permanently clear while the other keeps frosting. Make the HOST the
            // standing climate authority for every unowned car, even parked and far
            // away; guests just mirror it. (Old behaviour only streamed within 7 m, so
            // a car parked away from both players desynced — the reported bug.)
            return _bridge.Session != null && _bridge.Session.IsHost;
        }

        public VehicleClimate? TryBuildVehicleClimate(SyncedItem item)
        {
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return null;

            byte flags = 0;
            if (ReadWindowHeaterOn(item)) flags |= VehicleClimate.FlagWindowHeater;
            if (ReadGlassDefrosting(item)) flags |= VehicleClimate.FlagGlassDefrosting;
            if (ReadPlayerInCar(item)) flags |= VehicleClimate.FlagPlayerIn;

            return new VehicleClimate
            {
                VehicleId = item.Id,
                OwnerPlayerId = _bridge.Session?.LocalPlayerId ?? 0,
                // Default to the snapshot sentinel; the live SendVehicleClimate path
                // overwrites Sequence with ++OutClimateSequence. Snapshot/resync sends
                // keep the sentinel so the receiver applies them without dedup.
                Sequence = VehicleClimate.SnapshotSequence,
                Frost = QuantizeFrost(ReadFrost(item)),
                Flags = flags,
                HeaterTemp = QuantizeHeater(ReadHeaterTemp(item), HeaterTempMax),
                HeaterBlower = QuantizeHeater(ReadHeaterBlower(item), HeaterBlowerMax),
                HeaterDirection = QuantizeHeater(ReadHeaterDirection(item), HeaterDirectionMax),
                Fog = QuantizeFrost(ReadFog(item)),
                CabinTemp = QuantizeHeater(ReadCabinTemp(item), CabinTempMaxC),
            };
        }

        private void SendVehicleClimate(SessionManager session, SyncedItem item, float now)
        {
            if (now < item.NextClimateAt) return;
            item.NextClimateAt = now + 1f / VehicleClimateRateHz;

            var message = TryBuildVehicleClimate(item);
            if (message == null) return;

            message.Sequence = ++item.OutClimateSequence;
            message.OwnerPlayerId = session.LocalPlayerId;

            if (!item.LoggedClimateSend)
            {
                item.LoggedClimateSend = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: streaming '{item.Path}' climate — frost={message.Frost} fog={message.Fog} " +
                    $"cabin={message.CabinTemp} heater={message.HeaterTemp}/{message.HeaterBlower}/{message.HeaterDirection} " +
                    $"flags=0x{message.Flags:X2}.");
            }

            if (now >= item.NextClimateDiagAt)
            {
                item.NextClimateDiagAt = now + ClimateDiagIntervalSeconds;
                float liveCut = item.CutoffWindshieldVar != null ? item.CutoffWindshieldVar.Value : -1f;
                float liveFrostVar = item.FrostVar != null ? item.FrostVar.Value : -1f;
                bool host = _bridge.Session != null && _bridge.Session.IsHost;
                WinterMPPlugin.Log.LogInfo(
                    $"ClimateDiag SEND '{item.Path}' host={host} owned={item.LocallyOwned} remoteOwner={item.RemoteOwner} " +
                    $"sentFrost={message.Frost} sentFog={message.Fog} liveFrostVar={liveFrostVar:F3} liveCutWS={liveCut:F3} flags=0x{message.Flags:X2}");
            }

            session.SendWorldMessage(message, Channel.UnreliableSequenced);
        }

        public void OnRemoteVehicleClimate(VehicleClimate message)
        {
            if (!_items.Items.TryGetValue(message.VehicleId, out var item) || item.Body == null || !item.IsVehicle)
                return;
            if (item.LocallyOwned) return;

            // Join/resync snapshots carry SnapshotSequence and bypass the live-stream
            // dedup so a fresh guest near a parked frosted car receives its state.
            if (message.Sequence != VehicleClimate.SnapshotSequence)
            {
                ushort diff = (ushort)(message.Sequence - item.LastClimateSequence);
                if (diff == 0 || diff > short.MaxValue)
                {
                    ConnectionQuality.Instance.NoteUnreliableDropped();
                    return;
                }

                ConnectionQuality.Instance.NoteUnreliableReceived();
                item.LastClimateSequence = message.Sequence;
            }

            item.RemoteClimateUntil = Time.unscaledTime + ClimateHoldSeconds;

            ApplyRemoteClimate(item, message);
        }

        private static void ApplyRemoteClimate(SyncedItem item, VehicleClimate message)
        {
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return;

            bool windowHeater = message.WindowHeaterOn;
            bool glassDefrosting = message.GlassDefrosting;
            bool defrostActive = windowHeater || glassDefrosting;
            bool wasDefrost = item.RemoteWindowHeater || item.RemoteGlassDefrosting;
            bool windowHeaterChanged = windowHeater != item.RemoteWindowHeater;

            item.RemoteFrost = message.Frost;
            item.RemoteFog = message.Fog;
            item.RemoteCabinTemp = message.CabinTemp;
            item.RemotePlayerIn = message.PlayerIn;
            item.RemoteHeaterTemp = message.HeaterTemp;
            item.RemoteHeaterBlower = message.HeaterBlower;
            item.RemoteHeaterDirection = message.HeaterDirection;
            item.RemoteWindowHeater = windowHeater;
            item.RemoteGlassDefrosting = glassDefrosting;

            ApplyRemoteHeaterKnobs(item, replayStates: false);

            if (windowHeaterChanged || !item.LoggedClimateApply)
                ApplyRemoteWindowHeater(item, windowHeater);

            if (defrostActive && !wasDefrost)
                PulseRemoteDefrost(item, true);
            else if (!defrostActive && wasDefrost)
                PulseRemoteDefrost(item, false);

            ApplyRemoteClimatePresentation(item);

            if (!item.LoggedClimateApply)
            {
                item.LoggedClimateApply = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: remote climate on '{item.Path}' — frost={message.Frost} fog={message.Fog} " +
                    $"windowHeater={windowHeater} defrost={glassDefrosting}.");
            }
        }

        private static void UpdateRemoteClimatePresentation(SyncedItem item, float now)
        {
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return;

            if (now >= item.NextClimateDiagAt)
            {
                item.NextClimateDiagAt = now + ClimateDiagIntervalSeconds;
                float liveCut = item.CutoffWindshieldVar != null ? item.CutoffWindshieldVar.Value : -1f;
                float liveFrostVar = item.FrostVar != null ? item.FrostVar.Value : -1f;
                float wantFrost = DequantizeFrost(item.RemoteFrost);
                // pre* = what the car's own Freezing/GlassFrosting FSM left the value at
                // since our last write. If pre* keeps drifting away from want*, the local
                // FSM is fighting the stream (needs suppression); if pre* ~= want* but the
                // two machines still differ, the sender is reading a different frost.
                WinterMPPlugin.Log.LogInfo(
                    $"ClimateDiag APPLY '{item.Path}' owned={item.LocallyOwned} remoteOwner={item.RemoteOwner} " +
                    $"wantFrost={wantFrost:F3}({item.RemoteFrost}) preFrostVar={liveFrostVar:F3} preCutWS={liveCut:F3} " +
                    $"streamLiveFor={item.RemoteClimateUntil - now:F1}s");
            }

            ApplyRemoteClimatePresentation(item);

            bool defrostActive = item.RemoteWindowHeater || item.RemoteGlassDefrosting;
            if (item.GlassDefrostingVar != null)
                item.GlassDefrostingVar.Value = defrostActive;

            if (item.RemoteHeaterTemp != item.PresentedHeaterTemp
                || item.RemoteHeaterBlower != item.PresentedHeaterBlower
                || item.RemoteHeaterDirection != item.PresentedHeaterDirection)
            {
                ApplyRemoteHeaterKnobs(item, replayStates: true);
                item.PresentedHeaterTemp = item.RemoteHeaterTemp;
                item.PresentedHeaterBlower = item.RemoteHeaterBlower;
                item.PresentedHeaterDirection = item.RemoteHeaterDirection;
            }

            if (!defrostActive || now < item.NextDefrostPulseAt) return;
            item.NextDefrostPulseAt = now + DefrostPulseSeconds;
            PulseRemoteDefrost(item, true);
        }

        private static void ApplyRemoteClimatePresentation(SyncedItem item)
        {
            float frost = DequantizeFrost(item.RemoteFrost);
            float fog = DequantizeFrost(item.RemoteFog);
            float cabinTemp = DequantizeHeater(item.RemoteCabinTemp, CabinTempMaxC);

            ApplyRemoteFrostLevel(item, frost);
            ApplyRemoteFogLevel(item, fog, cabinTemp, item.RemotePlayerIn);
        }

        private static void ApplyRemoteFrostLevel(SyncedItem item, float frost)
        {
            if (item.FrostVar != null)
                item.FrostVar.Value = frost;

            if (item.FrostColorVar != null)
            {
                var color = item.FrostColorVar.Value;
                color.a = frost;
                item.FrostColorVar.Value = color;
            }

            WriteCutoff(item.CutoffWindshieldVar, frost);
            WriteCutoff(item.CutoffSideLeftVar, frost);
            WriteCutoff(item.CutoffSideRightVar, frost);
            WriteCutoff(item.CutoffDoorLeftVar, frost);
            WriteCutoff(item.CutoffDoorRightVar, frost);
            WriteCutoff(item.CutoffRearVar, frost);

            ApplyFrostGlassMaterial(item, fog: -1f, frost: frost);
        }

        private static void ApplyRemoteFogLevel(SyncedItem item, float fog, float cabinTemp, bool playerIn)
        {
            if (item.SweatRateVar != null)
                item.SweatRateVar.Value = fog;

            if (item.GlassTempVar != null)
                item.GlassTempVar.Value = cabinTemp;

            if (item.InteriorTempVar != null)
                item.InteriorTempVar.Value = cabinTemp;

            if (item.PlayerInVar != null)
                item.PlayerInVar.Value = playerIn;

            if (item.FrostColorVar != null)
            {
                var color = item.FrostColorVar.Value;
                color.r = fog;
                color.g = fog;
                color.b = fog;
                item.FrostColorVar.Value = color;
            }

            ApplyFrostGlassMaterial(item, fog, frost: -1f);
        }

        private static void ApplyFrostGlassMaterial(SyncedItem item, float fog, float frost)
        {
            if (item.FrostGlassMat == null || item.FrostGlassMat.Value == null) return;

            var mat = item.FrostGlassMat.Value;
            if (fog >= 0f)
            {
                if (mat.HasProperty("_Color"))
                {
                    var c = mat.color;
                    c.r = fog;
                    c.g = fog;
                    c.b = fog;
                    mat.color = c;
                }
            }

            if (frost >= 0f && mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", frost);
        }

        private static void WriteCutoff(HutongGames.PlayMaker.FsmFloat? var, float frost)
        {
            if (var != null) var.Value = frost;
        }


        private SyncedItem? FindVehicleItemForFsm(PlayMakerFSM fsm)
        {
            var t = fsm.transform;
            while (t != null)
            {
                var rb = t.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    foreach (var item in _items.Items.Values)
                    {
                        if (item.Body == rb && item.IsVehicle)
                            return item;
                    }
                }

                t = t.parent;
            }

            return null;
        }

        public void PrepareRemoteControl(SyncedControl control)
        {
            var item = FindVehicleItemForFsm(control.Fsm);
            if (item == null) return;

            string path = control.Path;
            if (path.IndexOf("ButtonWindowHeater", StringComparison.Ordinal) >= 0
                || path.IndexOf("ButtonHazard", StringComparison.Ordinal) >= 0)
            {
                EnsureVehicleSystemsProbe(item);
                if (item.RemoteAccOn && !item.RemoteElectricsApplied)
                    ApplyRemoteElectricity(item, true);
            }
            else if (path.IndexOf("ButtonHeater", StringComparison.Ordinal) >= 0)
            {
                ApplyRemoteHeaterKnobs(item, replayStates: false);
            }
        }

        public static void FinishRemoteControl(SyncedControl control, string stateName)
        {
            if (control.Path.IndexOf("ButtonWindowHeater", StringComparison.Ordinal) >= 0)
                SetWindowHeaterVisual(control.Fsm, stateName == "On");
            else if (control.Path.IndexOf("ButtonHazard", StringComparison.Ordinal) >= 0)
                SetHazardVisual(control.Fsm, stateName == "On");
        }

        private static void SetWindowHeaterVisual(PlayMakerFSM fsm, bool on)
        {
            var buttonOn = fsm.FsmVariables.FindFsmBool("ButtonOn");
            if (buttonOn != null)
                buttonOn.Value = on;

            var light = fsm.FsmVariables.GetFsmGameObject("Light");
            if (light != null && light.Value != null)
                light.Value.SetActive(on);
        }

        private static void ApplyRemoteHeaterKnobs(SyncedItem item, bool replayStates)
        {
            float temp = DequantizeHeater(item.RemoteHeaterTemp, HeaterTempMax);
            float blower = DequantizeHeater(item.RemoteHeaterBlower, HeaterBlowerMax);
            float direction = DequantizeHeater(item.RemoteHeaterDirection, HeaterDirectionMax);

            WriteHeaterKnob(item.HeaterSettingTemp, item.KnobTempSetting, item.KnobTempAngle, temp);
            WriteHeaterKnob(item.HeaterSettingBlower, item.KnobBlowerSetting, item.KnobBlowerAngle, blower);
            WriteHeaterKnob(item.HeaterSettingDirection, item.KnobDirectionSetting, item.KnobDirectionAngle, direction);

            if (!replayStates) return;

            FireKnobCommitState(item.KnobTempFsm, "Set");
            FireKnobCommitState(item.KnobBlowerFsm, item.KnobBlowerCommitState);
            FireKnobCommitState(item.KnobDirectionFsm, "Set");
        }

        private static void FireKnobCommitState(PlayMakerFSM? fsm, string stateName)
        {
            if (fsm == null || !FsmHook.EnsureRemoteEntry(fsm, stateName)) return;

            var world = WorldSyncManager.Instance;
            if (world == null) return;
            world.ApplyingRemote = true;
            try
            {
                FsmHook.FireRemoteEntry(fsm, stateName);
            }
            finally
            {
                world.ApplyingRemote = false;
            }
        }

        private static void ApplyRemoteWindowHeater(SyncedItem item, bool on)
        {
            if (item.WindowHeaterButtonFsm == null) return;

            string state = on ? "On" : "Off";
            if (!FsmHook.EnsureRemoteEntry(item.WindowHeaterButtonFsm, state))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: window heater '{state}' missing on '{item.Path}'.");
                return;
            }

            FsmHook.FireRemoteEntry(item.WindowHeaterButtonFsm, state);
            SetWindowHeaterVisual(item.WindowHeaterButtonFsm, on);
        }

        private static void PulseRemoteDefrost(SyncedItem item, bool on)
        {
            if (item.GlassDefrostingVar != null)
                item.GlassDefrostingVar.Value = on;

            if (!on)
            {
                if (item.GlassFrostingFsm != null)
                {
                    try { item.GlassFrostingFsm.SendEvent("FINISHED"); }
                    catch { /* FSM not ready yet */ }
                }

                return;
            }

            if (item.GlassFrostingFsm != null)
            {
                try { item.GlassFrostingFsm.SendEvent("DEFROST"); }
                catch { /* FSM not ready yet */ }
            }

            if (item.CarTempDataFsm != null
                && FsmHook.EnsureRemoteEntry(item.CarTempDataFsm, "Defrost"))
            {
                FsmHook.FireRemoteEntry(item.CarTempDataFsm, "Defrost");
            }
        }

        private static float ReadFrost(SyncedItem item)
        {
            float frost = item.FrostVar != null ? item.FrostVar.Value : 0f;
            if (item.CutoffWindshieldVar != null)
                frost = Mathf.Max(frost, item.CutoffWindshieldVar.Value);
            return frost;
        }

        private static float ReadFog(SyncedItem item)
        {
            float fog = 0f;
            if (item.SweatRateVar != null)
                fog = Mathf.Max(fog, item.SweatRateVar.Value);

            if (item.FrostColorVar != null)
            {
                var color = item.FrostColorVar.Value;
                fog = Mathf.Max(fog, Mathf.Max(color.r, Mathf.Max(color.g, color.b)));
            }

            return fog;
        }

        private static float ReadCabinTemp(SyncedItem item)
        {
            if (item.InteriorTempVar != null) return item.InteriorTempVar.Value;
            if (item.GlassTempVar != null) return item.GlassTempVar.Value;
            return 0f;
        }

        private static bool ReadPlayerInCar(SyncedItem item)
        {
            if (item.PlayerInVar != null) return item.PlayerInVar.Value;

            var passenger = PassengerController.Instance;
            if (passenger != null && passenger.IsLocalSeatedInVehicle(item.Id))
                return true;

            var world = WorldSyncManager.Instance;
            return item.Body != null && world != null && world.IsLocalPlayerDriving(item);
        }

        private static float ReadHeaterTemp(SyncedItem item) =>
            item.HeaterSettingTemp?.Value ?? item.KnobTempSetting?.Value ?? 0f;

        private static float ReadHeaterBlower(SyncedItem item) =>
            item.HeaterSettingBlower?.Value ?? item.KnobBlowerSetting?.Value ?? 0f;

        private static float ReadHeaterDirection(SyncedItem item) =>
            item.HeaterSettingDirection?.Value ?? item.KnobDirectionSetting?.Value ?? 0f;

        private static bool ReadWindowHeaterOn(SyncedItem item) =>
            item.WindowHeaterOnVar != null && item.WindowHeaterOnVar.Value;

        private static bool ReadGlassDefrosting(SyncedItem item)
        {
            if (ReadWindowHeaterOn(item)) return true;
            return item.GlassDefrostingVar != null && item.GlassDefrostingVar.Value;
        }

        private static bool ReadHazardOn(SyncedItem item)
        {
            if (item.BlinkerHazardsVar != null && item.BlinkerHazardsVar.Value)
                return true;

            if (item.HazardButtonFsm != null)
            {
                var hazardsOn = item.HazardButtonFsm.FsmVariables.FindFsmBool("HazardsOn");
                if (hazardsOn != null && hazardsOn.Value) return true;
                var buttonOn = item.HazardButtonFsm.FsmVariables.FindFsmBool("ButtonOn");
                if (buttonOn != null && buttonOn.Value) return true;
            }

            return false;
        }

        private static byte ReadCoolantTempByte(SyncedItem item)
        {
            float temp = ReadCoolantTempC(item);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(temp / CoolantTempMaxC * 255f), 0, 255);
        }

        private static float ReadCoolantTempC(SyncedItem item)
        {
            if (item.GaugeCoolantVar != null)
                return item.GaugeCoolantVar.Value;

            if (item.HeaterUnitFsm != null)
            {
                var coolant = item.HeaterUnitFsm.FsmVariables.FindFsmFloat("CoolantTemp");
                if (coolant != null) return coolant.Value;
            }

            if (item.GaugeCoolantAngleVar != null)
                return TempFromCoolantAngle(item.GaugeCoolantAngleVar.Value);

            return 0f;
        }

        private static float CoolantAngleForTemp(float tempC) =>
            Mathf.Lerp(0f, -220f, Mathf.Clamp01(tempC / CoolantTempMaxC));

        private static float TempFromCoolantAngle(float angle) =>
            Mathf.Clamp01(-angle / -220f) * CoolantTempMaxC;

        private static byte QuantizeFrost(float frost) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(frost) * 255f), 0, 255);

        private static float DequantizeFrost(byte wire) => wire / 255f;

        private static byte QuantizeHeater(float value, float max)
        {
            if (max <= 0f) return 0;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(value, 0f, max) / max * 255f), 0, 255);
        }

        private static float DequantizeHeater(byte wire, float max) => wire / 255f * max;

        private static void WriteHeaterValue(
            HutongGames.PlayMaker.FsmFloat? primary,
            HutongGames.PlayMaker.FsmFloat? knob,
            float value)
        {
            WriteHeaterKnob(primary, knob, null, value);
        }

        private static void WriteHeaterKnob(
            HutongGames.PlayMaker.FsmFloat? primary,
            HutongGames.PlayMaker.FsmFloat? setting,
            HutongGames.PlayMaker.FsmFloat? angle,
            float value)
        {
            if (primary != null) primary.Value = value;
            if (setting != null) setting.Value = value;
            if (angle != null)
                angle.Value = setting != null ? setting.Value : value;
        }

        private static AudioSource? EnsureRemoteEngineAudio(SyncedItem item)
        {
            if (item.RemoteEngineAudio != null) return item.RemoteEngineAudio;
            if (item.RemoteEngineAudioSearched || item.Body == null) return null;
            item.RemoteEngineAudioSearched = true;

            EnsureVehicleSystemsProbe(item);
            if (item.AudioEngine == null) return null;

            // Template: prefer the "High" loop, else any clip under AudioEngine.
            AudioSource? template = null;
            foreach (var source in item.AudioEngine.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.clip == null) continue;
                if (template == null) template = source;
                if (source.gameObject.name.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    template = source;
                    break;
                }
            }

            if (template == null) return null;

            // A fresh GameObject, not Instantiate: cloning the game's audio object
            // would clone its FSMs — i.e. run a second engine simulation.
            var holder = new GameObject("WinterMP_EngineAudio");
            holder.transform.parent = item.AudioEngine.transform.parent;
            holder.transform.position = item.AudioEngine.transform.position;
            var audio = holder.AddComponent<AudioSource>();
            audio.clip = template.clip;
            audio.loop = true;
            audio.playOnAwake = false;
            audio.spatialBlend = template.spatialBlend;
            audio.minDistance = template.minDistance;
            audio.maxDistance = template.maxDistance;
            audio.rolloffMode = template.rolloffMode;
            audio.dopplerLevel = template.dopplerLevel;
            audio.volume = EngineAudioVolume;
            item.RemoteEngineAudio = audio;
            WinterMPPlugin.Log.LogInfo($"WorldSync: remote engine audio for '{item.Path}' (clip '{template.clip.name}').");
            return audio;
        }



    }
}
