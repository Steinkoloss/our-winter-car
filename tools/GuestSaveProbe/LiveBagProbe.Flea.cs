using System;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool FleaProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_FLEA_TEST") == "1";
        private const string FleaCash = "FleaMarket/LOD/FleaCashRegister/CashRegisterLogic";
        private const string FleaEnvelope = "FleaMarket/LOD/OpenHours/MoneyFlea";
        private static object FleaCoordinator => Get(WorldSyncManager.Instance!, "_fleaSale");
        private static PlayMakerFSM FleaLogic => Find("FleaMarket/SaleTable", "Logic");
        private static bool FleaCommand(string[] args, List<string> rows)
        {
            if (!FleaProbe || !args[1].StartsWith("flea-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            var session = SessionManager.Instance!;
            if (FleaListingCommand(args, rows)) return true;
            switch (args[1])
            {
                case "flea-view": return true;
                case "flea-visit":
                    if (Player == null || Player.parent != null) throw new InvalidOperationException("Unseated player required.");
                    Player.position = Find(args[2] == "envelope" ? FleaEnvelope : FleaCash, args[2] == "envelope" ? "Use" : "Data").transform.position
                        + new Vector3(0, 0, args[2] == "far" ? 600 : args[2] == "away" ? 30 : 1);
                    return true;
                case "flea-open":
                    foreach (var path in new[] { "FleaMarket/LOD", "FleaMarket/LOD/OpenHours", "FleaMarket/LOD/FleaCashRegister" })
                    {
                        Transform? target = null;
                        foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(Transform)))
                            if (PathOf((Transform)obj) == path) { target = (Transform)obj; break; }
                        if (target == null) throw new InvalidOperationException("Missing fixture target " + path);
                        target.gameObject.SetActive(true);
                    }
                    rows.Add("flea-fixture|opened native shop presentation in disposable profile"); return true;
                case "flea-merch":
                    if (session.IsHost) throw new InvalidOperationException("Guest cart fixture only.");
                    var b = Get(FleaCoordinator, "_binding");
                    ((System.Collections.IList)Get(b, "_bought"))[0] = args[2] == "on";
                    rows.Add("flea-fixture|merchandise basket flag changed with zero total to test rental-only boundary"); return true;
                case "flea-add": Enter(Find("FleaMarket/LOD/OpenHours/BuyTableRent", "Buy"), "Add"); return true;
                case "flea-remove": Enter(Find("FleaMarket/LOD/OpenHours/BuyTableRent", "Buy"), "Remove"); return true;
                case "flea-checkout": Enter(Find(FleaCash, "Data"), "Check money"); return true;
                case "flea-collect": Enter(Find(FleaEnvelope, "Use"), "State 1"); return true;
                case "flea-wallet":
                    if (!session.IsHost) throw new InvalidOperationException("Host fixture only.");
                    FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney").Value = float.Parse(args[2], CultureInfo.InvariantCulture);
                    rows.Add("flea-fixture|host wallet set"); return true;
                case "flea-offer":
                    if (!session.IsHost) throw new InvalidOperationException("Host fixture only.");
                    FleaLogic.FsmVariables.FindFsmInt("RentDays").Value = -1;
                    FleaLogic.FsmVariables.FindFsmFloat("MoneyTotal").Value = float.Parse(args[2], CultureInfo.InvariantCulture);
                    FleaLogic.SendEvent("RESET");
                    rows.Add("flea-fixture|ended native rental and staged proceeds; no actual item sale claimed"); return true;
                case "flea-request":
                    if (session.IsHost) throw new InvalidOperationException("Guest request fixture only.");
                    session.SendWorldMessage(new FleaSaleIntent { PlayerId = session.LocalPlayerId, Action = byte.Parse(args[2]),
                        Sequence = ushort.Parse(args[3]), Revision = uint.Parse(args[4]), Weeks = ushort.Parse(args[5]) }, Channel.ReliableOrdered);
                    return true;
                default: throw new InvalidOperationException("Unknown flea probe command.");
            }
        }
        private static void FleaSnapshot(List<string> rows)
        {
            RequirePersistenceSandbox();
            if (Application.loadedLevelName != "GAME") return;
            var c = FleaCoordinator;
            FleaListingSnapshot(rows);
            rows.Add("flea-ready|" + (Get(c, "_binding") != null) + "|" + Get(c, "_failed"));
            var state = SessionManager.Instance!.IsHost ? Call(c, "BuildSnapshot") as FleaSaleState : Get(c, "_received") as FleaSaleState;
            rows.Add("flea-state|" + (state == null ? "none" : state.Revision + "|" + state.RentDays + "|" + state.MoneyTotal + "|" + state.Flags + "|" + state.WeekPrice));
            var pending = Get(c, "_pending") as FleaSaleIntent;
            rows.Add("flea-pending|" + (pending == null ? "none" : pending.Sequence + "|" + pending.Action + "|" + pending.Revision));
            foreach (var pair in new[] { new[] { "FleaMarket/SaleTable", "Logic" }, new[] { "FleaMarket/SaleTable", "Sell" },
                new[] { "FleaMarket/SaleTable", "DayChanger" }, new[] { FleaCash, "Data" }, new[] { FleaEnvelope, "Use" } })
            {
                var f = Find(pair[0], pair[1]);
                rows.Add("flea-fsm|" + pair[1] + "|" + f.enabled + "|" + f.gameObject.activeInHierarchy + "|" + f.ActiveStateName);
                foreach (var v in f.FsmVariables.FloatVariables) rows.Add("flea-float|" + pair[1] + "|" + v.Name + "|" + v.Value.ToString("R", CultureInfo.InvariantCulture));
                foreach (var v in f.FsmVariables.IntVariables) rows.Add("flea-int|" + pair[1] + "|" + v.Name + "|" + v.Value);
            }
            rows.Add("flea-wallet|" + FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney").Value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
