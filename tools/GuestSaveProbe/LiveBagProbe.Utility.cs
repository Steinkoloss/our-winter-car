using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static readonly object?[] _utilityRequests = new object?[4];
        private static bool UtilityCommand(string[] args, List<string> rows)
        {
            if (!args[1].StartsWith("utility-", StringComparison.Ordinal)) return false;
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_UTILITY_TEST") != "1")
                throw new InvalidOperationException("Utility probe is not enabled.");
            RequirePersistenceSandbox();
            int kind = int.Parse(args[2]);
            if (kind < 0 || kind > 3) throw new InvalidOperationException("Unknown utility meter.");
            bool phone = kind >= 2;
            string utility = phone ? "Phone" : "Electricity";
            int number = kind % 2 + 1;
            var meter = Find("Systems/" + utility + "Bills" + number, "Data");
            var sheet = Find("Sheets/" + utility + "Bill" + number, "Data");
            var pay = Find("Sheets/" + utility + "Bill" + number + "/Pay", "Button");
            var envelope = meter.FsmVariables.FindFsmGameObject("Bill").Value;
            var cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            switch (args[1])
            {
                case "utility-read": break;
                case "utility-expose":
                    // Both envelope Use components are disabled in the installed asset.
                    // Enable only this disposable fixture to exercise the payment form.
                    Find(PathOf(envelope.transform), "Use").enabled = true;
                    sheet.enabled = true; pay.enabled = true;
                    if (phone) envelope.SetActive(true);
                    break;
                case "utility-near": Player!.position = envelope.transform.position + new Vector3(0, 0, 1); break;
                case "utility-far": Player!.position = envelope.transform.position + new Vector3(0, 0, 100); break;
                case "utility-fixture":
                    if (!SessionManager.Instance!.IsHost && SessionManager.Instance.PlayerCount != 0)
                        throw new InvalidOperationException("Host or disconnected fixture only.");
                    float debt = float.Parse(args[3], CultureInfo.InvariantCulture);
                    float money = float.Parse(args[4], CultureInfo.InvariantCulture);
                    if (debt < 1 || debt > 5000 || money < 0 || money > 100000)
                        throw new InvalidOperationException("Fixture amounts outside limits.");
                    cash.Value = money;
                    meter.FsmVariables.FindFsmFloat("UnpaidBills").Value = debt;
                    if (!phone) meter.FsmVariables.FindFsmBool("MainSwitch").Value = true;
                    else
                    {
                        string[] names = { "Minutes", "MinutesLong", "Connects", "ConnectsLong" };
                        for (int i = 0; i < names.Length; i++)
                        {
                            float value = float.Parse(args[i + 5], CultureInfo.InvariantCulture);
                            if (value < 0 || value > 10000 || float.IsNaN(value)) throw new InvalidOperationException("Invalid phone usage.");
                            meter.FsmVariables.FindFsmFloat(names[i]).Value = value;
                        }
                        sheet.FsmVariables.FindFsmFloat("CostFinal").Value = 0;
                        sheet.FsmVariables.FindFsmBool("OldBill").Value = false;
                    }
                    Enter(meter, "Cut off");
                    WorldSyncManager.Instance!.ForceUtilityBillBroadcast(); break;
                case "utility-open": Enter(Find(PathOf(envelope.transform), "Use"), "Open bill"); break;
                case "utility-pay":
                    Enter(pay, "Check money");
                    var payment = UtilityPayment(kind);
                    if (payment != null) _utilityRequests[kind] = Get(payment, "Pending");
                    break;
                case "utility-retry":
                    if (_utilityRequests[kind] == null || SessionManager.Instance!.IsHost) throw new InvalidOperationException("No guest request to replay.");
                    for (int n = 0; n < 5; n++) SessionManager.Instance.SendWorldMessage((WinterMP.Net.Messages.IMessage)_utilityRequests[kind]!, WinterMP.Net.Channel.ReliableOrdered);
                    break;
                case "utility-switch":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host switch fixture only.");
                    meter.FsmVariables.FindFsmBool("MainSwitch").Value = args[3] == "on"; break;
                case "utility-close": Enter(pay, "Close"); break;
                default: throw new InvalidOperationException("Unknown utility command.");
            }
            rows.Add("utility|" + kind + "|" + meter.FsmVariables.FindFsmFloat("UnpaidBills").Value.ToString("R", CultureInfo.InvariantCulture)
                + "|" + (phone ? meter.FsmVariables.FindFsmBool("PhonePaid").Value : FsmVariables.GlobalVariables.FindFsmBool(kind == 0 ? "HouseElectricity" : "HouseElectricity2").Value)
                + "|" + (!phone && meter.FsmVariables.FindFsmBool("MainSwitch").Value) + "|" + envelope.activeSelf
                + "|" + meter.ActiveStateName + "|" + meter.enabled + "|" + sheet.gameObject.activeInHierarchy
                + "|" + sheet.FsmVariables.FindFsmFloat(phone ? "CostFinal" : "Total").Value.ToString("R", CultureInfo.InvariantCulture)
                + "|" + pay.ActiveStateName + "|" + cash.Value.ToString("R", CultureInfo.InvariantCulture)
                + "|" + Vector(envelope.transform.position) + "|" + Vector(pay.transform.position));
            if (phone)
            {
                foreach (string name in new[] { "Minutes", "MinutesLong", "Connects", "ConnectsLong", "WaitBill", "WaitCutoff" })
                    rows.Add("phone-usage|" + name + "|" + meter.FsmVariables.FindFsmFloat(name).Value.ToString("R", CultureInfo.InvariantCulture));
                foreach (string name in new[] { "CostBase", "CostConnection", "CostPerMinute", "CostPerMinuteLong", "CostFinal" })
                    rows.Add("phone-cost|" + name + "|" + sheet.FsmVariables.FindFsmFloat(name).Value.ToString("R", CultureInfo.InvariantCulture));
            }
            foreach (var peer in SessionManager.Instance!.Players)
                rows.Add("utility-peer|" + peer.PlayerId + "|" + Vector(peer.Position) + "|" + (Time.unscaledTime - peer.LastTransformTime));
            var use = Find(PathOf(envelope.transform), "Use");
            rows.Add("utility-envelope|" + use.enabled + "|" + use.Fsm.Initialized + "|" + use.Fsm.Started + "|" + envelope.activeInHierarchy);
            var binding = UtilityPayment(kind);
            if (binding != null) rows.Add("utility-payment|" + Get(binding, "Ready") + "|" + Get(binding, "Failed")
                + "|" + Get(binding, "DisplayedRevision") + "|" + (Get(binding, "Pending") != null));
            for (var node = envelope.transform; node != null; node = node.parent)
                rows.Add("utility-parent|" + node.name + "|" + node.gameObject.activeSelf);
            return true;
        }

        private static object? UtilityPayment(int kind)
        {
            var meters = (IList)Get(Get(WorldSyncManager.Instance!, "_utilityBills"), "_meters");
            return meters.Count > kind ? meters[kind].GetType().GetField("Payment", Members)?.GetValue(meters[kind]) : null;
        }
    }
}
