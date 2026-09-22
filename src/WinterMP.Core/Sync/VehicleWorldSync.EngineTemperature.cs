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
        internal sealed class NativeEngineTemperatureInputs
        {
            internal SyncedItem Item = null!;
            internal VehicleTemperatureSourceData Rule = null!;
            internal bool Electrical;
            internal readonly List<NativeEngineTemperatureRead> Reads = new List<NativeEngineTemperatureRead>();
        }
        internal sealed class NativeEngineTemperatureRead
        {
            internal NativeEngineTemperatureInputs Owner = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmState State = null!;
            internal FsmStateAction[] Actions = null!;
            internal FsmStateAction Action = null!;
            internal FieldInfo Field = null!;
            internal FsmFloat Original = null!;
            internal readonly FsmFloat Input = new FsmFloat();
            internal string Path = "", Name = "", StateName = "", Type = "";
            internal int Index, Depth;
        }

        private static void EnsureEngineTemperatureInputs(SyncedItem item)
        {
            if (item.Body == null) return;
            try
            {
                if (item.NativeEngineTemperature != null)
                { ValidateEngineTemperatureInputs(item.NativeEngineTemperature); return; }
                var rule = HostCoolantRule(item.Id);
                if (rule?.EngineInputs == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextEngineTemperatureProbeAt) return;
                item.NextEngineTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var profile = rule.EngineInputs; var b = new NativeEngineTemperatureInputs { Item = item, Rule = rule };
                var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                AddEngineTemperatureRead(b, fsms, profile.FuelPath, profile.FuelFsm, profile.CarburettorState, 3, 7, "FloatClamp", "minValue");
                AddEngineTemperatureRead(b, fsms, profile.FuelPath, profile.FuelFsm, profile.PrimingState, 0, 3, "FloatCompare", "float1");
                AddEngineTemperatureRead(b, fsms, profile.FuelPath, profile.FuelFsm, profile.PrimingState, 1, 3, "FloatClamp", "minValue");
                AddEngineTemperatureRead(b, fsms, profile.FuelPath, profile.MixtureFsm, profile.DensityState, 3, 7, "FloatOperator", "float1");
                AddEngineTemperatureRead(b, fsms, profile.OilPath, profile.OilFsm, profile.ViscosityState, 0, 4, "FloatOperator", "float1");
                AddEngineTemperatureRead(b, fsms, profile.OilPath, profile.PressureFsm, profile.PressureState, 0, 8, "FloatOperator", "float2");
                ValidateEngineTemperatureInputs(b); EnsureEngineTemperatureHooks(b);
                item.NativeEngineTemperature = b;
                foreach (var read in b.Reads) EngineTemperatureReaders[read.Action] = read;
                SyncEventLog.Record("engine-temperature-inputs-bound", item.Path);
            }
            catch (Exception error)
            {
                ClearEngineTemperatureInputs(item); NoteEngineTemperatureFailure(item, error);
                item.NextEngineTemperatureProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            }
        }

        private static void AddEngineTemperatureRead(NativeEngineTemperatureInputs b, PlayMakerFSM[] fsms,
            string path, string name, string stateName, int index, int count, string type, string fieldName)
        {
            var fsm = FindTemperatureFsm(fsms, path, name); var state = HeatState(fsm, stateName);
            if (state.Actions.Length != count) throw new InvalidOperationException("Engine temperature state layout changed.");
            var action = state.Actions[index]; var field = action.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            var source = FsmVariables.GlobalVariables.FindFsmFloat(b.Rule.EngineGlobal);
            if (field == null || field.FieldType != typeof(FsmFloat) || source == null)
                throw new InvalidOperationException("Engine temperature operand missing.");
            b.Reads.Add(new NativeEngineTemperatureRead { Owner = b, Fsm = fsm, State = state, Action = action,
                Actions = (FsmStateAction[])state.Actions.Clone(), Field = field, Original = source,
                Path = path, Name = name, StateName = stateName, Type = type, Index = index });
        }

        private static void ValidateEngineTemperatureInputs(NativeEngineTemperatureInputs b)
        {
            var item = b.Item; var rule = b.Rule; var profile = rule.EngineInputs;
            if (!ReferenceEquals(HostCoolantRule(item.Id), rule) || profile == null || item.Body == null
                || item.Path != rule.RootPath || ScenePath.Of(item.Body.transform) != rule.RootPath || b.Reads.Count != 6)
                throw new InvalidOperationException("Engine temperature catalog or vehicle changed.");
            foreach (var read in b.Reads)
            {
                ValidateEngineTemperatureReader(read, read.Name == profile.PressureFsm && read.Path == profile.OilPath);
                var action = read.Action;
                if (read.Type == "FloatClamp")
                {
                    EngineTemperatureLocal(read, "floatVariable", "FuelChamber");
                    if (TemperatureConstant(TemperatureField(action, "maxValue")) != 30)
                        throw new InvalidOperationException("Fuel chamber clamp changed.");
                }
                else if (read.Type == "FloatCompare")
                {
                    CoolingCompare(action, 3, "", "", "FINISHED");
                    CoolingTransitions(read.State, "DRY", "Fuel Usage", "FINISHED", "Fuel OK", "FLOODED", "Fuel Usage");
                }
                else
                {
                    bool pressure = read.Field.Name == "float2";
                    if (TemperatureField(action, "operation").ToString() != (pressure ? "Subtract" : "Add"))
                        throw new InvalidOperationException("Native temperature arithmetic changed.");
                    EngineTemperatureLocal(read, "storeResult", pressure ? "Math1" : read.Name == profile.MixtureFsm ? "Multiplier" : "TempAdjusted");
                    if (pressure) EngineTemperatureLocal(read, "float1", "ModifierTemp");
                    else if (TemperatureConstant(TemperatureField(action, "float2")) != 50)
                        throw new InvalidOperationException("Native temperature adjustment changed.");
                }
            }
        }

        private static void ValidateEngineTemperatureReader(NativeEngineTemperatureRead read, bool everyFrame)
        {
            var item = read.Owner.Item; var rule = read.Owner.Rule;
            if (item.Body == null) throw new InvalidOperationException("Engine temperature vehicle missing.");
            var fsm = read.Fsm; var action = read.Action;
            if (fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != read.Name || ScenePath.Of(fsm.transform) != read.Path
                || !fsm.transform.IsChildOf(item.Body.transform) || HeatState(fsm, read.StateName) != read.State
                || read.State.Actions.Length != read.Actions.Length || !action.Enabled
                || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + read.Type
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || (bool)TemperatureField(action, "everyFrame") != everyFrame
                || fsm.FsmVariables.FindFsmFloat(rule.EngineGlobal) != null
                || !ReferenceEquals(read.Original, FsmVariables.GlobalVariables.FindFsmFloat(rule.EngineGlobal))
                || !ReferenceEquals(read.Field.GetValue(action), read.Depth > 0 ? read.Input : read.Original))
                throw new InvalidOperationException("Engine temperature reader changed.");
            int count = 0;
            foreach (var candidate in fsm.GetComponents<PlayMakerFSM>()) if (candidate.FsmName == read.Name) count++;
            if (count != 1) throw new InvalidOperationException("Ambiguous engine temperature FSM.");
            for (int i = 0; i < read.Actions.Length; i++)
                if (!ReferenceEquals(read.Actions[i], read.State.Actions[i])) throw new InvalidOperationException("Engine temperature action array changed.");
        }

        private static void EngineTemperatureLocal(NativeEngineTemperatureRead read, string field, string name)
        {
            var value = read.Fsm.FsmVariables.FindFsmFloat(name);
            if (value == null || ReferenceEquals(value, read.Original) || !ReferenceEquals(value, TemperatureField(read.Action, field)))
                throw new InvalidOperationException("Engine temperature scratch variable changed.");
        }

        private static void ClearEngineTemperatureInputs(SyncedItem item)
        {
            var b = item.NativeEngineTemperature;
            if (b != null)
                foreach (var read in b.Reads)
                    if (EngineTemperatureReaders.TryGetValue(read.Action, out var current) && ReferenceEquals(current, read))
                        EngineTemperatureReaders.Remove(read.Action);
            item.NativeEngineTemperature = null;
        }

        private static void NoteEngineTemperatureFailure(SyncedItem item, Exception error)
        {
            if (Time.unscaledTime < item.NextEngineTemperatureErrorAt) return;
            item.NextEngineTemperatureErrorAt = Time.unscaledTime + 10f;
            try { SyncEventLog.Record("engine-temperature-inputs-unavailable", item.Path + ": " + error.Message); }
            catch { /* Diagnostics must not escape into native engine calculations. */ }
        }
    }
}
