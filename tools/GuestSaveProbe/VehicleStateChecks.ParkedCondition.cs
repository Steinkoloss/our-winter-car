using System;
using System.Collections;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunParkedConditionChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var itemsObject = Get(vehicles, "_items"); var items = (IDictionary)Get(itemsObject, "_items");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            using (var f = new WheelHealthFixture())
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                CallStatic("EnsureConditionProbe", item);
                Action clean = () =>
                {
                    reset(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected); SetProperty(session, "LocalPlayerId", (byte)3);
                    Set(item, "Body", f.Body); Set(item, "RemoteOwner", (byte)255); Set(item, "LocallyOwned", false);
                    Set(item, "LastRemoteSequence", (ushort)0); Set(item, "LastRemoteSequenceOwner", (byte)255);
                    Set(item, "RemoteIsDriver", false); Set(item, "RemoteVehicleStream", false); Set(item, "LastRemoteAt", -999f);
                    Set(item, "OriginalKinematic", true); Set(item, "KinematicSaved", true); Set(item, "RemoteEngineUntil", -999f);
                    Set(item, "LastPosition", Vector3.zero); Set(item, "LastMovedAt", -999f); Set(item, "LocalDriveActive", false);
                    f.Body.isKinematic = true; f.Body.transform.position = Vector3.zero;
                    Call(vehicles, "ClearConditionStreams"); f.Seed(); capture.Packets.Clear();
                };
                Func<byte, ushort, bool, ItemTransform> motion = (owner, sequence, final) => new ItemTransform {
                    ItemId = VehicleId, OwnerPlayerId = owner, Sequence = sequence, Position = new NetVector3(0, 0, 0),
                    Flags = final ? ItemTransform.FlagFinal : (byte)(ItemTransform.FlagVehicle | ItemTransform.FlagDriver) };
                Func<byte, ushort, byte, VehicleCondition> condition = (owner, sequence, health) => new VehicleCondition { Availability = VehicleCondition.AvailableAll,
                    VehicleId = VehicleId, OwnerPlayerId = owner, Sequence = sequence, TirePressure = 190,
                    HealthFL = health, HealthFR = (byte)(health + 1), HealthRL = (byte)(health + 2), HealthRR = (byte)(health + 3) };
                Action<IMessage> packet = message => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Func<VehicleCondition?> parked = () => (VehicleCondition?)Get(item, "ParkedVehicleCondition");
                Action park = () => { packet(motion(1, 10, false)); packet(condition(1, 0, 30)); packet(motion(1, 11, true)); };
                Action saved = () => { for (int i = 0; i < 4; i++) Require(f.Saved[i].Value == 90 + i, "Parking modified a saved tyre."); };

                for (int index = 0; index < 4; index++)
                {
                    int wheel = index;
                    check("parked condition: wheel " + wheel + " retains accepted health after the real final pose releases ownership", () =>
                    {
                        clean(); park(); f.Tick(); f.Tick();
                        Require((byte)Get(item, "RemoteOwner") == 255 && parked()?.OwnerPlayerId == 1
                            && f.Health[wheel].Value == 30 + wheel, "Final pose lost shared parked health or claimed host ownership."); saved();
                    });
                }
                check("parked condition: final puncture remains flat and known zero survives native reads", () =>
                {
                    clean(); packet(motion(1, 10, false)); var state = condition(1, 0, 0); state.Flags = 1; packet(state);
                    packet(motion(1, 11, true)); f.Tick(); Require(f.Wheels[0].ActiveStateName == "Flat friction" && f.Health[0].Value == 0,
                        "Parking restored the guest's original healthy tyre."); saved();
                });
                check("parked condition: retained record is copied and survives former-driver cleanup", () =>
                {
                    clean(); park(); var accepted = (VehicleCondition)Get(item, "AcceptedVehicleCondition"); accepted.HealthFL = 1;
                    Require(parked() != accepted && parked()?.HealthFL == 30, "Parked record aliases the live stream.");
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); Call(itemsObject, "ForgetPlayerItemSequences", (byte)1); f.Tick();
                    Require(Get(item, "AcceptedVehicleCondition") == null && f.Health[0].Value == 30 && parked() != null,
                        "Departure/readmission erased an approved world result."); saved();
                });
                check("parked condition: a new host snapshot supersedes and retires the retained record", () =>
                {
                    clean(); park(); packet(condition(0, 65535, 50)); f.Tick();
                    Require(parked() == null && Get(item, "ParkedConditionBody") == null && f.Health[0].Value == 50,
                        "Parked carry overruled a host correction."); saved();
                });
                foreach (byte owner in new byte[] { 1, 2 })
                    check("parked condition: new lease by player " + owner + " clears old state before accepting fresh condition", () =>
                    {
                        clean(); park(); packet(motion(owner, 12, false)); f.Tick();
                        Require(parked() == null && Get(item, "AcceptedVehicleCondition") == null && f.Health[0].Value == 90,
                            "New lease reused the previous condition baseline.");
                        packet(condition(owner, 1, 40)); f.Tick(); Require(f.Health[0].Value == 40, "New lease did not take over shared health."); saved();
                    });
                check("parked condition: unowned guest reports and forged releases cannot replace approved health", () =>
                {
                    clean(); park(); packet(condition(1, 1, 0));
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(motion(2, 40, false)), Channel.ReliableOrdered);
                    f.Tick(); Require((byte)Get(item, "RemoteOwner") == 255 && f.Health[0].Value == 30 && parked()?.OwnerPlayerId == 1,
                        "Parked retention admitted an unauthorized report or claim."); saved();
                });
                check("parked condition: duplicate final and unowned resync pose do not erase approved health", () =>
                {
                    clean(); park(); packet(motion(1, 11, true)); packet(motion(0, 20, true)); f.Tick();
                    Require(f.Health[0].Value == 30 && parked()?.OwnerPlayerId == 1, "A pose-only final erased approved condition."); saved();
                });
                check("parked condition: invalid-pose rejection preserves the approved parked result", () =>
                {
                    clean(); park(); var bad = motion(2, 12, false); bad.Position = new NetVector3(float.NaN, 0, 0); packet(bad); f.Tick();
                    Require(f.Health[0].Value == 30 && parked() != null && (byte)Get(item, "RemoteOwner") == 255,
                        "Rejected pose cleared parked condition."); saved();
                });
                check("parked condition: a timeout without an explicit final cannot approve a parked record", () =>
                {
                    clean(); packet(motion(1, 10, false)); packet(condition(1, 0, 30)); Set(item, "LastRemoteAt", -999f);
                    Call(itemsObject, "UpdateItems", session); f.Tick();
                    Require((byte)Get(item, "RemoteOwner") == 255 && parked() == null && f.Health[0].Value == 90,
                        "A quiet stream was treated as an approved release."); saved();
                });
                check("parked condition: an unowned final cannot approve an old cached driver's condition", () =>
                {
                    clean(); packet(motion(1, 10, false)); packet(condition(1, 0, 30)); Set(item, "RemoteOwner", (byte)255);
                    packet(motion(1, 11, true)); f.Tick(); Require(parked() == null && f.Health[0].Value == 90,
                        "An unowned final promoted a stale driver's cache."); saved();
                });
                check("parked condition: local claim retires the carry and forces a fresh unchanged-value report", () =>
                {
                    clean(); park(); Set(item, "OutConditionSequence", (ushort)400); Set(item, "HasSentCondition", true);
                    Set(item, "LastCondPressure", (byte)190); Set(item, "LastCondDrivetrain", (byte)0); Set(item, "LastCondFlags", (byte)0);
                    Set(item, "LastCondHFL", (byte)30); Set(item, "LastCondHFR", (byte)31); Set(item, "LastCondHRL", (byte)32); Set(item, "LastCondHRR", (byte)33);
                    Set(item, "NextConditionTickAt", 1e10f); Set(item, "NextConditionKeepAliveAt", 1e10f);
                    player.position = f.Body.transform.position;
                    Require((bool)Call(itemsObject, "TryClaimForInteraction", session, item)!, "Actual local claim failed.");
                    Require(parked() == null && Get(item, "AcceptedVehicleCondition") == null && !(bool)Get(item, "HasSentCondition"),
                        "Local claim kept stale incoming condition or its delta gate."); f.Tick(); capture.Packets.Clear();
                    Call(vehicles, "UpdateVehicleCondition", session);
                    int sent = 0; foreach (var sentPacket in capture.Packets)
                        if (sentPacket.Message is VehicleCondition next)
                        { sent++; Require(next.OwnerPlayerId == 3 && next.Sequence == 401 && next.HealthFL == 30, "New lease reset history or lost its approved health input."); }
                    Require(sent == 1, "Unchanged new-lease baseline was suppressed."); saved();
                });
                check("parked condition: actual local release packets retain health when replayed to an observer", () =>
                {
                    clean(); SetProperty(session, "LocalPlayerId", (byte)1); player.position = f.Body.transform.position;
                    Require((bool)Call(itemsObject, "TryClaimForInteraction", session, item)!, "Source could not claim the car.");
                    ItemTransform? claim = null;
                    foreach (var sentPacket in capture.Packets) if (sentPacket.Message is ItemTransform pose) claim = pose;
                    Require(claim != null && !claim.IsFinal && claim.IsVehicle, "Source did not emit its actual vehicle claim.");
                    for (int i = 0; i < 4; i++) f.Health[i].Value = 30 + i;
                    Set(item, "LastMovedAt", -999f); capture.Packets.Clear(); Call(itemsObject, "UpdateItems", session);
                    VehicleCondition? finalCondition = null; ItemTransform? finalPose = null;
                    foreach (var sentPacket in capture.Packets)
                    {
                        if (sentPacket.Message is VehicleCondition state)
                        { Require(finalPose == null, "Final condition followed release."); finalCondition = state; }
                        if (sentPacket.Message is ItemTransform pose && pose.IsFinal)
                        { Require(finalCondition != null, "Release preceded its condition."); finalPose = pose; }
                    }
                    Require(finalPose != null && !finalPose.IsVehicle && !finalPose.IsDriver && !(bool)Get(item, "LocallyOwned"),
                        "Actual settled release was not exercised.");
                    clean(); packet(claim!); packet(finalCondition!); packet(finalPose!); f.Tick();
                    Require((byte)Get(item, "RemoteOwner") == 255 && parked()?.OwnerPlayerId == 1 && f.Health[0].Value == 30,
                        "Actual sender packets did not retain parked health."); saved();
                });
                check("parked condition: body replacement and unregistration cannot inherit the old carry", () =>
                {
                    clean(); park(); Set(item, "Body", Get(original, "Body")); f.Tick();
                    Require(f.Health[0].Value == 90, "Parked record followed a different live body."); Set(item, "Body", f.Body);
                    items.Remove(VehicleId); f.Tick(); Require(f.Health[0].Value == 90, "Unregistered body retained parked input."); items[VehicleId] = item; saved();
                });
                check("parked condition: disconnect excludes the carry and session clear retires it", () =>
                {
                    clean(); park(); SetProperty(session, "State", SessionState.Idle); f.Tick(); Require(f.Health[0].Value == 90, "Disconnected reader retained parked input.");
                    Call(vehicles, "ClearConditionStreams"); SetProperty(session, "State", SessionState.Connected); f.Tick();
                    Require(parked() == null && Get(item, "ParkedConditionBody") == null && f.Health[0].Value == 90,
                        "Session teardown retained the parked condition."); saved();
                });
            }
            finally { items[VehicleId] = original; reset(); }
        }
    }
}
