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
        private static bool ValveCommand(string[] args, List<string> rows)
        {
            if (!args[1].StartsWith("valve-", StringComparison.Ordinal)) return false;
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_VALVE_TEST") != "1")
                throw new InvalidOperationException("Valve interaction probe not enabled.");
            var sync = Get(WorldSyncManager.Instance!, "_fsm");
            var valves = (IDictionary)Get(sync, "_valves");
            if (args[1] == "valve-head" || args[1] == "valve-head-resync" || args[1] == "valve-head-remove")
            {
                PlayMakerFSM? head = null;
                foreach (DictionaryEntry pair in valves) { head = (PlayMakerFSM)Get(pair.Value, "Data"); break; }
                if (head == null) throw new InvalidOperationException("Head unavailable.");
                WinterMP.Net.Sync.PartIdentity.TryItemId(head.FsmVariables.FindFsmString("ID").Value, out uint id);
                if (args[1] == "valve-head-resync") Call(WorldSyncManager.Instance!, "RequestObjectState", id);
                if (args[1] == "valve-head-remove")
                {
                    if (!SessionManager.Instance!.IsHost || head.GetComponent<Rigidbody>() != null
                        || head.FsmVariables.FindFsmFloat("Tightness").Value >= 1)
                        throw new InvalidOperationException("Only an unbolted fitted host head can be removed.");
                    var hook = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FsmHook", true);
                    if (!(bool)hook.GetMethod("EnsureRemoteEntry", Members).Invoke(null, new object[] { head, "Remove" }))
                        throw new InvalidOperationException("Missing native head removal entry.");
                    hook.GetMethod("FireRemoteEntry", Members).Invoke(null, new object[] { head, "Remove" });
                }
                var items = Get(WorldSyncManager.Instance!, "_items");
                var body = head.GetComponent<Rigidbody>();
                rows.Add("head|" + id + "|" + head.FsmVariables.FindFsmInt("AssemblyID").Value
                    + "|" + Call(items, "CylinderHeadReady", head) + "|" + (body != null) + "|" + (body != null && body.isKinematic)
                    + "|" + ((IDictionary)Get(items, "_items")).Contains(id) + "|" + head.transform.position.x
                    + "|" + head.transform.position.y + "|" + head.transform.position.z + "|" + (head.transform.parent == null ? "none" : PathOf(head.transform.parent))
                    + "|" + head.enabled);
                foreach (var child in head.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (child.FsmName == "Data" && child != head)
                        rows.Add("head-child|" + child.gameObject.name + "|" + child.transform.position.x
                            + "|" + child.transform.position.y + "|" + child.transform.position.z);
            }
            else if (args[1] == "valve-fit")
            {
                if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fitting only.");
                var head = Find("CARPARTS/StartParts/Cylinder Head(VINX0)", "Data");
                var point = head.FsmVariables.FindFsmGameObject("InstallPoint").Value;
                PlayMakerFSM? mount = null;
                foreach (var fsm in point.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Data") mount = fsm;
                if (mount == null || mount.FsmVariables.FindFsmBool("Installed").Value) throw new InvalidOperationException("Head mount unavailable.");
                var body = head.GetComponent<Rigidbody>();
                if (body == null) throw new InvalidOperationException("Head not loose.");
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.position = point.transform.position;
                head.SendEvent("ASSEMBLING");
                rows.Add("mount|" + mount.ActiveStateName);
            }
            else if (args[1] == "valve-confirm")
            {
                if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fitting only.");
                var head = Find("CARPARTS/StartParts/Cylinder Head(VINX0)", "Data");
                var point = head.FsmVariables.FindFsmGameObject("InstallPoint").Value;
                foreach (var mount in point.GetComponents<PlayMakerFSM>())
                    if (mount.FsmName == "Data")
                    {
                        if (mount.ActiveStateName != "Near") throw new InvalidOperationException("Native head checks have not accepted fitting: " + mount.ActiveStateName);
                        mount.SendEvent("PROCEED"); rows.Add("mount|" + mount.ActiveStateName);
                    }
            }
            else if (args[1] != "valve-list")
            {
                uint id = uint.Parse(args[2], CultureInfo.InvariantCulture);
                if (!valves.Contains(id)) throw new InvalidOperationException("Unknown valve.");
                var valve = valves[id]; var fsm = (PlayMakerFSM)Get(valve, "Fsm");
                switch (args[1])
                {
                    case "valve-near":
                        if (Player == null) throw new InvalidOperationException("Player missing.");
                        Player.position = fsm.transform.position + new Vector3(0, 0, 1); break;
                    case "valve-tools":
                        var catalog = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                        var c = catalog.GetProperty("ReplacementParts", Members).GetValue(null, null);
                        string name = (string)c.GetType().GetProperty("Item").GetValue(c, new object[] { "replicaRepairVariable" });
                        var repair = FsmVariables.GlobalVariables.FindFsmBool(name) ?? throw new InvalidOperationException("Tool mode missing.");
                        repair.Value = true; fsm.SendEvent("REPAIRMODE_ON"); break;
                    case "valve-turn":
                        if (args[3] != "1" && args[3] != "-1") throw new InvalidOperationException("Invalid direction.");
                        fsm.SendEvent(args[3] == "1" ? "TIGHTEN" : "UNTIGHTEN"); break;
                    case "valve-resync": Call(WorldSyncManager.Instance!, "RequestObjectState", id); break;
                    default: throw new InvalidOperationException("Unknown valve command.");
                }
            }
            foreach (DictionaryEntry pair in valves)
            {
                var valve = pair.Value; var fsm = (PlayMakerFSM)Get(valve, "Fsm"); var data = (PlayMakerFSM)Get(valve, "Data");
                var settings = (IList)Call(sync, "ValveArray", valve); var gate = Get(valve, "Gate");
                rows.Add("valve|" + pair.Key + "|" + Get(valve, "Slot") + "|" + Get(valve, "Failed") + "|" + fsm.ActiveStateName
                    + "|" + settings[(int)Get(valve, "Slot")] + "|" + ((FsmFloat)Get(valve, "Setting")).Value
                    + "|" + ((SphereCollider)Get(valve, "Pick")).enabled + "|" + data.FsmVariables.FindFsmInt("AssemblyID").Value
                    + "|" + (gate == null ? "host" : gate.GetType().GetProperty("Seeded").GetValue(gate, null).ToString()));
            }
            return true;
        }
    }
}
