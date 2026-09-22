using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunPassengerDiscoveryChecks(Action<string, Action> check, SessionManager session, CaptureTransport capture, Action reset)
        {
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var items = (IDictionary)Get(Get(world, "_items"), "_items"); var originalItems = new Hashtable(items);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position; var rotation = player.rotation;
            var cc = player.gameObject.AddComponent<CharacterController>();
            var savedPassenger = PassengerController.Instance; var savedDeath = DeathSyncManager.Instance;
            var go = new GameObject("passenger discovery probe"); var passenger = go.AddComponent<PassengerController>(); passenger.enabled = false;
            var vehicles = (IDictionary)Get(passenger, "_vehicles"); var ledger = (PassengerSeatLedger)Get(session, "_passengerSeats");
            var peers = (IDictionary)Get(session, "_playersByPeer"); var peer = new PeerId(1001); var one = (RemotePlayer)peers[peer];
            var savedPeer = new PassengerPeerSnapshot(one); var cars = new List<DiscoveryVehicle>();
            DiscoveryVehicle first = null!;
            Func<string, uint, bool, DiscoveryVehicle> add = (name, id, anchors) =>
            {
                var car = new DiscoveryVehicle(name, position, anchors); cars.Add(car);
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                Set(item, "Id", id); Set(item, "Body", car.Body); Set(item, "IsVehicle", true); Set(item, "Path", name); items[id] = item;
                return car;
            };
            Action scan = () => Call(passenger, "ScanVehicles", true);
            Action<bool> mode = host => { SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3); };
            Action clean = () =>
            {
                Call(passenger, "RetireAllSeats"); Call(passenger, "ClearRemoteSeats"); vehicles.Clear(); ledger.Clear();
                player.SetParent(parent, false); player.position = position; player.rotation = rotation; cc.enabled = true;
                foreach (var car in cars) car.Dispose(); cars.Clear(); items.Clear();
                Set(passenger, "_player", player); Set(passenger, "_disabled", false); Set(passenger, "_seatSequence", (ushort)0);
                Set(passenger, "_lastLevel", Application.loadedLevelName); Set(passenger, "_nextInteractionSearchAt", float.MaxValue);
                mode(true); one.IsDead = false; first = add("CORRIS", VehicleId, true); scan();
                Require(vehicles.Contains(VehicleId), "Initial passenger vehicle was not discovered."); capture.Packets.Clear();
            };
            Func<uint, object> seats = id => vehicles[id] ?? throw new InvalidOperationException("Missing discovered vehicle.");
            Func<uint, Vector3> seatPosition = id => ((Rigidbody)Get(seats(id), "Body")).transform.TransformPoint(((Vector3[])Get(seats(id), "SeatLocal"))[0]);
            Action enter = () => { Call(passenger, "Enter", session, seats(VehicleId), 0); Require(passenger.IsLocalSeated, "Seat entry failed."); };
            Func<ushort, PassengerState> claim = sequence => new PassengerState { PlayerId = 1, VehicleId = VehicleId, SeatIndex = 0, Sequence = sequence };
            Action<ushort> packet = sequence => Call(session, "OnPacketReceived", peer, PacketCodec.Encode(claim(sequence)), Channel.ReliableOrdered);
            Func<byte, Transform> anchor = id => { Require(passenger.TryGetSeatAnchor(id, out var a, out _), "Missing remote seat anchor.");
                return a ?? throw new InvalidOperationException("Null remote seat anchor."); };
            try
            {
                SetStaticProperty(typeof(PassengerController), "Instance", passenger); SetStaticProperty(typeof(DeathSyncManager), "Instance", null);
                check("passenger discovery: inactive native anchors register and unchanged bodies retain their seat bindings", () =>
                {
                    clean(); var original = seats(VehicleId); scan();
                    Require(ReferenceEquals(original, seats(VehicleId)) && ((Transform)Get(original, "DriveTrigger")).name == "DriveTriggerX",
                        "Unchanged body was re-created or inactive anchor was missed.");
                });
                check("passenger discovery: live replacement rebinds remote anchors without losing accepted occupancy", () =>
                {
                    clean(); passenger.OnRemotePassengerState(claim(10)); var oldAnchor = anchor(1); var old = seats(VehicleId);
                    var replacement = add("CORRIS", VehicleId, true); replacement.Body.transform.position += Vector3.right * 100; scan();
                    Require((Rigidbody)Get(seats(VehicleId), "Body") == replacement.Body && first.Body != null, "Live old body prevented replacement discovery.");
                    foreach (var a in (GameObject?[])Get(old, "RemoteAnchors")) Require(a == null, "Old anchor reference was retained.");
                    var current = anchor(1); Require(current != oldAnchor && current.IsChildOf(replacement.Body.transform), "Remote avatar remained on old body.");
                });
                check("passenger discovery: destroyed body with the same id is discovered again", () =>
                {
                    clean(); first.Dispose(); var replacement = add("CORRIS", VehicleId, true); scan();
                    Require((Rigidbody)Get(seats(VehicleId), "Body") == replacement.Body, "Destroyed cache entry blocked new body.");
                });
                check("passenger discovery: missing registration clears only cached geometry and can return", () =>
                {
                    clean(); passenger.OnRemotePassengerState(claim(10)); anchor(1); var item = items[VehicleId]; items.Remove(VehicleId); scan();
                    Require(!vehicles.Contains(VehicleId) && !passenger.TryGetSeatAnchor(1, out _, out _), "Missing vehicle retained an anchor.");
                    items[VehicleId] = item; scan(); Require(anchor(1).IsChildOf(first.Body.transform), "Returning logical vehicle lost accepted remote seating.");
                });
                check("passenger discovery: nonvehicle and null-body registrations cannot retain passenger geometry", () =>
                {
                    clean(); var item = items[VehicleId]; Set(item, "IsVehicle", false); scan(); Require(!vehicles.Contains(VehicleId), "Nonvehicle retained seats.");
                    Set(item, "IsVehicle", true); scan(); Set(item, "Body", null); scan(); Require(!vehicles.Contains(VehicleId), "Null body retained seats.");
                    Set(item, "Body", first.Body); scan(); Require(vehicles.Contains(VehicleId), "Repaired body failed to return.");
                });
                check("passenger discovery: validation refreshes live replacements before using entry proximity", () =>
                {
                    clean(); one.Position = seatPosition(VehicleId); one.LastTransformTime = Time.unscaledTime;
                    var replacement = add("CORRIS", VehicleId, true); replacement.Body.transform.position += Vector3.right * 100;
                    // Unity 5 defers Rigidbody.position -> Transform until a physics tick; this probe runs synchronously.
                    Require(Vector3.Distance(replacement.Body.transform.position, first.Body.transform.position) > 90f,
                        "Fixture replacement did not move its Transform: body=" + replacement.Body.position + ", transform=" + replacement.Body.transform.position + ", old=" + first.Body.transform.position);
                    Set(passenger, "_nextVehicleScanAt", float.MaxValue); packet(10);
                    Require((Rigidbody)Get(seats(VehicleId), "Body") == replacement.Body, "Host validation retained the old body.");
                    Require(!ledger.HasOccupant(VehicleId), "Old-body proximity accepted a new claim.");
                    one.Position = seatPosition(VehicleId); one.LastTransformTime = Time.unscaledTime; packet(11);
                    Require(ledger.IsOccupant(1, VehicleId), "Current-body proximity was rejected.");
                });
                check("passenger discovery: moving-car keepalive still keeps accepted logical occupancy after rebinding", () =>
                {
                    clean(); one.Position = seatPosition(VehicleId); one.LastTransformTime = Time.unscaledTime; packet(10);
                    var replacement = add("CORRIS", VehicleId, true); replacement.Body.transform.position += Vector3.right * 100;
                    one.LastTransformTime = 0; packet(11);
                    Require(ledger.IsOccupant(1, VehicleId) && anchor(1).IsChildOf(replacement.Body.transform), "Existing seat incorrectly required new proximity proof.");
                });
                check("passenger discovery: next host keepalive rejects a removed vehicle and broadcasts canonical exit", () =>
                {
                    clean(); one.Position = seatPosition(VehicleId); one.LastTransformTime = Time.unscaledTime; packet(10);
                    items.Remove(VehicleId); capture.Packets.Clear(); packet(11);
                    Require(!ledger.HasOccupant(VehicleId) && !passenger.TryGetSeatAnchor(1, out _, out _), "Removed vehicle remained occupied after host validation.");
                    Require(capture.Packets.Count > 0, "Missing canonical seat correction.");
                    foreach (var p in capture.Packets) Require(p.Message is PassengerState s && !s.IsSeated && s.Sequence == 11, "Correction changed claim ordering.");
                });
                check("passenger discovery: unsupported replacement cannot inherit cached seats", () =>
                { clean(); add("unregistered passenger model", VehicleId, true); scan(); Require(!vehicles.Contains(VehicleId), "Unsupported body inherited old seats."); });
                check("passenger discovery: late native anchors retry without losing other supported cars", () =>
                {
                    clean(); var other = add("SORBET(190-200psi)", VehicleId + 1, true); scan(); var otherSeats = seats(VehicleId + 1);
                    var replacement = add("CORRIS", VehicleId, false); scan(); Require(!vehicles.Contains(VehicleId), "Incomplete body inherited stale seats.");
                    Require(ReferenceEquals(otherSeats, seats(VehicleId + 1)), "Another car was reset.");
                    replacement.AddAnchors(); scan(); Require((Rigidbody)Get(seats(VehicleId), "Body") == replacement.Body && (Rigidbody)Get(otherSeats, "Body") == other.Body, "Late anchors were not retried.");
                });
                foreach (bool isHost in new[] { true, false })
                {
                    bool hostMode = isHost;
                    check("passenger discovery: " + (hostMode ? "host" : "guest") + " local rider exits once and explicitly enters the replacement", () =>
                    {
                        clean(); mode(hostMode); enter(); Call(passenger, "LateUpdate"); var before = player.position; capture.Packets.Clear();
                        var replacement = add("CORRIS", VehicleId, true); replacement.Body.transform.position += Vector3.right * 100; scan();
                        Require(!passenger.IsLocalSeated && player.parent == parent && cc.enabled && player.position == before, "Replacement retained or teleported local rider.");
                        Require(!ledger.IsOccupant(session.LocalPlayerId, VehicleId) && capture.Packets.Count > 0, "Local exit was not sent/recorded.");
                        foreach (var p in capture.Packets) Require(p.Message is PassengerState s && !s.IsSeated && s.Sequence == 2, "Local exit did not preserve reliable claim sequence.");
                        capture.Packets.Clear(); scan(); Require(capture.Packets.Count == 0, "Unchanged body repeatedly ejected passenger.");
                        enter(); Require(player.parent == replacement.Body.transform, "Fresh entry used old car parent.");
                    });
                }
                check("passenger discovery: removing a locally occupied car restores parenting and control", () =>
                {
                    clean(); enter(); items.Remove(VehicleId); scan();
                    Require(!passenger.IsLocalSeated && player.parent == parent && cc.enabled && !vehicles.Contains(VehicleId), "Missing car left the local player stranded.");
                });
                check("passenger discovery: rebinding retains observer sequence history", () =>
                {
                    clean(); mode(false); passenger.OnRemotePassengerState(claim(10)); add("CORRIS", VehicleId, true); scan();
                    passenger.OnRemotePassengerState(new PassengerState { PlayerId = 1, VehicleId = 0, SeatIndex = PassengerState.SeatNone, Sequence = 9 });
                    anchor(1); passenger.OnRemotePassengerState(new PassengerState { PlayerId = 1, VehicleId = 0, SeatIndex = PassengerState.SeatNone, Sequence = 11 });
                    Require(!passenger.TryGetSeatAnchor(1, out _, out _), "Fresh host exit did not retire re-bound seat.");
                });
                check("passenger discovery: repairing another car leaves the current local seat and remote anchors intact", () =>
                {
                    clean(); var other = add("SORBET(190-200psi)", VehicleId + 1, true); scan(); enter();
                    passenger.OnRemotePassengerState(new PassengerState { PlayerId = 1, VehicleId = VehicleId, SeatIndex = 1, Sequence = 10 });
                    var current = anchor(1); add("SORBET(190-200psi)", VehicleId + 1, true); scan();
                    Require(passenger.IsLocalSeated && player.parent == first.Body.transform && anchor(1) == current, "Unrelated repair disturbed occupied car.");
                });
                check("passenger discovery: normal Update performs the same repair when its scan interval expires", () =>
                {
                    clean(); enter(); var replacement = add("CORRIS", VehicleId, true); Set(passenger, "_nextVehicleScanAt", 0f); Call(passenger, "Update");
                    Require(!passenger.IsLocalSeated && (Rigidbody)Get(seats(VehicleId), "Body") == replacement.Body && player.parent == parent, "Update did not perform automatic recovery.");
                });
            }
            finally
            {
                Call(passenger, "RetireAllSeats"); Call(passenger, "ClearRemoteSeats"); ledger.Clear(); savedPeer.Restore();
                player.SetParent(parent, false); player.position = position; player.rotation = rotation; UnityEngine.Object.DestroyImmediate(cc);
                foreach (var car in cars) car.Dispose(); items.Clear(); foreach (DictionaryEntry entry in originalItems) items[entry.Key] = entry.Value;
                UnityEngine.Object.DestroyImmediate(go); SetStaticProperty(typeof(PassengerController), "Instance", savedPassenger);
                SetStaticProperty(typeof(DeathSyncManager), "Instance", savedDeath); reset();
            }
        }

        private sealed class DiscoveryVehicle : IDisposable
        {
            internal readonly Rigidbody Body;
            internal DiscoveryVehicle(string name, Vector3 position, bool anchors)
            {
                var go = new GameObject(name); go.transform.position = position; Body = go.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                if (anchors) AddAnchors();
            }
            internal void AddAnchors()
            {
                foreach (string name in new[] { "MassPassenger", "DriveTriggerX", "RearSeat" })
                {
                    var go = new GameObject(name); go.transform.SetParent(Body.transform, false);
                    go.transform.localPosition = name == "MassPassenger" ? new Vector3(.38f, .61f, -.31f)
                        : name == "DriveTriggerX" ? new Vector3(-.35f, -.14f, .4f) : new Vector3(0, 0, -.9f);
                    if (name == "MassPassenger") go.SetActive(false);
                }
            }
            public void Dispose() { if (Body != null) UnityEngine.Object.DestroyImmediate(Body.gameObject); }
        }
    }
}
