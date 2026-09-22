using System;
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
        internal sealed class NativeCoolingInputs
        {
            internal SyncedItem Item = null!;
            internal VehicleCoolingData Rule = null!;
            internal PlayMakerFSM Producer = null!, Cooling = null!;
            internal FsmState ProducerState = null!, AirState = null!, CheckState = null!;
            internal FsmState PumpState = null!, FanState = null!, LeakState = null!;
            internal FsmStateAction[] ProducerActions = null!, AirActions = null!, CheckActions = null!;
            internal FsmStateAction[] PumpActions = null!, FanActions = null!, LeakActions = null!;
            internal FsmFloat Speed = null!, Rpm = null!;
            internal NativeCoolingRead[] Reads = null!;
        }

        internal sealed class NativeCoolingRead
        {
            internal NativeCoolingInputs Owner = null!;
            internal FsmStateAction Action = null!;
            internal FieldInfo Field = null!;
            internal FsmFloat Original = null!;
            internal bool Rpm;
            internal readonly FsmFloat Input = new FsmFloat();
            internal int Depth;
        }

        private static void EnsureCoolingInputs(SyncedItem item)
        {
            var rule = SyncCatalog.VehicleCooling;
            try
            {
                if (item.NativeCooling != null) { ValidateCoolingInputs(item.NativeCooling); return; }
                if (rule == null || item.Body == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextCoolingInputProbeAt) return;
                item.NextCoolingInputProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                var b = new NativeCoolingInputs { Item = item, Rule = rule,
                    Producer = FindTemperatureFsm(fsms, rule.ProducerPath, rule.ProducerFsm),
                    Cooling = FindTemperatureFsm(fsms, rule.CoolingPath, rule.CoolingFsm),
                    Speed = FsmVariables.GlobalVariables.FindFsmFloat(rule.SpeedGlobal),
                    Rpm = FsmVariables.GlobalVariables.FindFsmFloat(rule.RpmGlobal) };
                b.ProducerState = HeatState(b.Producer, rule.ProducerState); b.AirState = HeatState(b.Cooling, rule.AirState);
                b.CheckState = HeatState(b.Cooling, rule.CheckState);
                b.PumpState = HeatState(b.Cooling, rule.PumpState); b.FanState = HeatState(b.Cooling, rule.FanState);
                b.LeakState = HeatState(b.Cooling, rule.LeakState);
                b.ProducerActions = (FsmStateAction[])b.ProducerState.Actions.Clone();
                b.AirActions = (FsmStateAction[])b.AirState.Actions.Clone(); b.CheckActions = (FsmStateAction[])b.CheckState.Actions.Clone();
                b.PumpActions = (FsmStateAction[])b.PumpState.Actions.Clone(); b.FanActions = (FsmStateAction[])b.FanState.Actions.Clone();
                b.LeakActions = (FsmStateAction[])b.LeakState.Actions.Clone();
                if (b.ProducerActions.Length != 3 || b.AirActions.Length != 7 || b.CheckActions.Length != 3
                    || b.PumpActions.Length != 8 || b.FanActions.Length != 6 || b.LeakActions.Length != 1)
                    throw new InvalidOperationException("Native cooling input graph changed.");
                b.Reads = new[] { CoolingRead(b, b.AirActions[1]), CoolingRead(b, b.CheckActions[1]),
                    CoolingRead(b, b.PumpActions[2], true), CoolingRead(b, b.FanActions[5], true), CoolingRead(b, b.LeakActions[0], true) };
                ValidateCoolingInputs(b); EnsureCoolingHooks(b); item.NativeCooling = b;
                foreach (var read in b.Reads) CoolingReaders[read.Action] = read;
                SyncEventLog.Record("vehicle-cooling-inputs-bound", item.Path);
            }
            catch (Exception error) { ClearCoolingInputs(item); NoteCoolingInputFailure(item, error); }
        }

        private static NativeCoolingRead CoolingRead(NativeCoolingInputs b, FsmStateAction action, bool rpm = false)
            => new NativeCoolingRead { Owner = b, Action = action, Rpm = rpm, Original = rpm ? b.Rpm : b.Speed,
                Field = NativeRpmField(action.GetType(), "float1", typeof(FsmFloat)) };

        private static void ValidateCoolingInputs(NativeCoolingInputs b)
        {
            var item = b.Item; var rule = b.Rule;
            if (!ReferenceEquals(rule, SyncCatalog.VehicleCooling) || item.Body == null || item.Path != rule.RootPath
                || ScenePath.Of(item.Body.transform) != rule.RootPath || b.Speed == null
                || !ReferenceEquals(b.Speed, FsmVariables.GlobalVariables.FindFsmFloat(rule.SpeedGlobal)) || b.Rpm == null
                || !ReferenceEquals(b.Rpm, FsmVariables.GlobalVariables.FindFsmFloat(rule.RpmGlobal)) || ReferenceEquals(b.Speed, b.Rpm))
                throw new InvalidOperationException("Native speed identity changed.");
            var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
            if (b.Producer == null || b.Cooling == null || !b.Producer.Fsm.Initialized || !b.Cooling.Fsm.Initialized
                || FindTemperatureFsm(fsms, rule.ProducerPath, rule.ProducerFsm) != b.Producer
                || FindTemperatureFsm(fsms, rule.CoolingPath, rule.CoolingFsm) != b.Cooling
                || b.Producer.FsmVariables.FindFsmFloat(rule.SpeedGlobal) != null || b.Cooling.FsmVariables.FindFsmFloat(rule.SpeedGlobal) != null
                || b.Cooling.FsmVariables.FindFsmFloat(rule.RpmGlobal) != null)
                throw new InvalidOperationException("Native speed source or consumer changed.");
            CoolingActions(b.Producer, b.ProducerState, rule.ProducerState, b.ProducerActions);
            CoolingActions(b.Cooling, b.AirState, rule.AirState, b.AirActions);
            CoolingActions(b.Cooling, b.CheckState, rule.CheckState, b.CheckActions);
            CoolingActions(b.Cooling, b.PumpState, rule.PumpState, b.PumpActions);
            CoolingActions(b.Cooling, b.FanState, rule.FanState, b.FanActions);
            CoolingActions(b.Cooling, b.LeakState, rule.LeakState, b.LeakActions);
            var get = b.ProducerActions[0]; var multiply = b.ProducerActions[1];
            CoolingAction(get, "GetSpeed", true); CoolingAction(multiply, "FloatMultiply", true);
            var target = TemperatureField(get, "gameObject") as FsmOwnerDefault;
            if (target == null || target.OwnerOption != OwnerDefaultOption.SpecifyGameObject || target.GameObject == null
                || target.GameObject.UseVariable || target.GameObject.Value != item.Body.gameObject
                || !ReferenceEquals(TemperatureField(get, "storeResult"), b.Speed)
                || !ReferenceEquals(TemperatureField(multiply, "floatVariable"), b.Speed)
                || TemperatureConstant(TemperatureField(multiply, "multiplyBy")) != 3.6f)
                throw new InvalidOperationException("Native movement speed producer changed.");
            // The following wheel-speed read must keep its separate local output;
            // otherwise it could overwrite movement after the km/h conversion.
            var differential = b.ProducerActions[2]; CoolingAction(differential, "GetProperty", true);
            var property = TemperatureField(differential, "targetProperty") as FsmProperty;
            var drive = property?.TargetObject?.Value as Component;
            if (property == null || property.setProperty || property.TargetTypeName != "Drivetrain"
                || property.PropertyName != "differentialSpeed" || drive == null || drive.gameObject != item.Body.gameObject
                || drive.GetType().FullName != "Drivetrain" || drive.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || !ReferenceEquals(property.FloatParameter, b.Producer.FsmVariables.FindFsmFloat("DiffSpeed"))
                || ReferenceEquals(property.FloatParameter, b.Speed) || property.FloatParameter == null)
                throw new InvalidOperationException("Native differential speed no longer has a separate output.");
            foreach (var read in b.Reads)
                if (!ReferenceEquals(read.Field.GetValue(read.Action), read.Depth > 0 ? read.Input : read.Original))
                    throw new InvalidOperationException("Cooling speed operand changed.");
            var air = b.AirActions[1]; CoolingAction(air, "FloatOperator", false);
            if (TemperatureField(air, "operation").ToString() != "Multiply"
                || !ReferenceEquals(TemperatureField(air, "float2"), b.Cooling.FsmVariables.FindFsmFloat("TempAreaAdjusted"))
                || !ReferenceEquals(TemperatureField(air, "storeResult"), b.Cooling.FsmVariables.FindFsmFloat("CoolingBaseRate")))
                throw new InvalidOperationException("Cooling airspeed calculation changed.");
            var compare = b.CheckActions[1]; CoolingAction(compare, "FloatCompare", false);
            string Event(string name) => compare.GetType().GetField(name).GetValue(compare) is FsmEvent e ? e.Name ?? "" : "";
            if (TemperatureConstant(TemperatureField(compare, "float2")) != 2f
                || TemperatureConstant(TemperatureField(compare, "tolerance")) != 0f
                || Event("equal") != "" || Event("lessThan") != "" || Event("greaterThan") != "FINISHED")
                throw new InvalidOperationException("Cooling stationary threshold changed.");
            if (b.ProducerState.Transitions.Length != 0 || b.AirState.Transitions.Length != 1
                || b.AirState.Transitions[0].EventName != "FINISHED" || b.AirState.Transitions[0].ToState != "Water pres"
                || b.CheckState.Transitions.Length != 2 || b.CheckState.Transitions[0].EventName != "GRILL"
                || b.CheckState.Transitions[0].ToState != "Grill trigger" || b.CheckState.Transitions[1].EventName != "FINISHED"
                || b.CheckState.Transitions[1].ToState != "Coolant temp 2")
                throw new InvalidOperationException("Native cooling speed transitions changed.");
            ValidateCoolingRpm(b);
        }

        private static void ValidateCoolingRpm(NativeCoolingInputs b)
        {
            CoolingCompare(b.PumpActions[2], 100, "", "PROCEED", "");
            CoolingCompare(b.LeakActions[0], 200, "LEAK", "FINISHED", "LEAK");
            var fan = b.FanActions[5]; CoolingAction(fan, "FloatOperator", false);
            if (TemperatureField(fan, "operation").ToString() != "Divide"
                || !ReferenceEquals(TemperatureField(fan, "float2"), b.Cooling.FsmVariables.FindFsmFloat("CoolingFanModifier"))
                || !ReferenceEquals(TemperatureField(fan, "storeResult"), b.Cooling.FsmVariables.FindFsmFloat("CoolingFanRate")))
                throw new InvalidOperationException("Native fan RPM calculation changed.");
            CoolingTransitions(b.PumpState, "PROCEED", "Closed", "FINISHED", "Open");
            CoolingTransitions(b.LeakState, "LEAK", "Housing tightness", "FINISHED", "Water leak");
            CoolingTransitions(b.FanState, "FINISHED", "Flect");
        }

        private static void CoolingCompare(FsmStateAction action, float threshold, string equal, string less, string greater)
        {
            CoolingAction(action, "FloatCompare", false);
            string Event(string name) => action.GetType().GetField(name).GetValue(action) is FsmEvent e ? e.Name ?? "" : "";
            if (TemperatureConstant(TemperatureField(action, "float2")) != threshold
                || TemperatureConstant(TemperatureField(action, "tolerance")) != 0
                || Event("equal") != equal || Event("lessThan") != less || Event("greaterThan") != greater)
                throw new InvalidOperationException("Native cooling RPM threshold changed.");
        }

        private static void CoolingTransitions(FsmState state, params string[] pairs)
        {
            if (state.Transitions.Length * 2 != pairs.Length) throw new InvalidOperationException("Cooling RPM transitions changed.");
            for (int i = 0; i < state.Transitions.Length; i++)
                if (state.Transitions[i].EventName != pairs[i * 2] || state.Transitions[i].ToState != pairs[i * 2 + 1])
                    throw new InvalidOperationException("Cooling RPM transition destination changed.");
        }

        private static void CoolingAction(FsmStateAction action, string type, bool everyFrame)
        {
            if (!action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + type
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || (bool)TemperatureField(action, "everyFrame") != everyFrame)
                throw new InvalidOperationException("Native speed action signature changed.");
        }

        private static void CoolingActions(PlayMakerFSM fsm, FsmState state, string name, FsmStateAction[] actions)
        {
            if (HeatState(fsm, name) != state || state.Actions.Length != actions.Length)
                throw new InvalidOperationException("Native speed state changed.");
            for (int i = 0; i < actions.Length; i++)
                if (!ReferenceEquals(actions[i], state.Actions[i])) throw new InvalidOperationException("Native speed actions were reinitialized.");
        }

        private static void CaptureSpeedTelemetry(SyncedItem item, VehicleState state)
        {
            state.MovementSpeedAvailable = false; state.MovementSpeedTenthsKmh = 0;
            EnsureCoolingInputs(item); var b = item.NativeCooling;
            if (b == null || !b.Producer.enabled || !b.Producer.gameObject.activeInHierarchy || !b.Producer.Fsm.Started
                || b.Producer.ActiveStateName != b.Rule.ProducerState || !FiniteTemperature(b.Speed.Value) || b.Speed.Value < 0) return;
            state.MovementSpeedAvailable = true;
            state.MovementSpeedTenthsKmh = (ushort)Mathf.Clamp(b.Speed.Value * 10f, 0f, ushort.MaxValue);
        }

        private static void ClearCoolingInputs(SyncedItem item)
        {
            if (item.NativeCooling != null)
                foreach (var read in item.NativeCooling.Reads)
                    if (CoolingReaders.TryGetValue(read.Action, out var current) && ReferenceEquals(current, read)) CoolingReaders.Remove(read.Action);
            item.NativeCooling = null;
        }

        private static void NoteCoolingInputFailure(SyncedItem item, Exception error)
        {
            item.NextCoolingInputProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            if (Time.unscaledTime < item.NextCoolingInputErrorAt) return;
            item.NextCoolingInputErrorAt = Time.unscaledTime + 10;
            try { SyncEventLog.Record("vehicle-cooling-inputs-unavailable", item.Path + ": " + error.Message); } catch { }
        }
    }
}
