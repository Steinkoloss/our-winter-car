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
            internal PlayMakerFSM? SavedOilpan;
            internal PlayMakerFSM SavedOilpanPart = null!;
            private PlayMakerFSM EnsureSavedOilpan()
            {
                if (SavedOilpan != null) return SavedOilpan;
                SavedOilpan = Empty(MountObject("CARPARTS/StartParts/VIN1010/VINP_Oilpan"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                    states.Add(new FsmState(SavedOilpan.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedOilpan.Fsm.States = states.ToArray(); SavedOilpan.Fsm.StartState = "Idle";
                SavedOilpan.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                SavedOilpan.FsmVariables.FloatVariables = new[] { Scalar("Wear", 77), Scalar("Tightness", 72), Scalar("Oil", 3.2f), Scalar("OilContamination", .8f), Scalar("OilViscosity", 12) };
                SavedOilpanPart = Data(Child(SavedOilpan.gameObject, "saved oil pan"), "VIN1069", 1);
                var floats = new List<FsmFloat>(SavedOilpanPart.FsmVariables.FloatVariables);
                floats.Add(Scalar("OilLevel", 3.2f)); floats.Add(Scalar("OilDirt", .8f)); floats.Add(Scalar("OilViscosity", 12));
                SavedOilpanPart.FsmVariables.FloatVariables = floats.ToArray(); SavedOilpanPart.FsmVariables.FindFsmFloat("Wear").Value = 77;
                SavedOilpanPart.FsmVariables.FindFsmFloat("Tightness").Value = 72;
                SavedOilpan.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedOilpanPart.gameObject) }; return SavedOilpan;
            }
            internal void SetOilpan(bool installed = true, float wear = 90, float tightness = 60, float oil = 2.5f, float contamination = 4, float viscosity = .6f)
            {
                ReceiveIntake(new EngineBlockState { Flags = 3, Wear = 90, OilpanInstalled = installed,
                    OilpanWear = installed ? wear : 0, OilpanTightness = installed ? tightness : 0, Oil = installed ? oil : 0,
                    OilContamination = installed ? contamination : 0, OilViscosity = installed ? viscosity : 0 });
            }
            private void AssertSavedOilpan()
            {
                if (SavedOilpan == null) return;
                foreach (var data in new[] { SavedOilpan, SavedOilpanPart })
                {
                    bool part = data == SavedOilpanPart; var v = data.FsmVariables;
                    Require(v.FindFsmFloat("Wear").Value == 77 && v.FindFsmFloat("Tightness").Value == 72 && v.FindFsmFloat(part ? "OilLevel" : "Oil").Value == 3.2f
                        && v.FindFsmFloat(part ? "OilDirt" : "OilContamination").Value == .8f && v.FindFsmFloat("OilViscosity").Value == 12, "Saved oilpan scalars changed.");
                }
                Require(SavedOilpan.FsmVariables.FindFsmBool("Installed").Value && SavedOilpanPart.FsmVariables.FindFsmString("ID").Value == "VIN1069"
                    && SavedOilpanPart.FsmVariables.FindFsmInt("AssemblyID").Value == 1 && SavedOilpanPart.transform.parent == SavedOilpan.transform
                    && SavedOilpan.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedOilpanPart.gameObject, "Saved oilpan assembly changed.");
            }
            internal PlayMakerFSM MakeNativeOilpan()
            {
                var obj = EnsureSavedOilpan().gameObject; UnityEngine.Object.DestroyImmediate(SavedOilpan);
                var row = FindNativeRow("CARPARTS/StartParts/VIN1010/VINP_Oilpan", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                SavedOilpan = data; ResetSavedOilpan(); data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedOilpanPart.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    // Exercise native scalar copies and detachment without creating bodies,
                    // invoking global assembly notifications or performing save operations.
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Update 2" && i >= 1 && i <= 6
                        || state.Name == "Install 2" && i >= 1 && i <= 5
                        || state.Name == "Remove part" && (i == 1 || i >= 5 && i <= 11 || i >= 18 && i <= 20)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                Reader.FsmVariables.FindFsmGameObject("db_Oilpan").Value = obj; return data;
            }
            internal void ResetSavedOilpan()
            {
                var v = SavedOilpan!.FsmVariables; v.FindFsmBool("Installed").Value = true;
                foreach (var data in new[] { SavedOilpan, SavedOilpanPart })
                {
                    bool part = data == SavedOilpanPart; var vars = data.FsmVariables;
                    vars.FindFsmFloat("Wear").Value = 77; vars.FindFsmFloat("Tightness").Value = 72;
                    vars.FindFsmFloat(part ? "OilLevel" : "Oil").Value = 3.2f; vars.FindFsmFloat(part ? "OilDirt" : "OilContamination").Value = .8f;
                    vars.FindFsmFloat("OilViscosity").Value = 12;
                }
                SavedOilpanPart.transform.parent = SavedOilpan.transform;
            }
            internal void ImportOilpanCalculation(PlayMakerFSM fsm, string stateName, int first, int last)
            {
                var row = FindNativeRow(fsm == Reader ? "CORRIS/Simulation/Engine/Oil" : fsm.FsmName == "Cylinders" ? "CORRIS/Simulation/Engine/Combustion" : "CORRIS/Simulation/Engine/Oil", fsm.FsmName);
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    if ((string)stateRow["name"] != stateName) continue;
                    var state = NativeBagPartChecks.State(fsm, stateName); var raw = (List<object>)stateRow["actions"];
                    for (int i = first; i <= last; i++) { state.Actions[i] = NativeAction((Dictionary<string, object>)raw[i], fsm); state.Actions[i].Init(state); }
                    if (fsm == Reader && stateName == "Major damage?") RestoreNativeTransitions(stateName);
                }
            }
        }
        internal static void RunOilpan(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN132", "Oil", "oilpan-input-probe.json"))
            {
                var wearing = f.AddConsumer("Wearing"); var cylinders = f.AddConsumer("Cylinders");
                var reads = new[] { f.Action("Major damage?", 2), f.Action("Friction", 0), f.Action("Oilpan leak", 0), f.Action("Get oil", 0),
                    NativeBagPartChecks.State(wearing, "Oil level").Actions[0], NativeBagPartChecks.State(wearing, "Oil contamination").Actions[0], NativeBagPartChecks.State(cylinders, "Plug data").Actions[1] };
                var owners = new object[reads.Length]; for (int i = 0; i < reads.Length; i++) owners[i] = Get(reads[i], "gameObject");
                Action saved = () => { f.AssertSaved(); Require(!f.SavedOilpan!.enabled, "Saved oilpan Data resumed."); };
                Action<float[]> outputs = values => { for (int i = 0; i < reads.Length; i++) { reads[i].OnEnter(); Require(Near(((FsmFloat)Get(reads[i], "storeValue")).Value, values[i]), "Oilpan read " + i + " diverged."); } };
                check("oilpan inputs: all seven saved caches warm then use absent host values", () =>
                {
                    outputs(new[] { 77f, 12, 72, 3.2f, 3.2f, .8f, .8f }); f.ClearEngineBlockInputs();
                    Require(f.Prepare(), "Oilpan inputs did not bind."); outputs(new float[7]); saved();
                });
                check("oilpan inputs: host arrival preserves scratch until each native read", () =>
                {
                    string state = f.Reader.ActiveStateName; f.SetOilpan(); Require(f.Prepare(), "Host oilpan failed preparation.");
                    Require(f.Reader.ActiveStateName == state && f.Reader.FsmVariables.FindFsmFloat("Oil").Value == 0, "Arrival replayed oil calculation.");
                    outputs(new[] { 90f, .6f, 60, 2.5f, 2.5f, 4, 4 }); foreach (var read in reads) Require(f.Target(read) != f.SavedOilpan!.gameObject && f.Target(read).GetComponent<Rigidbody>() == null, "Oilpan proxy is not isolated."); saved();
                });
                f.ImportOilpanCalculation(f.Reader, "Major damage?", 1, 1); f.ImportOilpanCalculation(f.Reader, "Major damage?", 3, 3);
                foreach (float condition in new[] { .99f, 1f, 1.01f })
                {
                    float value = condition;
                    check("oilpan inputs: native starvation wear boundary " + value, () =>
                    { f.SetOilpan(wear: value); Require(f.Prepare(), "Oilpan wear failed."); f.Fire("Major damage?"); Require(f.Reader.ActiveStateName == (value < 1 ? "No oil" : "Valve Cover"), "Oilpan wear used saved condition."); saved(); });
                }
                f.ImportOilpanCalculation(f.Reader, "Oilpan leak", 1, 3);
                foreach (float tightness in new[] { -1000f, 0, 60, 80, 90 })
                {
                    float value = tightness;
                    check("oilpan inputs: native bolt leak calculation " + value, () =>
                    { f.SetOilpan(tightness: value); f.Prepare(); f.Fire("Oilpan leak"); Require(Near(f.Reader.FsmVariables.FindFsmFloat("LeakOilpan").Value, Mathf.Clamp01((80 - value) / 1000)), "Oilpan leak arithmetic diverged."); saved(); });
                }
                f.ImportOilpanCalculation(f.Reader, "Friction", 1, 2); f.ImportOilpanCalculation(wearing, "Oil level", 1, 8);
                f.ImportOilpanCalculation(wearing, "Oil contamination", 1, 3); f.ImportOilpanCalculation(cylinders, "Plug data", 2, 2);
                foreach (float quantity in new[] { 0f, .1f, 2.5f, 3.71f })
                {
                    float value = quantity;
                    check("oilpan inputs: native friction and wear use host oil quantity " + value, () =>
                    {
                        f.SetOilpan(oil: value); f.Prepare(); reads[3].OnEnter(); f.Fire("Friction");
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("LubricationFriction").Value, (3.71f - value) / 9), "Oil friction did not use host quantity.");
                        var v = wearing.FsmVariables; v.FindFsmFloat("Wear1Divider").Value = 100000; v.FindFsmFloat("Wear2Divider").Value = 200000;
                        foreach (string name in new[] { "WearHeavy1", "WearLight1", "WearHeavy2", "WearLight2" }) v.FindFsmFloat(name).Value = 0;
                        ((FsmFloat)Get(NativeBagPartChecks.State(wearing, "Oil level").Actions[2], "float2")).Value = 2000;
                        NativeBagPartChecks.Fire(wearing, "Oil level"); Require(Near(v.FindFsmFloat("Wear1").Value, (3.71f - value) * .02f) && Near(v.FindFsmFloat("Wear2").Value, (3.71f - value) * .01f), "Oil-dependent wear diverged."); saved();
                    });
                }
                foreach (float contamination in new[] { .01f, 4f, 100f })
                {
                    float value = contamination;
                    check("oilpan inputs: native wear and plug contamination use host dirt " + value, () =>
                    {
                        f.SetOilpan(contamination: value); f.Prepare(); var v = wearing.FsmVariables; v.FindFsmFloat("Wear1Divider").Value = 1100000; v.FindFsmFloat("Wear2Divider").Value = 1300000;
                        NativeBagPartChecks.Fire(wearing, "Oil contamination"); Require(Near(v.FindFsmFloat("Wear1Divider").Value, 1100000 - value * 10000) && Near(v.FindFsmFloat("Wear2Divider").Value, 1300000 - value * 10000), "Dirty oil did not change wear divisors.");
                        reads[6].OnEnter(); NativeBagPartChecks.State(cylinders, "Plug data").Actions[2].OnEnter(); Require(Near(cylinders.FsmVariables.FindFsmFloat("SparkPlugOilCont").Value, value / 10000), "Plug contamination scaling diverged."); saved();
                    });
                }
                check("oilpan inputs: stale conflicting and removed state cannot restore saved oil", () =>
                {
                    f.SetOilpan(); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetOilpan(false);
                    Call(f.Sync, "OnEngineBlockState", old); old.Revision++; Call(f.Sync, "OnEngineBlockState", old); f.Prepare(); outputs(new float[7]); saved();
                });
                check("oilpan inputs: renamed moving native block preserves protected oil source", () =>
                {
                    f.SetOilpan(); var block = f.SavedOilpan!.transform.parent; var parent = block.parent; string name = block.name;
                    block.parent = f.Car.transform; block.name = "moving oil assembly";
                    try { Require(f.Prepare(), "Moved oilpan failed binding."); outputs(new[] { 90f, .6f, 60, 2.5f, 2.5f, 4, 4 }); saved(); }
                    finally { block.parent = parent; block.name = name; }
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName; var read = reads[0];
                    check("oilpan inputs: foreign " + field + " pauses and repairs its consumer", () =>
                    {
                        var original = Get(read, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "Wear", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(read, field, changed); try { f.Fire("Major damage?"); Require(!f.Reader.enabled && wearing.enabled && cylinders.enabled, "Oilpan signature escaped scoped guard."); saved(); }
                        finally { Set(read, field, field == "gameObject" ? owners[0] : original); if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired oil reader stayed paused.");
                    });
                }
                check("oilpan inputs: destroyed proxy repairs all cached reads", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(reads[0])); Require(f.Prepare(), "Oilpan proxy did not repair."); outputs(new[] { 90f, .6f, 60, 2.5f, 2.5f, 4, 4 }); saved(); });
                check("oilpan inputs: disconnect restores seven native owners and preserves saved data", () =>
                { Call(f.Sync, "ReleaseSession"); for (int i = 0; i < reads.Length; i++) Require(ReferenceEquals(Get(reads[i], "gameObject"), owners[i]), "Oilpan cleanup lost native owner."); saved(); });
            }
            RunOilpanCapture(check);
        }
    }
}
