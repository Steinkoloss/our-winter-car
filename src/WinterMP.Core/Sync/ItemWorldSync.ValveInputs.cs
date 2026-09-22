using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class GuestValveBinding
        {
            internal PlayMakerFSM Fsm = null!;
            internal GuestValveInputsData Rule = null!;
            internal GameObject Proxy = null!;
            internal FsmGameObject Source = null!;
            internal readonly List<GuestEngineInputRead> Reads = new List<GuestEngineInputRead>();
            internal readonly List<FsmVar> Results = new List<FsmVar>();
        }
        private readonly List<GuestValveBinding> _guestValveInputs = new List<GuestValveBinding>();
        private bool _valveCaptureFailed;

        private static IList ValveArray(GameObject head, string reference)
        {
            Component? found = null;
            foreach (var component in head.GetComponents<Component>())
                if (component != null && component.GetType().FullName == "PlayMakerArrayListProxy" && component.GetType().Assembly.GetName().Name == "Assembly-CSharp"
                    && component.GetType().GetField("referenceName")?.GetValue(component) as string == reference)
                {
                    if (found != null) throw new InvalidOperationException("Ambiguous valve adjustment array.");
                    found = component;
                }
            var values = found?.GetType().GetProperty("arrayList")?.GetValue(found, null) as IList;
            if (values == null || values.Count != EngineBlockState.ValveCount) throw new InvalidOperationException("Incomplete valve adjustment array.");
            return values;
        }
        private void CaptureValveSettings(GameObject? head, EngineBlockState state)
        {
            try
            {
                if (head != null)
                {
                    var values = ValveArray(head, SyncCatalog.GuestEngineInputs!.Valves.Reference); var next = new float[EngineBlockState.ValveCount];
                    for (int i = 0; i < next.Length; i++)
                    {
                        if (values[i] is not float value || float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Invalid valve adjustment.");
                        next[i] = value;
                    }
                    state.ValveSettings = next; state.ValvesAvailable = true;
                }
                _valveCaptureFailed = false;
            }
            catch (Exception error)
            {
                if (!_valveCaptureFailed) { _valveCaptureFailed = true; SyncEventLog.Record("valves-unavailable", error.Message); WinterMPPlugin.Log.LogWarning("WorldSync: valve input unavailable: " + error.Message); }
            }
        }
        private void PrepareGuestValveInputs(PlayMakerFSM fsm, GuestValveInputsData rule)
        {
            if (fsm.FsmName != rule.Fsm || ScenePath.Of(fsm.transform) != rule.ReaderPath) return;
            GuestValveBinding? binding = null;
            foreach (var old in new List<GuestValveBinding>(_guestValveInputs))
            {
                if (old.Fsm == null) { RestoreGuestValveInputs(old); continue; }
                if (ReferenceEquals(old.Fsm, fsm)) binding = old;
            }
            if (binding != null && (binding.Proxy == null || !ValveActionsCurrent(binding))) { RestoreGuestValveInputs(binding); binding = null; }
            if (binding == null)
            {
                binding = new GuestValveBinding { Fsm = fsm, Rule = rule, Source = fsm.FsmVariables.FindFsmGameObject(rule.TargetVariable) };
                if (binding.Source == null) throw new InvalidOperationException("Missing native valve head reference.");
                for (int i = 0; i < rule.States.Length; i++)
                {
                    var state = GuestEngineInputState(fsm, rule.States[i]); if (state.Actions.Length == 0) throw new InvalidOperationException("Missing valve array reader.");
                    var action = state.Actions[0]; var field = GuestEngineInputField(action.GetType(), "gameObject", typeof(FsmOwnerDefault));
                    var original = field.GetValue(action) as FsmOwnerDefault;
                    var result = PackageField<FsmVar>(action, "result");
                    if (original == null || result == null) throw new InvalidOperationException("Incomplete native valve reader.");
                    binding.Reads.Add(new GuestEngineInputRead { State = state, Action = action, TargetField = field, Original = original, Output = fsm.FsmVariables.FindFsmFloat(rule.Output) });
                    binding.Results.Add(result);
                }
                ValidateValveReads(binding, false);
                binding.Proxy = new GameObject("WinterMP valve inputs"); _guestValveInputs.Add(binding);
                try
                {
                    var type = binding.Reads[0].Action.GetType().Assembly.GetType("PlayMakerArrayListProxy", true);
                    var proxy = binding.Proxy.AddComponent(type); type.GetField("referenceName").SetValue(proxy, rule.Reference);
                    var values = (IList)type.GetProperty("arrayList").GetValue(proxy, null); values.Clear(); for (int i = 0; i < 8; i++) values.Add(0f);
                    foreach (var read in binding.Reads)
                    {
                        read.Target = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = binding.Proxy } };
                        read.TargetField.SetValue(read.Action, read.Target);
                    }
                }
                catch { RestoreGuestValveInputs(binding); throw; }
            }
            if (!ReferenceEquals(binding.Rule, rule)) throw new InvalidOperationException("Valve input profile changed.");
            ValidateValveReads(binding, true);
            var array = ValveArray(binding.Proxy, rule.Reference); var session = SessionManager.Instance;
            var stateRecord = session != null && !session.IsHost && session.State == SessionState.Connected ? _engineBlockReplica.Inputs : null;
            for (int i = 0; i < 8; i++) array[i] = stateRecord != null && stateRecord.ValvesAvailable ? stateRecord.ValveSettingAt(i) : 0f;
        }
        private static bool ValveActionsCurrent(GuestValveBinding binding)
        {
            foreach (var read in binding.Reads)
            {
                if (Array.IndexOf(binding.Fsm.Fsm.States, read.State) < 0 || read.State.Actions.Length == 0 || !ReferenceEquals(read.State.Actions[0], read.Action)) return false;
            }
            return true;
        }
        private static void ValidateValveReads(GuestValveBinding binding, bool owned)
        {
            var fsm = binding.Fsm; var rule = binding.Rule;
            if (!binding.Source.UseVariable || binding.Source.Name != rule.TargetVariable
                || !ReferenceEquals(fsm.FsmVariables.FindFsmGameObject(rule.TargetVariable), binding.Source)) throw new InvalidOperationException("Valve head scratch changed.");
            var headState = GuestEngineInputState(fsm, rule.HeadState); if (headState.Actions.Length <= rule.HeadActionIndex) throw new InvalidOperationException("Missing native valve head read.");
            var head = headState.Actions[rule.HeadActionIndex]; var owner = PackageField<FsmOwnerDefault>(head, "gameObject");
            if (!head.Enabled || head.GetType().FullName != "HutongGames.PlayMaker.Actions.GetFsmGameObject" || head.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || owner == null || owner.OwnerOption != OwnerDefaultOption.SpecifyGameObject || owner.GameObject == null || !owner.GameObject.UseVariable
                || owner.GameObject.Name != "db_Cylinderhead" || !ReferenceEquals(owner.GameObject, fsm.FsmVariables.FindFsmGameObject("db_Cylinderhead"))
                || PackageField<FsmString>(head, "fsmName") is not FsmString name || name.UseVariable || name.Value != "Data"
                || PackageField<FsmString>(head, "variableName") is not FsmString variable || variable.UseVariable || variable.Value != "ActivePart"
                || !ReferenceEquals(PackageField<FsmGameObject>(head, "storeValue"), binding.Source) || (bool)GuestEngineInputField(head.GetType(), "everyFrame", typeof(bool)).GetValue(head))
                throw new InvalidOperationException("Native valve head read changed.");
            for (int i = 0; i < binding.Reads.Count; i++)
            {
                var read = binding.Reads[i]; var action = read.Action; var target = owned ? read.Target : read.Original;
                var reference = PackageField<FsmString>(action, "reference"); var index = PackageField<FsmInt>(action, "atIndex"); var result = PackageField<FsmVar>(action, "result");
                if (!action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions.ArrayListGet" || action.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                    || read.State.Name != rule.States[i] || !ReferenceEquals(read.TargetField.GetValue(action), target)
                    || read.Original.OwnerOption != OwnerDefaultOption.SpecifyGameObject || !ReferenceEquals(read.Original.GameObject, binding.Source)
                    || owned && (target.OwnerOption != OwnerDefaultOption.SpecifyGameObject || target.GameObject == null || target.GameObject.UseVariable || target.GameObject.Name.Length != 0 || target.GameObject.Value != binding.Proxy)
                    || reference == null || reference.UseVariable || reference.Value != rule.Reference || index == null || index.UseVariable || index.Value != i
                    || result == null || !ReferenceEquals(result, binding.Results[i]) || !result.useVariable || result.Type != VariableType.Float || result.variableName != rule.Output
                    || !ReferenceEquals(read.Output, fsm.FsmVariables.FindFsmFloat(rule.Output)) || read.Output == null
                    || PackageField<FsmEvent>(action, "failureEvent") is FsmEvent failure && failure.Name.Length != 0)
                    throw new InvalidOperationException("Native valve array reader changed at " + rule.States[i] + ".");
            }
        }
        private void RestoreGuestValveInputs(GuestValveBinding binding)
        {
            foreach (var read in binding.Reads)
                if (read.Target != null && ReferenceEquals(read.TargetField.GetValue(read.Action), read.Target)) read.TargetField.SetValue(read.Action, read.Original);
            foreach (var read in binding.Reads)
                if (binding.Proxy != null && read.TargetField.GetValue(read.Action) is FsmOwnerDefault owner && owner.GameObject?.Value == binding.Proxy)
                    throw new InvalidOperationException("Changed valve reader still references owned inputs.");
            if (binding.Proxy != null) UnityEngine.Object.Destroy(binding.Proxy); _guestValveInputs.Remove(binding);
        }
    }
}
