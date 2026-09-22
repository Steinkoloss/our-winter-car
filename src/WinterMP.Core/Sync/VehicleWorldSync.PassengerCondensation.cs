using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal sealed class NativePassengerCondensation
        {
            internal VehicleWorldSync Owner = null!;
            internal SyncedItem Item = null!;
            internal PassengerCondensationData Rule = null!;
            internal PlayMakerFSM Fsm = null!;
            internal string Path = "";
            internal FsmState State = null!;
            internal FsmStateAction[] Actions = null!;
            internal FsmBool LocalEntry = null!;
            internal FsmFloat LocalSweat = null!;
            internal FieldInfo EntryOperand = null!, SweatOperand = null!;
            internal readonly FsmBool EntryInput = new FsmBool();
            internal readonly FsmFloat SweatInput = new FsmFloat();
            internal int EntryDepth, SweatDepth;
        }

        internal static bool TryReadLocalSweat(out float value)
        {
            value = 0f;
            var rule = SyncCatalog.PassengerCondensation;
            if (rule == null) return false;
            var source = FsmVariables.GlobalVariables.FindFsmFloat(rule.SweatGlobal);
            if (source == null || !PassengerCondensationPolicy.ValidSweat(source.Value)) return false;
            value = source.Value;
            return true;
        }

        private void EnsurePassengerCondensation(SyncedItem item)
        {
            if (item.Body == null || item.GlassFrostingFsm == null || SyncCatalog.PassengerCondensation == null) return;
            try
            {
                if (item.PassengerCondensation != null) { ValidatePassengerCondensation(item.PassengerCondensation); return; }
                if (Time.unscaledTime < item.NextPassengerCondensationProbeAt) return;
                item.NextPassengerCondensationProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var fsm = item.GlassFrostingFsm; var rule = SyncCatalog.PassengerCondensation;
                var b = new NativePassengerCondensation { Owner = this, Item = item, Rule = rule, Fsm = fsm,
                    Path = ScenePath.Of(fsm.transform), State = HeatState(fsm, rule.State),
                    LocalEntry = fsm.FsmVariables.FindFsmBool(rule.EntryVariable),
                    LocalSweat = FsmVariables.GlobalVariables.FindFsmFloat(rule.SweatGlobal) };
                b.Actions = (FsmStateAction[])b.State.Actions.Clone();
                if (b.Actions.Length != 6) throw new InvalidOperationException("Passenger condensation state changed.");
                b.EntryOperand = b.Actions[2].GetType().GetField("boolVariable", BindingFlags.Public | BindingFlags.Instance);
                b.SweatOperand = b.Actions[3].GetType().GetField("float1", BindingFlags.Public | BindingFlags.Instance);
                ValidatePassengerCondensation(b); EnsurePassengerCondensationHooks(b);
                item.PassengerCondensation = b;
                PassengerCondensationReaders[b.Actions[2]] = b; PassengerCondensationReaders[b.Actions[3]] = b;
                SyncEventLog.Record("passenger-condensation-bound", item.Path);
            }
            catch (Exception error)
            {
                ClearPassengerCondensation(item); NotePassengerCondensationFailure(item, error);
                item.NextPassengerCondensationProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            }
        }

        private static void ValidatePassengerCondensation(NativePassengerCondensation b)
        {
            var fsm = b.Fsm; var p = b.Rule;
            if (!ReferenceEquals(p, SyncCatalog.PassengerCondensation) || b.Item.Body == null || fsm == null
                || fsm != b.Item.GlassFrostingFsm || fsm.FsmName != "GlassFrosting" || !fsm.Fsm.Initialized
                || ScenePath.Of(fsm.transform) != b.Path || !fsm.transform.IsChildOf(b.Item.Body.transform)
                || !SyncCatalog.IsClimateVehicleFsmPath(b.Path) || !SyncCatalog.IsCarTempFsmPath(b.Path))
                throw new InvalidOperationException("Passenger condensation vehicle/catalog changed.");
            CabinTemperatureState(fsm, p.State, b.State, b.Actions, 6);
            string[] types = { "SetFloatValue", "SetFloatValue", "BoolTest", "FloatOperator", "FloatClamp", "SetFloatValue" };
            for (int i = 0; i < types.Length; i++) CabinTemperatureAction(b.Actions[i], types[i]);
            if (b.LocalEntry == null || b.LocalSweat == null || b.EntryOperand == null || b.SweatOperand == null
                || !ReferenceEquals(b.LocalEntry, fsm.FsmVariables.FindFsmBool(p.EntryVariable))
                || !ReferenceEquals(b.LocalSweat, FsmVariables.GlobalVariables.FindFsmFloat(p.SweatGlobal))
                || fsm.FsmVariables.FindFsmFloat(p.SweatGlobal) != null
                || !ReferenceEquals(b.EntryOperand.GetValue(b.Actions[2]), b.EntryDepth > 0 ? b.EntryInput : b.LocalEntry)
                || !ReferenceEquals(b.SweatOperand.GetValue(b.Actions[3]), b.SweatDepth > 0 ? b.SweatInput : b.LocalSweat))
                throw new InvalidOperationException("Passenger condensation input references changed.");
            CabinTemperatureLocal(fsm, b.Actions[0], "floatVariable", p.DefrostingRate);
            CabinTemperatureLocal(fsm, b.Actions[1], "floatVariable", p.RateVariable);
            CabinTemperatureLocal(fsm, b.Actions[1], "floatValue", p.DefaultRate);
            CabinTemperatureLocal(fsm, b.Actions[3], "storeResult", p.SweatVariable);
            CabinTemperatureLocal(fsm, b.Actions[4], "floatVariable", p.SweatVariable);
            CabinTemperatureLocal(fsm, b.Actions[5], "floatVariable", p.RateVariable);
            CabinTemperatureLocal(fsm, b.Actions[5], "floatValue", p.SweatVariable);
            var yes = TemperatureField(b.Actions[2], "isTrue") as FsmEvent;
            var no = TemperatureField(b.Actions[2], "isFalse") as FsmEvent;
            if (yes != null && !string.IsNullOrEmpty(yes.Name) || no == null || no.Name != "FINISHED"
                || TemperatureField(b.Actions[3], "operation").ToString() != "Divide"
                || TemperatureConstant(TemperatureField(b.Actions[0], "floatValue")) != 0f
                || TemperatureConstant(TemperatureField(b.Actions[3], "float2")) != 300f
                || TemperatureConstant(TemperatureField(b.Actions[4], "minValue")) != .02f
                || TemperatureConstant(TemperatureField(b.Actions[4], "maxValue")) != .1f)
                throw new InvalidOperationException("Passenger condensation arithmetic/events changed.");
        }

        private bool TryPassengerCondensation(NativePassengerCondensation b, out float sweat)
        {
            sweat = 0f;
            var session = _bridge.Session;
            if (session == null || (session.IsHost ? session.State != SessionState.Hosting : session.State != SessionState.Connected)
                || !ShouldStreamVehicleClimate(b.Item) || !_items.Items.TryGetValue(b.Item.Id, out var item)
                || !ReferenceEquals(item, b.Item)) return false;
            var passengers = PassengerController.Instance;
            bool alive = DeathSyncManager.Instance == null || !DeathSyncManager.Instance.IsLocalDead;
            if (alive && (b.LocalEntry.Value || _items.IsLocalPlayerDriving(item)
                || passengers != null && passengers.IsLocalSeatedInVehicle(item.Id)))
                sweat = PassengerCondensationPolicy.AddOccupant(sweat,
                    PassengerCondensationPolicy.ValidSweat(b.LocalSweat.Value) ? b.LocalSweat.Value : 0f);
            foreach (var player in session.Players)
            {
                if (player.PlayerId == session.LocalPlayerId || player.IsDead
                    || !PassengerCondensationPolicy.Fresh(Time.unscaledTime, player.LastTransformTime)) continue;
                bool seated = session.IsHost ? session.IsPassengerInVehicle(player.PlayerId, item.Id)
                    : passengers != null && passengers.IsRemotePassengerInVehicle(player.PlayerId, item.Id);
                if (seated) sweat = PassengerCondensationPolicy.AddOccupant(sweat, player.HasSweat ? player.Sweat : 0f);
            }
            return true;
        }

        private static void ClearPassengerCondensation(SyncedItem item)
        {
            var b = item.PassengerCondensation;
            if (b != null)
                foreach (var action in new[] { b.Actions[2], b.Actions[3] })
                    if (PassengerCondensationReaders.TryGetValue(action, out var current) && ReferenceEquals(current, b))
                        PassengerCondensationReaders.Remove(action);
            item.PassengerCondensation = null;
        }

        private void ClearPassengerCondensationBindings()
        {
            foreach (var b in new List<NativePassengerCondensation>(PassengerCondensationReaders.Values))
                if (ReferenceEquals(b.Owner, this)) ClearPassengerCondensation(b.Item);
        }

        private static void NotePassengerCondensationFailure(SyncedItem item, Exception error)
        {
            if (Time.unscaledTime < item.NextPassengerCondensationErrorAt) return;
            item.NextPassengerCondensationErrorAt = Time.unscaledTime + 10f;
            try { SyncEventLog.Record("passenger-condensation-unavailable", item.Path + ": " + error.Message); }
            catch { /* Diagnostics cannot escape into native condensation. */ }
        }
    }
}
