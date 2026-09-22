using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static BulbState? _cachedBulb;
        private static bool BulbCommand(string[] args, List<string> rows)
        {
            if (!SupplyProbe || !args[1].StartsWith("bulb-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            var session = SessionManager.Instance!;
            if (args[1] == "bulb-view") return true;
            if (args[1] == "bulb-replay")
            { if (!session.IsHost || _cachedBulb == null) throw new InvalidOperationException("Missing host bulb cache."); session.SendWorldMessage(_cachedBulb, Channel.ReliableOrdered); return true; }
            uint id = uint.Parse(args[2], CultureInfo.InvariantCulture);
            var b = ((IDictionary)Get(Items, "_bulbs"))[id] ?? throw new InvalidOperationException("Bulb not found.");
            var body = (Rigidbody)Get(b, "Body"); var data = (PlayMakerFSM)Get(b, "Data");
            switch (args[1])
            {
                case "bulb-wear":
                    if (!session.IsHost) throw new InvalidOperationException("Host condition only.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = float.Parse(args[3], CultureInfo.InvariantCulture); return true;
                case "bulb-cache":
                    if (!session.IsHost) throw new InvalidOperationException("Host cache only.");
                    _cachedBulb = (BulbState)Call(Items, "BuildBulbState", id); return true;
                case "bulb-destroy":
                    if (!session.IsHost) throw new InvalidOperationException("Host retirement only.");
                    UnityEngine.Object.Destroy(body.gameObject); return true;
                default: throw new InvalidOperationException("Unknown bulb command.");
            }
        }
        private static void BulbSnapshot(List<string> rows)
        {
            var f = Get(Items, "_bulbFactory");
            if (f != null)
            {
                var fsm = (PlayMakerFSM)Get(f, "Fsm");
                rows.Add("bulb-factory|" + Get(f, "Failed") + "|" + fsm.enabled + "|" + fsm.ActiveStateName);
            }
            foreach (DictionaryEntry entry in (IDictionary)Get(Items, "_bulbs"))
            {
                var b = entry.Value; var body = Get(b, "Body") as Rigidbody; var data = Get(b, "Data") as PlayMakerFSM;
                rows.Add("bulb|" + entry.Key + "|" + Get(b, "Replica") + "|" + (body != null) + "|"
                    + (data != null ? data.FsmVariables.FindFsmFloat("Wear").Value.ToString("R", CultureInfo.InvariantCulture) : "none") + "|"
                    + (body != null ? Vector(body.position) : "none") + "|" + (data != null ? data.ActiveStateName : "none"));
            }
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var data = obj as PlayMakerFSM;
                if (data == null || data.FsmName != "Data" || data.gameObject.name != "car light bulb(itemx)") continue;
                rows.Add("bulb-native|" + data.gameObject.GetInstanceID() + "|" + data.gameObject.activeInHierarchy + "|"
                    + data.enabled + "|" + data.ActiveStateName + "|" + data.FsmVariables.FindFsmFloat("Wear").Value.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}
