using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunHeatChecks(Action<string, Action> check, object item, object vehicles, SessionManager session,
            Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string name in fields) saved[name] = Get(item, name);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool before = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new HeatFixture())
            try
            {
                reset(); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item; protect(false); mode(true, 255); f.Start();
                ushort sequence = 0;
                Func<ushort, float, VehicleState> receive = (rpm, torque) => {
                    mode(true, 1); var message = State(1, sequence++, rpm); message.TorqueAvailable = true; message.EngineTorque = torque;
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", message)!, "Native heat telemetry rejected."); f.Rebind(); return message; };
                check("host heat: complete native graph binds both RPM operands and torque", () =>
                {
                    receive(3000, 120); Require(Get(item, "NativeHeat") != null, "Native heating graph did not bind."); f.OriginalInputs();
                });
                foreach (ushort rpm in new ushort[] { 0, 180, 400, 401, 3000, 6500 })
                    foreach (float torque in new[] { -100f, 0f, 120f })
                    {
                        ushort revs = rpm; float load = torque;
                        check("host heat: native local and delegated heating match at " + revs + " RPM and torque " + load, () =>
                        {
                            mode(true, 255); f.Rpm.Value = revs; f.SetTorque(load); var expected = f.Cycle();
                            receive(revs, load); f.Rpm.Value = 0; f.SetTorque(5); var actual = f.Cycle();
                            Require(actual[0] == expected[0] && actual[1] == expected[1], "Delegated heat differs from the native local calculation.");
                            Require(f.Heat.ActiveStateName == (revs > 400 ? "State 1" : "State 2"), "Native start threshold changed.");
                            Require(f.Rpm.Value == 0 && f.Friction.Value == 3000 && f.Power.Value == (revs > 400 ? 5 : f.Power.Value),
                                "Scoped inputs changed global RPM, host friction or native drivetrain scratch.");
                            Require(revs <= 400 || actual[0] > 30, "Native heat writer did not advance temperature."); f.OriginalInputs();
                        });
                    }
                check("host heat: host friction changes native heating under the same guest load", () =>
                {
                    receive(3000, 120); f.Rpm.Value = 0; f.Cycle(); float lowFriction = f.Rate.Value;
                    f.Friction.Value = 1000;
                    try { f.Cycle(); Require(f.Rate.Value > lowFriction, "Guest load bypassed authoritative host friction."); }
                    finally { f.Friction.Value = 3000; }
                });
                check("host heat: load changes heating without writing on packet arrival", () =>
                {
                    f.Temperature.Value = 30; receive(3000, 40); Require(f.Temperature.Value == 30, "A packet directly heated the host.");
                    f.Cycle(); float light = f.Rate.Value; receive(3000, 160); f.Cycle();
                    Require(f.Rate.Value > light, "Heating ignored engine load.");
                });
                check("host heat: native upper and lower clamps contain extreme finite torque", () =>
                {
                    receive(6500, float.MaxValue); f.Rpm.Value = 0; f.Cycle(); Require(f.Rate.Value == 7, "Native heat upper clamp changed.");
                    receive(6500, float.MinValue); f.Cycle(); Require(f.Rate.Value == 1.1f, "Native heat lower clamp changed.");
                });
                check("host heat: native running stop threshold retains its hysteresis", () =>
                {
                    receive(3000, 120); f.Rpm.Value = 0; f.Cycle(); receive(100, 120); f.Heat.Fsm.Update();
                    Require(f.Heat.ActiveStateName == "State 1", "Equality incorrectly stopped heating.");
                    receive(99, 120); f.Heat.Fsm.Update(); Require(f.Heat.ActiveStateName == "State 2", "Native stop threshold did not stop heating.");
                    receive(400, 120); f.Heat.Fsm.Update(); Require(f.Heat.ActiveStateName == "State 2", "Equality incorrectly restarted heating.");
                    receive(401, 120); f.Heat.Fsm.Update(); Require(f.Heat.ActiveStateName == "State 1", "Native restart threshold failed."); f.OriginalInputs();
                });
                check("host heat: missing torque expired and wrong-owner samples use native stop behavior", () =>
                {
                    Action stop = () => {
                        f.Rpm.Value = 6500; f.Heat.Fsm.Update(); Require(f.Heat.ActiveStateName == "State 2", "Incomplete sample continued heating.");
                        float temperature = f.Temperature.Value; f.Heat.Fsm.Update(); Require(f.Temperature.Value == temperature, "Stopped graph kept adding heat."); f.OriginalInputs(); };
                    receive(3000, 120); f.Rpm.Value = 0; f.Cycle(); Set(item, "RemoteEngineUntil", -1f); stop();
                    receive(3000, 120); f.Cycle(); Set(item, "AcceptedVehicleState", null); stop();
                    receive(3000, 120); f.Cycle(); Set(item, "RemoteOwner", (byte)2); stop();
                    receive(3000, 120); f.Cycle();
                    var missing = State(1, sequence++, 6500); Require((bool)Call(vehicles, "OnRemoteVehicleState", missing)!, "Unavailable load packet rejected."); stop();
                });
                check("host heat: copied telemetry rejects stale forged and malformed load", () =>
                {
                    var accepted = receive(3000, 120); accepted.EngineTorque = 900; accepted.Rpm = 65000;
                    var stale = State(1, accepted.Sequence, 65000); stale.TorqueAvailable = true; stale.EngineTorque = 900;
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleState", stale)!, "Duplicate heat report accepted.");
                    stale.Sequence++; stale.OwnerPlayerId = 2; Require(!(bool)Call(vehicles, "OnRemoteVehicleState", stale)!, "Foreign heat owner accepted.");
                    stale.OwnerPlayerId = 1; stale.EngineTorque = float.NaN; Require(!(bool)Call(vehicles, "OnRemoteVehicleState", stale)!, "Nonfinite torque accepted.");
                    f.Rpm.Value = 0; f.Cycle(); Require(Mathf.Abs(f.Rate.Value - 3.35f) < .0001f, "Rejected/mutated telemetry changed accepted heat.");
                    mode(true, 2); var next = State(2, 0, 3000); next.TorqueAvailable = true; next.EngineTorque = 40;
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", next)!, "New driver sequence zero was rejected.");
                    f.Cycle(); Require(Mathf.Abs(f.Rate.Value - 1.85f) < .0001f, "Handoff mixed old torque with the new driver.");
                });
                check("host heat: local seating ownership guest mode and disconnect restore native inputs", () =>
                {
                    receive(3000, 120); f.Rpm.Value = 0;
                    Action local = () => { f.Cycle(); Require(f.Temperature.Value == 30 && f.Heat.ActiveStateName == "State 2", "Remote load overrode local/native authority."); };
                    Set(item, "LocallyOwned", true); local(); Set(item, "LocallyOwned", false);
                    var world = World.GetProperty("Instance", Static).GetValue(null, null); var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent;
                    player.SetParent(f.Body.transform, false); try { local(); } finally { player.SetParent(parent, false); }
                    mode(false, 1); local(); mode(true, 1); SetProperty(session, "State", SessionState.Idle); local(); mode(true, 1);
                    protect(true); try { local(); } finally { protect(false); }
                });
                check("host heat: torque capture uses the active native producer and preserves precision", () =>
                {
                    mode(true, 255); f.Rpm.Value = 3000; f.SetTorque(-12.375f); f.Cycle(); f.Heat.enabled = true;
                    try
                    {
                        var value = State(0, 0, 3000); CallStatic("CaptureHeatTelemetry", item, value);
                        Require(value.TorqueAvailable && value.EngineTorque == -12.375f, "Native torque capture read currentPower or a dashboard value.");
                        f.Power.Value = float.NaN; CallStatic("CaptureHeatTelemetry", item, value);
                        Require(!value.TorqueAvailable && value.EngineTorque == 0, "Invalid torque did not clear availability.");
                        f.Power.Value = 120; f.Rpm.Value = 0; NativeBagPartChecks.Fire(f.Heat, "State 2");
                        CallStatic("CaptureHeatTelemetry", item, value); Require(!value.TorqueAvailable && value.EngineTorque == 0, "Stopped producer exposed stale torque.");
                    }
                    finally { f.Heat.enabled = false; }
                });
                check("host heat: native helper exception restores both global RPM references", () =>
                {
                    receive(3000, 120); var action = NativeBagPartChecks.State(f.Heat, "State 1").Actions[3]; object?[] args = { action, null };
                    Vehicles.GetNestedType("NativeHeatHooks", System.Reflection.BindingFlags.NonPublic).GetMethod("BeforeHeatRead", Static).Invoke(null, args); Require(args[1] != null, "Heat read was not scoped.");
                    var error = new InvalidOperationException("native heat probe failure");
                    var returned = Vehicles.GetNestedType("NativeHeatHooks", System.Reflection.BindingFlags.NonPublic).GetMethod("AfterHeatRead", Static).Invoke(null, new object[] { error, args[1]! });
                    Require(ReferenceEquals(error, returned), "Native exception was lost."); f.OriginalInputs();
                });
                foreach (string field in new[] { "operation", "float2", "storeResult", "everyFrame" })
                {
                    string name = field;
                    check("host heat: changed " + name + " rejects the complete group and repairs", () =>
                    {
                        var action = NativeBagPartChecks.State(f.Heat, "State 1").Actions[5]; object original = Get(action, name);
                        object replacement = name == "operation" ? Enum.ToObject(original.GetType(), 0) : name == "everyFrame" ? (object)false : new FsmFloat(0);
                        try { Set(action, name, replacement); f.Rebind(); Require(Get(item, "NativeHeat") == null, "Changed heating graph remained bound."); }
                        finally { Set(action, name, original); }
                        f.Rebind(); Require(Get(item, "NativeHeat") != null, "Repaired heating graph did not recover."); f.OriginalInputs();
                    });
                }
                check("host heat: changed native torque property fails capture and repairs", () =>
                {
                    var property = (FsmProperty)Get(NativeBagPartChecks.State(f.Heat, "State 1").Actions[1], "targetProperty");
                    string member = property.PropertyName; property.PropertyName = "currentPower";
                    try { f.Rebind(); Require(Get(item, "NativeHeat") == null, "Foreign torque property was accepted."); }
                    finally { property.PropertyName = member; }
                    f.Rebind(); Require(Get(item, "NativeHeat") != null, "Repaired torque property did not bind.");
                });
                check("host heat: stream cleanup removes all scoped operands", () =>
                {
                    Call(vehicles, "ClearVehicleStateStreams"); Require(Get(item, "NativeHeat") == null, "Heat binding survived cleanup.");
                    f.Rpm.Value = 0; f.Cycle(); Require(f.Temperature.Value == 30, "Cleared heat stream remained active."); f.OriginalInputs();
                });
            }
            finally { CallStatic("ClearHeatBinding", item); foreach (string name in fields) Set(item, name, saved[name]); protect(before); reset(); }
        }

        private sealed class HeatFixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly Component _drive;
            private readonly float _savedRpm, _savedTemperature;
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Heat;
            internal readonly FsmFloat Rpm, Temperature, Friction, Power, Rate;
            internal object Item = null!;
            internal HeatFixture()
            {
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../vehicle-heat-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                var row = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/CarData", "HeatGeneration");
                Rpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM"); Temperature = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp");
                _savedRpm = Rpm.Value; _savedTemperature = Temperature.Value; Rpm.Value = 0;
                _root = new GameObject("CORRIS"); _root.SetActive(false); Body = _root.AddComponent<Rigidbody>(); Body.useGravity = false; Body.isKinematic = true;
                Type? driveType = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if ((driveType = assembly.GetType("Drivetrain")) != null) break;
                _drive = _root.AddComponent(driveType ?? throw new InvalidOperationException("Native drivetrain absent.")); ((Behaviour)_drive).enabled = false;
                foreach (var component in _root.GetComponents<Behaviour>()) component.enabled = false;
                var sim = new GameObject("Simulation"); sim.transform.SetParent(_root.transform, false); var data = new GameObject("CarData"); data.transform.SetParent(sim.transform, false);
                Heat = NativeBagPartChecks.MakeFsm(data, row);
                var driveReference = new FsmObject { Name = "CarDrivetrain", UseVariable = true, ObjectType = driveType, Value = _drive };
                Heat.FsmVariables.ObjectVariables = new[] { driveReference };
                var friction = data.AddComponent<PlayMakerFSM>(); friction.enabled = false; Set(friction, "fsm", new Fsm()); friction.FsmName = "EngineFriction";
                friction.Fsm.StartState = "Idle"; friction.Fsm.States = new[] { new FsmState(friction.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
                Friction = new FsmFloat { Name = "FrictionToHeat", UseVariable = true, Value = 3000 }; friction.FsmVariables.FloatVariables = new[] { Friction };
                _root.SetActive(true); Heat.Fsm.Init(Heat); friction.Fsm.Init(friction); NativeBagPartChecks.Start(friction);
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(Heat, (string)stateRow["name"]); var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> raw in (IEnumerable)stateRow["actions"])
                    {
                        Dictionary<string, object>? property = null; var parameters = new List<object>();
                        foreach (Dictionary<string, object> parameter in (IEnumerable)raw["parameters"])
                            if ((string)parameter["type"] == "FsmProperty") property = (Dictionary<string, object>)parameter["value"]; else parameters.Add(parameter);
                        var copy = new Dictionary<string, object>(raw); copy["parameters"] = parameters;
                        var action = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { copy, Heat });
                        foreach (var field in action.GetType().GetFields())
                            if (field.FieldType == typeof(FsmFloat) && field.GetValue(action) is FsmFloat value && value.UseVariable)
                            { if (value.Name == "RPM") field.SetValue(action, Rpm); else if (value.Name == "EngineTemp") field.SetValue(action, Temperature); }
                        if (property != null)
                        {
                            var output = (Dictionary<string, object>)property["FloatParameter"];
                            Set(action, "targetProperty", new FsmProperty { TargetObject = driveReference, TargetType = driveType,
                                TargetTypeName = (string)property["TargetTypeName"], PropertyName = (string)property["PropertyName"],
                                setProperty = false, FloatParameter = Heat.FsmVariables.FindFsmFloat((string)output["name"]) });
                        }
                        actions.Add(action);
                    }
                    state.Actions = actions.ToArray(); foreach (var action in state.Actions) action.Init(state);
                }
                Power = Heat.FsmVariables.FindFsmFloat("Power"); Rate = Heat.FsmVariables.FindFsmFloat("HeatingRate");
            }
            internal void Start() { NativeBagPartChecks.Start(Heat); NativeBagPartChecks.Fire(Heat, "State 2"); }
            internal void SetTorque(float value) => Set(_drive, "torque", value);
            internal float[] Cycle()
            {
                Temperature.Value = 30; Rate.Value = 0; NativeBagPartChecks.Fire(Heat, "State 2");
                for (int i = 0; i < 4; i++) Heat.Fsm.Update(); return new[] { Temperature.Value, Rate.Value };
            }
            internal void Rebind()
            {
                Set(Item, "NextHeatProbeAt", 0f); CallStatic("EnsureHeatBinding", Item);
                if (Get(Item, "NativeHeat") == null) { Set(Item, "NextHeatProbeAt", 0f); CallStatic("EnsureHeatBinding", Item); }
            }
            internal void OriginalInputs()
            {
                var running = NativeBagPartChecks.State(Heat, "State 1").Actions;
                Require(ReferenceEquals(Get(running[3], "float1"), Rpm) && ReferenceEquals(Get(running[3], "float2"), Rpm)
                    && ReferenceEquals(Get(running[5], "float2"), Power) && ReferenceEquals(Get(running[10], "float1"), Rpm)
                    && ReferenceEquals(Get(NativeBagPartChecks.State(Heat, "State 2").Actions[0], "float1"), Rpm), "Heat read left a scoped operand attached.");
            }
            public void Dispose() { Rpm.Value = _savedRpm; Temperature.Value = _savedTemperature; UnityEngine.Object.DestroyImmediate(_root); }
        }
    }
}
