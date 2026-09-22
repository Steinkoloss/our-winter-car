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
        private static PlayMakerFSM TaxiTerminal => Find(TaxiRoot + "/TaxiFunctions/PaymentTerminal/Payment", "Use");
        private static PlayMakerFSM TaxiCash
        {
            get
            {
                foreach (var f in TaxiWalker.FsmVariables.FindFsmGameObject("Pay").Value.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Use") return f;
                throw new InvalidOperationException("Missing native taxi cash.");
            }
        }
        private static bool TaxiFareCommand(string[] args)
        {
            if (!args[1].StartsWith("taxi-fare-", StringComparison.Ordinal)) return false;
            switch (args[1])
            {
                case "taxi-fare-charge": Enter(TaxiTerminal, "Make payment"); return true;
                case "taxi-fare-collect":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Do not award host achievements in the disposable fixture.");
                    Enter(TaxiCash, "State 1"); return true;
                case "taxi-fare-print":
                    Enter(TaxiTerminal, "State 6"); return true;
                case "taxi-fare-take-receipt":
                    Enter(TaxiTerminal, "Drop ticket"); return true;
                case "taxi-fare-receipt-request":
                    if (!SessionManager.Instance!.IsHost || !TaxiWalker.FsmVariables.FindFsmBool("Paid").Value) throw new InvalidOperationException("Paid host customer fixture only.");
                    Enter(TaxiWalker, "State 1"); return true;
                case "taxi-fare-receipt-near":
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    Player.position = (args[2] == "paper" ? TaxiTerminal.FsmVariables.FindFsmGameObject("TicketPhysical").Value
                        : TaxiWalker.FsmVariables.FindFsmGameObject("Receiptrigger").Value).transform.position + Vector3.up * .4f; return true;
                case "taxi-fare-receipt-pickup":
                    var pickup = Find(Hand, "PickUp");
                    pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = TaxiTerminal.FsmVariables.FindFsmGameObject("TicketPhysical").Value;
                    Enter(pickup, "Set pivot 2"); return true;
                case "taxi-fare-receipt-give":
                    var receipt = TaxiWalker.FsmVariables.FindFsmGameObject("Receiptrigger").Value;
                    foreach (var f in receipt.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Use") { f.FsmVariables.FindFsmGameObject("Item").Value = null; Enter(f, "State 2"); if (f.FsmVariables.FindFsmGameObject("Item").Value != TaxiTerminal.FsmVariables.FindFsmGameObject("TicketPhysical").Value) throw new InvalidOperationException("Native receipt lookup did not find held paper: " + f.ActiveStateName); Enter(f, "State 1"); return true; }
                    throw new InvalidOperationException("Receipt use missing.");
                case "taxi-fare-near":
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    Player.position = (args[2] == "cash" ? TaxiCash.transform.position : TaxiTerminal.transform.position + TaxiCar.transform.right * 2f)
                        + Vector3.up * .4f; return true;
                case "taxi-fare-arrive":
                    if (SessionManager.Instance!.IsHost || TaxiItem == null) throw new InvalidOperationException("Guest-owned taxi fixture only.");
                    var body = (Rigidbody)Get(TaxiItem, "Body");
                    var destination = new Vector3(float.Parse(args[2], CultureInfo.InvariantCulture), float.Parse(args[3], CultureInfo.InvariantCulture),
                        float.Parse(args[4], CultureInfo.InvariantCulture));
                    var offset = destination - body.position;
                    if (Player == null) throw new InvalidOperationException("Driver missing.");
                    var driverPosition = Player.position + offset;
                    body.transform.position = destination; body.position = destination; Player.position = driverPosition;
                    body.velocity = body.angularVelocity = Vector3.zero; return true;
                case "taxi-fare-destination":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host destination fixture only.");
                    TaxiWalker.FsmVariables.FindFsmGameObject("DropOffPoint").Value.transform.position = TaxiCar.transform.position + TaxiCar.transform.forward * 24;
                    return true;
                case "taxi-fare-intent":
                    var session = SessionManager.Instance!;
                    session.SendWorldMessage(new TaxiFareIntent { PlayerId = session.LocalPlayerId, Sequence = uint.Parse(args[2]),
                        ExpectedControlRevision = uint.Parse(args[3]), FareId = uint.Parse(args[4]), Action = (TaxiFareAction)int.Parse(args[5]) }, Channel.ReliableOrdered); return true;
                case "taxi-fare-binding":
                    var sync = Get(WorldSyncManager.Instance!, "_taxiJob"); Call(sync, "ClearFare");
                    sync.GetType().GetField("_fareFailed", Members).SetValue(sync, args[2] == "off"); return true;
                default: throw new InvalidOperationException("Unknown taxi fare fixture.");
            }
        }
        private static void TaxiFareSnapshot(List<string> rows)
        {
            var sync = Get(WorldSyncManager.Instance!, "_taxiJob"); var fare = Get(sync, "_fare");
            var terminal = TaxiTerminal; var cash = TaxiCash; var w = TaxiWalker;
            rows.Add("fare-binding|" + (fare != null) + "|" + Get(sync, "_fareFailed") + "|" + (fare == null ? "0|0" : Get(fare, "_fareId") + "|" + Get(fare, "_controlRevision")));
            rows.Add("fare-native|" + terminal.enabled + "|" + terminal.ActiveStateName + "|" + cash.gameObject.activeInHierarchy + "|" + cash.enabled
                + "|" + cash.ActiveStateName + "|" + w.FsmVariables.FindFsmBool("Paid").Value);
            rows.Add("fare-cost|" + terminal.FsmVariables.FindFsmFloat("Cost").Value.ToString("R", CultureInfo.InvariantCulture)
                + "|" + w.FsmVariables.FindFsmFloat("Cost").Value.ToString("R", CultureInfo.InvariantCulture)
                + "|" + cash.FsmVariables.FindFsmString("Value").Value);
            rows.Add("fare-timer|" + TaxiJob.FsmVariables.FindFsmFloat("Timer").Value.ToString("R", CultureInfo.InvariantCulture));
            rows.Add("fare-destination|" + Vector(w.FsmVariables.FindFsmGameObject("DropOffPoint").Value.transform.position));
            rows.Add("fare-native-actions|" + terminal.Fsm.GetState("Make payment").Actions[0].GetType().Name + "|" + cash.Fsm.GetState("State 1").Actions[0].GetType().Name);
            var paper = terminal.FsmVariables.FindFsmGameObject("TicketPhysical").Value; var receiptItem = Get(Items, "_taxiReceipt");
            rows.Add("receipt|" + (paper.transform.parent == null ? "world" : PathOf(paper.transform.parent)) + "|" + paper.activeInHierarchy
                + "|" + paper.GetComponent<Rigidbody>().isKinematic + "|" + Vector(paper.transform.position));
            var pickup = Get(Items, "_bagPickup") as PlayMakerFSM; var joint = pickup == null ? null : pickup.GetComponent<FixedJoint>();
            rows.Add("receipt-hand|" + (pickup == null ? "none|unknown" : pickup.ActiveStateName + "|" + pickup.FsmVariables.FindFsmBool("HandEmpty").Value)
                + "|" + (joint == null || joint.connectedBody == null ? "none" : joint.connectedBody.name));
            rows.Add("receipt-item|" + (receiptItem == null ? "none" : Get(receiptItem, "Id") + "|" + Get(receiptItem, "LocallyOwned") + "|" + Get(receiptItem, "RemoteOwner")));
            if (fare != null) rows.Add("receipt-ledger|" + Get(fare, "_printed") + "|" + Get(fare, "_taken") + "|" + Get(fare, "_handed"));
            if (fare != null) rows.Add("fare-lcd|" + ((TextMesh)Get(fare, "_display")).text);
            var remote = Get(sync, "_remoteFare") as TaxiFareState;
            if (remote != null) rows.Add("receipt-remote|" + remote.ReceiptStage + "|" + remote.ReceiptFlags);
            if (remote != null) rows.Add("fare-remote|" + remote.Revision + "|" + remote.ControlRevision + "|" + remote.FareId + "|" + remote.Flags);
        }
    }
}
