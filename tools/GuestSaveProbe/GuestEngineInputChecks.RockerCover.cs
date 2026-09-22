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
            internal PlayMakerFSM? SavedRockerCover;
            internal PlayMakerFSM SavedRockerCoverPart = null!;
            internal GameObject SavedRockerCoverCap = null!;
            private PlayMakerFSM EnsureSavedRockerCover()
            {
                if (SavedRockerCover != null) return SavedRockerCover;
                SavedRockerCover = Empty(MountObject("CARPARTS/StartParts/VIN1110/VINP_RockerCover"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                    states.Add(new FsmState(SavedRockerCover.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedRockerCover.Fsm.States = states.ToArray(); SavedRockerCover.Fsm.StartState = "Idle";
                SavedRockerCover.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                SavedRockerCover.FsmVariables.FloatVariables = new[] { Scalar("Wear", 77), Scalar("Tightness", 64) };
                SavedRockerCoverPart = Data(Child(SavedRockerCover.gameObject, "saved rocker cover"), "VIN1189", 1);
                SavedRockerCoverPart.FsmVariables.FindFsmFloat("Wear").Value = 77; SavedRockerCoverPart.FsmVariables.FindFsmFloat("Tightness").Value = 64;
                SavedRockerCoverCap = Child(SavedRockerCover.gameObject, "saved oil cap");
                SavedRockerCover.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedRockerCoverPart.gameObject), ObjectVar("OilCap", SavedRockerCoverCap) };
                return SavedRockerCover;
            }
            internal void SetRockerCover(bool installed, float tightness = 48)
            { ReceiveIntake(new EngineBlockState { Flags = 11, Wear = 90, RockerCoverInstalled = installed, RockerCoverTightness = installed ? tightness : 0 }); }
            private void AssertSavedRockerCover()
            {
                if (SavedRockerCover == null) return;
                foreach (var data in new[] { SavedRockerCover, SavedRockerCoverPart })
                    Require(data.FsmVariables.FindFsmFloat("Wear").Value == 77 && data.FsmVariables.FindFsmFloat("Tightness").Value == 64, "Saved cover wear or tightness changed.");
                Require(SavedRockerCover.FsmVariables.FindFsmBool("Installed").Value && SavedRockerCoverPart.FsmVariables.FindFsmString("ID").Value == "VIN1189"
                    && SavedRockerCoverPart.FsmVariables.FindFsmInt("AssemblyID").Value == 1 && SavedRockerCoverPart.transform.parent == SavedRockerCover.transform
                    && SavedRockerCover.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedRockerCoverPart.gameObject && SavedRockerCoverCap.activeSelf, "Saved cover assembly or cap changed.");
            }
            internal PlayMakerFSM MakeNativeRockerCover()
            {
                var obj = EnsureSavedRockerCover().gameObject; UnityEngine.Object.DestroyImmediate(SavedRockerCover);
                var row = FindNativeRow("CARPARTS/StartParts/VIN1110/VINP_RockerCover", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmFloat("Wear").Value = 77; data.FsmVariables.FindFsmFloat("Tightness").Value = 64;
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedRockerCoverPart.gameObject; data.FsmVariables.FindFsmGameObject("OilCap").Value = SavedRockerCoverCap;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    // Keep native disabled wear disabled. Cover removal exercises wear,
                    // cap visibility and detachment without body creation or global events.
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Update 2" && i == 0
                        || state.Name == "Install 2" && i >= 1 && i <= 3
                        || state.Name == "Remove part" && (i == 0 || i == 1 || i == 6 || i >= 13 && i <= 15)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                SavedRockerCover = data; Reader.FsmVariables.FindFsmGameObject("db_Rockercover1").Value = obj; return data;
            }
            internal void ConfigureRockerCoverLeak()
            {
                var state = NativeBagPartChecks.State(Reader, "Valve Cover");
                for (int i = 1; i <= 2; i++) { state.Actions[i] = Import(state.Name, i); state.Actions[i].Init(state); }
            }
        }
        internal static void RunRockerCover(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN132", "Oil", "rocker-cover-input-probe.json"))
            {
                var read = f.Action("Valve Cover", 0); var original = (FsmOwnerDefault)Get(read, "gameObject");
                Action saved = () => { f.AssertSaved(); Require(!f.SavedRockerCover!.enabled, "Saved cover Data resumed."); };
                check("rocker cover inputs: warm saved tightness becomes absent host input", () =>
                {
                    read.OnEnter(); Require(f.Tightness == 64, "Saved cover cache did not warm."); f.ClearEngineBlockInputs();
                    Require(f.Prepare(), "Cover did not bind."); read.OnEnter(); Require(f.Tightness == 0 && f.Target(read) != original.GameObject.Value && !f.Target(read).GetComponent<PlayMakerFSM>().enabled
                        && f.Target(read).GetComponent<Rigidbody>() == null && original.GameObject.Value == f.SavedRockerCover!.gameObject, "Absent cover borrowed saved tightness or changed its target."); saved();
                });
                check("rocker cover inputs: arrival leaves native scratch and state until read", () =>
                {
                    string state = f.Reader.ActiveStateName; f.SetRockerCover(true); Require(f.Prepare() && f.Tightness == 0 && f.Reader.ActiveStateName == state, "Arrival replayed cover calculation.");
                    read.OnEnter(); Require(f.Tightness == 48, "Host cover tightness not read."); saved();
                });
                f.ConfigureRockerCoverLeak();
                foreach (float tightness in new[] { -8f, 0, 8, 48, 63.99f, 64, 64.01f, 72 })
                {
                    float value = tightness;
                    check("rocker cover inputs: native oil leak uses host tightness " + value, () =>
                    {
                        f.SetRockerCover(true, value); Require(f.Prepare(), "Cover change failed binding."); f.Fire("Valve Cover");
                        Require(Math.Abs(f.Reader.FsmVariables.FindFsmFloat("LeakRockerCover").Value - (64 - value) / 60000) < .00000001f, "Native cover leak was clamped or used saved bolts."); saved();
                    });
                }
                check("rocker cover inputs: removing the cover preserves the independent oilpan source", () =>
                {
                    f.SetOilpan(); var state = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; state.Flags |= 8; state.RockerCoverInstalled = true; state.RockerCoverTightness = 48;
                    f.ReceiveIntake(state); f.Prepare(); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!;
                    state.RockerCoverInstalled = false; state.RockerCoverTightness = 0; f.ReceiveIntake(state); Call(f.Sync, "OnEngineBlockState", old); old.Revision++; Call(f.Sync, "OnEngineBlockState", old);
                    Require(f.Prepare(), "Removed cover did not prepare."); f.Fire("Valve Cover"); Require(Near(f.Reader.FsmVariables.FindFsmFloat("LeakRockerCover").Value, 64f / 60000), "Removed cover retained host bolts.");
                    f.Action("Get oil", 0).OnEnter(); Require(f.Reader.FsmVariables.FindFsmFloat("Oil").Value == 2.5f, "Cover removal cleared independent oilpan."); saved();
                });
                check("rocker cover inputs: renamed moved head keeps its protected cover", () =>
                {
                    f.SetRockerCover(true); var head = f.SavedRockerCover!.transform.parent; var parent = head.parent; string name = head.name;
                    head.parent = f.Car.transform; head.name = "relocated covered head";
                    try { Require(f.Prepare(), "Moved cover lost binding."); read.OnEnter(); Require(f.Tightness == 48, "Moved cover lost host tightness."); saved(); }
                    finally { head.parent = parent; head.name = name; }
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName;
                    check("rocker cover inputs: foreign " + field + " pauses and repairs the consumer", () =>
                    {
                        var before = Get(read, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "Tightness", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(read, field, changed); try { f.Fire("Valve Cover"); Require(!f.Reader.enabled, "Foreign cover signature escaped native entry guard."); saved(); }
                        finally { Set(read, field, field == "gameObject" ? original : before); if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired cover reader stayed paused.");
                    });
                }
                check("rocker cover inputs: invalid saved head identity pauses and recovers", () =>
                {
                    var id = f.SavedRockerCover!.transform.parent.GetComponent<PlayMakerFSM>().FsmVariables.FindFsmString("ID"); string old = id.Value; id.Value = "VIN11101";
                    try { f.Fire("Valve Cover"); Require(!f.Reader.enabled, "Malformed head supplied cover input."); saved(); }
                    finally { id.Value = old; if (!f.Prepare()) f.Prepare(); } Require(f.Reader.enabled, "Restored head failed recovery.");
                });
                check("rocker cover inputs: destroyed proxy restores accepted host tightness", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(read)); Require(f.Prepare(), "Destroyed cover proxy failed repair."); read.OnEnter(); Require(f.Tightness == 48, "Repair lost host cover input."); saved(); });
                check("rocker cover inputs: disconnect restores native owner and keeps saved cover protected", () =>
                { Call(f.Sync, "ReleaseSession"); Require(ReferenceEquals(Get(read, "gameObject"), original) && ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnect retained host cover or lost owner."); saved(); });
            }
            RunRockerCoverCapture(check);
        }
    }
}
