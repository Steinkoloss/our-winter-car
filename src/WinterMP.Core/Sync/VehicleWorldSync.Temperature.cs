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
        internal sealed class NativeTemperatureBinding
        {
            internal VehicleTemperatureSourceData Rule = null!;
            internal PlayMakerFSM Gauge = null!, Source = null!;
            internal FsmState State = null!;
            internal FsmStateAction Read = null!, Scale = null!, Clamp = null!;
            internal FsmFloat Output = null!, Celsius = null!;
            internal FsmFloat? EngineCelsius;
            internal float Divisor, Minimum, Maximum;
            internal FsmState? ReadyState;
            internal bool SourceReady;
        }

        private static void EnsureTemperatureProbe(SyncedItem item)
        {
            if (item.Body == null) return;
            VehicleTemperatureSourceData? rule = null;
            var profile = SyncCatalog.VehicleTemperature;
            if (profile != null)
                foreach (var entry in profile.Sources)
                    if (entry.RootPath == item.Path) { rule = entry; break; }
            if (rule != null || SyncCatalog.VehicleTemperatureError != null) item.RequiresNativeTemperature = true;
            try
            {
                if (item.NativeTemperature != null)
                {
                    if (!ReferenceEquals(item.NativeTemperature.Rule, rule))
                        throw new InvalidOperationException("Temperature catalog changed.");
                    ValidateTemperatureBinding(item, item.NativeTemperature);
                    return;
                }
                if (rule == null || Time.unscaledTime < item.NextTemperatureProbeAt) return;
                item.NextTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                var gauge = FindTemperatureFsm(fsms, rule.GaugePath, "Temp");
                var source = FindTemperatureFsm(fsms, rule.SourcePath, rule.SourceFsm);
                FsmState? state = null;
                foreach (var candidate in gauge.Fsm.States)
                    if (candidate.Name == "Speed")
                    {
                        if (state != null) throw new InvalidOperationException("Ambiguous temperature gauge state.");
                        state = candidate;
                    }
                if (state == null || !state.IsInitialized || state.Actions.Length < 3)
                    throw new InvalidOperationException("Temperature gauge actions missing.");
                var binding = new NativeTemperatureBinding {
                    Rule = rule, Gauge = gauge, Source = source, State = state,
                    Read = state.Actions[0], Scale = state.Actions[1], Clamp = state.Actions[2],
                    Output = gauge.FsmVariables.FindFsmFloat(rule.GaugeVariable),
                    Celsius = source.FsmVariables.FindFsmFloat(rule.SourceVariable) };
                if (rule.HostAuthoritative)
                {
                    binding.EngineCelsius = FsmVariables.GlobalVariables.FindFsmFloat(rule.EngineGlobal);
                    foreach (var candidate in source.Fsm.States)
                        if (candidate.Name == rule.ReadyState)
                        {
                            if (binding.ReadyState != null) throw new InvalidOperationException("Ambiguous coolant readiness state.");
                            binding.ReadyState = candidate;
                        }
                }
                ValidateTemperatureBinding(item, binding);
                item.NativeTemperature = binding;
                SyncEventLog.Record("vehicle-temperature-bound", item.Path + " " + rule.SourceFsm + "." + rule.SourceVariable);
            }
            catch (Exception error)
            {
                ClearTemperatureReader(item);
                item.NativeTemperature = null;
                item.NextTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                if (Time.unscaledTime < item.NextTemperatureErrorAt) return;
                item.NextTemperatureErrorAt = Time.unscaledTime + 10f;
                SyncEventLog.Record("vehicle-temperature-unavailable", item.Path + ": " + error.Message);
            }
        }

        private static PlayMakerFSM FindTemperatureFsm(PlayMakerFSM[] fsms, string path, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var candidate in fsms)
                if (candidate != null && candidate.FsmName == name
                    && ScenePath.MayMatchLeafName(path, candidate.gameObject.name) && ScenePath.Of(candidate.transform) == path)
                {
                    if (found != null) throw new InvalidOperationException("Ambiguous temperature FSM.");
                    found = candidate;
                }
            return found ?? throw new InvalidOperationException("Temperature FSM not loaded.");
        }

        private static object TemperatureField(FsmStateAction action, string name)
        {
            var field = action.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            return field?.GetValue(action) ?? throw new InvalidOperationException("Temperature action field missing: " + name);
        }

        private static void TemperatureAction(FsmStateAction action, string name)
        {
            if (action.GetType().FullName != "HutongGames.PlayMaker.Actions." + name
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || !(bool)TemperatureField(action, "everyFrame"))
                throw new InvalidOperationException("Temperature gauge action signature changed.");
        }

        private static bool TemperatureLiteral(object value, string expected) => value is FsmString text
            && !text.UseVariable && text.Value == expected;

        private static float TemperatureConstant(object value)
        {
            if (value is not FsmFloat number || number.UseVariable || !FiniteTemperature(number.Value))
                throw new InvalidOperationException("Temperature gauge constant changed.");
            return number.Value;
        }

        private static void ValidateTemperatureBinding(SyncedItem item, NativeTemperatureBinding b, bool topology = true)
        {
            var rule = b.Rule;
            if (item.Body == null || ScenePath.Of(item.Body.transform) != rule.RootPath || item.Path != rule.RootPath
                || b.Gauge == null || b.Source == null || b.Output == null || b.Celsius == null
                || !b.Gauge.Fsm.Initialized || !b.Source.Fsm.Initialized
                || b.Gauge.FsmName != "Temp" || b.Source.FsmName != rule.SourceFsm
                || ScenePath.Of(b.Gauge.transform) != rule.GaugePath || ScenePath.Of(b.Source.transform) != rule.SourcePath
                || !b.Gauge.transform.IsChildOf(item.Body.transform) || !b.Source.transform.IsChildOf(item.Body.transform)
                || !ReferenceEquals(b.Output, b.Gauge.FsmVariables.FindFsmFloat(rule.GaugeVariable))
                || !ReferenceEquals(b.Celsius, b.Source.FsmVariables.FindFsmFloat(rule.SourceVariable))
                || ReferenceEquals(b.Output, b.Celsius)
                || ReferenceEquals(b.Output, FsmVariables.GlobalVariables.FindFsmFloat(rule.GaugeVariable)))
                throw new InvalidOperationException("Temperature references no longer identify this vehicle.");
            if (topology)
            {
                var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                if (FindTemperatureFsm(fsms, rule.GaugePath, "Temp") != b.Gauge
                    || FindTemperatureFsm(fsms, rule.SourcePath, rule.SourceFsm) != b.Source)
                    throw new InvalidOperationException("Temperature source replaced.");
            }
            if (rule.HostAuthoritative)
            {
                if (b.EngineCelsius == null || ReferenceEquals(b.EngineCelsius, b.Celsius)
                    || !ReferenceEquals(b.EngineCelsius, FsmVariables.GlobalVariables.FindFsmFloat(rule.EngineGlobal))
                    || b.Source.FsmVariables.FindFsmFloat(rule.EngineGlobal) != null)
                    throw new InvalidOperationException("Engine temperature global changed.");
                int ready = 0;
                foreach (var state in b.Source.Fsm.States)
                    if (state.Name == rule.ReadyState)
                    { ready++; if (!ReferenceEquals(state, b.ReadyState)) ready++; }
                if (ready != 1 || rule.ReadyState == b.Source.Fsm.StartState)
                    throw new InvalidOperationException("Coolant readiness state changed.");
            }
            int matching = 0;
            foreach (var state in b.Gauge.Fsm.States)
                if (state.Name == "Speed") { matching++; if (!ReferenceEquals(state, b.State)) matching++; }
            if (matching != 1 || !b.State.IsInitialized || b.State.Actions.Length < 3 || !ReferenceEquals(b.State.Actions[0], b.Read)
                || !ReferenceEquals(b.State.Actions[1], b.Scale) || !ReferenceEquals(b.State.Actions[2], b.Clamp))
                throw new InvalidOperationException("Temperature gauge state replaced.");
            TemperatureAction(b.Read, "GetFsmFloat");
            TemperatureAction(b.Scale, "FloatOperator");
            TemperatureAction(b.Clamp, "FloatClamp");
            var target = TemperatureField(b.Read, "gameObject") as FsmOwnerDefault;
            if (!b.Read.Enabled || !b.Clamp.Enabled || target == null || target.OwnerOption != OwnerDefaultOption.SpecifyGameObject
                || target.GameObject == null || target.GameObject.Value != b.Source.gameObject
                || !TemperatureLiteral(TemperatureField(b.Read, "fsmName"), rule.SourceFsm)
                || !TemperatureLiteral(TemperatureField(b.Read, "variableName"), rule.SourceVariable)
                || !ReferenceEquals(TemperatureField(b.Read, "storeValue"), b.Output)
                || !ReferenceEquals(TemperatureField(b.Clamp, "floatVariable"), b.Output))
                throw new InvalidOperationException("Temperature reader target or output changed.");
            b.Divisor = 1f;
            if (b.Scale.Enabled)
            {
                if (TemperatureField(b.Scale, "operation").ToString() != "Divide"
                    || !ReferenceEquals(TemperatureField(b.Scale, "float1"), b.Output)
                    || !ReferenceEquals(TemperatureField(b.Scale, "storeResult"), b.Output))
                    throw new InvalidOperationException("Unsupported temperature gauge scale.");
                b.Divisor = TemperatureConstant(TemperatureField(b.Scale, "float2"));
                if (b.Divisor == 0f) throw new InvalidOperationException("Zero temperature gauge divisor.");
            }
            b.Minimum = TemperatureConstant(TemperatureField(b.Clamp, "minValue"));
            b.Maximum = TemperatureConstant(TemperatureField(b.Clamp, "maxValue"));
            if (b.Minimum > b.Maximum) throw new InvalidOperationException("Inverted temperature gauge range.");
        }

        private static bool FiniteTemperature(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float ReadCoolantTempC(SyncedItem item)
        {
            EnsureTemperatureProbe(item);
            if (item.RequiresNativeTemperature)
            {
                var binding = item.NativeTemperature;
                // A stopped producer still owns its last physical temperature.
                // Neither a dashboard clamp nor an observer packet can replace it.
                return binding != null && FiniteTemperature(binding.Celsius.Value) ? binding.Celsius.Value : 0f;
            }
            var heater = item.HeaterUnitFsm?.FsmVariables.FindFsmFloat("CoolantTemp");
            return heater != null && FiniteTemperature(heater.Value) ? heater.Value : 0f;
        }

        private static void ApplyRemoteTemperature(SyncedItem item, float celsius)
        {
            if (UsesHostCoolant(item) && !TryGetHostCoolant(item, out celsius)) return;
            ApplyTemperatureGauge(item, celsius);
        }

        private static void ApplyTemperatureGauge(SyncedItem item, float celsius)
        {
            EnsureTemperatureProbe(item);
            var binding = item.NativeTemperature;
            if (binding != null)
            {
                if ((TryGetHostCoolant(item, out _) || HasRemoteTemperature(item)) && EnsureTemperatureReadHook(binding.Read.GetType()))
                    TemperatureReaders[binding.Read] = item;
                binding.Output.Value = Mathf.Clamp(celsius / binding.Divisor, binding.Minimum, binding.Maximum);
            }
        }
    }
}
