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
        private static void RunWheelRimChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members);
            bool previousProtection = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            using (var f = new WheelHealthFixture(true))
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                Action clean = () =>
                {
                    reset(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected);
                    SetProperty(session, "LocalPlayerId", (byte)3); Set(item, "RemoteOwner", (byte)1); Set(item, "LocallyOwned", false);
                    Set(item, "Body", f.Body); Call(vehicles, "ClearConditionStreams"); f.Seed();
                    for (int i = 0; i < 4; i++) { Set(f.Physics[i], "radius", .31f); Set(f.Physics[i], "rollingFrictionCoefficient", .018f); }
                    capture.Packets.Clear();
                };
                Func<int, ushort, VehicleCondition> report = (wheel, seq) => new VehicleCondition { VehicleId = VehicleId,
                    OwnerPlayerId = 1, Sequence = seq, Availability = (byte)(VehicleCondition.AvailableFL << wheel),
                    HealthFL = 31, HealthFR = 32, HealthRL = 33, HealthRR = 34, Flags = (byte)(16 << wheel) };
                Action<VehicleCondition> packet = message => Call(session, "OnPacketReceived", new PeerId(session.IsHost ? 1001UL : 999UL), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Action retry = () => { Array.Clear((float[])Get(item, "NextWheelRimRetryAt"), 0, 4); Call(vehicles, "UpdateConditionBindings", item); };
                Action saved = () => { for (int i = 0; i < 4; i++) Require(f.Saved[i].Value == 90 + i, "Rim replay changed saved tyre health."); };
                Action<int> applied = wheel =>
                {
                    string details = (string)Core.GetType("WinterMP.Core.Diagnostics.SyncEventLog", true).GetMethod("GetSnapshot", Static).Invoke(null, null);
                    if (details.Length > 3000) details = details.Substring(details.Length - 3000);
                    Require(f.Wheels[wheel].ActiveStateName == "Rim friction" && (float)Get(f.Physics[wheel], "radius") == .165f
                        && (float)Get(f.Physics[wheel], "rollingFrictionCoefficient") == .1f, "Rim physics did not apply: " + details);
                    Require(!f.Wheels[wheel].FsmVariables.FindFsmGameObject("FlatSound").Value.activeSelf, "Old puncture sound stayed active.");
                    Require(!(bool)Get(item, "ConditionNeedsApply"), "Completed rim replay remained pending."); saved();
                };

                for (int i = 0; i < 4; i++)
                {
                    int wheel = i; string suffix = new[] { "FL", "FR", "RL", "RR" }[i];
                    check("wheel rim: " + suffix + " healthy observer enters native rim physics independently of saved tyre type", () =>
                    {
                        clean(); packet(report(wheel, 0)); f.Tick(); applied(wheel);
                        for (int other = 0; other < 4; other++) if (other != wheel)
                            Require((float)Get(f.Physics[other], "radius") == .31f && f.Wheels[other].ActiveStateName == "State 1", "Rim report changed another wheel.");
                        Require(capture.Packets.Count == 0, "Observer replay emitted a new condition or intent.");
                    });
                    check("wheel rim: " + suffix + " flat observer switches to rim and silences native puncture sound", () =>
                    {
                        clean(); NativeBagPartChecks.Fire(f.Wheels[wheel], "Sound");
                        Require(f.Wheels[wheel].ActiveStateName == "Flat friction"
                            && f.Wheels[wheel].FsmVariables.FindFsmGameObject("FlatSound").Value.activeSelf, "Native flat setup failed.");
                        packet(report(wheel, 0)); f.Tick(); applied(wheel);
                    });
                    check("wheel rim: " + suffix + " host applies guest result without entering the saved-health puncture writer", () =>
                    {
                        clean(); SetProperty(session, "IsHost", true); SetProperty(session, "State", SessionState.Hosting);
                        SetProperty(session, "LocalPlayerId", (byte)0);
                        var punctureWriter = NativeBagPartChecks.State(f.Wheels[wheel], "Flat friction").Actions[0];
                        punctureWriter.Enabled = true; protect(false);
                        try
                        {
                            packet(report(wheel, 0)); f.Tick(); applied(wheel);
                            NativeBagPartChecks.Fire(f.Wheels[wheel], "Sound");
                            Require(f.Saved[wheel].Value == 0, "Host test did not exercise a live native saved-health writer.");
                            f.Saved[wheel].Value = 90 + wheel;
                        }
                        finally { protect(previousProtection); punctureWriter.Enabled = false; }
                    });
                }
                check("wheel rim: actual native continuous action keeps rim friction after a physics overwrite", () =>
                {
                    clean(); packet(report(0, 0)); Set(f.Physics[0], "rollingFrictionCoefficient", .7f); f.Tick(); applied(0);
                });
                check("wheel rim: accepted keepalive repairs radius drift and reactivated puncture sound", () =>
                {
                    clean(); packet(report(0, 0)); Set(f.Physics[0], "radius", .55f);
                    f.Wheels[0].FsmVariables.FindFsmGameObject("FlatSound").Value.SetActive(true); packet(report(0, 1)); applied(0);
                    packet(report(0, 2)); applied(0);
                });
                check("wheel rim: invalid peer duplicate and local driver cannot apply rim physics", () =>
                {
                    clean(); var healthy = report(0, 0); healthy.Flags = 0; packet(healthy); packet(report(0, 0));
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(report(0, 1)), Channel.ReliableOrdered);
                    Set(item, "LocallyOwned", true); packet(report(0, 2));
                    Require(f.Wheels[0].ActiveStateName == "State 1" && (float)Get(f.Physics[0], "radius") == .31f, "Rejected rim report changed physics."); saved();
                });
                check("wheel rim: unavailable wheel cannot act on a rim flag", () =>
                {
                    clean(); var message = report(0, 0); message.Availability = 0; packet(message);
                    Require(f.Wheels[0].ActiveStateName == "State 1" && (float)Get(f.Physics[0], "radius") == .31f, "Unavailable rim became damage."); saved();
                });
                check("wheel rim: host snapshot enters parked rim physics without claiming driving ownership", () =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)255); var message = report(0, VehicleCondition.SnapshotSequence); message.OwnerPlayerId = 0;
                    packet(message); applied(0); Require((byte)Get(item, "RemoteOwner") == 255 && !(bool)Get(item, "LocallyOwned"), "Rim snapshot claimed a driver.");
                });
                check("wheel rim: late native wheel target retries accepted state without another packet", () =>
                {
                    clean(); var target = f.Wheels[0].FsmVariables.FindFsmObject("Wheel"); var value = target.Value;
                    target.Value = null;
                    try { packet(report(0, 0)); Require((bool)Get(item, "ConditionNeedsApply") && f.Wheels[0].ActiveStateName == "State 1", "Missing wheel was considered applied."); }
                    finally { target.Value = value; }
                    retry(); applied(0);
                });
                check("wheel rim: withdrawal cancels pending application before a late target returns", () =>
                {
                    clean(); var target = f.Wheels[0].FsmVariables.FindFsmObject("Wheel"); var value = target.Value; target.Value = null;
                    try { packet(report(0, 0)); var message = report(0, 1); message.Availability = 0; packet(message); }
                    finally { target.Value = value; }
                    retry(); Require(f.Wheels[0].ActiveStateName == "State 1" && !(bool)Get(item, "ConditionNeedsApply"), "Withdrawn rim report applied later."); saved();
                });
                check("wheel rim: replacing or clearing the registered body retires pending rim application", () =>
                {
                    clean(); var target = f.Wheels[0].FsmVariables.FindFsmObject("Wheel"); var value = target.Value; target.Value = null;
                    try { packet(report(0, 0)); } finally { target.Value = value; }
                    Set(item, "Body", Get(original, "Body")); retry();
                    Require(f.Wheels[0].ActiveStateName == "State 1", "Old body received deferred rim physics.");
                    Set(item, "Body", f.Body); Call(vehicles, "ClearConditionStreams"); retry();
                    Require(f.Wheels[0].ActiveStateName == "State 1", "Cleared session replayed rim physics."); saved();
                });

                foreach (string mutation in new[] { "radius member", "friction literal", "friction cadence", "disabled action", "foreign wheel", "invalid radius", "foreign sound", "extra state action", "redirected entry", "local shadow" })
                {
                    string changed = mutation;
                    check("wheel rim: changed " + changed + " pauses before mutation and recovers after repair", () =>
                    {
                        clean(); var fsm = f.Wheels[0]; var state = NativeBagPartChecks.State(fsm, "Rim friction"); var actions = state.Actions;
                        var radius = (FsmProperty)Get(actions[0], "targetProperty"); var friction = (FsmProperty)Get(actions[1], "targetProperty");
                        var target = fsm.FsmVariables.FindFsmObject("Wheel"); var sound = fsm.FsmVariables.FindFsmGameObject("FlatSound");
                        string member = radius.PropertyName; float frictionValue = friction.FloatParameter.Value, rimRadius = radius.FloatParameter.Value;
                        var oldTarget = target.Value; var oldSound = sound.Value; var globals = fsm.Fsm.GlobalTransitions;
                        var healthy = NativeBagPartChecks.State(fsm, "State 1"); var locals = healthy.Transitions;
                        try
                        {
                            switch (changed)
                            {
                                case "radius member": radius.PropertyName = "width"; break;
                                case "friction literal": friction.FloatParameter.Value = .2f; break;
                                case "friction cadence": Set(actions[1], "everyFrame", false); break;
                                case "disabled action": actions[0].Enabled = false; break;
                                case "foreign wheel": target.Value = f.Physics[1]; break;
                                case "invalid radius": radius.FloatParameter.Value = float.NaN; break;
                                case "foreign sound": sound.Value = f.Body.gameObject; break;
                                case "extra state action": state.Actions = new[] { actions[0], actions[1], new WheelHealthQuiet() }; break;
                                case "redirected entry": fsm.Fsm.GlobalTransitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("MP_RIM_FRICTION"), ToState = "Sound" } }; break;
                                case "local shadow": healthy.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("MP_RIM_FRICTION"), ToState = "Sound" } }; break;
                            }
                            packet(report(0, 0));
                            Require(fsm.ActiveStateName == "State 1" && (float)Get(f.Physics[0], "radius") == .31f
                                && (float)Get(f.Physics[1], "radius") == .31f && f.Body.gameObject.activeSelf
                                && (bool)Get(item, "ConditionNeedsApply"), "Changed rim binding mutated physics or was dropped."); saved();
                        }
                        finally
                        {
                            radius.PropertyName = member; friction.FloatParameter.Value = frictionValue; radius.FloatParameter.Value = rimRadius;
                            Set(actions[1], "everyFrame", true); actions[0].Enabled = true; target.Value = oldTarget; sound.Value = oldSound;
                            state.Actions = actions; fsm.Fsm.GlobalTransitions = globals; healthy.Transitions = locals;
                        }
                        retry(); applied(0);
                    });
                }
            }
            finally { protect(previousProtection); items[VehicleId] = original; reset(); }
        }
    }
}
