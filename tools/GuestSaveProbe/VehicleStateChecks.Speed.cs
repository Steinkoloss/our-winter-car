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
        private static void RunSpeedChecks(Action<string, Action> check, object item, object vehicles, SessionManager session,
            Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string name in fields) saved[name] = Get(item, name);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool before = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new SpeedFixture())
            try
            {
                reset(); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item; protect(false); mode(true, 255); f.Start();
                ushort sequence = 0;
                Func<ushort, VehicleState> receive = speed => {
                    mode(true, 1); var value = State(1, sequence++, 3000); value.MovementSpeedAvailable = true;
                    value.MovementSpeedTenthsKmh = speed; value.SpeedTenthsKmh = 1900;
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", value)!, "Movement sample rejected."); f.Rebind(); return value; };
                check("host cooling speed: complete producer and both readers bind", () =>
                { receive(800); Require(Get(item, "NativeCooling") != null, "Cooling speed did not bind."); f.OriginalInputs(); });
                foreach (ushort speed in new ushort[] { 0, 20, 21, 200, 800, ushort.MaxValue })
                    foreach (float ambient in new[] { -20f, 30f, 45f })
                    {
                        ushort movement = speed; float area = ambient;
                        check("host cooling speed: native local and delegated airflow match at " + movement + " tenths kmh and ambient " + area, () =>
                        {
                            mode(true, 255); f.Speed.Value = movement * .1f; var expected = f.Air(area);
                            receive(movement); f.Speed.Value = 12; var actual = f.Air(area);
                            Require(actual[0] == expected[0] && actual[1] == expected[1], "Delegated airflow differs: " + expected[0] + "/" + expected[1] + " versus " + actual[0] + "/" + actual[1]);
                            Require(f.Speed.Value == 12 && f.Local("TempArea").Value == area, "Projection changed host speed or ambient input."); f.OriginalInputs();
                        });
                    }
                check("host cooling speed: packet arrival does not run cooling or change temperature", () =>
                {
                    f.Temperature.Value = 93; f.Local("CoolingAirRate").Value = .37f; receive(800);
                    Require(f.Temperature.Value == 93 && f.Local("CoolingAirRate").Value == .37f, "Packet advanced native cooling.");
                });
                check("host cooling speed: stationary hot-engine branch retains native two kmh threshold", () =>
                {
                    foreach (ushort speed in new ushort[] { 0, 19, 20, 21, 800 })
                    {
                        mode(true, 255); f.Speed.Value = speed * .1f; string expected = f.CheckTemperature(90);
                        receive(speed); f.Speed.Value = 100; string actual = f.CheckTemperature(90);
                        Require(expected == actual && actual == (speed > 20 ? "Coolant temp 2" : "Grill trigger"), "Stationary threshold at " + speed + ": native=" + expected + ", delegated=" + actual);
                        f.OriginalInputs();
                    }
                    receive(0); Require(f.CheckTemperature(84) == "Coolant temp 2", "Stationary cool engine used hot branch.");
                });
                check("host cooling speed: wheelspin does not create cooling airflow", () =>
                {
                    receive(0); f.Speed.Value = 100; var still = f.Air(-20);
                    Require(still[0] == .03f, "Wheel speed or stale host speed supplied airflow.");
                    receive(800); Require(f.Air(-20)[0] > still[0], "Actual movement failed to increase cooling.");
                });
                check("host cooling speed: missing expired and foreign samples use native stationary inputs", () =>
                {
                    Action stationary = () => { f.Speed.Value = 100; Require(f.Air(-20)[0] == .03f && f.CheckTemperature(90) == "Grill trigger", "Incomplete sample retained movement."); f.OriginalInputs(); };
                    receive(800); Set(item, "RemoteEngineUntil", -1f); stationary();
                    receive(800); Set(item, "AcceptedVehicleState", null); stationary();
                    receive(800); Set(item, "RemoteOwner", (byte)2); stationary();
                    receive(800); var unavailable = State(1, sequence++, 3000); unavailable.SpeedTenthsKmh = 1800;
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", unavailable)!, "Unavailable sample rejected."); stationary();
                });
                check("host cooling speed: copied sample stale rejection and driver handoff preserve movement", () =>
                {
                    var sample = receive(0); sample.MovementSpeedTenthsKmh = 2500;
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleState", sample)!, "Stale movement accepted.");
                    sample.Sequence++; sample.OwnerPlayerId = 2; Require(!(bool)Call(vehicles, "OnRemoteVehicleState", sample)!, "Foreign movement accepted.");
                    f.Speed.Value = 100; Require(f.Air(-20)[0] == .03f, "Caller or rejected report mutated stored speed.");
                    mode(true, 2); sample.Sequence = 0; Require((bool)Call(vehicles, "OnRemoteVehicleState", sample)!, "New driver's sequence zero rejected.");
                    Require(f.Air(-20)[0] == 2.1f, "New driver's movement did not replace old speed.");
                    var snapshot = (VehicleState)Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0)!;
                    Require(snapshot.MovementSpeedAvailable && snapshot.MovementSpeedTenthsKmh == 2500 && snapshot.SpeedTenthsKmh == 1900,
                        "Snapshot confused movement and wheel speed.");
                });
                check("host cooling speed: local ownership seating guest mode and disconnect retain native speed", () =>
                {
                    receive(0); f.Speed.Value = 100;
                    string phase = "ownership";
                    Action local = () => { float rate = f.Air(-20)[0]; string branch = f.CheckTemperature(90);
                        Require(rate > .03f && branch == "Coolant temp 2", "Native authority phase=" + phase + ", speed=" + f.Speed.Value + ", rate=" + rate + ", branch=" + branch); };
                    Set(item, "LocallyOwned", true); try { local(); } finally { Set(item, "LocallyOwned", false); }
                    var world = World.GetProperty("Instance", Static).GetValue(null, null); var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent;
                    phase = "seating"; player.SetParent(f.Body.transform, false); try { local(); } finally { player.SetParent(parent, false); }
                    phase = "guest"; mode(false, 1); local(); mode(true, 1); phase = "disconnect"; SetProperty(session, "State", SessionState.Idle); local(); mode(true, 1);
                    protect(true);
                    try
                    {
                        // The real guest writer guard can block state entry here. Verify
                        // speed-hook abstention without expecting that guard to run cooling.
                        var prefix = Vehicles.GetNestedType("NativeCoolingHooks", System.Reflection.BindingFlags.NonPublic).GetMethod("BeforeCoolingRead", Static);
                        foreach (var action in new[] { f.AirActions[1], f.CheckActions[1] })
                        { object?[] args = { action, null }; prefix.Invoke(null, args); Require(args[1] == null, "Protected save acquired a speed scope."); }
                        f.OriginalInputs();
                    }
                    finally { protect(false); }
                });
                check("host cooling speed: native producer captures movement magnitude independently of differential speed", () =>
                {
                    mode(true, 255); f.Producer.enabled = true;
                    try
                    {
                        foreach (var velocity in new[] { Vector3.zero, new Vector3(10, 0, 0), new Vector3(-10, 0, 0), new Vector3(0, 3, 4) })
                        {
                            f.Body.velocity = velocity; f.Producer.Fsm.Update(); var value = State(0, 0, 3000); value.SpeedTenthsKmh = 1800;
                            CallStatic("CaptureSpeedTelemetry", item, value);
                            Require(value.MovementSpeedAvailable && value.MovementSpeedTenthsKmh == (ushort)(velocity.magnitude * 36f)
                                && value.SpeedTenthsKmh == 1800, "Movement capture: velocity=" + velocity + ", native=" + f.Speed.Value + ", available=" + value.MovementSpeedAvailable + ", wire=" + value.MovementSpeedTenthsKmh + ", state=" + f.Producer.ActiveStateName);
                        }
                        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
                        { f.Speed.Value = invalid; var value = State(0, 0, 0); CallStatic("CaptureSpeedTelemetry", item, value); Require(!value.MovementSpeedAvailable && value.MovementSpeedTenthsKmh == 0, "Invalid movement remained available."); }
                        f.Speed.Value = float.MaxValue; var extreme = State(0, 0, 0); CallStatic("CaptureSpeedTelemetry", item, extreme);
                        Require(extreme.MovementSpeedAvailable && extreme.MovementSpeedTenthsKmh == ushort.MaxValue, "Finite speed overflow was not clamped.");
                    }
                    finally { f.Producer.enabled = false; f.Body.velocity = Vector3.zero; f.Speed.Value = 0; }
                    var stopped = State(0, 0, 0); CallStatic("CaptureSpeedTelemetry", item, stopped); Require(!stopped.MovementSpeedAvailable, "Disabled producer exposed stale movement.");
                });
                check("host cooling speed: nested helper scope and native exception restore original operands", () =>
                {
                    receive(800); var action = f.AirActions[1]; var hooks = Vehicles.GetNestedType("NativeCoolingHooks", System.Reflection.BindingFlags.NonPublic);
                    var prefix = hooks.GetMethod("BeforeCoolingRead", Static); var finalizer = hooks.GetMethod("AfterCoolingRead", Static);
                    object?[] outer = { action, null }, inner = { action, null }; prefix.Invoke(null, outer); prefix.Invoke(null, inner);
                    Require(outer[1] != null && inner[1] != null, "Nested speed scope did not bind.");
                    finalizer.Invoke(null, new object?[] { null, inner[1] }); Require(!ReferenceEquals(Get(action, "float1"), f.Speed), "Nested return removed outer scope.");
                    var error = new InvalidOperationException("native speed probe failure");
                    Require(ReferenceEquals(error, finalizer.Invoke(null, new object?[] { error, outer[1] })), "Native exception was lost."); f.OriginalInputs();
                });
                foreach (string fault in new[] { "producer target", "producer scale", "wheel output", "air operand", "threshold", "timing" })
                {
                    string changed = fault;
                    check("host cooling speed: changed " + changed + " rejects whole group and repairs", () =>
                    {
                        receive(800); object target; string field; object replacement;
                        if (changed == "producer target") { target = Get(f.ProducerActions[0], "gameObject"); field = "GameObject"; replacement = new FsmGameObject { Value = f.Cooling.gameObject }; }
                        else if (changed == "producer scale") { target = f.ProducerActions[1]; field = "multiplyBy"; replacement = new FsmFloat(1); }
                        else if (changed == "wheel output") { target = Get(f.ProducerActions[2], "targetProperty"); field = "FloatParameter"; replacement = f.Speed; }
                        else if (changed == "air operand") { target = f.AirActions[1]; field = "float1"; replacement = new FsmFloat(100); }
                        else if (changed == "threshold") { target = f.CheckActions[1]; field = "float2"; replacement = new FsmFloat(3); }
                        else { target = f.CheckActions[1]; field = "everyFrame"; replacement = true; }
                        var member = target.GetType().GetProperty(field, Members); bool property = member != null;
                        object original = property ? member!.GetValue(target, null) : Get(target, field);
                        if (property) member!.SetValue(target, replacement, null); else Set(target, field, replacement);
                        try { f.Rebind(); Require(Get(item, "NativeCooling") == null, "Changed graph remained bound."); }
                        finally { if (property) member!.SetValue(target, original, null); else Set(target, field, original); }
                        f.Rebind(); Require(Get(item, "NativeCooling") != null, "Repaired graph did not bind."); f.OriginalInputs();
                    });
                }
                RunCoolingRpmChecks(check, f, item, vehicles, session, reset, mode, protect);
                check("host cooling speed: cleanup removes both scoped readers", () =>
                {
                    receive(0); Call(vehicles, "ClearVehicleStateStreams"); Require(Get(item, "NativeCooling") == null, "Speed binding survived cleanup.");
                    f.Speed.Value = 100; Require(f.Air(-20)[0] > .03f, "Cleared stream retained speed projection."); f.OriginalInputs();
                });
            }
            finally { CallStatic("ClearCoolingInputs", item); foreach (string name in fields) Set(item, name, saved[name]); protect(before); reset(); }
        }

        private sealed partial class SpeedFixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly float _savedSpeed, _savedTemperature, _savedRpm;
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Producer, Cooling;
            internal readonly FsmFloat Speed, Temperature, Rpm;
            internal FsmStateAction[] ProducerActions => NativeBagPartChecks.State(Producer, "State 1").Actions;
            internal FsmStateAction[] AirActions => NativeBagPartChecks.State(Cooling, "Air cooling").Actions;
            internal FsmStateAction[] CheckActions => NativeBagPartChecks.State(Cooling, "Check temp").Actions;
            internal object Item = null!;

            internal SpeedFixture()
            {
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../vehicle-speed-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                var producer = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/CarData", "Measurements");
                var cooling = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Systems/Cooling", "Cooling");
                Speed = FsmVariables.GlobalVariables.FindFsmFloat("SpeedKMH"); Temperature = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp");
                Rpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM");
                _savedSpeed = Speed.Value; _savedTemperature = Temperature.Value; _savedRpm = Rpm.Value;
                _root = new GameObject("CORRIS"); _root.SetActive(false); Body = _root.AddComponent<Rigidbody>(); Body.useGravity = false; Body.isKinematic = true;
                Type? driveType = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if ((driveType = assembly.GetType("Drivetrain")) != null) break;
                var drive = _root.AddComponent(driveType ?? throw new InvalidOperationException("Native drivetrain absent."));
                foreach (var component in _root.GetComponents<Behaviour>()) component.enabled = false;
                var sim = Child(_root, "Simulation"); var data = Child(sim, "CarData"); var coolingObject = Child(Child(sim, "Systems"), "Cooling");
                Producer = NativeBagPartChecks.MakeFsm(data, producer); Cooling = NativeBagPartChecks.MakeFsm(coolingObject, cooling);
                Cooling.FsmVariables.FindFsmGameObject("GrillTrigger").Value = Child(coolingObject, "GrillTrigger");
                _root.SetActive(true); Producer.Fsm.Init(Producer); Cooling.Fsm.Init(Cooling);
                Load(Producer, producer, drive, driveType); Load(Cooling, cooling, drive, driveType);
                var target = (FsmOwnerDefault)Get(ProducerActions[0], "gameObject"); target.GameObject.Value = _root;
                Set(drive, "differentialSpeed", 180f);
                CreateCoolingParts();
            }

            private void Load(PlayMakerFSM fsm, Dictionary<string, object> row, Component drive, Type driveType)
            {
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(fsm, (string)stateRow["name"]);
                    if (fsm == Cooling && state.Name != "Air cooling" && state.Name != "Check temp"
                        && state.Name != "Water Pump 2" && state.Name != "Fan" && state.Name != "Motor on?"
                        && state.Name != "Open" && state.Name != "Closed")
                    { state.Transitions = new FsmTransition[0]; continue; }
                    var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> raw in (IEnumerable)stateRow["actions"])
                    {
                        Dictionary<string, object>? property = null; var parameters = new List<object>();
                        foreach (Dictionary<string, object> parameter in (IEnumerable)raw["parameters"])
                            if ((string)parameter["type"] == "FsmProperty") property = (Dictionary<string, object>)parameter["value"]; else parameters.Add(parameter);
                        var copy = new Dictionary<string, object>(raw); copy["parameters"] = parameters;
                        var action = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { copy, fsm });
                        foreach (var field in action.GetType().GetFields())
                            if (field.FieldType == typeof(FsmFloat) && field.GetValue(action) is FsmFloat value && value.UseVariable)
                            { if (value.Name == "SpeedKMH") field.SetValue(action, Speed); else if (value.Name == "EngineTemp") field.SetValue(action, Temperature); else if (value.Name == "RPM") field.SetValue(action, Rpm); }
                        if (property != null)
                            Set(action, "targetProperty", new FsmProperty { TargetObject = new FsmObject { ObjectType = driveType, Value = drive },
                                TargetType = driveType, TargetTypeName = "Drivetrain", PropertyName = "differentialSpeed", setProperty = false,
                                FloatParameter = fsm.FsmVariables.FindFsmFloat("DiffSpeed") });
                        actions.Add(action);
                    }
                    state.Actions = actions.ToArray(); foreach (var action in state.Actions) action.Init(state);
                }
            }

            private static GameObject Child(GameObject parent, string name)
            { var child = new GameObject(name); child.transform.SetParent(parent.transform, false); return child; }
            internal void Start() { NativeBagPartChecks.Start(Producer); NativeBagPartChecks.Fire(Producer, "State 1"); NativeBagPartChecks.Start(Cooling); }
            internal FsmFloat Local(string name) => Cooling.FsmVariables.FindFsmFloat(name);
            internal float[] Air(float ambient)
            {
                Local("CoolingRateCoolant").Value = 0; Local("TempArea").Value = ambient; Local("CoolingAirRateModifier").Value = 2900; Local("WaterLevel").Value = 10;
                Local("CoolingHeaterRate").Value = .1f; Local("CoolingFanRate").Value = .2f;
                Local("CoolingFlectRate").Value = .3f; Local("CoolingDynoFan").Value = .4f;
                NativeBagPartChecks.Fire(Cooling, "Air cooling"); return new[] { Local("CoolingAirRate").Value, Local("CoolingRateCoolant").Value };
            }
            internal string CheckTemperature(float value)
            { Temperature.Value = value; NativeBagPartChecks.Fire(Cooling, "Check temp"); return Cooling.ActiveStateName; }
            internal void Rebind()
            {
                Set(Item, "NextCoolingInputProbeAt", 0f); CallStatic("EnsureCoolingInputs", Item);
                if (Get(Item, "NativeCooling") == null) { Set(Item, "NextCoolingInputProbeAt", 0f); CallStatic("EnsureCoolingInputs", Item); }
            }
            internal void OriginalInputs()
            { Require(ReferenceEquals(Get(AirActions[1], "float1"), Speed) && ReferenceEquals(Get(CheckActions[1], "float1"), Speed), "Speed operand was not restored."); RpmInputsRestored(); }
            public void Dispose() { Speed.Value = _savedSpeed; Temperature.Value = _savedTemperature; Rpm.Value = _savedRpm; UnityEngine.Object.DestroyImmediate(_root); }
        }
    }
}
