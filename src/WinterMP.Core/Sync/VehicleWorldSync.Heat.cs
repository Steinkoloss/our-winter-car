using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal sealed class NativeHeatBinding
        {
            internal SyncedItem Item = null!;
            internal VehicleHeatData Rule = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmState Running = null!, Stopped = null!;
            internal FsmStateAction[] RunningActions = null!, StoppedActions = null!;
            internal FsmObject Target = null!;
            internal Component Drive = null!;
            internal FsmFloat Rpm = null!, Torque = null!, Temperature = null!;
            internal readonly List<NativeHeatRead> Reads = new List<NativeHeatRead>();
        }

        internal sealed class NativeHeatRead
        {
            internal NativeHeatBinding Owner = null!;
            internal FsmStateAction Action = null!;
            internal FsmFloat Original = null!;
            internal readonly FsmFloat Input = new FsmFloat();
            internal FieldInfo[] Fields = null!;
            internal bool Torque;
            internal int Depth;
        }

        private static void EnsureHeatBinding(SyncedItem item)
        {
            var rule = SyncCatalog.VehicleHeat;
            try
            {
                if (item.NativeHeat != null) { ValidateHeatBinding(item.NativeHeat); return; }
                if (rule == null || item.Body == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextHeatProbeAt) return;
                item.NextHeatProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var fsm = FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), rule.Path, rule.Fsm);
                var binding = new NativeHeatBinding { Item = item, Rule = rule, Fsm = fsm,
                    Running = HeatState(fsm, rule.RunningState), Stopped = HeatState(fsm, rule.StoppedState),
                    Target = fsm.FsmVariables.FindFsmObject(rule.ObjectVariable),
                    Rpm = FsmVariables.GlobalVariables.FindFsmFloat(rule.RpmGlobal),
                    Temperature = FsmVariables.GlobalVariables.FindFsmFloat(rule.TemperatureGlobal),
                    Torque = fsm.FsmVariables.FindFsmFloat(rule.TorqueVariable) };
                binding.Drive = binding.Target?.Value as Component ?? throw new InvalidOperationException("Native heat drivetrain missing.");
                binding.RunningActions = (FsmStateAction[])binding.Running.Actions.Clone();
                binding.StoppedActions = (FsmStateAction[])binding.Stopped.Actions.Clone();
                if (binding.RunningActions.Length != 11 || binding.StoppedActions.Length != 1)
                    throw new InvalidOperationException("Native heating graph changed.");
                AddHeatRead(binding, binding.RunningActions[3], false, "float1", "float2");
                AddHeatRead(binding, binding.RunningActions[5], true, "float2");
                AddHeatRead(binding, binding.RunningActions[10], false, "float1");
                AddHeatRead(binding, binding.StoppedActions[0], false, "float1");
                ValidateHeatBinding(binding);
                EnsureHeatHooks(binding);
                item.NativeHeat = binding;
                foreach (var read in binding.Reads) HeatReaders[read.Action] = read;
                SyncEventLog.Record("vehicle-heat-bound", item.Path);
            }
            catch (Exception error) { ClearHeatBinding(item); NoteHeatFailure(item, error); }
        }

        private static FsmState HeatState(PlayMakerFSM fsm, string name)
        {
            FsmState? found = null;
            foreach (var state in fsm.Fsm.States)
                if (state.Name == name) { if (found != null) throw new InvalidOperationException("Ambiguous heat state."); found = state; }
            if (found == null) throw new InvalidOperationException("Missing heat state.");
            if (!found.IsInitialized) throw new InvalidOperationException("Native vehicle state is not initialized yet.");
            return found;
        }

        private static void AddHeatRead(NativeHeatBinding b, FsmStateAction action, bool torque, params string[] names)
        {
            var fields = new FieldInfo[names.Length];
            for (int i = 0; i < names.Length; i++) fields[i] = NativeRpmField(action.GetType(), names[i], typeof(FsmFloat));
            b.Reads.Add(new NativeHeatRead { Owner = b, Action = action, Torque = torque, Fields = fields, Original = torque ? b.Torque : b.Rpm });
        }

        private static void ValidateHeatBinding(NativeHeatBinding b)
        {
            var item = b.Item; var rule = b.Rule; var fsm = b.Fsm;
            if (!ReferenceEquals(rule, SyncCatalog.VehicleHeat) || item.Body == null || item.Path != rule.RootPath
                || ScenePath.Of(item.Body.transform) != rule.RootPath || fsm == null || !fsm.Fsm.Initialized
                || fsm.FsmName != rule.Fsm || ScenePath.Of(fsm.transform) != rule.Path || !fsm.transform.IsChildOf(item.Body.transform)
                || FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), rule.Path, rule.Fsm) != fsm
                || b.Drive == null || b.Target == null || b.Drive.gameObject != item.Body.gameObject
                || b.Drive.GetType().FullName != rule.ComponentType || b.Drive.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || b.Target.Value != b.Drive || b.Target.TypeName != rule.ComponentType || b.Target.ObjectType != b.Drive.GetType()
                || !ReferenceEquals(b.Target, fsm.FsmVariables.FindFsmObject(rule.ObjectVariable))
                || b.Rpm == null || b.Temperature == null || b.Torque == null
                || !ReferenceEquals(b.Rpm, FsmVariables.GlobalVariables.FindFsmFloat(rule.RpmGlobal))
                || !ReferenceEquals(b.Temperature, FsmVariables.GlobalVariables.FindFsmFloat(rule.TemperatureGlobal))
                || !ReferenceEquals(b.Torque, fsm.FsmVariables.FindFsmFloat(rule.TorqueVariable))
                || fsm.FsmVariables.FindFsmFloat(rule.RpmGlobal) != null || fsm.FsmVariables.FindFsmFloat(rule.TemperatureGlobal) != null)
                throw new InvalidOperationException("Native heat references changed.");
            HeatActions(b, b.Running, rule.RunningState, b.RunningActions, rule.StoppedState);
            HeatActions(b, b.Stopped, rule.StoppedState, b.StoppedActions, rule.RunningState);
            var a = b.RunningActions;
            HeatAction(a[0], "GetFsmFloat");
            var target = TemperatureField(a[0], "gameObject") as FsmOwnerDefault;
            if (target == null || target.OwnerOption != OwnerDefaultOption.UseOwner
                || !TemperatureLiteral(TemperatureField(a[0], "fsmName"), "EngineFriction")
                || !TemperatureLiteral(TemperatureField(a[0], "variableName"), "FrictionToHeat"))
                throw new InvalidOperationException("Native heat friction source changed.");
            HeatLocal(b, a[0], "storeValue", "HeatProduceModifier");
            HeatProperty(b, a[1], rule.TorqueMember, rule.TorqueVariable);
            HeatProperty(b, a[2], "currentPower", "PowerCurrent");
            HeatOperator(b, a[3], "Multiply", rule.RpmGlobal, rule.RpmGlobal, "RPM2");
            HeatOperator(b, a[4], "Divide", "RPM2", "Power2HeatDiv", "Power2HeatMultip");
            HeatOperator(b, a[5], "Multiply", "Power2HeatMultip", rule.TorqueVariable, "Math1");
            HeatOperator(b, a[6], "Divide", "Math1", "HeatProduceModifier", "HeatingRate");
            HeatAction(a[7], "FloatOperator"); HeatLocal(b, a[7], "float1", "HeatingRate"); HeatLocal(b, a[7], "storeResult", "HeatingRate");
            HeatAction(a[8], "FloatClamp"); HeatLocal(b, a[8], "floatVariable", "HeatingRate");
            float minimum = TemperatureConstant(TemperatureField(a[8], "minValue"));
            if (TemperatureField(a[7], "operation").ToString() != "Add" || TemperatureConstant(TemperatureField(a[7], "float2")) != minimum
                || minimum < 0 || TemperatureConstant(TemperatureField(a[8], "maxValue")) < minimum)
                throw new InvalidOperationException("Native heat clamp changed.");
            HeatAction(a[9], "FloatAdd"); HeatLocal(b, a[9], "add", "HeatingRate");
            if (!ReferenceEquals(TemperatureField(a[9], "floatVariable"), b.Temperature) || !(bool)TemperatureField(a[9], "perSecond"))
                throw new InvalidOperationException("Native heat writer changed.");
            float stop = HeatCompare(b, a[10], true), start = HeatCompare(b, b.StoppedActions[0], false);
            if (stop < 0 || start <= stop) throw new InvalidOperationException("Native heating thresholds inverted.");
        }

        private static void HeatActions(NativeHeatBinding b, FsmState state, string name, FsmStateAction[] actions, string destination)
        {
            if (HeatState(b.Fsm, name) != state || state.Actions.Length != actions.Length
                || state.Transitions.Length != 1 || state.Transitions[0].EventName != "PROCEED" || state.Transitions[0].ToState != destination)
                throw new InvalidOperationException("Heat state transition changed.");
            for (int i = 0; i < actions.Length; i++)
                if (!ReferenceEquals(state.Actions[i], actions[i]) || !actions[i].Enabled)
                    throw new InvalidOperationException("Heat action changed.");
        }

        private static void HeatAction(FsmStateAction action, string type) => TemperatureAction(action, type);

        private static void HeatLocal(NativeHeatBinding b, FsmStateAction action, string field, string name)
        {
            var expected = name == b.Rule.RpmGlobal ? b.Rpm : b.Fsm.FsmVariables.FindFsmFloat(name);
            object actual = TemperatureField(action, field);
            foreach (var read in b.Reads)
                if (read.Action == action && read.Depth > 0)
                    foreach (var operand in read.Fields)
                        if (operand.Name == field && ReferenceEquals(actual, read.Input)) actual = read.Original;
            if (expected == null || !ReferenceEquals(actual, expected)) throw new InvalidOperationException("Heat operand changed: " + name);
        }

        private static void HeatOperator(NativeHeatBinding b, FsmStateAction action, string operation, string first, string second, string output)
        {
            HeatAction(action, "FloatOperator");
            if (TemperatureField(action, "operation").ToString() != operation) throw new InvalidOperationException("Heat operation changed.");
            HeatLocal(b, action, "float1", first); HeatLocal(b, action, "float2", second); HeatLocal(b, action, "storeResult", output);
        }

        private static void HeatProperty(NativeHeatBinding b, FsmStateAction action, string member, string output)
        {
            HeatAction(action, "GetProperty");
            var property = TemperatureField(action, "targetProperty") as FsmProperty;
            var native = b.Drive.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance);
            if (native == null || native.FieldType != typeof(float) || property == null || property.setProperty
                || property.PropertyName != member || property.TargetTypeName != b.Rule.ComponentType
                || !ReferenceEquals(property.TargetObject, b.Target) || !ReferenceEquals(property.FloatParameter, b.Fsm.FsmVariables.FindFsmFloat(output)))
                throw new InvalidOperationException("Native heat drivetrain read changed.");
        }

        private static float HeatCompare(NativeHeatBinding b, FsmStateAction action, bool stop)
        {
            HeatAction(action, "FloatCompare"); HeatLocal(b, action, "float1", b.Rule.RpmGlobal);
            string Event(string field) => action.GetType().GetField(field).GetValue(action) is FsmEvent value ? value.Name ?? "" : "";
            if (TemperatureConstant(TemperatureField(action, "tolerance")) != 0 || Event("equal") != ""
                || Event(stop ? "lessThan" : "greaterThan") != "PROCEED" || Event(stop ? "greaterThan" : "lessThan") != "")
                throw new InvalidOperationException("Heating threshold events changed.");
            return TemperatureConstant(TemperatureField(action, "float2"));
        }

        private static void CaptureHeatTelemetry(SyncedItem item, VehicleState state)
        {
            state.TorqueAvailable = false; state.EngineTorque = 0;
            EnsureHeatBinding(item);
            var b = item.NativeHeat;
            if (b == null || !b.Fsm.enabled || !b.Fsm.gameObject.activeInHierarchy || !b.Fsm.Fsm.Started
                || b.Fsm.ActiveStateName != b.Rule.RunningState || !FiniteTemperature(b.Torque.Value)) return;
            state.TorqueAvailable = true; state.EngineTorque = b.Torque.Value;
        }

        private static void ClearHeatBinding(SyncedItem item)
        {
            if (item.NativeHeat != null)
                foreach (var read in item.NativeHeat.Reads)
                    if (HeatReaders.TryGetValue(read.Action, out var current) && ReferenceEquals(current, read)) HeatReaders.Remove(read.Action);
            item.NativeHeat = null;
        }

        private static void NoteHeatFailure(SyncedItem item, Exception error)
        {
            item.NextHeatProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            if (Time.unscaledTime < item.NextHeatErrorAt) return;
            item.NextHeatErrorAt = Time.unscaledTime + 10;
            try { SyncEventLog.Record("vehicle-heat-unavailable", item.Path + ": " + error.Message); } catch { }
        }
    }
}
