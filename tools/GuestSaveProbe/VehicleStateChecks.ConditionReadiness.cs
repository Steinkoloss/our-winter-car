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
        private static void RunConditionReadinessChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            using (var f = new ConditionStreamFixture())
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                Action clean = () =>
                {
                    reset(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected); SetProperty(session, "LocalPlayerId", (byte)3);
                    Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "RemoteOwner", (byte)1); Set(item, "LocallyOwned", false);
                    for (int i = 0; i < 4; i++) { f.Wheels[i].transform.SetParent(f.Body.transform, false); f.Wheels[i].enabled = true; }
                    Call(vehicles, "ClearConditionStreams"); f.Seed(); capture.Packets.Clear();
                };
                Func<VehicleCondition> read = () => (VehicleCondition)CallStatic("TryReadConditionState", item)!;
                Action retry = () => { Set(item, "NextConditionProbeAt", 0f); Call(vehicles, "UpdateVehicleCondition", session); };
                Func<byte, ushort, VehicleCondition> state = (mask, sequence) => new VehicleCondition {
                    VehicleId = VehicleId, OwnerPlayerId = 1, Sequence = sequence, Availability = mask,
                    TirePressure = 230, DrivetrainDamage = 3, HealthFL = 40, HealthFR = 41, HealthRL = 42, HealthRR = 43 };
                Action<VehicleCondition> packet = message => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Func<VehicleCondition> accepted = () => (VehicleCondition)Get(item, "AcceptedVehicleCondition");
                Func<VehicleCondition> sent = () =>
                {
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Message is VehicleCondition, "Expected one outgoing condition report.");
                    return (VehicleCondition)capture.Packets[0].Message;
                };

                check("condition readiness: empty vehicle captures unavailable fields instead of known damage", () =>
                {
                    clean(); Set(item, "Body", Get(original, "Body")); var value = read();
                    Require(value.Availability == 0 && value.VehicleId == VehicleId && value.Flags == 0, "Empty discovery invented known condition.");
                });
                check("condition readiness: all canonical stable native inputs are explicitly known", () =>
                { clean(); var value = read(); Require(value.Availability == 63 && value.TirePressure == 190 && value.HealthRR == 93, "Complete native inputs were omitted."); });
                check("condition readiness: wheel startup and branch states cannot publish uninitialized zero", () =>
                {
                    clean();
                    foreach (Dictionary<string, object> row in ReadOccupancyRows("condition-stream-probe.json"))
                        if (((string)row["path"]).EndsWith("WHEELc_FL")) NativeBagPartChecks.LoadActions(f.Wheels[0], row, "State 2");
                    NativeBagPartChecks.Fire(f.Wheels[0], "State 2"); f.Health[0].Value = 0;
                    Require(f.Wheels[0].ActiveStateName == "State 2", "Native startup wait was not preserved by the fixture.");
                    NativeBagPartChecks.Fire(f.Wheels[1], "Probe idle"); var value = read();
                    Require(value.Availability == 51 && value.HealthFL == 0, "Startup health became known damage.");
                    NativeBagPartChecks.Fire(f.Wheels[0], "State 1"); value = read();
                    Require(value.HasWheel(0) && value.HealthFL == 0 && !value.HasWheel(1), "Known zero was confused with absent health.");
                });
                check("condition readiness: stable flat and rim states carry their own availability and flags", () =>
                {
                    clean(); NativeBagPartChecks.Fire(f.Wheels[0], "Flat friction"); NativeBagPartChecks.Fire(f.Wheels[1], "Rim friction");
                    f.Health[0].Value = 0; var value = read(); Require(value.Availability == 63 && value.Flags == 0x21 && value.HealthFL == 0, "Stable discrete condition lost its wheel bit.");
                });
                check("condition readiness: absent packet fields leave healthy native values and discrete state untouched", () =>
                {
                    clean(); var value = state(0, 0); value.Flags = 15; packet(value); f.AssertSeed();
                    Require(accepted().Availability == 0 && accepted().Sequence == 0, "Valid withdrawal was discarded."); retry(); f.AssertSeed();
                });
                check("condition readiness: one known wheel cannot overwrite another wheel pressure or gearbox", () =>
                {
                    clean(); var value = state(VehicleCondition.AvailableFL, 0); value.HealthFL = 0; value.Flags = VehicleCondition.FlagPunctureRR;
                    packet(value); Require(f.Health[0].Value == 0 && f.Health[1].Value == 91 && f.Health[3].Value == 93
                        && f.Pressure.Value == 190 && f.Damage.Value == 0 && f.Wheels[3].ActiveStateName == "State 1", "Partial packet changed an unavailable field.");
                });
                check("condition readiness: valid zero pressure and zero damage still replace known native scalars", () =>
                {
                    clean(); f.Damage.Value = 8; var value = state(3, 0); value.TirePressure = value.DrivetrainDamage = 0; packet(value);
                    Require(f.Pressure.Value == 0 && f.Damage.Value == 0 && f.Health[0].Value == 90, "Known scalar zero did not apply independently.");
                });
                check("condition readiness: late wheel discovers after pressure and publishes a mask-only change", () =>
                {
                    clean(); for (int i = 0; i < 4; i++) { f.Health[i].Value = 0; f.Wheels[i].transform.SetParent(null, false); }
                    Set(item, "LocallyOwned", true); Call(vehicles, "UpdateVehicleCondition", session);
                    Require(sent().Availability == 3 && sent().HealthFL == 0, "Early pressure discovery invented wheel condition.");
                    capture.Packets.Clear(); Set(item, "NextConditionKeepAliveAt", float.MaxValue); Set(item, "NextConditionTickAt", float.MaxValue);
                    f.Wheels[0].transform.SetParent(f.Body.transform, false); retry(); var value = sent();
                    Require(value.Availability == 7 && value.HealthFL == 0 && value.Sequence == 2, "Late discovery was frozen or mask-only publication suppressed.");
                });
                check("condition readiness: removed wheels withdraw availability immediately without waiting for rediscovery", () =>
                {
                    clean(); Set(item, "LocallyOwned", true); Call(vehicles, "UpdateVehicleCondition", session); capture.Packets.Clear();
                    Set(item, "NextConditionProbeAt", float.MaxValue); f.Wheels[0].transform.SetParent(null, false);
                    Call(vehicles, "UpdateVehicleCondition", session); Require(!sent().HasWheel(0) && sent().HasWheel(1), "Removed source remained known from cache.");
                });
                check("condition readiness: complete loss of bindings sends an explicit withdrawal", () =>
                {
                    clean(); Set(item, "LocallyOwned", true); Call(vehicles, "UpdateVehicleCondition", session); capture.Packets.Clear();
                    Set(item, "Body", Get(original, "Body")); Call(vehicles, "UpdateVehicleCondition", session);
                    Require(sent().Availability == 0 && sent().Sequence == 2, "No bindings silently retained the previous complete report.");
                });
                check("condition readiness: accepted state applies to a late wheel without another packet or new sequence", () =>
                {
                    clean(); f.Wheels[0].transform.SetParent(null, false); packet(state(63, 4)); Require(f.Health[0].Value == 90, "Unbound wheel was written.");
                    f.Wheels[0].transform.SetParent(f.Body.transform, false); Set(item, "NextConditionProbeAt", 0f); read();
                    Call(vehicles, "UpdateVehicleCondition", session);
                    Require(f.Health[0].Value == 40 && accepted().Sequence == 4 && capture.Packets.Count == 0
                        && Get(item, "ApplyingVehicleCondition") == null, "Capture consumed pending recovery or recovery changed stream history.");
                    packet(state(0, 4)); Require(accepted().Availability == 63, "Recovery reset duplicate protection.");
                });
                check("condition readiness: wheel leaving startup consumes accepted state on the next observer update", () =>
                {
                    clean(); NativeBagPartChecks.Fire(f.Wheels[0], "Probe idle"); packet(state(63, 1)); Require(f.Health[0].Value == 90, "Startup field was prematurely written.");
                    NativeBagPartChecks.Fire(f.Wheels[0], "State 1"); Call(vehicles, "UpdateVehicleCondition", session);
                    Require(f.Health[0].Value == 40, "Stable wheel did not consume pending accepted state.");
                });
                check("condition readiness: host also reconciles a late target from its accepted current driver", () =>
                {
                    clean(); SetProperty(session, "IsHost", true); SetProperty(session, "State", SessionState.Hosting); SetProperty(session, "LocalPlayerId", (byte)0);
                    f.Wheels[2].transform.SetParent(null, false); Require((bool)Call(vehicles, "ApplyVehicleCondition", state(63, 1))!, "Current driver rejected.");
                    f.Wheels[2].transform.SetParent(f.Body.transform, false); retry(); Require(f.Health[2].Value == 42 && capture.Packets.Count == 0, "Host recovery lost accepted driver state or relayed it again.");
                });
                check("condition readiness: changed owner and local claims block late replay", () =>
                {
                    clean(); f.Wheels[0].transform.SetParent(null, false); packet(state(63, 1)); f.Wheels[0].transform.SetParent(f.Body.transform, false);
                    Set(item, "RemoteOwner", (byte)2); retry(); Require(f.Health[0].Value == 90, "Former owner changed a new lease.");
                    Set(item, "RemoteOwner", (byte)1); Set(item, "LocallyOwned", true); retry(); Require(f.Health[0].Value == 90, "Local driver consumed observer recovery.");
                });
                check("condition readiness: partial parked result restores only its available wheel after late binding", () =>
                {
                    clean(); f.Wheels[0].transform.SetParent(null, false); packet(state(VehicleCondition.AvailableFL, 1));
                    Call(Get(vehicles, "_items"), "OnRemoteItemTransform", new ItemTransform { ItemId = VehicleId, OwnerPlayerId = 1, Sequence = 10, Flags = ItemTransform.FlagFinal });
                    f.Wheels[0].transform.SetParent(f.Body.transform, false); retry();
                    Require(f.Health[0].Value == 40 && f.Health[1].Value == 91 && ((VehicleCondition)Get(item, "ParkedVehicleCondition")).Availability == 4,
                        "Parked recovery lost partial availability.");
                });
                check("condition readiness: nonfinite native values are unavailable and finite replacements recover", () =>
                {
                    clean(); f.Pressure.Value = float.NaN; f.Health[2].Value = float.PositiveInfinity; var value = read();
                    Require(!value.HasPressure && !value.HasWheel(2) && value.HasWheel(1), "Nonfinite source became known zero.");
                    f.Pressure.Value = 0; f.Health[2].Value = 0; value = read(); Require(value.HasPressure && value.HasWheel(2), "Finite zero failed recovery.");
                });
                check("condition readiness: disabled native FSM withdraws its field and recovers on reenable", () =>
                {
                    clean(); read(); f.Wheels[1].enabled = false; Require(!read().HasWheel(1), "Disabled wheel stayed known.");
                    f.Wheels[1].enabled = true; Require(!read().HasWheel(1), "Restarted idle wheel became ready prematurely.");
                    NativeBagPartChecks.Fire(f.Wheels[1], "State 1"); Require(read().HasWheel(1), "Stable reenabled wheel stayed unavailable.");
                });
                check("condition readiness: missing canonical variable retires cached scalar until rediscovery", () =>
                {
                    clean(); read(); var vars = f.Wheels[0].FsmVariables; var saved = vars.FloatVariables;
                    var kept = new List<FsmFloat>(); foreach (var scalar in saved) if (scalar.Name != "Health") kept.Add(scalar);
                    try { vars.FloatVariables = kept.ToArray(); Require(!read().HasWheel(0), "Detached cached scalar was published."); }
                    finally { vars.FloatVariables = saved; }
                    Set(item, "NextConditionProbeAt", 0f); Require(read().HasWheel(0), "Restored canonical scalar did not recover.");
                });
                check("condition readiness: duplicate wheel FSM is unavailable until ambiguity is removed", () =>
                {
                    clean(); var duplicate = new GameObject("WHEELc_FL"); duplicate.transform.SetParent(f.Body.transform, false);
                    try
                    {
                        Dictionary<string, object>? row = null;
                        foreach (Dictionary<string, object> candidate in ReadOccupancyRows("condition-stream-probe.json"))
                            if (((string)candidate["path"]).EndsWith("WHEELc_FL")) row = candidate;
                        var fsm = NativeBagPartChecks.MakeFsm(duplicate, row!); fsm.Fsm.Init(fsm); NativeBagPartChecks.Start(fsm); NativeBagPartChecks.Fire(fsm, "State 1");
                        Require(!read().HasWheel(0) && read().HasWheel(1), "Ambiguous wheel source was selected arbitrarily.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                    Set(item, "NextConditionProbeAt", 0f); Require(read().HasWheel(0), "Removed duplicate did not restore unique source.");
                });
                check("condition readiness: globally aliased health is never captured or overwritten", () =>
                {
                    clean(); var globals = FsmVariables.GlobalVariables; var saved = globals.FloatVariables;
                    var extended = new FsmFloat[saved.Length + 1]; Array.Copy(saved, extended, saved.Length); extended[saved.Length] = f.Health[0];
                    try
                    {
                        globals.FloatVariables = extended; Require(!read().HasWheel(0), "Global alias was captured.");
                        packet(state(4, 0)); Require(f.Health[0].Value == 90, "Remote health overwrote a global alias.");
                    }
                    finally { globals.FloatVariables = saved; }
                    retry(); Require(f.Health[0].Value == 40, "Removed alias did not recover accepted state.");
                });
                check("condition readiness: replacement body forces immediate discovery despite the old retry timer", () =>
                {
                    clean(); packet(state(63, 1)); Set(item, "NextConditionProbeAt", float.MaxValue);
                    using (var replacement = new ConditionStreamFixture())
                    try
                    {
                        replacement.Seed(); Set(item, "Body", replacement.Body); Call(vehicles, "UpdateVehicleCondition", session);
                        Require(replacement.Health[0].Value == 40 && replacement.Pressure.Value == 230
                            && ReferenceEquals(Get(item, "ConditionProbeBody"), replacement.Body), "Replacement body used stale bindings or timer.");
                    }
                    finally { Set(item, "Body", f.Body); }
                });
                check("condition readiness: destroyed cached FSM withdraws its wheel without throwing", () =>
                {
                    clean(); using (var replacement = new ConditionStreamFixture())
                    try
                    {
                        replacement.Seed(); Set(item, "Body", replacement.Body); Require(read().Availability == 63, "Replacement setup missing inputs.");
                        UnityEngine.Object.DestroyImmediate(replacement.Wheels[0]); Require(!read().HasWheel(0) && read().HasWheel(1), "Destroyed cached wheel remained available.");
                    }
                    finally { Set(item, "Body", f.Body); }
                });
                check("condition readiness: availability withdrawal retains wrap and stale packet protection", () =>
                {
                    clean(); packet(state(63, 65534)); packet(state(0, 0)); packet(state(63, 65534));
                    Require(accepted().Availability == 0 && accepted().Sequence == 0, "Old complete state overruled a wrapped withdrawal.");
                    packet(state(4, 1)); Require(accepted().Availability == 4 && accepted().Sequence == 1, "Fresh partial state did not recover after withdrawal.");
                });
                check("condition readiness: parked guest checksum ignores withdrawn fields but still detects declared native drift", () =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)255); var report = state(4, 65535); report.OwnerPlayerId = 0; packet(report);
                    // This condition fixture supplies no native Corris RPM producers.
                    Set(item, "Path", "condition checksum probe"); Set(item, "RequiresNativeEngineRpm", false);
                    var power = MakePower(f.Body.gameObject); NativeBagPartChecks.Start(power);
                    Set(item, "ElectricityPowerFsm", power); Set(item, "EngineRevsVar", new FsmFloat { Name = "Revs", UseVariable = true, Value = 0 });
                    Set(item, "SystemsReady", true); Set(item, "NextSystemsProbeAt", float.MaxValue);
                    uint before = (uint)Call(vehicles, "FoldVehicleChecksum", 123u, item)!;
                    Require(before != 123u, "Checksum fixture skipped the vehicle."); f.Health[1].Value = 1; f.Pressure.Value = 0; f.Damage.Value = 200;
                    Require((uint)Call(vehicles, "FoldVehicleChecksum", 123u, item)! == before, "Ignored native fields caused a false resync.");
                    f.Health[0].Value = 39; Require((uint)Call(vehicles, "FoldVehicleChecksum", 123u, item)! != before, "Declared native drift was hidden by accepted data.");
                    Require(accepted().Availability == 4 && accepted().HealthFL == 40, "Checksum changed the accepted report.");
                });
                check("condition readiness: checksum keeps host discovery and missing local target availability distinct", () =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)255); var report = state(4, 65535); report.OwnerPlayerId = 0; packet(report);
                    var local = (VehicleCondition)Call(vehicles, "TryReadConditionForChecksum", item)!; Require(local.Availability == 4, "Guest ignored authority availability.");
                    f.Wheels[0].transform.SetParent(null, false); local = (VehicleCondition)Call(vehicles, "TryReadConditionForChecksum", item)!;
                    Require(local.Availability == 0, "Missing local target was reported as applied.");
                    f.Wheels[0].transform.SetParent(f.Body.transform, false); SetProperty(session, "IsHost", true); SetProperty(session, "State", SessionState.Hosting);
                    Require(((VehicleCondition)Call(vehicles, "TryReadConditionForChecksum", item)!).Availability == 63, "Host discovery inherited a guest mask.");
                });
                check("condition readiness: session teardown drops pending reconciliation and allows fresh discovery", () =>
                {
                    clean(); f.Wheels[0].transform.SetParent(null, false); packet(state(63, 30)); Call(vehicles, "ClearConditionStreams");
                    f.Wheels[0].transform.SetParent(f.Body.transform, false); retry(); Require(f.Health[0].Value == 90 && Get(item, "AcceptedVehicleCondition") == null, "Teardown replayed previous session data.");
                    packet(state(4, 0)); Require(f.Health[0].Value == 40, "New session could not discover and apply fresh data.");
                });
            }
            finally
            {
                items[VehicleId] = original; reset();
                for (int i = 0; i < 4; i++) f.Wheels[i].transform.SetParent(f.Body.transform, false);
            }
        }
    }
}
