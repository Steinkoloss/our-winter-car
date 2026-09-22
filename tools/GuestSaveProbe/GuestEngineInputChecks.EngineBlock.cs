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
            internal PlayMakerFSM? SavedEngineBlock;
            internal PlayMakerFSM SavedEngineBlockPart = null!;
            private uint _engineBlockRevision;
            private PlayMakerFSM EnsureSavedEngineBlock()
            {
                if (SavedEngineBlock != null) return SavedEngineBlock;
                SavedEngineBlock = Empty(PathObject(Car, "CORRIS/MotorPivot/MassCenter/Block/VINP_Block"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 2", "Installed", "Remove part", "Allow removal?", "Update", "Install 1", "State 1" })
                    states.Add(new FsmState(SavedEngineBlock.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedEngineBlock.Fsm.States = states.ToArray(); SavedEngineBlock.Fsm.StartState = "Idle";
                SavedEngineBlock.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true },
                    new FsmBool { Name = "Damaged", UseVariable = true, Value = false } };
                SavedEngineBlock.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Wear", UseVariable = true, Value = 77 } };
                SavedEngineBlockPart = Data(Child(SavedEngineBlock.gameObject, "saved block assembly"), null, 1);
                SavedEngineBlockPart.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = 1 } };
                SavedEngineBlockPart.FsmVariables.FindFsmFloat("Wear").Value = 77;
                SavedEngineBlock.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedEngineBlockPart.gameObject) };
                return SavedEngineBlock;
            }
            private void ConfigureDirectBlockTargets(PlayMakerFSM reader)
            {
                if (reader.FsmName != "Starter" || SavedEngineBlock == null) return;
                foreach (string state in new[] { "Motor installed", "Running" })
                {
                    var action = NativeBagPartChecks.State(reader, state).Actions[state == "Running" ? 8 : 0];
                    var owner = (FsmOwnerDefault)Get(action, "gameObject");
                    Require(!owner.GameObject.UseVariable, "Native direct block target became a shared variable.");
                    owner.GameObject.Value = SavedEngineBlock.gameObject;
                }
            }
            internal void SetEngineBlock(bool installed, float wear, bool damaged, bool available = true)
            {
                var state = new EngineBlockState { Revision = ++_engineBlockRevision,
                    Flags = available ? (byte)(EngineBlockState.Available | (installed ? EngineBlockState.Installed | (damaged ? EngineBlockState.Damaged : 0) : 0)) : (byte)0,
                    Wear = available && installed ? wear : 0 };
                SeedRequiredAmbient(state); Call(Sync, "OnEngineBlockState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Get()?.Revision == _engineBlockRevision, "Fixture block state rejected.");
            }
            private void AssertSavedEngineBlock()
            {
                if (SavedEngineBlock == null) return;
                Require(SavedEngineBlock.FsmVariables.FindFsmBool("Installed").Value && !SavedEngineBlock.FsmVariables.FindFsmBool("Damaged").Value
                    && SavedEngineBlock.FsmVariables.FindFsmFloat("Wear").Value == 77 && SavedEngineBlockPart.FsmVariables.FindFsmFloat("Wear").Value == 77
                    && SavedEngineBlockPart.FsmVariables.FindFsmInt("AssemblyID").Value == 1
                    && SavedEngineBlock.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedEngineBlockPart.gameObject
                    && SavedEngineBlockPart.transform.parent == SavedEngineBlock.transform, "Engine projection changed saved block values or assembly.");
            }
            internal PlayMakerFSM MakeNativeBlock()
            {
                var obj = EnsureSavedEngineBlock().gameObject; UnityEngine.Object.DestroyImmediate(SavedEngineBlock);
                var row = FindNativeRow("CORRIS/MotorPivot/MassCenter/Block/VINP_Block", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row);
                data.Fsm.Init(data); data.FsmVariables.FindFsmBool("Installed").Value = true;
                data.FsmVariables.FindFsmBool("Damaged").Value = false; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedEngineBlockPart.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]);
                    var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++)
                        actions[i] = state.Name == "Update" && i == 1 || state.Name == "Remove part" && (i == 3 || i == 9 || i == 10 || i == 11)
                            ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state);
                    // Native wear copying and detachment are exercised; body creation,
                    // save operations and global assembly notifications are separate.
                    state.Transitions = new FsmTransition[0];
                }
                SavedEngineBlock = data; ConfigureDirectBlockTargets(Reader); return data;
            }
            internal EngineBlockState CaptureEngineBlock() => (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode((EngineBlockState)Call(Sync, "BuildEngineBlockState")!));
            internal void ConfigureBlockDecisions()
            {
                string state = Reader.FsmName == "Starter" ? "Motor installed" : Reader.FsmName == "Oil" ? "Major damage?" : "Block damage";
                var target = NativeBagPartChecks.State(Reader, state); target.Actions[1] = Import(state, 1); target.Actions[1].Init(target); RestoreNativeTransitions(state);
                if (Reader.FsmName == "Starter")
                {
                    NativeBagPartChecks.State(Reader, "Wiring").Transitions = new FsmTransition[0];
                    var running = NativeBagPartChecks.State(Reader, "Running"); running.Actions[9] = Import("Running", 9); running.Actions[9].Init(running);
                    RestoreNativeTransitions("Running");
                }
            }
        }

        internal static void RunEngineBlock(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "Starter", "Oil", "Cooling" })
            using (var f = new Fixture(consumer == "Starter" ? "VIN130" : consumer == "Oil" ? "VIN132" : "VIN126", consumer, "engine-block-input-probe.json"))
            {
                string label = "block inputs: " + consumer + " ";
                string state = consumer == "Starter" ? "Motor installed" : consumer == "Oil" ? "Major damage?" : "Block damage";
                var action = f.Action(state, 0); var original = (FsmOwnerDefault)Get(action, "gameObject");
                Func<bool> absent = () => consumer == "Starter" ? !f.Reader.FsmVariables.FindFsmBool("MotorInstalled").Value
                    : consumer == "Oil" ? f.Reader.FsmVariables.FindFsmFloat("Wear").Value == 0 : f.Reader.FsmVariables.FindFsmBool("Damaged").Value;
                check(label + "saved cache is replaced without changing original targets", () =>
                {
                    action.OnEnter(); Require(!absent(), "Native saved block cache did not warm.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Block reader did not prepare."); action.OnEnter();
                    Require(absent() && original.GameObject.Value == f.SavedEngineBlock!.gameObject && f.Target(action) != original.GameObject.Value,
                        "Missing host borrowed or rewrote saved block.");
                    Require(f.Target(action).GetComponent<Rigidbody>() == null && !f.SavedEngineBlock!.enabled, "Proxy acquired physics or resumed saved block."); f.AssertSaved();
                });
                check(label + "host state changes only native read outputs", () =>
                {
                    f.SetEngineBlock(true, 19.5f, false); Require(f.Prepare() && absent(), "Packet replayed block read."); action.OnEnter();
                    Require(!absent() && (consumer != "Oil" || f.Reader.FsmVariables.FindFsmFloat("Wear").Value == 19.5f), "Host block was not read.");
                    f.SetEngineBlock(true, 0, true); f.Prepare(); action.OnEnter();
                    Require(consumer == "Starter" ? f.Reader.FsmVariables.FindFsmBool("MotorInstalled").Value : absent(), "Block damage was invented from wear or installed state."); f.AssertSaved();
                });
                check(label + "removal unavailable and conflicting records cannot restore a block", () =>
                {
                    foreach (bool available in new[] { true, false })
                    {
                        f.SetEngineBlock(false, 0, false, available); f.Prepare(); action.OnEnter(); Require(absent(), "Removal retained host block input.");
                        var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; old.Flags = 3; old.Wear = 100;
                        Call(f.Sync, "OnEngineBlockState", old); old.Revision--; Call(f.Sync, "OnEngineBlockState", old);
                        f.Prepare(); action.OnEnter(); Require(absent(), "Stale/conflicting block restored power.");
                    }
                    f.SetEngineBlock(true, 90, false); f.Prepare(); action.OnEnter(); Require(!absent(), "Repaired host block remained unavailable."); f.AssertSaved();
                });
                f.ConfigureBlockDecisions();
                if (consumer == "Starter")
                {
                    foreach (bool installed in new[] { false, true })
                    {
                        bool value = installed;
                        check(label + "native start decision installed=" + value, () =>
                        {
                            f.SetEngineBlock(value, 0, true); Require(f.Prepare(), "Starter block failed."); f.Fire(state);
                            Require(f.Reader.ActiveStateName == (value ? "Wiring" : "Wait"), "Native block gate acquired a wear/damage threshold."); f.AssertSaved();
                        });
                    }
                    check(label + "running everyFrame read sees removal and native stall without reentry", () =>
                    {
                        f.SetEngineBlock(true, 90, false); f.Prepare(); f.Fire("Running");
                        Require(f.Reader.ActiveStateName == "Running", "Installed block stopped running.");
                        f.SetEngineBlock(false, 0, false); f.Prepare(); Require(f.Reader.ActiveStateName == "Running", "Packet synthesized a stall event.");
                        f.Reader.Fsm.Update(); Require(f.Reader.ActiveStateName == "Stall engine", "Native everyFrame block removal failed to stall."); f.AssertSaved();
                    });
                    check(label + "running native read refreshes host removal without periodic preparation", () =>
                    {
                        f.SetEngineBlock(true, 90, false); Require(f.Prepare(), "Starter preparation failed."); f.Fire("Running");
                        Require(f.Reader.ActiveStateName == "Running", "Starter never entered Running.");
                        f.SetEngineBlock(false, 0, false);
                        f.Reader.Fsm.Update();
                        Require(f.Reader.ActiveStateName == "Stall engine", "Every-frame read retained the old installed input."); f.AssertSaved();
                    });
                    check(label + "foreign second direct target pauses and repairs both native reads", () =>
                    {
                        var running = f.Action("Running", 8); var binding = FindBlockOriginal(f.Sync, running); var owner = (FsmOwnerDefault)Get(binding, "Original");
                        var saved = owner.GameObject.Value; owner.GameObject.Value = f.Mount.gameObject;
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Cross-mount direct target was accepted."); f.AssertSaved(); }
                        finally { owner.GameObject.Value = saved; if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Restored direct target did not recover.");
                    });
                }
                else if (consumer == "Oil")
                    foreach (float wear in new[] { .99f, 1f, 1.01f })
                    {
                        float value = wear;
                        check(label + "native wear boundary " + value, () =>
                        {
                            f.SetEngineBlock(true, value, false); f.Prepare(); f.Fire(state);
                            Require(f.Reader.ActiveStateName == (value < 1 ? "No oil" : "Valve Cover"), "Native strict block wear comparison changed."); f.AssertSaved();
                        });
                    }
                else
                    foreach (bool damage in new[] { false, true })
                    {
                        bool value = damage;
                        check(label + "native damage branch " + value, () =>
                        {
                            f.SetEngineBlock(true, 90, value); f.Prepare(); f.Fire(state);
                            Require(f.Reader.ActiveStateName == (value ? "State 4" : "Water leak"), "Native block damage branch was inverted."); f.AssertSaved();
                        });
                    }
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame" })
                {
                    string field = fieldName;
                    check(label + "changed " + field + " contains the graph and repairs", () =>
                    {
                        var saved = Get(action, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue"
                            ? consumer == "Oil" ? (object)new FsmFloat { Name = "Wear", UseVariable = true } : new FsmBool { Name = "MotorInstalled", UseVariable = true }
                            : new FsmString { Value = "Other" };
                        Set(action, field, changed);
                        try { Require(!f.Prepare() && !f.Reader.enabled && !f.SavedEngineBlock!.enabled, "Broken block reader escaped protection."); f.AssertSaved(); }
                        finally { Set(action, field, saved); if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired block reader stayed paused.");
                    });
                }
                check(label + "destroyed proxy repairs while saved block remains untouched", () =>
                {
                    UnityEngine.Object.DestroyImmediate(f.Target(action)); f.SetEngineBlock(true, 44, false);
                    Require(f.Prepare(), "Block proxy failed repair."); action.OnEnter(); Require(!absent(), "Repair lost accepted state."); f.AssertSaved();
                });
                check(label + "disconnect restores original targets and clears host block", () =>
                {
                    Call(f.Sync, "ReleaseSession"); Require(((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null
                        && ReferenceEquals(Get(action, "gameObject"), original) && !f.SavedEngineBlock!.enabled, "Disconnect lost block ownership/protection."); f.AssertSaved();
                });
            }
            using (var f = new Fixture("VIN130", "Starter", "engine-block-input-probe.json"))
            {
                var oil = f.AddConsumer("Oil"); var cooling = f.AddConsumer("Cooling");
                check("block inputs: starter oil and cooling share one accepted host observation", () =>
                {
                    foreach (bool installed in new[] { false, true, false, true })
                    {
                        f.SetEngineBlock(installed, 17.5f, false); Require(f.Prepare(), "Shared block consumers failed to prepare.");
                        f.Action("Motor installed", 0).OnEnter();
                        NativeBagPartChecks.State(oil, "Major damage?").Actions[0].OnEnter();
                        NativeBagPartChecks.State(cooling, "Block damage").Actions[0].OnEnter();
                        Require(f.Reader.FsmVariables.FindFsmBool("MotorInstalled").Value == installed
                            && oil.FsmVariables.FindFsmFloat("Wear").Value == (installed ? 17.5f : 0)
                            && cooling.FsmVariables.FindFsmBool("Damaged").Value == !installed, "Shared consumers disagreed about the host block.");
                        f.AssertSaved();
                    }
                });
            }
            RunEngineBlockCapture(check);
        }
        private static object FindBlockOriginal(object sync, FsmStateAction action)
        {
            foreach (object binding in (IEnumerable)Get(sync, "_guestEngineInputs"))
                foreach (object read in (IEnumerable)Get(binding, "Reads"))
                    if (ReferenceEquals(Get(read, "Action"), action)) return read;
            throw new InvalidOperationException("Missing block reader binding.");
        }

        private static void RunEngineBlockCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN130", "Starter", "engine-block-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var data = f.MakeNativeBlock(); var installed = data.FsmVariables.FindFsmBool("Installed"); var wear = data.FsmVariables.FindFsmFloat("Wear");
                var damage = data.FsmVariables.FindFsmBool("Damaged");
                check("block capture: initialization and native parent transition must finish", () =>
                {
                    Require(f.CaptureEngineBlock().Flags == 0, "Unstarted block published defaults."); NativeBagPartChecks.Start(data);
                    NativeBagPartChecks.Fire(data, "Installed"); Require(f.CaptureEngineBlock().Flags == 0, "Intermediate block was accepted.");
                    NativeBagPartChecks.Fire(data, "Update"); wear.Value = 33.25f;
                    Require(f.CaptureEngineBlock().Flags == 3 && f.CaptureEngineBlock().Wear == 33.25f, "Host read stale part condition.");
                });
                foreach (string stateName in new[] { "Install 1", "Install 2", "State 1", "Allow removal?" })
                {
                    string state = stateName;
                    check("block capture: transition " + state + " is unavailable", () =>
                    { NativeBagPartChecks.Fire(data, state); Require(f.CaptureEngineBlock().Flags == 0, "Intermediate host block accepted."); });
                }
                check("block capture: snapshots preserve live changes and independent damage flags", () =>
                {
                    NativeBagPartChecks.Fire(data, "Update"); var first = f.CaptureEngineBlock(); var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication");
                    publication.MarkBroadcast(first.Revision); wear.Value = 91; damage.Value = true;
                    var snapshot = f.CaptureEngineBlock(); Require(snapshot.Revision == first.Revision + 1 && snapshot.Flags == 7 && snapshot.Wear == 91
                        && publication.NeedsBroadcast, "Snapshot swallowed native block condition update.");
                    Require(f.CaptureEngineBlock().Revision == snapshot.Revision && publication.NeedsBroadcast, "Snapshot consumed pending publication.");
                    installed.Value = false; NativeBagPartChecks.Fire(data, "Idle"); Require(f.CaptureEngineBlock().Flags == 1 && f.CaptureEngineBlock().Wear == 0, "Removed block retained damage/wear.");
                    installed.Value = true; NativeBagPartChecks.Fire(data, "Update"); Require(f.CaptureEngineBlock().Flags == 7, "Block reinstall failed to recover.");
                });
                foreach (string faultName in new[] { "inactive", "part inactive", "parent", "assembly", "duplicate", "wear", "active part" })
                {
                    string fault = faultName;
                    check("block capture: " + fault + " fails locally and recovers", () =>
                    {
                        PlayMakerFSM? duplicate = null;
                        if (fault == "inactive") data.gameObject.SetActive(false);
                        if (fault == "part inactive") f.SavedEngineBlockPart.gameObject.SetActive(false);
                        if (fault == "parent") f.SavedEngineBlockPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedEngineBlockPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate") duplicate = Empty(data.gameObject, "Data");
                        if (fault == "wear") wear.Value = float.NaN;
                        if (fault == "active part") data.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        try { Require(f.CaptureEngineBlock().Flags == 0 && f.CaptureEngineBlock().Wear == 0, "Broken source retained fitted block."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                            data.gameObject.SetActive(true); f.SavedEngineBlockPart.gameObject.SetActive(true); f.SavedEngineBlockPart.transform.parent = data.transform;
                            f.SavedEngineBlockPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1; wear.Value = 77;
                            data.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedEngineBlockPart.gameObject; NativeBagPartChecks.Fire(data, "Update");
                        }
                        Require(f.CaptureEngineBlock().Flags == 7, "Repaired block remained unavailable.");
                    });
                }
                check("block protection: disabled continuous wear stays inert while native removal writes and detaches", () =>
                {
                    var copy = NativeBagPartChecks.State(data, "Update").Actions[1];
                    wear.Value = 65; NativeBagPartChecks.Fire(data, "Update"); data.Fsm.Update();
                    Require(!copy.Enabled && f.SavedEngineBlockPart.FsmVariables.FindFsmFloat("Wear").Value == 77,
                        "Fixture enabled the disabled native continuous wear action.");
                    wear.Value = 64; NativeBagPartChecks.Fire(data, "Remove part");
                    Require(!installed.Value && f.SavedEngineBlockPart.transform.parent == null
                        && f.SavedEngineBlockPart.FsmVariables.FindFsmFloat("Wear").Value == 64, "Native removal did not copy wear and detach block.");
                    Require(f.CaptureEngineBlock().Flags == 0, "Intermediate removal was published early."); NativeBagPartChecks.Fire(data, "Idle"); Require(f.CaptureEngineBlock().Flags == 1, "Settled removal not published.");
                    f.SavedEngineBlockPart.transform.parent = data.transform; installed.Value = true; damage.Value = false; wear.Value = 77;
                    f.SavedEngineBlockPart.FsmVariables.FindFsmFloat("Wear").Value = 77; NativeBagPartChecks.Fire(data, "Update");
                });
                check("block capture: hosts reject peer records and guests cannot publish", () =>
                {
                    Call(f.Sync, "ClearEngineBlock"); Call(f.Sync, "OnEngineBlockState", new EngineBlockState { Revision = 500, Flags = 3, Wear = 99 });
                    Require(((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Host accepted guest block state.");
                    Property(f.Session, "IsHost", false); Require(Call(f.Sync, "BuildEngineBlockState") == null, "Guest published its own block."); Property(f.Session, "IsHost", true);
                });
                check("block protection: guest admission prevents removal wear and detachment through disconnect", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false); f.ClearEngineBlockInputs();
                    Require(f.Prepare() && !data.enabled && !data.Fsm.RestartOnEnable, "Guest block simulation stayed active.");
                    data.Fsm.Update(); NativeBagPartChecks.Fire(data, "Remove part"); f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); data.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(data, "Remove part");
                    Require(!data.enabled, "Disconnect resumed saved block removal."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
