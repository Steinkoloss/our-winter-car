using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class UtilityBillSync
    {
        private void LocatePayment(Meter meter)
        {
            if (!meter.IsElectricity) { LocatePhonePayment(meter); return; }
            var p = meter.Payment;
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.UtilityPayments;
            if (c == null) throw new InvalidOperationException("Electricity payment catalog missing.");
            p.Config = c;
            string path = c[meter.Kind == 0 ? "sheet1" : "sheet2"];
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                if (fsm.FsmName == c["sheetFsm"] && ScenePath.Of(fsm.transform) == path) p.Sheet = fsm;
                if (fsm.FsmName == c["payFsm"] && ScenePath.Of(fsm.transform) == path + "/" + c["payChild"]) p.Pay = fsm;
            }
            if (p.Sheet == null || p.Pay == null) return;
            p.Failure.Suppress(p.Pay);
            foreach (var fsm in new[] { p.Sheet, p.Pay })
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            p.Cash = FsmVariables.GlobalVariables.FindFsmFloat(SyncCatalog.Banking?.CashGlobal ?? string.Empty);
            p.Total = p.Sheet.FsmVariables.FindFsmFloat(c["total"]);
            p.TotalString = p.Sheet.FsmVariables.FindFsmString(c["totalText"]);
            var request = FsmHook.FindState(p.Pay, c["request"]) ?? throw new InvalidOperationException("Missing payment request.");
            var commit = FsmHook.FindState(p.Pay, c["commit"]) ?? throw new InvalidOperationException("Missing payment commit.");
            var calculate = FsmHook.FindState(p.Sheet, c["calculate"]) ?? throw new InvalidOperationException("Missing bill calculation.");
            var ra = PaymentActions(request, "SetMaterial", "GetFsmFloat", "FloatCompare");
            var ca = PaymentActions(commit, "MasterAudioPlaySound", "FloatSubtract", "SendEvent");
            var da = PaymentActions(calculate, "GetFsmFloat", "GetFsmFloat", "FloatOperator", "ConvertFloatToString",
                "ConvertFloatToString", "ConvertFloatToString", "SetProperty", "SetProperty", "SetProperty");
            var eventTarget = Field<FsmEventTarget>(ca[2], "eventTarget");
            if (p.Cash == null || p.Total == null || p.TotalString == null
                || PaymentTarget(p.Pay, ra[1]) != p.Sheet.gameObject
                || Field<FsmString>(ra[1], "variableName").Value != c["total"]
                || Field<FsmString>(ra[1], "fsmName").Value != c["sheetFsm"]
                || Field<FsmFloat>(ra[1], "storeValue").Name != c["total"]
                || Field<FsmFloat>(ra[2], "float1").Name != c["total"]
                || Field<FsmFloat>(ra[2], "float2").Name != p.Cash.Name
                || Field<FsmFloat>(ca[1], "floatVariable").Name != p.Cash.Name
                || Field<FsmFloat>(ca[1], "subtract").Name != c["total"]
                || Field<FsmEvent>(ca[2], "sendEvent").Name != c["payEvent"]
                || (int)eventTarget.target != 2 || Field<FsmFloat>(ca[2], "delay").UseVariable
                || Field<FsmFloat>(ca[2], "delay").Value != 0 || Field<bool>(ca[2], "everyFrame")
                || p.Pay.Fsm.GetOwnerDefaultTarget(eventTarget.gameObject) != meter.Data!.gameObject
                || eventTarget.fsmName.Value != meter.Data.FsmName
                || PaymentTarget(p.Sheet, da[0]) != meter.Data.gameObject
                || Field<FsmString>(da[0], "variableName").Value != c["debt"]
                || Field<FsmFloat>(da[0], "storeValue").Name != c["total"])
                throw new InvalidOperationException("Native electricity payment arguments changed.");
            var text = Field<FsmProperty>(da[8], "targetProperty");
            p.TotalText = text.TargetObject.Value as TextMesh;
            if (p.TotalText == null || text.StringParameter.Name != c["totalText"] || text.PropertyName != "text")
                throw new InvalidOperationException("Electricity bill total text changed.");
            var nativePay = FsmHook.FindState(meter.Data, "Pay bills") ?? throw new InvalidOperationException("Missing native settlement.");
            var pa = PaymentActions(nativePay, "ActivateGameObject", "SetFloatValue", "SetFloatValue", "SetBoolValue");
            bool transition = false;
            foreach (var t in meter.Data.Fsm.GlobalTransitions)
                if (t.EventName == c["payEvent"] && t.ToState == nativePay.Name) transition = true;
            if (!transition || PaymentTarget(meter.Data, pa[0]) != meter.Bill || Field<FsmBool>(pa[0], "activate").Value
                || Field<FsmFloat>(pa[1], "floatVariable").Name != c["debt"]
                || Field<FsmFloat>(pa[1], "floatValue").UseVariable || Field<FsmFloat>(pa[1], "floatValue").Value != 0
                || Field<FsmFloat>(pa[2], "floatVariable").Name != "WaitCutoff"
                || Field<FsmFloat>(pa[2], "floatValue").UseVariable || Field<FsmFloat>(pa[2], "floatValue").Value != 0
                || Field<FsmBool>(pa[3], "boolVariable").Name != meter.PowerBool!.Name
                || Field<FsmBool>(pa[3], "boolValue").UseVariable || !Field<FsmBool>(pa[3], "boolValue").Value)
                throw new InvalidOperationException("Native electricity settlement changed.");
            foreach (var key in new[] { "commit", "close", "funds", "idle" })
                if (!FsmHook.EnsureRemoteEntry(p.Pay, c[key])) throw new InvalidOperationException("Missing electricity presentation state.");
            p.RequestState = request; p.RequestActions = request.Actions;
            p.CommitState = commit; p.CommitActions = commit.Actions;
            request.Actions = new FsmStateAction[] { new PaymentRequestAction(() => QueuePayment(meter)) };
            commit.Actions = new[] { ca[0] };
            p.Ready = true; p.Failure.Restore();
            WinterMPPlugin.Log.LogInfo("Electricity payment bound: " + path);
        }

        private static GameObject PaymentTarget(PlayMakerFSM fsm, FsmStateAction action)
            => fsm.Fsm.GetOwnerDefaultTarget(Field<FsmOwnerDefault>(action, "gameObject"));

        private static FsmStateAction[] PaymentActions(FsmState state, params string[] types)
        {
            var actions = state.Actions;
            if (actions.Length != types.Length) throw new InvalidOperationException("Utility action count changed: " + state.Name);
            for (int i = 0; i < types.Length; i++)
                if (!actions[i].Enabled || actions[i].GetType().Name != types[i])
                    throw new InvalidOperationException("Utility action changed: " + state.Name + "/" + i);
            return actions;
        }
    }
}
