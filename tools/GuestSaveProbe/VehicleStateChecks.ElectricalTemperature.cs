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
        private static void RunElectricalTemperatureChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "Id", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string field in fields) saved[field] = Get(item, field);
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var property = policy.GetType().GetProperty("ProtectWorld", Members); bool wasProtected = (bool)property.GetValue(policy, null);
            Action<bool> protect = value => property.GetSetMethod(true).Invoke(policy, new object[] { value });
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items"); uint id = StableHash.Fnv1a32("vehicle:CORRIS");
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new ElectricalTemperatureFixture())
            try
            {
                reset(); protect(false); items.Remove((uint)saved["Id"]); items.Add(id, item);
                Set(item, "Id", id); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item; f.Start();
                uint revision = 0;
                Action<float> receive = heat =>
                {
                    mode(false, 3); Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState {
                        VehicleId = id, Revision = ++revision, Flags = 1, Celsius = 87.125f, EngineCelsius = heat }); f.Rebind();
                };
                check("electrical temperature: three readers bind without changing heat or stored charge", () =>
                {
                    f.Engine.Value = 170; f.StoredCharge.Value = 126; receive(-20);
                    Require(Get(item, "NativeElectricalTemperature") != null && f.Engine.Value == 170 && f.StoredCharge.Value == 126, "Electrical temperature binding failed or changed sources.");
                    f.OriginalInputs();
                });
                foreach (float engine in new[] { -80f, -20.1f, -20f, -19.9f, -.11f, -.1f, -.09f, 0f, 90f, 300f })
                    foreach (float charge in new[] { 83.9f, 84.1f, 104f, 126f })
                    {
                        float degrees = engine, battery = charge;
                        check("electrical temperature: native cold penalty at " + degrees + " C charge " + battery, () =>
                        {
                            mode(true, 255); f.Engine.Value = degrees; var expected = f.Cycle(battery, 2500);
                            f.Engine.Value = 170; receive(degrees); Set(item, "LocallyOwned", true);
                            SameElectrical(expected, f.Cycle(battery, 2500)); f.UnchangedSources(battery, 2500);
                        });
                    }
                foreach (ushort rpm in new ushort[] { 0, 399, 400, 401, 800, 6500 })
                    foreach (float temperature in new[] { -80f, 90f, 300f })
                    {
                        ushort speed = rpm; float heat = temperature;
                        check("electrical temperature: native charging limit at " + heat + " C and " + speed + " RPM", () =>
                        {
                            mode(true, 255); f.Engine.Value = heat; var expected = f.Cycle(126, speed);
                            f.Engine.Value = 170; receive(heat); SameElectrical(expected, f.Cycle(126, speed)); f.UnchangedSources(126, speed);
                        });
                    }
                check("electrical temperature: voltage cutoff preserves native equality and neighboring branches", () =>
                {
                    foreach (float limit in new[] { 0f, 84f, 100f })
                        foreach (float delta in new[] { -.01f, 0f, .01f })
                        {
                            f.Main.FsmVariables.FindFsmFloat("VoltageLimit").Value = limit;
                            f.Interior.FsmVariables.FindFsmFloat("VoltageLimit").Value = limit;
                            mode(true, 255); f.Engine.Value = -20; var expected = f.Cycle(limit + 20 + delta, 2500);
                            f.Engine.Value = 170; receive(-20); SameElectrical(expected, f.Cycle(limit + 20 + delta, 2500));
                        }
                    f.Main.FsmVariables.FindFsmFloat("VoltageLimit").Value = 84; f.Interior.FsmVariables.FindFsmFloat("VoltageLimit").Value = 84;
                });
                check("electrical temperature: repeated battery reads do not accumulate a persistent cold penalty", () =>
                {
                    receive(-20); var expected = f.Cycle(126, 2500);
                    for (int i = 0; i < 5; i++) SameElectrical(expected, f.Cycle(126, 2500)); f.UnchangedSources(126, 2500);
                });
                check("electrical temperature: copied stale conflicting and foreign reports retain accepted heat", () =>
                {
                    receive(-20); var expected = f.Cycle(100, 2500);
                    var state = new VehicleCoolantState { VehicleId = id, Revision = ++revision, Flags = 1, EngineCelsius = -20, Celsius = 87.125f };
                    Call(vehicles, "OnVehicleCoolantState", state); state.EngineCelsius = 300; SameElectrical(expected, f.Cycle(100, 2500));
                    Call(vehicles, "OnVehicleCoolantState", state); SameElectrical(expected, f.Cycle(100, 2500));
                    state.Revision--; Call(vehicles, "OnVehicleCoolantState", state); SameElectrical(expected, f.Cycle(100, 2500));
                    state.VehicleId++; state.Revision += 100; Call(vehicles, "OnVehicleCoolantState", state); SameElectrical(expected, f.Cycle(100, 2500));
                });
                check("electrical temperature: missing and unavailable host state use native zero degree behavior", () =>
                {
                    mode(true, 255); f.Engine.Value = 0; var expected = f.Cycle(100, 2500); f.Engine.Value = 170;
                    reset(); mode(false, 3); f.Rebind(); SameElectrical(expected, f.Cycle(100, 2500));
                    receive(-20); Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState { VehicleId = id, Revision = ++revision });
                    SameElectrical(expected, f.Cycle(100, 2500)); f.UnchangedSources(100, 2500);
                });
                check("electrical temperature: heat received before discovery attaches on update", () =>
                {
                    reset(); mode(false, 3); items.Remove(id);
                    Call(vehicles, "OnVehicleCoolantState", new VehicleCoolantState { VehicleId = id, Revision = ++revision, Flags = 1, EngineCelsius = -20, Celsius = 87.125f });
                    items.Add(id, item); Set(vehicles, "_nextCoolantPoll", 0f); Set(item, "NextElectricalTemperatureProbeAt", 0f);
                    Call(vehicles, "UpdateVehicleCoolant", session); Require(Get(item, "NativeElectricalTemperature") != null, "Late electrical inputs did not bind.");
                    var expected = f.Cycle(100, 2500); mode(true, 255); f.Engine.Value = -20; SameElectrical(expected, f.Cycle(100, 2500)); f.Engine.Value = 170;
                });
                check("electrical temperature: ownership changes and engine expiry do not replace shared heat", () =>
                {
                    receive(-20); var expected = f.Cycle(100, 2500);
                    Set(item, "LocallyOwned", true); SameElectrical(expected, f.Cycle(100, 2500));
                    Set(item, "LocallyOwned", false); Set(item, "RemoteOwner", (byte)2); Set(item, "RemoteEngineUntil", -999f);
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); SameElectrical(expected, f.Cycle(100, 2500));
                });
                check("electrical temperature: host and disconnected sessions retain native calculations", () =>
                {
                    receive(-20); mode(true, 255); var expected = f.Cycle(100, 2500);
                    mode(false, 3); SetProperty(session, "State", SessionState.Idle); SameElectrical(expected, f.Cycle(100, 2500));
                    mode(false, 3); Require(f.Cycle(100, 2500).Values[0] != expected.Values[0], "Guest electrical inputs did not recover.");
                });
                check("electrical temperature: scoped native reads work with active guest save protection", () =>
                {
                    receive(-20); protect(true);
                    try
                    {
                        foreach (var fsm in new[] { f.Main, f.Interior })
                        {
                            var read = NativeBagPartChecks.State(fsm, "Battery").Actions[1]; read.OnEnter();
                            Require(fsm.FsmVariables.FindFsmFloat("BatteryTemp").Value == -20, "Protected assignment used local heat.");
                            read.OnUpdate(); Require(fsm.FsmVariables.FindFsmFloat("BatteryTemp").Value == -20, "Protected update used local heat.");
                        }
                        NativeBagPartChecks.State(f.Main, "Charge battery").Actions[2].OnEnter();
                        Require(f.Main.FsmVariables.FindFsmFloat("Math1").Value == 31 && f.Engine.Value == 170, "Protected charging read changed heat."); f.OriginalInputs();
                    }
                    finally { protect(false); }
                });
                check("electrical temperature: nested assignments restore after an exception", () =>
                {
                    receive(-20); var hooks = Vehicles.GetNestedType("NativeEngineTemperatureHooks", Members);
                    var before = hooks.GetMethod("Before", Static); var after = hooks.GetMethod("After", Static);
                    foreach (object read in (IEnumerable)Get(Get(item, "NativeElectricalTemperature"), "Reads"))
                    {
                        var action = Get(read, "Action"); var field = (System.Reflection.FieldInfo)Get(read, "Field"); var args = new object?[] { action, null };
                        before.Invoke(null, args); var outer = args[1]; Require(outer != null, "Outer electrical read abstained.");
                        args[1] = null; before.Invoke(null, args); Require(args[1] != null, "Nested electrical read abstained.");
                        var error = new InvalidOperationException("fixture error"); Require(ReferenceEquals(after.Invoke(null, new[] { error, args[1] }), error), "Finalizer lost native exception.");
                        Require(!ReferenceEquals(field.GetValue(action), f.Engine), "Nested finalizer removed outer operand.");
                        after.Invoke(null, new object?[] { null, outer }); f.OriginalInputs();
                    }
                });
                foreach (string fault in new[] { "operand", "output", "charging constant", "charging operation", "cadence", "enabled", "local shadow", "duplicate", "cold minimum", "cold maximum", "penalty cadence", "charge output", "voltage event", "voltage transition", "charging divisor", "charging maximum", "charging output", "state array", "catalog state" })
                {
                    string changed = fault; check("electrical temperature: changed " + changed + " disables the group and repairs", () => f.Fault(changed));
                }
                check("electrical temperature: native dispatch detects a changed group before the next scan", () =>
                {
                    receive(-20); var clamp = NativeBagPartChecks.State(f.Interior, "Battery").Actions[2]; var original = Get(clamp, "minValue");
                    try
                    {
                        Set(clamp, "minValue", new FsmFloat(-21)); f.Cycle(100, 2500);
                        Require(Get(item, "NativeElectricalTemperature") == null, "Native callback kept a changed electrical reader group."); f.UnchangedSources(100, 2500);
                    }
                    finally { Set(clamp, "minValue", original); }
                    f.Rebind(); Require(Get(item, "NativeElectricalTemperature") != null, "Native callback failure did not recover.");
                });
                check("electrical temperature: cleanup removes readers and reconnect binds new heat", () =>
                {
                    mode(true, 255); var expected = f.Cycle(100, 2500); receive(-20); Call(vehicles, "ClearVehicleStateStreams");
                    Require(Get(item, "NativeElectricalTemperature") == null, "Electrical temperature survived cleanup."); SameElectrical(expected, f.Cycle(100, 2500));
                    receive(-20); Require(f.Cycle(100, 2500).Values[0] != expected.Values[0], "Reconnection did not bind new heat."); f.UnchangedSources(100, 2500);
                });
            }
            finally
            {
                reset(); items.Remove(id); foreach (string field in fields) Set(item, field, saved[field]); items[(uint)saved["Id"]] = item; protect(wasProtected);
            }
        }

        private static void SameElectrical(ThermalResult expected, ThermalResult actual)
        {
            Require(expected.Branch == actual.Branch, "Native battery branches differ: " + expected.Branch + " / " + actual.Branch);
            for (int i = 0; i < expected.Values.Length; i++) Require(expected.Values[i] == actual.Values[i], "Native electrical result " + i + " differs: " + expected.Values[i] + " / " + actual.Values[i]);
        }

        private sealed partial class ElectricalTemperatureFixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly float _engine, _rpm;
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Main, Interior, Battery, Amps;
            internal readonly FsmFloat Engine, Rpm, StoredCharge;
            internal object Item = null!;
            internal ElectricalTemperatureFixture(bool charging = false)
            {
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../electrical-temperature-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                Engine = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp"); Rpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM"); _engine = Engine.Value; _rpm = Rpm.Value;
                _root = new GameObject("CORRIS"); _root.SetActive(false); Body = _root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                Main = NativeBagPartChecks.MakeFsm(ObjectAt("CORRIS/Simulation/Systems/Electrics"), NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Systems/Electrics", "Electrics"));
                Interior = NativeBagPartChecks.MakeFsm(ObjectAt("CORRIS/InteriorLight/Electrics"), NativeBagPartChecks.Find(rows, "CORRIS/InteriorLight/Electrics", "Consumption"));
                Battery = ScalarSource("CORRIS/Assemblies/VINP_Battery", "Data", "Charge"); Amps = ScalarSource("CORRIS/Simulation/Electricity/PowerON", "Amps", "Amps");
                StoredCharge = Battery.FsmVariables.FindFsmFloat("Charge");
                _root.SetActive(true);
                foreach (var fsm in new[] { Main, Interior, Battery, Amps }) fsm.Fsm.Init(fsm);
                Load(Main, NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Systems/Electrics", "Electrics"));
                Load(Interior, NativeBagPartChecks.Find(rows, "CORRIS/InteriorLight/Electrics", "Consumption"));
                if (charging) LoadChargingGraph(rows);
                foreach (var fsm in new[] { Main, Interior })
                    ((FsmOwnerDefault)Get(NativeBagPartChecks.State(fsm, "Battery").Actions[0], "gameObject")).GameObject.Value = Battery.gameObject;
                ((FsmOwnerDefault)Get(NativeBagPartChecks.State(Main, "Charge battery").Actions[7], "gameObject")).GameObject.Value = Amps.gameObject;
            }
            private GameObject ObjectAt(string path)
            {
                var current = _root.transform; var names = path.Split('/');
                for (int i = 1; i < names.Length; i++)
                {
                    var child = current.Find(names[i]); if (child == null) { var obj = new GameObject(names[i]); obj.transform.SetParent(current, false); child = obj.transform; }
                    current = child;
                }
                return current.gameObject;
            }
            private PlayMakerFSM ScalarSource(string path, string name, string variable)
            {
                var fsm = ObjectAt(path).AddComponent<PlayMakerFSM>(); fsm.enabled = false; Set(fsm, "fsm", new Fsm()); fsm.Fsm.Name = name;
                fsm.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = variable, UseVariable = true } };
                fsm.Fsm.StartState = "Idle"; fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Idle", Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] } };
                return fsm;
            }
            private void Load(PlayMakerFSM fsm, Dictionary<string, object> row)
            {
                foreach (Dictionary<string, object> rawState in (IEnumerable)row["states"])
                {
                    string name = (string)rawState["name"]; var state = NativeBagPartChecks.State(fsm, name);
                    if (name != "Battery" && (fsm != Main || name != "Charge battery")) { state.Transitions = new FsmTransition[0]; continue; }
                    var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> raw in (IEnumerable)rawState["actions"])
                    {
                        string type = (string)raw["type"];
                        var action = type.EndsWith(".ActivateGameObject", StringComparison.Ordinal) ? new TemperatureQuiet()
                            : (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { raw, fsm });
                        foreach (var field in action.GetType().GetFields())
                            if (field.FieldType == typeof(FsmFloat) && field.GetValue(action) is FsmFloat value && value.UseVariable)
                            { if (value.Name == "EngineTemp") field.SetValue(action, Engine); if (value.Name == "RPM") field.SetValue(action, Rpm); }
                        actions.Add(action);
                    }
                    state.Actions = actions.ToArray(); foreach (var action in state.Actions) action.Init(state);
                }
            }
            internal void Start() { foreach (var fsm in new[] { Main, Interior, Battery, Amps }) NativeBagPartChecks.Start(fsm); }
            internal ThermalResult Cycle(float charge, ushort rpm)
            {
                StoredCharge.Value = charge; Rpm.Value = rpm;
                Main.FsmVariables.FindFsmFloat("AlternatorEfficiency").Value = 1500; Main.FsmVariables.FindFsmFloat("AlternatorVolts").Value = 13.7f;
                Main.FsmVariables.FindFsmFloat("Volts").Value = 9; Amps.FsmVariables.FindFsmFloat("Amps").Value = .05f;
                NativeBagPartChecks.Fire(Main, "Battery"); string main = Main.ActiveStateName;
                NativeBagPartChecks.Fire(Interior, "Battery"); string interior = Interior.ActiveStateName;
                NativeBagPartChecks.Fire(Main, "Charge battery");
                return new ThermalResult { Branch = main + " / " + interior, Values = new[] {
                    Main.FsmVariables.FindFsmFloat("BatteryTemp").Value, Interior.FsmVariables.FindFsmFloat("BatteryTemp").Value,
                    Main.FsmVariables.FindFsmFloat("Charge").Value, Interior.FsmVariables.FindFsmFloat("Charge").Value,
                    Main.FsmVariables.FindFsmFloat("Math1").Value, Main.FsmVariables.FindFsmFloat("Charging").Value,
                    Main.FsmVariables.FindFsmFloat("Volts").Value } };
            }
            internal void OriginalInputs()
            {
                foreach (var fsm in new[] { Main, Interior })
                    Require(ReferenceEquals(Get(NativeBagPartChecks.State(fsm, "Battery").Actions[1], "floatValue"), Engine), "Battery temperature source not restored.");
                Require(ReferenceEquals(Get(NativeBagPartChecks.State(Main, "Charge battery").Actions[2], "float1"), Engine), "Charging temperature source not restored.");
            }
            internal void UnchangedSources(float charge, ushort rpm)
            { Require(Engine.Value == 170 && StoredCharge.Value == charge && Rpm.Value == rpm, "Electrical projection changed heat, stored charge or RPM."); OriginalInputs(); }
            internal void Rebind()
            {
                Set(Item, "NextElectricalTemperatureProbeAt", 0f); CallStatic("EnsureElectricalTemperatureInputs", Item);
                if (Get(Item, "NativeElectricalTemperature") == null) { Set(Item, "NextElectricalTemperatureProbeAt", 0f); CallStatic("EnsureElectricalTemperatureInputs", Item); }
            }
            internal void Fault(string name)
            {
                var state = NativeBagPartChecks.State(Main, "Battery"); var action = state.Actions[1]; var charging = NativeBagPartChecks.State(Main, "Charge battery").Actions;
                string field = "floatValue"; object target = action;
                if (name == "output") field = "floatVariable";
                else if (name == "cadence") field = "everyFrame";
                else if (name == "charging constant" || name == "charging operation") { target = charging[2]; field = name == "charging constant" ? "float2" : "operation"; }
                else if (name == "cold minimum" || name == "cold maximum") { target = state.Actions[2]; field = name == "cold minimum" ? "minValue" : "maxValue"; }
                else if (name == "penalty cadence" || name == "charge output") { target = state.Actions[3]; field = name == "penalty cadence" ? "perSecond" : "floatVariable"; }
                else if (name == "voltage event") { target = state.Actions[4]; field = "greaterThan"; }
                else if (name == "charging divisor") { target = charging[3]; field = "divideBy"; }
                else if (name == "charging maximum") { target = charging[4]; field = "maxValue"; }
                else if (name == "charging output") { target = charging[5]; field = "floatVariable"; }
                object original = Get(target, field); bool enabled = action.Enabled; var variables = Interior.FsmVariables.FloatVariables; var actions = state.Actions; var transitions = state.Transitions;
                object profile = Get(Get(Get(Item, "NativeElectricalTemperature"), "Rule"), "ElectricalInputs"); object catalogState = Get(profile, "BatteryState"); PlayMakerFSM? duplicate = null;
                try
                {
                    if (name == "enabled") action.Enabled = false;
                    else if (name == "local shadow") { var copy = new List<FsmFloat>(variables); copy.Add(new FsmFloat { Name = "EngineTemp", UseVariable = true }); Interior.FsmVariables.FloatVariables = copy.ToArray(); }
                    else if (name == "duplicate") { duplicate = Interior.gameObject.AddComponent<PlayMakerFSM>(); duplicate.enabled = false; duplicate.FsmName = Interior.FsmName; }
                    else if (name == "state array") state.Actions = new FsmStateAction[0];
                    else if (name == "voltage transition") state.Transitions = new FsmTransition[0];
                    else if (name == "catalog state") Set(profile, "BatteryState", "Missing");
                    else Set(target, field, name == "cadence" || name == "penalty cadence" ? (object)true : name == "charging operation" ? Enum.ToObject(original.GetType(), 1)
                        : name == "voltage event" ? FsmEvent.GetFsmEvent("FINISHED") : new FsmFloat(999));
                    Rebind(); Require(Get(Item, "NativeElectricalTemperature") == null, "Malformed electrical group remained active.");
                }
                finally
                {
                    Set(target, field, original); action.Enabled = enabled; Interior.FsmVariables.FloatVariables = variables; state.Actions = actions; state.Transitions = transitions; Set(profile, "BatteryState", catalogState);
                    if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                }
                Rebind(); Require(Get(Item, "NativeElectricalTemperature") != null, "Repaired electrical group did not rebind."); OriginalInputs();
            }
            public void Dispose() { Engine.Value = _engine; Rpm.Value = _rpm; UnityEngine.Object.DestroyImmediate(_root); }
        }
    }
}
