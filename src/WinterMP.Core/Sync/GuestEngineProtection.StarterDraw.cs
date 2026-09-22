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
    internal static partial class GuestEngineProtection
    {
        private static readonly Dictionary<FsmStateAction, Write> StarterObservers = new Dictionary<FsmStateAction, Write>();

        private static FsmFloat ValidateStarterAmount(PlayMakerFSM fsm, FsmStateAction action, byte kind)
        {
            string name = kind == StarterDrawRequest.Loaded ? "StarterDraw" : "StarterDrawNoLoad";
            var amount = Field<FsmFloat>(action, "addValue");
            if (!amount.UseVariable || amount.Name != name || !ReferenceEquals(amount, fsm.FsmVariables.FindFsmFloat(name))
                || float.IsNaN(amount.Value) || float.IsInfinity(amount.Value) || amount.Value > 0
                || !Field<bool>(action, "everyFrame") || Field<bool>(action, "perSecond"))
                throw new InvalidOperationException("Native starter draw operand or cadence changed.");
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(global, amount)) throw new InvalidOperationException("Starter draw aliases a global.");
            return amount;
        }

        private static bool ObserveStarterDraw(FsmStateAction action)
        {
            if (!StarterObservers.TryGetValue(action, out var write)) return false;
            var fsm = write.State.Fsm.Owner as PlayMakerFSM;
            try
            {
                // Retain old action identities after graph replacement so a stale
                // callback can never become an unguarded native write.
                if (fsm == null || write.StarterRule == null || !Bindings.TryGetValue(fsm, out var binding)
                    || !Current(binding) || !ReferenceEquals(binding.Rule, FindRule(fsm))
                    || !FsmHook.IsNativeAction(write.State, write.Index, action))
                    throw new InvalidOperationException("Stale starter draw callback.");
                ValidateWrite(fsm, write.StarterRule);
                if (!action.Enabled || !fsm.enabled || !fsm.gameObject.activeInHierarchy || fsm.Fsm.ActiveState != write.State
                    || Failures.ContainsKey(fsm)) return true;
                var target = ResolveStarterData(action);
                if (write.StarterRule.StarterWear)
                {
                    if (target == null || target != HostStarterData(fsm, "db_Starter"))
                        throw new InvalidOperationException("Starter wear destination changed.");
                    WorldSyncManager.Instance?.RecordStarterWear(fsm, Time.deltaTime);
                }
                else
                {
                    if (target == null || !IsBatteryReadSource(target)) throw new InvalidOperationException("Starter battery destination changed.");
                    WorldSyncManager.Instance?.RecordStarterDraw(fsm, write.StarterRule.StarterDraw);
                }
            }
            catch (Exception error)
            {
                if (fsm != null) Fail(fsm, error); else NoteFailure("starter draw", error);
            }
            return true;
        }

        private static PlayMakerFSM? ResolveStarterData(FsmStateAction action)
        {
            if (!ExternalFloatWriters.TryGetValue(action.GetType(), out var fields)) return null;
            var target = action.Fsm.GetOwnerDefaultTarget(Field<FsmOwnerDefault>(action, "gameObject"));
            if (target == null) return null;
            var data = target == (GameObject)fields.LastObject.GetValue(action)
                ? fields.CachedFsm.GetValue(action) as PlayMakerFSM
                : _resolveExternalFloatFsm!(target, Field<FsmString>(action, "fsmName").Value);
            if (data == null || data.FsmName != "Data" || data.gameObject != target) return null;
            // Observation skips the native helper, including its cache updates.
            fields.LastObject.SetValue(action, target); fields.CachedFsm.SetValue(action, data);
            return data;
        }

        internal static bool ApplyHostStarterDraw(PlayMakerFSM fsm, StarterDrawRequest request)
        {
            if (!Initialize() || !fsm.enabled || !fsm.gameObject.activeInHierarchy || !fsm.Fsm.Started
                || FindRule(fsm) is not GuestEngineWriterData writer || SyncCatalog.GuestEngineInputs?.Battery == null) return false;
            GuestEngineWriteActionData? selected = null;
            foreach (var rule in writer.Actions)
                if (rule.StarterDraw != 0)
                {
                    // A still-active host crank already spends native battery power.
                    if (fsm.ActiveStateName == rule.State) return false;
                    if (selected == null && rule.StarterDraw == request.Kind) selected = rule;
                }
            if (selected == null) return false;
            var write = ValidateWrite(fsm, selected); var action = write.Action;
            if (!action.Enabled || !HostStarterBool(fsm, "db_Starter", "Installed")
                || !HostStarterBool(fsm, "db_WiringStarter", "Bolted")
                || !HostStarterBool(fsm, "db_WiringBatteryHarness", "Bolted")
                || !HostStarterBool(fsm, "db_WiringGround", "Installed")) return false;
            var flywheel = HostStarterData(fsm, "db_Flywheel")?.FsmVariables.FindFsmBool("Installed");
            if (flywheel == null || flywheel.Value != (request.Kind == StarterDrawRequest.Loaded)) return false;
            var battery = ResolveStarterData(action); var batteryRule = SyncCatalog.GuestEngineInputs.Battery;
            if (battery == null || ScenePath.Of(battery.transform) != batteryRule.Path || battery.FsmName != batteryRule.Fsm
                || battery.gameObject != GameObject.Find(batteryRule.Path)) return false;
            var charge = battery.FsmVariables.FindFsmFloat("Charge"); var amount = ValidateStarterAmount(fsm, action, request.Kind);
            if (charge == null || float.IsNaN(charge.Value) || float.IsInfinity(charge.Value)
                || float.IsInfinity(charge.Value + amount.Value * request.Count)) return false;
            var helper = action.GetType().GetMethod("DoSubtractFsmFloat", BindingFlags.Instance | BindingFlags.NonPublic);
            if (helper == null) throw new InvalidOperationException("Native starter drain helper changed.");
            for (int i = 0; i < request.Count; i++) helper.Invoke(action, null);
            SyncEventLog.Record("starter-draw-applied", "vehicle=" + request.VehicleId + " player=" + request.PlayerId
                + " sequence=" + request.Sequence + " kind=" + request.Kind + " count=" + request.Count);
            return true;
        }

        private static bool HostStarterBool(PlayMakerFSM fsm, string source, string variable)
            => HostStarterData(fsm, source)?.FsmVariables.FindFsmBool(variable)?.Value == true;

        private static PlayMakerFSM? HostStarterData(PlayMakerFSM fsm, string source)
        {
            var obj = fsm.FsmVariables.FindFsmGameObject(source)?.Value;
            if (obj == null || !obj.activeInHierarchy) return null;
            PlayMakerFSM? found = null;
            foreach (var data in obj.GetComponents<PlayMakerFSM>())
                if (data.FsmName == "Data") { if (found != null) return null; found = data; }
            return found;
        }

        private static void RetireStarterObservers()
        {
            foreach (var pair in new List<KeyValuePair<FsmStateAction, Write>>(StarterObservers))
                if ((pair.Value.State.Fsm.Owner as PlayMakerFSM) == null) StarterObservers.Remove(pair.Key);
        }
    }
}
