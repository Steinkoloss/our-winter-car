using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal IList? SavedValves;
            private void ConfigureValveSources(PlayMakerFSM reader)
            {
                var head = EnsureSavedCylinderHead();
                if (SavedValves == null)
                {
                    Type? type = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if (assembly.GetName().Name == "Assembly-CSharp") type = assembly.GetType("PlayMakerArrayListProxy");
                    Require(type != null, "Native valve array type missing."); var proxy = SavedCylinderHeadPart.gameObject.AddComponent(type); Set(proxy, "referenceName", "Valves");
                    SavedValves = (IList)type!.GetProperty("arrayList").GetValue(proxy, null); SavedValves.Clear(); for (int i = 0; i < 8; i++) SavedValves.Add(8f + i);
                }
                reader.FsmVariables.FindFsmGameObject("db_Cylinderhead").Value = head.gameObject;
                reader.FsmVariables.FindFsmGameObject("Cylinderhead").Value = SavedCylinderHeadPart.gameObject;
            }
            internal void SetValves(float[] values, bool available = true)
            {
                var state = ((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Get() ?? new EngineBlockState { Flags = 11, Wear = 90 };
                state.ValvesAvailable = available; state.ValveSettings = (float[])values.Clone(); if (available) { state.Flags |= 11; state.Wear = 90; }
                ReceiveIntake(state);
            }
            internal void AssertSavedValves()
            {
                if (SavedValves == null) return;
                Require(SavedValves.Count == 8, "Saved valve count changed."); for (int i = 0; i < 8; i++) Require((float)SavedValves[i] == 8f + i, "Saved valve setting changed.");
            }
        }
        internal static void RunValveInputs(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN125", "FuelLine", "valve-adjustment-probe.json"))
            {
                var valves = f.AddConsumer("Valves"); var actions = new FsmStateAction[8]; var owners = new object[8];
                for (int i = 0; i < 8; i++) { actions[i] = NativeBagPartChecks.State(valves, "Cyl " + (i / 2 + 1) + (i % 2 == 0 ? " intake" : " exhaust")).Actions[0]; owners[i] = Get(actions[i], "gameObject"); }
                Action saved = () => { f.AssertSavedValves(); f.AssertSaved(); };
                check("valve inputs: warm saved reads switch to unavailable host zeros", () =>
                {
                    actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("Data").Value == 8, "Saved native array failed to warm.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Valve projection failed."); actions[0].OnEnter();
                    Require(valves.FsmVariables.FindFsmFloat("Data").Value == 0, "Missing host borrowed saved valve adjustment."); saved();
                });
                check("valve inputs: independent host values wait for native reads and preserve head scratch", () =>
                {
                    var source = valves.FsmVariables.FindFsmGameObject("Cylinderhead").Value; string state = valves.ActiveStateName;
                    f.SetValves(new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }); Require(f.Prepare(), "Host valve projection failed.");
                    Require(valves.ActiveStateName == state && valves.FsmVariables.FindFsmFloat("Data").Value == 0 && valves.FsmVariables.FindFsmGameObject("Cylinderhead").Value == source, "Arrival altered shared scratch or state.");
                    for (int i = 0; i < 8; i++) { actions[i].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("Data").Value == i + 1, "Valve order changed."); }
                    saved();
                });
                for (int slot = 0; slot < 8; slot++) foreach (float setting in new[] { 3f, 5f, 7f })
                {
                    int index = slot; float value = setting;
                    check("valve inputs: native slot " + index + " adjustment=" + value + " preserves arithmetic and tolerance decision", () =>
                    {
                        var values = new float[8]; for (int i = 0; i < 8; i++) values[i] = 5; values[index] = value; f.SetValves(values); f.Prepare();
                        string name = "Cyl " + (index / 2 + 1) + (index % 2 == 0 ? " intake" : " exhaust"); var state = NativeBagPartChecks.State(valves, name);
                        var row = f.FindNativeRow("CORRIS/Simulation/Engine/Valves", "Valves");
                        foreach (Dictionary<string, object> native in (IEnumerable)row["states"]) if ((string)native["name"] == name)
                        {
                            var raw = (List<object>)native["actions"]; for (int i = 1; i < 3; i++) { state.Actions[i] = NativeAction((Dictionary<string, object>)raw[i], valves); state.Actions[i].Init(state); }
                        }
                        var original = state.Transitions;
                        state.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("LOOSE"), ToState = "Loop" }, new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("TIGHT"), ToState = "Calculations" }, new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = "Reset power" } };
                        valves.FsmVariables.FindFsmFloat("ValveTolerance").Value = 1; valves.FsmVariables.FindFsmFloat("DataPower").Value = 100; valves.FsmVariables.FindFsmFloat("DataTorque").Value = 200;
                        try
                        {
                            NativeBagPartChecks.Fire(valves, name); Require(valves.ActiveStateName == (value < 4 ? "Loop" : value > 6 ? "Calculations" : "Reset power"), "Native valve tolerance branch changed.");
                            Require(valves.FsmVariables.FindFsmFloat(index % 2 == 0 ? "DataPower" : "DataTorque").Value == (index % 2 == 0 ? 100 + value : 200 - value), "Native adjustment arithmetic changed."); saved();
                        }
                        finally { state.Transitions = original; }
                    });
                }
                foreach (string fieldName in new[] { "reference", "atIndex", "result", "failureEvent", "gameObject" })
                {
                    string field = fieldName;
                    check("valve inputs: foreign " + field + " is contained and repairs", () =>
                    {
                        var action = actions[4]; var original = Get(action, field);
                        object changed = field == "reference" ? (object)new FsmString { Value = "Other" } : field == "atIndex" ? new FsmInt { Value = 1 }
                            : field == "result" ? new FsmVar { Type = VariableType.Float, variableName = "Data", useVariable = true }
                            : field == "failureEvent" ? (object)FsmEvent.GetFsmEvent("OTHER") : new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.SavedCylinderHeadPart.gameObject } };
                        Set(action, field, changed);
                        try { NativeBagPartChecks.Fire(valves, "Cyl 3 intake"); Require(!valves.enabled, "Foreign valve reader escaped guard."); saved(); }
                        finally { Set(action, field, original); if (!f.Prepare()) f.Prepare(); }
                        Require(valves.enabled, "Restored valve reader stayed paused.");
                    });
                }
                check("valve inputs: stale and conflicting snapshots cannot restore removed tuning", () =>
                {
                    var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetValves(new float[8], false); Call(f.Sync, "OnEngineBlockState", old); old.Revision++;
                    Call(f.Sync, "OnEngineBlockState", old); f.Prepare(); foreach (var action in actions) { action.OnEnter(); Require(valves.FsmVariables.FindFsmFloat("Data").Value == 0, "Stale adjustment returned."); } saved();
                });
                check("valve inputs: protected disconnected session clears cached host tuning", () =>
                {
                    f.SetValves(new float[] { 6, 6, 6, 6, 6, 6, 6, 6 }); f.Prepare(); Property(f.Session, "State", SessionState.Idle);
                    try { Require(f.Prepare(), "Disconnected valve preparation failed."); actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("Data").Value == 0, "Disconnected session retained host tuning."); saved(); }
                    finally { Property(f.Session, "State", SessionState.Connected); f.Prepare(); }
                });
                check("valve inputs: replaced native array reader rebinds before it can read saved tuning", () =>
                {
                    var state = NativeBagPartChecks.State(valves, "Cyl 1 intake"); var original = state.Actions[0];
                    var row = f.FindNativeRow("CORRIS/Simulation/Engine/Valves", "Valves");
                    foreach (Dictionary<string, object> native in (IEnumerable)row["states"]) if ((string)native["name"] == state.Name)
                    { state.Actions[0] = NativeAction((Dictionary<string, object>)((List<object>)native["actions"])[0], valves); state.Actions[0].Init(state); }
                    try { Require(f.Prepare(), "Replacement valve action failed to bind."); state.Actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("Data").Value == 6, "New reader used saved valve array."); saved(); }
                    finally { state.Actions[0] = original; f.Prepare(); }
                });
                check("valve inputs: proxy destruction repairs caches and disconnect restores eight owners", () =>
                {
                    f.SetValves(new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }); f.Prepare(); UnityEngine.Object.DestroyImmediate(f.Target(actions[0])); Require(f.Prepare(), "Valve proxy failed repair.");
                    actions[7].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("Data").Value == 8, "Repaired valve cache lost data."); Call(f.Sync, "ReleaseSession");
                    for (int i = 0; i < 8; i++) Require(ReferenceEquals(Get(actions[i], "gameObject"), owners[i]), "Disconnect retained array proxy owner."); saved();
                });
            }
            using (var f = new Fixture("VIN125", "FuelLine", "valve-adjustment-probe.json"))
            {
                var valves = f.AddConsumer("Valves");
                check("valve inputs: destroyed native consumer releases its owned array binding", () =>
                {
                    Require(f.Prepare(), "Valve rebind failed."); UnityEngine.Object.DestroyImmediate(valves); Require(f.Prepare(), "Destroyed valve consumer blocked other inputs.");
                    Require(((IList)Get(f.Sync, "_guestValveInputs")).Count == 0, "Destroyed consumer retained valve array."); f.AssertSaved();
                });
            }
            RunValveCapture(check);
        }
    }
}
