using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunEngineTemperatureChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "Id", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string name in fields) saved[name] = Get(item, name);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var property = policy.GetType().GetProperty("ProtectWorld", Members); bool wasProtected = (bool)property.GetValue(policy, null);
            Action<bool> protect = value => property.GetSetMethod(true).Invoke(policy, new object[] { value });
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items"); uint id = StableHash.Fnv1a32("vehicle:CORRIS");
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new EngineTemperatureFixture())
            try
            {
                reset(); protect(false); items.Remove((uint)saved["Id"]); items.Add(id, item);
                Set(item, "Id", id); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item; f.Start();
                uint revision = 0;
                Action<float> receive = temp =>
                {
                    mode(false, 3); Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState {
                        VehicleId = id, Revision = ++revision, Flags = 1, Celsius = 87.125f, EngineCelsius = temp }); f.Rebind();
                };
                check("engine temperature: six native readers bind without rewriting the global", () =>
                {
                    float before = f.Temperature.Value; receive(80);
                    Require(Get(item, "NativeEngineTemperature") != null && f.Temperature.Value == before, "Thermal binding failed or receipt rewrote native heat.");
                    f.OriginalInputs();
                });
                foreach (float temp in new[] { -25f, 0f, 2.9f, 3f, 3.1f, 90f, 130f, 220f })
                    foreach (float chamber in new[] { 0f, 12f, 16f, 24f })
                    {
                        float degrees = temp, fuel = chamber;
                        check("engine temperature: native fuel and oil match " + degrees + " C with chamber " + fuel, () =>
                        {
                            mode(true, 255); f.Temperature.Value = degrees; var expected = f.Cycle(fuel, 2500);
                            f.Temperature.Value = 162.5f; receive(degrees); Set(item, "LocallyOwned", true);
                            var actual = f.Cycle(fuel, 2500); SameThermal(expected, actual);
                            Require(f.Temperature.Value == 162.5f && f.Rpm.Value == 2500, "Projection changed native temperature or driver RPM.");
                            f.OriginalInputs();
                        });
                    }
                foreach (ushort rpm in new ushort[] { 0, 800, 2500, 6500 })
                {
                    ushort speed = rpm;
                    check("engine temperature: oil pressure retains local " + speed + " RPM", () =>
                    {
                        mode(true, 255); f.Temperature.Value = 80; var expected = f.Cycle(16, speed);
                        f.Temperature.Value = 150; receive(80); SameThermal(expected, f.Cycle(16, speed));
                    });
                }
                check("engine temperature: copied state rejects stale conflicting and foreign heat", () =>
                {
                    receive(80); var expected = f.Cycle(16, 2500);
                    var incoming = new VehicleCoolantState { VehicleId = id, Revision = ++revision, Flags = 1, Celsius = 87.125f, EngineCelsius = 80 };
                    Call(vehicles, "OnVehicleCoolantState", incoming); incoming.EngineCelsius = 220;
                    SameThermal(expected, f.Cycle(16, 2500)); Call(vehicles, "OnVehicleCoolantState", incoming);
                    SameThermal(expected, f.Cycle(16, 2500)); incoming.Revision--; Call(vehicles, "OnVehicleCoolantState", incoming);
                    SameThermal(expected, f.Cycle(16, 2500)); incoming.VehicleId++; incoming.Revision += 100;
                    Call(vehicles, "OnVehicleCoolantState", incoming); SameThermal(expected, f.Cycle(16, 2500));
                });
                check("engine temperature: unseeded and unavailable states use native zero-degree inputs", () =>
                {
                    mode(true, 255); f.Temperature.Value = 0; var expected = f.Cycle(16, 2500); f.Temperature.Value = 170;
                    reset(); mode(false, 3); f.Rebind(); SameThermal(expected, f.Cycle(16, 2500));
                    receive(90); Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState { VehicleId = id, Revision = ++revision });
                    SameThermal(expected, f.Cycle(16, 2500)); Require(f.Temperature.Value == 170, "Unavailable heat reset the guest global.");
                });
                check("engine temperature: driver ownership and engine packet expiry do not reset shared heat", () =>
                {
                    receive(82.25f); var expected = f.Cycle(16, 2500);
                    Set(item, "LocallyOwned", false); Set(item, "RemoteOwner", (byte)2); Set(item, "RemoteEngineUntil", -999f);
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); SameThermal(expected, f.Cycle(16, 2500));
                    Set(item, "LocallyOwned", true); Set(item, "RemoteOwner", (byte)3); SameThermal(expected, f.Cycle(16, 2500));
                });
                check("engine temperature: host and disconnected sessions retain local calculations", () =>
                {
                    receive(80); f.Temperature.Value = 120; mode(true, 3); var expected = f.Cycle(16, 2500);
                    mode(false, 3); SetProperty(session, "State", SessionState.Idle); SameThermal(expected, f.Cycle(16, 2500));
                    mode(false, 3); var remote = f.Cycle(16, 2500); Require(remote.Values[4] != expected.Values[4], "Guest did not recover shared heat.");
                });
                check("engine temperature: scoped pressure read works while guest save protection stays active", () =>
                {
                    receive(83); f.Temperature.Value = 155; protect(true);
                    try
                    {
                        var action = NativeBagPartChecks.State(f.Pressure, "Oil pressure").Actions[0]; action.OnUpdate();
                        Require(f.Pressure.FsmVariables.FindFsmFloat("Math1").Value == f.Pressure.FsmVariables.FindFsmFloat("ModifierTemp").Value - 83
                            && f.Temperature.Value == 155, "Save-protected pressure read fell back to guest heat."); f.OriginalInputs();
                    }
                    finally { protect(false); }
                });
                check("engine temperature: nested boundaries restore operands after an exception", () =>
                {
                    receive(84); var hooks = Vehicles.GetNestedType("NativeEngineTemperatureHooks", Members);
                    var before = hooks.GetMethod("Before", Static); var after = hooks.GetMethod("After", Static);
                    foreach (object read in (IEnumerable)Get(Get(item, "NativeEngineTemperature"), "Reads"))
                    {
                        var action = Get(read, "Action"); var field = (System.Reflection.FieldInfo)Get(read, "Field");
                        var args = new object?[] { action, null }; before.Invoke(null, args); var outer = args[1];
                        Require(outer != null && !ReferenceEquals(field.GetValue(action), f.Temperature), "Scoped prefix abstained.");
                        args[1] = null; before.Invoke(null, args); Require(args[1] != null, "Nested native boundary rejected its own operand.");
                        var error = new InvalidOperationException("fixture error"); Require(ReferenceEquals(after.Invoke(null, new[] { error, args[1] }), error), "Finalizer swallowed a native error.");
                        Require(!ReferenceEquals(field.GetValue(action), f.Temperature), "Nested return removed outer operand.");
                        after.Invoke(null, new object?[] { null, outer }); Require(ReferenceEquals(field.GetValue(action), f.Temperature), "Outer return did not restore native input.");
                    }
                });
                foreach (string fault in new[] { "operand", "output", "constant", "operation", "cadence", "enabled", "event", "transition", "local shadow", "duplicate" })
                {
                    string changed = fault;
                    check("engine temperature: changed " + changed + " retires the group and repairs", () => f.Fault(changed));
                }
                check("engine temperature: cleanup removes every scoped input", () =>
                {
                    receive(80); f.Temperature.Value = 120; mode(true, 255); var expected = f.Cycle(16, 2500); mode(false, 3);
                    Call(vehicles, "ClearVehicleStateStreams"); Require(Get(item, "NativeEngineTemperature") == null, "Temperature binding survived cleanup.");
                    SameThermal(expected, f.Cycle(16, 2500)); f.OriginalInputs();
                });
            }
            finally
            {
                reset(); items.Remove(id); foreach (string name in fields) Set(item, name, saved[name]);
                items[(uint)saved["Id"]] = item; protect(wasProtected);
            }
        }

        private sealed class ThermalResult { internal float[] Values = null!; internal string Branch = ""; }
        private static void SameThermal(ThermalResult expected, ThermalResult actual)
        {
            Require(expected.Branch == actual.Branch, "Native priming branch differs: " + expected.Branch + " / " + actual.Branch);
            for (int i = 0; i < expected.Values.Length; i++) Require(expected.Values[i] == actual.Values[i],
                "Native temperature calculation differs at " + i + ": " + expected.Values[i] + " / " + actual.Values[i]);
        }

        private sealed class EngineTemperatureFixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly float _temperature, _rpm;
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Fuel, Mixture, Oil, Pressure;
            internal readonly FsmFloat Temperature, Rpm;
            internal object Item = null!;
            internal EngineTemperatureFixture()
            {
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../engine-temperature-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                Temperature = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp"); Rpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM");
                _temperature = Temperature.Value; _rpm = Rpm.Value;
                _root = new GameObject("CORRIS"); _root.SetActive(false); Body = _root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                Fuel = Make(rows, "Fuel", "FuelLine"); Mixture = Make(rows, "Fuel", "Mixture");
                Oil = Make(rows, "Oil", "Oil"); Pressure = Make(rows, "Oil", "Pressure");
                _root.SetActive(true);
                foreach (var fsm in new[] { Fuel, Mixture, Oil, Pressure })
                {
                    fsm.Fsm.Init(fsm); Load(fsm, NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/" + (fsm == Fuel || fsm == Mixture ? "Fuel" : "Oil"), fsm.FsmName));
                }
            }
            private PlayMakerFSM Make(List<object> rows, string path, string name)
            {
                var current = _root.transform;
                foreach (string part in new[] { "Simulation", "Engine", path })
                {
                    var child = current.Find(part);
                    if (child == null) { var obj = new GameObject(part); obj.transform.SetParent(current, false); child = obj.transform; }
                    current = child;
                }
                return NativeBagPartChecks.MakeFsm(current.gameObject, NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/" + path, name));
            }
            private void Load(PlayMakerFSM fsm, Dictionary<string, object> row)
            {
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(fsm, (string)stateRow["name"]);
                    bool selected = fsm == Fuel && (state.Name == "Carburator" || state.Name == "Priming")
                        || fsm == Mixture && state.Name == "Calculate density" || fsm == Oil && state.Name == "Viscosity"
                        || fsm == Pressure && state.Name == "Oil pressure";
                    if (!selected) { state.Transitions = new FsmTransition[0]; continue; }
                    var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> raw in (IEnumerable)stateRow["actions"])
                    {
                        string type = (string)raw["type"];
                        var action = type.EndsWith(".FloatOperator", StringComparison.Ordinal) || type.EndsWith(".FloatClamp", StringComparison.Ordinal)
                            || type.EndsWith(".FloatCompare", StringComparison.Ordinal) || type.EndsWith(".FloatDivide", StringComparison.Ordinal)
                            || type.EndsWith(".FloatAdd", StringComparison.Ordinal)
                            ? (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { raw, fsm }) : new TemperatureQuiet();
                        foreach (var field in action.GetType().GetFields())
                            if (field.FieldType == typeof(FsmFloat) && field.GetValue(action) is FsmFloat value && value.UseVariable)
                            { if (value.Name == "EngineTemp") field.SetValue(action, Temperature); if (value.Name == "RPM") field.SetValue(action, Rpm); }
                        actions.Add(action);
                    }
                    state.Actions = actions.ToArray(); foreach (var action in state.Actions) action.Init(state);
                }
            }
            internal void Start() { foreach (var fsm in new[] { Fuel, Mixture, Oil, Pressure }) NativeBagPartChecks.Start(fsm); }
            internal void Rebind()
            {
                Set(Item, "NextEngineTemperatureProbeAt", 0f); CallStatic("EnsureEngineTemperatureInputs", Item);
                if (Get(Item, "NativeEngineTemperature") == null)
                { Set(Item, "NextEngineTemperatureProbeAt", 0f); CallStatic("EnsureEngineTemperatureInputs", Item); }
            }
            internal ThermalResult Cycle(float chamber, ushort rpm)
            {
                Rpm.Value = rpm; Fuel.FsmVariables.FindFsmFloat("FuelChamber").Value = chamber;
                NativeBagPartChecks.Fire(Fuel, "Carburator"); float carburator = Fuel.FsmVariables.FindFsmFloat("FuelChamber").Value;
                Fuel.FsmVariables.FindFsmFloat("FuelChamber").Value = chamber;
                NativeBagPartChecks.Fire(Fuel, "Priming"); string branch = Fuel.ActiveStateName;
                float primed = Fuel.FsmVariables.FindFsmFloat("FuelChamber").Value;
                NativeBagPartChecks.Fire(Mixture, "Calculate density");
                Oil.FsmVariables.FindFsmFloat("OilViscosity").Value = 1.2f; Oil.FsmVariables.FindFsmFloat("LubricationFriction").Value = .02f;
                NativeBagPartChecks.Fire(Oil, "Viscosity");
                Pressure.FsmVariables.FindFsmFloat("OilViscosity").Value = .03f; Pressure.FsmVariables.FindFsmFloat("Oil").Value = 3;
                Pressure.FsmVariables.FindFsmFloat("PressureLeak").Value = .8f;
                NativeBagPartChecks.Fire(Pressure, "Oil pressure");
                return new ThermalResult { Branch = branch, Values = new[] { carburator, primed,
                    Mixture.FsmVariables.FindFsmFloat("AirDensity").Value, Oil.FsmVariables.FindFsmFloat("FrictionOil").Value,
                    Pressure.FsmVariables.FindFsmFloat("Math1").Value, Pressure.FsmVariables.FindFsmFloat("OilPressureBar").Value } };
            }
            internal void OriginalInputs()
            {
                foreach (var action in new[] { NativeBagPartChecks.State(Fuel, "Carburator").Actions[3], NativeBagPartChecks.State(Fuel, "Priming").Actions[0],
                    NativeBagPartChecks.State(Fuel, "Priming").Actions[1], NativeBagPartChecks.State(Mixture, "Calculate density").Actions[3],
                    NativeBagPartChecks.State(Oil, "Viscosity").Actions[0], NativeBagPartChecks.State(Pressure, "Oil pressure").Actions[0] })
                {
                    string field = action.GetType().Name == "FloatClamp" ? "minValue" : action == NativeBagPartChecks.State(Pressure, "Oil pressure").Actions[0] ? "float2" : "float1";
                    Require(ReferenceEquals(Get(action, field), Temperature), "Native engine temperature reference was not restored.");
                }
            }
            internal void Fault(string name)
            {
                var action = NativeBagPartChecks.State(Mixture, "Calculate density").Actions[3];
                string field = name == "operand" ? "float1" : name == "output" ? "storeResult" : name == "constant" ? "float2" : name == "operation" ? "operation" : "everyFrame";
                object original = Get(action, field); bool enabled = action.Enabled;
                var compare = NativeBagPartChecks.State(Fuel, "Priming").Actions[0]; object eventBefore = Get(compare, "greaterThan");
                var transitions = NativeBagPartChecks.State(Fuel, "Priming").Transitions; var variables = Mixture.FsmVariables.FloatVariables;
                PlayMakerFSM? duplicate = null;
                try
                {
                    if (name == "enabled") action.Enabled = false;
                    else if (name == "event") Set(compare, "greaterThan", FsmEvent.GetFsmEvent("FLOODED"));
                    else if (name == "transition") NativeBagPartChecks.State(Fuel, "Priming").Transitions = new FsmTransition[0];
                    else if (name == "local shadow") { var copy = new List<FsmFloat>(variables); copy.Add(new FsmFloat { Name = "EngineTemp", UseVariable = true }); Mixture.FsmVariables.FloatVariables = copy.ToArray(); }
                    else if (name == "duplicate") { duplicate = Mixture.gameObject.AddComponent<PlayMakerFSM>(); duplicate.enabled = false; duplicate.FsmName = Mixture.FsmName; }
                    else Set(action, field, name == "cadence" ? (object)true : name == "operation" ? Enum.ToObject(original.GetType(), 1) : new FsmFloat(999));
                    Rebind(); Require(Get(Item, "NativeEngineTemperature") == null, "Malformed reader retained partial projection.");
                }
                finally
                {
                    Set(action, field, original); action.Enabled = enabled; Set(compare, "greaterThan", eventBefore);
                    NativeBagPartChecks.State(Fuel, "Priming").Transitions = transitions; Mixture.FsmVariables.FloatVariables = variables;
                    if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                }
                Rebind(); Require(Get(Item, "NativeEngineTemperature") != null, "Repaired group did not rebind."); OriginalInputs();
            }
            public void Dispose() { Temperature.Value = _temperature; Rpm.Value = _rpm; UnityEngine.Object.DestroyImmediate(_root); }
        }
    }
}
