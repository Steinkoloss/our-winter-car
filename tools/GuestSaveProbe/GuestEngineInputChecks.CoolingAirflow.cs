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
        private static readonly string[] AirflowNames = { "Grille", "GrilleBlockoff", "Hood", "FiberglassHood" };
        private static readonly string[] AirflowIds = { "VIN4139", "BLOCKOFF0", "VIN4119", "HOODa09" };
        private static readonly string[] AirflowTargets = { "db_Grille", "db_GrilleBlockoff", "db_Hood", "db_Hood2" };
        private static readonly float[] SavedAirflow = { 25, 0, 900, 700 };
        private static string AirflowPath(int i) => "CORRIS/" + (i == 1 || i == 3 ? "AssembliesTuning/" : "Assemblies/") + "VINP_" + AirflowNames[i];
        private static readonly string[][] AirflowStates = {
            new[] { "Idle", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Install 1", "Assemble 2", "Set joint 2", "Update 2", "Repair 4", "State 1", "Repair 2", "Repair 3", "Repair 5", "Remove other" },
            new[] { "Idle", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Install 1", "Update 2", "State 1", "State 2", "State 3" },
            new[] { "Idle", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Install 1", "Assemble 2", "Set joint 2", "Update 2", "Repair" },
            new[] { "Idle", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Install 1", "Assemble 2", "Set joint 2", "Update 2" }
        };
        private sealed partial class Fixture
        {
            internal readonly PlayMakerFSM?[] SavedAirflowMounts = new PlayMakerFSM?[4];
            internal readonly PlayMakerFSM[] SavedAirflowParts = new PlayMakerFSM[4];
            private PlayMakerFSM EnsureSavedAirflow(byte index)
            {
                if (SavedAirflowMounts[index] != null) return SavedAirflowMounts[index]!;
                var data = Empty(MountObject(AirflowPath(index)), "Data"); var states = new List<FsmState>();
                foreach (string name in AirflowStates[index]) states.Add(new FsmState(data.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                data.Fsm.States = states.ToArray(); data.Fsm.StartState = "Idle";
                data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                data.FsmVariables.FloatVariables = index == 1 ? new FsmFloat[0] : new[] { Scalar("CoolingAirRateModifier", SavedAirflow[index]) };
                var part = Data(Child(data.gameObject, "saved airflow part"), AirflowIds[index], 1);
                part.FsmVariables.FloatVariables = index == 1 ? new FsmFloat[0] : index == 0 ? new[] { Scalar("Tightness", 16) }
                    : new[] { Scalar("Tightness", 16), Scalar("CoolingAirRateModifier", SavedAirflow[index]) };
                data.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", part.gameObject) };
                SavedAirflowMounts[index] = data; SavedAirflowParts[index] = part; return data;
            }
            private void AssertSavedAirflow()
            {
                for (int i = 0; i < 4; i++)
                {
                    var data = SavedAirflowMounts[i]; if (data == null) continue; var part = SavedAirflowParts[i];
                    Require(data.FsmVariables.FindFsmBool("Installed").Value && part.FsmVariables.FindFsmString("ID").Value == AirflowIds[i]
                        && part.FsmVariables.FindFsmInt("AssemblyID").Value == 1 && part.transform.parent == data.transform
                        && data.FsmVariables.FindFsmGameObject("ActivePart").Value == part.gameObject, "Saved airflow assembly changed.");
                    if (i != 1) Require(data.FsmVariables.FindFsmFloat("CoolingAirRateModifier").Value == SavedAirflow[i]
                        && part.FsmVariables.FindFsmFloat("Tightness").Value == 16, "Saved airflow scalar changed.");
                    if (i >= 2) Require(part.FsmVariables.FindFsmFloat("CoolingAirRateModifier").Value == SavedAirflow[i], "Saved bonnet modifier changed.");
                }
            }
            internal void SetAirflow(byte mask = 15, float grille = 100, float hood = 900, float fiberglass = 700)
            {
                ReceiveIntake(new EngineBlockState { CoolingAirflowFlags = mask, GrilleAirflow = (mask & 1) == 0 ? 0 : grille,
                    HoodAirflow = (mask & 4) == 0 ? 0 : hood, FiberglassHoodAirflow = (mask & 8) == 0 ? 0 : fiberglass });
            }
            internal PlayMakerFSM MakeNativeAirflow(byte index)
            {
                var obj = EnsureSavedAirflow(index).gameObject; UnityEngine.Object.DestroyImmediate(SavedAirflowMounts[index]);
                var row = FindNativeRow(AirflowPath(index), "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true;
                if (index != 1) data.FsmVariables.FindFsmFloat("CoolingAirRateModifier").Value = SavedAirflow[index];
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedAirflowParts[index].gameObject;
                data.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = obj;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    // Native installation/removal booleans, bonnet modifier copying
                    // and part parenting execute; joints, meshes and global assembly events are outside this fixture.
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Install 2" && i == (index == 0 ? 5 : index == 1 ? 0 : 3)
                        || state.Name == "Install 2" && index >= 2 && i == 2
                        || state.Name == "Installed" && i == (index == 1 ? 0 : 1)
                        || state.Name == "Remove part" && (i >= (index == 0 || index == 2 ? 9 : index == 1 ? 11 : 8) && i <= (index == 0 || index == 2 ? 12 : index == 1 ? 14 : 11))
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                SavedAirflowMounts[index] = data; Reader.FsmVariables.FindFsmGameObject(AirflowTargets[index]).Value = obj; return data;
            }
            internal void ConfigureAirflowDecisions()
            {
                var reset = NativeBagPartChecks.State(Reader, "Reset"); reset.Actions[3] = Import("Reset", 3); reset.Actions[3].Init(reset); RestoreNativeTransitions("Reset");
                foreach (string name in new[] { "Grille", "Grille and Cover", "Hood", "Hood installed", "Hood installed 2" })
                {
                    var state = NativeBagPartChecks.State(Reader, name);
                    for (int i = 0; i < state.Actions.Length; i++)
                    {
                        if (name == "Grille" && i == 0 || name == "Grille and Cover" && (i == 0 || i == 2) || name == "Hood" && (i == 0 || i == 2)
                            || (name == "Hood installed" || name == "Hood installed 2") && i == 0) continue;
                        state.Actions[i] = Import(name, i); state.Actions[i].Init(state);
                    }
                    RestoreNativeTransitions(name);
                }
                NativeBagPartChecks.State(Reader, "Bottom hose").Transitions = new FsmTransition[0];
            }
        }
        internal static void RunCoolingAirflow(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN126", "Cooling", "cooling-airflow-input-probe.json"))
            {
                var read = f.Action("Grille and Cover", 0); var original = (FsmOwnerDefault)Get(read, "gameObject");
                Action saved = () => { f.AssertSaved(); foreach (var mount in f.SavedAirflowMounts) Require(mount != null && !mount.enabled, "Saved airflow mount resumed."); };
                check("cooling airflow inputs: warm saved modifiers become absent without shared-owner mutation", () =>
                {
                    read.OnEnter(); Require(f.CoolingValue("Data") == 25, "Saved grille did not warm native cache.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Airflow projection failed.");
                    read.OnEnter(); f.Action("Grille", 0).OnEnter(); Require(f.CoolingValue("Data") == 0 && !f.Reader.FsmVariables.FindFsmBool("Installed1").Value
                        && original.GameObject.Value == f.SavedAirflowMounts[0]!.gameObject && f.Target(read) != original.GameObject.Value
                        && !f.Target(read).GetComponent<PlayMakerFSM>().enabled && f.Target(read).GetComponent<Rigidbody>() == null, "Absent airflow borrowed saved data or rewired shared owner."); saved();
                });
                check("cooling airflow inputs: arrival preserves scratch and state until entry-only reads", () =>
                {
                    string state = f.Reader.ActiveStateName; f.SetAirflow(); Require(f.Prepare() && f.Reader.ActiveStateName == state && f.CoolingValue("Data") == 0, "Airflow arrival replayed native math.");
                    read.OnEnter(); Require(!(bool)Get(read, "everyFrame") && f.CoolingValue("Data") == 100, "Host grille modifier was lost."); saved();
                });
                f.ConfigureAirflowDecisions();
                for (byte m = 0; m < 16; m++)
                {
                    byte mask = m;
                    check("cooling airflow inputs: native grille-cover and bonnet priority mask " + mask, () =>
                    {
                        f.SetAirflow(mask); Require(f.Prepare(), "Airflow mask failed binding."); f.Fire("Reset");
                        float expected = 2900 + ((mask & 1) != 0 ? 100 + ((mask & 2) != 0 ? 4000 : 0) : 0) + ((mask & 4) != 0 ? 900 : (mask & 8) != 0 ? 700 : 0);
                        Require(f.Reader.ActiveStateName == "Bottom hose" && f.CoolingValue("CoolingAirRateModifier") == expected,
                            "Native airflow diverged: state=" + f.Reader.ActiveStateName + " actual=" + f.CoolingValue("CoolingAirRateModifier") + " expected=" + expected); saved();
                    });
                }
                foreach (float v in new[] { -40f, 0, 900.5f })
                {
                    float value = v;
                    check("cooling airflow inputs: native modifiers retain range and reset accumulator " + value, () =>
                    {
                        f.SetAirflow(15, value, value, value + 7); f.Prepare(); f.Fire("Reset"); Require(f.CoolingValue("CoolingAirRateModifier") == 6900 + 2 * value, "Stock priority or finite range changed.");
                        f.SetAirflow(11, value, 0, value + 7); f.Prepare(); f.Fire("Reset"); Require(f.CoolingValue("CoolingAirRateModifier") == 6907 + 2 * value, "Fiberglass modifier or normal reset changed."); saved();
                    });
                }
                check("cooling airflow inputs: removed cover and bonnets reject stale and conflicting refits", () =>
                {
                    f.SetAirflow(); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetAirflow(1); Call(f.Sync, "OnEngineBlockState", old); old.Revision++; Call(f.Sync, "OnEngineBlockState", old);
                    f.Prepare(); f.Fire("Reset"); Require(f.CoolingValue("CoolingAirRateModifier") == 3000, "Old airflow refitted removed cover or bonnet."); saved();
                });
                foreach (string fieldName in new[] { "gameObject", "fsmName", "variableName", "storeValue", "everyFrame" })
                {
                    string field = fieldName;
                    check("cooling airflow inputs: foreign " + field + " pauses and repairs cooling", () =>
                    {
                        var before = Get(read, field); object change = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "Data", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(read, field, change); try { f.Fire("Grille and Cover"); Require(!f.Reader.enabled, "Foreign airflow signature escaped guard."); saved(); }
                        finally { Set(read, field, field == "gameObject" ? original : before); if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Repaired airflow consumer stayed paused.");
                    });
                }
                check("cooling airflow inputs: moved fixed mount pauses and recovers", () =>
                {
                    var mount = f.SavedAirflowMounts[0]!.transform; var parent = mount.parent; mount.parent = f.Extras.transform;
                    try { f.Fire("Grille"); Require(!f.Reader.enabled, "Wrong grille path supplied airflow."); saved(); }
                    finally { mount.parent = parent; if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Restored airflow mount failed recovery.");
                });
                check("cooling airflow inputs: destroyed proxy restores accepted modifier", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(read)); Require(f.Prepare(), "Destroyed airflow proxy failed repair."); read.OnEnter(); Require(f.CoolingValue("Data") == 100, "Repair lost host airflow."); saved(); });
                check("cooling airflow inputs: disconnect restores owners and keeps saved mounts protected", () =>
                { Call(f.Sync, "ReleaseSession"); Require(ReferenceEquals(Get(read, "gameObject"), original) && ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnect retained airflow or lost owner."); saved(); });
            }
            RunCoolingAirflowCapture(check);
        }
    }
}
