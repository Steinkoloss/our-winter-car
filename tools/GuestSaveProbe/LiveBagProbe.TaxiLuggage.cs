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
        private static readonly string[] LuggageNames = { "Suitcase1", "Suitcase2", "Suitcase3", "Beercase1", "Mattress1" };
        private static PlayMakerFSM TaxiCases
        {
            get { foreach (var f in TaxiWalker.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Suitcases") return f;
                throw new InvalidOperationException("Native luggage FSM missing."); }
        }
        private static Rigidbody LuggageBody(int index) => TaxiCases.FsmVariables.FindFsmGameObject(LuggageNames[index]).Value.GetComponent<Rigidbody>();
        private static IList LuggageList(string name)
        {
            foreach (var c in TaxiWalker.GetComponents<MonoBehaviour>())
                if (c != null && c.GetType().Name == "PlayMakerArrayListProxy" && (string)Get(c, "referenceName") == name)
                    return (IList)c.GetType().GetProperty("arrayList").GetValue(c, null);
            throw new InvalidOperationException("Native luggage list missing.");
        }
        private static bool TaxiLuggageCommand(string[] args)
        {
            if (!args[1].StartsWith("taxi-luggage-", StringComparison.Ordinal)) return false;
            var session = SessionManager.Instance!;
            int slot = 0; if (args.Length > 2) int.TryParse(args[2], out slot);
            switch (args[1])
            {
                case "taxi-luggage-prepare":
                    if (!session.IsHost) throw new InvalidOperationException("Host fixture only.");
                    var customer = TaxiWalker.FsmVariables.FindFsmGameObject("Parent").Value;
                    customer.SetActive(true); TaxiWalker.enabled = false;
                    foreach (var f in customer.GetComponents<PlayMakerFSM>()) f.enabled = false;
                    customer.transform.position = TaxiCar.transform.position + TaxiCar.transform.right * 5;
                    return true;
                case "taxi-luggage-reset":
                    if (!session.IsHost || slot < 0 || slot > 6) throw new InvalidOperationException("Safe native luggage roll required.");
                    var amounts = LuggageList("Amounts"); var saved = new ArrayList(amounts);
                    try { amounts.Clear(); amounts.Add(slot); TaxiCases.SendEvent("RESET"); }
                    finally { amounts.Clear(); foreach (var value in saved) amounts.Add(value); }
                    return true;
                case "taxi-luggage-near":
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    Player.position = LuggageBody(slot).position + Vector3.up * .5f + TaxiCar.transform.right * 1.5f; return true;
                case "taxi-luggage-pickup":
                    var pick = Get(Items, "_bagPickup") as PlayMakerFSM ?? Find(Hand, "PickUp");
                    pick.FsmVariables.FindFsmGameObject("PickedObject").Value = LuggageBody(slot).gameObject;
                    Enter(pick, "Set pivot 2"); return true;
                case "taxi-luggage-drop":
                    ((PlayMakerFSM)Get(Items, "_bagPickup")).SendEvent("DROP_PART"); return true;
                case "taxi-luggage-place":
                    var body = LuggageBody(slot);
                    body.position = body.transform.position = TaxiCar.transform.TransformPoint(new Vector3(float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture)));
                    body.velocity = body.angularVelocity = Vector3.zero; return true;
                case "taxi-luggage-distances":
                    if (!session.IsHost) throw new InvalidOperationException("Host distances fixture only.");
                    TaxiCases.SendEvent("DISTANCES"); return true;
                case "taxi-luggage-move-loaded-car":
                    if (session.IsHost || TaxiDrive.ActiveStateName != "Player in car" || Player == null)
                        throw new InvalidOperationException("Guest driving fixture required.");
                    var vehicle = TaxiCar.GetComponent<Rigidbody>(); var luggage = LuggageBody(slot);
                    var offset = TaxiCar.transform.forward * 5;
                    var playerPosition = Player.position + offset;
                    luggage.transform.position += offset; luggage.position = luggage.transform.position;
                    vehicle.transform.position += offset; vehicle.position = vehicle.transform.position;
                    Player.position = playerPosition; vehicle.velocity = vehicle.angularVelocity = Vector3.zero;
                    luggage.velocity = luggage.angularVelocity = Vector3.zero; return true;
                case "taxi-luggage-retire":
                    session.SendWorldMessage(new ItemDespawn { ItemId = uint.Parse(args[2]) }, Channel.ReliableOrdered); return true;
                case "taxi-luggage-old-pose":
                    session.SendWorldMessage(new ItemTransform { ItemId = uint.Parse(args[2]), OwnerPlayerId = session.LocalPlayerId,
                        Sequence = 30000, Position = new NetVector3(0, 50, 0), Rotation = NetQuaternion.Identity }, Channel.ReliableOrdered); return true;
                default: throw new InvalidOperationException("Unknown luggage fixture.");
            }
        }
        private static void TaxiLuggageSnapshot(List<string> rows)
        {
            var cases = TaxiCases;
            rows.Add("luggage-native|" + cases.enabled + "|" + cases.ActiveStateName + "|" + LuggageList("Luggage").Count
                + "|" + TaxiWalker.FsmVariables.FindFsmFloat("MaxDelay").Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            rows.Add("luggage-mask|" + Get(Items, "_taxiLuggageMask"));
            var entries = (Array)Get(Items, "_taxiLuggage");
            for (int i = 0; i < 5; i++)
            {
                var body = LuggageBody(i); var item = entries.GetValue(i);
                rows.Add("luggage-physics-" + i + "|" + body.collisionDetectionMode + "|" + body.maxDepenetrationVelocity.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                rows.Add("luggage-" + i + "|" + body.gameObject.activeInHierarchy + "|" + body.isKinematic + "|" + Vector(body.position)
                    + "|" + (body.transform.parent == null ? "world" : PathOf(body.transform.parent)) + "|" + body.tag
                    + "|" + (item == null ? "none" : Get(item, "Id") + "|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner")
                        + "|" + Get(item, "LocalCargoVehicleId") + "|" + Get(item, "RemoteCargoVehicleId")));
            }
        }
    }
}
