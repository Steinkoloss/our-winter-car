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
        private static void RunHostCoolantChecks(Action<string, Action> check, TemperatureFixture f, object item,
            object vehicles, SessionManager session, CaptureTransport capture, Action reset, Action<bool, byte> mode)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            string[] fields = { "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string name in fields) saved[name] = Get(item, name);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool beforeProtection = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            uint oldId = (uint)Get(item, "Id"), id = StableHash.Fnv1a32("vehicle:CORRIS");
            const string prefix = "host coolant: ";
            Action poll = () => { Set(vehicles, "_nextCoolantPoll", 0f); Call(vehicles, "UpdateVehicleCoolant", session); };
            Func<VehicleCoolantState> build = () => (VehicleCoolantState)Call(vehicles, "BuildVehicleCoolantState", item)!;
            Action<uint, byte, float> receive = (revision, flags, temp) => Call(vehicles, "OnVehicleCoolantState",
                new VehicleCoolantState { VehicleId = id, Revision = revision, Flags = flags, Celsius = temp });
            Action<float> display = temp =>
            {
                f.Gauge.Fsm.Update();
                Require(f.Output.Value == Mathf.Clamp(temp, 50, 130), "Native gauge did not retain host degrees: " + f.Output.Value);
            };
            try
            {
                protect(false); reset(); items.Remove(oldId); items.Add(id, item); Set(item, "Id", id);
                f.Source.Fsm.StartState = "Init";
                foreach (var state in f.Source.Fsm.States) state.Transitions = new FsmTransition[0];
                NativeBagPartChecks.Start(f.Source); f.Rebind();
                check(prefix + "initial native cooling delay publishes unavailable", () =>
                {
                    NativeBagPartChecks.Fire(f.Source, "Init"); f.Celsius.Value = -99;
                    var state = build(); Require(state.Flags == 0 && state.Celsius == 0, "Asset startup sentinel was published as live coolant.");
                });
                check(prefix + "first native thermal cycle seeds host degrees", () =>
                {
                    NativeBagPartChecks.Fire(f.Source, "Coolant temp 2"); f.Celsius.Value = 87.125f;
                    var state = build(); Require(state.Flags == 1 && state.Celsius == 87.125f, "Ready source did not seed host coolant.");
                });
                check(prefix + "native engine degrees share readiness and advance their own changes", () =>
                {
                    var engine = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp"); float before = engine.Value;
                    try
                    {
                        engine.Value = 92.375f; var first = build();
                        Require(first.EngineCelsius == 92.375f && first.Celsius == 87.125f, "Engine temperature was replaced by coolant or gauge units.");
                        engine.Value = 93.625f; var next = build();
                        Require(next.Revision == first.Revision + 1 && next.EngineCelsius == 93.625f && next.Celsius == first.Celsius,
                            "Engine-only change did not advance publication.");
                        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                        {
                            engine.Value = invalid; var absent = build();
                            Require(absent.Flags == 0 && absent.Celsius == 0 && absent.EngineCelsius == 0, "Invalid engine heat escaped through available coolant.");
                        }
                        engine.Value = 94.125f; Require(build().EngineCelsius == 94.125f && build().Flags == 1, "Engine source failed to recover.");
                    }
                    finally { engine.Value = before; }
                });
                check(prefix + "protected and disconnected hosts cannot publish", () =>
                {
                    protect(true);
                    try { Require(Call(vehicles, "BuildVehicleCoolantState", item) == null, "Protected host published local world state."); }
                    finally { protect(false); }
                    SetProperty(session, "State", SessionState.Idle);
                    try { Require(Call(vehicles, "BuildVehicleCoolantState", item) == null, "Disconnected host published."); }
                    finally { SetProperty(session, "State", SessionState.Hosting); }
                });
                foreach (float degrees in new[] { -25.375f, 0f, 50f, 87.125f, 120f, 129.75f, 140f })
                {
                    float temp = degrees;
                    check(prefix + "host capture and native guest gauge at " + temp + " C", () =>
                    {
                        mode(true, 1); f.Celsius.Value = temp; f.Output.Value = -999;
                        var host = build(); Require(host.Flags == 1 && host.Celsius == temp && f.Output.Value == -999,
                            "Capture quantized degrees or rewrote native state.");
                        mode(false, 3); Set(item, "LocallyOwned", true); f.Celsius.Value = 37;
                        Call(vehicles, "OnVehicleCoolantState", host); poll(); NativeBagPartChecks.Fire(f.Gauge, "Speed"); display(temp);
                        Require(f.Celsius.Value == 37, "Host display wrote guest simulated coolant.");
                    });
                }
                check(prefix + "snapshot cannot consume a pending reliable broadcast", () =>
                {
                    reset(); f.Rebind(); NativeBagPartChecks.Fire(f.Source, "Coolant temp 2"); f.Celsius.Value = 80;
                    poll(); Require(capture.Packets.Count == 2, "Initial host state did not reach both peers."); capture.Packets.Clear();
                    f.Celsius.Value = 81.25f; VehicleCoolantState? snapshot = null;
                    foreach (IMessage message in (IEnumerable)Call(vehicles, "BuildVehicleStateMessages", item, (byte)0)!)
                        if (message is VehicleCoolantState coolant) snapshot = coolant;
                    Require(snapshot != null && snapshot.Celsius == 81.25f, "Vehicle snapshot omitted coolant.");
                    poll(); Require(capture.Packets.Count == 2, "Join snapshot stranded existing peers.");
                    foreach (var packet in capture.Packets)
                        Require(packet.Channel == Channel.ReliableOrdered && packet.Message is VehicleCoolantState state
                            && state.Revision == snapshot!.Revision && state.Celsius == 81.25f, "Live coolant publication differs from snapshot.");
                    capture.Packets.Clear(); poll(); Require(capture.Packets.Count == 0, "Unchanged state was sent before keepalive.");
                    Set(vehicles, "_nextCoolantKeepalive", 0f); poll(); Require(capture.Packets.Count == 2, "Unchanged state cannot repair a late bind.");
                });
                check(prefix + "host retains its own gauge under a guest driver packet", () =>
                {
                    mode(true, 1); Set(item, "LocallyOwned", false); f.Celsius.Value = 83;
                    var state = State(1, 0, 2000); state.VehicleId = id; state.CoolantTemp = 255;
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", state)!, "Current guest driver was rejected.");
                    NativeBagPartChecks.Fire(f.Gauge, "Speed"); CallStatic("ApplyRemoteGauges", item); display(83);
                    Require(build().Celsius == 83, "Guest driver temperature became host thermal state.");
                    receive(999, 1, 125); Require(Get(item, "HostCoolant") == null, "Host accepted a host-only receive callback.");
                });
                check(prefix + "unseeded guest uses cold stop regardless of local ownership", () =>
                {
                    reset(); mode(false, 3); Set(item, "LocallyOwned", true); f.Celsius.Value = 121;
                    poll(); NativeBagPartChecks.Fire(f.Gauge, "Speed"); display(0);
                    Require(f.Celsius.Value == 121, "Unseeded display overwrote guest thermal simulation.");
                });
                check(prefix + "host arrival is copied and every-frame gauge reads stay authoritative", () =>
                {
                    var state = new VehicleCoolantState { VehicleId = id, Revision = 10, Flags = 1, Celsius = 87.125f };
                    float before = f.Output.Value; Call(vehicles, "OnVehicleCoolantState", state); state.Celsius = 130;
                    Require(f.Output.Value == before && f.Celsius.Value == 121, "Packet receipt changed native state.");
                    for (int frame = 0; frame < 5; frame++) display(87.125f);
                    receive(9, 1, 130); display(87.125f); receive(10, 1, 129); display(87.125f);
                    receive(10, 0, 0); display(87.125f); receive(10, 1, 87.125f); display(87.125f);
                    receive(11, 1, float.NaN); display(87.125f);
                });
                check(prefix + "driver handoff seating and engine timeout cannot reset host heat", () =>
                {
                    Set(item, "LocallyOwned", false); Set(item, "RemoteOwner", (byte)1); display(87.125f);
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); Set(item, "RemoteOwner", (byte)2); display(87.125f);
                    Set(item, "LocallyOwned", true); Set(item, "RemoteEngineUntil", -999f); display(87.125f);
                    var world = World.GetProperty("Instance", Static).GetValue(null, null);
                    var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent;
                    try { player.SetParent(f.Body.transform, false); display(87.125f); } finally { player.SetParent(parent, false); }
                    receive(11, 1, 91.625f); display(91.625f);
                });
                check(prefix + "unavailable host clears heat until a newer valid state", () =>
                {
                    receive(12, 0, 0); display(0); receive(11, 1, 91.625f); display(0);
                    receive(13, 1, 102.5f); display(102.5f); Require(f.Celsius.Value == 121, "Retirement rewrote guest temperature.");
                });
                check(prefix + "late scene discovery retains the accepted host snapshot", () =>
                {
                    reset(); mode(false, 3); items.Remove(id);
                    try { receive(22, 1, 94.25f); } finally { items.Add(id, item); }
                    Require(Get(item, "HostCoolant") == null, "Absent scene item unexpectedly mutated.");
                    poll(); display(94.25f);
                });
                check(prefix + "unknown vehicle and inactive sessions cannot replace host state", () =>
                {
                    Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState { VehicleId = id + 1, Revision = 999, Flags = 1, Celsius = 130 });
                    display(94.25f); SetProperty(session, "State", SessionState.Idle); receive(999, 1, 130);
                    display(121); SetProperty(session, "State", SessionState.Connected); display(94.25f);
                    Require(Call(vehicles, "BuildVehicleCoolantState", item) == null, "Guest published authoritative coolant.");
                });
                check(prefix + "source loss sends unavailable and repaired source waits for readiness", () =>
                {
                    reset(); f.Rebind(); NativeBagPartChecks.Fire(f.Source, "Coolant temp 2"); f.Celsius.Value = 87;
                    Require(build().Flags == 1, "Host source not available before fault.");
                    object original = Get(f.State.Actions[0], "variableName");
                    try { Set(f.State.Actions[0], "variableName", new FsmString("Wrong")); Require(build().Flags == 0, "Changed source remained available."); }
                    finally { Set(f.State.Actions[0], "variableName", original); }
                    NativeBagPartChecks.Fire(f.Source, "Init"); f.Rebind(); Require(build().Flags == 0, "Rebinding skipped native readiness.");
                    NativeBagPartChecks.Fire(f.Source, "Coolant temp 2"); Require(build().Flags == 1, "Repaired host source failed to recover.");
                });
                check(prefix + "nonfinite host values retire and stopped ready cooling retains heat", () =>
                {
                    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    { f.Celsius.Value = invalid; var state = build(); Require(state.Flags == 0 && state.Celsius == 0, "Invalid native heat escaped."); }
                    f.Celsius.Value = 78.5f; f.Source.enabled = false; f.Source.gameObject.SetActive(false);
                    try { Require(build().Celsius == 78.5f && build().Flags == 1, "Stopped source lost ready temperature."); }
                    finally { f.Source.gameObject.SetActive(true); f.Source.enabled = true; }
                });
                check(prefix + "native restart waits for a new thermal cycle", () =>
                {
                    NativeBagPartChecks.Fire(f.Source, "Init"); Require(build().Flags == 0, "Native restart retained readiness from before initialization.");
                    NativeBagPartChecks.Fire(f.Source, "Coolant temp 2"); Require(build().Flags == 1, "Native restart could not recover.");
                });
                check(prefix + "removed vehicle publishes an unavailable edge", () =>
                {
                    capture.Packets.Clear(); items.Remove(id);
                    try { poll(); } finally { items.Add(id, item); }
                    Require(capture.Packets.Count == 2, "Removed source left connected peers with live heat.");
                    foreach (var packet in capture.Packets)
                        Require(packet.Message is VehicleCoolantState state && state.VehicleId == id && state.Flags == 0 && state.Celsius == 0,
                            "Removed source retained a temperature.");
                    Require(build().Flags == 1, "Reappearing source could not recover.");
                });
                check(prefix + "session cleanup releases the gauge and permits fresh revision zero", () =>
                {
                    mode(false, 3); receive(500, 1, 105); poll(); display(105);
                    Call(vehicles, "ClearVehicleStateStreams"); display(78.5f);
                    receive(0, 1, 96.125f); poll(); display(96.125f);
                    Require(f.Celsius.Value == 78.5f, "Cleanup altered physical coolant.");
                });
            }
            finally
            {
                reset(); items.Remove(id); items[oldId] = item; Set(item, "Id", oldId);
                Set(item, "RequiresHostCoolant", false);
                foreach (string name in fields) Set(item, name, saved[name]);
                protect(beforeProtection);
            }
        }
    }
}
