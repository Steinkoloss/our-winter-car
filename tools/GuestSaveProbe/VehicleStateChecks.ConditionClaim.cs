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
        private static void RunConditionClaimChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var itemsObject = Get(vehicles, "_items"); var items = (IDictionary)Get(itemsObject, "_items");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position;
            using (var f = new WheelHealthFixture())
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                Action clean = () =>
                {
                    reset(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected); SetProperty(session, "LocalPlayerId", (byte)3);
                    Set(item, "RemoteOwner", (byte)255); Set(item, "LocallyOwned", false); Set(item, "Body", f.Body);
                    Set(item, "LastRemoteSequenceOwner", (byte)255); Set(item, "LastRemoteSequence", (ushort)0); Set(item, "LastRemoteAt", -999f);
                    Set(item, "RemoteVehicleStream", false); Set(item, "RemoteIsDriver", false); Set(item, "LocalDriveActive", false);
                    Call(vehicles, "ClearConditionStreams"); player.SetParent(parent, false); player.position = position; f.Seed(); capture.Packets.Clear();
                };
                Func<ushort, byte, VehicleCondition> state = (sequence, available) => new VehicleCondition {
                    VehicleId = VehicleId, OwnerPlayerId = 0, Sequence = sequence, Availability = available,
                    TirePressure = 190, HealthFL = 30, HealthFR = 31, HealthRL = 32, HealthRR = 33 };
                Action<IMessage> packet = message => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Action claim = () =>
                {
                    player.position = f.Body.transform.position;
                    Require((bool)Call(itemsObject, "TryClaimForInteraction", session, item)!, "Actual physics claim failed.");
                    Require((bool)Get(item, "LocallyOwned"), "Claim did not establish local ownership.");
                };
                Action saved = () => { for (int i = 0; i < 4; i++) Require(f.Saved[i].Value == 90 + i, "Claim input changed a saved tyre."); };
                Func<VehicleCondition?> retained = () => (VehicleCondition?)Get(item, "ClaimedVehicleCondition");
                Func<VehicleCondition> read = () => (VehicleCondition)CallStatic("TryReadConditionState", item)!;
                Action park = () =>
                {
                    Set(item, "RemoteOwner", (byte)1); var input = state(0, 63); input.OwnerPlayerId = 1; packet(input);
                    packet(new ItemTransform { ItemId = VehicleId, OwnerPlayerId = 1, Sequence = 10, Flags = ItemTransform.FlagFinal });
                    Require(Get(item, "ParkedVehicleCondition") != null && (byte)Get(item, "RemoteOwner") == 255, "Final did not approve parked input.");
                };

                for (int i = 0; i < 4; i++)
                {
                    int wheel = i;
                    check("condition claim: wheel " + wheel + " retains accepted host health through native driver ticks", () =>
                    {
                        clean(); packet(state(65535, 63)); claim(); f.Tick(); f.Tick();
                        Require(f.Health[wheel].Value == 30 + wheel && retained()?.HealthFL == 30,
                            "Native driver read reverted to its local save after claiming."); saved();
                    });
                }
                check("condition claim: a seated guest captures the previous push owner before takeover clears it", () =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)1); Set(item, "LastRemoteAt", Time.unscaledTime); Set(item, "RemoteVehicleStream", true);
                    var input = state(0, 63); input.OwnerPlayerId = 1; packet(input); player.SetParent(f.Body.transform, false);
                    Call(itemsObject, "UpdateItems", session); f.Tick();
                    Require((bool)Get(item, "LocallyOwned") && (byte)Get(item, "RemoteOwner") == 255
                        && retained()?.OwnerPlayerId == 1 && f.Health[0].Value == 30, "Takeover lost its old owner's accepted inputs."); saved();
                });
                check("condition claim: approved parked state seeds a copied input and survives former-driver cleanup", () =>
                {
                    clean(); park(); var old = (VehicleCondition)Get(item, "ParkedVehicleCondition"); claim(); old.HealthFL = 1;
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); f.Tick();
                    Require(retained() != old && f.Health[0].Value == 30 && Get(item, "ParkedVehicleCondition") == null,
                        "Claim aliased or lost the previously approved result."); saved();
                });
                check("condition claim: first outgoing report before a reader tick uses retained health and preserves sequences", () =>
                {
                    clean(); packet(state(65535, 63)); Set(item, "OutConditionSequence", (ushort)400); claim();
                    f.Health[0].Value = 90; capture.Packets.Clear(); Call(vehicles, "UpdateVehicleCondition", session);
                    int reports = 0; foreach (var sent in capture.Packets) if (sent.Message is VehicleCondition value)
                    { reports++; Require(value.HealthFL == 30 && value.OwnerPlayerId == 3 && value.Sequence == 401, "First report leaked saved health or reset history."); }
                    Require(reports == 1 && retained()?.Sequence == 65535, "Readback changed retained snapshot metadata or failed publication."); saved();
                });
                check("condition claim: partial shared availability does not publish missing saved-part inputs", () =>
                {
                    clean(); packet(state(65535, VehicleCondition.AvailableFL)); claim(); f.Tick(); var value = read();
                    Require(f.Health[0].Value == 30 && f.Health[1].Value == 91 && value.HasWheel(0) && !value.HasWheel(1)
                        && value.HealthFR == 0 && value.HasPressure, "Partial input invented availability or withheld native pressure."); saved();
                });
                check("condition claim: explicit withdrawal overrides older approved parked input", () =>
                {
                    clean(); park(); packet(state(65535, 0)); claim(); f.Tick(); var value = read();
                    Require(retained()?.Availability == 0 && f.Health[0].Value == 90 && !value.HasWheel(0), "Withdrawn input revived an old parked result."); saved();
                });
                check("condition claim: rejected reports during ownership cannot replace the retained input", () =>
                {
                    clean(); packet(state(65535, 63)); claim(); var competing = state(1, 63); competing.HealthFL = 1; packet(competing);
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(competing), Channel.ReliableOrdered); f.Tick();
                    Require(f.Health[0].Value == 30 && retained()?.HealthFL == 30, "Rejected report changed the active claim."); saved();
                });
                check("condition claim: native source and warmed cache survive driver projection", () =>
                {
                    clean(); f.Tick(); var reader = NativeBagPartChecks.State(f.Wheels[0], "State 1").Actions[12];
                    var target = Get(reader, "gameObject"); var cache = Get(reader, "fsm"); var last = Get(reader, "goLastFrame");
                    packet(state(65535, 63)); claim(); f.Tick();
                    Require(f.Health[0].Value == 30 && ReferenceEquals(target, Get(reader, "gameObject"))
                        && ReferenceEquals(cache, Get(reader, "fsm")) && ReferenceEquals(last, Get(reader, "goLastFrame")), "Claim rewired a native source/cache."); saved();
                });
                check("condition claim: native readiness still gates retained input capture", () =>
                {
                    clean(); packet(state(65535, 63)); claim(); var wheel = f.Wheels[0]; wheel.enabled = false;
                    try { Require(!read().HasWheel(0) && read().HasWheel(1), "Retained input made an unavailable native reader ready."); }
                    finally { wheel.enabled = true; NativeBagPartChecks.Fire(wheel, "State 1"); }
                    f.Tick(); Require(read().HealthFL == 30, "Restored reader lost its claim input."); saved();
                });
                check("condition claim: final report precedes release and local release retires the input", () =>
                {
                    clean(); packet(state(65535, 63)); claim(); f.Health[0].Value = 90; Set(item, "LastMovedAt", -999f); capture.Packets.Clear();
                    Call(itemsObject, "UpdateItems", session); bool condition = false, final = false;
                    foreach (var sent in capture.Packets)
                    {
                        if (sent.Message is VehicleCondition value) { Require(!final && value.HealthFL == 30, "Final used saved health or followed release."); condition = true; }
                        if (sent.Message is ItemTransform motion && motion.IsFinal) { Require(condition, "Release lacked final condition."); final = true; }
                    }
                    Require(final && !(bool)Get(item, "LocallyOwned") && retained() == null, "Released lease retained driver input."); saved();
                });
                check("condition claim: accepted competing motion retires the input before the new owner's condition", () =>
                {
                    clean(); packet(state(65535, 63)); claim(); packet(new ItemTransform {
                        ItemId = VehicleId, OwnerPlayerId = 0, Sequence = 2, Flags = (byte)(ItemTransform.FlagVehicle | ItemTransform.FlagDriver) });
                    Require(!(bool)Get(item, "LocallyOwned") && retained() == null, "Remote winner retained the previous local input.");
                    var incoming = state(2, 63); incoming.HealthFL = 40; packet(incoming); f.Tick();
                    Require(f.Health[0].Value == 40, "Remote winner could not establish its input."); saved();
                });
                check("condition claim: replacement body and session reset permanently retire the input", () =>
                {
                    clean(); packet(state(65535, 63)); claim(); Set(item, "Body", Get(original, "Body")); read(); Set(item, "Body", f.Body); f.Tick();
                    Require(retained() == null && f.Health[0].Value == 90, "Replacement body resurrected the old claim input.");
                    clean(); packet(state(65535, 63)); claim(); Call(vehicles, "ClearConditionStreams"); f.Tick();
                    Require(retained() == null && f.Health[0].Value == 90, "Session reset retained driver input."); saved();
                });
                check("condition claim: different local player and disconnected session cannot read the retained input", () =>
                {
                    clean(); packet(state(65535, 63)); claim(); SetProperty(session, "LocalPlayerId", (byte)4); f.Tick();
                    Require(f.Health[0].Value == 90, "Another local player borrowed this claim."); SetProperty(session, "LocalPlayerId", (byte)3);
                    SetProperty(session, "State", SessionState.Idle); f.Tick(); Require(f.Health[0].Value == 90, "Disconnected session used retained input."); saved();
                });
                check("condition claim: hosts and claims without eligible input keep native lookup", () =>
                {
                    clean(); claim(); f.Tick(); Require(retained() == null && f.Health[0].Value == 90, "Missing shared input was fabricated.");
                    clean(); packet(state(65535, 63)); SetProperty(session, "IsHost", true); SetProperty(session, "State", SessionState.Hosting); SetProperty(session, "LocalPlayerId", (byte)0);
                    claim(); f.Tick(); Require(retained() == null && f.Health[0].Value == 90, "Host claim consumed a guest input copy."); saved();
                });
            }
            finally { items[VehicleId] = original; player.SetParent(parent, false); player.position = position; reset(); }
        }
    }
}
