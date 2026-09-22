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
        private static void RunGearboxOilUseChecks(Action<string, Action> check, DifferentialFixture f,
            PlayMakerFSM transmission, PlayMakerFSM automatic, Func<bool> prepare, Action<PlayMakerFSM, string> fire,
            Action<bool, byte> mode, object item)
        {
            var session = SessionManager.Instance ?? throw new InvalidOperationException("Missing native test session.");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var vehicles = Get(world, "_vehicles"); var transport = (CaptureTransport)Get(session, "_transport");
            var localPlayer = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = localPlayer.parent; var localPosition = localPlayer.localPosition;
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool originalProtection = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            var data = automatic.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
            var oil = data.FsmVariables.FindFsmFloat("OilLevel"); var wear = data.FsmVariables.FindFsmFloat("Wear");
            var priorInts = data.FsmVariables.IntVariables; var priorBools = data.FsmVariables.BoolVariables;
            var type = new FsmInt { Name = "Type", UseVariable = true, Value = 2 }; var installed = new FsmBool { Name = "Installed", UseVariable = true, Value = true };
            data.FsmVariables.IntVariables = new List<FsmInt>(priorInts) { type }.ToArray();
            data.FsmVariables.BoolVariables = new List<FsmBool>(priorBools) { installed }.ToArray();
            float originalOil = oil.Value, originalWear = wear.Value;
            var first = NativeBagPartChecks.State(automatic, "State 1").Actions[3]; var third = NativeBagPartChecks.State(automatic, "State 3").Actions[3];
            var savedWrites = NativeBagPartChecks.State(f.Fsm, "Wear").Actions; var enabled = new bool[savedWrites.Length];
            for (int i = 0; i < enabled.Length; i++) enabled[i] = savedWrites[i].Enabled;
            const string prefix = "gearbox oil use: "; GearboxOilUseRequest? observed = null;
            uint id = (uint)Get(item, "Id"); ushort sequence = 0;
            Func<byte, GearboxOilUseRequest> request = phase => new GearboxOilUseRequest { VehicleId = id, PlayerId = 3, Sequence = ++sequence, Phase = phase };
            Func<GearboxOilUseRequest, bool> apply = value => (bool)Call(vehicles, "OnHostGearboxOilUse", value, (byte)3)!;
            Action budget = () => Call(vehicles, "ClearGearboxOilUse");
            try
            {
                check(prefix + "actual driver native callbacks emit one reliable request each without saved writes", () =>
                {
                    mode(false, 255); Set(item, "LocallyOwned", true); localPlayer.SetParent(f.Body.transform, false); prepare();
                    transport.Packets.Clear(); fire(automatic, "State 1"); fire(automatic, "State 3");
                    Require(transport.Packets.Count == 2, "Native driver callbacks did not emit exactly two requests.");
                    for (int i = 0; i < 2; i++)
                    {
                        var packet = transport.Packets[i]; observed = packet.Message as GearboxOilUseRequest;
                        Require(packet.Channel == Channel.ReliableOrdered && observed != null && observed.VehicleId == id && observed.PlayerId == 3
                            && observed.Phase == (i == 0 ? 1 : 3), "Wrong oil-use intent or channel.");
                    }
                    sequence = observed!.Sequence; Require(oil.Value == originalOil && wear.Value == originalWear, "Guest oil callback changed saved data.");
                });
                check(prefix + "direct repeated callbacks and ticks cannot duplicate one native entry", () =>
                {
                    transport.Packets.Clear(); third.OnEnter(); third.OnUpdate(); third.OnEnter();
                    Require(transport.Packets.Count == 0 && oil.Value == originalOil, "Repeated callback duplicated the event.");
                });
                check(prefix + "non-driver ownership and disconnected sessions cannot report", () =>
                {
                    localPlayer.SetParent(parent, false); localPlayer.localPosition = localPosition; prepare(); transport.Packets.Clear(); fire(automatic, "State 1");
                    Require(transport.Packets.Count == 0 && !first.Enabled, "Non-driver ownership emitted oil use.");
                    localPlayer.SetParent(f.Body.transform, false); SetProperty(session, "State", SessionState.Idle); prepare(); first.OnEnter();
                    Require(transport.Packets.Count == 0 && oil.Value == originalOil, "Disconnect emitted or wrote oil."); mode(false, 255);
                });
                check(prefix + "changed native oil cadence fails locally and repairs", () =>
                {
                    prepare(); Set(first, "perSecond", true); transport.Packets.Clear();
                    try { fire(automatic, "State 1"); Require(!automatic.enabled && transport.Packets.Count == 0 && oil.Value == originalOil, "Changed callback cadence escaped protection."); }
                    finally { Set(first, "perSecond", false); prepare(); }
                    Require(automatic.enabled, "Repaired callback stayed paused.");
                });
                check(prefix + "host applies the captured event once using current host wear and native helpers", () =>
                {
                    localPlayer.SetParent(parent, false); localPlayer.localPosition = localPosition; Set(item, "LocallyOwned", false); mode(true, 3); protect(false); budget();
                    for (int i = 0; i < savedWrites.Length; i++) savedWrites[i].Enabled = true;
                    first.Enabled = third.Enabled = true; oil.Value = 1; wear.Value = 10;
                    var scratch = automatic.FsmVariables.FindFsmFloat("OilLeakRate"); var scratchWear = automatic.FsmVariables.FindFsmFloat("GearboxWear"); scratch.Value = 99; scratchWear.Value = 100;
                    string active = automatic.ActiveStateName; var operand = Get(third, "subtractValue");
                    if (!apply(observed!))
                    {
                        var snapshot = (VehicleDrivetrainWearState)Call(vehicles, "BuildVehicleDrivetrainWearState", item)!;
                        string events = (string)Core.GetType("WinterMP.Core.Diagnostics.SyncEventLog", true).GetMethod("GetSnapshot", Static).Invoke(null, null);
                        throw new InvalidOperationException("Host rejected native callback: flags=" + snapshot.Flags + " oil=" + snapshot.GearboxOilAvailable + "\n" + events);
                    }
                    Require(Mathf.Abs(oil.Value - .999f) < .000001f && wear.Value == 10 && scratch.Value == 99 && scratchWear.Value == 100
                        && automatic.ActiveStateName == active && ReferenceEquals(operand, Get(third, "subtractValue")), "Host helper changed scratch/state or used guest/stale rate.");
                    float after = oil.Value; Require(!apply(observed!) && oil.Value == after, "Duplicate request drained oil twice.");
                });
                check(prefix + "host observer callback cannot double-charge a delegated car", () =>
                {
                    float before = oil.Value; first.OnEnter(); third.OnEnter(); Require(oil.Value == before, "Host observer performed competing native oil use.");
                });
                foreach (float hostWear in new[] { 100f, 15f, 14f, 0f, -6000f })
                    check(prefix + "native host rate uses saved wear " + hostWear, () =>
                    {
                        budget(); wear.Value = hostWear; oil.Value = 6.3f; float rate = Mathf.Clamp((15f - hostWear) / 5000f, .0000001f, 1f);
                        Require(apply(request(1)), "Valid native host oil step rejected.");
                        Require(Mathf.Abs(oil.Value - (6.3f - rate)) < .000001f && wear.Value == hostWear, "Native saved oil amount differs from host wear.");
                    });
                foreach (string fault in new[] { "manual", "removed", "missing oil", "nonfinite oil", "changed division", "warm foreign cache" })
                    check(prefix + fault + " is rejected once and cannot replay after repair", () =>
                    {
                        budget(); wear.Value = 10; oil.Value = 1;
                        var division = NativeBagPartChecks.State(automatic, "Set stall speed").Actions[5]; var divisor = Get(division, "divideBy");
                        var floats = data.FsmVariables.FloatVariables; var cached = Get(first, "fsm"); var last = Get(first, "goLastFrame");
                        if (fault == "manual") type.Value = 0; if (fault == "removed") installed.Value = false;
                        if (fault == "missing oil") data.FsmVariables.FloatVariables = new[] { wear };
                        if (fault == "nonfinite oil") oil.Value = float.NaN;
                        if (fault == "changed division") Set(division, "divideBy", new FsmFloat(6000));
                        if (fault == "warm foreign cache") { Set(first, "goLastFrame", data.gameObject); Set(first, "fsm", f.Fsm); }
                        var rejected = request(1);
                        try { Require(!apply(rejected), "Unsafe host binding accepted oil use."); Require(float.IsNaN(oil.Value) || oil.Value == 1, "Rejected request changed oil."); }
                        finally { type.Value = 2; installed.Value = true; data.FsmVariables.FloatVariables = floats; oil.Value = 1; Set(division, "divideBy", divisor); Set(first, "fsm", cached); Set(first, "goLastFrame", last); }
                        Require(!apply(rejected) && oil.Value == 1, "Repair revived the rejected callback.");
                        Require(apply(request(1)) && oil.Value < 1, "Fresh callback did not recover after repair.");
                    });
                check(prefix + "matching-path duplicate gearbox cannot receive host oil use", () =>
                {
                    budget(); oil.Value = 1; wear.Value = 10; var twin = MakeGearboxTwin(data); var reference = automatic.FsmVariables.FindFsmGameObject("db_Gearbox");
                    var cached = Get(first, "fsm"); var last = Get(first, "goLastFrame"); var rejected = request(1);
                    try
                    {
                        reference.Value = twin.gameObject;
                        Require(!apply(rejected) && oil.Value == 1 && twin.FsmVariables.FindFsmFloat("OilLevel").Value == 1,
                            "Matching path and oil substituted a different native saved gearbox.");
                    }
                    finally { reference.Value = data.gameObject; Set(first, "fsm", cached); Set(first, "goLastFrame", last); UnityEngine.Object.DestroyImmediate(twin.gameObject); }
                    Require(!apply(rejected) && apply(request(1)), "Target repair revived an old oil callback or blocked a fresh one.");
                });
                check(prefix + "ownership changes and host driving reject late or forged callbacks", () =>
                {
                    budget(); var value = request(1); float before = oil.Value;
                    mode(true, 2); Require(!apply(value), "Previous driver remained authoritative."); mode(true, 3);
                    Set(item, "LocallyOwned", true); Require(!apply(value), "Host-driven car accepted guest use."); Set(item, "LocallyOwned", false);
                    Require(!(bool)Call(vehicles, "OnHostGearboxOilUse", value, (byte)2)! && oil.Value == before, "Forged sender changed oil.");
                    Require(apply(value), "Rejected identity consumed the rightful driver's event.");
                });
                check(prefix + "host oil result publishes the native subtraction to every guest", () =>
                {
                    transport.Packets.Clear(); Call(vehicles, "UpdateDrivetrainWearStates", session);
                    Require(transport.Packets.Count == session.PlayerCount, "Oil use did not schedule its host result.");
                    foreach (var packet in transport.Packets)
                    { var result = packet.Message as VehicleDrivetrainWearState; Require(result != null && result.GearboxOilAvailable && result.GearboxOilLevel == oil.Value, "Broadcast lost authoritative oil use."); }
                });
                check(prefix + "departed player cleanup admits a new sequence for a reused slot", () =>
                {
                    budget(); sequence = 500; Require(apply(request(1)), "Prior connection callback rejected.");
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)3); sequence = 0;
                    float before = oil.Value; Require(apply(request(1)) && oil.Value < before, "Departed player history blocked the new connection.");
                });
                check(prefix + "solo and host local ownership retain native oil use", () =>
                {
                    mode(true, 255); Set(item, "LocallyOwned", true); oil.Value = 1; automatic.FsmVariables.FindFsmFloat("OilLeakRate").Value = .001f;
                    first.OnEnter(); Require(Mathf.Abs(oil.Value - .999f) < .000001f, "Local host native write was blocked.");
                });
            }
            finally
            {
                localPlayer.SetParent(parent, false); localPlayer.localPosition = localPosition;
                data.FsmVariables.IntVariables = priorInts; data.FsmVariables.BoolVariables = priorBools; oil.Value = originalOil; wear.Value = originalWear;
                for (int i = 0; i < enabled.Length; i++) savedWrites[i].Enabled = enabled[i];
                Set(item, "LocallyOwned", false); mode(false, 255); protect(originalProtection); Call(vehicles, "ClearDrivetrainWearStates");
            }
        }
    }
}
