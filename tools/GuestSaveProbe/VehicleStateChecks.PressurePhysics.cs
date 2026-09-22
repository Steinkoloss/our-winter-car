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
        private static void RunPressurePhysicsChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            using (var f = new PressurePhysicsFixture())
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Path", "CORRIS"); Set(item, "Body", f.Body); Set(item, "IsVehicle", true);
                Action clean = () =>
                {
                    reset(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected); SetProperty(session, "LocalPlayerId", (byte)3);
                    Set(item, "Body", f.Body); Set(item, "RemoteOwner", (byte)255); Set(item, "LocallyOwned", false);
                    Call(vehicles, "ClearConditionStreams"); f.Seed(); capture.Packets.Clear();
                };
                Func<byte, ushort, VehicleCondition> state = (pressure, seq) => new VehicleCondition { Availability = VehicleCondition.AvailableAll,
                    VehicleId = VehicleId, OwnerPlayerId = 0, Sequence = seq, TirePressure = pressure };
                Action<VehicleCondition> packet = message => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Action tick = () => { Set(item, "NextTirePressureProbeAt", 0f); Call(vehicles, "UpdateVehicleCondition", session); };
                Action retry = tick;
                Action untouched = () => { for (int i = 0; i < 4; i++) Require(f.Read(i, "pressure") == 37 + i,
                    "A rejected or unavailable pressure application changed a wheel."); };
                foreach (byte value in new byte[] { 0, 1, 127, 190, 230, 254, 255 })
                {
                    byte expected = value;
                    check("pressure physics: native TIRES copies wire " + expected + " to all four actual Wheel components", () =>
                    {
                        clean(); packet(state(expected, 1)); f.Expect(expected, 190);
                        Require(f.Data.ActiveStateName == "WheelFriction" && !f.Enabled.Value, "Pressure replay toggled simulation or missed its native state.");
                    });
                }
                check("pressure physics: missing pressure bit never dispatches native TIRES", () =>
                {
                    clean(); var report = state(0, 1); report.Availability = VehicleCondition.AvailableFL; packet(report); tick(); untouched();
                    Require(f.Pressure.Value == 190 && f.Data.ActiveStateName == "Probe idle", "Absent pressure changed native scalar or event state.");
                });
                check("pressure physics: withdrawn pressure stops repair until a fresh available report", () =>
                {
                    clean(); packet(state(230, 1)); var report = state(0, 2); report.Availability = 0; packet(report); f.Seed(); tick(); untouched();
                    packet(state(0, 3)); f.Expect(0, 190);
                });
                check("pressure physics: native apply preserves enabled simulation and custom optimum", () =>
                { clean(); f.Enabled.Value = true; f.Optimum.Value = 215; packet(state(230, 1)); f.Expect(230, 215); Require(f.Enabled.Value, "Replay toggled enabled simulation."); });
                check("pressure physics: host accepts the current guest driver's pressure into native wheels", () =>
                {
                    clean(); SetProperty(session, "IsHost", true); SetProperty(session, "State", SessionState.Hosting); SetProperty(session, "LocalPlayerId", (byte)0);
                    Set(item, "RemoteOwner", (byte)1); var message = state(225, 1); message.OwnerPlayerId = 1;
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(message), Channel.ReliableOrdered); f.Expect(225, 190);
                });
                check("pressure physics: unchanged observer updates do not repeatedly enter TIRES", () =>
                { clean(); packet(state(230, 1)); NativeBagPartChecks.Fire(f.Data, "Probe idle"); tick(); tick(); Require(f.Data.ActiveStateName == "Probe idle", "Unchanged pressure replayed native actions."); });
                check("pressure physics: drift and native optimum changes self-heal without another packet", () =>
                { clean(); packet(state(230, 1)); Set(f.Wheels[2], "pressure", 15f); f.Optimum.Value = 220; tick(); f.Expect(230, 220); });
                check("pressure physics: duplicate stale and forged packets cannot change native pressure", () =>
                {
                    clean(); packet(state(230, 2)); packet(state(0, 2)); packet(state(0, 1));
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(state(0, 3)), Channel.ReliableOrdered); f.Expect(230, 190);
                });
                check("pressure physics: local and seated pre-claim drivers keep their native wheel values", () =>
                {
                    clean(); Set(item, "LocallyOwned", true); packet(state(230, 1)); tick(); untouched();
                    clean(); player.SetParent(f.Body.transform, false); packet(state(230, 1)); tick(); untouched();
                });
                check("pressure physics: changed ownership and disconnected sessions cannot apply old pressure", () =>
                {
                    clean(); packet(state(230, 1)); f.Seed(); Set(item, "RemoteOwner", (byte)2); tick(); untouched();
                    Set(item, "RemoteOwner", (byte)255); SetProperty(session, "State", SessionState.Idle); tick(); untouched();
                });
                check("pressure physics: approved parked pressure repairs drift after release", () =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)1); var message = state(230, 1); message.OwnerPlayerId = 1; packet(message);
                    Call(Get(vehicles, "_items"), "OnRemoteItemTransform", new ItemTransform { ItemId = VehicleId,
                        OwnerPlayerId = 1, Sequence = 10, Flags = ItemTransform.FlagFinal });
                    f.Seed(); tick(); f.Expect(230, 190);
                    Set(item, "RemoteOwner", (byte)2); f.Seed(); tick(); untouched();
                });
                check("pressure physics: a missing fourth target defers every write and recovers from accepted state", () =>
                {
                    clean(); var target = f.Targets[3]; var saved = target.Value; target.Value = null;
                    try { packet(state(230, 1)); untouched(); } finally { target.Value = saved; }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: a late pressure FSM binds despite earlier condition discovery", () =>
                {
                    clean(); f.Data.FsmName = "Not ready";
                    try { packet(state(230, 1)); untouched(); } finally { f.Data.FsmName = "Data"; }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: an altered final action prevents partial writes and repairs safely", () =>
                {
                    clean(); var property = (FsmProperty)Get(f.Actions[7], "targetProperty"); var name = property.PropertyName; property.PropertyName = "radius";
                    try { packet(state(230, 1)); untouched(); } finally { property.PropertyName = name; }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: changed native enablement blocks the whole event and restores after repair", () =>
                {
                    clean(); f.Actions[7].Enabled = true;
                    try { packet(state(230, 1)); untouched(); } finally { f.Actions[7].Enabled = false; }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: native action enablement is restored after every shared update", () =>
                {
                    clean(); packet(state(230, 1)); f.Expect(230, 190);
                    for (int i = 0; i < 8; i++)
                        Require(f.Actions[i].Enabled == (i < 2) && (i < 2 || !NativeBagPartChecks.State(f.Data, "WheelFriction").ActiveActions.Contains(f.Actions[i])),
                            "Replay left a vanilla-disabled write enabled or scheduled.");
                    f.Seed(); f.Data.SendEvent("TIRES"); Require(f.Read(0, "pressure") == 190, "Native FL write did not resume.");
                    for (int i = 1; i < 4; i++) Require(f.Read(i, "pressure") == 37 + i, "Ordinary native TIRES enabled another wheel.");
                });
                check("pressure physics: drift retries are bounded but a new accepted packet applies immediately", () =>
                {
                    clean(); packet(state(230, 1)); Set(f.Wheels[0], "pressure", 15f);
                    Set(item, "NextTirePressureProbeAt", Time.unscaledTime + 100f); Call(vehicles, "UpdateVehicleCondition", session);
                    Require(f.Read(0, "pressure") == 15, "Pressure retry ignored its timer.");
                    packet(state(225, 2)); f.Expect(225, 190);
                });
                check("pressure physics: wrong wheel identity cannot redirect pressure", () =>
                {
                    clean(); var saved = f.Targets[3].Value; f.Targets[3].Value = f.Wheels[0];
                    try { packet(state(230, 1)); untouched(); } finally { f.Targets[3].Value = saved; }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: an aliased property input cannot overwrite the native optimum", () =>
                {
                    clean(); var property = (FsmProperty)Get(f.Actions[0], "targetProperty"); var input = property.FloatParameter;
                    property.FloatParameter = f.Optimum;
                    try { packet(state(230, 1)); untouched(); Require(f.Optimum.Value == 190, "Aliased source changed optimum."); }
                    finally { property.FloatParameter = input; }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: a duplicate native source invalidates a previously bound graph", () =>
                {
                    clean(); packet(state(230, 1)); f.Seed();
                    var duplicate = NativeBagPartChecks.MakeFsm(f.Data.gameObject, NativeBagPartChecks.Find(ReadOccupancyRows("tire-pressure-physics-probe.json"),
                        "CORRIS/Simulation/Systems/TirePressure", "Data"));
                    try { tick(); untouched(); } finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: a changed TIRES transition cannot enter the native enable toggle", () =>
                {
                    clean(); FsmTransition? target = null; foreach (var transition in f.Data.Fsm.GlobalTransitions) if (transition.EventName == "TIRES") target = transition;
                    string saved = target!.ToState; target.ToState = "State 1";
                    try { packet(state(230, 1)); untouched(); Require(!f.Enabled.Value, "Invalid event toggled simulation."); }
                    finally { target.ToState = saved; }
                    retry(); f.Expect(230, 190);
                });
                check("pressure physics: disabled graph and invalid optimum defer without losing accepted state", () =>
                {
                    clean(); f.Data.enabled = false;
                    try { packet(state(230, 1)); untouched(); } finally { f.Data.enabled = true; }
                    f.Optimum.Value = float.NaN; retry(); untouched(); f.Optimum.Value = 190; retry(); f.Expect(230, 190);
                });
                check("pressure physics: replacing the body and unregistering the car cannot reuse native targets", () =>
                {
                    clean(); packet(state(230, 1)); f.Seed(); Set(item, "Body", Get(original, "Body")); tick(); untouched();
                    Set(item, "Body", f.Body); items.Remove(VehicleId); retry(); untouched(); items[VehicleId] = item;
                });
                check("pressure physics: session clear retires the binding and deferred pressure", () =>
                {
                    clean(); packet(state(230, 1)); Call(vehicles, "ClearConditionStreams"); f.Seed(); tick(); untouched();
                    Require(Get(item, "NativeTirePressure") == null && (float)Get(item, "NextTirePressureProbeAt") == 0, "Teardown retained a native binding.");
                });
            }
            finally { player.SetParent(((Component)world).transform, false); items[VehicleId] = original; reset(); }
        }

        private sealed class PressurePhysicsFixture : IDisposable
        {
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Data;
            internal readonly Component[] Wheels = new Component[4];
            internal readonly FsmObject[] Targets = new FsmObject[4];
            internal readonly FsmStateAction[] Actions = new FsmStateAction[8];
            internal readonly FsmFloat Pressure, Optimum;
            internal readonly FsmBool Enabled;
            internal PressurePhysicsFixture()
            {
                var row = NativeBagPartChecks.Find(ReadOccupancyRows("tire-pressure-physics-probe.json"), "CORRIS/Simulation/Systems/TirePressure", "Data");
                var root = new GameObject("CORRIS"); root.SetActive(false); Body = root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                Type? wheelType = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if ((wheelType = assembly.GetType("Wheel")) != null) break;
                if (wheelType == null) throw new InvalidOperationException("Native Wheel type missing.");
                Data = NativeBagPartChecks.MakeFsm(PressurePath(root.transform, (string)row["path"]).gameObject, row);
                Pressure = Data.FsmVariables.FindFsmFloat("Pressure"); Optimum = Data.FsmVariables.FindFsmFloat("PressureOptimal"); Enabled = Data.FsmVariables.FindFsmBool("Enabled");
                var rule = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("VehicleTirePressure", Static).GetValue(null, null);
                int index = 0; foreach (var wheel in (IEnumerable)Get(rule, "Wheels"))
                {
                    var transform = PressurePath(root.transform, (string)Get(wheel, "Path")); transform.gameObject.SetActive(false);
                    Wheels[index] = transform.gameObject.AddComponent(wheelType); ((Behaviour)Wheels[index]).enabled = false;
                    Targets[index] = new FsmObject { Name = (string)Get(wheel, "ObjectVariable"), UseVariable = true, ObjectType = wheelType, Value = Wheels[index] }; index++;
                }
                Data.FsmVariables.ObjectVariables = Targets; Data.Fsm.Init(Data);
                var nativeState = NativeBagPartChecks.State(Data, "WheelFriction");
                foreach (Dictionary<string, object> state in (IEnumerable)row["states"])
                {
                    if ((string)state["name"] != "WheelFriction") continue;
                    int i = 0; foreach (Dictionary<string, object> raw in (IEnumerable)state["actions"])
                    {
                        Dictionary<string, object>? property = null; var parameters = new List<object>();
                        foreach (Dictionary<string, object> p in (IEnumerable)raw["parameters"])
                            if ((string)p["type"] == "FsmProperty") property = (Dictionary<string, object>)p["value"]; else parameters.Add(p);
                        var copy = new Dictionary<string, object>(raw); copy["parameters"] = parameters;
                        Actions[i] = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { copy, Data });
                        var input = (Dictionary<string, object>)property!["FloatParameter"];
                        Set(Actions[i], "targetProperty", new FsmProperty { TargetObject = Targets[i / 2], TargetType = wheelType,
                            TargetTypeName = (string)property["TargetTypeName"], PropertyName = (string)property["PropertyName"],
                            setProperty = Convert.ToBoolean(property["setProperty"]), FloatParameter = Data.FsmVariables.FindFsmFloat((string)input["name"]) });
                        i++;
                    }
                }
                nativeState.Actions = Actions; foreach (var action in Actions) action.Init(nativeState);
                root.SetActive(true); NativeBagPartChecks.Start(Data); Seed();
            }
            private static Transform PressurePath(Transform root, string path)
            {
                var current = root; var names = path.Split('/');
                for (int i = 1; i < names.Length; i++)
                { var child = current.Find(names[i]); if (child == null) { child = new GameObject(names[i]).transform; child.SetParent(current, false); } current = child; }
                return current;
            }
            internal void Seed()
            { Pressure.Value = Optimum.Value = 190; Enabled.Value = false; for (int i = 0; i < 4; i++) { Set(Wheels[i], "pressure", (float)(37 + i)); Set(Wheels[i], "optimalPressure", 190f); } NativeBagPartChecks.Fire(Data, "Probe idle"); }
            internal float Read(int index, string field) => (float)Get(Wheels[index], field);
            internal void Expect(float pressure, float optimum)
            {
                for (int i = 0; i < 4; i++)
                    if (Read(i, "pressure") != pressure || Read(i, "optimalPressure") != optimum)
                    {
                        string detail = "";
                        var snapshot = (string)Core.GetType("WinterMP.Core.Diagnostics.SyncEventLog", true).GetMethod("GetSnapshot", Static).Invoke(null, null);
                        foreach (string line in snapshot.Split('\n')) if (line.Contains("vehicle-tire-pressure-unavailable")) detail = line;
                        throw new InvalidOperationException("Native wheel " + i + " pressure " + Read(i, "pressure") + ", optimum "
                            + Read(i, "optimalPressure") + ", state " + Data.ActiveStateName + ": " + detail);
                    }
            }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(Body.gameObject);
        }
    }
}
