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
        private static void RunConditionStreamChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var itemsObject = Get(vehicles, "_items"); var items = (IDictionary)Get(itemsObject, "_items");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position;
            using (var f = new ConditionStreamFixture())
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                CallStatic("EnsureConditionProbe", item);
                var first = new PeerId(1001); var second = new PeerId(1002); var hostPeer = new PeerId(999);
                Action<bool, byte> mode = (host, owner) =>
                {
                    SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                    SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3);
                    Set(item, "RemoteOwner", owner); Set(item, "LocallyOwned", false); capture.Packets.Clear();
                };
                Action clean = () =>
                {
                    Call(vehicles, "ClearConditionStreams"); f.Seed(); mode(true, 1);
                    player.SetParent(parent, false); player.position = position;
                };
                Func<byte, ushort, byte, VehicleCondition> state = (owner, seq, pressure) => new VehicleCondition { Availability = VehicleCondition.AvailableAll,
                    VehicleId = VehicleId, OwnerPlayerId = owner, Sequence = seq, TirePressure = pressure,
                    DrivetrainDamage = 2, HealthFL = 70, HealthFR = 71, HealthRL = 72, HealthRR = 73 };
                Action<PeerId, VehicleCondition> packet = (peer, message) => Call(session, "OnPacketReceived", peer, PacketCodec.Encode(message), Channel.ReliableOrdered);
                Func<VehicleCondition?> accepted = () => (VehicleCondition?)Get(item, "AcceptedVehicleCondition");
                Action<byte> pressureIs = value => Require(f.Pressure.Value == value && accepted()?.TirePressure == value, "Wrong native pressure or accepted condition.");

                check("condition stream: parked host rejects unowned guest reports before native writes and relay", () =>
                { clean(); mode(true, 255); packet(first, state(1, 0, 0)); Require(accepted() == null && capture.Packets.Count == 0, "Unowned claim accepted."); f.AssertSeed(); });
                check("condition stream: first zero sequence from current driver writes native scalars and relays once", () =>
                {
                    clean(); packet(first, state(1, 0, 200)); pressureIs(200);
                    Require(f.Damage.Value == 2 && f.Health[0].Value == 70 && f.Health[3].Value == 73, "Condition scalars were not applied.");
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Peer == second && capture.Packets[0].Channel == Channel.ReliableOrdered, "Accepted report was not relayed reliably.");
                });
                check("condition stream: duplicate zero cannot replay puncture or overwrite native state", () =>
                {
                    clean(); packet(first, state(1, 0, 200)); capture.Packets.Clear(); var duplicate = state(1, 0, 0); duplicate.Flags = 1;
                    packet(first, duplicate); pressureIs(200); Require(f.Wheels[0].ActiveStateName == "State 1" && capture.Packets.Count == 0, "Duplicate replayed a tyre event or relay.");
                });
                check("condition stream: stale and half-range reports cannot relay", () =>
                {
                    clean(); packet(first, state(1, 4, 200)); capture.Packets.Clear(); packet(first, state(1, 3, 0)); packet(first, state(1, 32772, 0));
                    pressureIs(200); Require(capture.Packets.Count == 0, "Stale condition relayed.");
                });
                check("condition stream: forged passenger and unknown peer cannot replace current driver", () =>
                {
                    clean(); packet(second, state(2, 1, 0)); packet(first, state(2, 1, 0)); packet(new PeerId(123456), state(1, 1, 0));
                    Require(accepted() == null && capture.Packets.Count == 0, "Non-driver condition reached native state."); f.AssertSeed();
                });
                check("condition stream: guest snapshot sentinel is rejected without consuming live history", () =>
                {
                    clean(); packet(first, state(1, VehicleCondition.SnapshotSequence, 0)); f.AssertSeed(); Require(capture.Packets.Count == 0, "Guest snapshot relayed.");
                    packet(first, state(1, 0, 200)); pressureIs(200);
                });
                check("condition stream: contradictory puncture and rim flags fail before any native event", () =>
                {
                    clean(); var invalid = state(1, 1, 0); invalid.Flags = 0x11; packet(first, invalid); f.AssertSeed(); Require(capture.Packets.Count == 0, "Invalid flags relayed.");
                    packet(first, state(1, 1, 200)); pressureIs(200);
                });
                check("condition stream: valid puncture reaches only the audited local transition after acceptance", () =>
                {
                    clean(); var report = state(1, 1, 200); report.Flags = 1; packet(first, report);
                    Require(f.Wheels[0].ActiveStateName == "Check rim" && f.Wheels[1].ActiveStateName == "State 1" && capture.Packets.Count == 1,
                        "Accepted event did not follow the native state graph.");
                });
                check("condition stream: host-owned and pre-claim local driver reject remote writes", () =>
                {
                    clean(); Set(item, "LocallyOwned", true); packet(first, state(1, 1, 0)); Set(item, "LocallyOwned", false);
                    player.SetParent(f.Body.transform, false); packet(first, state(1, 1, 0)); f.AssertSeed(); Require(capture.Packets.Count == 0, "Local driver allowed remote writes.");
                    player.SetParent(parent, false); player.position = position; packet(first, state(1, 1, 200)); pressureIs(200);
                });
                check("condition stream: returning drivers keep independent sequence histories", () =>
                {
                    clean(); packet(first, state(1, 50, 210)); mode(true, 2); packet(second, state(2, 0, 220)); capture.Packets.Clear();
                    packet(first, state(1, 9000, 0)); pressureIs(220); Require(capture.Packets.Count == 0, "Former driver relayed.");
                    mode(true, 1); packet(first, state(1, 49, 0)); pressureIs(220); packet(first, state(1, 51, 230)); pressureIs(230);
                });
                check("condition stream: release rejects the old driver without changing native state", () =>
                { clean(); packet(first, state(1, 10, 210)); mode(true, 255); packet(first, state(1, 11, 0)); pressureIs(210); Require(capture.Packets.Count == 0, "Released driver relayed."); });
                check("condition stream: guest accepts only selected-host reports for current ownership", () =>
                {
                    clean(); mode(false, 1); packet(first, state(1, 1, 0)); Require(accepted() == null, "Guest accepted a direct peer report.");
                    packet(hostPeer, state(1, 1, 200)); pressureIs(200); packet(hostPeer, state(2, 1, 0)); pressureIs(200);
                });
                check("condition stream: parked guest accepts host snapshot and first live zero independently", () =>
                {
                    clean(); mode(false, 255); packet(hostPeer, state(0, VehicleCondition.SnapshotSequence, 200)); pressureIs(200);
                    packet(hostPeer, state(0, 0, 210)); pressureIs(210); packet(hostPeer, state(0, VehicleCondition.SnapshotSequence, 220)); pressureIs(220);
                    packet(hostPeer, state(0, 0, 0)); pressureIs(220);
                });
                check("condition stream: host repair cannot overwrite an active guest driver's stream", () =>
                {
                    clean(); mode(false, 1); packet(hostPeer, state(1, 1, 200));
                    packet(hostPeer, state(0, VehicleCondition.SnapshotSequence, 0)); pressureIs(200);
                });
                check("condition stream: host snapshots copy current accepted state without sampling scratch or changing counters", () =>
                {
                    clean(); var report = state(1, 40, 210); Require((bool)Call(vehicles, "ApplyVehicleCondition", report)!, "Valid direct report rejected.");
                    report.TirePressure = 0; f.Seed(); Set(item, "OutConditionSequence", (ushort)800);
                    var copy = (VehicleCondition)Call(vehicles, "TryBuildConditionSnapshot", item, (byte)0)!;
                    Require(copy.TirePressure == 210 && copy.HealthRR == 73 && copy.OwnerPlayerId == 0 && copy.Sequence == VehicleCondition.SnapshotSequence
                        && (ushort)Get(item, "OutConditionSequence") == 800, "Snapshot sampled divergent scratch or advanced the live counter.");
                    copy.HealthRR = 0; Require(accepted()?.HealthRR == 73 && accepted()?.Sequence == 40, "Snapshot aliased accepted state."); f.AssertSeed();
                });
                check("condition stream: missing or former-owner state cannot become another driver's snapshot", () =>
                {
                    clean(); Require(Call(vehicles, "TryBuildConditionSnapshot", item, (byte)0) == null, "Missing owner borrowed host state.");
                    packet(first, state(1, 40, 210)); mode(true, 2); Require(Call(vehicles, "TryBuildConditionSnapshot", item, (byte)0) == null, "Former-owner report became current snapshot.");
                });
                check("condition stream: parked snapshots use native pressure and preserve live publication baselines", () =>
                {
                    clean(); mode(true, 255); Set(item, "OutConditionSequence", (ushort)800);
                    var copy = (VehicleCondition)Call(vehicles, "TryBuildConditionSnapshot", item, (byte)0)!;
                    Require(copy.TirePressure == 190 && copy.Sequence == VehicleCondition.SnapshotSequence && (ushort)Get(item, "OutConditionSequence") == 800
                        && !(bool)Get(item, "HasSentCondition"), "Parked snapshot changed live publication bookkeeping.");
                });
                check("condition stream: final condition bypasses timers and skips reserved sequence", () =>
                {
                    clean(); mode(false, 255); Set(item, "LocallyOwned", true); Call(vehicles, "UpdateVehicleCondition", session); capture.Packets.Clear();
                    Set(item, "NextConditionTickAt", float.MaxValue); Set(item, "NextConditionKeepAliveAt", float.MaxValue); Set(item, "OutConditionSequence", (ushort)65534);
                    Call(vehicles, "SendFinalVehicleCondition", session, item);
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Message is VehicleCondition s && s.Sequence == 0 && s.TirePressure == 190
                        && capture.Packets[0].Channel == Channel.ReliableOrdered, "Final condition was suppressed or used snapshot sentinel.");
                });
                check("condition stream: real item release sends final condition before its final pose", () =>
                {
                    clean(); mode(false, 255); Set(item, "LocallyOwned", true); Set(item, "LocalDriveActive", false); Set(item, "LastMovedAt", -999f);
                    Set(item, "LastPosition", f.Body.position); Set(item, "NextSystemsProbeAt", float.MaxValue); Set(item, "NextClimateProbeAt", float.MaxValue);
                    Call(itemsObject, "UpdateItems", session); int condition = -1, pose = -1;
                    for (int i = 0; i < capture.Packets.Count; i++)
                    {
                        var p = capture.Packets[i]; if (p.Message is VehicleCondition c && c.VehicleId == VehicleId) { condition = i; Require(p.Channel == Channel.ReliableOrdered, "Final condition is unreliable."); }
                        if (p.Message is ItemTransform t && t.ItemId == VehicleId && t.IsFinal) { pose = i; Require(p.Channel == Channel.ReliableOrdered, "Final pose is unreliable."); }
                    }
                    Require(condition >= 0 && pose > condition && !(bool)Get(item, "LocallyOwned"), "Release occurred before final condition.");
                });
                check("condition stream: readmission clears only departing sender's accepted state and history", () =>
                {
                    clean(); packet(first, state(1, 400, 200)); Call(vehicles, "ForgetVehicleStatePlayer", (byte)2); Require(accepted() != null, "Unrelated readmission cleared condition.");
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); Require(accepted() == null, "Readmission retained stale condition."); packet(first, state(1, 0, 210)); pressureIs(210);
                });
                check("condition stream: session reset clears accepted state and counters and blocks disconnected delivery", () =>
                {
                    clean(); packet(first, state(1, 400, 200)); Set(item, "OutConditionSequence", (ushort)500); Call(vehicles, "ClearVehicleStateStreams");
                    Require(accepted() == null && (ushort)Get(item, "OutConditionSequence") == 0, "Session clear retained condition history.");
                    SetProperty(session, "State", SessionState.Idle); Require(!(bool)Call(vehicles, "ApplyVehicleCondition", state(1, 0, 0))!, "Disconnected condition applied.");
                    mode(true, 1); packet(first, state(1, 0, 210)); pressureIs(210);
                });
            }
            finally { items[VehicleId] = original; player.SetParent(parent, false); player.position = position; reset(); }
        }

        private sealed class ConditionStreamFixture : IDisposable
        {
            internal readonly Rigidbody Body;
            internal readonly FsmFloat Pressure;
            internal readonly FsmInt Damage;
            internal readonly PlayMakerFSM[] Wheels = new PlayMakerFSM[4];
            internal readonly FsmFloat[] Health = new FsmFloat[4];
            internal ConditionStreamFixture()
            {
                var root = new GameObject("CORRIS"); Body = root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                var rows = ReadOccupancyRows("condition-stream-probe.json");
                foreach (Dictionary<string, object> row in rows)
                {
                    string path = (string)row["path"]; string name = path.Substring(path.LastIndexOf('/') + 1);
                    var child = new GameObject(name); child.transform.SetParent(root.transform, false);
                    var fsm = NativeBagPartChecks.MakeFsm(child, row); fsm.Fsm.Init(fsm); NativeBagPartChecks.Start(fsm);
                    if (name == "TirePressure") Pressure = fsm.FsmVariables.FindFsmFloat("Pressure");
                    else if (name == "GearboxDamage") Damage = fsm.FsmVariables.FindFsmInt("DamageType");
                    else
                    {
                        int index = Array.IndexOf(new[] { "WHEELc_FL", "WHEELc_FR", "WHEELc_RL", "WHEELc_RR" }, name);
                        Wheels[index] = fsm; Health[index] = fsm.FsmVariables.FindFsmFloat("Health");
                    }
                }
                if (Pressure == null || Damage == null) throw new InvalidOperationException("Missing audited pressure/gearbox variables.");
            }
            internal void Seed()
            {
                Pressure.Value = 190; Damage.Value = 0;
                for (int i = 0; i < 4; i++) { Health[i].Value = 90 + i; NativeBagPartChecks.Fire(Wheels[i], "State 1"); }
            }
            internal void AssertSeed()
            {
                Require(Pressure.Value == 190 && Damage.Value == 0, "Rejected report changed scalar state.");
                for (int i = 0; i < 4; i++) Require(Health[i].Value == 90 + i && Wheels[i].ActiveStateName == "State 1", "Rejected report changed wheel state.");
            }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(Body.gameObject); }
        }
    }
}
