using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool SupplyProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_SUPPLY_TEST") == "1";
        private static SupplyItemState? _cachedSupply;
        private static object SupplyItem(string id) => ((IDictionary)Get(Items, "_items"))[uint.Parse(id, CultureInfo.InvariantCulture)]
            ?? throw new InvalidOperationException("Supply fixture item is missing.");
        private static bool SupplyCommand(string[] args, List<string> rows)
        {
            if (!SupplyProbe || !args[1].StartsWith("supply-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            var session = SessionManager.Instance!;
            if (args[1] == "supply-view") return true;
            if (args[1] == "supply-scan") { Call(Items, "ScanItems"); return true; }
            if (args[1] == "supply-replay")
            { if (!session.IsHost || _cachedSupply == null) throw new InvalidOperationException("Missing host cache."); session.SendWorldMessage(_cachedSupply, Channel.ReliableOrdered); return true; }
            if (args[1] == "supply-request")
            {
                if (session.IsHost) throw new InvalidOperationException("Guest request only.");
                session.SendWorldMessage(new PackageOpenRequest { PlayerId = session.LocalPlayerId,
                    ItemId = uint.Parse(args[2]), ExpectedRevision = uint.Parse(args[3]), Sequence = uint.Parse(args[4]), Token = ulong.Parse(args[5]) }, Channel.ReliableOrdered);
                return true;
            }
            if (args[1] == "supply-native")
            {
                if (!session.IsHost && session.State != SessionState.Idle) throw new InvalidOperationException("Native fixture requires host or disconnected guest.");
                var factory = Find("Spawner/CreateItems", args[2]);
                if (args[2] != "Fuse" && args[2] != "FusePackage" && args[2] != "Sparkplugs" && args[2] != "LightbulbBox" && args[2] != "Lightbulb" && args[2] != "R20Battery" && args[2] != "R20BatteryBox") throw new InvalidOperationException("Factory outside supply scope.");
                if (args[2] == "Fuse" || args[2] == "Lightbulb" || args[2] == "R20Battery") factory.FsmVariables.FindFsmGameObject("SpawnPoint").Value = Player!.gameObject;
                else FsmVariables.GlobalVariables.FindFsmGameObject("ShoppingBagSpawn").Value = Player!.gameObject;
                Enter(factory, "Create product"); return true;
            }
            uint id = uint.Parse(args[2], CultureInfo.InvariantCulture);
            var body = (Rigidbody)Get(SupplyItem(args[2]), "Body");
            switch (args[1])
            {
                case "supply-near": Player!.position = body.position + new Vector3(0, 0, 1); return true;
                case "supply-open":
                    var box = ((IDictionary)Get(Items, "_packages"))[id] ?? throw new InvalidOperationException("Box missing.");
                    var rule = Get(Get(box, "Factory"), "Rule");
                    Enter((PlayMakerFSM)Get(box, "Use"), (string)Get(rule, "OpenState")); return true;
                case "supply-pick":
                    var pickup = Find(Hand, "PickUp"); pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = body.gameObject;
                    Enter(pickup, "Set pivot 2"); return true;
                case "supply-garbage": body.GetComponent<PlayMakerFSM>().SendEvent("GARBAGE"); return true;
                case "supply-cache":
                    if (!session.IsHost) throw new InvalidOperationException("Host cache only.");
                    _cachedSupply = Call(Items, "BuildSupplyState", id) as SupplyItemState ?? throw new InvalidOperationException("Supply snapshot missing."); return true;
                default: throw new InvalidOperationException("Unknown supply command.");
            }
        }

        private static void SupplySnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            rows.Add("player|" + (Player != null ? Vector(Player.position) : "none"));
            var pickup = Find(Hand, "PickUp"); var joint = pickup.GetComponent<FixedJoint>();
            rows.Add("supply-hand|" + pickup.ActiveStateName + "|" + (joint != null && joint.connectedBody != null
                ? joint.connectedBody.GetComponent<PlayMakerFSM>()?.FsmVariables.FindFsmString("ID")?.Value : "none"));
            rows.Add("cash|" + FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney").Value.ToString("R", CultureInfo.InvariantCulture));
            foreach (string table in new[] { "_packageFactories", "_supplyFactories" })
                foreach (DictionaryEntry entry in (IDictionary)Get(Items, table))
                {
                    var factory = entry.Value; var fsm = (PlayMakerFSM)Get(factory, "Fsm");
                    if (fsm.FsmName != "Fuse" && fsm.FsmName != "FusePackage" && fsm.FsmName != "Sparkplugs" && fsm.FsmName != "LightbulbBox" && fsm.FsmName != "R20Battery" && fsm.FsmName != "R20BatteryBox") continue;
                    rows.Add("supply-factory|" + fsm.FsmName + "|" + Get(factory, "Failed") + "|" + fsm.ActiveStateName
                        + "|" + fsm.FsmVariables.FindFsmInt("ObjectNumberInt")?.Value + "|" + fsm.Fsm.Started);
                }
            foreach (string table in new[] { "_packages", "_supplies" })
                foreach (DictionaryEntry entry in (IDictionary)Get(Items, table))
                {
                    var binding = entry.Value; var body = Get(binding, "Body") as Rigidbody; var use = Get(binding, "Use") as PlayMakerFSM;
                    string nativeId = (string)Get(binding, "NativeId");
                    if (!nativeId.StartsWith("fuse", StringComparison.Ordinal) && !nativeId.StartsWith("sparkplugbox", StringComparison.Ordinal) && !nativeId.StartsWith("lightbulbbox", StringComparison.Ordinal) && !nativeId.StartsWith("r20battery", StringComparison.Ordinal)) continue;
                    var state = table == "_packages" && SessionManager.Instance!.IsHost ? Call(Items, "BuildPackageState", (uint)entry.Key) as PackageState : null;
                    rows.Add("supply-item|" + entry.Key + "|" + nativeId + "|" + Get(binding, "Replica") + "|"
                        + (use != null ? use.FsmVariables.FindFsmInt("Quantity")?.Value.ToString() ?? "-" : "missing") + "|"
                        + (body != null ? Vector(body.position) : "none") + "|" + (body != null && body.gameObject.activeInHierarchy)
                        + "|" + (use != null ? use.ActiveStateName : "none") + "|" + state?.Revision);
                }
            foreach (DictionaryEntry entry in Bags)
            {
                var body = Get(entry.Value, "Body") as Rigidbody;
                rows.Add("supply-bag|" + entry.Key + "|" + Get(entry.Value, "NativeId") + "|" + (body != null));
            }
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var use = obj as PlayMakerFSM;
                if (use == null || use.FsmName != "Use" || !use.Fsm.Started) continue;
                string nativeId = use.FsmVariables.FindFsmString("ID")?.Value ?? string.Empty;
                if (!nativeId.StartsWith("fuse0", StringComparison.Ordinal) && !nativeId.StartsWith("fusepackage0", StringComparison.Ordinal) && !nativeId.StartsWith("lightbulbbox", StringComparison.Ordinal) && !nativeId.StartsWith("r20battery", StringComparison.Ordinal)) continue;
                rows.Add("supply-native|" + nativeId + "|" + use.gameObject.activeInHierarchy + "|" + use.enabled + "|"
                    + use.ActiveStateName + "|" + (use.GetComponent<Rigidbody>() != null) + "|" + use.FsmVariables.FindFsmInt("Quantity")?.Value);
            }
            var opening = Get(Items, "_packageOpening"); rows.Add("supply-opening|" + (opening == null ? "none" : Get(opening, "ExpectedNativeId")));
        }
    }
}
