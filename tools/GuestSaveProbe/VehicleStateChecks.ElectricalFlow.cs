using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private sealed partial class ElectricalTemperatureFixture
        {
            private readonly Dictionary<string, PlayMakerFSM> _parts = new Dictionary<string, PlayMakerFSM>();
            private static readonly string[] ChargingStates = { "Check alternator", "Alternator damage", "Engine running?", "Alternator eff",
                "Charge battery", "Wear", "Delay", "No charge", "Battery 2", "Run on battery", "Engine off", "Light on" };

            private void LoadChargingGraph(List<object> rows)
            {
                foreach (string name in new[] { "db_Alternator", "db_Fanbelt", "db_wAlternator", "db_wRegulator" })
                {
                    var fsm = ScalarSource("CORRIS/probe electrical parts/" + name, "Data", "Wear");
                    fsm.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Wear", UseVariable = true }, new FsmFloat { Name = "Efficiency", UseVariable = true } };
                    fsm.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true }, new FsmBool { Name = "Damaged", UseVariable = true } };
                    fsm.Fsm.Init(fsm); _parts.Add(name, fsm); Main.FsmVariables.FindFsmGameObject(name).Value = fsm.gameObject;
                }
                var batteryFloats = new List<FsmFloat>(Battery.FsmVariables.FloatVariables);
                batteryFloats.Add(new FsmFloat { Name = "ChargeMax", UseVariable = true }); Battery.FsmVariables.FloatVariables = batteryFloats.ToArray();
                Main.FsmVariables.FindFsmGameObject("db_Battery").Value = Battery.gameObject;
                Main.FsmVariables.FindFsmGameObject("AmpCalc").Value = Amps.gameObject;
                var row = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Systems/Electrics", "Electrics");
                foreach (Dictionary<string, object> rawState in (IEnumerable)row["states"])
                {
                    string name = (string)rawState["name"]; if (Array.IndexOf(ChargingStates, name) < 0) continue;
                    var state = NativeBagPartChecks.State(Main, name); var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> raw in (IEnumerable)rawState["actions"])
                    {
                        string type = (string)raw["type"];
                        bool external = type.EndsWith(".ActivateGameObject", StringComparison.Ordinal) || type.EndsWith(".SetFsmBool", StringComparison.Ordinal);
                        var action = external ? new TemperatureQuiet() : (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { raw, Main });
                        foreach (var field in action.GetType().GetFields())
                            if (field.FieldType == typeof(FsmFloat) && field.GetValue(action) is FsmFloat value && value.UseVariable)
                            { if (value.Name == "RPM") field.SetValue(action, Rpm); if (value.Name == "EngineTemp") field.SetValue(action, Engine); }
                        actions.Add(action);
                    }
                    state.Actions = actions.ToArray(); foreach (var action in state.Actions) action.Init(state);
                    var transitions = new List<FsmTransition>();
                    if (name != "Delay")
                        foreach (Dictionary<string, object> t in (IEnumerable)rawState["transitions"])
                            transitions.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent((string)t["event"]), ToState = (string)t["to"] });
                    state.Transitions = transitions.ToArray();
                }
            }

            internal ThermalResult ChargeCycle(string fault = "working", float condition = 98, float charge = 100)
            {
                foreach (var part in _parts.Values)
                {
                    part.FsmVariables.FindFsmFloat("Wear").Value = condition; part.FsmVariables.FindFsmFloat("Efficiency").Value = 350;
                    part.FsmVariables.FindFsmBool("Installed").Value = true; part.FsmVariables.FindFsmBool("Damaged").Value = false;
                }
                if (fault == "damaged") _parts["db_Alternator"].FsmVariables.FindFsmBool("Damaged").Value = true;
                else if (fault == "fanbelt") _parts["db_Fanbelt"].FsmVariables.FindFsmBool("Installed").Value = false;
                else if (fault == "alternator") _parts["db_Alternator"].FsmVariables.FindFsmBool("Installed").Value = false;
                else if (fault == "wiring") _parts["db_wAlternator"].FsmVariables.FindFsmBool("Installed").Value = false;
                else if (fault == "regulator") _parts["db_wRegulator"].FsmVariables.FindFsmBool("Installed").Value = false;
                Main.FsmVariables.FindFsmBool("Battery").Value = true;
                foreach (string name in new[] { "Charging", "Revs", "Volts" }) Main.FsmVariables.FindFsmFloat(name).Value = 0;
                Main.FsmVariables.FindFsmFloat("Charge").Value = charge; Main.FsmVariables.FindFsmFloat("DurabilityAlternator").Value = .75f;
                StoredCharge.Value = charge; Battery.FsmVariables.FindFsmFloat("ChargeMax").Value = 0; Amps.FsmVariables.FindFsmFloat("Amps").Value = .05f;
                NativeBagPartChecks.Fire(Main, "Check alternator");
                return new ThermalResult { Branch = Main.ActiveStateName, Values = new[] {
                    Main.FsmVariables.FindFsmFloat("Charging").Value, Main.FsmVariables.FindFsmFloat("Revs").Value,
                    StoredCharge.Value, Battery.FsmVariables.FindFsmFloat("ChargeMax").Value, _parts["db_Alternator"].FsmVariables.FindFsmFloat("Wear").Value,
                    Main.FsmVariables.FindFsmFloat("Volts").Value } };
            }

            internal void RebindRpm()
            {
                Set(Item, "NextElectricalInputProbeAt", 0f); CallStatic("EnsureElectricalInputs", Item);
                if (Get(Item, "NativeElectrical") == null) { Set(Item, "NextElectricalInputProbeAt", 0f); CallStatic("EnsureElectricalInputs", Item); }
            }
            internal void OriginalRpmInputs()
            {
                foreach (var action in new[] { NativeBagPartChecks.State(Main, "Engine running?").Actions[0],
                    NativeBagPartChecks.State(Main, "Charge battery").Actions[1], NativeBagPartChecks.State(Main, "Run on battery").Actions[0] })
                    Require(ReferenceEquals(Get(action, "float1"), Rpm), "Electrical RPM input was not restored.");
            }
            internal void RpmFault(string name)
            {
                var running = NativeBagPartChecks.State(Main, "Engine running?"); var charging = NativeBagPartChecks.State(Main, "Charge battery");
                var battery = NativeBagPartChecks.State(Main, "Run on battery"); object target = running.Actions[0]; string field = "float1";
                if (name == "threshold") field = "float2";
                else if (name == "event") field = "greaterThan";
                else if (name == "cadence") field = "everyFrame";
                else if (name == "charging operand" || name == "charging output" || name == "efficiency") { target = charging.Actions[1]; field = name == "charging operand" ? "float1" : name == "charging output" ? "storeResult" : "float2"; }
                else if (name == "drain divisor" || name == "drain output") { target = battery.Actions[0]; field = name == "drain divisor" ? "float2" : "storeResult"; }
                else if (name == "drain minimum") { target = battery.Actions[1]; field = "minValue"; }
                object original = Get(target, field); bool enabled = running.Actions[0].Enabled; var variables = Main.FsmVariables.FloatVariables;
                var actions = running.Actions; var transitions = running.Transitions; object rule = Get(Get(Item, "NativeElectrical"), "Rule"); object stateName = Get(rule, "RunningState");
                PlayMakerFSM? duplicate = null;
                try
                {
                    if (name == "enabled") running.Actions[0].Enabled = false;
                    else if (name == "shadow") { var copy = new List<FsmFloat>(variables); copy.Add(new FsmFloat { Name = "RPM", UseVariable = true }); Main.FsmVariables.FloatVariables = copy.ToArray(); }
                    else if (name == "duplicate") { duplicate = Main.gameObject.AddComponent<PlayMakerFSM>(); duplicate.enabled = false; duplicate.FsmName = Main.FsmName; }
                    else if (name == "array") running.Actions = new FsmStateAction[0];
                    else if (name == "transition") running.Transitions = new FsmTransition[0];
                    else if (name == "catalog") Set(rule, "RunningState", "Missing");
                    else Set(target, field, name == "cadence" ? (object)true : name == "event" ? FsmEvent.GetFsmEvent("FINISHED") : new FsmFloat(999));
                    RebindRpm(); Require(Get(Item, "NativeElectrical") == null, "Malformed electrical RPM group still bound.");
                }
                finally
                {
                    Set(target, field, original); actions[0].Enabled = enabled; Main.FsmVariables.FloatVariables = variables; running.Actions = actions; running.Transitions = transitions; Set(rule, "RunningState", stateName);
                    if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                }
                RebindRpm(); Require(Get(Item, "NativeElectrical") != null, "Electrical RPM group failed to recover."); OriginalRpmInputs();
            }
        }
    }
}
