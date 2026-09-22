using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunClimateOwnershipChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var parserType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var parser = Activator.CreateInstance(parserType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../window-ice-probe.json")) }, null);
            var rows = (List<object>)((Dictionary<string, object>)parserType.GetMethod("ReadObject", Members).Invoke(parser, null))["fsms"];
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent; var position = player.position;
            using (var f = new WindowIceFixture(rows, "CORRIS/Simulation/CarTempCorris"))
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                CallStatic("EnsureClimateProbe", item); Set(item, "LoggedClimateApply", true); Set(item, "NextClimateDiagAt", float.MaxValue);
                var first = new PeerId(1001); var second = new PeerId(1002); var hostPeer = new PeerId(999);
                Action<bool, byte> mode = (host, owner) =>
                {
                    SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                    SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3); Set(item, "RemoteOwner", owner);
                    Set(item, "LocallyOwned", false); capture.Packets.Clear();
                };
                Action clean = () =>
                {
                    Call(vehicles, "ClearClimateStreams"); f.Seed(); mode(true, 1);
                    player.SetParent(parent, false); player.position = position;
                };
                Action<PeerId, VehicleClimate, Channel> packet = (peer, state, channel) =>
                    Call(session, "OnPacketReceived", peer, PacketCodec.Encode(state), channel);
                Func<byte, ushort, byte, VehicleClimate> state = (owner, sequence, rear) => new VehicleClimate {
                    VehicleId = VehicleId, OwnerPlayerId = owner, Sequence = sequence, Frost = 50, Fog = 60, CabinTemp = 70,
                    HeaterTemp = 80, HeaterBlower = 90, HeaterDirection = 100, Ice = 10, IceSideLeft = 20,
                    IceSideRight = 30, IceDoorLeft = 40, IceDoorRight = 50, IceRear = rear, IceMask = 63 };
                Action<byte> rearIs = value => Require((byte)Get(item, "RemoteIceRear") == value
                    && Mathf.Abs(f.Panes[5].Value - value / 255f) < .000001f, "Wrong native rear cutoff or accepted climate.");
                Func<VehicleClimate> accepted = () => (VehicleClimate)Get(item, "AcceptedVehicleClimate");

                check("climate ownership: parked host rejects nearby guest climate", () =>
                {
                    clean(); mode(true, 255); packet(first, state(1, 0, 200), Channel.UnreliableSequenced);
                    Require(capture.Packets.Count == 0 && accepted() == null, "Unowned guest climate was accepted or relayed."); f.Same(f.Initial);
                });
                check("climate ownership: first zero sequence from established guest reaches native panes and relay", () =>
                {
                    clean(); packet(first, state(1, 0, 200), Channel.UnreliableSequenced); rearIs(200);
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Peer == second && capture.Packets[0].Channel == Channel.UnreliableSequenced,
                        "Established climate was not relayed to the other guest.");
                });
                check("climate ownership: duplicate cannot change native panes refresh hold or relay", () =>
                {
                    capture.Packets.Clear(); Set(item, "RemoteClimateUntil", 12345f); packet(first, state(1, 0, 0), Channel.UnreliableSequenced);
                    rearIs(200); Require(capture.Packets.Count == 0 && (float)Get(item, "RemoteClimateUntil") == 12345f, "Duplicate refreshed or relayed climate.");
                });
                check("climate ownership: stale packet cannot undo accepted progress", () =>
                {
                    packet(first, state(1, 3, 180), Channel.UnreliableSequenced); capture.Packets.Clear();
                    packet(first, state(1, 2, 0), Channel.UnreliableSequenced); rearIs(180); Require(capture.Packets.Count == 0, "Stale packet was relayed.");
                });
                check("climate ownership: passenger and forged player id cannot replace the driver", () =>
                {
                    clean(); packet(first, state(1, 1, 170), Channel.UnreliableSequenced); capture.Packets.Clear();
                    packet(second, state(2, 9000, 0), Channel.UnreliableSequenced);
                    packet(first, state(2, 9000, 0), Channel.UnreliableSequenced); rearIs(170);
                    Require(capture.Packets.Count == 0, "Passenger or forged owner reached the relay.");
                });
                check("climate ownership: guest snapshot sentinel is never relayed", () =>
                {
                    packet(first, state(1, VehicleClimate.SnapshotSequence, 0), Channel.ReliableOrdered); rearIs(170);
                    Require(capture.Packets.Count == 0, "Guest bypassed dedup with snapshot sentinel.");
                });
                check("climate ownership: malformed flags do not consume the next sequence", () =>
                {
                    var invalid = state(1, 2, 0); invalid.Flags = 16; packet(first, invalid, Channel.UnreliableSequenced);
                    rearIs(170); Require(capture.Packets.Count == 0, "Invalid climate flags relayed.");
                    packet(first, state(1, 2, 160), Channel.UnreliableSequenced); rearIs(160);
                });
                check("climate ownership: reliable final retains its sequence and channel through the host", () =>
                {
                    capture.Packets.Clear(); packet(first, state(1, 3, 150), Channel.ReliableOrdered); rearIs(150);
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Channel == Channel.ReliableOrdered
                        && ((VehicleClimate)capture.Packets[0].Message).Sequence == 3, "Final climate was downgraded or rewritten.");
                });
                check("climate ownership: release stops old presentation and the host resumes publication", () =>
                {
                    mode(true, 255); f.Seed(); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.Same(f.Initial);
                    packet(first, state(1, 4, 0), Channel.UnreliableSequenced); f.Same(f.Initial); Require(capture.Packets.Count == 0, "Released owner still relayed.");
                    Set(item, "NextClimateAt", 0f); Call(vehicles, "UpdateVehicleClimate", session);
                    Require(capture.Packets.Count > 0, "Host did not resume parked climate.");
                    foreach (var p in capture.Packets) Require(p.Message is VehicleClimate c && c.OwnerPlayerId == 0, "Resumed source was not host.");
                });
                check("climate ownership: active remote owner prevents host climate publication", () =>
                { clean(); Call(vehicles, "UpdateVehicleClimate", session); Require(capture.Packets.Count == 0, "Host competed with delegated owner."); });
                check("climate ownership: unowned guest cannot publish from native ignition activity", () =>
                {
                    clean(); mode(false, 255); var engine = new FsmFloat(5000); Set(item, "EngineRevsVar", engine); Set(item, "SystemsReady", true);
                    Call(vehicles, "UpdateVehicleClimate", session); Require(capture.Packets.Count == 0, "Unowned guest promoted itself from native RPM.");
                });
                check("climate ownership: final publication bypasses timer and skips snapshot sentinel", () =>
                {
                    mode(false, 255); Set(item, "LocallyOwned", true); Set(item, "NextClimateAt", float.MaxValue); Set(item, "OutClimateSequence", (ushort)65534);
                    Call(vehicles, "SendFinalVehicleClimate", session, item);
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Channel == Channel.ReliableOrdered
                        && ((VehicleClimate)capture.Packets[0].Message).Sequence == 0, "Final sender used a timer or snapshot sentinel.");
                    f.Same(f.Initial);
                });
                check("climate ownership: host snapshot copies the current guest report without sampling divergent native climate", () =>
                {
                    clean(); var report = state(1, 44, 140); Require((bool)Call(vehicles, "OnRemoteVehicleClimate", report)!, "Guest source rejected.");
                    report.IceRear = 0; report.HeaterBlower = 0; f.Seed(); Set(item, "OutClimateSequence", (ushort)800);
                    var snapshot = (VehicleClimate)Call(vehicles, "TryBuildVehicleClimate", item)!;
                    Require(snapshot.IceRear == 140 && snapshot.HeaterBlower == 90 && snapshot.OwnerPlayerId == 0
                        && snapshot.Sequence == VehicleClimate.SnapshotSequence && (ushort)Get(item, "OutClimateSequence") == 800,
                        "Host repair sampled local drift or consumed send sequence.");
                    snapshot.IceRear = 0; Require(accepted().IceRear == 140 && accepted().Sequence == 44, "Snapshot mutated accepted report."); f.Same(f.Initial);
                });
                check("climate ownership: missing expired or former-owner state cannot become a host repair", () =>
                {
                    mode(true, 2); Require(Call(vehicles, "TryBuildVehicleClimate", item) == null, "Former owner became a new owner's snapshot.");
                    mode(true, 1); Set(item, "RemoteClimateUntil", -999f); Require(Call(vehicles, "TryBuildVehicleClimate", item) == null, "Expired climate was snapshotted.");
                    Call(vehicles, "ClearClimateStreams"); Require(Call(vehicles, "TryBuildVehicleClimate", item) == null, "Missing climate borrowed native state.");
                });
                check("climate ownership: driver handoffs retain independent returning-owner history", () =>
                {
                    clean(); packet(first, state(1, 50, 130), Channel.UnreliableSequenced); mode(true, 2);
                    packet(second, state(2, 0, 120), Channel.UnreliableSequenced); rearIs(120); capture.Packets.Clear();
                    packet(first, state(1, 9999, 0), Channel.UnreliableSequenced); rearIs(120); Require(capture.Packets.Count == 0, "Old driver relayed after handoff.");
                    mode(true, 1); packet(first, state(1, 49, 0), Channel.UnreliableSequenced); rearIs(120);
                    packet(first, state(1, 51, 110), Channel.UnreliableSequenced); rearIs(110);
                });
                check("climate ownership: local and seated pre-claim drivers reject remote writes", () =>
                {
                    clean(); packet(first, state(1, 1, 100), Channel.UnreliableSequenced); capture.Packets.Clear(); Set(item, "LocallyOwned", true); f.Seed();
                    packet(first, state(1, 2, 0), Channel.UnreliableSequenced); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.Same(f.Initial);
                    Set(item, "LocallyOwned", false); player.SetParent(f.Body.transform, false);
                    packet(first, state(1, 2, 0), Channel.UnreliableSequenced); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.Same(f.Initial);
                    Require(capture.Packets.Count == 0, "Local driver protection still relayed remote writes."); player.SetParent(parent, false); player.position = position;
                });
                check("climate ownership: guest accepts only the selected host relay for its current owner", () =>
                {
                    clean(); mode(false, 1); packet(first, state(1, 1, 0), Channel.UnreliableSequenced); Require(accepted() == null, "Guest accepted direct peer climate.");
                    packet(hostPeer, state(1, 1, 90), Channel.UnreliableSequenced); rearIs(90);
                    packet(hostPeer, state(2, 1, 0), Channel.UnreliableSequenced); rearIs(90);
                });
                check("climate ownership: guest releases the previous owner hold immediately at handoff", () =>
                {
                    mode(false, 2); f.Seed(); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.Same(f.Initial);
                    packet(hostPeer, state(2, 0, 80), Channel.UnreliableSequenced); rearIs(80);
                });
                check("climate ownership: late-join snapshot initializes parked climate without consuming live sequence", () =>
                {
                    clean(); mode(false, 255); packet(hostPeer, state(0, VehicleClimate.SnapshotSequence, 70), Channel.ReliableOrdered); rearIs(70);
                    packet(hostPeer, state(0, 0, 60), Channel.UnreliableSequenced); rearIs(60);
                });
                check("climate ownership: host repair does not overwrite an active guest driver stream", () =>
                {
                    mode(false, 1); packet(hostPeer, state(1, 1, 50), Channel.UnreliableSequenced); rearIs(50);
                    packet(hostPeer, state(0, VehicleClimate.SnapshotSequence, 0), Channel.ReliableOrdered); rearIs(50);
                });
                check("climate ownership: player readmission clears history and presentation for that sender", () =>
                {
                    clean(); packet(first, state(1, 400, 40), Channel.UnreliableSequenced);
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); Require(accepted() == null && (float)Get(item, "RemoteClimateUntil") < 0, "Readmission retained old climate.");
                    packet(first, state(1, 0, 30), Channel.UnreliableSequenced); rearIs(30);
                });
                check("climate ownership: session teardown stops holds resets counters and rejects disconnected delivery", () =>
                {
                    Call(Get(vehicles, "_items"), "ReleaseSession"); Require(accepted() == null && (float)Get(item, "RemoteClimateUntil") < 0
                        && (ushort)Get(item, "OutClimateSequence") == 0, "Session release retained climate state.");
                    mode(false, 255); SetProperty(session, "State", SessionState.Idle); f.Seed();
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleClimate", state(0, 0, 0))!, "Disconnected world accepted climate."); f.Same(f.Initial);
                    mode(false, 255); packet(hostPeer, state(0, 0, 20), Channel.UnreliableSequenced); rearIs(20);
                });
            }
            finally
            {
                player.SetParent(parent, false); player.position = position;
                items[VehicleId] = original; reset();
            }
        }
    }
}
