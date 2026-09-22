using System;
using System.Reflection;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static FsmFloat StarterLocalFloat(PlayMakerFSM fsm, FsmStateAction action, string field, string name)
        {
            var value = Field<FsmFloat>(action, field);
            if (!value.UseVariable || value.Name != name || !ReferenceEquals(value, fsm.FsmVariables.FindFsmFloat(name)))
                throw new InvalidOperationException("Native starter local operand changed: " + name);
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(value, global)) throw new InvalidOperationException("Starter operand aliases a global.");
            return value;
        }

        private static FsmFloat ValidateStarterWearAmount(PlayMakerFSM fsm, FsmStateAction action)
        {
            var value = StarterLocalFloat(fsm, action, "subtractValue", "StarterWear");
            if (!Field<bool>(action, "everyFrame") || !Field<bool>(action, "perSecond"))
                throw new InvalidOperationException("Native starter wear cadence changed.");
            return value;
        }

        internal static bool ApplyHostStarterWear(PlayMakerFSM fsm, StarterWearRequest request)
        {
            if (!Initialize() || !request.Valid || !fsm.enabled || !fsm.gameObject.activeInHierarchy || !fsm.Fsm.Started
                || FindRule(fsm) is not GuestEngineWriterData writer || SyncCatalog.GuestEngineInputs?.Battery == null) return false;
            GuestEngineWriteActionData? selected = null;
            foreach (var rule in writer.Actions)
            {
                if ((rule.StarterWear || rule.StarterDraw != 0) && fsm.ActiveStateName == rule.State) return false;
                if (rule.StarterWear) selected = rule;
            }
            if (selected == null || !HostStarterBool(fsm, "db_Starter", "Installed") || !HostStarterBool(fsm, "db_Flywheel", "Installed")
                || !HostStarterBool(fsm, "db_WiringStarter", "Bolted") || !HostStarterBool(fsm, "db_WiringBatteryHarness", "Bolted")
                || !HostStarterBool(fsm, "db_WiringGround", "Installed")) return false;
            var action = ValidateWrite(fsm, selected).Action;
            var data = ResolveStarterData(action);
            if (!action.Enabled || data == null || data != HostStarterData(fsm, "db_Starter")) return false;
            var wear = data.FsmVariables.FindFsmFloat("Wear"); var durability = data.FsmVariables.FindFsmFloat("Durability");
            if (wear == null || durability == null || !Finite(wear.Value) || !Finite(durability.Value) || durability.Value < 0) return false;

            // The host is not entering Fuel Mixture while a guest drives. Read its
            // native rate and current mounted durability instead of stale scratch.
            var read = FindAction(fsm, "Starter damage", 1, "GetFsmFloat").Action;
            if (!read.Enabled || !NamedTarget(Field<FsmOwnerDefault>(read, "gameObject"), "db_Starter")
                || fsm.Fsm.GetOwnerDefaultTarget(Field<FsmOwnerDefault>(read, "gameObject")) != data.gameObject
                || !Literal(Field<FsmString>(read, "fsmName"), "Data") || !Literal(Field<FsmString>(read, "variableName"), "Durability")
                || Field<bool>(read, "everyFrame")) return false;
            var scratchDurability = StarterLocalFloat(fsm, read, "storeValue", "StarterDurability");
            var calculation = FindAction(fsm, "Fuel Mixture", selected.Index - 1, "FloatOperator").Action;
            var factor = Field<FsmFloat>(calculation, "float1");
            var operation = calculation.GetType().GetField("operation");
            if (!calculation.Enabled || operation == null || Convert.ToInt32(operation.GetValue(calculation)) != 2
                || factor.UseVariable || !Finite(factor.Value) || factor.Value < 0 || Field<bool>(calculation, "everyFrame")
                || !ReferenceEquals(StarterLocalFloat(fsm, calculation, "float2", "StarterDurability"), scratchDurability)
                || !ReferenceEquals(StarterLocalFloat(fsm, calculation, "storeResult", "StarterWear"), ValidateStarterWearAmount(fsm, action))) return false;
            float amount = factor.Value * durability.Value * request.Seconds;
            if (!Finite(amount) || !Finite(wear.Value - amount)) return false;
            var helper = action.GetType().GetMethod("DoSubtractFsmFloat", BindingFlags.Instance | BindingFlags.NonPublic);
            var amountField = action.GetType().GetField("subtractValue"); var perSecondField = action.GetType().GetField("perSecond");
            if (helper == null || amountField == null || perSecondField == null) throw new InvalidOperationException("Native starter wear helper changed.");
            var original = amountField.GetValue(action);
            // Duration was measured at the guest's native callback. Apply that
            // integral once; multiplying by the host frame time again is incorrect.
            try
            {
                amountField.SetValue(action, new FsmFloat { Value = amount }); perSecondField.SetValue(action, false);
                helper.Invoke(action, null);
            }
            finally { amountField.SetValue(action, original); perSecondField.SetValue(action, true); }
            SyncEventLog.Record("starter-wear-applied", "vehicle=" + request.VehicleId + " player=" + request.PlayerId
                + " sequence=" + request.Sequence + " seconds=" + request.Seconds);
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
