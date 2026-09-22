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
        internal sealed class NativeCabinTemperatureInputs
        {
            internal SyncedItem Item = null!;
            internal VehicleTemperatureSourceData Rule = null!;
            internal PlayMakerFSM Cabin = null!, Heater = null!, Source = null!;
            internal FsmState CabinState = null!, HeaterState = null!;
            internal FsmStateAction[] CabinActions = null!, HeaterActions = null!;
            internal FsmFloat Engine = null!, Coolant = null!, Output = null!;
            internal readonly FsmFloat Input = new FsmFloat();
            internal FieldInfo Operand = null!;
            internal int Depth;
        }

        private static void EnsureCabinTemperatureInputs(SyncedItem item)
        {
            if (item.Body == null) return;
            try
            {
                if (item.NativeCabinTemperature != null)
                { ValidateCabinTemperatureInputs(item.NativeCabinTemperature); return; }
                var rule = HostCoolantRule(item.Id);
                if (rule?.CabinInputs == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextCabinTemperatureProbeAt) return;
                item.NextCabinTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var p = rule.CabinInputs; var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                var b = new NativeCabinTemperatureInputs { Item = item, Rule = rule,
                    Cabin = FindTemperatureFsm(fsms, p.CabinPath, p.CabinFsm), Heater = FindTemperatureFsm(fsms, p.HeaterPath, p.HeaterFsm),
                    Source = FindTemperatureFsm(fsms, rule.SourcePath, rule.SourceFsm) };
                b.CabinState = HeatState(b.Cabin, p.CabinState); b.HeaterState = HeatState(b.Heater, p.HeaterState);
                b.CabinActions = (FsmStateAction[])b.CabinState.Actions.Clone(); b.HeaterActions = (FsmStateAction[])b.HeaterState.Actions.Clone();
                if (b.CabinActions.Length != 8 || b.HeaterActions.Length != 12) throw new InvalidOperationException("Cabin thermal states changed.");
                b.Engine = FsmVariables.GlobalVariables.FindFsmFloat(rule.EngineGlobal);
                b.Coolant = b.Source.FsmVariables.FindFsmFloat(rule.SourceVariable);
                b.Output = b.Heater.FsmVariables.FindFsmFloat("CoolantTemp");
                b.Operand = b.CabinActions[4].GetType().GetField("float1", BindingFlags.Instance | BindingFlags.Public);
                ValidateCabinTemperatureInputs(b); EnsureCabinTemperatureHooks(b);
                item.NativeCabinTemperature = b;
                CabinTemperatureReaders[b.CabinActions[4]] = b; CabinTemperatureReaders[b.HeaterActions[0]] = b;
                SyncEventLog.Record("cabin-temperature-inputs-bound", item.Path);
            }
            catch (Exception error)
            {
                ClearCabinTemperatureInputs(item); NoteCabinTemperatureFailure(item, error);
                item.NextCabinTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            }
        }

        private static void ValidateCabinTemperatureInputs(NativeCabinTemperatureInputs b)
        {
            var item = b.Item; var rule = b.Rule; var p = rule.CabinInputs;
            if (!ReferenceEquals(HostCoolantRule(item.Id), rule) || p == null || item.Body == null
                || item.Path != rule.RootPath || ScenePath.Of(item.Body.transform) != rule.RootPath)
                throw new InvalidOperationException("Cabin temperature catalog or vehicle changed.");
            CabinTemperatureFsm(item, b.Cabin, p.CabinPath, p.CabinFsm);
            CabinTemperatureFsm(item, b.Heater, p.HeaterPath, p.HeaterFsm);
            CabinTemperatureFsm(item, b.Source, rule.SourcePath, rule.SourceFsm);
            CabinTemperatureState(b.Cabin, p.CabinState, b.CabinState, b.CabinActions, 8);
            CabinTemperatureState(b.Heater, p.HeaterState, b.HeaterState, b.HeaterActions, 12);
            var scale = b.CabinActions[4]; var clamp = b.CabinActions[5]; var read = b.HeaterActions[0];
            CabinTemperatureAction(scale, "FloatOperator"); CabinTemperatureAction(clamp, "FloatClamp");
            CabinTemperatureAction(read, "GetFsmFloat");
            if (b.Engine == null || b.Coolant == null || b.Output == null || b.Operand == null || b.Operand.FieldType != typeof(FsmFloat)
                || !ReferenceEquals(b.Engine, FsmVariables.GlobalVariables.FindFsmFloat(rule.EngineGlobal))
                || b.Cabin.FsmVariables.FindFsmFloat(rule.EngineGlobal) != null
                || !ReferenceEquals(b.Operand.GetValue(scale), b.Depth > 0 ? b.Input : b.Engine)
                || TemperatureField(scale, "operation").ToString() != "Divide" || TemperatureConstant(TemperatureField(scale, "float2")) != 5
                || !ReferenceEquals(b.Coolant, b.Source.FsmVariables.FindFsmFloat(rule.SourceVariable))
                || ReferenceEquals(b.Output, b.Coolant) || ReferenceEquals(b.Output, b.Engine)
                || !ReferenceEquals(b.Output, b.Heater.FsmVariables.FindFsmFloat("CoolantTemp")))
                throw new InvalidOperationException("Cabin thermal sources or engine arithmetic changed.");
            CabinTemperatureLocal(b.Cabin, scale, "storeResult", "MaxTemp");
            CabinTemperatureLocal(b.Cabin, clamp, "floatVariable", "InteriorTemp");
            CabinTemperatureLocal(b.Cabin, clamp, "minValue", "TempArea");
            CabinTemperatureLocal(b.Cabin, clamp, "maxValue", "MaxTemp");
            CabinTemperatureLocal(b.Heater, read, "storeValue", "CoolantTemp");
            var target = TemperatureField(read, "gameObject") as FsmOwnerDefault;
            if (target == null || target.OwnerOption != OwnerDefaultOption.SpecifyGameObject || target.GameObject == null
                || target.GameObject.Value != b.Source.gameObject
                || !TemperatureLiteral(TemperatureField(read, "fsmName"), rule.SourceFsm)
                || !TemperatureLiteral(TemperatureField(read, "variableName"), rule.SourceVariable))
                throw new InvalidOperationException("Heater coolant target changed.");
        }

        private static void CabinTemperatureFsm(SyncedItem item, PlayMakerFSM fsm, string path, string name)
        {
            if (fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != name || ScenePath.Of(fsm.transform) != path
                || item.Body == null || !fsm.transform.IsChildOf(item.Body.transform))
                throw new InvalidOperationException("Cabin thermal FSM changed.");
            int count = 0; foreach (var candidate in fsm.GetComponents<PlayMakerFSM>()) if (candidate.FsmName == name) count++;
            if (count != 1) throw new InvalidOperationException("Ambiguous cabin thermal FSM.");
        }

        private static void CabinTemperatureState(PlayMakerFSM fsm, string name, FsmState state, FsmStateAction[] actions, int count)
        {
            if (HeatState(fsm, name) != state || state.Actions.Length != count || actions.Length != count)
                throw new InvalidOperationException("Cabin thermal state changed.");
            for (int i = 0; i < count; i++) if (!ReferenceEquals(actions[i], state.Actions[i]))
                throw new InvalidOperationException("Cabin thermal action array changed.");
        }

        private static void CabinTemperatureAction(FsmStateAction action, string name)
        {
            if (!action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + name
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp" || (bool)TemperatureField(action, "everyFrame"))
                throw new InvalidOperationException("Cabin thermal action signature changed.");
        }

        private static void CabinTemperatureLocal(PlayMakerFSM fsm, FsmStateAction action, string field, string name)
        {
            var value = fsm.FsmVariables.FindFsmFloat(name);
            if (value == null || ReferenceEquals(value, FsmVariables.GlobalVariables.FindFsmFloat(name))
                || !ReferenceEquals(value, TemperatureField(action, field)))
                throw new InvalidOperationException("Cabin thermal scratch variable changed.");
        }

        private static void ClearCabinTemperatureInputs(SyncedItem item)
        {
            var b = item.NativeCabinTemperature;
            if (b != null)
                foreach (var action in new[] { b.CabinActions[4], b.HeaterActions[0] })
                    if (CabinTemperatureReaders.TryGetValue(action, out var current) && ReferenceEquals(current, b))
                        CabinTemperatureReaders.Remove(action);
            item.NativeCabinTemperature = null;
        }

        private static void NoteCabinTemperatureFailure(SyncedItem item, Exception error)
        {
            if (Time.unscaledTime < item.NextCabinTemperatureErrorAt) return;
            item.NextCabinTemperatureErrorAt = Time.unscaledTime + 10f;
            try { SyncEventLog.Record("cabin-temperature-inputs-unavailable", item.Path + ": " + error.Message); }
            catch { /* Diagnostics cannot escape into the native heater. */ }
        }
    }
}
