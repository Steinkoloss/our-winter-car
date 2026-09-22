using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static PlayMakerFSM TaxiPayments => Find(TaxiRoot + "/TaxiFunctions", "Payments");
        private static PlayMakerFSM TaxiPaydayEnvelope => Find("HOMENEW/Stuff/PlayerMailBox/Move/EnvelopeTaxiRundown", "Use");
        private static PlayMakerFSM TaxiPaydaySheet => Find("Sheets/TaxiRundown", "Setup");
        private static IList PaydayList()
        {
            foreach (var component in TaxiPayments.GetComponents<MonoBehaviour>())
                if (component != null && component.GetType().Name == "PlayMakerArrayListProxy"
                    && (string)component.GetType().GetField("referenceName").GetValue(component) == "Rundown")
                    return (IList)component.GetType().GetProperty("arrayList").GetValue(component, null);
            throw new InvalidOperationException("No native rundown list.");
        }
        private static bool TaxiPaydayCommand(string[] args)
        {
            var s = SessionManager.Instance!;
            switch (args[1])
            {
                case "taxi-payday-fixture":
                    if (!s.IsHost) throw new InvalidOperationException("Host payday fixture only.");
                    var meter = Find(TaxiRoot + "/TaxiFunctions/Tripmeter", "Function");
                    meter.FsmVariables.FindFsmFloat("IncomeTotal").Value = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                    meter.FsmVariables.FindFsmFloat("IncomeReceipts").Value = float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
                    meter.FsmVariables.FindFsmFloat("OdoTotal").Value = float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
                    var odo = Find(TaxiRoot + "/Functions/Dashboard/Odometer", "Data");
                    TaxiPayments.FsmVariables.FindFsmFloat("CarOdoOldF").Value = odo.FsmVariables.FindFsmInt("OdometerReading").Value * 10f
                        + odo.FsmVariables.FindFsmFloat("Odo").Value / 1000f - float.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
                    TaxiPayments.FsmVariables.FindFsmFloat("CallMinutes").Value = float.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture);
                    TaxiPayments.FsmVariables.FindFsmBool("Employed").Value = true;
                    TaxiPayments.FsmVariables.FindFsmInt("PaymentDay").Value = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay").Value;
                    return true;
                case "taxi-payday-trigger":
                    if (!s.IsHost) throw new InvalidOperationException("Host payday clock fixture only.");
                    Enter(TaxiPayments, "Add day"); return true;
                case "taxi-payday-near":
                    Player!.position = TaxiPaydayEnvelope.transform.position + Vector3.up * .5f + Vector3.forward * .5f;
                    return true;
                case "taxi-payday-open":
                    if (!TaxiPaydayEnvelope.gameObject.activeInHierarchy) throw new InvalidOperationException("No visible salary letter.");
                    Enter(TaxiPaydayEnvelope, "Open ad"); return true;
                case "taxi-payday-mailbox":
                    Enter(Find("HOMENEW/Stuff/PlayerMailBox/Hatch/Pivot/OpenMailBox", "Use"), "Open"); return true;
                case "taxi-payday-close":
                    if (!TaxiPaydaySheet.gameObject.activeInHierarchy) throw new InvalidOperationException("No open salary sheet.");
                    TaxiPaydaySheet.SendEvent("FINISHED"); return true;
                case "taxi-payday-read-intent":
                    s.SendWorldMessage(new TaxiPaydayReadIntent { PlayerId = s.LocalPlayerId, PaydayId = uint.Parse(args[2]) }, Channel.ReliableOrdered);
                    return true;
                default: return false;
            }
        }
        private static void TaxiPaydaySnapshot(List<string> rows)
        {
            var p = TaxiPayments;
            rows.Add("payday-native|" + p.enabled + "|" + p.ActiveStateName + "|" + p.FsmVariables.FindFsmBool("Rundown").Value
                + "|" + TaxiPaydayEnvelope.gameObject.activeSelf + "|" + TaxiPaydaySheet.gameObject.activeSelf);
            rows.Add("payday-input|" + TaxiPaydayEnvelope.enabled + "|" + TaxiPaydayEnvelope.Fsm.Started
                + "|" + FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu").Value);
            foreach (string name in new[] { "Money", "MoneyOrig", "CarOdoOldF", "KMsDriven", "CallMinutes", "Phonebill", "FuelExpenses" })
                rows.Add("payday-value|" + name + "|" + AtfNumber(p.FsmVariables.FindFsmFloat(name).Value));
            foreach (string name in new[] { "PlayerMoney", "PlayerBankAccount", "PlayerNetIncome" })
                rows.Add("payday-balance|" + name + "|" + AtfNumber(FsmVariables.GlobalVariables.FindFsmFloat(name).Value));
            var list = PaydayList(); for (int i = 0; i < list.Count; i++) rows.Add("payday-row|" + i + "|" + list[i]);
            foreach (var f in TaxiPaydaySheet.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Data") rows.Add("payday-text|" + f.name + "|" + f.GetComponent<TextMesh>().text);
            var b = Get(Get(WinterMP.Core.Sync.WorldSyncManager.Instance!, "_taxiJob"), "_service");
            rows.Add("payday-binding|" + (b == null ? "none" : Get(b, "_paydayId") + "|" + Get(b, "_shownPaydayId")));
            var bank = Find("Systems/BankAccount", "Data");
            rows.Add("payday-bank-entry|" + bank.FsmVariables.FindFsmString("DataDescription").Value + "|" + bank.FsmVariables.FindFsmString("DataValue").Value);
        }
    }
}
