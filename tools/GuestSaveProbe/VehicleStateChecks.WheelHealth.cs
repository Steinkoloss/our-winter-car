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
        private static void RunWheelHealthReads(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position;
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            using (var f = new WheelHealthFixture())
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                CallStatic("EnsureConditionProbe", item);
                Action clean = () =>
                {
                    reset(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected);
                    SetProperty(session, "LocalPlayerId", (byte)3); Set(item, "RemoteOwner", (byte)1); Set(item, "LocallyOwned", false);
                    Set(item, "Body", f.Body); player.SetParent(parent, false); player.position = position;
                    Call(vehicles, "ClearConditionStreams"); f.Seed(); capture.Packets.Clear();
                };
                Func<ushort, byte, byte, VehicleCondition> state = (sequence, health, flags) => new VehicleCondition { Availability = VehicleCondition.AvailableAll,
                    VehicleId = VehicleId, OwnerPlayerId = 1, Sequence = sequence, TirePressure = 190,
                    HealthFL = health, HealthFR = (byte)(health + 1), HealthRL = (byte)(health + 2), HealthRR = (byte)(health + 3), Flags = flags };
                Action<VehicleCondition> packet = message => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Action saved = () => { for (int i = 0; i < 4; i++) Require(f.Saved[i].Value == 90 + i, "Native shared read changed a saved tyre."); };

                for (int index = 0; index < 4; index++)
                {
                    int wheel = index; string label = new[] { "FL", "FR", "RL", "RR" }[wheel];
                    check("wheel health reads: " + label + " accepted health survives native ticks and feeds grip arithmetic", () =>
                    {
                        clean(); packet(state(0, 30, 0)); f.Tick(); f.Tick();
                        Require(f.Health[wheel].Value == 30 + wheel && Math.Abs(f.Wheels[wheel].FsmVariables.FindFsmFloat("GripReduction").Value
                            - (100f - 30 - wheel) / 1800f) < .00001f, "Native read or subsequent grip math used the guest's saved health."); saved();
                    });
                    check("wheel health reads: " + label + " native puncture entry reads the current application before commit", () =>
                    {
                        clean(); packet(state(0, 70, 0)); packet(state(1, 20, (byte)(1 << wheel)));
                        Require(f.Wheels[wheel].ActiveStateName == "Flat friction" && f.Health[wheel].Value == 20 + wheel,
                            "Synchronous native flat read used the old accepted condition.");
                        Require(Get(item, "ApplyingVehicleCondition") == null, "Temporary condition escaped apply scope."); f.Tick();
                        Require(f.Health[wheel].Value == 20 + wheel, "Flat every-frame read lost shared health."); saved();
                    });
                    check("wheel health reads: " + label + " native health lookup resumes for a local driver", () =>
                    {
                        clean(); packet(state(0, 30, 0)); Set(item, "LocallyOwned", true); f.Tick();
                        Require(f.Health[wheel].Value == 90 + wheel, "Observer projection overwrote a driver's native input."); saved();
                    });
                }
                check("wheel health reads: known zero and byte255 survive actual native readers", () =>
                {
                    clean(); var message = state(0, 0, 0); message.HealthRR = 255; packet(message); f.Tick();
                    Require(f.Health[0].Value == 0 && f.Health[3].Value == 255, "Known zero became missing or byte range changed."); saved();
                });
                check("wheel health reads: forged peer and duplicate report cannot change the native input", () =>
                {
                    clean(); packet(state(0, 30, 0)); packet(state(0, 0, 1));
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(state(1, 0, 1)), Channel.ReliableOrdered); f.Tick();
                    Require(f.Health[0].Value == 30 && f.Wheels[0].ActiveStateName == "State 1", "Rejected packet entered native health reads."); saved();
                });
                check("wheel health reads: new owner cannot inherit a former owner's read source", () =>
                {
                    clean(); packet(state(0, 30, 0)); Set(item, "RemoteOwner", (byte)2); f.Tick();
                    Require(f.Health[0].Value == 90, "Former owner supplied shared health.");
                    var next = state(0, 40, 0); next.OwnerPlayerId = 2; packet(next); f.Tick();
                    Require(f.Health[0].Value == 40, "New owner's accepted stream did not resume projection."); saved();
                });
                check("wheel health reads: host snapshot supplies an unowned car without live sequence effects", () =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)255); var snapshot = state(65535, 50, 0); snapshot.OwnerPlayerId = 0;
                    packet(snapshot); f.Tick(); Require(f.Health[0].Value == 50 && (ushort)Get(item, "OutConditionSequence") == 0,
                        "Snapshot lost shared health or advanced live publication."); saved();
                });
                check("wheel health reads: partial availability projects only the declared native reader", () =>
                {
                    clean(); var report = state(0, 30, 0); report.Availability = VehicleCondition.AvailableFL; packet(report); f.Tick();
                    Require(f.Health[0].Value == 30 && f.Health[1].Value == 91 && f.Health[3].Value == 93,
                        "An unavailable wheel consumed shared health."); saved();
                });
                check("wheel health reads: withdrawal returns native readers to their own source without saving remote damage", () =>
                {
                    clean(); packet(state(0, 30, 0)); f.Tick(); var report = state(1, 0, 0); report.Availability = 0; packet(report); f.Tick();
                    Require(f.Health[0].Value == 90 && f.Health[3].Value == 93, "Withdrawn health remained projected."); saved();
                });
                check("wheel health reads: preclaim parenting keeps native driver lookup", () =>
                {
                    clean(); packet(state(0, 30, 0)); player.SetParent(f.Body.transform, false); f.Tick();
                    Require(f.Health[0].Value == 90, "Already-parented driver consumed observer input."); saved();
                });
                check("wheel health reads: missing state and cleared streams retain native lookup", () =>
                {
                    clean(); f.Tick(); Require(f.Health[0].Value == 90, "Missing state invented tyre health.");
                    packet(state(0, 30, 0)); Call(vehicles, "ClearConditionStreams"); f.Tick();
                    Require(f.Health[0].Value == 90, "Cleared stream still supplied shared health."); saved();
                });
                check("wheel health reads: host and disconnected sessions retain native lookup", () =>
                {
                    clean(); packet(state(0, 30, 0)); SetProperty(session, "IsHost", true); f.Tick();
                    Require(f.Health[0].Value == 90, "Host consumed observer state.");
                    SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Idle); f.Tick();
                    Require(f.Health[0].Value == 90, "Disconnected guest consumed shared health."); saved();
                });
                check("wheel health reads: replacing or unregistering the body retires old readers", () =>
                {
                    clean(); packet(state(0, 30, 0)); Set(item, "Body", Get(original, "Body")); f.Tick();
                    Require(f.Health[0].Value == 90, "Old wheel read through a replacement body."); Set(item, "Body", f.Body);
                    items.Remove(VehicleId); f.Tick(); Require(f.Health[0].Value == 90, "Unregistered wheel kept shared input."); items[VehicleId] = item; saved();
                });
                check("wheel health reads: projection preserves native source and warmed cache", () =>
                {
                    clean(); f.Tick(); var read = NativeBagPartChecks.State(f.Wheels[0], "State 1").Actions[12];
                    var target = Get(read, "gameObject"); var cache = Get(read, "fsm"); var last = Get(read, "goLastFrame");
                    packet(state(0, 30, 0)); f.Tick();
                    Require(ReferenceEquals(target, Get(read, "gameObject")) && ReferenceEquals(cache, Get(read, "fsm"))
                        && ReferenceEquals(last, Get(read, "goLastFrame")) && f.Health[0].Value == 30,
                        "Projection rewired native lookup/cache."); saved();
                });
                foreach (string change in new[] { "removed", "disabled" })
                    check("wheel health reads: " + change + " flat reader fails preparation and recovers after repair", () =>
                    {
                        clean(); packet(state(0, 30, 0)); var flat = NativeBagPartChecks.State(f.Wheels[0], "Flat friction");
                        var read = flat.Actions[3];
                        try
                        {
                            if (change == "removed") { flat.Actions[3] = new WheelHealthQuiet(); flat.Actions[3].Init(flat); }
                            else read.Enabled = false;
                            Require(!(bool)guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true }) && !f.Wheels[0].enabled,
                                "Missing/disabled read silently retained guest health lookup."); saved();
                        }
                        finally { flat.Actions[3] = read; read.Enabled = true; }
                        Require((bool)guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true }), "Repaired reader did not prepare.");
                        NativeBagPartChecks.Fire(f.Wheels[0], "State 1"); f.Tick();
                        Require(f.Wheels[0].enabled && f.Health[0].Value == 30, "Repaired reader did not resume accepted health."); saved();
                    });
                foreach (string field in new[] { "storeValue", "variableName", "everyFrame" })
                    check("wheel health reads: changed " + field + " pauses the consumer without touching the saved tyre", () =>
                    {
                        clean(); packet(state(0, 30, 0)); var read = NativeBagPartChecks.State(f.Wheels[0], "State 1").Actions[12];
                        object previous = Get(read, field);
                        try
                        {
                            Set(read, field, field == "storeValue" ? (object)f.Saved[0] : field == "variableName" ? new FsmString { Value = "OtherSavedHealth" } : (object)false);
                            read.OnEnter(); Require(!f.Wheels[0].enabled && !f.Wheels[0].Fsm.RestartOnEnable, "Changed reader did not pause its graph."); saved();
                        }
                        finally { Set(read, field, previous); }
                        Require((bool)guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true }), "Restored reader did not recover.");
                        NativeBagPartChecks.Fire(f.Wheels[0], "State 1"); f.Tick();
                        Require(f.Wheels[0].enabled && f.Health[0].Value == 30, "Repaired reader did not resume shared health."); saved();
                    });
                RunWheelReaderSafetyChecks(check, f, item, vehicles, guard, clean, packet, state, saved);
            }
            finally
            {
                items[VehicleId] = original; player.SetParent(parent, false); player.position = position; reset();
            }
        }

        private sealed class WheelHealthFixture : IDisposable
        {
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM[] Wheels = new PlayMakerFSM[4];
            internal readonly FsmFloat[] Health = new FsmFloat[4], Saved = new FsmFloat[4];
            private readonly FsmInt[] _types = new FsmInt[4];
            internal readonly Component[] Physics = new Component[4];
            internal WheelHealthFixture(bool rimPhysics = false)
            {
                var root = new GameObject("CORRIS"); root.SetActive(false);
                Body = root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                var rows = ReadOccupancyRows("wheel-health-probe.json");
                var native = new Dictionary<PlayMakerFSM, Dictionary<string, object>>();
                foreach (Dictionary<string, object> row in rows)
                {
                    var current = root.transform; var parts = ((string)row["path"]).Split('/');
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var child = current.Find(parts[i]);
                        if (child == null) { child = new GameObject(parts[i]).transform; child.SetParent(current, false); }
                        current = child;
                    }
                    var fsm = NativeBagPartChecks.MakeFsm(current.gameObject, row); fsm.Fsm.Init(fsm); native.Add(fsm, row);
                    int wheel = Array.IndexOf(new[] { "WHEELc_FL", "WHEELc_FR", "WHEELc_RL", "WHEELc_RR" }, current.name);
                    if (wheel >= 0)
                    {
                        if (fsm.FsmName == "Condition") { Wheels[wheel] = fsm; Health[wheel] = fsm.FsmVariables.FindFsmFloat("Health"); }
                        else _types[wheel] = fsm.FsmVariables.FindFsmInt("TireType");
                    }
                }
                for (int i = 0; i < 4; i++)
                {
                    var wheel = Wheels[i]; var target = wheel.transform.Find("tire/VINP_Wheel" + new[] { "FL", "FR", "RL", "RR" }[i]).gameObject;
                    Saved[i] = target.GetComponent<PlayMakerFSM>().FsmVariables.FindFsmFloat("TireHealth");
                    wheel.FsmVariables.FindFsmGameObject("ThisTire").Value = target;
                    var sound = new GameObject("inert flat sound"); sound.transform.SetParent(wheel.transform, false);
                    wheel.FsmVariables.FindFsmGameObject("FlatSound").Value = sound;
                    foreach (Dictionary<string, object> stateRow in (IEnumerable)native[wheel]["states"])
                    {
                        string name = (string)stateRow["name"]; var raw = (List<object>)stateRow["actions"];
                        var state = NativeBagPartChecks.State(wheel, name); var actions = new FsmStateAction[raw.Count];
                        for (int j = 0; j < raw.Count; j++)
                        {
                            bool run = name == "State 1" && (j == 0 || j == 11 || j == 12 || j == 14 || j == 15)
                                || name == "Flat friction" && (j == 0 || j == 3) || name == "Check rim" || name == "Sound";
                            actions[j] = run ? (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null,
                                new object[] { (Dictionary<string, object>)raw[j], wheel }) : new WheelHealthQuiet();
                        }
                        state.Actions = actions; foreach (var action in actions) action.Init(state);
                    }
                    if (rimPhysics) AddRimPhysics(i, native[wheel]);
                }
                root.SetActive(true); foreach (var fsm in native.Keys) NativeBagPartChecks.Start(fsm);
            }
            internal void Seed()
            {
                for (int i = 0; i < 4; i++)
                {
                    Saved[i].Value = 90 + i; _types[i].Value = 1;
                    NativeBagPartChecks.Fire(Wheels[i], "State 1");
                }
            }
            internal void Tick() { foreach (var wheel in Wheels) { wheel.Fsm.Update(); wheel.Fsm.FixedUpdate(); } }
            private void AddRimPhysics(int index, Dictionary<string, object> row)
            {
                Type? type = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if ((type = assembly.GetType("Wheel")) != null) break;
                if (type == null) throw new InvalidOperationException("Native Wheel type missing.");
                var wheel = Wheels[index]; Physics[index] = wheel.gameObject.AddComponent(type);
                ((Behaviour)Physics[index]).enabled = false;
                var target = new FsmObject { Name = "Wheel", UseVariable = true, ObjectType = type, Value = Physics[index] };
                wheel.FsmVariables.ObjectVariables = new[] { target };
                var state = NativeBagPartChecks.State(wheel, "Rim friction");
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    if ((string)stateRow["name"] != state.Name) continue;
                    var raw = (List<object>)stateRow["actions"];
                    for (int i = 0; i < raw.Count; i++)
                    {
                        var actionRow = (Dictionary<string, object>)raw[i]; var parameters = new List<object>();
                        Dictionary<string, object>? property = null;
                        foreach (Dictionary<string, object> p in (IEnumerable)actionRow["parameters"])
                            if ((string)p["type"] == "FsmProperty") property = (Dictionary<string, object>)p["value"]; else parameters.Add(p);
                        var copy = new Dictionary<string, object>(actionRow); copy["parameters"] = parameters;
                        var action = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { copy, wheel });
                        var input = (Dictionary<string, object>)property!["FloatParameter"];
                        var value = Convert.ToBoolean(input["useVariable"]) ? wheel.FsmVariables.FindFsmFloat((string)input["name"])
                            : new FsmFloat(Convert.ToSingle(input["value"]));
                        Set(action, "targetProperty", new FsmProperty { TargetObject = target, TargetType = type,
                            TargetTypeName = (string)property["TargetTypeName"], PropertyName = (string)property["PropertyName"],
                            setProperty = Convert.ToBoolean(property["setProperty"]), FloatParameter = value });
                        state.Actions[i] = action; action.Init(state);
                    }
                }
            }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(Body.gameObject);
        }
        private sealed class WheelHealthQuiet : FsmStateAction { public override void OnEnter() => Finish(); }
    }
}
