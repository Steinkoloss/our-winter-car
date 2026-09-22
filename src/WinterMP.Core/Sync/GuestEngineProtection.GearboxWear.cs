using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static FsmFloat ValidateGearboxWearAmount(PlayMakerFSM fsm, FsmStateAction action)
        {
            var amount = Field<FsmFloat>(action, "subtractValue");
            if (amount == null || amount.UseVariable || amount.Value != .0525f
                || Field<bool>(action, "everyFrame") || Field<bool>(action, "perSecond")
                || !ReferenceEquals(Field<FsmOwnerDefault>(action, "gameObject").GameObject, fsm.FsmVariables.FindFsmGameObject("db_Gearbox")))
                throw new InvalidOperationException("Native gearbox failure wear amount or cadence changed.");
            return amount;
        }
        internal static bool ApplyHostGearboxWear(PlayMakerFSM fsm, float hostWear, GameObject expectedMount)
        {
            if (!Initialize() || GuestSaveGuard.ProtectWorld || !fsm.gameObject.activeInHierarchy || !fsm.Fsm.Initialized || !fsm.Fsm.Started
                || FindRule(fsm) is not GuestEngineWriterData rule || rule.GearboxConditionRead == null)
                throw new InvalidOperationException("Native gearbox failure consumer is not ready.");
            ValidateGearboxConditionReader(fsm, rule.GearboxConditionRead);
            GuestEngineWriteActionData? selected = null;
            foreach (var action in rule.Actions) if (action.GearboxWear) selected = action;
            if (selected == null) return false;
            var write = ValidateWrite(fsm, selected).Action;
            if (!write.Enabled) throw new InvalidOperationException("Native gearbox failure writer is disabled.");
            var data = ResolveStarterData(write);
            if (data == null || data.gameObject != expectedMount || ScenePath.Of(data.transform) != SyncCatalog.VehicleDrivetrainWear?.Targets[1].Path
                || !data.transform.IsChildOf(fsm.transform.root) || !data.Fsm.Initialized || !data.Fsm.Started || !data.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Native gearbox failure saved target changed.");
            var wear = data.FsmVariables.FindFsmFloat("Wear"); var damage = data.FsmVariables.FindFsmInt("DamageType");
            var installed = data.FsmVariables.FindFsmBool("Installed");
            if (wear == null || wear.IsNone || !wear.UseVariable || !Finite(wear.Value) || wear.Value != hostWear
                || damage == null || damage.IsNone || !damage.UseVariable || damage.Value < 1 || damage.Value > 3
                || installed == null || installed.IsNone || !installed.UseVariable || !installed.Value) return false;
            int wears = 0, damages = 0, installations = 0;
            foreach (var value in data.FsmVariables.FloatVariables) if (value.Name == "Wear") wears++;
            foreach (var value in data.FsmVariables.IntVariables) if (value.Name == "DamageType") damages++;
            foreach (var value in data.FsmVariables.BoolVariables) if (value.Name == "Installed") installations++;
            if (wears != 1 || damages != 1 || installations != 1) return false;
            foreach (var value in FsmVariables.GlobalVariables.FloatVariables) if (ReferenceEquals(value, wear)) return false;
            foreach (var value in FsmVariables.GlobalVariables.IntVariables) if (ReferenceEquals(value, damage)) return false;
            foreach (var value in FsmVariables.GlobalVariables.BoolVariables) if (ReferenceEquals(value, installed)) return false;
            if (!Finite(wear.Value - ValidateGearboxWearAmount(fsm, write).Value)) return false;
            bool applying = ApplyingHostGearboxUse;
            try
            {
                ApplyingHostGearboxUse = true;
                // Only the saved subtraction runs: no host gear change, sound,
                // random choice or entry into the guest's physical failure state.
                OilHelper(write, "DoSubtractFsmFloat"); return true;
            }
            finally { ApplyingHostGearboxUse = applying; }
        }
    }
}
