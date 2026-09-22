using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private sealed class NativePassengerHeating
        {
            internal VehicleWorldSync Owner = null!;
            internal PassengerHeatingData Rule = null!;
            internal Transform Player = null!;
            internal PlayMakerFSM Body = null!;
            internal FsmState Ambient = null!, Heat = null!;
            internal FsmStateAction[] AmbientActions = null!, HeatActions = null!;
            internal FsmFloat Output = null!;
        }

        private NativePassengerHeating? _passengerHeating;
        private float _nextPassengerHeatingProbe, _nextPassengerHeatingError;
        private static bool _passengerHeatingHooked;
        private static readonly Dictionary<FsmStateAction, NativePassengerHeating> PassengerHeatingReaders
            = new Dictionary<FsmStateAction, NativePassengerHeating>();

        private void EnsurePassengerHeating()
        {
            try
            {
                if (_passengerHeating != null) { ValidatePassengerHeating(_passengerHeating); return; }
                if (Time.unscaledTime < _nextPassengerHeatingProbe) return;
                _nextPassengerHeatingProbe = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var rule = SyncCatalog.PassengerHeating; var player = _bridge.LocalPlayer;
                if (rule == null)
                {
                    if (SyncCatalog.PassengerHeatingError != null) throw new InvalidOperationException(SyncCatalog.PassengerHeatingError);
                    return;
                }
                if (player == null) return;
                var transform = player.Find(rule.BodyPath); if (transform == null) return;
                PlayMakerFSM? body = null;
                foreach (var candidate in transform.GetComponents<PlayMakerFSM>())
                    if (candidate.FsmName == rule.BodyFsm)
                    { if (body != null) throw new InvalidOperationException("Ambiguous body heating FSM."); body = candidate; }
                if (body == null || !body.Fsm.Initialized) return;
                var b = new NativePassengerHeating { Owner = this, Rule = rule, Player = player, Body = body,
                    Ambient = HeatState(body, rule.AmbientState), Heat = HeatState(body, rule.HeatState),
                    Output = body.FsmVariables.FindFsmFloat(rule.TemperatureVariable) };
                b.AmbientActions = (FsmStateAction[])b.Ambient.Actions.Clone(); b.HeatActions = (FsmStateAction[])b.Heat.Actions.Clone();
                ValidatePassengerHeating(b);
                if (!_passengerHeatingHooked)
                {
                    var method = b.AmbientActions[0].GetType().GetMethod("DoGetFsmFloat",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (method == null || method.GetMethodBody() == null || method.ReturnType != typeof(void))
                        throw new InvalidOperationException("Native passenger heating read boundary changed.");
                    new Harmony("com.ourwintercar.wintermp.passenger-heating").Patch(method,
                        postfix: new HarmonyMethod(typeof(VehicleWorldSync), nameof(AfterPassengerHeatRead)));
                    _passengerHeatingHooked = true;
                }
                _passengerHeating = b;
                PassengerHeatingReaders[b.AmbientActions[0]] = b; PassengerHeatingReaders[b.HeatActions[0]] = b;
                SyncEventLog.Record("passenger-heating-bound", rule.BodyPath + "::" + rule.BodyFsm);
            }
            catch (Exception error) { ClearPassengerHeating(); NotePassengerHeatingFailure(error); }
        }

        private static void ValidatePassengerHeating(NativePassengerHeating b)
        {
            var p = b.Rule; var body = b.Body;
            if (!ReferenceEquals(p, SyncCatalog.PassengerHeating) || b.Player == null || b.Player != b.Owner._bridge.LocalPlayer
                || body == null || !body.Fsm.Initialized || body.FsmName != p.BodyFsm || b.Player.Find(p.BodyPath) != body.transform)
                throw new InvalidOperationException("Passenger heating player/catalog changed.");
            CabinTemperatureState(body, p.AmbientState, b.Ambient, b.AmbientActions, 4);
            CabinTemperatureState(body, p.HeatState, b.Heat, b.HeatActions, 1);
            for (int i = 0; i < 4; i++) CabinTemperatureAction(b.AmbientActions[i], i == 0 ? "GetFsmFloat" : "IntCompare");
            CabinTemperatureAction(b.HeatActions[0], "GetFsmFloat");
            if (b.Output == null || !ReferenceEquals(b.Output, body.FsmVariables.FindFsmFloat(p.TemperatureVariable))
                || ReferenceEquals(b.Output, FsmVariables.GlobalVariables.FindFsmFloat(p.TemperatureVariable)))
                throw new InvalidOperationException("Passenger heating output changed.");
            ValidatePassengerHeatRead(b, b.AmbientActions[0], p.RainVariable, p.RainFsm);
            ValidatePassengerHeatRead(b, b.HeatActions[0], p.HeatVariable, p.HeatFsm);
            var rain = b.Player.Find(p.RainPath);
            if (rain == null || body.FsmVariables.FindFsmGameObject(p.RainVariable).Value != rain.gameObject)
                throw new InvalidOperationException("Passenger heating ambient source changed.");
        }

        private static void ValidatePassengerHeatRead(NativePassengerHeating b, FsmStateAction action, string targetVariable, string fsmName)
        {
            var target = TemperatureField(action, "gameObject") as FsmOwnerDefault;
            var native = b.Body.FsmVariables.FindFsmGameObject(targetVariable);
            if (native == null || target == null || target.OwnerOption != OwnerDefaultOption.SpecifyGameObject
                || !ReferenceEquals(target.GameObject, native) || !ReferenceEquals(TemperatureField(action, "storeValue"), b.Output)
                || !TemperatureLiteral(TemperatureField(action, "fsmName"), fsmName)
                || !TemperatureLiteral(TemperatureField(action, "variableName"), b.Rule.TemperatureVariable))
                throw new InvalidOperationException("Passenger heating reader changed.");
        }

        private static bool TryReadNativeCabinTemperature(SyncedItem item, out byte temperature)
        {
            temperature = 128; var data = item.CarTempDataFsm;
            return item.Body != null && data != null && data.Fsm.Initialized && data.FsmName == "Data"
                && data.transform.IsChildOf(item.Body.transform) && SyncCatalog.IsClimateVehicleFsmPath(ScenePath.Of(data.transform))
                && SyncCatalog.IsCarTempFsmPath(ScenePath.Of(data.transform)) && item.InteriorTempVar != null
                && ReferenceEquals(item.InteriorTempVar, data.FsmVariables.FindFsmFloat("InteriorTemp"))
                && VehicleClimate.TryQuantizeCabinTemperature(item.InteriorTempVar.Value, out temperature);
        }

        private bool TryReadPassengerCabinTemperature(out float temperature)
        {
            temperature = 0; var session = _bridge.Session; var passenger = PassengerController.Instance;
            if (session == null || (session.IsHost ? session.State != SessionState.Hosting : session.State != SessionState.Connected)
                || passenger == null || !passenger.TryGetLocalPassengerVehicle(out uint id)
                || DeathSyncManager.Instance != null && DeathSyncManager.Instance.IsLocalDead
                || !_items.Items.TryGetValue(id, out var item) || !item.IsVehicle || item.Body == null
                || _bridge.LocalPlayer == null || !_bridge.LocalPlayer.IsChildOf(item.Body.transform)) return false;
            if (session.IsHost && !session.IsPassengerInVehicle(session.LocalPlayerId, id)) return false;
            if (ShouldStreamVehicleClimate(item))
            {
                EnsureClimateProbe(item);
                if (!TryReadNativeCabinTemperature(item, out byte native)) return false;
                temperature = VehicleClimate.DequantizeCabinTemperature(native); return true;
            }
            var state = item.AcceptedVehicleClimate;
            if (state == null || !state.HasCabinTemperature || Time.unscaledTime >= item.RemoteClimateUntil || !CanPresentRemoteClimate(item)) return false;
            temperature = VehicleClimate.DequantizeCabinTemperature(state.CabinTemp); return true;
        }

        private static void AfterPassengerHeatRead(FsmStateAction __instance)
        {
            if (!PassengerHeatingReaders.TryGetValue(__instance, out var b)) return;
            try
            {
                ValidatePassengerHeating(b);
                // Replace only the body calculation's read result. Native source
                // objects/radii, lookup caches, clothing and thermal timing stay intact.
                if (b.Owner.TryReadPassengerCabinTemperature(out float temperature)) b.Output.Value = temperature;
            }
            catch (Exception error) { b.Owner.ClearPassengerHeating(); b.Owner.NotePassengerHeatingFailure(error); }
        }

        private void ClearPassengerHeating()
        {
            var b = _passengerHeating;
            if (b != null)
                foreach (var action in new[] { b.AmbientActions[0], b.HeatActions[0] })
                    if (PassengerHeatingReaders.TryGetValue(action, out var current) && ReferenceEquals(current, b)) PassengerHeatingReaders.Remove(action);
            _passengerHeating = null; _nextPassengerHeatingProbe = Time.unscaledTime + SystemsProbeIntervalSeconds;
        }

        private void NotePassengerHeatingFailure(Exception error)
        {
            if (Time.unscaledTime < _nextPassengerHeatingError) return;
            _nextPassengerHeatingError = Time.unscaledTime + 10f;
            try { SyncEventLog.Record("passenger-heating-unavailable", error.Message); } catch { /* Keep diagnostics out of native reads. */ }
        }
    }
}
