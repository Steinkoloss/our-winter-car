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
        internal sealed class NativeWearInputs
        {
            internal SyncedItem Item = null!;
            internal VehicleWearInputData Rule = null!;
            internal readonly List<NativeWearRead> Reads = new List<NativeWearRead>();
        }

        internal sealed class NativeWearRead
        {
            internal NativeWearInputs Owner = null!;
            internal VehicleWearReaderData Rule = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmState State = null!;
            internal FsmStateAction Action = null!;
            internal FieldInfo Field = null!;
            internal FsmFloat Original = null!;
            internal readonly FsmFloat Input = new FsmFloat();
        }

        private static void EnsureWearInputs(SyncedItem item)
        {
            var rule = SyncCatalog.VehicleWearInputs;
            if (item.Body == null) return;
            try
            {
                if (item.NativeWear != null)
                {
                    if (!ReferenceEquals(item.NativeWear.Rule, rule)) throw new InvalidOperationException("Wear catalog changed.");
                    foreach (var read in item.NativeWear.Reads) ValidateWearRead(read);
                    return;
                }
                if (rule == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextWearInputProbeAt) return;
                item.NextWearInputProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                var binding = new NativeWearInputs { Item = item, Rule = rule };
                foreach (var input in rule.Readers)
                {
                    var fsm = FindTemperatureFsm(fsms, rule.Path, input.Fsm);
                    FsmState? state = null;
                    foreach (var candidate in fsm.Fsm.States)
                        if (candidate.Name == input.State)
                        {
                            if (state != null) throw new InvalidOperationException("Ambiguous wear state.");
                            state = candidate;
                        }
                    if (state == null || !state.IsInitialized || input.Index >= state.Actions.Length)
                        throw new InvalidOperationException("Wear input unavailable.");
                    var action = state.Actions[input.Index];
                    var field = action.GetType().GetField(input.Field, BindingFlags.Instance | BindingFlags.Public);
                    if (field == null || field.FieldType != typeof(FsmFloat)) throw new InvalidOperationException("Wear operand changed.");
                    var original = FsmVariables.GlobalVariables.FindFsmFloat(input.Global);
                    if (original == null) throw new InvalidOperationException("Native wear global missing.");
                    var read = new NativeWearRead { Owner = binding, Rule = input, Fsm = fsm, State = state,
                        Action = action, Field = field, Original = original };
                    ValidateWearRead(read); binding.Reads.Add(read);
                }
                if (!EnsureWearReadHook(binding.Reads[0].Action.GetType()))
                    throw new InvalidOperationException("Wear input boundary unavailable.");
                item.NativeWear = binding;
                foreach (var read in binding.Reads) WearReaders[read.Action] = read;
                SyncEventLog.Record("vehicle-wear-inputs-bound", item.Path);
            }
            catch (Exception error)
            {
                ClearWearInputs(item);
                item.NextWearInputProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                NoteWearInputFailure(item, error);
            }
        }

        private static void ValidateWearRead(NativeWearRead read)
        {
            var owner = read.Owner; var item = owner.Item; var rule = read.Rule; var fsm = read.Fsm;
            if (!ReferenceEquals(SyncCatalog.VehicleWearInputs, owner.Rule) || item.Body == null || item.Path != owner.Rule.RootPath || ScenePath.Of(item.Body.transform) != owner.Rule.RootPath
                || fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != rule.Fsm || ScenePath.Of(fsm.transform) != owner.Rule.Path
                || !fsm.transform.IsChildOf(item.Body.transform) || !read.Action.Enabled
                || read.Action.GetType().FullName != "HutongGames.PlayMaker.Actions.FloatOperator"
                || read.Action.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || fsm.FsmVariables.FindFsmFloat(rule.Global) != null
                || !ReferenceEquals(read.Original, FsmVariables.GlobalVariables.FindFsmFloat(rule.Global))
                || !ReferenceEquals(read.Field.GetValue(read.Action), read.Original))
                throw new InvalidOperationException("Wear input no longer identifies the native vehicle global.");
            int sources = 0;
            foreach (var candidate in fsm.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == rule.Fsm) sources++;
            if (sources != 1) throw new InvalidOperationException("Ambiguous native wear FSM.");
            int matches = 0;
            foreach (var state in fsm.Fsm.States)
                if (state.Name == rule.State) { matches++; if (!ReferenceEquals(state, read.State)) matches++; }
            if (matches != 1 || !read.State.IsInitialized || read.State.Actions.Length <= rule.Index || !ReferenceEquals(read.State.Actions[rule.Index], read.Action)
                || (bool)TemperatureField(read.Action, "everyFrame") != rule.EveryFrame
                || TemperatureField(read.Action, "operation").ToString() != rule.Operation)
                throw new InvalidOperationException("Native wear action signature changed.");
            var output = fsm.FsmVariables.FindFsmFloat(rule.Output);
            if (output == null || ReferenceEquals(output, read.Original)
                || !ReferenceEquals(TemperatureField(read.Action, "storeResult"), output))
                throw new InvalidOperationException("Native wear result changed.");
            var other = TemperatureField(read.Action, rule.Field == "float1" ? "float2" : "float1");
            if (rule.OtherVariable != null)
            {
                if (!ReferenceEquals(other, fsm.FsmVariables.FindFsmFloat(rule.OtherVariable)))
                    throw new InvalidOperationException("Native wear scratch operand changed.");
            }
            else if (TemperatureConstant(other) != rule.OtherConstant)
                throw new InvalidOperationException("Native wear constant changed.");
        }

        private static void ClearWearInputs(SyncedItem item)
        {
            var binding = item.NativeWear;
            if (binding != null)
                foreach (var read in binding.Reads)
                    if (WearReaders.TryGetValue(read.Action, out var current) && ReferenceEquals(current, read)) WearReaders.Remove(read.Action);
            item.NativeWear = null;
        }

        private static void NoteWearInputFailure(SyncedItem item, Exception error)
        {
            if (Time.unscaledTime < item.NextWearInputErrorAt) return;
            item.NextWearInputErrorAt = Time.unscaledTime + 10f;
            try { SyncEventLog.Record("vehicle-wear-inputs-unavailable", item.Path + ": " + error.Message); }
            catch { /* Input diagnostics cannot escape into the native wear loop. */ }
        }
    }
}
