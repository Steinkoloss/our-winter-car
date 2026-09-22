using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private readonly VehicleClimateStreamPolicy _vehicleClimateStreams = new VehicleClimateStreamPolicy();

        private static void EnsureClimateProbe(SyncedItem item)
        {
            if (item.ClimateReady || item.Body == null) return;
            if (Time.unscaledTime < item.NextClimateProbeAt) return;
            item.NextClimateProbeAt = Time.unscaledTime + ClimateProbeIntervalSeconds;

            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                string fsmName = fsm.FsmName;
                if (fsmName == "Use")
                {
                    string button = fsm.gameObject.name;
                    if (button != "ButtonHeaterTemp" && button != "ButtonHeaterBlower"
                        && button != "ButtonHeaterDirection" && button != "ButtonWindowHeater") continue;
                }
                else if (fsmName != "GlassFrosting" && fsmName != "Freezing"
                    && fsmName != "Data" && fsmName != "Function") continue;
                string path = ScenePath.Of(fsm.transform);
                if (!SyncCatalog.IsClimateVehicleFsmPath(path)) continue;

                bool carTempRoot = SyncCatalog.IsCarTempFsmPath(path);

                if (item.GlassFrostingFsm == null && fsm.FsmName == "GlassFrosting" && carTempRoot)
                {
                    item.GlassFrostingFsm = fsm;
                    item.FrostVar = fsm.FsmVariables.FindFsmFloat("Frost");
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
            EnsurePassengerHeating();
            float now = Time.unscaledTime;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null) continue;
                EnsureClimateProbe(item);
                EnsurePassengerCondensation(item);
                if (session.PlayerCount == 0 || !ShouldStreamVehicleClimate(item)) continue;
                SendVehicleClimate(session, item, now);
            }
        }

        private bool ShouldStreamVehicleClimate(SyncedItem item) => _bridge.Session != null
            && VehicleStateStreamPolicy.CanPublish(_bridge.Session.IsHost, item.LocallyOwned, item.RemoteOwner);

        public VehicleClimate? TryBuildVehicleClimate(SyncedItem item)
        {
            if (_bridge.Session?.IsHost == true && !item.LocallyOwned && item.RemoteOwner != WorldSyncIds.NoOwner)
            {
                var accepted = item.AcceptedVehicleClimate;
                if (accepted == null || accepted.OwnerPlayerId != item.RemoteOwner
                    || Time.unscaledTime >= item.RemoteClimateUntil) return null;
                var snapshot = VehicleClimateStreamPolicy.Copy(accepted);
                snapshot.OwnerPlayerId = 0;
                snapshot.Sequence = VehicleClimate.SnapshotSequence;
                return snapshot;
            }
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return null;

            byte flags = 0;
            if (ReadWindowHeaterOn(item)) flags |= VehicleClimate.FlagWindowHeater;
            if (ReadGlassDefrosting(item)) flags |= VehicleClimate.FlagGlassDefrosting;
            if (ReadPlayerInCar(item)) flags |= VehicleClimate.FlagPlayerIn;
            if (TryReadNativeCabinTemperature(item, out byte cabinTemp))
                flags |= VehicleClimate.FlagCabinTemperature;

            var message = new VehicleClimate
            {
                VehicleId = item.Id,
                OwnerPlayerId = _bridge.Session?.LocalPlayerId ?? 0,
                // Default to the snapshot sentinel; the live SendVehicleClimate path
                // assigns the next live sequence, skipping the reserved sentinel. Snapshot/resync sends
                // keep the sentinel so the receiver applies them without dedup.
                Sequence = VehicleClimate.SnapshotSequence,
                Frost = QuantizeFrost(ReadFrost(item)),
                Flags = flags,
                HeaterTemp = QuantizeHeater(ReadHeaterTemp(item), HeaterTempMax),
                HeaterBlower = QuantizeHeater(ReadHeaterBlower(item), HeaterBlowerMax),
                HeaterDirection = QuantizeHeater(ReadHeaterDirection(item), HeaterDirectionMax),
                Fog = 0,
                CabinTemp = cabinTemp,
            };
            CaptureWindowIce(item, message);
            CaptureParkingBrake(item, message);
            return message;
        }

        internal void SendFinalVehicleClimate(SessionManager session, SyncedItem item)
        {
            if (item.IsVehicle && item.LocallyOwned) SendVehicleClimate(session, item, Time.unscaledTime, final: true);
        }

        private void SendVehicleClimate(SessionManager session, SyncedItem item, float now, bool final = false)
        {
            if (!final && now < item.NextClimateAt) return;
            item.NextClimateAt = now + 1f / VehicleClimateRateHz;

            var message = TryBuildVehicleClimate(item);
            if (message == null) return;

            message.Sequence = item.OutClimateSequence = VehicleStateStreamPolicy.NextSequence(item.OutClimateSequence);
            message.OwnerPlayerId = session.LocalPlayerId;

            if (!item.LoggedClimateSend)
            {
                item.LoggedClimateSend = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: streaming '{item.Path}' climate — frost={message.Frost} " +
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
                    $"sentFrost={message.Frost} liveFrostVar={liveFrostVar:F3} liveCutWS={liveCut:F3} flags=0x{message.Flags:X2}");
            }

            session.SendWorldMessage(message, final ? Channel.ReliableOrdered : Channel.UnreliableSequenced);
        }

        public bool OnRemoteVehicleClimate(VehicleClimate message)
        {
            var session = _bridge.Session;
            if (session == null || (session.IsHost ? session.State != SessionState.Hosting : session.State != SessionState.Connected)
                || !_items.Items.TryGetValue(message.VehicleId, out var item)
                || item.Body == null || !item.IsVehicle) return false;
            bool localDriver = item.LocallyOwned || _items.IsLocalPlayerDriving(item);
            if (!_vehicleClimateStreams.Receive(message, session.IsHost, localDriver, item.RemoteOwner))
            {
                if (message.Sequence != VehicleClimate.SnapshotSequence) ConnectionQuality.Instance.NoteUnreliableDropped();
                return false;
            }
            if (message.Sequence != VehicleClimate.SnapshotSequence) ConnectionQuality.Instance.NoteUnreliableReceived();
            item.AcceptedVehicleClimate = VehicleClimateStreamPolicy.Copy(message);
            item.RemoteClimateUntil = Time.unscaledTime + ClimateHoldSeconds;
            ApplyRemoteParkingBrake(item, message);
            ApplyRemoteClimate(item, message);
            return true;
        }

        private bool CanPresentRemoteClimate(SyncedItem item)
        {
            var session = _bridge.Session;
            return session != null && item.AcceptedVehicleClimate != null
                && VehicleClimateStreamPolicy.CanPresent(item.AcceptedVehicleClimate.OwnerPlayerId, session.IsHost,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), item.RemoteOwner);
        }

        private void ForgetClimatePlayer(byte playerId)
        {
            _vehicleClimateStreams.ForgetPlayer(playerId);
            foreach (var item in _items.Items.Values)
                if (item.AcceptedVehicleClimate?.OwnerPlayerId == playerId)
                { item.AcceptedVehicleClimate = null; item.RemoteClimateUntil = -999f; }
        }

        private void ClearClimateStreams()
        {
            ClearPassengerHeating();
            ClearPassengerCondensationBindings();
            _vehicleClimateStreams.Clear();
            foreach (var item in _items.Items.Values)
            {
                item.AcceptedVehicleClimate = null; item.RemoteClimateUntil = -999f;
                item.RemoteIceMask = 0; item.OutClimateSequence = 0; item.NextClimateAt = 0;
                item.RemoteHasCabinTemperature = false;
                ClearPassengerCondensation(item); item.NextPassengerCondensationProbeAt = 0;
            }
        }

        private static void ApplyRemoteClimate(SyncedItem item, VehicleClimate message)
        {
            item.RemoteHasCabinTemperature = message.HasCabinTemperature;
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return;

            bool windowHeater = message.WindowHeaterOn;
            bool glassDefrosting = message.GlassDefrosting;
            bool defrostActive = windowHeater || glassDefrosting;
            bool wasDefrost = item.RemoteWindowHeater || item.RemoteGlassDefrosting;
            bool windowHeaterChanged = windowHeater != item.RemoteWindowHeater;

            item.RemoteFrost = message.Frost;
            item.RemoteIce = message.Ice;
            item.RemoteIceSideLeft = message.IceSideLeft;
            item.RemoteIceSideRight = message.IceSideRight;
            item.RemoteIceDoorLeft = message.IceDoorLeft;
            item.RemoteIceDoorRight = message.IceDoorRight;
            item.RemoteIceRear = message.IceRear;
            item.RemoteIceMask = message.IceMask;
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
                    $"WorldSync: remote climate on '{item.Path}' — frost={message.Frost} " +
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
            float cabinTemp = VehicleClimate.DequantizeCabinTemperature(item.RemoteCabinTemp);

            ApplyRemoteFrostLevel(item, frost);
            ApplyRemoteIceLevel(item);
            if (item.RemoteHasCabinTemperature && item.InteriorTempVar != null) item.InteriorTempVar.Value = cabinTemp;
        }

        // Interior glass frost: the GlassFrosting amount + its material. Deliberately does NOT
        // touch the exterior Cutoff* windows — those are the Ice channel (ApplyRemoteIceLevel).
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

            // Native SetColorRGBA/SetMaterialColor use _Color.a for condensation.
            // RGB is tint; SweatRate and Temp are arithmetic scratch, not opacity/degrees.
            var mat = item.FrostGlassMat?.Value;
            if (mat != null && mat.HasProperty("_Color"))
            {
                var color = mat.color;
                color.a = frost;
                mat.color = color;
            }
        }

        private static void CaptureWindowIce(SyncedItem item, VehicleClimate message)
        {
            message.Ice = CaptureWindowCutoff(item.CutoffWindshieldVar, VehicleClimate.Windshield, ref message.IceMask);
            message.IceSideLeft = CaptureWindowCutoff(item.CutoffSideLeftVar, VehicleClimate.SideLeft, ref message.IceMask);
            message.IceSideRight = CaptureWindowCutoff(item.CutoffSideRightVar, VehicleClimate.SideRight, ref message.IceMask);
            message.IceDoorLeft = CaptureWindowCutoff(item.CutoffDoorLeftVar, VehicleClimate.DoorLeft, ref message.IceMask);
            message.IceDoorRight = CaptureWindowCutoff(item.CutoffDoorRightVar, VehicleClimate.DoorRight, ref message.IceMask);
            message.IceRear = CaptureWindowCutoff(item.CutoffRearVar, VehicleClimate.Rear, ref message.IceMask);
        }

        private static byte CaptureWindowCutoff(HutongGames.PlayMaker.FsmFloat? source, byte bit, ref byte mask)
        {
            byte value;
            if (source == null || !VehicleClimate.TryQuantizeIce(source.Value, out value)) return 0;
            mask |= bit;
            return value;
        }

        private static void ApplyRemoteIceLevel(SyncedItem item)
        {
            // Missing source panes cannot borrow the windshield or overwrite a local pane.
            if (PaneScrapeSync.Instance?.OwnsPane(item.Id) != true)
                WriteCutoff(item.CutoffWindshieldVar, item.RemoteIce, item.RemoteIceMask, VehicleClimate.Windshield);
            WriteCutoff(item.CutoffSideLeftVar, item.RemoteIceSideLeft, item.RemoteIceMask, VehicleClimate.SideLeft);
            WriteCutoff(item.CutoffSideRightVar, item.RemoteIceSideRight, item.RemoteIceMask, VehicleClimate.SideRight);
            WriteCutoff(item.CutoffDoorLeftVar, item.RemoteIceDoorLeft, item.RemoteIceMask, VehicleClimate.DoorLeft);
            WriteCutoff(item.CutoffDoorRightVar, item.RemoteIceDoorRight, item.RemoteIceMask, VehicleClimate.DoorRight);
            WriteCutoff(item.CutoffRearVar, item.RemoteIceRear, item.RemoteIceMask, VehicleClimate.Rear);
        }

        private static void WriteCutoff(HutongGames.PlayMaker.FsmFloat? variable, byte cutoff, byte mask, byte bit)
        {
            if (variable != null && (mask & bit) != 0) variable.Value = VehicleClimate.DequantizeIce(cutoff);
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

            // Guarded: window heater button is a catalog SyncedControl (echo guard).
            FireKnobCommitState(item.WindowHeaterButtonFsm, state);
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

        // Interior glass frost only (GlassFrosting.Frost). Kept separate from exterior ice
        // (see ReadIce): a parked cold car is iced outside yet clear inside, and the old
        // Mathf.Max collapsed the two so observers force-frosted the interior to the ice level.
        private static float ReadFrost(SyncedItem item) =>
            item.FrostVar != null ? item.FrostVar.Value : 0f;

        private static bool ReadPlayerInCar(SyncedItem item)
        {
            // Native PlayerIn belongs to local entry/reset. Received occupancy is kept
            // separately: writing it here can turn an observer into a driver after timeout.
            if (item.PlayerInVar != null && item.PlayerInVar.Value) return true;
            var session = SessionManager.Instance;
            if (session == null) return false;
            if (session.IsHost && session.HasPassengerInVehicle(item.Id)) return true;
            var passenger = PassengerController.Instance;
            if (passenger != null && (passenger.IsLocalSeatedInVehicle(item.Id)
                || passenger.HasRemotePassengerInVehicle(item.Id))) return true;
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
            float temp = Mathf.Clamp(ReadCoolantTempC(item), 0f, CoolantTempMaxC);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(temp / CoolantTempMaxC * 255f), 0, 255);
        }

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
