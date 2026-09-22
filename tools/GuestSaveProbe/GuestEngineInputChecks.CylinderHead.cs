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
            internal PlayMakerFSM? SavedCylinderHead;
            internal PlayMakerFSM SavedCylinderHeadPart = null!;
            private PlayMakerFSM EnsureSavedCylinderHead()
            {
                if (SavedCylinderHead != null) return SavedCylinderHead;
                SavedCylinderHead = Empty(MountObject("CARPARTS/StartParts/VIN1010/VINP_Cylinderhead"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "UPDATE" })
                    states.Add(new FsmState(SavedCylinderHead.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedCylinderHead.Fsm.States = states.ToArray(); SavedCylinderHead.Fsm.StartState = "Idle";
                SavedCylinderHead.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                SavedCylinderHead.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Wear", UseVariable = true, Value = 77 } };
                SavedCylinderHeadPart = Data(Child(SavedCylinderHead.gameObject, "saved cylinder head"), "VIN1119", 1);
                SavedCylinderHeadPart.FsmVariables.FindFsmFloat("Wear").Value = 77;
                SavedCylinderHead.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedCylinderHeadPart.gameObject) };
                return SavedCylinderHead;
            }
            internal void SetCylinderHead(bool installed, bool blockInstalled = true, bool available = true, bool damaged = false)
            {
                var state = new EngineBlockState { Revision = ++_engineBlockRevision,
                    Flags = available ? (byte)(1 | (blockInstalled ? 2 | (damaged ? 4 : 0) | (installed ? 8 : 0) : 0)) : (byte)0,
                    Wear = available && blockInstalled ? 90 : 0 };
                SeedRequiredAmbient(state); Call(Sync, "OnEngineBlockState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Get()?.Revision == _engineBlockRevision, "Fixture head state rejected.");
            }
            private void AssertSavedCylinderHead()
            {
                if (SavedCylinderHead == null) return;
                Require(SavedCylinderHead.FsmVariables.FindFsmBool("Installed").Value && SavedCylinderHead.FsmVariables.FindFsmFloat("Wear").Value == 77
                    && SavedCylinderHeadPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value == "VIN1119"
                    && SavedCylinderHeadPart.FsmVariables.FindFsmInt("AssemblyID").Value == 1
                    && SavedCylinderHeadPart.transform.parent == SavedCylinderHead.transform
                    && SavedCylinderHead.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedCylinderHeadPart.gameObject,
                    "Head input changed saved part condition, identity or assembly.");
            }
            internal PlayMakerFSM MakeNativeCylinderHead()
            {
                var obj = EnsureSavedCylinderHead().gameObject; UnityEngine.Object.DestroyImmediate(SavedCylinderHead);
                var row = FindNativeRow("CARPARTS/StartParts/VIN1010/VINP_Cylinderhead", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedCylinderHeadPart.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"];
                    var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Remove part" && (i == 4 || i == 11 || i == 12)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                SavedCylinderHead = data; var reference = Reader.FsmVariables.FindFsmGameObject("db_Cylinderhead"); if (reference != null) reference.Value = obj; return data;
            }
        }

        internal static void RunCylinderHead(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN102", "Cylinders", "cylinder-head-input-probe.json"))
            {
                f.Receive(90, 0, 0, true); f.ReceiveVariant(f.Auxiliary("VIN115").Variants[0], 90, 1, 2, true);
                var action = f.Action("Powertrain", 0); var original = (FsmOwnerDefault)Get(action, "gameObject");
                check("head inputs: saved cache is replaced without changing the original mount", () =>
                {
                    action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Native saved head cache did not warm.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Head input did not prepare."); action.OnEnter();
                    Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value && f.Target(action) != original.GameObject.Value
                        && original.GameObject.Value == f.SavedCylinderHead!.gameObject && !f.SavedCylinderHead.enabled, "Missing host borrowed saved head."); f.AssertSaved();
                });
                check("head inputs: arrival leaves calculation scratch until the native read", () =>
                {
                    string active = f.Reader.ActiveStateName; f.SetCylinderHead(true); Require(f.Prepare(), "Host head did not bind.");
                    Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value && f.Reader.ActiveStateName == active, "Arrival replayed combustion.");
                    action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value && f.Target(action).GetComponent<Rigidbody>() == null, "Host head input missing or physical."); f.AssertSaved();
                });
                foreach (bool installed in new[] { false, true })
                    foreach (bool blockInstalled in new[] { false, true })
                    {
                        bool head = installed, block = blockInstalled;
                        check("head inputs: native powertrain head=" + head + " block=" + block, () =>
                        {
                            f.SetCylinderHead(head, block); Require(f.Prepare(), "Head gate did not prepare."); f.Fire("Powertrain");
                            Require(f.Reader.ActiveStateName == (head && block ? "Flywheel" : "Not Ok"), "Native powertrain ignored shared head installation.");
                            if (!head || !block) Require(f.Starter.FsmVariables.FindFsmBool("ShutOff").Value, "Missing head did not stop native starting."); f.AssertSaved();
                        });
                    }
                check("head inputs: block damage does not invent a head removal", () =>
                { f.SetCylinderHead(true, true, true, true); f.Prepare(); action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Damage was treated as head removal."); f.AssertSaved(); });
                check("head inputs: unavailable stale and conflicting state cannot restore a head", () =>
                {
                    f.SetCylinderHead(false, true, false); f.Prepare(); action.OnEnter(); Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Unavailable head remained installed.");
                    var stale = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; stale.Flags = 11; stale.Wear = 90;
                    Call(f.Sync, "OnEngineBlockState", stale); stale.Revision--; Call(f.Sync, "OnEngineBlockState", stale);
                    f.Prepare(); action.OnEnter(); Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Stale head restored combustion.");
                    f.SetCylinderHead(true); f.Prepare(); action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Repaired head remained absent.");
                });
                check("head inputs: relocation and rename follow the saved block identity", () =>
                {
                    var block = f.SavedCylinderHead!.transform.parent; var parent = block.parent; string name = block.name;
                    block.parent = f.Car.transform; block.name = "moved saved engine";
                    try { Require(f.Prepare() && !f.SavedCylinderHead.enabled, "Moving block lost head protection."); action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Moving block lost input."); f.AssertSaved(); }
                    finally { block.parent = parent; block.name = name; }
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName;
                    check("head inputs: foreign " + field + " pauses and repairs", () =>
                    {
                        var saved = Get(action, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmBool { Name = "Installed1", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } }
                            : (object)new FsmString { Value = "Other" };
                        Set(action, field, changed);
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Foreign head read escaped protection."); f.AssertSaved(); }
                        finally { Set(action, field, field == "gameObject" ? original : saved); if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Head read failed recovery.");
                    });
                }
                foreach (string id in new[] { "VIN1110", "VIN101", "VIN101x1" })
                {
                    string value = id;
                    check("head inputs: foreign block identity " + value + " remains protected", () =>
                    {
                        var root = f.SavedCylinderHead!.transform.parent.GetComponent<PlayMakerFSM>(); var native = root.FsmVariables.FindFsmString("ID"); string saved = native.Value;
                        native.Value = value;
                        try { f.Fire("Powertrain"); Require(!f.Reader.enabled && !f.SavedCylinderHead.enabled, "Foreign head root escaped containment."); f.AssertSaved(); }
                        finally { native.Value = saved; if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Restored native block identity did not recover.");
                    });
                }
                check("head inputs: proxy destruction repairs accepted installation", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(action)); Require(f.Prepare(), "Destroyed head proxy failed repair."); action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Repair lost head state."); f.AssertSaved(); });
                check("head inputs: disconnect restores native owner and clears head observation", () =>
                {
                    Call(f.Sync, "ReleaseSession"); Require(((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null
                        && ReferenceEquals(Get(action, "gameObject"), original) && !f.SavedCylinderHead!.enabled, "Disconnect lost head ownership/protection."); f.AssertSaved();
                });
            }
            RunCylinderHeadCapture(check);
        }

        private static void RunCylinderHeadCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN102", "Cylinders", "cylinder-head-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                var blockPart = f.SavedEngineBlockPart; var strings = new List<FsmString>(blockPart.FsmVariables.StringVariables);
                strings.Add(new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" }); blockPart.FsmVariables.StringVariables = strings.ToArray();
                var data = f.MakeNativeCylinderHead(); data.transform.parent = blockPart.transform;
                var installed = data.FsmVariables.FindFsmBool("Installed");
                check("head capture: initialization and attachment must finish", () =>
                {
                    Require(f.CaptureEngineBlock().Flags == 3, "Unstarted head changed block availability or appeared installed.");
                    NativeBagPartChecks.Start(data); NativeBagPartChecks.Fire(data, "Installed"); Require(f.CaptureEngineBlock().Flags == 3, "Intermediate head appeared installed.");
                    NativeBagPartChecks.Fire(data, "UPDATE"); Require(f.CaptureEngineBlock().Flags == 11, "Ready host head missing.");
                });
                foreach (string stateName in new[] { "Install 1", "Install 2", "Allow removal?", "Allow install?", "Near" })
                {
                    string state = stateName;
                    check("head capture: transition " + state + " never exposes installation", () =>
                    { NativeBagPartChecks.Fire(data, state); Require(f.CaptureEngineBlock().Flags == 3, "Transitional head changed block or appeared installed."); });
                }
                check("head capture: snapshots retain pending installation changes and independent block damage", () =>
                {
                    NativeBagPartChecks.Fire(data, "UPDATE"); var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); var first = f.CaptureEngineBlock();
                    publication.MarkBroadcast(first.Revision); installed.Value = false; NativeBagPartChecks.Fire(data, "Idle"); var removed = f.CaptureEngineBlock();
                    Require(removed.Flags == 3 && removed.Revision == first.Revision + 1 && publication.NeedsBroadcast, "Snapshot swallowed head removal.");
                    installed.Value = true; NativeBagPartChecks.Fire(data, "UPDATE"); block.FsmVariables.FindFsmBool("Damaged").Value = true;
                    var refitted = f.CaptureEngineBlock(); Require(refitted.Flags == 15 && refitted.Revision == removed.Revision + 1 && publication.NeedsBroadcast, "Refit lost native damage or revision.");
                    block.FsmVariables.FindFsmBool("Damaged").Value = false;
                });
                foreach (string faultName in new[] { "inactive", "part inactive", "parent", "assembly", "duplicate", "duplicate mount", "part identity", "block identity", "missing part", "missing mount" })
                {
                    string fault = faultName;
                    check("head capture: " + fault + " leaves block available and recovers", () =>
                    {
                        PlayMakerFSM? duplicate = null; GameObject? extra = null;
                        if (fault == "inactive") data.gameObject.SetActive(false);
                        if (fault == "part inactive") f.SavedCylinderHeadPart.gameObject.SetActive(false);
                        if (fault == "parent") f.SavedCylinderHeadPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedCylinderHeadPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate") duplicate = Empty(data.gameObject, "Data");
                        if (fault == "duplicate mount") extra = Child(blockPart.gameObject, data.gameObject.name);
                        if (fault == "part identity") f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN1120";
                        if (fault == "block identity") blockPart.FsmVariables.FindFsmString("ID").Value = "VIN1020";
                        if (fault == "missing part") data.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") data.transform.parent = f.Extras.transform;
                        try { Require(f.CaptureEngineBlock().Flags == 3, "Invalid head disabled the block or remained installed."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
                            data.gameObject.SetActive(true); f.SavedCylinderHeadPart.gameObject.SetActive(true); data.transform.parent = blockPart.transform;
                            f.SavedCylinderHeadPart.transform.parent = data.transform; f.SavedCylinderHeadPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                            f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN1119"; blockPart.FsmVariables.FindFsmString("ID").Value = "VIN1010";
                            data.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedCylinderHeadPart.gameObject; NativeBagPartChecks.Fire(data, "UPDATE");
                        }
                        Require(f.CaptureEngineBlock().Flags == 11, "Repaired head remained absent.");
                    });
                }
                check("head capture: installed head cannot outlive removal of its host block", () =>
                {
                    block.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(block, "Idle"); Require(f.CaptureEngineBlock().Flags == 1, "Detached block retained head input.");
                    block.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(block, "Update"); Require(f.CaptureEngineBlock().Flags == 11, "Block refit lost head input.");
                });
                check("head protection: native removal copies wear and detaches before protection", () =>
                {
                    data.FsmVariables.FindFsmFloat("Wear").Value = 64; NativeBagPartChecks.Fire(data, "Remove part");
                    Require(!installed.Value && f.SavedCylinderHeadPart.transform.parent == null && f.SavedCylinderHeadPart.FsmVariables.FindFsmFloat("Wear").Value == 64,
                        "Native head removal did not run.");
                    f.SavedCylinderHeadPart.transform.parent = data.transform; installed.Value = true; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                    f.SavedCylinderHeadPart.FsmVariables.FindFsmFloat("Wear").Value = 77; NativeBagPartChecks.Fire(data, "UPDATE"); f.AssertSaved();
                });
                check("head protection: admission follows renamed block and prevents removal through disconnect", () =>
                {
                    blockPart.gameObject.name = "renamed saved engine"; protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare() && !data.enabled && !data.Fsm.RestartOnEnable, "Moved head mount was not paused on admission.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = 12; NativeBagPartChecks.Fire(data, "Remove part");
                    Require(f.SavedCylinderHeadPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedCylinderHeadPart.transform.parent == data.transform, "Paused native head wrote or detached.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = 77; f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); data.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(data, "Remove part"); Require(!data.enabled, "Disconnect resumed saved head."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
