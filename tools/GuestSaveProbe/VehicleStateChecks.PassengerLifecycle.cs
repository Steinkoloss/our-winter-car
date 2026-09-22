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
    internal static partial class VehicleStateChecks
    {
        private static void RunPassengerLifecycleChecks(Action<string, Action> check, SessionManager session, CaptureTransport capture, Action reset)
        {
            var savedPassenger = PassengerController.Instance; var savedDeath = DeathSyncManager.Instance;
            var controller = new GameObject("passenger lifecycle probe");
            var passenger = controller.AddComponent<PassengerController>(); passenger.enabled = false;
            var death = controller.AddComponent<DeathSyncManager>(); death.enabled = false;
            var hook = Get(death, "_deathHook");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var worldItems = (IDictionary)Get(Get(world, "_items"), "_items"); var originalItem = worldItems[VehicleId];
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var originalParent = player.parent; var originalPosition = player.position; var originalRotation = player.rotation; string originalName = player.name;
            var cc = player.gameObject.AddComponent<CharacterController>();
            var car = new GameObject("CORRIS"); var body = car.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var registered = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
            Set(registered, "Id", VehicleId); Set(registered, "Body", body); Set(registered, "IsVehicle", true); Set(registered, "Path", "CORRIS");
            worldItems[VehicleId] = registered;
            foreach (string name in new[] { "DriveTrigger", "MassPassenger", "RearSeat" })
            {
                var anchor = new GameObject(name); anchor.transform.SetParent(car.transform, false);
                anchor.transform.localPosition = name == "MassPassenger" ? new Vector3(.5f, .6f, 0)
                    : name == "DriveTrigger" ? new Vector3(-.5f, .6f, 0) : new Vector3(0, .6f, -1);
            }
            var seatType = typeof(PassengerController).GetNestedType("VehicleSeats", Static);
            var seats = typeof(PassengerController).GetMethod("ResolveSeats", Static).Invoke(null, new object[] { VehicleId, body });
            Require(seats != null && seatType != null, "Passenger fixture failed native anchor discovery.");
            var vehicles = (IDictionary)Get(passenger, "_vehicles"); vehicles[VehicleId] = seats;
            var ledger = (PassengerSeatLedger)Get(session, "_passengerSeats");
            var peers = (IDictionary)Get(session, "_playersByPeer"); var first = new PeerId(1001); var second = new PeerId(1002); var host = new PeerId(999);
            var one = (RemotePlayer)peers[first]; var two = (RemotePlayer)peers[second];
            var snapshots = new[] { new PassengerPeerSnapshot(one), new PassengerPeerSnapshot(two) };
            bool savedPermadeath = session.PermanentDeathEnabled;
            var systems = new GameObject("Systems"); var deathObject = new GameObject("Death"); deathObject.SetActive(false); deathObject.transform.SetParent(systems.transform, false);
            var deathFsm = NativeBagPartChecks.MakeFsm(deathObject,
                NativeBagPartChecks.Find(ReadOccupancyRows("passenger-lifecycle-probe.json"), "Systems/Death", "Activate Dead Body"));
            deathFsm.Fsm.Init(deathFsm); deathObject.SetActive(true); NativeBagPartChecks.Start(deathFsm);
            Action<bool> mode = isHost => { SetProperty(session, "IsHost", isHost);
                SetProperty(session, "State", isHost ? SessionState.Hosting : SessionState.Connected); SetProperty(session, "LocalPlayerId", isHost ? (byte)0 : (byte)3); };
            Action clean = () =>
            {
                Call(passenger, "RetireAllSeats"); Call(passenger, "ClearRemoteSeats"); ledger.Clear();
                vehicles[VehicleId] = seats; player.SetParent(originalParent, false); player.position = originalPosition; player.rotation = Quaternion.identity;
                car.transform.position = originalPosition; car.transform.rotation = Quaternion.Euler(8, 25, 12);
                if (cc == null) cc = player.gameObject.AddComponent<CharacterController>(); cc.enabled = true;
                Set(passenger, "_player", player); Set(passenger, "_disabled", false); Set(passenger, "_seatSequence", (ushort)0);
                Set(passenger, "_lastLevel", Application.loadedLevelName); Set(passenger, "_nextVehicleScanAt", float.MaxValue);
                Set(passenger, "_nextInteractionSearchAt", float.MaxValue);
                Set(hook, "_localDeathActive", false); Set(hook, "_deathReported", false); Set(hook, "_reportSequence", (byte)0);
                foreach (var p in new[] { one, two }) { p.IsDead = false; p.Position = car.transform.TransformPoint(((Vector3[])Get(seats!, "SeatLocal"))[0]); p.LastTransformTime = Time.unscaledTime; }
                SetProperty(session, "PermanentDeathEnabled", false); mode(true); capture.Packets.Clear();
            };
            Action enter = () => { Call(passenger, "Enter", session, seats!, 0); Require(passenger.IsLocalSeated, "Entry failed."); };
            Func<byte, ushort, byte, PassengerState> claim = (id, sequence, index) => new PassengerState {
                PlayerId = id, VehicleId = VehicleId, SeatIndex = index, Sequence = sequence };
            Action<PeerId, IMessage> packet = (peer, message) => Call(session, "OnPacketReceived", peer, PacketCodec.Encode(message), Channel.ReliableOrdered);
            Action free = () => Require(!passenger.IsLocalSeated && player.parent == originalParent && (cc == null || cc.enabled), "Passenger remained pinned or controller was not restored.");
            Action<byte> noRemoteSeat = id => Require(!passenger.TryGetSeatAnchor(id, out _, out _), "Retired passenger still has an avatar anchor.");
            try
            {
                player.name = "PLAYER";
                SetStaticProperty(typeof(PassengerController), "Instance", passenger); SetStaticProperty(typeof(DeathSyncManager), "Instance", death);
                Call(hook, "TryHookDeathFsm", deathFsm, session);
                check("passenger lifecycle: actual entry pins a tilted moving car and ordinary exit restores control", () =>
                {
                    clean(); enter(); Require(player.parent == car.transform && !cc.enabled && ledger.IsOccupant(0, VehicleId), "Entry did not parent and record seat.");
                    car.transform.position += new Vector3(10, 2, 3); Call(passenger, "LateUpdate");
                    var expected = car.transform.TransformPoint(((Vector3[])Get(seats!, "SeatLocal"))[0] + new Vector3(0, -.45f, 0));
                    Require((player.position - expected).sqrMagnitude < .000001f, "Seat pin did not follow moving car.");
                    var position = player.position; Call(passenger, "Exit", session); free();
                    Require(!ledger.HasOccupant(VehicleId) && player.position == position && Vector3.Dot(player.up, Vector3.up) > .999f, "Exit kept seat/tilt or teleported the player.");
                });
                check("passenger lifecycle: disabled controller stays disabled after ordinary exit", () =>
                { clean(); cc.enabled = false; enter(); Call(passenger, "Exit", session); Require(!cc.enabled && !passenger.IsLocalSeated, "Exit enabled a controller it did not disable."); });
                foreach (bool isHost in new[] { true, false })
                {
                    bool hosting = isHost;
                    check("passenger lifecycle: native death hook retires " + (hosting ? "host" : "guest") + " seat before reporting", () =>
                    {
                        clean(); mode(hosting); enter(); Call(passenger, "LateUpdate"); capture.Packets.Clear();
                        NativeBagPartChecks.Fire(deathFsm, "Probe idle"); NativeBagPartChecks.Fire(deathFsm, "Take photo"); free();
                        Require(!ledger.HasOccupant(VehicleId) && (bool)Get(hook, "_localDeathActive"), "Death retained host occupancy or missed death state.");
                        Require(capture.Packets.Count > 0, "Death was not reported.");
                        foreach (var p in capture.Packets) Require(hosting ? p.Message is PlayerDeathEvent : p.Message is PlayerDeathReport, "Unexpected death lifecycle output.");
                        var position = player.position; car.transform.position += Vector3.right * 100; Call(passenger, "LateUpdate"); Require(player.position == position, "Dead player was repinned.");
                    });
                }
                check("passenger lifecycle: native destruction of movement controller does not block death cleanup", () =>
                {
                    clean(); mode(false); enter(); UnityEngine.Object.DestroyImmediate(cc);
                    NativeBagPartChecks.Fire(deathFsm, "Probe idle"); NativeBagPartChecks.Fire(deathFsm, "Take photo");
                    free(); Require(player.GetComponent<CharacterController>() == null, "Death cleanup recreated a destroyed controller.");
                });
                check("passenger lifecycle: dead players cannot enter and LateUpdate catches death between updates", () =>
                {
                    clean(); Set(hook, "_localDeathActive", true); Call(passenger, "Enter", session, seats!, 0); Require(!passenger.IsLocalSeated && cc.enabled, "Dead player entered.");
                    Set(hook, "_localDeathActive", false); enter(); Set(hook, "_localDeathActive", true); Call(passenger, "LateUpdate"); free();
                });
                check("passenger lifecycle: respawn cleanup preserves a new native parent and pose", () =>
                {
                    clean(); mode(false); enter(); Set(hook, "_localDeathActive", true);
                    player.SetParent(controller.transform, false); player.position = originalPosition + new Vector3(25, 3, 7);
                    var position = player.position; var rotation = Quaternion.Euler(2, 80, 4); player.rotation = rotation;
                    capture.Packets.Clear(); Call(hook, "NotifyRespawned", session);
                    Require(!passenger.IsLocalSeated && player.parent == controller.transform && player.position == position
                        && player.rotation == rotation && cc.enabled, "Seat cleanup pulled a respawn back or rewrote its rotation.");
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Message is PlayerRespawn, "Local respawn was not reported.");
                    Call(passenger, "LateUpdate"); Require(player.position == position, "Respawn was repinned.");
                });
                check("passenger lifecycle: seat release still detaches after the vehicle index is cleared", () =>
                { clean(); enter(); vehicles.Clear(); Call(passenger, "ForceExit", "fixture vehicle index removed"); free(); });
                check("passenger lifecycle: ended session releases before any late seat pin", () =>
                { clean(); enter(); SetProperty(session, "State", SessionState.Idle); Call(passenger, "LateUpdate"); free(); });
                check("passenger lifecycle: repeated retirement never changes a later controller or parent", () =>
                {
                    clean(); enter(); Call(session, "RetirePassengerSeat", (byte)0); free();
                    cc.enabled = false; player.SetParent(controller.transform, false); Call(session, "RetirePassengerSeat", (byte)0);
                    Require(!cc.enabled && player.parent == controller.transform, "Duplicate retirement rewrote released player state.");
                });
                check("passenger lifecycle: host death report frees canonical seat and avatar before terminal broadcast", () =>
                {
                    clean(); packet(first, claim(1, 10, 0)); Require(ledger.IsOccupant(1, VehicleId), "Valid guest claim rejected.");
                    Require(passenger.TryGetSeatAnchor(1, out _, out _), "Accepted guest lacked anchor.");
                    capture.Packets.Clear(); packet(first, new PlayerDeathReport { PlayerId = 1, Cause = DeathCause.Hypothermia });
                    Require(one.IsDead && !ledger.HasOccupant(VehicleId), "Host death retained canonical occupancy."); noRemoteSeat(1);
                    foreach (var p in capture.Packets) Require(p.Message is PlayerDeathEvent e && e.PlayerId == 1, "Host death event was not canonical.");
                    packet(second, claim(2, 0, 0)); Require(ledger.IsOccupant(2, VehicleId), "Dead passenger still blocks a new claimant.");
                });
                check("passenger lifecycle: dead keepalive cannot restore occupancy and consumes its claim sequence", () =>
                {
                    clean(); packet(first, claim(1, 10, 0)); one.IsDead = true;
                    packet(first, claim(1, 11, 0)); Require(!ledger.HasOccupant(VehicleId), "Dead continuing claim survived validation.");
                    one.IsDead = false; packet(first, claim(1, 11, 0)); Require(!ledger.HasOccupant(VehicleId), "Rejected dead claim replay became valid.");
                    packet(first, claim(1, 12, 0)); Require(ledger.IsOccupant(1, VehicleId), "Fresh living claim could not enter.");
                });
                check("passenger lifecycle: authenticated respawn stays on foot until a fresh seat claim", () =>
                {
                    clean(); packet(first, claim(1, 10, 0)); packet(first, new PlayerDeathReport());
                    var position = car.transform.TransformPoint(((Vector3[])Get(seats!, "SeatLocal"))[0]);
                    packet(first, new PlayerRespawn { PlayerId = 2, Position = new NetVector3(position.x, position.y, position.z), Rotation = NetQuaternion.Identity });
                    Require(!one.IsDead && !ledger.HasOccupant(VehicleId), "Respawn revived occupancy."); noRemoteSeat(1);
                    packet(first, claim(1, 10, 0)); Require(!ledger.HasOccupant(VehicleId), "Old pre-death claim revived occupancy.");
                    packet(first, claim(1, 11, 0)); Require(ledger.IsOccupant(1, VehicleId), "Fresh post-respawn claim failed.");
                });
                check("passenger lifecycle: spoofed death identity cannot clear another passenger", () =>
                {
                    clean(); packet(first, claim(1, 10, 0)); packet(second, new PlayerDeathReport { PlayerId = 1 });
                    Require(!one.IsDead && two.IsDead && ledger.IsOccupant(1, VehicleId), "Forged victim id retired another passenger.");
                    packet(new PeerId(777), new PlayerDeathReport { PlayerId = 1 }); Require(ledger.IsOccupant(1, VehicleId), "Unknown peer retired passenger.");
                });
                check("passenger lifecycle: observer death clears one anchor and rejects dead seat updates", () =>
                {
                    clean(); mode(false); packet(host, claim(1, 10, 0)); packet(host, claim(2, 10, 1));
                    Require(passenger.TryGetSeatAnchor(1, out _, out _), "Observer did not create anchor.");
                    packet(first, new PlayerDeathEvent { PlayerId = 1 }); Require(!one.IsDead, "Non-host death event accepted.");
                    packet(host, new PlayerDeathEvent { PlayerId = 1 }); noRemoteSeat(1); Require(passenger.TryGetSeatAnchor(2, out _, out _), "Other passenger was cleared.");
                    packet(host, claim(1, 11, 0)); noRemoteSeat(1);
                    packet(host, new PlayerRespawn { PlayerId = 1 }); packet(host, claim(1, 11, 0)); noRemoteSeat(1);
                    packet(host, claim(1, 12, 0)); Require(passenger.TryGetSeatAnchor(1, out _, out _), "Fresh living seat was not accepted.");
                });
                check("passenger lifecycle: group death retires all local and remote seats without running vanilla death effects", () =>
                {
                    clean(); mode(false); enter(); packet(host, claim(1, 10, 1)); packet(host, claim(2, 10, 2));
                    Set(hook, "_localDeathActive", true);
                    packet(host, new PlayerDeathEvent { PlayerId = 1, Flags = PlayerDeathEventFlags.PermadeathWipe });
                    free(); noRemoteSeat(1); noRemoteSeat(2); Require(one.IsDead && two.IsDead, "Group death left a living passenger record.");
                    Set(hook, "_localDeathActive", false); one.IsDead = false; packet(host, claim(1, 10, 1)); noRemoteSeat(1);
                    Call(passenger, "ClearRemoteSeats"); packet(host, claim(1, 0, 1)); Require(passenger.TryGetSeatAnchor(1, out _, out _), "Session reset retained retired request history.");
                });
                check("passenger lifecycle: host group retirement empties join occupancy and retains dead-claim rejection", () =>
                {
                    clean(); enter(); packet(first, claim(1, 10, 1)); packet(second, claim(2, 10, 2));
                    Call(session, "RetirePassengersForGroupDeath"); free(); Require(!ledger.HasOccupant(VehicleId) && one.IsDead && two.IsDead, "Group cleanup retained host join occupancy.");
                    packet(first, claim(1, 11, 1)); Require(!ledger.HasOccupant(VehicleId), "Group-dead player reclaimed a seat.");
                });
            }
            finally
            {
                Call(passenger, "RetireAllSeats"); Call(passenger, "ClearRemoteSeats"); ledger.Clear();
                foreach (var p in snapshots) p.Restore(); SetProperty(session, "PermanentDeathEnabled", savedPermadeath);
                player.SetParent(originalParent, false); player.position = originalPosition; player.rotation = originalRotation; player.name = originalName;
                if (cc != null) UnityEngine.Object.DestroyImmediate(cc);
                worldItems[VehicleId] = originalItem;
                UnityEngine.Object.DestroyImmediate(systems); UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(controller);
                SetStaticProperty(typeof(PassengerController), "Instance", savedPassenger); SetStaticProperty(typeof(DeathSyncManager), "Instance", savedDeath); reset();
            }
        }

        private sealed class PassengerPeerSnapshot
        {
            private readonly RemotePlayer _player; private readonly bool _dead; private readonly Vector3 _position;
            private readonly Quaternion _rotation; private readonly float _time;
            internal PassengerPeerSnapshot(RemotePlayer player)
            { _player = player; _dead = player.IsDead; _position = player.Position; _rotation = player.Rotation; _time = player.LastTransformTime; }
            internal void Restore() { _player.IsDead = _dead; _player.Position = _position; _player.Rotation = _rotation; _player.LastTransformTime = _time; }
        }
    }
}
