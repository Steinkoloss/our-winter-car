using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal PlayMakerFSM? SavedAirCleaner;
            internal PlayMakerFSM SavedAirCleanerPart = null!;
            private bool _airCleanerSeeded;
            private PlayMakerFSM EnsureSavedAirCleaner()
            {
                if (SavedAirCleaner != null) return SavedAirCleaner;
                SavedAirCleaner = Empty(MountObject("CARPARTS/StartParts/VIN1110/VINP_AirCleaner"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                    states.Add(new FsmState(SavedAirCleaner.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedAirCleaner.Fsm.States = states.ToArray(); SavedAirCleaner.Fsm.StartState = "Idle";
                SavedAirCleaner.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                SavedAirCleaner.FsmVariables.FloatVariables = new[] { Scalar("Wear", 77), Scalar("DataPower", 103), Scalar("DataTorque", 253), Scalar("DataPowerAdd", -.03f) };
                SavedAirCleanerPart = Data(Child(SavedAirCleaner.gameObject, "saved air cleaner"), "VIN1359", 1);
                var floats = new List<FsmFloat>(SavedAirCleanerPart.FsmVariables.FloatVariables);
                floats.Add(Scalar("DataPower", 103)); floats.Add(Scalar("DataTorque", 253)); floats.Add(Scalar("DataPowerAdd", -.03f));
                SavedAirCleanerPart.FsmVariables.FloatVariables = floats.ToArray(); SavedAirCleanerPart.FsmVariables.FindFsmFloat("Wear").Value = 77;
                SavedAirCleaner.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedAirCleanerPart.gameObject) }; return SavedAirCleaner;
            }
            private void SeedAirCleaner()
            {
                var state = ((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Get() ?? new EngineBlockState { Flags = 11, Wear = 90 };
                state.Flags |= 43; state.AirCleanerPower = 100; state.AirCleanerTorque = 250; state.AirCleanerPowerAdd = -.04f; ReceiveIntake(state);
            }
            internal void ReceiveIntake(EngineBlockState state)
            {
                state.Revision = ++_engineBlockRevision; SeedRequiredAmbient(state); Call(Sync, "OnEngineBlockState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Get()?.Revision == state.Revision, "Fixture intake state rejected.");
            }
            internal void SetIntake(bool carburettor, bool filter)
            {
                ReceiveIntake(new EngineBlockState { Flags = (byte)(11 | (carburettor ? 16 : 0) | (filter ? 32 : 0)), Wear = 90,
                    FuelChamber = carburettor ? 25 : 0, CarbReserve = carburettor ? .1f : 0, SettingMixture = carburettor ? 14.5f : 0,
                    CarburettorPower = carburettor ? 140 : 0, CarburettorTorque = carburettor ? 210 : 0, CarburettorPowerAdd = carburettor ? .07f : 0,
                    AirCleanerPower = filter ? 20 : 0, AirCleanerTorque = filter ? 40 : 0, AirCleanerPowerAdd = filter ? -.03f : 0 });
            }
            private void AssertSavedAirCleaner()
            {
                if (SavedAirCleaner == null) return;
                foreach (var data in new[] { SavedAirCleaner, SavedAirCleanerPart })
                {
                    var vars = data.FsmVariables;
                    Require(vars.FindFsmFloat("Wear").Value == 77 && vars.FindFsmFloat("DataPower").Value == 103
                        && vars.FindFsmFloat("DataTorque").Value == 253 && vars.FindFsmFloat("DataPowerAdd").Value == -.03f, "Guest saved air-cleaner condition or performance changed.");
                }
                Require(SavedAirCleaner.FsmVariables.FindFsmBool("Installed").Value && SavedAirCleanerPart.FsmVariables.FindFsmString("ID").Value == "VIN1359"
                    && SavedAirCleanerPart.FsmVariables.FindFsmInt("AssemblyID").Value == 1 && SavedAirCleanerPart.transform.parent == SavedAirCleaner.transform
                    && SavedAirCleaner.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedAirCleanerPart.gameObject, "Guest saved air-cleaner identity or assembly changed.");
            }
            internal void ConfigureIntakeCalculation(PlayMakerFSM valves)
            {
                var gate = NativeBagPartChecks.State(Reader, "Airfilter"); gate.Actions[3] = Import(gate.Name, 3); gate.Actions[3].Init(gate); RestoreNativeTransitions(gate.Name);
                var row = FindNativeRow("CORRIS/Simulation/Engine/Valves", "Valves");
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    string name = (string)stateRow["name"]; if (name != "Carburettor" && name != "AirFilter") continue;
                    var state = NativeBagPartChecks.State(valves, name); var raw = (List<object>)stateRow["actions"];
                    for (int i = 3; i < 6; i++) { state.Actions[i] = NativeAction((Dictionary<string, object>)raw[i], valves); state.Actions[i].Init(state); }
                    state.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = name == "Carburettor" ? "AirFilter" : "Headers" } };
                }
            }
            internal PlayMakerFSM MakeNativeAirCleaner()
            {
                var obj = EnsureSavedAirCleaner().gameObject; UnityEngine.Object.DestroyImmediate(SavedAirCleaner);
                var row = FindNativeRow("CARPARTS/StartParts/VIN1110/VINP_AirCleaner", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                data.FsmVariables.FindFsmFloat("DataPower").Value = 103; data.FsmVariables.FindFsmFloat("DataTorque").Value = 253; data.FsmVariables.FindFsmFloat("DataPowerAdd").Value = -.03f;
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedAirCleanerPart.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Update 2" && i == 0
                        || state.Name == "Remove part" && (i == 1 || i == 2 || i == 3 || i == 7 || i == 14 || i == 15)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                SavedAirCleaner = data; Reader.FsmVariables.FindFsmGameObject("db_Airfilter").Value = obj; return data;
            }
        }
        internal static void RunIntake(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN125", "FuelLine", "intake-input-probe.json"))
            {
                var valves = f.AddConsumer("Valves"); f.ConfigureIntakeCalculation(valves); f.Receive(90, 0, 0, true);
                var filter = f.Action("Airfilter", 1); var carb = NativeBagPartChecks.State(valves, "Carburettor"); var air = NativeBagPartChecks.State(valves, "AirFilter");
                var reads = new[] { filter, carb.Actions[0], carb.Actions[1], carb.Actions[2], air.Actions[0], air.Actions[1], air.Actions[2] };
                var originals = new object[reads.Length]; for (int i = 0; i < reads.Length; i++) originals[i] = Get(reads[i], "gameObject");
                Action saved = () => { f.AssertSaved(); Require(!f.SavedAirCleaner!.enabled && !f.SavedCarburettor!.enabled, "Saved intake graphs resumed."); };
                check("intake inputs: native saved caches are replaced with absent host inputs", () =>
                {
                    filter.OnEnter(); carb.Actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 101, "Saved carburettor cache did not warm.");
                    air.Actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 103, "Saved filter cache did not warm.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Absent intake failed preparation.");
                    foreach (var read in reads) read.OnEnter(); Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value && valves.FsmVariables.FindFsmFloat("PartPower").Value == 0
                        && valves.FsmVariables.FindFsmFloat("PartTorque").Value == 0 && valves.FsmVariables.FindFsmFloat("PartPowerAdd").Value == 0, "Missing host intake borrowed saved values."); saved();
                });
                check("intake inputs: packet arrival preserves shared calculation scratch and native state", () =>
                {
                    string state = valves.ActiveStateName; f.SetIntake(true, true); Require(f.Prepare(), "Host intake failed to bind.");
                    Require(valves.ActiveStateName == state && valves.FsmVariables.FindFsmFloat("PartPower").Value == 0, "Arrival replayed intake calculations.");
                    carb.Actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 140, "Carburettor missed shared scratch.");
                    f.Prepare(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 140, "Preparation overwrote current calculation.");
                    air.Actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 20 && f.Target(filter).GetComponent<Rigidbody>() == null, "Filter source did not own its native read."); saved();
                });
                foreach (bool carburettor in new[] { false, true }) foreach (bool filterInstalled in new[] { false, true })
                {
                    bool c = carburettor, a = filterInstalled;
                    check("intake inputs: native power sums carburettor=" + c + " filter=" + a, () =>
                    {
                        f.SetIntake(c, a); Require(f.Prepare(), "Intake changes failed preparation.");
                        valves.FsmVariables.FindFsmFloat("MaxPower").Value = 500; valves.FsmVariables.FindFsmFloat("MaxTorque").Value = 600; valves.FsmVariables.FindFsmFloat("PowerAdd").Value = 2;
                        NativeBagPartChecks.Fire(valves, "Carburettor");
                        Require(valves.ActiveStateName == "Headers" && Near(valves.FsmVariables.FindFsmFloat("MaxPower").Value, 500 + (c ? 140 : 0) + (a ? 20 : 0))
                            && Near(valves.FsmVariables.FindFsmFloat("MaxTorque").Value, 600 + (c ? 210 : 0) + (a ? 40 : 0))
                            && Near(valves.FsmVariables.FindFsmFloat("PowerAdd").Value, 2 + (c ? .07f : 0) - (a ? .03f : 0)), "Native intake power/torque additions diverged."); saved();
                    });
                    check("intake inputs: native filtration branch carburettor=" + c + " filter=" + a, () =>
                    {
                        f.SetIntake(c, a); Require(f.Prepare(), "Filter installation failed preparation."); f.Fire("Airfilter");
                        Require(f.Reader.ActiveStateName == (a ? "Starting" : "Dirt accumulation"), "Filtration used the guest's installed filter."); saved();
                    });
                }
                check("intake inputs: stale and conflicting performance cannot restore removed parts", () =>
                {
                    f.SetIntake(true, true); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetIntake(false, false);
                    Call(f.Sync, "OnEngineBlockState", old); old.Revision++; Call(f.Sync, "OnEngineBlockState", old); f.Prepare();
                    foreach (var read in reads) read.OnEnter(); Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value && valves.FsmVariables.FindFsmFloat("PartPower").Value == 0, "Old intake restored a removed part."); saved();
                });
                check("intake inputs: renamed moved head preserves both protected mounts", () =>
                {
                    f.SetIntake(true, true); var head = f.SavedAirCleaner!.transform.parent; var parent = head.parent; string name = head.name;
                    head.parent = f.Car.transform; head.name = "relocated intake head";
                    try { Require(f.Prepare(), "Moved head invalidated intake readers."); filter.OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Moved head lost host filtration."); saved(); }
                    finally { head.parent = parent; head.name = name; }
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName; var read = air.Actions[0];
                    check("intake inputs: foreign filter " + field + " is contained and repairs", () =>
                    {
                        var original = Get(read, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "PartPower", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(read, field, changed);
                        try { NativeBagPartChecks.Fire(valves, "AirFilter"); Require(!valves.enabled, "Foreign intake signature escaped entry guard."); saved(); }
                        finally { Set(read, field, field == "gameObject" ? originals[4] : original); if (!f.Prepare()) f.Prepare(); }
                        Require(valves.enabled, "Repaired intake reader stayed paused.");
                    });
                }
                check("intake inputs: invalid head identity stays protected and recovers", () =>
                {
                    var identity = f.SavedAirCleaner!.transform.parent.GetComponent<PlayMakerFSM>().FsmVariables.FindFsmString("ID"); string original = identity.Value; identity.Value = "VIN11101";
                    try { f.Fire("Airfilter"); Require(!f.Reader.enabled, "Invalid saved head supplied filtration."); saved(); }
                    finally { identity.Value = original; if (!f.Prepare()) f.Prepare(); }
                    Require(f.Reader.enabled && valves.enabled, "Restored head identity failed recovery.");
                });
                check("intake inputs: destroyed power proxy recovers from accepted host state", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(air.Actions[0])); Require(f.Prepare(), "Intake proxy failed repair."); air.Actions[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 20, "Repair lost host filter power."); saved(); });
                check("intake inputs: disconnect restores all seven native owners and retains protection", () =>
                {
                    Call(f.Sync, "ReleaseSession"); Require(((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnect retained host intake.");
                    for (int i = 0; i < reads.Length; i++) Require(ReferenceEquals(Get(reads[i], "gameObject"), originals[i]), "Disconnect lost native intake owner."); saved();
                });
            }
            RunIntakeCapture(check);
        }
    }
}
