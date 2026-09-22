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
        private static readonly string[] CoolantHoseParts = { "RadiatorHoseTop", "RadiatorHoseBottom", "HeaterHoseInlet", "HeaterHoseOutlet" };
        private static readonly string[] CoolantHosePrefixes = { "VIN202", "VIN203", "VIN216", "VIN217" };
        private static readonly string[] CoolantHoseTargets = { "db_RadiatorHose1", "db_RadiatorHose2", "db_HeaterHose1", "db_HeaterHose2" };
        private sealed partial class Fixture
        {
            internal readonly PlayMakerFSM?[] SavedCoolantHoses = new PlayMakerFSM?[4];
            internal readonly PlayMakerFSM[] SavedCoolantHoseParts = new PlayMakerFSM[4];
            private PlayMakerFSM EnsureSavedCoolantHose(byte index)
            {
                if (SavedCoolantHoses[index] != null) return SavedCoolantHoses[index]!;
                var data = Empty(MountObject("CORRIS/Assemblies/VINP_" + CoolantHoseParts[index]), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", index < 2 ? "Update 2" : "Update" })
                    states.Add(new FsmState(data.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                data.Fsm.States = states.ToArray(); data.Fsm.StartState = "Idle";
                data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                data.FsmVariables.FloatVariables = new[] { Scalar("Wear", 77), Scalar("Tightness", 16) };
                var part = Data(Child(data.gameObject, "saved coolant hose"), CoolantHosePrefixes[index] + "9", 1);
                part.FsmVariables.FindFsmFloat("Wear").Value = 77; part.FsmVariables.FindFsmFloat("Tightness").Value = 16;
                data.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", part.gameObject) };
                SavedCoolantHoses[index] = data; SavedCoolantHoseParts[index] = part; return data;
            }
            private void AssertSavedCoolantHoses()
            {
                for (int i = 0; i < 4; i++)
                {
                    var data = SavedCoolantHoses[i]; if (data == null) continue; var part = SavedCoolantHoseParts[i];
                    foreach (var fsm in new[] { data, part }) Require(fsm.FsmVariables.FindFsmFloat("Wear").Value == 77 && fsm.FsmVariables.FindFsmFloat("Tightness").Value == 16, "Saved hose scalars changed.");
                    Require(data.FsmVariables.FindFsmBool("Installed").Value && part.FsmVariables.FindFsmString("ID").Value == CoolantHosePrefixes[i] + "9"
                        && part.FsmVariables.FindFsmInt("AssemblyID").Value == 1 && part.transform.parent == data.transform
                        && data.FsmVariables.FindFsmGameObject("ActivePart").Value == part.gameObject, "Saved hose assembly changed.");
                }
            }
            internal void SetCoolantHoses(byte mask = 15, float[]? tightness = null, float carb = 40, bool carbInstalled = true)
            {
                var state = new EngineBlockState { Flags = (byte)(carbInstalled ? 27 : 11), Wear = 90, CoolantHoseFlags = mask, CarburettorTightness = carbInstalled ? carb : 0 };
                for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) state.CoolantHoseTightness[i] = tightness == null ? 16 : tightness[i];
                ReceiveIntake(state);
            }
            internal PlayMakerFSM MakeNativeCoolantHose(byte index)
            {
                var obj = EnsureSavedCoolantHose(index).gameObject; UnityEngine.Object.DestroyImmediate(SavedCoolantHoses[index]);
                var row = FindNativeRow("CORRIS/Assemblies/VINP_" + CoolantHoseParts[index], "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmFloat("Wear").Value = 77; data.FsmVariables.FindFsmFloat("Tightness").Value = 16;
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedCoolantHoseParts[index].gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    // Keep disabled native wear disabled; removal covers scalar copying
                    // and detachment without body creation or global assembly events.
                    for (int i = 0; i < actions.Length; i++) actions[i] = (state.Name == "Update" || state.Name == "Update 2") && i == 0
                        || state.Name == "Install 2" && (i == 1 || i == 2)
                        || state.Name == "Remove part" && (i == 0 || i == 1 || i == 4 || i >= 11 && i <= 14)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                SavedCoolantHoses[index] = data; Reader.FsmVariables.FindFsmGameObject(CoolantHoseTargets[index]).Value = obj; return data;
            }
            internal void ConfigureCoolantHoseDecisions()
            {
                var reset = NativeBagPartChecks.State(Reader, "Reset");
                foreach (int i in new[] { 2, 6 }) { reset.Actions[i] = Import("Reset", i); reset.Actions[i].Init(reset); }
                reset.Transitions = new FsmTransition[0];
                foreach (string name in new[] { "Hoses", "Bottom hose", "State 5" })
                {
                    var state = NativeBagPartChecks.State(Reader, name);
                    int start = name == "Hoses" ? 5 : name == "Bottom hose" ? 1 : 0;
                    int end = name == "State 5" ? 3 : state.Actions.Length;
                    for (int i = start; i < end; i++) { state.Actions[i] = Import(name, i); state.Actions[i].Init(state); }
                    if (name != "State 5") RestoreNativeTransitions(name);
                    else state.Transitions = new FsmTransition[0];
                }
                foreach (string name in new[] { "Block damage", "Empty coolant", "Motor on?" }) NativeBagPartChecks.State(Reader, name).Transitions = new FsmTransition[0];
            }
        }
        internal static void RunCoolantHoses(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN126", "Cooling", "coolant-hose-input-probe.json"))
            {
                var read = f.Action("Hoses", 0); var original = (FsmOwnerDefault)Get(read, "gameObject");
                Action saved = () => { f.AssertSaved(); foreach (var hose in f.SavedCoolantHoses) Require(hose != null && !hose.enabled, "Saved hose resumed."); Require(f.SavedCarburettor!.FsmVariables.FindFsmFloat("Tightness").Value == 40, "Saved carburettor clamp changed."); };
                check("coolant hose inputs: warm saved clamps become absent without changing shared owners", () =>
                {
                    for (int i = 0; i < 5; i++) f.Action("Hoses", i).OnEnter(); Require(f.CoolingValue("Tightness1") == 16 && f.CoolingValue("Tightness5") == 40, "Saved clamp caches did not warm.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Hose projection failed.");
                    for (int i = 0; i < 5; i++) { f.Action("Hoses", i).OnEnter(); Require(f.CoolingValue("Tightness" + (i + 1)) == 0, "Absent clamp used saved tightness."); }
                    Require(original.GameObject.Value == f.SavedCoolantHoses[0]!.gameObject && f.Target(read) != original.GameObject.Value && !f.Target(read).GetComponent<PlayMakerFSM>().enabled, "Projection rewired the saved hose or created an active proxy."); saved();
                });
                check("coolant hose inputs: packet arrival preserves scratch until six native entry-only reads", () =>
                {
                    string state = f.Reader.ActiveStateName; f.SetCoolantHoses(15, new float[] { 4, 8, 12, 16 }, 32); Require(f.Prepare() && f.Reader.ActiveStateName == state && f.CoolingValue("Tightness1") == 0, "Arrival replayed native leak math.");
                    for (int i = 0; i < 5; i++) { var action = f.Action("Hoses", i); Require(!(bool)Get(action, "everyFrame"), "Clamp cadence changed."); action.OnEnter(); Require(f.CoolingValue("Tightness" + (i + 1)) == (i == 4 ? 32 : (i + 1) * 4), "Host clamp read diverged."); }
                    f.Action("Bottom hose", 0).OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Installed bottom hose was absent."); saved();
                });
                f.ConfigureCoolantHoseDecisions();
                foreach (byte maskValue in new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })
                {
                    byte mask = maskValue;
                    check("coolant hose inputs: independent installed mask " + mask + " selects native bottom-hose branch", () =>
                    {
                        f.SetCoolantHoses(mask); f.Prepare(); f.Fire("Bottom hose"); Require(f.Reader.ActiveStateName == ((mask & 2) == 0 ? "Empty coolant" : "Motor on?"), "Bottom hose used a different hose or saved installation.");
                        Require(!f.Action("Empty coolant", 0).Enabled, "Missing hose drained saved radiator."); saved();
                    });
                }
                foreach (int changed in new[] { 0, 1, 2, 3, 4 }) foreach (float change in new[] { -200f, -4, -.01f, 0, 4 })
                {
                    int index = changed; float delta = change;
                    check("coolant hose inputs: native combined leak at clamp " + index + " delta " + delta, () =>
                    {
                        var clamps = new float[] { 16, 16, 16, 16 }; if (index < 4) clamps[index] += delta;
                        f.SetCoolantHoses(15, clamps, index == 4 ? 40 + delta : 40); Require(f.Prepare(), "Clamp update failed binding."); f.Fire("Reset"); f.CoolingValue("WaterLeakRate", .01f); f.Fire("Hoses");
                        float total = 104 + delta; Require(f.Reader.ActiveStateName == (total < 104 ? "State 5" : "Block damage")
                            && Math.Abs(f.CoolingValue("WaterLeakRate") - (.01f + (total < 104 ? .2f / Mathf.Clamp(total, 1, 104) : 0))) < .0000001f, "Native combined clamp calculation diverged: state=" + f.Reader.ActiveStateName + " total=" + f.CoolingValue("TightnessTotal") + " leak=" + f.CoolingValue("WaterLeakRate") + " math=" + f.CoolingValue("Math1") + " clamps=" + f.CoolingValue("Tightness1") + "/" + f.CoolingValue("Tightness2") + "/" + f.CoolingValue("Tightness3") + "/" + f.CoolingValue("Tightness4") + "/" + f.CoolingValue("Tightness5")); saved();
                    });
                }
                check("coolant hose inputs: carburettor removal clears its clamp but retains hoses", () =>
                { f.SetCoolantHoses(15, null, 0, false); f.Prepare(); f.Fire("Reset"); f.Fire("Hoses"); Require(f.CoolingValue("TightnessTotal") == 64 && Near(f.CoolingValue("WaterLeakRate"), .2f / 64), "Carburettor removal cleared hoses or retained its clamp: total=" + f.CoolingValue("TightnessTotal") + " leak=" + f.CoolingValue("WaterLeakRate")); saved(); });
                check("coolant hose inputs: missing hose rejects stale and conflicting refits", () =>
                {
                    f.SetCoolantHoses(); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetCoolantHoses(13); Call(f.Sync, "OnEngineBlockState", old); old.Revision++; Call(f.Sync, "OnEngineBlockState", old);
                    f.Prepare(); f.Fire("Bottom hose"); Require(f.Reader.ActiveStateName == "Empty coolant", "Stale packet refitted removed hose."); f.SetCoolantHoses(); f.Prepare(); saved();
                });
                foreach (string fieldName in new[] { "gameObject", "fsmName", "variableName", "storeValue", "everyFrame" })
                {
                    string field = fieldName;
                    check("coolant hose inputs: foreign " + field + " pauses and repairs cooling", () =>
                    {
                        var before = Get(read, field); object change = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "Tightness1", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(read, field, change); try { f.Fire("Hoses"); Require(!f.Reader.enabled, "Foreign hose signature escaped native guard."); saved(); }
                        finally { Set(read, field, field == "gameObject" ? original : before); if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Repaired hose consumer stayed paused.");
                    });
                }
                check("coolant hose inputs: moved fixed mount pauses and recovers", () =>
                {
                    var mount = f.SavedCoolantHoses[0]!.transform; var parent = mount.parent; mount.parent = f.Extras.transform;
                    try { f.Fire("Hoses"); Require(!f.Reader.enabled, "Wrong hose path supplied clamps."); saved(); }
                    finally { mount.parent = parent; if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Restored hose failed recovery.");
                });
                check("coolant hose inputs: destroyed proxy restores accepted clamps", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(read)); Require(f.Prepare(), "Destroyed hose proxy failed repair."); read.OnEnter(); Require(f.CoolingValue("Tightness1") == 16, "Repair lost host hose input."); saved(); });
                check("coolant hose inputs: disconnect restores owners and leaves saved assemblies protected", () =>
                { Call(f.Sync, "ReleaseSession"); Require(ReferenceEquals(Get(read, "gameObject"), original) && ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnect retained hose state or lost native owner."); saved(); });
            }
            RunCoolantHoseCapture(check);
            RunHeaterHoses(check);
        }
    }
}
