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
        private static void RunCabinTemperatureChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "Id", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string field in fields) saved[field] = Get(item, field);
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var property = policy.GetType().GetProperty("ProtectWorld", Members); bool wasProtected = (bool)property.GetValue(policy, null);
            Action<bool> protect = value => property.GetSetMethod(true).Invoke(policy, new object[] { value });
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items"); uint id = StableHash.Fnv1a32("vehicle:CORRIS");
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new CabinTemperatureFixture())
            try
            {
                reset(); protect(false); items.Remove((uint)saved["Id"]); items.Add(id, item);
                Set(item, "Id", id); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item;
                uint revision = 0;
                Action<float, float> receive = (engine, coolant) =>
                {
                    mode(false, 3); Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState {
                        VehicleId = id, Revision = ++revision, Flags = 1, Celsius = coolant, EngineCelsius = engine }); f.Rebind();
                };
                check("cabin temperature: native heater and cabin bind without source writes", () =>
                {
                    f.Engine.Value = 173; f.Coolant.Value = 151; receive(90, 80);
                    Require(Get(item, "NativeCabinTemperature") != null && f.Engine.Value == 173 && f.Coolant.Value == 151, "Cabin bind failed or changed native sources.");
                });
                foreach (float engine in new[] { -25f, 0f, 25f, 90f, 220f })
                    foreach (int setting in new[] { 0, 1, 2, 3 })
                        foreach (bool open in new[] { false, true })
                        {
                            float degrees = engine, coolant = engine - 10; int control = setting; bool doors = open;
                            check("cabin temperature: native heat at " + degrees + " C controls " + control + " doors " + doors, () =>
                            {
                                mode(true, 255); f.Engine.Value = degrees; f.Coolant.Value = coolant; var expected = f.Cycle(control, doors);
                                f.Engine.Value = 173; f.Coolant.Value = 151; receive(degrees, coolant); Set(item, "LocallyOwned", true);
                                SameCabin(expected, f.Cycle(control, doors)); f.UnchangedSources();
                            });
                        }
                check("cabin temperature: engine and coolant remain independent", () =>
                {
                    mode(true, 255); f.Engine.Value = 95; f.Coolant.Value = 37.125f; var expected = f.Cycle(2, false);
                    f.Engine.Value = 173; f.Coolant.Value = 151; receive(95, 37.125f); SameCabin(expected, f.Cycle(2, false)); f.UnchangedSources();
                });
                check("cabin temperature: repeated native heater reads start in degrees before scaling", () =>
                {
                    receive(95, 87.125f); var expected = f.Cycle(2, false);
                    for (int i = 0; i < 5; i++) SameCabin(expected, f.Cycle(2, false)); f.UnchangedSources();
                });
                check("cabin temperature: copied stale conflicting and foreign reports preserve accepted heat", () =>
                {
                    receive(95, 87); var expected = f.Cycle(2, false);
                    var state = new VehicleCoolantState { VehicleId = id, Revision = ++revision, Flags = 1, EngineCelsius = 95, Celsius = 87 };
                    Call(vehicles, "OnVehicleCoolantState", state); state.EngineCelsius = 250; state.Celsius = 210;
                    SameCabin(expected, f.Cycle(2, false)); Call(vehicles, "OnVehicleCoolantState", state); SameCabin(expected, f.Cycle(2, false));
                    state.Revision--; Call(vehicles, "OnVehicleCoolantState", state); SameCabin(expected, f.Cycle(2, false));
                    state.VehicleId++; state.Revision += 100; Call(vehicles, "OnVehicleCoolantState", state); SameCabin(expected, f.Cycle(2, false));
                });
                check("cabin temperature: unseeded and unavailable heat uses native zero degree behavior", () =>
                {
                    mode(true, 255); f.Engine.Value = f.Coolant.Value = 0; var expected = f.Cycle(2, false);
                    f.Engine.Value = 173; f.Coolant.Value = 151; reset(); mode(false, 3); f.Rebind(); SameCabin(expected, f.Cycle(2, false));
                    receive(95, 87); Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState { VehicleId = id, Revision = ++revision });
                    SameCabin(expected, f.Cycle(2, false)); f.UnchangedSources();
                });
                check("cabin temperature: heat received before discovery attaches on the next update", () =>
                {
                    reset(); mode(false, 3); items.Remove(id);
                    Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState { VehicleId = id, Revision = ++revision, Flags = 1, EngineCelsius = 95, Celsius = 87 });
                    items.Add(id, item); Set(vehicles, "_nextCoolantPoll", 0f); Set(item, "NextCabinTemperatureProbeAt", 0f);
                    Call(vehicles, "UpdateVehicleCoolant", session); Require(Get(item, "NativeCabinTemperature") != null, "Late cabin did not bind.");
                    var expected = f.Cycle(2, false); mode(true, 255); f.Engine.Value = 95; f.Coolant.Value = 87;
                    SameCabin(expected, f.Cycle(2, false)); f.Engine.Value = 173; f.Coolant.Value = 151;
                });
                check("cabin temperature: driving passenger handoff and engine expiry retain host heat", () =>
                {
                    receive(95, 87); var expected = f.Cycle(2, false);
                    Set(item, "LocallyOwned", true); SameCabin(expected, f.Cycle(2, false));
                    Set(item, "LocallyOwned", false); Set(item, "RemoteOwner", (byte)2); Set(item, "RemoteEngineUntil", -999f);
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); SameCabin(expected, f.Cycle(2, false));
                });
                check("cabin temperature: host and disconnected sessions use native heat", () =>
                {
                    receive(95, 87); mode(true, 255); var expected = f.Cycle(2, false);
                    mode(false, 3); SetProperty(session, "State", SessionState.Idle); SameCabin(expected, f.Cycle(2, false));
                    mode(false, 3); Require(f.Cycle(2, false)[0] != expected[0], "Guest thermal projection did not recover.");
                });
                check("cabin temperature: active guest save protection permits only the scoped thermal reads", () =>
                {
                    receive(95, 87); protect(true);
                    try
                    {
                        var engineRead = NativeBagPartChecks.State(f.Cabin, "Data").Actions[4];
                        engineRead.OnEnter(); NativeBagPartChecks.State(f.Heater, "Calc defrosting").Actions[0].OnEnter();
                        Require(f.Cabin.FsmVariables.FindFsmFloat("MaxTemp").Value == 19 && f.Heater.FsmVariables.FindFsmFloat("CoolantTemp").Value == 87,
                            "Protected read ignored host heat."); f.UnchangedSources();
                    }
                    finally { protect(false); }
                });
                check("cabin temperature: nested engine reads restore the global reference after exceptions", () =>
                {
                    receive(95, 87); var hooks = Vehicles.GetNestedType("NativeCabinTemperatureHooks", Members);
                    var before = hooks.GetMethod("BeforeEngine", Static); var after = hooks.GetMethod("AfterEngine", Static);
                    var action = NativeBagPartChecks.State(f.Cabin, "Data").Actions[4]; var args = new object?[] { action, null };
                    before.Invoke(null, args); var outer = args[1]; Require(outer != null, "Outer read abstained.");
                    args[1] = null; before.Invoke(null, args); Require(args[1] != null, "Nested read abstained.");
                    var error = new InvalidOperationException("fixture error"); Require(ReferenceEquals(after.Invoke(null, new[] { error, args[1] }), error), "Finalizer lost the original exception.");
                    Require(!ReferenceEquals(Get(action, "float1"), f.Engine), "Nested finalizer restored too early.");
                    after.Invoke(null, new object?[] { null, outer }); f.UnchangedSources();
                });
                foreach (string fault in new[] { "operand", "output", "constant", "operation", "cadence", "enabled", "local shadow", "duplicate", "target", "source name", "source variable", "heater output", "missing source", "state array", "catalog state" })
                {
                    string changed = fault; check("cabin temperature: changed " + changed + " disables both readers and repairs", () => f.Fault(changed));
                }
                check("cabin temperature: cleanup removes both readers and reconnect binds fresh heat", () =>
                {
                    mode(true, 255); var expected = f.Cycle(2, false); receive(95, 87); Call(vehicles, "ClearVehicleStateStreams");
                    Require(Get(item, "NativeCabinTemperature") == null, "Cabin thermal hooks survived cleanup."); SameCabin(expected, f.Cycle(2, false));
                    receive(95, 87); Require(f.Cycle(2, false)[0] != expected[0], "Reconnect failed to bind."); f.UnchangedSources();
                });
            }
            finally
            {
                reset(); items.Remove(id); foreach (string field in fields) Set(item, field, saved[field]);
                items[(uint)saved["Id"]] = item; protect(wasProtected);
            }
        }

        private static void SameCabin(float[] expected, float[] actual)
        {
            for (int i = 0; i < expected.Length; i++) Require(expected[i] == actual[i], "Native cabin result " + i + " differs: " + expected[i] + " / " + actual[i]);
        }

        private sealed class CabinTemperatureFixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly float _engine;
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Cabin, Heater, Source;
            internal readonly FsmFloat Engine, Coolant;
            internal object Item = null!;
            internal CabinTemperatureFixture()
            {
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../cabin-heat-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                Engine = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp"); _engine = Engine.Value;
                _root = new GameObject("CORRIS"); _root.SetActive(false); Body = _root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                Cabin = Make(rows, "CORRIS/Simulation/CarTempCorris", "Data");
                Heater = Make(rows, "CORRIS/Simulation/Electricity/PowerON/HeaterUnit", "Function");
                Source = Make(rows, "CORRIS/Simulation/Systems/Cooling", "Cooling");
                _root.SetActive(true);
                foreach (var fsm in new[] { Cabin, Heater, Source })
                {
                    fsm.Fsm.Init(fsm); foreach (var state in fsm.Fsm.States) state.Transitions = new FsmTransition[0];
                }
                Coolant = Source.FsmVariables.FindFsmFloat("CoolantTemp");
                Load(Cabin, NativeBagPartChecks.Find(rows, "CORRIS/Simulation/CarTempCorris", "Data"), "Data");
                Load(Heater, NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Electricity/PowerON/HeaterUnit", "Function"), "Calc defrosting");
                ((FsmOwnerDefault)Get(NativeBagPartChecks.State(Heater, "Calc defrosting").Actions[0], "gameObject")).GameObject.Value = Source.gameObject;
                foreach (var fsm in new[] { Cabin, Heater, Source }) NativeBagPartChecks.Start(fsm);
            }
            private PlayMakerFSM Make(List<object> rows, string path, string name)
            {
                var current = _root.transform; var names = path.Split('/');
                for (int i = 1; i < names.Length; i++)
                {
                    var child = current.Find(names[i]); if (child == null) { var obj = new GameObject(names[i]); obj.transform.SetParent(current, false); child = obj.transform; }
                    current = child;
                }
                return NativeBagPartChecks.MakeFsm(current.gameObject, NativeBagPartChecks.Find(rows, path, name));
            }
            private void Load(PlayMakerFSM fsm, Dictionary<string, object> row, string name)
            {
                var state = NativeBagPartChecks.State(fsm, name);
                foreach (Dictionary<string, object> rawState in (IEnumerable)row["states"])
                {
                    if ((string)rawState["name"] != name) continue;
                    var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> raw in (IEnumerable)rawState["actions"])
                    {
                        string type = (string)raw["type"];
                        bool external = type.EndsWith(".SetFsmFloat", StringComparison.Ordinal) || type.EndsWith(".Wait", StringComparison.Ordinal)
                            || fsm == Cabin && type.EndsWith(".GetFsmFloat", StringComparison.Ordinal);
                        var action = external ? new TemperatureQuiet() : (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { raw, fsm });
                        if (fsm == Cabin && type.EndsWith(".FloatOperator", StringComparison.Ordinal)) Set(action, "float1", Engine);
                        actions.Add(action);
                    }
                    state.Actions = actions.ToArray(); foreach (var action in state.Actions) action.Init(state);
                }
            }
            internal float[] Cycle(int controls, bool open)
            {
                var heater = Heater.FsmVariables; var cabin = Cabin.FsmVariables;
                heater.FindFsmFloat("SettingTemp").Value = controls == 0 ? 0 : controls == 1 ? .5f : 1;
                heater.FindFsmFloat("SettingBlower").Value = controls == 0 ? 0 : controls == 1 ? 1 : 3;
                heater.FindFsmFloat("SettingDirection").Value = controls == 3 ? 1 : controls == 2 ? .5f : 0;
                cabin.FindFsmFloat("InteriorTemp").Value = 50; cabin.FindFsmFloat("TempArea").Value = -15;
                cabin.FindFsmFloat("CoolingDoorLeftRate").Value = open ? .75f : 0;
                cabin.FindFsmFloat("CoolingDoorRightRate").Value = open ? .75f : 0;
                cabin.FindFsmFloat("CoolingWindshieldOffRate").Value = 0;
                NativeBagPartChecks.Fire(Heater, "Calc defrosting"); NativeBagPartChecks.Fire(Cabin, "Data");
                return new[] { cabin.FindFsmFloat("MaxTemp").Value, cabin.FindFsmFloat("InteriorTemp").Value,
                    cabin.FindFsmFloat("CoolingRateFinal").Value, heater.FindFsmFloat("TempEfficiency").Value,
                    heater.FindFsmFloat("HeatingRate").Value, heater.FindFsmFloat("DefrostingRate").Value,
                    heater.FindFsmFloat("CoolantTemp").Value, heater.FindFsmFloat("ColdMultiplier").Value };
            }
            internal void UnchangedSources()
            {
                Require(Engine.Value == 173 && Coolant.Value == 151, "Cabin projection wrote native source heat.");
                Require(ReferenceEquals(Get(NativeBagPartChecks.State(Cabin, "Data").Actions[4], "float1"), Engine), "Cabin engine operand was not restored.");
            }
            internal void Rebind()
            {
                Set(Item, "NextCabinTemperatureProbeAt", 0f); CallStatic("EnsureCabinTemperatureInputs", Item);
                if (Get(Item, "NativeCabinTemperature") == null) { Set(Item, "NextCabinTemperatureProbeAt", 0f); CallStatic("EnsureCabinTemperatureInputs", Item); }
            }
            internal void Fault(string name)
            {
                var state = NativeBagPartChecks.State(Cabin, "Data"); var action = state.Actions[4]; var read = NativeBagPartChecks.State(Heater, "Calc defrosting").Actions[0];
                string field = name == "operand" ? "float1" : name == "output" ? "storeResult" : name == "constant" ? "float2" : name == "operation" ? "operation" : "everyFrame";
                object original = Get(action, field); bool enabled = action.Enabled; var variables = Cabin.FsmVariables.FloatVariables;
                var sourceVars = Source.FsmVariables.FloatVariables; var actions = state.Actions;
                var target = (FsmOwnerDefault)Get(read, "gameObject"); var targetBefore = target.GameObject.Value;
                string readField = name == "source name" ? "fsmName" : name == "source variable" ? "variableName" : "storeValue"; object readBefore = Get(read, readField);
                object profile = Get(Get(Get(Item, "NativeCabinTemperature"), "Rule"), "CabinInputs"); object stateBefore = Get(profile, "CabinState");
                PlayMakerFSM? duplicate = null;
                try
                {
                    if (name == "enabled") action.Enabled = false;
                    else if (name == "local shadow") { var copy = new List<FsmFloat>(variables); copy.Add(new FsmFloat { Name = "EngineTemp", UseVariable = true }); Cabin.FsmVariables.FloatVariables = copy.ToArray(); }
                    else if (name == "duplicate") { duplicate = Heater.gameObject.AddComponent<PlayMakerFSM>(); duplicate.enabled = false; duplicate.FsmName = Heater.FsmName; }
                    else if (name == "target") target.GameObject.Value = Cabin.gameObject;
                    else if (name == "source name" || name == "source variable") Set(read, readField, new FsmString("Wrong"));
                    else if (name == "heater output") Set(read, readField, Coolant);
                    else if (name == "missing source") Source.FsmVariables.FloatVariables = new FsmFloat[0];
                    else if (name == "state array") state.Actions = new FsmStateAction[0];
                    else if (name == "catalog state") Set(profile, "CabinState", "Missing");
                    else Set(action, field, name == "cadence" ? (object)true : name == "operation" ? Enum.ToObject(original.GetType(), 0) : new FsmFloat(999));
                    Rebind(); Require(Get(Item, "NativeCabinTemperature") == null, "Changed cabin binding remained active.");
                }
                finally
                {
                    Set(action, field, original); action.Enabled = enabled; Cabin.FsmVariables.FloatVariables = variables; Source.FsmVariables.FloatVariables = sourceVars;
                    state.Actions = actions; target.GameObject.Value = targetBefore; Set(read, readField, readBefore); Set(profile, "CabinState", stateBefore);
                    if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                }
                Rebind(); Require(Get(Item, "NativeCabinTemperature") != null, "Repaired cabin binding did not recover."); UnchangedSources();
            }
            public void Dispose() { Engine.Value = _engine; UnityEngine.Object.DestroyImmediate(_root); }
        }
    }
}
