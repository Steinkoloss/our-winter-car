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
        private static readonly string[] ExhaustStates = { "Headers", "Exhaust front", "Exhaust rear", "Muffler" };
        private sealed partial class Fixture
        {
            private readonly Dictionary<byte, PlayMakerFSM> _savedExhaust = new Dictionary<byte, PlayMakerFSM>();
            internal readonly Dictionary<byte, PlayMakerFSM> SavedExhaustParts = new Dictionary<byte, PlayMakerFSM>();
            private readonly Dictionary<byte, object> _exhaustRules = new Dictionary<byte, object>();
            private bool _exhaustSeeded;
            internal PlayMakerFSM ExhaustMount(byte group) => _savedExhaust[group];
            private PlayMakerFSM EnsureSavedExhaust(object source)
            {
                byte group = (byte)Get(source, "Index"); if (_savedExhaust.TryGetValue(group, out var saved)) return saved;
                var rule = Get(source, "Mount"); var mount = Empty(MountObject((string)Get(rule, "MountPath")), "Data"); var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                    states.Add(new FsmState(mount.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                mount.Fsm.States = states.ToArray(); mount.Fsm.StartState = "Idle";
                mount.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                mount.FsmVariables.FloatVariables = new[] { Scalar("Wear", 77), Scalar("DataPower", 70 + group), Scalar("DataTorque", 170 + group), Scalar("DataPowerAdd", .5f + group) };
                var part = Data(Child(mount.gameObject, "saved exhaust part " + group), (string)Get(rule, "PartPrefix") + "9", 1);
                var floats = new List<FsmFloat>(part.FsmVariables.FloatVariables); floats.Add(Scalar("DataPower", 70 + group)); floats.Add(Scalar("DataTorque", 170 + group)); floats.Add(Scalar("DataPowerAdd", .5f + group));
                part.FsmVariables.FloatVariables = floats.ToArray(); part.FsmVariables.FindFsmFloat("Wear").Value = 77;
                mount.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", part.gameObject) };
                _savedExhaust.Add(group, mount); SavedExhaustParts.Add(group, part); _exhaustRules.Add(group, source); return mount;
            }
            private void SeedExhaust()
            {
                var state = ((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Get() ?? new EngineBlockState { Flags = 11, Wear = 90 };
                state.Flags |= 11; state.ExhaustFlags = 15;
                for (int i = 0; i < state.ExhaustPerformance.Length; i++) state.ExhaustPerformance[i] = i + 1;
                ReceiveIntake(state);
            }
            internal void SetExhaust(byte mask, bool head = true)
            {
                var state = new EngineBlockState { Flags = head ? (byte)11 : (byte)0, Wear = head ? 90 : 0, ExhaustFlags = (byte)(mask & (head ? 15 : 14)) };
                for (int group = 0; group < 4; group++) if ((state.ExhaustFlags & (1 << group)) != 0)
                { state.ExhaustPerformance[group * 3] = 11 * (group + 1); state.ExhaustPerformance[group * 3 + 1] = 101 * (group + 1); state.ExhaustPerformance[group * 3 + 2] = (group % 2 == 0 ? -.01f : .01f) * (group + 1); }
                ReceiveIntake(state);
            }
            private void AssertSavedExhaust()
            {
                foreach (var entry in _savedExhaust)
                {
                    byte group = entry.Key; var mount = entry.Value; var part = SavedExhaustParts[group]; var rule = Get(_exhaustRules[group], "Mount");
                    foreach (var data in new[] { mount, part }) Require(data.FsmVariables.FindFsmFloat("Wear").Value == 77 && data.FsmVariables.FindFsmFloat("DataPower").Value == 70 + group
                        && data.FsmVariables.FindFsmFloat("DataTorque").Value == 170 + group && data.FsmVariables.FindFsmFloat("DataPowerAdd").Value == .5f + group, "Saved exhaust condition or performance changed.");
                    Require(mount.FsmVariables.FindFsmBool("Installed").Value && mount.FsmVariables.FindFsmGameObject("ActivePart").Value == part.gameObject
                        && part.transform.parent == mount.transform && part.FsmVariables.FindFsmInt("AssemblyID").Value == 1
                        && part.FsmVariables.FindFsmString("ID").Value == (string)Get(rule, "PartPrefix") + "9", "Saved exhaust assembly or identity changed.");
                }
            }
            internal void ConfigureExhaustCalculation(PlayMakerFSM valves)
            {
                var row = FindNativeRow("CORRIS/Simulation/Engine/Valves", "Valves");
                foreach (Dictionary<string, object> native in (IEnumerable)row["states"])
                {
                    string name = (string)native["name"]; int index = Array.IndexOf(ExhaustStates, name); if (index < 0) continue;
                    var state = NativeBagPartChecks.State(valves, name); var raw = (List<object>)native["actions"];
                    for (int i = 3; i < 6; i++) { state.Actions[i] = NativeAction((Dictionary<string, object>)raw[i], valves); state.Actions[i].Init(state); }
                    state.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = index == 3 ? "Calculations" : ExhaustStates[index + 1] } };
                }
            }
            internal PlayMakerFSM MakeNativeExhaust(byte group, PlayMakerFSM valves)
            {
                var obj = _savedExhaust[group].gameObject; UnityEngine.Object.DestroyImmediate(_savedExhaust[group]);
                var source = _exhaustRules[group]; var row = FindNativeRow((string)Get(Get(source, "Mount"), "MountPath"), "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                data.FsmVariables.FindFsmFloat("DataPower").Value = 70 + group; data.FsmVariables.FindFsmFloat("DataTorque").Value = 170 + group; data.FsmVariables.FindFsmFloat("DataPowerAdd").Value = .5f + group;
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedExhaustParts[group].gameObject;
                foreach (Dictionary<string, object> native in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)native["name"]); var raw = (List<object>)native["actions"]; var actions = new FsmStateAction[raw.Count];
                    // The manifold clears DataPower before Tightness; the car-mounted sections reverse those two actions.
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Update 2" && i == 0
                        || state.Name == "Remove part" && (i == (group == 0 ? 0 : 1) || i == 2 || i == 3 || i == 7 || i == 14 || i == 15)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                _savedExhaust[group] = data; valves.FsmVariables.FindFsmGameObject((string)Get(source, "TargetVariable")).Value = obj; return data;
            }
        }
        internal static void RunExhaust(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN125", "FuelLine", "exhaust-input-probe.json"))
            {
                var valves = f.AddConsumer("Valves"); f.ConfigureExhaustCalculation(valves); var reads = new List<FsmStateAction>();
                foreach (string state in ExhaustStates) for (int index = 0; index < 3; index++) reads.Add(NativeBagPartChecks.State(valves, state).Actions[index]);
                var owners = new object[reads.Count]; for (int i = 0; i < reads.Count; i++) owners[i] = Get(reads[i], "gameObject");
                Action saved = () => { f.AssertSaved(); for (byte i = 0; i < 4; i++) Require(!f.ExhaustMount(i).enabled, "Saved exhaust mount resumed."); };
                check("exhaust inputs: all native caches warm from saved mounts then cold host state supplies zero", () =>
                {
                    for (int i = 0; i < reads.Count; i += 3) { reads[i].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 70 + i / 3, "Native exhaust cache failed to warm."); }
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Absent host exhaust failed preparation.");
                    foreach (var read in reads) read.OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 0 && valves.FsmVariables.FindFsmFloat("PartTorque").Value == 0
                        && valves.FsmVariables.FindFsmFloat("PartPowerAdd").Value == 0, "Missing host borrowed saved exhaust."); saved();
                });
                check("exhaust inputs: packet arrival preserves native state and shared scratch", () =>
                {
                    string current = valves.ActiveStateName; f.SetExhaust(15); Require(f.Prepare(), "Host exhaust failed projection.");
                    Require(valves.ActiveStateName == current && valves.FsmVariables.FindFsmFloat("PartPower").Value == 0, "Arrival replayed exhaust calculation.");
                    reads[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 11, "Headers missed their read."); f.Prepare();
                    Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 11, "Preparation overwrote shared scratch.");
                    reads[9].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 44 && f.Target(reads[9]).GetComponent<Rigidbody>() == null, "Muffler read lost independent input."); saved();
                });
                for (byte mask = 0; mask < 16; mask++)
                {
                    byte flags = mask;
                    check("exhaust inputs: native power and torque sum for installed mask=" + flags, () =>
                    {
                        f.SetExhaust(flags); Require(f.Prepare(), "Exhaust mask failed preparation."); var state = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!;
                        float power = 500, torque = 600, add = 2;
                        for (int i = 0; i < 4; i++) { power += state.ExhaustPerformance[i * 3]; torque += state.ExhaustPerformance[i * 3 + 1]; add += state.ExhaustPerformance[i * 3 + 2]; }
                        valves.FsmVariables.FindFsmFloat("MaxPower").Value = 500; valves.FsmVariables.FindFsmFloat("MaxTorque").Value = 600; valves.FsmVariables.FindFsmFloat("PowerAdd").Value = 2;
                        NativeBagPartChecks.Fire(valves, "Headers"); Require(valves.ActiveStateName == "Calculations" && Near(valves.FsmVariables.FindFsmFloat("MaxPower").Value, power)
                            && Near(valves.FsmVariables.FindFsmFloat("MaxTorque").Value, torque) && Near(valves.FsmVariables.FindFsmFloat("PowerAdd").Value, add), "Native exhaust additions diverged."); saved();
                    });
                }
                check("exhaust inputs: missing engine head clears headers while car-mounted pipes remain independent", () =>
                {
                    f.SetExhaust(15, false); f.Prepare(); reads[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 0, "Headers survived head removal.");
                    reads[3].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 22, "Head removal discarded fixed exhaust."); saved();
                });
                check("exhaust inputs: stale conflicting and mutated packet arrays cannot restore removed exhaust", () =>
                {
                    f.SetExhaust(15); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; f.SetExhaust(0); Call(f.Sync, "OnEngineBlockState", old);
                    old.Revision++; Call(f.Sync, "OnEngineBlockState", old); old.ExhaustPerformance[0] = 900; f.Prepare();
                    foreach (var read in reads) read.OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 0, "Old exhaust restored removed power."); saved();
                });
                check("exhaust inputs: moving saved head preserves header protection", () =>
                {
                    f.SetExhaust(15); var head = f.ExhaustMount(0).transform.parent; var parent = head.parent; string name = head.name; head.parent = f.Car.transform; head.name = "moved exhaust head";
                    try { Require(f.Prepare(), "Moved header mount lost input."); reads[0].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 11, "Moved head lost host headers."); saved(); }
                    finally { head.parent = parent; head.name = name; }
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName; var read = reads[6];
                    check("exhaust inputs: foreign rear " + field + " is contained and repairs", () =>
                    {
                        var original = Get(read, field); object change = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "PartPower", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } } : (object)new FsmString { Value = "Other" };
                        Set(read, field, change);
                        try { NativeBagPartChecks.Fire(valves, "Exhaust rear"); Require(!valves.enabled, "Foreign exhaust signature escaped state-entry guard."); saved(); }
                        finally { Set(read, field, field == "gameObject" ? owners[6] : original); if (!f.Prepare()) f.Prepare(); }
                        Require(valves.enabled, "Repaired exhaust failed to resume.");
                    });
                }
                check("exhaust inputs: fixed mount relocation is contained and repairs", () =>
                {
                    var mount = f.ExhaustMount(1); var parent = mount.transform.parent; mount.transform.parent = f.Extras.transform;
                    try { NativeBagPartChecks.Fire(valves, "Exhaust front"); Require(!valves.enabled, "Foreign fixed pipe path supplied engine power."); saved(); }
                    finally { mount.transform.parent = parent; if (!f.Prepare()) f.Prepare(); }
                    Require(valves.enabled, "Restored fixed mount stayed paused.");
                });
                check("exhaust inputs: destroyed proxy repairs and disconnect restores all twelve owners", () =>
                {
                    UnityEngine.Object.DestroyImmediate(f.Target(reads[9])); Require(f.Prepare(), "Exhaust proxy failed repair."); reads[9].OnEnter(); Require(valves.FsmVariables.FindFsmFloat("PartPower").Value == 44, "Repair lost host exhaust.");
                    Call(f.Sync, "ReleaseSession"); Require(((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnect retained host exhaust.");
                    for (int i = 0; i < reads.Count; i++) Require(ReferenceEquals(Get(reads[i], "gameObject"), owners[i]), "Disconnect lost native exhaust owner."); saved();
                });
            }
            RunExhaustCapture(check);
        }
    }
}
