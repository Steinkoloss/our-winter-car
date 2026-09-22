using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static FsmFloat ValidateOilUseAmount(PlayMakerFSM fsm, FsmStateAction action)
        {
            var amount = StarterLocalFloat(fsm, action, "subtractValue", "OilLeakRate");
            if (Field<bool>(action, "everyFrame") || Field<bool>(action, "perSecond"))
                throw new InvalidOperationException("Native gearbox oil use is no longer once per entry.");
            return amount;
        }
        private static FsmStateAction[] ValidateOilUseCalculations(PlayMakerFSM fsm)
        {
            var math = FindAction(fsm, "Set stall speed", 4, "FloatOperator").Action;
            var divide = FindAction(fsm, "Set stall speed", 5, "FloatDivide").Action;
            var clamp = FindAction(fsm, "Set stall speed", 6, "FloatClamp").Action;
            if (!math.Enabled || !divide.Enabled || !clamp.Enabled || Field<bool>(math, "everyFrame")
                || Field<bool>(divide, "everyFrame") || Field<bool>(clamp, "everyFrame")
                || Convert.ToInt32(math.GetType().GetField("operation").GetValue(math)) != 1)
                throw new InvalidOperationException("Native gearbox oil calculation changed.");
            OilConstant(math, "float1", 15f); StarterLocalFloat(fsm, math, "float2", "GearboxWear");
            var rate = StarterLocalFloat(fsm, math, "storeResult", "OilLeakRate");
            if (!ReferenceEquals(rate, StarterLocalFloat(fsm, divide, "floatVariable", "OilLeakRate"))
                || !ReferenceEquals(rate, StarterLocalFloat(fsm, clamp, "floatVariable", "OilLeakRate")))
                throw new InvalidOperationException("Native gearbox oil calculation aliases another value.");
            OilConstant(divide, "divideBy", 5000f); OilConstant(clamp, "minValue", .0000001f); OilConstant(clamp, "maxValue", 1f);
            return new[] { math, divide, clamp };
        }
        private static void OilConstant(FsmStateAction action, string field, float expected)
        {
            var value = Field<FsmFloat>(action, field);
            if (value == null || value.UseVariable || value.Value != expected) throw new InvalidOperationException("Native gearbox oil constant changed.");
        }

        internal static bool ApplyHostGearboxOil(PlayMakerFSM fsm, byte phase, float hostWear, float hostOil, GameObject expectedMount)
        {
            if (!Initialize() || !fsm.gameObject.activeInHierarchy || !fsm.Fsm.Initialized || GuestSaveGuard.ProtectWorld
                || FindRule(fsm) is not GuestEngineWriterData rule || rule.GearboxOilRead == null)
                throw new InvalidOperationException("Native gearbox oil consumer is not ready.");
            foreach (var read in DrivetrainReaders(rule)) ValidateDrivetrainReader(fsm, read);
            GuestEngineWriteActionData? selected = null;
            foreach (var write in rule.Actions) if (write.GearboxOilUse && write.State == (phase == 1 ? "State 1" : "State 3")) selected = write;
            if (selected == null) throw new InvalidOperationException("Native gearbox oil-use phase is missing.");
            var action = ValidateWrite(fsm, selected).Action;
            if (!action.Enabled) throw new InvalidOperationException("Native gearbox oil writer is disabled.");
            var data = ResolveStarterData(action);
            if (data == null || data.gameObject != expectedMount || ScenePath.Of(data.transform) != SyncCatalog.VehicleDrivetrainWear?.Targets[1].Path
                || !data.transform.IsChildOf(fsm.transform.root) || !data.Fsm.Started || !data.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Native gearbox oil saved target is not ready.");
            var oil = data.FsmVariables.FindFsmFloat("OilLevel"); var wear = data.FsmVariables.FindFsmFloat("Wear");
            var installed = data.FsmVariables.FindFsmBool("Installed"); var type = data.FsmVariables.FindFsmInt("Type");
            if (oil == null || wear == null || oil.Value != hostOil || wear.Value != hostWear || installed == null || !installed.Value
                || !installed.UseVariable || type == null || !type.UseVariable || type.Value != 2)
                throw new InvalidOperationException("Native gearbox oil mounted state changed.");
            foreach (var global in FsmVariables.GlobalVariables.BoolVariables) if (ReferenceEquals(global, installed)) return false;
            foreach (var global in FsmVariables.GlobalVariables.IntVariables) if (ReferenceEquals(global, type)) return false;
            var calculations = ValidateOilUseCalculations(fsm); var rate = new FsmFloat(0); var wearInput = new FsmFloat(hostWear);
            var fields = new[] { calculations[0].GetType().GetField("float2"), calculations[0].GetType().GetField("storeResult"),
                calculations[1].GetType().GetField("floatVariable"), calculations[2].GetType().GetField("floatVariable"), action.GetType().GetField("subtractValue") };
            var owners = new[] { calculations[0], calculations[0], calculations[1], calculations[2], action };
            var original = new object[fields.Length]; for (int i = 0; i < fields.Length; i++) original[i] = fields[i].GetValue(owners[i]);
            bool applying = ApplyingHostGearboxUse;
            try
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(owners[i], i == 0 ? wearInput : rate);
                OilHelper(calculations[0], "DoFloatOperator"); calculations[1].OnUpdate(); OilHelper(calculations[2], "DoClamp");
                if (!Finite(rate.Value) || rate.Value < .0000001f || rate.Value > 1 || !Finite(oil.Value - rate.Value))
                    throw new InvalidOperationException("Native gearbox oil calculation produced an invalid amount: " + rate.Value.ToString("R"));
                // Native helpers retain float32 intermediate storage and the real
                // saved subtraction, without entering the host's driving state.
                ApplyingHostGearboxUse = true; OilHelper(action, "DoSubtractFsmFloat");
                return true;
            }
            finally
            {
                ApplyingHostGearboxUse = applying;
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(owners[i], original[i]);
            }
        }
        private static void OilHelper(FsmStateAction action, string name)
        {
            var method = action.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (method == null || method.GetMethodBody() == null) throw new InvalidOperationException("Native gearbox oil helper changed: " + name);
            method.Invoke(action, null);
        }
    }
}
