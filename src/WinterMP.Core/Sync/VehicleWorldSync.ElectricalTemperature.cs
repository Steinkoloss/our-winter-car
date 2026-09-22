using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private static void EnsureElectricalTemperatureInputs(SyncedItem item)
        {
            if (item.Body == null) return;
            try
            {
                if (item.NativeElectricalTemperature != null)
                { ValidateElectricalTemperatureInputs(item.NativeElectricalTemperature); return; }
                var rule = HostCoolantRule(item.Id);
                if (rule?.ElectricalInputs == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextElectricalTemperatureProbeAt) return;
                item.NextElectricalTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var p = rule.ElectricalInputs; var b = new NativeEngineTemperatureInputs { Item = item, Rule = rule, Electrical = true };
                var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                AddEngineTemperatureRead(b, fsms, p.ElectricsPath, p.ElectricsFsm, p.BatteryState, 1, 5, "SetFloatValue", "floatValue");
                AddEngineTemperatureRead(b, fsms, p.ElectricsPath, p.ElectricsFsm, p.ChargingState, 2, 9, "FloatOperator", "float1");
                AddEngineTemperatureRead(b, fsms, p.InteriorPath, p.InteriorFsm, p.InteriorBatteryState, 1, 5, "SetFloatValue", "floatValue");
                ValidateElectricalTemperatureInputs(b); EnsureEngineTemperatureHooks(b);
                item.NativeElectricalTemperature = b;
                foreach (var read in b.Reads) EngineTemperatureReaders[read.Action] = read;
                SyncEventLog.Record("electrical-temperature-inputs-bound", item.Path);
            }
            catch (Exception error)
            {
                ClearElectricalTemperatureInputs(item); NoteElectricalTemperatureFailure(item, error);
                item.NextElectricalTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            }
        }

        private static void ValidateElectricalTemperatureInputs(NativeEngineTemperatureInputs b)
        {
            var item = b.Item; var rule = b.Rule; var p = rule.ElectricalInputs;
            if (!b.Electrical || !ReferenceEquals(HostCoolantRule(item.Id), rule) || p == null || item.Body == null
                || item.Path != rule.RootPath || ScenePath.Of(item.Body.transform) != rule.RootPath || b.Reads.Count != 3)
                throw new InvalidOperationException("Electrical temperature catalog or vehicle changed.");
            for (int i = 0; i < b.Reads.Count; i++)
            {
                var read = b.Reads[i]; bool interior = i == 2, charging = i == 1;
                if (read.Path != (interior ? p.InteriorPath : p.ElectricsPath) || read.Name != (interior ? p.InteriorFsm : p.ElectricsFsm)
                    || read.StateName != (interior ? p.InteriorBatteryState : charging ? p.ChargingState : p.BatteryState))
                    throw new InvalidOperationException("Electrical temperature reader catalog changed.");
                ValidateEngineTemperatureReader(read, false);
                var actions = read.Actions;
                if (charging)
                {
                    EngineTemperatureLocal(read, "storeResult", "Math1");
                    if (TemperatureField(read.Action, "operation").ToString() != "Add" || TemperatureConstant(TemperatureField(read.Action, "float2")) != 51)
                        throw new InvalidOperationException("Charging temperature adjustment changed.");
                    ElectricalTemperatureAction(actions[3], "FloatDivide"); ElectricalTemperatureLocal(read, actions[3], "floatVariable", "Math1");
                    if (TemperatureConstant(TemperatureField(actions[3], "divideBy")) != 570)
                        throw new InvalidOperationException("Charging temperature scale changed.");
                    ElectricalTemperatureAction(actions[4], "FloatClamp"); ElectricalTemperatureLocal(read, actions[4], "floatVariable", "Math1");
                    if (TemperatureConstant(TemperatureField(actions[4], "minValue")) != .0001f || TemperatureConstant(TemperatureField(actions[4], "maxValue")) != .55f)
                        throw new InvalidOperationException("Charging temperature limits changed.");
                    ElectricalTemperatureAction(actions[5], "FloatClamp"); ElectricalTemperatureLocal(read, actions[5], "floatVariable", "Charging");
                    ElectricalTemperatureLocal(read, actions[5], "maxValue", "Math1");
                    if (TemperatureConstant(TemperatureField(actions[5], "minValue")) != 0)
                        throw new InvalidOperationException("Charging minimum changed.");
                }
                else
                {
                    EngineTemperatureLocal(read, "floatVariable", "BatteryTemp");
                    ElectricalTemperatureAction(actions[2], "FloatClamp"); ElectricalTemperatureLocal(read, actions[2], "floatVariable", "BatteryTemp");
                    if (TemperatureConstant(TemperatureField(actions[2], "minValue")) != -20 || TemperatureConstant(TemperatureField(actions[2], "maxValue")) != -.1f)
                        throw new InvalidOperationException("Battery cold penalty changed.");
                    ElectricalTemperatureAction(actions[3], "FloatAdd"); ElectricalTemperatureLocal(read, actions[3], "floatVariable", "Charge");
                    ElectricalTemperatureLocal(read, actions[3], "add", "BatteryTemp");
                    if ((bool)TemperatureField(actions[3], "perSecond")) throw new InvalidOperationException("Battery cold penalty cadence changed.");
                    var compare = actions[4]; ElectricalTemperatureAction(compare, "FloatCompare");
                    ElectricalTemperatureLocal(read, compare, "float1", "Charge"); ElectricalTemperatureLocal(read, compare, "float2", "VoltageLimit");
                    string Event(string name) => compare.GetType().GetField(name).GetValue(compare) is FsmEvent e ? e.Name ?? "" : "";
                    if (TemperatureConstant(TemperatureField(compare, "tolerance")) != 0 || Event("equal") != ""
                        || Event("lessThan") != "FINISHED" || Event("greaterThan") != "PROCEED")
                        throw new InvalidOperationException("Battery voltage decision changed.");
                    CoolingTransitions(read.State, "FINISHED", interior ? "State 1" : "Not Ok", "PROCEED", interior ? "State 2" : "Fuel gauge");
                }
            }
        }

        private static void ElectricalTemperatureAction(FsmStateAction action, string name)
        {
            if (!action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + name
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp" || (bool)TemperatureField(action, "everyFrame"))
                throw new InvalidOperationException("Electrical temperature action signature changed.");
        }

        private static void ElectricalTemperatureLocal(NativeEngineTemperatureRead read, FsmStateAction action, string field, string name)
        {
            var value = read.Fsm.FsmVariables.FindFsmFloat(name);
            if (value == null || ReferenceEquals(value, read.Original) || ReferenceEquals(value, FsmVariables.GlobalVariables.FindFsmFloat(name))
                || !ReferenceEquals(value, TemperatureField(action, field)))
                throw new InvalidOperationException("Electrical temperature scratch variable changed.");
        }

        private static void ClearElectricalTemperatureInputs(SyncedItem item)
        {
            var b = item.NativeElectricalTemperature;
            if (b != null)
                foreach (var read in b.Reads)
                    if (EngineTemperatureReaders.TryGetValue(read.Action, out var current) && ReferenceEquals(current, read))
                        EngineTemperatureReaders.Remove(read.Action);
            item.NativeElectricalTemperature = null;
        }

        private static void ClearTemperatureGroup(NativeEngineTemperatureInputs b)
        {
            if (b.Electrical)
            { if (ReferenceEquals(b.Item.NativeElectricalTemperature, b)) ClearElectricalTemperatureInputs(b.Item); }
            else if (ReferenceEquals(b.Item.NativeEngineTemperature, b)) ClearEngineTemperatureInputs(b.Item);
        }

        private static void NoteTemperatureGroupFailure(NativeEngineTemperatureInputs b, Exception error)
        {
            if (b.Electrical) NoteElectricalTemperatureFailure(b.Item, error);
            else NoteEngineTemperatureFailure(b.Item, error);
        }

        private static void NoteElectricalTemperatureFailure(SyncedItem item, Exception error)
        {
            item.NextElectricalTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            if (Time.unscaledTime < item.NextElectricalTemperatureErrorAt) return;
            item.NextElectricalTemperatureErrorAt = Time.unscaledTime + 10f;
            try { SyncEventLog.Record("electrical-temperature-inputs-unavailable", item.Path + ": " + error.Message); }
            catch { /* Diagnostics cannot escape into native battery calculations. */ }
        }
    }
}
