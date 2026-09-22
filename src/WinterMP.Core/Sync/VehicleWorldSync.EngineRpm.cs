using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal sealed class NativeEngineRpmBinding
        {
            internal VehicleEngineRpmSourceData Rule = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmState[] States = null!;
            internal FsmObject Target = null!;
            internal Component Drivetrain = null!;
            internal FsmFloat Output = null!;
            internal readonly List<RpmProducerBinding> Producers = new List<RpmProducerBinding>();
        }

        internal sealed class RpmProducerBinding
        {
            internal VehicleEngineRpmProducerData Rule = null!;
            internal FsmState State = null!;
            internal FsmStateAction Action = null!;
            internal FieldInfo OutputField = null!;
            internal FieldInfo FrameField = null!;
            internal FieldInfo? InputField;
        }

        private static VehicleEngineRpmSourceData? FindNativeRpmRule(SyncedItem item)
        {
            var profile = SyncCatalog.VehicleEngineRpm;
            if (profile == null || item.Body == null) return null;
            foreach (var source in profile.Sources)
                if (source.RootPath == item.Path && ScenePath.Of(item.Body.transform) == source.RootPath)
                    return source;
            return null;
        }

        private static VehicleEngineRpmSourceData? RefreshNativeRpmBinding(SyncedItem item)
        {
            var rule = FindNativeRpmRule(item);
            if (rule != null) item.RequiresNativeEngineRpm = true;
            var binding = item.NativeEngineRpm;
            if (binding != null)
            {
                try
                {
                    if (!ReferenceEquals(binding.Rule, rule)) throw new InvalidOperationException("Native RPM catalog or vehicle identity changed.");
                    ValidateNativeRpmBinding(item, binding);
                    item.EngineRevsVar = binding.Output;
                    return rule;
                }
                catch (Exception error) { NoteNativeRpmFailure(item, error); }
                item.NativeEngineRpm = null;
                item.NextSystemsProbeAt = 0f;
            }
            if (item.RequiresNativeEngineRpm)
            {
                item.EngineRevsVar = null;
                item.SystemsReady = false;
            }
            return rule;
        }

        private static void BindNativeEngineRpm(SyncedItem item, VehicleEngineRpmSourceData rule, PlayMakerFSM[] fsms)
        {
            try
            {
                PlayMakerFSM? source = null;
                foreach (var candidate in fsms)
                {
                    if (candidate == null || candidate.FsmName != rule.Fsm
                        || ScenePath.Of(candidate.transform) != rule.ProducerPath) continue;
                    if (source != null) throw new InvalidOperationException("Ambiguous native RPM producer.");
                    source = candidate;
                }
                if (source == null) throw new InvalidOperationException("Native RPM producer has not loaded.");
                var target = source.FsmVariables.FindFsmObject(rule.ObjectVariable);
                var drivetrain = target?.Value as Component;
                var output = FsmVariables.GlobalVariables.FindFsmFloat(rule.GlobalVariable);
                if (target == null || drivetrain == null || output == null)
                    throw new InvalidOperationException("Native RPM references have not loaded.");
                var binding = new NativeEngineRpmBinding {
                    Rule = rule, Fsm = source, Target = target, Drivetrain = drivetrain, Output = output,
                    States = (FsmState[])source.Fsm.States.Clone() };
                foreach (var producer in rule.Producers)
                {
                    FsmState? state = null;
                    foreach (var candidate in binding.States)
                        if (candidate.Name == producer.State)
                        {
                            if (state != null) throw new InvalidOperationException("Ambiguous native RPM state.");
                            state = candidate;
                        }
                    if (state == null)
                        throw new InvalidOperationException("Native RPM producer action changed.");
                    var action = NativeRpmAction(state, producer.Index);
                    var type = action.GetType();
                    if (type.FullName != "HutongGames.PlayMaker.Actions." + producer.ActionType
                        || type.Assembly.GetName().Name != "Assembly-CSharp")
                        throw new InvalidOperationException("Native RPM action type changed.");
                    bool property = producer.ActionType == "GetProperty";
                    binding.Producers.Add(new RpmProducerBinding {
                        Rule = producer, State = state, Action = action,
                        OutputField = NativeRpmField(type, property ? "targetProperty" : "floatVariable", property ? typeof(FsmProperty) : typeof(FsmFloat)),
                        FrameField = NativeRpmField(type, "everyFrame", typeof(bool)),
                        InputField = property ? null : NativeRpmField(type, "floatValue", typeof(FsmFloat)) });
                }
                ValidateNativeRpmBinding(item, binding);
                item.NativeEngineRpm = binding;
                item.NativeEngineRpmOutput = output;
                item.EngineRevsVar = output;
                SyncEventLog.Record("vehicle-rpm-bound", item.Path + " " + rule.Fsm + "." + rule.GlobalVariable);
            }
            catch (Exception error) { NoteNativeRpmFailure(item, error); }
        }

        private static FieldInfo NativeRpmField(Type type, string name, Type expected)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.FieldType != expected) throw new InvalidOperationException("Native RPM action field changed: " + name);
            return field;
        }

        private static FsmStateAction NativeRpmAction(FsmState state, int index)
        {
            return FsmHook.NativeAction(state, index)
                ?? throw new InvalidOperationException("Native RPM producer action is unavailable or changed.");
        }

        private static void ValidateNativeRpmBinding(SyncedItem item, NativeEngineRpmBinding binding)
        {
            var rule = binding.Rule;
            var source = binding.Fsm;
            var drivetrain = binding.Drivetrain;
            if (source == null || drivetrain == null || item.Body == null
                || drivetrain.gameObject != item.Body.gameObject || source.FsmName != rule.Fsm
                || ScenePath.Of(source.transform) != rule.ProducerPath
                || drivetrain.GetType().FullName != rule.ComponentType
                || drivetrain.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || binding.Target.TypeName != rule.ComponentType || binding.Target.ObjectType != drivetrain.GetType()
                || !ReferenceEquals(binding.Target, source.FsmVariables.FindFsmObject(rule.ObjectVariable))
                || binding.Target.Value != drivetrain
                || !ReferenceEquals(binding.Output, FsmVariables.GlobalVariables.FindFsmFloat(rule.GlobalVariable))
                || source.FsmVariables.FindFsmFloat(rule.GlobalVariable) != null)
                throw new InvalidOperationException("Native RPM references no longer identify this vehicle.");
            var member = drivetrain.GetType().GetField(rule.RpmMember, BindingFlags.Instance | BindingFlags.Public);
            if (member == null || member.FieldType != typeof(float))
                throw new InvalidOperationException("Native drivetrain RPM member changed.");
            var states = source.Fsm.States;
            if (states.Length != binding.States.Length) throw new InvalidOperationException("Native RPM states changed.");
            for (int i = 0; i < states.Length; i++)
                if (!ReferenceEquals(states[i], binding.States[i])) throw new InvalidOperationException("Native RPM state was reinitialized.");
            foreach (var producer in binding.Producers)
            {
                var action = producer.Action;
                var entry = producer.Rule;
                if (producer.State.Name != entry.State
                    || !ReferenceEquals(NativeRpmAction(producer.State, entry.Index), action) || !action.Enabled
                    || (bool)producer.FrameField.GetValue(action) != entry.EveryFrame)
                    throw new InvalidOperationException("Native RPM producer signature changed.");
                if (producer.InputField == null)
                {
                    var property = producer.OutputField.GetValue(action) as FsmProperty;
                    if (property == null || property.setProperty || property.PropertyName != rule.RpmMember
                        || property.TargetTypeName != rule.ComponentType
                        || !ReferenceEquals(property.TargetObject, binding.Target)
                        || !ReferenceEquals(property.FloatParameter, binding.Output))
                        throw new InvalidOperationException("Native RPM property output changed.");
                }
                else
                {
                    var input = producer.InputField.GetValue(action) as FsmFloat;
                    if (!ReferenceEquals(producer.OutputField.GetValue(action), binding.Output) || input == null)
                        throw new InvalidOperationException("Native RPM scalar output changed.");
                    if (entry.SourceVariable != null)
                    {
                        if (!ReferenceEquals(input, source.FsmVariables.FindFsmFloat(entry.SourceVariable)))
                            throw new InvalidOperationException("Native cranking RPM input changed.");
                    }
                    else if (input.UseVariable || !entry.SourceConstant.HasValue || input.Value != entry.SourceConstant.Value)
                        throw new InvalidOperationException("Native stopped RPM input changed.");
                }
            }
        }

        private static bool FiniteRpm(FsmFloat? value) => value != null
            && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value) && value.Value >= 0f;

        private static bool HasEngineRpm(SyncedItem item) => FiniteRpm(item.EngineRevsVar)
            && (!item.RequiresNativeEngineRpm || item.NativeEngineRpm != null);

        private static bool IsNativeRpmOutput(SyncedItem item, FsmFloat? value)
        {
            if (value == null) return false;
            if (ReferenceEquals(value, item.EngineRevsVar) || ReferenceEquals(value, item.NativeEngineRpmOutput)) return true;
            var rule = FindNativeRpmRule(item);
            return rule != null && ReferenceEquals(value, FsmVariables.GlobalVariables.FindFsmFloat(rule.GlobalVariable));
        }

        private static void NoteNativeRpmFailure(SyncedItem item, Exception error)
        {
            if (Time.unscaledTime < item.NextEngineRpmErrorAt) return;
            item.NextEngineRpmErrorAt = Time.unscaledTime + 10f;
            try
            {
                string detail = item.Path + ": " + error.Message;
                WinterMPPlugin.Log.LogWarning("VehicleWorldSync: engine updates waiting for native RPM; " + detail);
                SyncEventLog.Record("vehicle-rpm-unavailable", detail);
            }
            catch { /* A logging failure must not accept an unverified RPM source. */ }
        }
    }
}
