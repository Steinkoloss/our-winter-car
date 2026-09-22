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
        private static bool WoodDeliveryProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_WOOD_DELIVERY_TEST") == "1";
        private const string WoodLoadPath = "FLATBED/Bed/LogTrigger";
        private static bool WoodDeliveryCommand(string[] args)
        {
            if (!WoodDeliveryProbe || !args[1].StartsWith("delivery-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            var load = Find(WoodLoadPath, "Logic");
            switch (args[1])
            {
                case "delivery-load":
                    // Seed the disposable trailer's existing load; the test then runs
                    // native unloading, customer calculation and payment end to end.
                    load.FsmVariables.FindFsmFloat("Logs").Value = float.Parse(args[2], CultureInfo.InvariantCulture);
                    load.FsmVariables.FindFsmFloat("Firewood").Value = float.Parse(args[3], CultureInfo.InvariantCulture);
                    Enter(load, "Add scale"); return true;
                case "delivery-stage":
                    var job = Find("JOBS/HouseWood" + int.Parse(args[2]) + "/WoodJob" + int.Parse(args[2]) + "Point", "Logic");
                    var root = load.transform.root;
                    foreach (var body in root.GetComponentsInChildren<Rigidbody>())
                    { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
                    var check = load.FsmVariables.FindFsmGameObject("Check").Value.transform;
                    root.position += job.transform.position + new Vector3(0, 2, 0) - check.position;
                    return true;
                case "delivery-unload":
                    load.FsmVariables.FindFsmBool("Unload").Value = bool.Parse(args[2]); return true;
                case "delivery-request":
                    SessionManager.Instance!.SendWorldMessage(new FirewoodUnloadIntent { PlayerId = SessionManager.Instance.LocalPlayerId,
                        Epoch = uint.Parse(args[2]), Sequence = uint.Parse(args[3]), Unload = bool.Parse(args[4]) }, Channel.ReliableOrdered); return true;
                case "delivery-hatch":
                    Enter(Find("FLATBED/Bed/HatchPivot/flatbed_hatch", "Use"), bool.Parse(args[2]) ? "Open door" : "Close door"); return true;
                case "delivery-tilt":
                    var tilt = load.transform.parent.localEulerAngles; tilt.x = float.Parse(args[2], CultureInfo.InvariantCulture);
                    load.transform.parent.localEulerAngles = tilt; return true;
                case "delivery-penalty":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    Find("JOBS/HouseWood" + int.Parse(args[2]) + "/WoodJob" + int.Parse(args[2]) + "Point", "Logic")
                        .FsmVariables.FindFsmFloat("Penalty").Value = float.Parse(args[3], CultureInfo.InvariantCulture);
                    return true;
                default: throw new InvalidOperationException("Unknown wood delivery command.");
            }
        }

        private static void WoodDeliverySnapshot(List<string> rows)
        {
            RequirePersistenceSandbox();
            if (Application.loadedLevelName != "GAME") return;
            foreach (var peer in SessionManager.Instance!.Players)
                rows.Add("delivery-peer|" + peer.PlayerId + "|" + peer.IsDead + "|"
                    + (Time.unscaledTime - peer.LastTransformTime).ToString("R", CultureInfo.InvariantCulture) + "|" + Vector(peer.Position));
            var load = Find(WoodLoadPath, "Logic");
            var sync = Get(WorldSyncManager.Instance!, "_woodDelivery");
            var state = Get(sync, SessionManager.Instance!.IsHost ? "_current" : "_remote") as FirewoodLoadState;
            rows.Add("delivery-sync|" + Get(sync, "_disabled") + "|" + (state == null ? "none" : state.Revision + "|" + state.Epoch + "|" + state.Piles.Length));
            int liveIndex = 0;
            foreach (var obj in (IEnumerable<GameObject>)Get(sync, "_piles"))
            {
                if (obj == null) continue;
                rows.Add("delivery-geometry|" + liveIndex++ + "|" + Vector(obj.transform.position) + "|"
                    + obj.transform.Find("mesh").localScale.z.ToString("R", CultureInfo.InvariantCulture) + "|" + obj.activeInHierarchy);
            }
            if (state != null) for (int i = 0; i < state.Piles.Length; i++)
            {
                var pile = state.Piles[i];
                rows.Add("delivery-pile|" + i + "|" + pile.Position.X + "," + pile.Position.Y + "," + pile.Position.Z + "|" + pile.Scale.ToString("R", CultureInfo.InvariantCulture));
            }
            rows.Add("delivery|" + load.ActiveStateName + "|" + load.enabled + "|" + load.FsmVariables.FindFsmBool("Unload").Value);
            foreach (var v in load.FsmVariables.FloatVariables)
                rows.Add("delivery-float|" + v.Name + "|" + v.Value.ToString("R", CultureInfo.InvariantCulture));
            foreach (string name in new[] { "Check", "Closest", "Customer", "LogpileGround", "LogpileGroundMesh", "LogpileBed" })
            {
                var obj = load.FsmVariables.FindFsmGameObject(name).Value;
                rows.Add("delivery-object|" + name + "|" + (obj == null ? "none" : Identity(obj) + "|" + Vector(obj.transform.position) + "|" + Vector(obj.transform.localScale)));
            }
            var body = load.transform.parent.GetComponent<Rigidbody>();
            rows.Add("delivery-body|" + body.mass.ToString("R", CultureInfo.InvariantCulture) + "|" + Vector(body.position));
            for (int house = 1; house <= 4; house++)
            {
                var job = Find("JOBS/HouseWood" + house + "/WoodJob" + house + "Point", "Logic");
                rows.Add("delivery-site|" + house + "|" + job.ActiveStateName + "|" + job.enabled + "|"
                    + job.FsmVariables.FindFsmFloat("Surplus").Value.ToString("R", CultureInfo.InvariantCulture) + "|"
                    + job.FsmVariables.FindFsmFloat("Penalty").Value.ToString("R", CultureInfo.InvariantCulture) + "|" + job.FsmVariables.FindFsmBool("Order").Value);
            }
            rows.Add("cash|" + FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney").Value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
