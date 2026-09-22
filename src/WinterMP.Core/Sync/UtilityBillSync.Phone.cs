using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class UtilityBillSync
    {
        private static UtilityBillState ObserveInvoice(Meter meter)
        {
            byte flags = meter.PowerBool!.Value ? UtilityBillState.FlagPowerOn : (byte)0;
            if (meter.Bill!.activeSelf) flags |= UtilityBillState.FlagBillVisible;
            if (meter.IsElectricity && meter.MainSwitch!.Value) flags |= UtilityBillState.FlagMainSwitchOn;
            var state = new UtilityBillState { Meter = meter.Kind, UnpaidBills = meter.UnpaidBills!.Value, Flags = flags };
            bool changed = false;
            if (!meter.IsElectricity)
            {
                var u = meter.PhoneUsage!; var rates = meter.Payment.PhoneRates!;
                state.Phone = new PhoneBillQuote { Minutes = u[0].Value, MinutesLong = u[1].Value,
                    Connects = u[2].Value, ConnectsLong = u[3].Value, Base = rates[0].Value,
                    ConnectionRate = rates[1].Value, MinuteRate = rates[2].Value, LongMinuteRate = rates[3].Value };
                changed = !state.Phone.Same(meter.ObservedPhone);
            }
            if (!UtilityBillPolicy.Valid(state)) throw new InvalidOperationException("Invalid native utility invoice.");
            meter.Ledger.Observe(state.BillVisible ? state.PaymentTotal : 0, state.BillVisible, changed);
            if (changed) meter.ObservedPhone = state.Phone;
            state.Revision = meter.Ledger.Revision;
            return state;
        }

        private static void ApplyPhoneUsage(Meter meter, PhoneBillQuote quote)
        {
            var u = meter.PhoneUsage!;
            u[0].Value = quote.Minutes; u[1].Value = quote.MinutesLong;
            u[2].Value = quote.Connects; u[3].Value = quote.ConnectsLong;
        }

        private static void RenderPhone(BillPayment p, PhoneBillQuote q)
        {
            var values = new[] { q.Connects, q.Connects * q.ConnectionRate, q.Minutes, q.Minutes * q.MinuteRate,
                q.ConnectsLong, q.ConnectsLong * q.ConnectionRate, q.MinutesLong, q.MinutesLong * q.LongMinuteRate };
            for (int i = 0; i < values.Length; i++) p.PhoneDetails![i].text = values[i].ToString(i % 2 == 0 ? "0" : "0.00");
        }

        private sealed class ResetPhoneTotal : FsmStateAction
        {
            private readonly FsmFloat _total;
            internal ResetPhoneTotal(FsmFloat total) { _total = total; }
            // Native Totals adds to CostFinal; reopening must not compound the prior bill.
            public override void OnEnter() { _total.Value = 0; Finish(); }
        }

        private void LocatePhonePayment(Meter meter)
        {
            var p = meter.Payment;
            var c = SyncCatalog.PhonePayments;
            // Fallback paths are used only to block native payment if its catalog is missing.
            string path = c == null ? "Sheets/PhoneBill" + (meter.Kind - 1) : c[meter.Kind == 2 ? "sheet1" : "sheet2"];
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                if (ScenePath.Of(fsm.transform) == path && fsm.FsmName == (c == null ? "Data" : c["sheetFsm"])) p.Sheet = fsm;
                if (ScenePath.Of(fsm.transform) == path + "/" + (c == null ? "Pay" : c["payChild"])
                    && fsm.FsmName == (c == null ? "Button" : c["payFsm"])) p.Pay = fsm;
            }
            if (p.Pay != null) p.Failure.Suppress(p.Pay);
            if (c == null) throw new InvalidOperationException("Phone payment catalog missing.");
            if (p.Sheet == null || p.Pay == null) return;
            p.Config = c;
            foreach (var fsm in new[] { p.Sheet, p.Pay }) if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            p.Cash = FsmVariables.GlobalVariables.FindFsmFloat(SyncCatalog.Banking?.CashGlobal ?? string.Empty);
            p.Total = p.Sheet.FsmVariables.FindFsmFloat(c["total"]);
            p.TotalString = p.Sheet.FsmVariables.FindFsmString(c["totalText"]);
            p.OldBill = p.Sheet.FsmVariables.FindFsmBool(c["oldBill"]);
            if (p.Cash == null || p.Total == null || p.TotalString == null || p.OldBill == null)
                throw new InvalidOperationException("Phone sheet variables missing.");
            string[] rateKeys = { "base", "connectionRate", "minuteRate", "longMinuteRate" };
            p.PhoneRates = new FsmFloat[4];
            for (int i = 0; i < 4; i++) p.PhoneRates[i] = p.Sheet.FsmVariables.FindFsmFloat(c[rateKeys[i]])
                ?? throw new InvalidOperationException("Phone tariff missing.");
            var request = FsmHook.FindState(p.Pay, c["request"]) ?? throw new InvalidOperationException("Phone request state missing.");
            var commit = FsmHook.FindState(p.Pay, c["commit"]) ?? throw new InvalidOperationException("Phone commit state missing.");
            var ra = PaymentActions(request, "SetMaterial", "GetFsmFloat", "FloatCompare");
            var ca = PaymentActions(commit, "MasterAudioPlaySound", "SetFsmBool", "FloatSubtract", "SendEvent");
            var target = Field<FsmEventTarget>(ca[3], "eventTarget");
            string payTotal = Field<FsmFloat>(ra[1], "storeValue").Name;
            if (PaymentTarget(p.Pay, ra[1]) != p.Sheet.gameObject
                || Field<FsmString>(ra[1], "fsmName").Value != c["sheetFsm"]
                || Field<FsmString>(ra[1], "variableName").Value != c["total"]
                || Field<FsmFloat>(ra[2], "float1").Name != payTotal || Field<FsmFloat>(ra[2], "float2").Name != p.Cash.Name
                || PaymentTarget(p.Pay, ca[1]) != p.Sheet.gameObject
                || Field<FsmString>(ca[1], "variableName").Value != c["oldBill"] || Field<FsmBool>(ca[1], "setValue").Value
                || Field<FsmFloat>(ca[2], "floatVariable").Name != p.Cash.Name || Field<FsmFloat>(ca[2], "subtract").Name != payTotal
                || Field<FsmEvent>(ca[3], "sendEvent").Name != c["payEvent"] || (int)target.target != 2
                || p.Pay.Fsm.GetOwnerDefaultTarget(target.gameObject) != meter.Data!.gameObject || target.fsmName.Value != meter.Data.FsmName
                || Field<FsmFloat>(ca[3], "delay").Value != 0 || Field<FsmFloat>(ca[3], "delay").UseVariable || Field<bool>(ca[3], "everyFrame"))
                throw new InvalidOperationException("Native phone payment arguments changed.");
            BindPhoneCalculation(meter, c);
            var settlement = FsmHook.FindState(meter.Data, "Pay bills") ?? throw new InvalidOperationException("Phone settlement missing.");
            var actions = PaymentActions(settlement, "ActivateGameObject", "SetBoolValue", "SetFloatValue", "SetFloatValue",
                "SetFloatValue", "SetFloatValue", "SetFloatValue", "SetFloatValue");
            bool transition = false;
            foreach (var t in meter.Data.Fsm.GlobalTransitions)
                if (t.EventName == c["payEvent"] && t.ToState == settlement.Name) transition = true;
            if (!transition || PaymentTarget(meter.Data, actions[0]) != meter.Bill || Field<FsmBool>(actions[0], "activate").Value
                || Field<FsmBool>(actions[1], "boolVariable").Name != meter.PowerBool!.Name || !Field<FsmBool>(actions[1], "boolValue").Value)
                throw new InvalidOperationException("Native phone settlement changed.");
            string[] cleared = { c["debt"], "WaitCutoff", c["minutes"], c["longMinutes"], c["connects"], c["longConnects"] };
            for (int i = 0; i < cleared.Length; i++)
                if (Field<FsmFloat>(actions[i + 2], "floatVariable").Name != cleared[i]
                    || Field<FsmFloat>(actions[i + 2], "floatValue").UseVariable || Field<FsmFloat>(actions[i + 2], "floatValue").Value != 0)
                    throw new InvalidOperationException("Native phone usage reset changed.");
            foreach (var key in new[] { "commit", "close", "funds", "idle" })
                if (!FsmHook.EnsureRemoteEntry(p.Pay, c[key])) throw new InvalidOperationException("Phone presentation state missing.");
            p.RequestState = request; p.RequestActions = request.Actions;
            p.CommitState = commit; p.CommitActions = commit.Actions;
            request.Actions = new FsmStateAction[] { new PaymentRequestAction(() => QueuePayment(meter)) };
            commit.Actions = new[] { ca[0], ca[1] };
            p.Ready = true; p.Failure.Restore();
            WinterMPPlugin.Log.LogInfo("Phone payment bound: " + path);
        }

        private static void BindPhoneCalculation(Meter meter, UtilityPaymentsData c)
        {
            var p = meter.Payment; var sheet = p.Sheet!;
            var read = FsmHook.FindState(sheet, c["calculate"]) ?? throw new InvalidOperationException("Phone calculation missing.");
            var inputs = PaymentActions(read, "GetFsmFloat", "GetFsmFloat", "GetFsmFloat", "GetFsmFloat");
            string[] keys = { "minutes", "longMinutes", "connects", "longConnects" };
            string[] stores = new string[4];
            for (int i = 0; i < 4; i++)
            {
                if (PaymentTarget(sheet, inputs[i]) != meter.Data!.gameObject || Field<FsmString>(inputs[i], "variableName").Value != c[keys[i]])
                    throw new InvalidOperationException("Native phone usage source changed.");
                stores[i] = Field<FsmFloat>(inputs[i], "storeValue").Name;
            }
            p.PhoneDetails = new TextMesh[8];
            string[] totals = new string[2];
            for (int half = 0; half < 2; half++)
            {
                var state = FsmHook.FindState(sheet, c[half == 0 ? "localCalculate" : "longCalculate"])
                    ?? throw new InvalidOperationException("Phone charge calculation missing.");
                var actions = PaymentActions(state, "FloatOperator", "FloatOperator", "ConvertFloatToString", "ConvertFloatToString",
                    "ConvertFloatToString", "ConvertFloatToString", "SetProperty", "SetProperty", "SetProperty", "SetProperty", "FloatOperator");
                VerifyPhoneOperator(actions[0], stores[half], c[half == 0 ? "minuteRate" : "longMinuteRate"], 2);
                VerifyPhoneOperator(actions[1], stores[half + 2], c["connectionRate"], 2);
                VerifyPhoneOperator(actions[10], Field<FsmFloat>(actions[1], "storeResult").Name, Field<FsmFloat>(actions[0], "storeResult").Name, 0);
                totals[half] = Field<FsmFloat>(actions[10], "storeResult").Name;
                int[] sources = { 4, 5, 2, 3 };
                for (int i = 0; i < 4; i++) p.PhoneDetails[half * 4 + i] = PhoneText(actions[6 + i],
                    Field<FsmString>(actions[sources[i]], "stringVariable").Name);
            }
            var total = FsmHook.FindState(sheet, c["totals"]) ?? throw new InvalidOperationException("Phone total missing.");
            var ta = PaymentActions(total, "FloatAdd", "FloatAdd", "FloatAdd", "FloatClamp", "ConvertFloatToString", "SetProperty");
            string[] additions = { totals[0], totals[1], c["base"] };
            for (int i = 0; i < 3; i++)
                if (Field<FsmFloat>(ta[i], "floatVariable").Name != c["total"] || Field<FsmFloat>(ta[i], "add").Name != additions[i]
                    || Field<bool>(ta[i], "everyFrame") || Field<bool>(ta[i], "perSecond")) throw new InvalidOperationException("Phone total addition changed.");
            if (Field<FsmFloat>(ta[3], "floatVariable").Name != c["total"] || Field<FsmFloat>(ta[3], "minValue").Value != 0
                || Field<FsmFloat>(ta[3], "maxValue").Value != 999999 || Field<FsmFloat>(ta[4], "floatVariable").Name != c["total"]
                || Field<FsmString>(ta[4], "stringVariable").Name != c["totalText"])
                throw new InvalidOperationException("Phone total/clamp changed.");
            p.TotalText = PhoneText(ta[5], c["totalText"]);
            p.CalculateState = read; p.CalculateActions = read.Actions;
            var reset = new FsmStateAction[inputs.Length + 1]; reset[0] = new ResetPhoneTotal(p.Total!);
            Array.Copy(inputs, 0, reset, 1, inputs.Length); read.Actions = reset;
        }

        private static TextMesh PhoneText(FsmStateAction action, string source)
        {
            var property = Field<FsmProperty>(action, "targetProperty");
            return property.PropertyName == "text" && property.StringParameter.Name == source && property.TargetObject.Value is TextMesh text ? text
                : throw new InvalidOperationException("Phone bill text target changed.");
        }

        private static void VerifyPhoneOperator(FsmStateAction action, string first, string second, int op)
        {
            if (Field<FsmFloat>(action, "float1").Name != first || Field<FsmFloat>(action, "float2").Name != second
                || Convert.ToInt32(action.GetType().GetField("operation").GetValue(action)) != op || Field<bool>(action, "everyFrame"))
                throw new InvalidOperationException("Phone tariff operation changed.");
        }
    }
}
