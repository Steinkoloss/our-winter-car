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
        private static bool HouseholdFuseProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_HOUSEHOLD_FUSE_TEST") == "1";
        private static readonly int[] _householdShockCount = new int[2];
        private static bool _householdSafe;
        private static float _householdNeedsAt;
        private static readonly GameObject?[] _householdOriginals = new GameObject?[11];
        private static readonly GameObject?[] _householdReplicas = new GameObject?[11];
        private static object FuseHolder(int index) => ((Array)Get(Items, "_fuseHolders")).GetValue(index);
        private static object FuseTable(int home) => ((Array)Get(Items, "_fuseTables")).GetValue(home);
        private static GameObject FuseObject(int index) => (GameObject)Get(FuseHolder(index), "Object");
        private static void TickHouseholdFuses()
        {
            if (!HouseholdFuseProbe || Application.loadedLevelName != "GAME" || Time.unscaledTime < _householdNeedsAt) return;
            RequirePersistenceSandbox(); _householdNeedsAt = Time.unscaledTime + 5;
            foreach (string name in new[] { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerUrine", "PlayerTemp" })
            { var v = FsmVariables.GlobalVariables.FindFsmFloat(name); if (v != null) v.Value = name == "PlayerTemp" ? 50 : 0; }
        }
        private static bool HouseholdFuseCommand(string[] args, List<string> rows)
        {
            if (!HouseholdFuseProbe || !args[1].StartsWith("fuse-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            if (args[1] == "fuse-view") return true;
            if (args[1] == "fuse-safe")
            {
                if (_householdSafe) return true; _householdSafe = true;
                for (int i = 0; i < 2; i++)
                {
                    int home = i; var shock = (PlayMakerFSM)Get(FuseTable(i), "Shock");
                    foreach (var a in shock.Fsm.GetState("State 2").Actions)
                        if (a.GetType().Name == "SendRandomEvent")
                        {
                            var events = (FsmEvent[])a.GetType().GetField("events").GetValue(a); var weights = (FsmFloat[])a.GetType().GetField("weights").GetValue(a);
                            for (int j = 0; j < events.Length; j++) weights[j].Value = events[j].Name == "DIE" ? 0 : 1;
                        }
                    Hook.GetMethod("OnStateEnter", Members, null, new[] { typeof(PlayMakerFSM), typeof(string), typeof(Action) }, null)
                        .Invoke(null, new object[] { shock, "State 2", (Action)(() => _householdShockCount[home]++) });
                }
                return true;
            }
            int index = int.Parse(args[2], CultureInfo.InvariantCulture);
            if (args[1] == "fuse-slot-near")
            {
                var table = FuseTable(index < 7 ? 0 : 1); var slots = (PlayMakerFSM[])Get(table, "Slots");
                var pos = slots[index - (int)Get(table, "Offset")].transform.position;
                Player!.position = pos + new Vector3(0, -1, 1); return true;
            }
            var holder = FuseHolder(index); var obj = (GameObject)Get(holder, "Object");
            switch (args[1])
            {
                case "fuse-supply":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host supply fixture only.");
                    var factory = Find("Spawner/CreateItems", "Fuse"); var spawn = factory.FsmVariables.FindFsmGameObject("SpawnPoint");
                    var prior = spawn.Value; var marker = new GameObject("HouseholdFuseTestSpawn");
                    marker.transform.position = obj.transform.position + new Vector3(0, .25f, .25f);
                    try { spawn.Value = marker; Enter(factory, "Create product"); }
                    finally { spawn.Value = prior; UnityEngine.Object.Destroy(marker); }
                    return true;
                case "fuse-near":
                    var state = Get(holder, "Last") as HouseholdFuseHolder;
                    Player!.position = obj.transform.position + new Vector3(0, state?.Slot == 255 ? 0 : -1, 1); return true;
                case "fuse-turn": Enter((PlayMakerFSM)Get(holder, "Screw"), args[3] == "up" ? "Screw" : "Unscrew"); return true;
                case "fuse-remove": Enter((PlayMakerFSM)Get(holder, "Removal"), "Remove part"); return true;
                case "fuse-blow":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host native blowout fixture only.");
                    ((PlayMakerFSM)Get(holder, "Use")).SendEvent("BLOWFUSE"); return true;
                case "fuse-pick":
                    var pickup = Find(Hand, "PickUp"); pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = obj;
                    Enter(pickup, "Check if Item"); return true;
                case "fuse-aim":
                    if (Find(Hand, "PickUp").FsmVariables.FindFsmGameObject("PickedObject").Value != obj)
                        throw new InvalidOperationException("Aim fixture requires the actual held holder.");
                    int aimSlot = int.Parse(args[3]); var aimTable = FuseTable(aimSlot < 7 ? 0 : 1);
                    var aim = ((PlayMakerFSM[])Get(aimTable, "Slots"))[aimSlot - (int)Get(aimTable, "Offset")].transform.position;
                    var hand = Find(Hand, "PickUp"); hand.transform.position = aim;
                    hand.FsmVariables.FindFsmGameObject("ItemPivot").Value.transform.position = aim;
                    obj.transform.position = aim; obj.GetComponent<Rigidbody>().position = aim; return true;
                case "fuse-insert":
                    var body = (Rigidbody)Get(SupplyItem(args[3]), "Body"); var insert = (PlayMakerFSM)Get(holder, "Insert");
                    insert.FsmVariables.FindFsmGameObject("Part").Value = body.gameObject; Enter(insert, "Wait"); return true;
                case "fuse-fit":
                    int target = int.Parse(args[3]); var table = FuseTable(target < 7 ? 0 : 1);
                    var slot = ((PlayMakerFSM[])Get(table, "Slots"))[target - (int)Get(table, "Offset")];
                    slot.FsmVariables.FindFsmGameObject("Part").Value = obj; Enter(slot, "Wait"); return true;
                case "fuse-intent":
                    SessionManager.Instance!.SendWorldMessage(new HouseholdFuseIntent { PlayerId = byte.Parse(args[3]), Holder = (byte)index,
                        Action = (HouseholdFuseAction)byte.Parse(args[4]), Sequence = uint.Parse(args[5]), ControlRevision = uint.Parse(args[6]),
                        Slot = byte.Parse(args[7]), ItemId = uint.Parse(args[8]) }, Channel.ReliableOrdered); return true;
                default: throw new InvalidOperationException("Unknown household fuse command.");
            }
        }
        private static void HouseholdFuseSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            var holders = Get(Items, "_fuseHolders") as Array;
            rows.Add("fuse-binding|" + (holders != null) + "|" + Get(Items, "_fuseFailed"));
            rows.Add("fuse-shock|" + _householdShockCount[0] + "|" + _householdShockCount[1]);
            rows.Add("fuse-pending|" + ((IDictionary)Get(Items, "_pendingFuseIntents")).Count);
            rows.Add("fuse-sequence|" + Get(Items, "_fuseIntentSequence"));
            var tracked = (IDictionary)Get(Items, "_items");
            foreach (DictionaryEntry supply in (IDictionary)Get(Items, "_supplies"))
                if (tracked.Contains(supply.Key))
                {
                    var item = tracked[supply.Key];
                    rows.Add("fuse-supply-owner|" + supply.Key + "|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner"));
                }
            if (holders != null)
                for (int i = 0; i < 11; i++)
                { _householdOriginals[i] = (GameObject)Get(FuseHolder(i), "Original"); _householdReplicas[i] = FuseObject(i); }
            for (int i = 0; i < 11; i++)
                if (_householdOriginals[i] != null)
                {
                    var original = _householdOriginals[i]!; var native = FuseFsmForProbe(original);
                    rows.Add("fuse-local|" + i + "|" + original.activeSelf + "|" + native.enabled + "|" + native.FsmVariables.FindFsmInt("FuseState").Value
                        + "|" + native.FsmVariables.FindFsmFloat("Tightness").Value + "|" + (_householdReplicas[i] != null));
                }
            if (holders == null) return;
            var picked = Find(Hand, "PickUp").FsmVariables.FindFsmGameObject("PickedObject").Value;
            int held = -1;
            for (int i = 0; i < 11; i++) if (picked == FuseObject(i)) held = i;
            rows.Add("fuse-hand|" + held);
            for (int i = 0; i < 2; i++)
            {
                var table = FuseTable(i); var power = (IList)Get(table, "Fuses"); var bits = new List<string>();
                foreach (var bit in power) bits.Add(bit.ToString()); rows.Add("fuse-power|" + i + "|" + string.Join(",", bits.ToArray()));
                var slots = (PlayMakerFSM[])Get(table, "Slots");
                for (int j = 0; j < slots.Length; j++) rows.Add("fuse-slot|" + ((int)Get(table, "Offset") + j) + "|" + slots[j].gameObject.activeInHierarchy + "|" + slots[j].ActiveStateName);
            }
            for (int i = 0; i < 11; i++)
            {
                var h = FuseHolder(i); var use = (PlayMakerFSM)Get(h, "Use"); var obj = (GameObject)Get(h, "Object"); var body = obj.GetComponent<Rigidbody>();
                var item = Get(h, "Item"); var last = Get(h, "Last") as HouseholdFuseHolder;
                rows.Add("fuse-holder|" + i + "|" + use.FsmVariables.FindFsmInt("FuseState").Value + "|" + use.FsmVariables.FindFsmFloat("Tightness").Value
                    + "|" + last?.Slot + "|" + Get(h, "ControlRevision") + "|" + Vector(obj.transform.position) + "|" + (obj.transform.parent == null ? "root" : PathOf(obj.transform.parent))
                    + "|" + obj.activeInHierarchy + "|" + (body != null) + "|" + (item == null ? "none" : Get(item, "Id") + "," + Get(item, "LocallyOwned") + "," + Get(item, "RemoteOwner"))
                    + "|" + ((PlayMakerFSM)Get(h, "Screw")).ActiveStateName + "|" + ((PlayMakerFSM)Get(h, "Removal")).ActiveStateName
                    + "|" + ((PlayMakerFSM)Get(h, "Insert")).gameObject.activeInHierarchy);
                var original = (GameObject)Get(h, "Original"); var native = FuseFsmForProbe(original);
                rows.Add("fuse-original|" + i + "|" + original.activeSelf + "|" + native.enabled + "|" + native.FsmVariables.FindFsmInt("FuseState").Value + "|" + native.FsmVariables.FindFsmFloat("Tightness").Value);
            }
        }
        private static PlayMakerFSM FuseFsmForProbe(GameObject obj)
        { foreach (var f in obj.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Use") return f; throw new InvalidOperationException("Missing holder Use."); }
    }
}
