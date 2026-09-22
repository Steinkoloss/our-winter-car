using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal sealed class NativeElectricalInputs
        {
            internal SyncedItem Item = null!;
            internal VehicleElectricalData Rule = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmFloat Rpm = null!;
            internal NativeElectricalRead[] Reads = null!;
        }

        internal sealed class NativeElectricalRead
        {
            internal NativeElectricalInputs Owner = null!;
            internal FsmState State = null!;
            internal FsmStateAction[] Actions = null!;
            internal FsmStateAction Action = null!;
            internal FieldInfo Field = null!;
            internal readonly FsmFloat Input = new FsmFloat();
            internal int Depth;
        }

        private static void EnsureElectricalInputs(SyncedItem item)
        {
            if (item.Body == null) return;
            try
            {
                if (item.NativeElectrical != null) { ValidateElectricalInputs(item.NativeElectrical); return; }
                var rule = SyncCatalog.VehicleElectrical;
                if (rule == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextElectricalInputProbeAt) return;
                item.NextElectricalInputProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var b = new NativeElectricalInputs { Item = item, Rule = rule,
                    Fsm = FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), rule.Path, rule.Fsm),
                    Rpm = FsmVariables.GlobalVariables.FindFsmFloat(rule.RpmGlobal) };
                b.Reads = new[] { ElectricalRead(b, rule.RunningState, 0, 3), ElectricalRead(b, rule.ChargingState, 1, 9), ElectricalRead(b, rule.BatteryState, 0, 3) };
                ValidateElectricalInputs(b); EnsureElectricalHooks(b); item.NativeElectrical = b;
                foreach (var read in b.Reads) ElectricalReaders[read.Action] = read;
                SyncEventLog.Record("vehicle-electrical-inputs-bound", item.Path);
            }
            catch (Exception error) { ClearElectricalInputs(item); NoteElectricalInputFailure(item, error); }
        }

        private static NativeElectricalRead ElectricalRead(NativeElectricalInputs b, string name, int index, int count)
        {
            var state = HeatState(b.Fsm, name);
            if (state.Actions.Length != count) throw new InvalidOperationException("Electrical RPM state layout changed.");
            var action = state.Actions[index];
            return new NativeElectricalRead { Owner = b, State = state, Actions = (FsmStateAction[])state.Actions.Clone(), Action = action,
                Field = NativeRpmField(action.GetType(), "float1", typeof(FsmFloat)) };
        }

        private static void ValidateElectricalInputs(NativeElectricalInputs b)
        {
            var rule = b.Rule; var item = b.Item; var fsm = b.Fsm;
            if (!ReferenceEquals(rule, SyncCatalog.VehicleElectrical) || item.Body == null || item.Path != rule.RootPath
                || ScenePath.Of(item.Body.transform) != rule.RootPath || b.Rpm == null
                || !ReferenceEquals(b.Rpm, FsmVariables.GlobalVariables.FindFsmFloat(rule.RpmGlobal))
                || fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != rule.Fsm || ScenePath.Of(fsm.transform) != rule.Path
                || !fsm.transform.IsChildOf(item.Body.transform) || fsm.FsmVariables.FindFsmFloat(rule.RpmGlobal) != null || b.Reads.Length != 3)
                throw new InvalidOperationException("Electrical RPM source or vehicle changed.");
            int count = 0; foreach (var candidate in fsm.GetComponents<PlayMakerFSM>()) if (candidate.FsmName == rule.Fsm) count++;
            if (count != 1) throw new InvalidOperationException("Ambiguous electrical RPM FSM.");
            string[] states = { rule.RunningState, rule.ChargingState, rule.BatteryState };
            for (int i = 0; i < b.Reads.Length; i++)
            {
                var read = b.Reads[i]; CoolingActions(fsm, read.State, states[i], read.Actions);
                CoolingAction(read.Action, i == 0 ? "FloatCompare" : "FloatOperator", false);
                if (!ReferenceEquals(read.Field.GetValue(read.Action), read.Depth > 0 ? read.Input : b.Rpm))
                    throw new InvalidOperationException("Electrical RPM operand changed.");
            }
            var running = b.Reads[0]; CoolingCompare(running.Action, 400, "", "", "PROCEED");
            CoolingTransitions(running.State, "FINISHED", "No charge", "PROCEED", "Alternator eff");
            var charging = b.Reads[1]; var battery = b.Reads[2];
            if (TemperatureField(charging.Action, "operation").ToString() != "Divide"
                || TemperatureField(battery.Action, "operation").ToString() != "Divide"
                || TemperatureConstant(TemperatureField(battery.Action, "float2")) != 60000)
                throw new InvalidOperationException("Electrical RPM arithmetic changed.");
            ElectricalRpmLocal(b, charging.Action, "float2", "AlternatorEfficiency"); ElectricalRpmLocal(b, charging.Action, "storeResult", "Charging");
            ElectricalRpmLocal(b, battery.Action, "storeResult", "Revs");
            var clamp = battery.Actions[1]; CoolingAction(clamp, "FloatClamp", false); ElectricalRpmLocal(b, clamp, "floatVariable", "Revs");
            if (TemperatureConstant(TemperatureField(clamp, "minValue")) != .00001f || TemperatureConstant(TemperatureField(clamp, "maxValue")) != 1)
                throw new InvalidOperationException("Native battery drain limits changed.");
            CoolingTransitions(charging.State, "FINISHED", "Wear", "LIGHT", "Light on");
            CoolingTransitions(battery.State, "PROCEED", "Delay", "FINISHED", "Engine off");
        }

        private static void ElectricalRpmLocal(NativeElectricalInputs b, FsmStateAction action, string field, string name)
        {
            var value = b.Fsm.FsmVariables.FindFsmFloat(name);
            if (value == null || ReferenceEquals(value, b.Rpm) || ReferenceEquals(value, FsmVariables.GlobalVariables.FindFsmFloat(name))
                || !ReferenceEquals(value, TemperatureField(action, field)))
                throw new InvalidOperationException("Electrical RPM scratch variable changed.");
        }

        private static void ClearElectricalInputs(SyncedItem item)
        {
            if (item.NativeElectrical != null)
                foreach (var read in item.NativeElectrical.Reads)
                    if (ElectricalReaders.TryGetValue(read.Action, out var current) && ReferenceEquals(current, read)) ElectricalReaders.Remove(read.Action);
            item.NativeElectrical = null;
        }

        private static void NoteElectricalInputFailure(SyncedItem item, Exception error)
        {
            item.NextElectricalInputProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            if (Time.unscaledTime < item.NextElectricalInputErrorAt) return;
            item.NextElectricalInputErrorAt = Time.unscaledTime + 10;
            try { SyncEventLog.Record("vehicle-electrical-inputs-unavailable", item.Path + ": " + error.Message); }
            catch { /* Diagnostics cannot escape into native charging or battery drain. */ }
        }
    }
}
