using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool TaxiPassengerProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_TAXI_PASSENGER_TEST") == "1";
        private static PassengerController Passengers => PassengerController.Instance ?? throw new InvalidOperationException("Passenger controller missing.");
        private static readonly Dictionary<PlayMakerFSM, bool> PausedTaxiTutorial = new Dictionary<PlayMakerFSM, bool>();
        private static object TaxiSeats
        {
            get
            {
                Call(Passengers, "ScanVehicles", true);
                foreach (DictionaryEntry pair in (IDictionary)Get(Passengers, "_vehicles"))
                    if (Get(pair.Value, "Body") is Rigidbody body && body.gameObject == TaxiCar) return pair.Value;
                throw new InvalidOperationException("Taxi seats not registered.");
            }
        }

        private static bool TaxiPassengerCommand(string[] args)
        {
            if (!TaxiPassengerProbe || !args[1].StartsWith("taxi-passenger-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox(); var session = SessionManager.Instance!;
            switch (args[1])
            {
                case "taxi-passenger-near":
                    if (Player == null || Player.parent != null) throw new InvalidOperationException("On-foot player required.");
                    int index = int.Parse(args[2]);
                    var point = ((Vector3[])Get(TaxiSeats, "SeatLocal"))[index];
                    point.x += point.x > 0 ? .65f : -.65f; point.y -= .45f;
                    Player.position = TaxiCar.transform.TransformPoint(point); return true;
                case "taxi-passenger-enter":
                    Call(Passengers, "Enter", session, TaxiSeats, int.Parse(args[2])); return true;
                case "taxi-passenger-exit":
                    Call(Passengers, "Exit", session); return true;
                case "taxi-passenger-request":
                    if (session.IsHost) throw new InvalidOperationException("Guest request only.");
                    ushort sequence = unchecked((ushort)((ushort)Get(Passengers, "_seatSequence") + 1));
                    Passengers.GetType().GetField("_seatSequence", Members).SetValue(Passengers, sequence);
                    session.SendWorldMessage(new PassengerState { PlayerId = session.LocalPlayerId,
                        VehicleId = (uint)Get(TaxiSeats, "VehicleId"), SeatIndex = byte.Parse(args[2]), Sequence = sequence }, Channel.ReliableOrdered);
                    return true;
                case "taxi-passenger-move":
                    if (TaxiDrive.ActiveStateName != "Player in car") throw new InvalidOperationException("Accepted driver required.");
                    var car = TaxiCar.GetComponent<Rigidbody>();
                    car.transform.position += TaxiCar.transform.forward * float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                    car.transform.rotation *= Quaternion.Euler(0, 12, 0); return true;
                case "taxi-passenger-tutorial":
                    if (!session.IsHost) throw new InvalidOperationException("Host tutorial fixture only.");
                    var tutorial = TaxiCar.transform.Find("TaxiFunctions/Tutorial").gameObject;
                    if (args[2] == "on")
                    {
                        if (tutorial.activeSelf) throw new InvalidOperationException("Tutorial is already active.");
                        foreach (var fsm in tutorial.GetComponentsInChildren<PlayMakerFSM>(true))
                        { PausedTaxiTutorial.Add(fsm, fsm.enabled); fsm.enabled = false; }
                        tutorial.SetActive(true);
                    }
                    else
                    {
                        tutorial.SetActive(false);
                        foreach (var pair in PausedTaxiTutorial) pair.Key.enabled = pair.Value;
                        PausedTaxiTutorial.Clear();
                    }
                    return true;
                case "taxi-passenger-layout":
                    var seats = TaxiSeats; var coords = (Vector3[])Get(seats, "SeatLocal");
                    if ((byte)Get(seats, "AvailableSeats") != 5 || coords[0].x < .3f || coords[2].x > -.3f
                        || coords[0].z - coords[2].z < .5f || coords[0].y < .4f || coords[2].y < .4f)
                        throw new InvalidOperationException("Native taxi seat layout mismatch.");
                    // A game update losing one anchor must not invent geometry.
                    var catalog = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                    var data = catalog.GetProperty("TaxiPassengers", Members).GetValue(null, null);
                    var names = (IDictionary)Get(data, "Names"); var saved = names["driverMass"];
                    try
                    {
                        names["driverMass"] = "missing-native-seat";
                        if (Call(Passengers, "ResolveTaxiSeats", (uint)Get(seats, "VehicleId"), TaxiCar.GetComponent<Rigidbody>()) != null)
                            throw new InvalidOperationException("Missing taxi anchor accepted.");
                    }
                    finally { names["driverMass"] = saved; }
                    return true;
                default: throw new InvalidOperationException("Unknown taxi passenger fixture.");
            }
        }

        private static void TaxiPassengerSnapshot(List<string> rows)
        {
            if (!TaxiPassengerProbe) return;
            var session = SessionManager.Instance!; var pc = Passengers;
            rows.Add("passenger-local|" + pc.IsLocalSeated + "|" + Get(pc, "_seatedVehicleId") + "|" + Get(pc, "_seatedIndex")
                + "|" + Get(pc, "_seatSequence") + "|" + (Player == null ? "none" : Player.parent == null ? "root" : PathOf(Player.parent))
                + "|" + (Player != null && Player.GetComponent<CharacterController>() != null && Player.GetComponent<CharacterController>().enabled)
                + "|" + Get(pc, "_hint") + "|" + Get(pc, "_disabled"));
            foreach (DictionaryEntry pair in (IDictionary)Get(pc, "_vehicles"))
            {
                var body = (Rigidbody)Get(pair.Value, "Body");
                rows.Add("passenger-vehicle|" + pair.Key + "|" + body.name + "|" + Get(pair.Value, "AvailableSeats"));
                if (body.gameObject != TaxiCar) continue;
                var seats = (Vector3[])Get(pair.Value, "SeatLocal");
                for (int i = 0; i < seats.Length; i++)
                    rows.Add("passenger-seat|" + i + "|" + Vector(seats[i]) + "|" + Call(pc, "SeatAvailable", pair.Value, i));
                if (pc.IsLocalSeated) rows.Add("passenger-offset|" + Vector(body.transform.InverseTransformPoint(Player!.position)));
            }
            foreach (var player in session.Players)
            {
                if (pc.TryGetSeatAnchor(player.PlayerId, out var anchor, out var vehicle))
                    rows.Add("passenger-remote|" + player.PlayerId + "|" + PathOf(vehicle!) + "|" + Vector(vehicle!.InverseTransformPoint(anchor!.position)));
                rows.Add("passenger-peer|" + player.PlayerId + "|" + (Time.unscaledTime - player.LastTransformTime) + "|" + Vector(player.Position));
            }
            foreach (var state in ((PassengerSeatLedger)Get(session, "_passengerSeats")).Occupants)
                rows.Add("passenger-accepted|" + state.PlayerId + "|" + state.VehicleId + "|" + state.SeatIndex + "|" + state.Sequence);
        }
    }
}
