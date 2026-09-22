using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            private readonly bool _ambientProbe;
            internal PlayMakerFSM? SavedCoolingAmbient;
            private PlayMakerFSM EnsureSavedCoolingAmbient()
            {
                if (SavedCoolingAmbient != null) return SavedCoolingAmbient;
                var data = Empty(MountObject("CORRIS/Functions/RoofCheck"), "Raycast"); var states = new List<FsmState>();
                foreach (string name in new[] { "Cast ray", "Check roof", "Under roof", "Under sky" })
                    states.Add(new FsmState(data.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                data.Fsm.States = states.ToArray(); data.Fsm.StartState = "Cast ray";
                data.FsmVariables.FloatVariables = new[] { Scalar("TempCar", 20), Scalar("TempArea", 20), Scalar("HeatChange", 3) };
                data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "RoofCover", UseVariable = true } };
                SavedCoolingAmbient = data; return data;
            }
            private void SeedInitialAmbient()
            {
                // Revision zero leaves the older fixture's head-prerequisite gate
                // intact; subsequent part packets carry their own ambient input.
                var state = new EngineBlockState(); SeedRequiredAmbient(state);
                Call(Sync, "OnEngineBlockState", PacketCodec.Decode(PacketCodec.Encode(state)));
            }
            private void SeedRequiredAmbient(EngineBlockState state)
            {
                // Older part fixtures keep a separate valid ambient prerequisite;
                // this fixture explicitly controls unavailable and cold-join cases.
                if (!_ambientProbe) { state.CoolingAmbientAvailable = true; state.CoolingAmbientTemperature = -12; }
            }
            internal void ClearEngineBlockInputs()
            { ((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Clear(); if (!_ambientProbe) ReceiveIntake(new EngineBlockState()); }
            internal void SetCoolingAmbient(bool available, float temperature = 0)
            { ReceiveIntake(new EngineBlockState { CoolingAmbientAvailable = available, CoolingAmbientTemperature = temperature }); }
            internal void ConfigureAmbientCooling()
            {
                foreach (string name in new[] { "Reset", "Air cooling" })
                {
                    var state = NativeBagPartChecks.State(Reader, name);
                    if (name == "Reset")
                    {
                        foreach (int i in new[] { 3, 4, 5, 8 }) { state.Actions[i] = Import(name, i); state.Actions[i].Init(state); }
                    }
                    else for (int i = 0; i < state.Actions.Length; i++) { state.Actions[i] = Import(name, i); state.Actions[i].Init(state); }
                    state.Transitions = new FsmTransition[0];
                }
            }
            internal PlayMakerFSM MakeNativeCoolingAmbient()
            {
                var obj = EnsureSavedCoolingAmbient().gameObject; UnityEngine.Object.DestroyImmediate(SavedCoolingAmbient);
                var row = FindNativeRow("CORRIS/Functions/RoofCheck", "Raycast"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                var roof = Empty(Child(Extras, "controlled warm roof"), "Data"); roof.FsmVariables.FloatVariables = new[] { Scalar("Temp", 12) };
                data.FsmVariables.FindFsmGameObject("Roof").Value = roof.gameObject;
                data.FsmVariables.FindFsmFloat("TempCar").Value = -12; data.FsmVariables.FindFsmFloat("HeatChange").Value = 3;
                foreach (Dictionary<string, object> rawState in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)rawState["name"]); var raw = (List<object>)rawState["actions"]; var actions = new FsmStateAction[raw.Count];
                    // Exercise native shelter selection and thermal progression with
                    // a controlled roof hit; physical raycasts and rain effects remain outside the fixture.
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Cast ray" ? new Quiet() : NativeAction((Dictionary<string, object>)raw[i], data);
                    state.Actions = actions; foreach (var action in actions) action.Init(state); if (state.Name != "Check roof") state.Transitions = new FsmTransition[0];
                }
                var underRoof = NativeBagPartChecks.State(data, "Under roof"); ((FsmFloat)Get(underRoof.Actions[2], "minValue")).Value = -20;
                var sky = NativeBagPartChecks.State(data, "Under sky"); ((FsmFloat)Get(sky.Actions[1], "minValue")).Value = -20;
                SavedCoolingAmbient = data; Reader.FsmVariables.FindFsmGameObject("RoofCheck").Value = obj; return data;
            }
        }
        internal static void RunCoolingAmbient(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN126", "Cooling", "cooling-ambient-input-probe.json"))
            {
                var read = f.Action("Reset", 1); var original = (FsmOwnerDefault)Get(read, "gameObject");
                Action saved = () => { f.AssertSaved(); Require(f.SavedCoolingAmbient!.FsmVariables.FindFsmFloat("TempCar").Value == 20 && f.SavedCoolingAmbient.enabled
                    && f.Reader.FsmVariables.FindFsmGameObject("RoofCheck").Value == f.SavedCoolingAmbient.gameObject, "Guest RoofCheck was overwritten, paused or rewired."); };
                check("cooling ambient inputs: cold join pauses only Cooling without replacing warmed scratch", () =>
                {
                    read.OnEnter(); Require(f.CoolingValue("TempArea") == 20, "Local ambient cache did not warm.");
                    f.ClearEngineBlockInputs(); Require(!f.Prepare() && !f.Reader.enabled && f.CoolingValue("TempArea") == 20, "Missing ambient invented a temperature or left Cooling running."); saved();
                    var waitingProxy = f.Target(read); var companion = f.AddConsumer("Oil"); f.Prepare();
                    Require(companion.enabled && f.Target(read) == waitingProxy, "Missing ambient paused another consumer or rebuilt its valid proxy while waiting.");
                });
                check("cooling ambient inputs: actual admission accepts a safely paused consumer before connecting", () =>
                {
                    Property(f.Session, "State", WinterMP.Core.Session.SessionState.Idle);
                    var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
                    Require((bool)guard.GetMethod("TryBeginGuest", Static).Invoke(null, null) && !f.Reader.enabled && f.CoolingValue("TempArea") == 20,
                        "Ambient wait blocked admission or resumed Cooling before the connection.");
                    Property(f.Session, "State", WinterMP.Core.Session.SessionState.Connected); Require(!f.Prepare(), "Connection alone supplied ambient temperature."); saved();
                });
                check("cooling ambient inputs: pending temperature cannot conceal malformed bindings during admission", () =>
                {
                    var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
                    var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                    var entries = (IList)Get(catalog.GetProperty("GuestEngineInputs", Static).GetValue(null, null), "Entries");
                    object? ambient = null; foreach (var entry in entries) if (Get(entry, "CoolingAmbientSource") != null) ambient = entry;
                    Require(ambient != null, "Ambient catalog entry missing."); int previousIndex = entries.IndexOf(ambient); entries.Remove(ambient); entries.Insert(0, ambient);
                    try
                    {
                        foreach (var action in new[] { read, f.Readers[0] })
                        {
                            var before = Get(action, "fsmName"); Set(action, "fsmName", new FsmString { Value = "Other" });
                            try { Require(!(bool)guard.GetMethod("TryBeginGuest", Static).Invoke(null, null) && !f.Reader.enabled, "Pending temperature hid a malformed input binding."); }
                            finally { Set(action, "fsmName", before); }
                            Require((bool)guard.GetMethod("TryBeginGuest", Static).Invoke(null, null) && !f.Reader.enabled, "Repaired pending admission failed."); saved();
                        }
                    }
                    finally { entries.Remove(ambient); entries.Insert(previousIndex, ambient); }
                });
                check("cooling ambient inputs: host arrival repairs consumer while preserving native scratch", () =>
                {
                    f.SetCoolingAmbient(true, -12.5f); Require(f.Prepare() && f.Reader.enabled && f.CoolingValue("TempArea") == 20, "Ambient arrival failed recovery or replayed the read.");
                    read.OnEnter(); Require(f.CoolingValue("TempArea") == -12.5f && !(bool)Get(read, "everyFrame") && f.Target(read) != original.GameObject.Value
                        && !f.Target(read).GetComponent<PlayMakerFSM>().enabled && f.Target(read).GetComponent<PlayMakerFSM>().FsmName == "Raycast", "Host ambient projection diverged."); saved();
                });
                f.ConfigureAmbientCooling();
                foreach (float temp in new[] { -40f, -12.5f, 0, 12, 25, 30, 45 }) foreach (float speed in new[] { 0f, 20, 120 })
                {
                    float temperature = temp, kmh = speed;
                    check("cooling ambient inputs: native air cooling at temperature " + temperature + " and speed " + kmh, () =>
                    {
                        f.SetCoolingAmbient(true, temperature); Require(f.Prepare(), "Ambient update failed binding."); f.Fire("Reset");
                        ((FsmFloat)Get(f.Action("Air cooling", 1), "float1")).Value = kmh; f.CoolingValue("CoolingAirRateModifier", 2900); f.CoolingValue("WaterLevel", 25); f.CoolingValue("CoolingFlectRate", 0); f.CoolingValue("CoolingDynoFan", 0);
                        f.Fire("Air cooling"); float expected = Mathf.Clamp(Math.Abs(kmh * (temperature - 30)) / 2900, .03f, 2.1f);
                        Require(f.CoolingValue("TempArea") == temperature && Near(f.CoolingValue("CoolingAirRate"), expected) && Near(f.CoolingValue("CoolingRateCoolant"), expected), "Native ambient cooling math diverged."); saved();
                    });
                }
                check("cooling ambient inputs: unavailable revision blocks entries and rejects delayed and conflicting recovery", () =>
                {
                    f.SetCoolingAmbient(true, -10); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetCoolingAmbient(false);
                    f.CoolingValue("TempArea", 71); f.Fire("Reset"); Require(!f.Reader.enabled && f.CoolingValue("TempArea") == 71, "Unavailable ambient allowed Reset to change scratch.");
                    Call(f.Sync, "OnEngineBlockState", old); old.Revision++; Call(f.Sync, "OnEngineBlockState", old); Require(!f.Prepare(), "Stale/conflicting ambient resumed Cooling."); saved();
                    f.SetCoolingAmbient(true, 0); Require(f.Prepare() && f.Reader.enabled && f.CoolingValue("TempArea") == 0, "Valid freezing temperature failed blocked-entry recovery."); saved();
                });
                foreach (string fieldName in new[] { "gameObject", "fsmName", "variableName", "storeValue", "everyFrame" })
                {
                    string field = fieldName;
                    check("cooling ambient inputs: foreign " + field + " pauses and repairs the native read", () =>
                    {
                        var before = Get(read, field); object value = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "TempArea", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(read, field, value); try { f.Fire("Reset"); Require(!f.Reader.enabled, "Foreign ambient signature escaped guard."); saved(); }
                        finally { Set(read, field, field == "gameObject" ? original : before); if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Repaired ambient reader stayed paused.");
                    });
                }
                check("cooling ambient inputs: moved source pauses Cooling and recovers", () =>
                {
                    var source = f.SavedCoolingAmbient!.transform; var parent = source.parent; source.parent = f.Extras.transform;
                    try { f.Fire("Reset"); Require(!f.Reader.enabled, "Moved RoofCheck supplied cooling."); saved(); }
                    finally { source.parent = parent; if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Restored ambient source failed recovery.");
                });
                check("cooling ambient inputs: destroyed proxy restores accepted zero temperature", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(read)); Require(f.Prepare(), "Destroyed ambient proxy failed repair."); read.OnEnter(); Require(f.CoolingValue("TempArea") == 0, "Repaired proxy lost freezing temperature."); saved(); });
                check("cooling ambient inputs: disconnect clears host cache and restores original owner", () =>
                { Call(f.Sync, "ReleaseSession"); Require(ReferenceEquals(Get(read, "gameObject"), original) && ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnect retained ambient data or lost owner."); saved(); });
            }
            RunCoolingAmbientCapture(check);
        }
    }
}
