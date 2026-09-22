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
        private static readonly string[] RadiatorFields = { "Wear", "Coolant", "PressureCap", "FlectEfficiency" };
        private static readonly float[] SavedRadiatorValues = { 77, 6.2f, 13, .7f };
        private sealed partial class Fixture
        {
            internal PlayMakerFSM? SavedRadiator;
            internal PlayMakerFSM SavedRadiatorPart = null!;
            internal GameObject SavedRadiatorCap = null!;
            private PlayMakerFSM EnsureSavedRadiator()
            {
                if (SavedRadiator != null) return SavedRadiator;
                SavedRadiator = Empty(MountObject("CORRIS/Assemblies/VINP_Radiator"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Remove other", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                    states.Add(new FsmState(SavedRadiator.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedRadiator.Fsm.States = states.ToArray(); SavedRadiator.Fsm.StartState = "Idle";
                SavedRadiator.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                SavedRadiator.FsmVariables.FloatVariables = new[] { Scalar("Wear", 77), Scalar("Coolant", 6.2f), Scalar("PressureCap", 13), Scalar("FlectEfficiency", .7f) };
                SavedRadiatorPart = Data(Child(SavedRadiator.gameObject, "saved radiator"), "VIN2019", 1);
                var scalars = new List<FsmFloat>(SavedRadiatorPart.FsmVariables.FloatVariables);
                scalars.Add(Scalar("Coolant", 6.2f)); scalars.Add(Scalar("CoolantMax", 8.5f)); scalars.Add(Scalar("PressureCap", 13)); scalars.Add(Scalar("FlectEfficiency", .7f));
                SavedRadiatorPart.FsmVariables.FloatVariables = scalars.ToArray(); SavedRadiatorPart.FsmVariables.FindFsmFloat("Wear").Value = 77;
                SavedRadiatorCap = Child(SavedRadiator.gameObject, "saved radiator cap");
                SavedRadiator.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedRadiatorPart.gameObject), ObjectVar("OpenCap", SavedRadiatorCap) };
                return SavedRadiator;
            }
            internal void SetRadiator(bool installed = true, float coolant = 4.5f, float wear = 83, float pressure = 16, float fan = 2.1f)
            { ReceiveIntake(new EngineBlockState { RadiatorInstalled = installed, RadiatorWear = installed ? wear : 0, RadiatorCoolant = installed ? coolant : 0,
                RadiatorPressureCap = installed ? pressure : 0, RadiatorFlectEfficiency = installed ? fan : 0 }); }
            private void AssertSavedRadiator()
            {
                if (SavedRadiator == null) return;
                foreach (var data in new[] { SavedRadiator, SavedRadiatorPart })
                    for (int i = 0; i < RadiatorFields.Length; i++) Require(data.FsmVariables.FindFsmFloat(RadiatorFields[i]).Value == SavedRadiatorValues[i], "Saved radiator " + RadiatorFields[i] + " changed.");
                Require(SavedRadiator.FsmVariables.FindFsmBool("Installed").Value && SavedRadiatorPart.FsmVariables.FindFsmString("ID").Value == "VIN2019"
                    && SavedRadiatorPart.FsmVariables.FindFsmInt("AssemblyID").Value == 1 && SavedRadiatorPart.transform.parent == SavedRadiator.transform
                    && SavedRadiator.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedRadiatorPart.gameObject && SavedRadiatorCap.activeSelf, "Saved radiator assembly or cap changed.");
            }
            internal PlayMakerFSM MakeNativeRadiator()
            {
                var obj = EnsureSavedRadiator().gameObject; UnityEngine.Object.DestroyImmediate(SavedRadiator);
                var row = FindNativeRow("CORRIS/Assemblies/VINP_Radiator", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmFloat("CoolantMax").Value = 8.5f;
                for (int i = 0; i < RadiatorFields.Length; i++) data.FsmVariables.FindFsmFloat(RadiatorFields[i]).Value = SavedRadiatorValues[i];
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedRadiatorPart.gameObject; data.FsmVariables.FindFsmGameObject("OpenCap").Value = SavedRadiatorCap;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    // Exercise native scalar copying, cap visibility and part removal;
                    // body creation, fan mesh parenting and hose/global events stay outside this fixture.
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Update 2"
                        || state.Name == "Install 2" && (i >= 1 && i <= 7 || i == 10)
                        || state.Name == "Remove part" && (i == 0 || i == 1 || i == 3 || i == 4 || i == 7 || i == 8 || i >= 15 && i <= 19)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                SavedRadiator = data; Reader.FsmVariables.FindFsmGameObject("db_Radiator").Value = obj; return data;
            }
            internal void ConfigureRadiatorCooling()
            {
                foreach (string name in new[] { "Radiator installed?", "Radiator Data", "No radiator", "Flect", "Fan ON", "Fan OFF", "Water pres" })
                {
                    var state = NativeBagPartChecks.State(Reader, name);
                    for (int i = 0; i < state.Actions.Length; i++)
                    {
                        // Preserve the five bound reader action instances and guarded coolant writer.
                        if (name == "Radiator installed?" && i == 0 || name == "Radiator Data" && (i == 0 || i == 1 || i == 3 || i == 5)
                            || name == "Flect" && i == 1 || (name == "Fan ON" || name == "Water pres") && i == 0) continue;
                        state.Actions[i] = Import(name, i); state.Actions[i].Init(state);
                    }
                    if (name == "Radiator installed?" || name == "Flect" || name == "Water pres") RestoreNativeTransitions(name);
                    else state.Transitions = new FsmTransition[0];
                }
                foreach (string name in new[] { "Air cooling", "Check temp", "Release pressure" }) NativeBagPartChecks.State(Reader, name).Transitions = new FsmTransition[0];
            }
            internal float CoolingValue(string name) => Reader.FsmVariables.FindFsmFloat(name).Value;
            internal void CoolingValue(string name, float value) { Reader.FsmVariables.FindFsmFloat(name).Value = value; }
            internal bool RadiatorFans => Reader.FsmVariables.FindFsmBool("RadiatorFansOn").Value;
        }
        internal static void RunRadiator(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN126", "Cooling", "radiator-input-probe.json"))
            {
                var installed = f.Action("Radiator installed?", 0); var coolant = f.Action("Radiator Data", 0); var original = (FsmOwnerDefault)Get(coolant, "gameObject");
                var reads = new[] { installed, coolant, f.Action("Radiator Data", 1), f.Action("Radiator Data", 3), f.Action("Flect", 1) };
                Action saved = () => { f.AssertSaved(); Require(!f.SavedRadiator!.enabled, "Saved radiator Data resumed."); };
                check("radiator inputs: warm saved data becomes absent host input", () =>
                {
                    coolant.OnEnter(); Require(f.CoolingValue("WaterLevel") == 6.2f, "Saved coolant cache did not warm.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Radiator failed binding.");
                    foreach (var read in reads) read.OnEnter(); Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value && f.CoolingValue("WaterLevel") == 0
                        && f.Target(coolant) != original.GameObject.Value && !f.Target(coolant).GetComponent<PlayMakerFSM>().enabled && f.Target(coolant).GetComponent<Rigidbody>() == null
                        && original.GameObject.Value == f.SavedRadiator!.gameObject, "Absent radiator borrowed saved data or changed the shared owner."); saved();
                });
                check("radiator inputs: arrival leaves native scratch and state until five entry-only reads", () =>
                {
                    string state = f.Reader.ActiveStateName; f.SetRadiator(); Require(f.Prepare() && f.CoolingValue("WaterLevel") == 0 && f.Reader.ActiveStateName == state, "Arrival replayed cooling.");
                    foreach (var read in reads) { Require(!(bool)Get(read, "everyFrame"), "Radiator read cadence changed."); read.OnEnter(); }
                    Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value && f.CoolingValue("WaterLevel") == 4.5f && f.CoolingValue("PressureCap") == 16
                        && f.CoolingValue("Wear") == 83 && f.CoolingValue("FlectEff") == 2.1f, "Radiator host inputs diverged."); saved();
                });
                f.ConfigureRadiatorCooling();
                foreach (float amount in new[] { -1f, 0, .05f, 4.5f, 25, 27 })
                {
                    float value = amount;
                    check("radiator inputs: native coolant clamp " + value, () =>
                    { f.SetRadiator(true, value); Require(f.Prepare(), "Coolant update failed."); f.Fire("Radiator installed?"); Require(f.Reader.ActiveStateName == "Radiator Data" && f.CoolingValue("WaterLevel") == Mathf.Clamp(value, 0, 25), "Native radiator branch or clamp diverged."); saved(); });
                }
                foreach (float wear in new[] { 4.99f, 5, 5.01f })
                {
                    float value = wear;
                    check("radiator inputs: native damaged radiator leak keeps saved coolant at wear " + value, () =>
                    { f.SetRadiator(true, 4.5f, value); f.Prepare(); f.Fire("Radiator Data"); Require(f.CoolingValue("Wear") == value && !f.Action("Radiator Data", 5).Enabled, "Native radiator wear read or leak protection diverged."); saved(); });
                }
                foreach (float cap in new[] { 13f, 16 }) foreach (float delta in new[] { -.1f, .1f })
                {
                    float pressure = cap, offset = delta;
                    check("radiator inputs: native pressure threshold " + pressure + " offset " + offset, () =>
                    {
                        f.SetRadiator(true, 4.5f, 83, pressure); f.Prepare(); f.Fire("Radiator Data"); f.CoolingValue("CoolantTemp", pressure * 1.3f + 100 + offset); f.Fire("Water pres");
                        Require(Near(f.CoolingValue("WaterPressureLimit"), pressure * 1.3f + 100) && f.Reader.ActiveStateName == (offset > 0 ? "Release pressure" : "Check temp"), "Native pressure decision diverged."); saved();
                    });
                }
                foreach (float temp in new[] { 79.9f, 80, 85, 92, 92.1f }) foreach (bool wasOn in new[] { false, true })
                {
                    float value = temp; bool previous = wasOn;
                    check("radiator inputs: native electric fan hysteresis " + value + " previously " + previous, () =>
                    {
                        f.SetRadiator(); f.Prepare(); f.Reader.FsmVariables.FindFsmBool("RadiatorFansOn").Value = previous; f.CoolingValue("CoolantTemp", value); f.Fire("Flect");
                        Require(f.RadiatorFans == (value >= 80 && (previous || value > 92)), "Native fan hysteresis diverged."); saved();
                    });
                }
                check("radiator inputs: fanless stock radiator turns off electric cooling", () =>
                { f.SetRadiator(true, 4.5f, 83, 13, 0); f.Prepare(); f.CoolingValue("CoolantTemp", 100); f.Reader.FsmVariables.FindFsmBool("RadiatorFansOn").Value = true; f.Fire("Flect"); Require(!f.RadiatorFans, "Stock radiator kept electric fan cooling."); saved(); });
                check("radiator inputs: removal rejects stale refit and uses native no-radiator branch", () =>
                {
                    var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetRadiator(false); Call(f.Sync, "OnEngineBlockState", old); old.Revision++; Call(f.Sync, "OnEngineBlockState", old);
                    f.Prepare(); f.CoolingValue("CoolingAirRateModifier", 4554); f.Fire("Radiator installed?"); Require(f.Reader.ActiveStateName == "No radiator" && f.CoolingValue("WaterLevel") == 0 && f.CoolingValue("CoolingAirRateModifier") == 4154, "Removed radiator retained host coolant."); saved();
                    f.SetRadiator(); Require(f.Prepare(), "Refit failed.");
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName;
                    check("radiator inputs: foreign " + field + " pauses and repairs cooling", () =>
                    {
                        var before = Get(coolant, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "WaterLevel", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(coolant, field, changed); try { f.Fire("Radiator Data"); Require(!f.Reader.enabled, "Foreign radiator signature escaped entry guard."); saved(); }
                        finally { Set(coolant, field, field == "gameObject" ? original : before); if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Repaired radiator reader stayed paused.");
                    });
                }
                check("radiator inputs: relocated saved mount pauses and recovers", () =>
                {
                    var mount = f.SavedRadiator!.transform; var parent = mount.parent; mount.parent = f.Extras.transform;
                    try { f.Fire("Radiator Data"); Require(!f.Reader.enabled, "Relocated fixed radiator supplied cooling."); saved(); }
                    finally { mount.parent = parent; if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Restored radiator mount failed recovery.");
                });
                check("radiator inputs: destroyed proxy restores accepted cooling data", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(coolant)); Require(f.Prepare(), "Destroyed radiator proxy failed repair."); coolant.OnEnter(); Require(f.CoolingValue("WaterLevel") == 4.5f, "Repair lost host radiator."); saved(); });
                check("radiator inputs: disconnect restores owner and keeps saved radiator protected", () =>
                { Call(f.Sync, "ReleaseSession"); Require(ReferenceEquals(Get(coolant, "gameObject"), original) && ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnect retained radiator state or lost owner."); saved(); });
            }
            RunRadiatorCapture(check);
        }
    }
}
