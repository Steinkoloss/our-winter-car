using System;
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
        internal sealed class NativeTirePressureBinding
        {
            internal VehicleTirePressureData Rule = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmState State = null!;
            internal FsmFloat Pressure = null!, Optimum = null!;
            internal readonly Component[] Wheels = new Component[4];
            internal readonly FieldInfo[] PressureFields = new FieldInfo[4], OptimumFields = new FieldInfo[4];
            internal bool Applied;
        }

        private void ApplyNativeTirePressure(SyncedItem item, bool received = false)
        {
            var session = _bridge.Session; var rule = SyncCatalog.VehicleTirePressure;
            if (session == null || (session.IsHost ? session.State != SessionState.Hosting : session.State != SessionState.Connected)
                || rule == null || item.Body == null || !item.IsVehicle || item.Path != rule.RootPath) return;
            bool local = item.LocallyOwned || _items.IsLocalPlayerDriving(item);
            if (!VehicleConditionStreamPolicy.TryGetObserverPressure(item.ApplyingVehicleCondition ?? item.AcceptedVehicleCondition,
                    item.Id, local, item.RemoteOwner, out byte pressure)
                && (item.ApplyingVehicleCondition != null || !ReferenceEquals(item.ParkedConditionBody, item.Body)
                    || !VehicleConditionStreamPolicy.TryGetParkedObserverPressure(item.ParkedVehicleCondition,
                        item.Id, local, item.RemoteOwner, out pressure))) return;
            if (!received && Time.unscaledTime < item.NextTirePressureProbeAt) return;
            try
            {
                var binding = item.NativeTirePressure;
                if (binding == null || !ReferenceEquals(binding.Rule, rule))
                {
                    var fsm = FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), rule.Path, rule.Fsm);
                    binding = new NativeTirePressureBinding { Rule = rule, Fsm = fsm, State = HeatState(fsm, rule.State),
                        Pressure = fsm.FsmVariables.FindFsmFloat(rule.Pressure), Optimum = fsm.FsmVariables.FindFsmFloat(rule.Optimum) };
                }
                ValidateTirePressureBinding(item, binding);
                item.NativeTirePressure = binding;
                item.NextTirePressureProbeAt = Time.unscaledTime + 1f;
                float value = VehicleConditionPolicy.DecodeNativeTirePressure(pressure);
                bool changed = !binding.Applied || binding.Pressure.Value != value;
                for (int i = 0; i < 4; i++)
                    changed |= (float)binding.PressureFields[i].GetValue(binding.Wheels[i]) != value
                        || (float)binding.OptimumFields[i].GetValue(binding.Wheels[i]) != binding.Optimum.Value;
                if (!changed) return;
                binding.Pressure.Value = value;
                // Vanilla only enables the FL pair. The audited one-shot writes
                // cover all four wheels; restore native enablement even on failure.
                var actions = binding.State.Actions;
                var enabled = new bool[actions.Length];
                for (int i = 0; i < actions.Length; i++) { enabled[i] = actions[i].Enabled; actions[i].Enabled = true; }
                try { binding.Fsm.SendEvent(rule.Event); }
                finally
                {
                    for (int i = 0; i < actions.Length; i++)
                    {
                        actions[i].Enabled = enabled[i];
                        if (!enabled[i]) binding.State.ActiveActions.Remove(actions[i]);
                    }
                }
                for (int i = 0; i < 4; i++)
                    if ((float)binding.PressureFields[i].GetValue(binding.Wheels[i]) != value
                        || (float)binding.OptimumFields[i].GetValue(binding.Wheels[i]) != binding.Optimum.Value)
                        throw new InvalidOperationException("Native tyre pressure event did not update every wheel.");
                binding.Applied = true;
            }
            catch (Exception error)
            {
                item.NativeTirePressure = null;
                item.NextTirePressureProbeAt = Time.unscaledTime + ConditionProbeIntervalSeconds;
                SyncEventLog.Record("vehicle-tire-pressure-unavailable", item.Path + ": " + error.Message);
            }
        }

        private static void ValidateTirePressureBinding(SyncedItem item, NativeTirePressureBinding b)
        {
            var rule = b.Rule;
            if (item.Body == null || b.Fsm == null || !b.Fsm.enabled || !b.Fsm.gameObject.activeInHierarchy
                || !b.Fsm.transform.IsChildOf(item.Body.transform) || ScenePath.Of(b.Fsm.transform) != rule.Path
                || b.Fsm.FsmName != rule.Fsm || HeatState(b.Fsm, rule.State) != b.State
                || b.State.Actions.Length != 8 || b.State.Transitions.Length != 0
                || b.Pressure == null || b.Optimum == null || ReferenceEquals(b.Pressure, b.Optimum)
                || !ReferenceEquals(b.Pressure, b.Fsm.FsmVariables.FindFsmFloat(rule.Pressure))
                || !ReferenceEquals(b.Optimum, b.Fsm.FsmVariables.FindFsmFloat(rule.Optimum))
                || float.IsNaN(b.Optimum.Value) || float.IsInfinity(b.Optimum.Value) || b.Optimum.Value <= 0)
                throw new InvalidOperationException("Native tyre pressure source changed or is not ready.");
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(global, b.Pressure) || ReferenceEquals(global, b.Optimum))
                    throw new InvalidOperationException("Native tyre pressure source aliases a global.");
            int sources = 0;
            foreach (var fsm in b.Fsm.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == rule.Fsm) sources++;
            if (sources != 1) throw new InvalidOperationException("Native tyre pressure FSM is ambiguous.");
            int transitions = 0;
            foreach (var transition in b.Fsm.Fsm.GlobalTransitions)
                if (transition.EventName == rule.Event)
                {
                    transitions++;
                    if (transition.ToState != rule.State) throw new InvalidOperationException("Native tyre pressure event changed.");
                }
            if (transitions != 1) throw new InvalidOperationException("Native tyre pressure event is missing or ambiguous.");
            foreach (var state in b.Fsm.Fsm.States)
                foreach (var transition in state.Transitions)
                    if (transition.EventName == rule.Event) throw new InvalidOperationException("Local transition shadows tyre pressure event.");
            for (int i = 0; i < 4; i++)
            {
                var wheel = rule.Wheels[i]; var target = b.Fsm.FsmVariables.FindFsmObject(wheel.ObjectVariable);
                var component = target?.Value as Component;
                if (component == null || component.GetType().FullName != "Wheel"
                    || component.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                    || !component.transform.IsChildOf(item.Body.transform) || ScenePath.Of(component.transform) != wheel.Path
                    || component.GetComponents(component.GetType()).Length != 1)
                    throw new InvalidOperationException("Native tyre pressure wheel target changed or is not ready.");
                b.Wheels[i] = component;
                b.PressureFields[i] = TirePressureProperty(b, wheel.PressureIndex, wheel.Enabled, target!, component, "pressure", b.Pressure);
                b.OptimumFields[i] = TirePressureProperty(b, wheel.OptimumIndex, wheel.Enabled, target!, component, "optimalPressure", b.Optimum);
            }
        }

        private static FieldInfo TirePressureProperty(NativeTirePressureBinding b, int index, bool enabled, FsmObject target,
            Component component, string member, FsmFloat input)
        {
            var action = b.State.Actions[index];
            if (action == null) throw new InvalidOperationException("Native tyre pressure action missing at " + index);
            if (action.Enabled != enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions.SetProperty"
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp" || (bool)TemperatureField(action, "everyFrame"))
                throw new InvalidOperationException("Native tyre pressure action changed at " + index + ": "
                    + action.GetType().AssemblyQualifiedName + ", enabled=" + action.Enabled);
            var property = TemperatureField(action, "targetProperty") as FsmProperty;
            var field = component.GetType().GetField(member, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.setProperty || property.PropertyName != member || property.TargetTypeName != "Wheel"
                || !ReferenceEquals(property.TargetObject, target) || !ReferenceEquals(property.FloatParameter, input)
                || field == null || field.FieldType != typeof(float) || field.IsInitOnly)
                throw new InvalidOperationException("Native tyre pressure property signature changed.");
            return field;
        }
    }
}
