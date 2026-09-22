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
    internal static partial class VehicleStateChecks
    {
        private static void RunConditionReleaseChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var itemsObject = Get(vehicles, "_items"); var items = (IDictionary)Get(itemsObject, "_items");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position;
            using (var f = new WheelHealthFixture())
            try
            {
                object guest = null!, host = null!;
                Func<object> fresh = () =>
                {
                    var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                    return item;
                };
                Action<bool> role = hosting =>
                {
                    items[VehicleId] = hosting ? host : guest; SetProperty(session, "IsHost", hosting);
                    SetProperty(session, "State", hosting ? SessionState.Hosting : SessionState.Connected);
                    SetProperty(session, "LocalPlayerId", hosting ? (byte)0 : (byte)1);
                    player.SetParent(parent, false); player.position = position; capture.Packets.Clear();
                };
                Action clean = () => { reset(); guest = fresh(); host = fresh(); role(false); Call(vehicles, "ClearConditionStreams"); f.Seed(); };
                Action<IMessage> deliver = message => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Func<byte, VehicleCondition> initial = available => new VehicleCondition { VehicleId = VehicleId, OwnerPlayerId = 0, Sequence = 65535,
                    Availability = available, TirePressure = 190, HealthFL = 30, HealthFR = 31, HealthRL = 32, HealthRR = 33 };
                Action saved = () => { for (int i = 0; i < 4; i++) Require(f.Saved[i].Value == 90 + i, "Release confirmation wrote saved tyre data."); };
                Func<byte, List<IMessage>> release = available =>
                {
                    deliver(initial(available)); player.position = f.Body.transform.position;
                    Require((bool)Call(itemsObject, "TryClaimForInteraction", session, guest)!, "Guest could not claim the fixture car.");
                    Set(guest, "LastMovedAt", -999f); Call(itemsObject, "UpdateItems", session);
                    var sent = new List<IMessage>(); foreach (var packet in capture.Packets)
                        if (packet.Message is ItemTransform || packet.Message is VehicleCondition) sent.Add(packet.Message);
                    Require(!(bool)Get(guest, "LocallyOwned") && Get(guest, "PendingConditionRelease") != null
                        && Get(guest, "ParkedVehicleCondition") == null, "Release did not wait for host approval.");
                    return sent;
                };
                Func<List<IMessage>, VehicleConditionReleaseAck> approve = outbound =>
                {
                    role(true); var peers = (IDictionary)Get(session, "_playersByPeer"); var driver = (RemotePlayer)peers[new PeerId(1001)];
                    driver.Position = f.Body.transform.position; driver.LastTransformTime = Time.unscaledTime + .1f;
                    foreach (var message in outbound) Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(message), Channel.ReliableOrdered);
                    VehicleConditionReleaseAck? result = null; int acknowledgments = 0;
                    foreach (var packet in capture.Packets) if (packet.Message is VehicleConditionReleaseAck ack)
                    {
                        acknowledgments++; Require(packet.Peer == new PeerId(1001) && packet.Channel == Channel.ReliableOrdered, "Confirmation was not targeted reliably to its sender."); result = ack;
                    }
                    Require(acknowledgments == 1 && result != null && Get(host, "ParkedVehicleCondition") != null,
                        "Host failed to confirm its accepted guest release exactly once."); return result!;
                };
                Func<VehicleConditionReleaseAck> round = () => { var outbound = release(63); var ack = approve(outbound); role(false); return ack; };

                for (int i = 0; i < 4; i++)
                {
                    int wheel = i;
                    check("condition release: wheel " + wheel + " keeps host-approved health on the actual former driver", () =>
                    {
                        clean(); var ack = round(); f.Tick(); Require(f.Health[wheel].Value == 90 + wheel, "Unconfirmed release was prematurely approved.");
                        deliver(ack); f.Tick(); f.Tick(); Require(f.Health[wheel].Value == 30 + wheel
                            && Get(guest, "PendingConditionRelease") == null && Get(guest, "ParkedVehicleCondition") != null,
                            "Former driver lost confirmed parked health."); saved();
                    });
                }
                check("condition release: confirmation applies condition without replaying the final pose", () =>
                {
                    clean(); var ack = round(); f.Body.transform.position = new Vector3(40, 2, 3); var pose = f.Body.transform.position;
                    try { deliver(ack); Require(f.Body.transform.position == pose && !(bool)Get(guest, "LocallyOwned"), "Confirmation moved or claimed the car."); saved(); }
                    finally { f.Body.transform.position = Vector3.zero; }
                });
                check("condition release: host snapshots and checksums retain approved condition after native scratch drift", () =>
                {
                    clean(); var ack = approve(release(63)); f.Tick(); Require(f.Health[0].Value == 90, "Host fixture did not exercise native scratch drift.");
                    var snapshot = (VehicleCondition)Call(vehicles, "TryBuildConditionSnapshot", host, (byte)0)!;
                    var checksum = (VehicleCondition)Call(vehicles, "TryReadConditionForChecksum", host)!;
                    Require(snapshot.HealthFL == 30 && checksum.HealthFL == 30 && snapshot.Sequence == 65535 && snapshot.OwnerPlayerId == 0,
                        "Host resync/checksum undid its approved parked result.");
                    snapshot.HealthFL = 1; Require(((VehicleCondition)Get(host, "ParkedVehicleCondition")).HealthFL == 30, "Snapshot aliased parked state.");
                    role(false); deliver(ack); f.Tick(); var local = (VehicleCondition)Call(vehicles, "TryReadConditionForChecksum", guest)!;
                    Require(local.HealthFL == checksum.HealthFL && local.Availability == checksum.Availability, "Former driver disagreed with the host condition checksum."); saved();
                });
                check("condition release: a fresh join receives the approved parked snapshot", () =>
                {
                    clean(); approve(release(63)); var snapshot = (VehicleCondition)Call(vehicles, "TryBuildConditionSnapshot", host, (byte)0)!;
                    guest = fresh(); role(false); f.Seed(); deliver(snapshot); f.Tick();
                    Require(f.Health[0].Value == 30 && Get(guest, "AcceptedVehicleCondition") != null, "Late join borrowed host scratch instead of the approved snapshot."); saved();
                });
                check("condition release: duplicate and delayed confirmation cannot undo a newer host correction", () =>
                {
                    clean(); var ack = round(); deliver(ack); var parked = Get(guest, "ParkedVehicleCondition"); deliver(ack);
                    Require(ReferenceEquals(parked, Get(guest, "ParkedVehicleCondition")), "Duplicate approval reapplied the release.");
                    var newer = initial(63); newer.HealthFL = 40; deliver(newer); deliver(ack); f.Tick();
                    Require(f.Health[0].Value == 40 && Get(guest, "ParkedVehicleCondition") == null, "Old approval undid a host correction."); saved();
                });
                foreach (string kind in new[] { "release sequence", "condition sequence", "owner", "peer", "channel" })
                {
                    string changed = kind;
                    check("condition release: wrong " + changed + " cannot approve a pending release", () =>
                    {
                        clean(); var ack = round(); var invalid = ack.Copy(); var peer = new PeerId(999); var channel = Channel.ReliableOrdered;
                        if (changed == "release sequence") invalid.ReleaseSequence++;
                        else if (changed == "condition sequence") invalid.Condition.Sequence++;
                        else if (changed == "owner") invalid.Condition.OwnerPlayerId = 2;
                        else if (changed == "peer") peer = new PeerId(1002);
                        else channel = Channel.UnreliableSequenced;
                        Call(session, "OnPacketReceived", peer, PacketCodec.Encode(invalid), channel); f.Tick();
                        Require(Get(guest, "PendingConditionRelease") != null && Get(guest, "ParkedVehicleCondition") == null && f.Health[0].Value == 90,
                            "Invalid confirmation became approved parked state."); deliver(ack); f.Tick(); Require(f.Health[0].Value == 30, "Valid confirmation failed after rejection."); saved();
                    });
                }
                check("condition release: new local claim retires an older unconfirmed release", () =>
                {
                    clean(); var ack = round(); player.position = f.Body.transform.position;
                    Require((bool)Call(itemsObject, "TryClaimForInteraction", session, guest)!, "Next claim failed."); deliver(ack); f.Tick();
                    Require(Get(guest, "PendingConditionRelease") == null && Get(guest, "ParkedVehicleCondition") == null && f.Health[0].Value == 90,
                        "Prior release escaped into a new lease."); saved();
                });
                check("condition release: remote motion supersedes pending approval before condition arrives", () =>
                {
                    clean(); var ack = round(); deliver(new ItemTransform { ItemId = VehicleId, OwnerPlayerId = 0, Sequence = 20, Flags = ItemTransform.FlagVehicle });
                    deliver(ack); Require(Get(guest, "PendingConditionRelease") == null && Get(guest, "ParkedVehicleCondition") == null,
                        "Prior approval outlived a competing motion stream."); saved();
                });
                check("condition release: replacement body and teardown cannot inherit pending approval", () =>
                {
                    clean(); var ack = round(); Set(guest, "Body", Get(original, "Body")); deliver(ack); Set(guest, "Body", f.Body); deliver(ack);
                    Require(Get(guest, "ParkedVehicleCondition") == null && Get(guest, "PendingConditionRelease") == null, "Body replacement kept pending approval.");
                    clean(); ack = round(); Call(vehicles, "ClearConditionStreams"); deliver(ack);
                    Require(Get(guest, "ParkedVehicleCondition") == null && Get(guest, "PendingConditionRelease") == null, "Teardown retained pending approval."); saved();
                });
                check("condition release: approval arriving before local release waits without overwriting the driver", () =>
                {
                    clean(); var ack = round(); Set(guest, "LocallyOwned", true); f.Health[0].Value = 77; deliver(ack);
                    Require(f.Health[0].Value == 77 && Get(guest, "ParkedVehicleCondition") == null && Get(guest, "ApprovedConditionRelease") != null,
                        "Early approval wrote over a current driver."); Set(guest, "LocallyOwned", false); Call(vehicles, "CompleteLocalConditionRelease", guest); f.Tick();
                    Require(f.Health[0].Value == 30 && Get(guest, "ParkedVehicleCondition") != null, "Deferred approval did not complete on release."); saved();
                });
                check("condition release: missing native bindings reconcile after approval without another packet", () =>
                {
                    clean(); var ack = round(); var wheel = f.Wheels[0]; var wheelParent = wheel.transform.parent; wheel.transform.SetParent(null, false);
                    try { deliver(ack); Require(Get(guest, "ParkedVehicleCondition") != null, "Missing native binding lost accepted approval."); }
                    finally { wheel.transform.SetParent(wheelParent, false); }
                    Set(guest, "NextConditionProbeAt", 0f); Call(vehicles, "UpdateVehicleCondition", session); f.Tick();
                    Require(f.Health[0].Value == 30, "Late native binding did not consume approved condition."); saved();
                });
                check("condition release: host never acknowledges duplicate finals or guest-sent confirmations", () =>
                {
                    clean(); var outbound = release(63); var ack = approve(outbound); capture.Packets.Clear();
                    foreach (var msg in outbound) if (msg is ItemTransform pose && pose.IsFinal)
                        Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(pose), Channel.ReliableOrdered);
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(ack), Channel.ReliableOrdered);
                    Require(capture.Packets.Count == 0, "Duplicate final or forged confirmation caused host output."); saved();
                });
                check("condition release: rejected final pose cannot produce host confirmation", () =>
                {
                    clean(); var outbound = release(63); role(true); var driver = (RemotePlayer)((IDictionary)Get(session, "_playersByPeer"))[new PeerId(1001)];
                    driver.Position = f.Body.transform.position; driver.LastTransformTime = Time.unscaledTime + .1f;
                    foreach (var msg in outbound)
                    {
                        if (msg is ItemTransform pose && pose.IsFinal) pose.Position = new NetVector3(float.NaN, 0, 0);
                        Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(msg), Channel.ReliableOrdered);
                    }
                    foreach (var sent in capture.Packets) Require(!(sent.Message is VehicleConditionReleaseAck), "Rejected pose was acknowledged."); saved();
                });
                check("condition release: withdrawn saved-part availability survives release and snapshots", () =>
                {
                    clean(); var ack = approve(release(0)); Require(ack.Condition.Availability == 1, "Delegated native pressure or unavailable saved inputs changed.");
                    var snapshot = (VehicleCondition)Call(vehicles, "TryBuildConditionSnapshot", host, (byte)0)!; Require(snapshot.Availability == 1, "Snapshot invented parked health.");
                    role(false); deliver(ack); f.Tick(); Require(f.Health[0].Value == 90 && ((VehicleCondition)Get(guest, "ParkedVehicleCondition")).Availability == 1,
                        "Unavailable health was invented on the former driver."); saved();
                });
            }
            finally { items[VehicleId] = original; player.SetParent(parent, false); player.position = position; reset(); }
        }
    }
}
