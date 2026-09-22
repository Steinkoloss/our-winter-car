using System;
using System.Collections;
using System.Collections.Generic;
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
        private static bool FirewoodCommand(string[] args, List<string> rows)
        {
            if (!args[1].StartsWith("wood-", StringComparison.Ordinal)) return false;
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_FIREWOOD_TEST") != "1")
                throw new InvalidOperationException("Firewood probe not enabled.");
            var sync = Get(WorldSyncManager.Instance!, "_fsm");
            var controls = (IDictionary)Get(sync, "_controls");
            if (args[1] == "wood-visit" || args[1] == "wood-order-native" || args[1] == "wood-delivered-native")
            {
                int house = int.Parse(args[2]);
                if (house < 1 || house > 4 || Player == null) throw new InvalidOperationException("Invalid site.");
                var job = Find("JOBS/HouseWood" + house + "/WoodJob" + house + "Point", "Logic");
                if (args[1] == "wood-visit") Player.position = job.transform.position + new Vector3(0, 1, float.Parse(args[3]));
                else
                {
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    if (args[1] == "wood-order-native")
                    {
                        FsmVariables.GlobalVariables.FindFsmInt("GlobalHour").Value = 12;
                        job.SendEvent("ORDERTAKEN");
                    }
                    else
                    {
                        var buyer = job.FsmVariables.FindFsmGameObject("Buyer").Value;
                        var animation = Find(PathOf(buyer.transform), "Animations");
                        if (!buyer.activeInHierarchy || !animation.enabled) throw new InvalidOperationException("Native buyer is not available.");
                        animation.FsmVariables.FindFsmFloat("Money").Value = args.Length > 3 ? float.Parse(args[3]) : 500;
                        animation.SendEvent("UNLOADED");
                    }
                }
            }
            else if (args[1] == "wood-leave") SessionManager.Instance!.Shutdown("Firewood buyer reconnect test");
            else if (args[1] == "wood-join") SessionManager.Instance!.StartJoinLocal("127.0.0.1", WinterMP.Net.Transport.UdpTransport.DefaultPort);
            else if (args[1] == "wood-view") { }
            else if (args[1] == "wood-buyer-snapshots")
            {
                if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host snapshots only.");
                foreach (FirewoodBuyerState state in (IEnumerable)Call(sync, "BuildFirewoodBuyerSnapshots"))
                    rows.Add("buyer-snapshot|" + state.NetId + "|" + state.Revision + "|" + state.Flags + "|" + state.Amount);
            }
            else if (args[1] == "wood-snapshots")
            {
                foreach (WorldDoorSnapshot snapshot in (IEnumerable)Call(sync, "BuildDoorSnapshotChunks"))
                    foreach (var entry in snapshot.Entries)
                        if (controls.Contains(entry.NetId) && ((string)Get(controls[entry.NetId], "Path")).StartsWith("JOBS/HouseWood", StringComparison.Ordinal))
                            rows.Add("payment-replay|" + entry.NetId);
            }
            else if (args[1] == "wood-site")
            {
                int house = int.Parse(args[2]);
                if (house < 1 || house > 4 || Player == null) throw new InvalidOperationException("Invalid site.");
                var job = Find("JOBS/HouseWood" + house + "/WoodJob" + house + "Point", "Logic");
                Player.position = job.transform.position + new Vector3(0, 1, 0);
                job.SendEvent("ORDERTAKEN");
                var buyer = job.FsmVariables.FindFsmGameObject("Buyer").Value;
                foreach (var native in buyer.transform.root.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (native.FsmName == "LOD" && PathOf(native.transform).StartsWith("JOBS/HouseWood" + house, StringComparison.Ordinal)) native.enabled = false;
                foreach (var native in buyer.GetComponents<PlayMakerFSM>())
                    if (native.FsmName == "Animations")
                    {
                        var payment = native.FsmVariables.FindFsmGameObject("PayMoney").Value;
                        foreach (var use in payment.GetComponents<PlayMakerFSM>())
                            if (use.FsmName == "Use") use.FsmVariables.FindFsmFloat("Money").Value = 500;
                        for (var p = payment.transform; p != null; p = p.parent) p.gameObject.SetActive(true);
                    }
            }
            else if (args[1] != "wood-list")
            {
                uint id = uint.Parse(args[2]);
                if (!controls.Contains(id)) throw new InvalidOperationException("Unknown control.");
                var control = controls[id]; var fsm = (PlayMakerFSM)Get(control, "Fsm");
                string path = PathOf(fsm.transform);
                if (!path.StartsWith("JOBS/HouseWood", StringComparison.Ordinal) || !path.EndsWith("/PayMoney", StringComparison.Ordinal))
                    throw new InvalidOperationException("Outside firewood payout scope.");
                switch (args[1])
                {
                    case "wood-near":
                        if (Player == null) throw new InvalidOperationException("Player missing.");
                        Player.position = fsm.transform.position + new Vector3(0, 0, 1); break;
                    case "wood-offer":
                        // Seed a pending payment to isolate collection from the delivery journey.
                        // This command is restricted to the disposable test game and profiles.
                        if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                        fsm.FsmVariables.FindFsmFloat("Money").Value = 500;
                        for (var p = fsm.transform; p != null; p = p.parent) p.gameObject.SetActive(true);
                        Enter(fsm, "Wait player"); break;
                    case "wood-request":
                        if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest intent only.");
                        int count = args.Length > 3 ? int.Parse(args[3]) : 1;
                        if (count < 1 || count > 10) throw new InvalidOperationException("Invalid request count.");
                        for (int i = 0; i < count; i++) SessionManager.Instance.SendWorldMessage(
                            new FsmStateEnter { NetId = id, StateName = "State 1" }, Channel.ReliableOrdered);
                        break;
                    case "wood-hide-local":
                        if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest presentation fixture only.");
                        fsm.gameObject.SetActive(false); fsm.FsmVariables.FindFsmFloat("Money").Value = 999;
                        break;
                    case "wood-stale":
                        if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest delayed-state fixture only.");
                        WorldSyncManager.Instance!.OnFirewoodBuyer(new FirewoodBuyerState { NetId = id,
                            Revision = uint.Parse(args[3]), Flags = 3, Amount = float.Parse(args[4]) });
                        break;
                    // Enter after native mouse selection; physical input is outside this driver.
                    case "wood-collect": Enter(fsm, "State 1"); break;
                    default: throw new InvalidOperationException("Unknown firewood command.");
                }
            }
            foreach (DictionaryEntry pair in controls)
            {
                var fsm = (PlayMakerFSM)Get(pair.Value, "Fsm"); string path = PathOf(fsm.transform);
                if (!path.StartsWith("JOBS/HouseWood", StringComparison.Ordinal) || !path.EndsWith("/PayMoney", StringComparison.Ordinal)) continue;
                rows.Add("wood|" + pair.Key + "|" + path + "|" + fsm.ActiveStateName + "|" + fsm.enabled
                    + "|" + fsm.gameObject.activeInHierarchy + "|" + fsm.FsmVariables.FindFsmFloat("Money").Value
                    + "|" + (pair.Value.GetType().GetField("Payment", Members)?.GetValue(pair.Value) != null));
            }
            var buyers = (IDictionary)Get(sync, "_firewoodBuyers");
            foreach (DictionaryEntry pair in buyers)
            {
                var buyer = (PlayMakerFSM)Get(pair.Value, "Buyer"); var payment = (PlayMakerFSM)Get(pair.Value, "Payment");
                var job = (PlayMakerFSM)Get(pair.Value, "Job"); var lod = (PlayMakerFSM)Get(pair.Value, "Lod");
                var npc = (PlayMakerFSM)Get(pair.Value, "BuyerLod");
                rows.Add("buyer|" + pair.Key + "|" + PathOf(buyer.transform) + "|" + buyer.ActiveStateName + "|" + buyer.enabled
                    + "|" + buyer.gameObject.activeInHierarchy + "|" + payment.gameObject.activeInHierarchy + "|" + payment.ActiveStateName
                    + "|" + payment.FsmVariables.FindFsmFloat("Money").Value + "|" + payment.FsmVariables.FindFsmString("Value").Value
                    + "|" + job.ActiveStateName + "|" + job.enabled + "|" + npc.enabled + "|" + lod.ActiveStateName
                    + "|" + lod.FsmVariables.FindFsmFloat("Distance").Value + "|" + npc.FsmVariables.FindFsmFloat("Distance").Value);
                var state = Get(pair.Value, SessionManager.Instance!.IsHost ? "_current" : "_remote") as FirewoodBuyerState;
                int visible = 0;
                foreach (var renderer in buyer.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy) visible++;
                rows.Add("buyer-pose|" + pair.Key + "|" + Vector(npc.transform.position) + "|" + npc.transform.rotation.x + "," + npc.transform.rotation.y + "," + npc.transform.rotation.z + "," + npc.transform.rotation.w);
                rows.Add("buyer-view|" + pair.Key + "|" + visible + "|" + payment.FsmVariables.FindFsmGameObject("Bill").Value.activeInHierarchy
                    + "|" + (state != null ? state.Revision + "|" + state.Flags + "|" + state.Amount : "none"));
            }
            rows.Add("income|" + FsmVariables.GlobalVariables.FindFsmFloat("PlayerNetIncome").Value);
            return true;
        }
    }
}
