using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net.Transport;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool MilkCommand(string[] args, List<string> rows)
        {
            if (!args[1].StartsWith("milk-", StringComparison.Ordinal)) return false;
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_MILK_TEST") != "1")
                throw new InvalidOperationException("Milk probe is not enabled.");
            if (args[1].StartsWith("milk-fridge-", StringComparison.Ordinal))
            {
                FridgeCommand(args, rows);
                return true;
            }
            if (args[1] == "milk-disconnect" || args[1] == "milk-join")
            {
                if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest connection test only.");
                if (args[1] == "milk-disconnect") SessionManager.Instance.Shutdown("Milk reconnect test");
                else SessionManager.Instance.StartJoinLocal("127.0.0.1", UdpTransport.DefaultPort);
                return true;
            }
            var milks = new Dictionary<uint, PlayMakerFSM>();
            foreach (DictionaryEntry pair in (IDictionary)Get(Items, "_items"))
            {
                var body = Get(pair.Value, "Body") as Rigidbody;
                if (body == null || (body.name != "milk(itemx)" && body.name != "spoiled milk(itemx)")) continue;
                foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == "Use") milks.Add((uint)pair.Key, fsm);
            }
            if (args[1] != "milk-list")
            {
                uint id = uint.Parse(args[2]); var use = milks[id]; var body = use.GetComponent<Rigidbody>();
                switch (args[1])
                {
                    case "milk-set":
                        float value = float.Parse(args[3], CultureInfo.InvariantCulture);
                        if (value < 0 || value > 100 || float.IsNaN(value)) throw new InvalidOperationException("Invalid test condition.");
                        use.FsmVariables.FindFsmFloat("Condition").Value = value; break;
                    case "milk-step":
                        int count = int.Parse(args[3]);
                        if (count < 1 || count > 100) throw new InvalidOperationException("Invalid native tick count.");
                        for (int i = 0; i < count; i++) Enter(use, "Spoil 2"); break;
                    case "milk-drink": Enter(use, "Check drink"); break;
                    case "milk-resync": Call(WorldSyncManager.Instance!, "RequestObjectState", id); break;
                    case "milk-place":
                        if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host placement only.");
                        var areaRoot = FsmVariables.GlobalVariables.FindFsmGameObject("FridgePoint1").Value;
                        GameObject? area = null;
                        foreach (var component in areaRoot.GetComponents<Component>())
                        {
                            if (component.GetType().Name != "PlayMakerArrayListProxy") continue;
                            var type = component.GetType();
                            if ((string)type.GetField("referenceName").GetValue(component) != "Areas") continue;
                            var list = (IList)type.GetProperty("arrayList").GetValue(component, null);
                            foreach (var valueObject in list) if (valueObject is GameObject candidate && candidate != null) { area = candidate; break; }
                        }
                        if (area == null) throw new InvalidOperationException("No native chilling area.");
                        body.velocity = body.angularVelocity = Vector3.zero;
                        body.position = area.transform.position + (args[3] == "fridge" ? Vector3.zero : new Vector3(0, 0, 3));
                        rows.Add("chill-area|" + PathOf(area.transform));
                        // Run the real lookup while the carton is at the selected position.
                        Enter(use, "Spoil 2"); break;
                    default: throw new InvalidOperationException("Unknown milk command.");
                }
            }
            foreach (var pair in milks)
            {
                var use = pair.Value;
                rows.Add("milk|" + pair.Key + "|" + use.gameObject.name + "|" + use.ActiveStateName
                    + "|" + use.FsmVariables.FindFsmFloat("Condition").Value.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + use.FsmVariables.FindFsmFloat("Distance").Value.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + use.FsmVariables.FindFsmString("ID").Value);
            }
            return true;
        }

        private static void FridgeCommand(string[] args, List<string> rows)
        {
            bool house = args[2] == "house";
            if (!house && args[2] != "apartment") throw new InvalidOperationException("Unknown test fridge.");
            string root = house ? "YARD/Building/KITCHEN/Fridge" : "HOMENEW/Functions/Fridge";
            var chilling = Find(root + "/FridgePoint", "Chilling");
            var handle = Find(root + "/Pivot/Handle", "Use");
            var meter = Find("Systems/ElectricityBills" + (house ? "1" : "2"), "Data");
            var power = FsmVariables.GlobalVariables.FindFsmBool(house ? "HouseElectricity" : "HouseElectricity2");
            switch (args[1])
            {
                case "milk-fridge-read": break;
                case "milk-fridge-near": Player!.position = handle.transform.position + new Vector3(0, 0, 1); break;
                case "milk-fridge-door":
                    if (args[3] != "open" && args[3] != "close") throw new InvalidOperationException("Unknown door input.");
                    Enter(handle, args[3] == "open" ? "Open door" : "Close door"); break;
                case "milk-fridge-power":
                    if (!SessionManager.Instance!.IsHost && SessionManager.Instance.PlayerCount != 0)
                        throw new InvalidOperationException("Host or disconnected bill fixture only.");
                    if (args[3] != "cutoff" && args[3] != "restore") throw new InvalidOperationException("Unknown bill fixture.");
                    Enter(meter, args[3] == "cutoff" ? "Cut off" : "Pay bills"); break;
                case "milk-fridge-switch":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host switch fixture only.");
                    if (args[3] != "on" && args[3] != "off") throw new InvalidOperationException("Unknown switch fixture.");
                    meter.FsmVariables.FindFsmBool("MainSwitch").Value = args[3] == "on"; break;
                case "milk-fridge-place":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host placement only.");
                    if (args[4] != "fridge" && args[4] != "warm") throw new InvalidOperationException("Unknown placement.");
                    var item = ((IDictionary)Get(Items, "_items"))[uint.Parse(args[3])];
                    var body = (Rigidbody)Get(item, "Body");
                    if (body.name != "milk(itemx)") throw new InvalidOperationException("Fresh milk fixture required.");
                    body.velocity = body.angularVelocity = Vector3.zero;
                    body.constraints = RigidbodyConstraints.FreezeAll;
                    // The interior stays fixed when the native cooling area moves away.
                    body.position = chilling.transform.position + (args[4] == "warm" ? new Vector3(0, 0, 3) : Vector3.zero);
                    body.transform.position = body.position;
                    rows.Add("fridge-milk-position|" + args[3] + "|" + Vector(body.position)); break;
                default: throw new InvalidOperationException("Unknown fridge command.");
            }
            var area = chilling.FsmVariables.FindFsmGameObject("ChillArea").Value;
            rows.Add("fridge|" + args[2] + "|" + power.Value + "|" + meter.FsmVariables.FindFsmBool("MainSwitch").Value
                + "|" + meter.ActiveStateName + "|" + meter.enabled + "|" + handle.FsmVariables.FindFsmBool("DoorOpen").Value
                + "|" + chilling.ActiveStateName + "|" + chilling.FsmVariables.FindFsmBool("Kitchen").Value
                + "|" + Vector(area.transform.localPosition) + "|" + Vector(chilling.transform.position)
                + "|" + meter.FsmVariables.FindFsmFloat("UnpaidBills").Value.ToString("R", CultureInfo.InvariantCulture)
                + "|" + meter.FsmVariables.FindFsmGameObject("Bill").Value.activeSelf
                + "|" + Find(house ? "YARD/Building/Dynamics/HouseElectricity" : "HOMENEW/Functions/ElectricThings/HouseElectricity", "Status")
                    .transform.Find("ElectricAppliances").gameObject.activeInHierarchy);
        }
    }
}
