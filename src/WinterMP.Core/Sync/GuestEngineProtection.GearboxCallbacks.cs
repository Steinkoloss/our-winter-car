using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static readonly Dictionary<FsmStateAction, Write> GearboxUseObservers = new Dictionary<FsmStateAction, Write>();
        private static bool ApplyingHostGearboxUse;

        private static bool IsGearboxUseObserver(Write write) => write.GearboxUseRule != null
            && GearboxUseObservers.TryGetValue(write.Action, out var observed) && ReferenceEquals(write, observed);

        private static bool IsDelegatedGearboxUse(FsmStateAction action)
        {
            if (action.GetType().FullName != "HutongGames.PlayMaker.Actions.SubtractFsmFloat") return false;
            var owner = action.Fsm?.Owner as PlayMakerFSM;
            if (owner == null || FindRule(owner) is not GuestEngineWriterData rule) return false;
            var world = WorldSyncManager.Instance;
            if (world == null || !(rule.GearboxOilRead != null ? world.ShouldDelegateGearboxOil(owner) : world.ShouldDelegateGearboxWear(owner))) return false;
            foreach (var selected in rule.Actions)
                if (selected.GearboxOilUse || selected.GearboxWear)
                {
                    var state = FindState(owner, selected.State);
                    if (FsmHook.IsNativeAction(state, selected.Index, action)) return true;
                }
            return false;
        }
        private static bool ObserveGearboxUse(FsmStateAction action)
        {
            if (!GearboxUseObservers.TryGetValue(action, out var write)) return false;
            var owner = action.Fsm?.Owner as PlayMakerFSM;
            try
            {
                if (owner == null || write.GearboxUseRule == null || !Bindings.TryGetValue(owner, out var binding)
                    || !Current(binding) || !ReferenceEquals(binding.Rule, FindRule(owner))
                    || !FsmHook.IsNativeAction(write.State, write.Index, action))
                    throw new InvalidOperationException("Stale gearbox-use callback.");
                if (write.GearboxUseRule.GearboxWear)
                {
                    if (binding.GearboxConditionRead == null) throw new InvalidOperationException("Missing gearbox failure reader.");
                    ValidateGearboxConditionSignature(owner, binding.GearboxConditionRead.Action);
                    ValidateWrite(owner, write.GearboxUseRule);
                }
                else { ValidateDrivetrainProtection(binding); ValidateOilUseCalculations(owner); }
                if (!action.Enabled || !owner.enabled || !owner.gameObject.activeInHierarchy || owner.Fsm.ActiveState != write.State
                    || Failures.ContainsKey(owner) || binding.Entry == 0 || write.ObservedEntry == binding.Entry) return true;
                write.ObservedEntry = binding.Entry;
                if (write.GearboxUseRule.GearboxWear) WorldSyncManager.Instance?.RecordGearboxWear(owner);
                else WorldSyncManager.Instance?.RecordGearboxOilUse(owner, write.State.Name == "State 1" ? (byte)1 : (byte)3);
            }
            catch (Exception error) { if (owner != null) Fail(owner, error); else NoteFailure("gearbox callback", error); }
            return true;
        }
        private static void RetireGearboxUseObservers()
        {
            foreach (var pair in new List<KeyValuePair<FsmStateAction, Write>>(GearboxUseObservers))
                if (pair.Value.State.Fsm.Owner is not PlayMakerFSM fsm || fsm == null) GearboxUseObservers.Remove(pair.Key);
        }
    }
}
