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
        private bool ApplyNativeWheelRim(SyncedItem item, int wheel, PlayMakerFSM fsm)
        {
            if (Time.unscaledTime < item.NextWheelRimRetryAt[wheel]) return false;
            try
            {
                GuestEngineWriterData? writer = null;
                var profile = SyncCatalog.GuestEngineProtection;
                if (profile != null)
                    foreach (var candidate in profile.Writers)
                        if (candidate.WheelHealthIndex == wheel && candidate.Fsm == fsm.FsmName
                            && candidate.Path == ScenePath.Of(fsm.transform))
                        {
                            if (writer != null) throw new InvalidOperationException("Ambiguous wheel rim profile.");
                            writer = candidate;
                        }
                var rule = writer?.WheelRim;
                if (rule == null || item.Body == null || !ConditionFsmLive(item, fsm)
                    || !fsm.Fsm.Initialized || !fsm.Fsm.Started || !writer!.Path.StartsWith(item.Path + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Wheel rim profile or native FSM is not ready.");
                int count = 0;
                foreach (var candidate in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (candidate.FsmName == writer.Fsm && ScenePath.Of(candidate.transform) == writer.Path) count++;
                if (count != 1) throw new InvalidOperationException("Ambiguous wheel condition FSM.");

                var state = HeatState(fsm, rule.State);
                var quietState = HeatState(fsm, rule.QuietState);
                if (state.Actions.Length != 2 || state.Transitions.Length != 0)
                    throw new InvalidOperationException("Native rim state gained actions or transitions.");
                string eventName = FsmHook.RemoteEntryEventName(rule.State); int entries = 0;
                foreach (var transition in fsm.Fsm.GlobalTransitions)
                    if (transition.EventName == eventName && (++entries > 1 || transition.ToState != rule.State))
                        throw new InvalidOperationException("Native rim entry was redirected.");
                foreach (var localState in fsm.Fsm.States)
                    foreach (var transition in localState.Transitions)
                        if (transition.EventName == eventName) throw new InvalidOperationException("Local event shadows native rim entry.");
                var target = fsm.FsmVariables.FindFsmObject(rule.WheelObject);
                var component = target?.Value as Component;
                if (target == null || target.IsNone || !target.UseVariable || component == null
                    || component.GetType().FullName != "Wheel" || component.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                    || component.transform != fsm.transform || component.GetComponents(component.GetType()).Length != 1)
                    throw new InvalidOperationException("Native rim target is not this wheel.");
                var radius = fsm.FsmVariables.FindFsmFloat(rule.RadiusVariable);
                if (radius == null || radius.IsNone || !radius.UseVariable || !ConditionFloat(radius) || radius.Value <= 0 || radius.Value > 1)
                    throw new InvalidOperationException("Native rim radius is unavailable.");
                foreach (var other in fsm.GetComponents<PlayMakerFSM>())
                    if (other != fsm)
                        foreach (var scalar in other.FsmVariables.FloatVariables)
                            if (ReferenceEquals(scalar, radius)) throw new InvalidOperationException("Rim radius aliases another FSM.");
                var radiusField = RimProperty(state.Actions[rule.RadiusIndex], target, component, "radius", false, radius, 0);
                var frictionField = RimProperty(state.Actions[rule.FrictionIndex], target, component, "rollingFrictionCoefficient", true, null, rule.Friction);
                var quiet = quietState.Actions[rule.QuietIndex];
                if (!ReferenceEquals(state.Actions[rule.RadiusIndex].Fsm, fsm.Fsm)
                    || !ReferenceEquals(state.Actions[rule.FrictionIndex].Fsm, fsm.Fsm) || !ReferenceEquals(quiet.Fsm, fsm.Fsm))
                    throw new InvalidOperationException("Native rim action belongs to another FSM.");
                RimAction(quiet, "ActivateGameObject", false);
                var sound = fsm.FsmVariables.FindFsmGameObject(rule.SoundVariable);
                var owner = TemperatureField(quiet, "gameObject") as FsmOwnerDefault;
                if (sound == null || sound.IsNone || !sound.UseVariable || owner == null || owner.OwnerOption != OwnerDefaultOption.SpecifyGameObject
                    || !ReferenceEquals(owner.GameObject, sound) || !RimFalse(TemperatureField(quiet, "activate"))
                    || !RimFalse(TemperatureField(quiet, "recursive")) || (bool)TemperatureField(quiet, "resetOnExit"))
                    throw new InvalidOperationException("Native puncture sound action changed.");
                if (sound.Value != null && (sound.Value == item.Body.gameObject || sound.Value == fsm.gameObject
                    || !sound.Value.transform.IsChildOf(item.Body.transform)))
                    throw new InvalidOperationException("Native puncture sound escaped this vehicle.");
                foreach (var global in FsmVariables.GlobalVariables.GameObjectVariables)
                    if (ReferenceEquals(global, sound)) throw new InvalidOperationException("Puncture sound aliases a global.");

                // RIM is local to Check rim. Going through PUNCTURE would select
                // the observer's saved tyre type and can zero the host's saved
                // health. Enter only the audited two-action physics state.
                if (ReadWheelState(fsm) != rule.State || (float)radiusField.GetValue(component) != radius.Value
                    || (float)frictionField.GetValue(component) != rule.Friction)
                {
                    if (!FsmHook.EnsureRemoteEntry(fsm, rule.State)) throw new InvalidOperationException("Native rim state entry unavailable.");
                    FsmHook.FireRemoteEntry(fsm, rule.State);
                }
                if (sound.Value != null && sound.Value.activeSelf) quiet.OnEnter();
                if (ReadWheelState(fsm) != rule.State || (float)radiusField.GetValue(component) != radius.Value
                    || (float)frictionField.GetValue(component) != rule.Friction || (sound.Value != null && sound.Value.activeSelf))
                    throw new InvalidOperationException("Native rim actions did not apply.");
                item.NextWheelRimRetryAt[wheel] = 0;
                return true;
            }
            catch (Exception error)
            {
                item.NextWheelRimRetryAt[wheel] = Time.unscaledTime + ConditionProbeIntervalSeconds;
                SyncEventLog.Record("vehicle-wheel-rim-unavailable", item.Path + " " + WheelSuffixes[wheel] + ": " + error.Message);
                return false;
            }
        }

        private static bool RimFalse(object value) => value is FsmBool flag && !flag.IsNone && !flag.UseVariable && !flag.Value;

        private static void RimAction(FsmStateAction action, string name, bool everyFrame)
        {
            if (action == null || !action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + name
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp" || (bool)TemperatureField(action, "everyFrame") != everyFrame)
                throw new InvalidOperationException("Native rim action changed: " + name);
        }

        private static FieldInfo RimProperty(FsmStateAction action, FsmObject target, Component component, string member,
            bool everyFrame, FsmFloat? input, float literal)
        {
            RimAction(action, "SetProperty", everyFrame);
            var property = TemperatureField(action, "targetProperty") as FsmProperty;
            var field = component.GetType().GetField(member, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.setProperty || property.PropertyName != member || property.TargetTypeName != "Wheel"
                || !ReferenceEquals(property.TargetObject, target) || field == null || field.FieldType != typeof(float) || field.IsInitOnly)
                throw new InvalidOperationException("Native rim property changed: " + member);
            var value = property.FloatParameter;
            if (value == null || value.IsNone || (input != null ? !ReferenceEquals(value, input) : value.UseVariable || value.Value != literal))
                throw new InvalidOperationException("Native rim property input changed: " + member);
            return field;
        }
    }
}
