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
            internal PlayMakerFSM? SavedGearbox;
            internal PlayMakerFSM SavedGearboxPart = null!;
            private uint _gearboxRevision;
            private PlayMakerFSM EnsureSavedGearbox()
            {
                if (SavedGearbox != null) return SavedGearbox;
                SavedGearbox = Empty(PathObject(Car, "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Update 2", "State 1",
                    "Remove other", "Remove other 2", "Allow install?", "Far", "Near", "Check Flywheel" })
                    states.Add(new FsmState(SavedGearbox.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedGearbox.Fsm.States = states.ToArray(); SavedGearbox.Fsm.StartState = "Idle";
                SavedGearbox.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                SavedGearbox.FsmVariables.IntVariables = new[] { new FsmInt { Name = "Type", UseVariable = true, Value = 0 }, new FsmInt { Name = "DamageType", UseVariable = true, Value = 3 } };
                SavedGearbox.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Wear", UseVariable = true, Value = 77 }, new FsmFloat { Name = "OilLevel", UseVariable = true, Value = 4 } };
                SavedGearboxPart = Data(Child(SavedGearbox.gameObject, "saved transmission"), null, 1);
                SavedGearboxPart.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = 1 },
                    new FsmInt { Name = "Type", UseVariable = true, Value = 0 }, new FsmInt { Name = "DamageType", UseVariable = true, Value = 3 } };
                SavedGearboxPart.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Wear", UseVariable = true, Value = 77 }, new FsmFloat { Name = "OilLevel", UseVariable = true, Value = 4 } };
                SavedGearbox.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedGearboxPart.gameObject) };
                return SavedGearbox;
            }
            internal void SetGearbox(int type, bool available = true)
            {
                var state = new GearboxState { Revision = ++_gearboxRevision, Flags = available ? GearboxState.Available : (byte)0, Type = available ? type : 0 };
                Call(Sync, "OnGearboxState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((GearboxReplica)Get(Sync, "_gearboxReplica")).Get()?.Revision == _gearboxRevision, "Fixture gearbox state rejected.");
            }
            private void AssertSavedGearbox()
            {
                if (SavedGearbox == null) return;
                Require(SavedGearbox.FsmVariables.FindFsmBool("Installed").Value && SavedGearbox.FsmVariables.FindFsmInt("Type").Value == 0
                    && SavedGearboxPart.FsmVariables.FindFsmInt("Type").Value == 0 && SavedGearboxPart.FsmVariables.FindFsmInt("AssemblyID").Value == 1
                    && SavedGearbox.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedGearboxPart.gameObject
                    && SavedGearboxPart.transform.parent == SavedGearbox.transform, "Projection changed saved transmission identity or assembly.");
                foreach (var data in new[] { SavedGearbox, SavedGearboxPart })
                    Require(data.FsmVariables.FindFsmFloat("Wear").Value == 77 && data.FsmVariables.FindFsmFloat("OilLevel").Value == 4
                        && data.FsmVariables.FindFsmInt("DamageType").Value == 3, "Projection changed saved transmission condition.");
            }
            internal PlayMakerFSM ConfigureGearboxDecision()
            {
                var state = NativeBagPartChecks.State(Reader, "Check automatic");
                for (int i = 1; i < state.Actions.Length; i++) { state.Actions[i] = Import(state.Name, i); state.Actions[i].Init(state); }
                RestoreNativeTransitions(state.Name);
                foreach (string boundary in new[] { "Motor installed", "ACC off" }) NativeBagPartChecks.State(Reader, boundary).Transitions = new FsmTransition[0];
                var row = FindNativeRow("CORRIS/Simulation/Systems/Drivetrain/GearboxAutomatic", "3 speed");
                var selector = NativeBagPartChecks.MakeFsm(PathObject(Car, (string)row["path"]), row); selector.Fsm.Init(selector);
                selector.FsmVariables.FindFsmGameObject("db_Gearbox").Value = EnsureSavedGearbox().gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var target = NativeBagPartChecks.State(selector, (string)stateRow["name"]); target.Transitions = new FsmTransition[0];
                    var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    for (int index = 0; index < actions.Length; index++)
                    {
                        bool choice = index == 0 && Array.IndexOf(new[] { "P", "N", "D", "R", "1", "2" }, target.Name) >= 0;
                        // The selector shares its FSM with the newly guarded wear
                        // reader and oil writes; keep their real signatures too.
                        bool wear = target.Name == "Set stall speed" && (index == 0 || index == 3)
                            || index == 3 && (target.Name == "State 1" || target.Name == "State 3");
                        actions[index] = choice || wear ? NativeAction((Dictionary<string, object>)raw[index], selector) : new Quiet();
                    }
                    target.Actions = actions; foreach (var action in actions) action.Init(target);
                }
                Reader.FsmVariables.FindFsmGameObject("SimAutomatic").Value = selector.gameObject; NativeBagPartChecks.Start(selector); return selector;
            }
            internal PlayMakerFSM MakeNativeGearbox()
            {
                var obj = EnsureSavedGearbox().gameObject; UnityEngine.Object.DestroyImmediate(SavedGearbox);
                var row = FindNativeRow("CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmInt("Type").Value = 0;
                data.FsmVariables.FindFsmInt("DamageType").Value = 3; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                data.FsmVariables.FindFsmFloat("OilLevel").Value = 4; data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedGearboxPart.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"];
                    var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++)
                        actions[i] = state.Name == "Update 2" && i < 3 || state.Name == "Remove part" && (i == 7 || i == 15 || i == 16)
                            ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state);
                    // Exercise the actual condition writers and detachment, with
                    // physics creation and global assembly notifications bounded.
                    state.Transitions = new FsmTransition[0];
                }
                SavedGearbox = data; Reader.FsmVariables.FindFsmGameObject("db_Gearbox").Value = obj; return data;
            }
            internal GearboxState CaptureGearbox() => (GearboxState)PacketCodec.Decode(PacketCodec.Encode((GearboxState)Call(Sync, "BuildGearboxState")!));
        }

        internal static void RunGearbox(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN130", "Starter", "gearbox-starter-input-probe.json"))
            {
                var action = f.Action("Check automatic", 0); var original = (FsmOwnerDefault)Get(action, "gameObject");
                var selector = f.ConfigureGearboxDecision(); var selectorAction = f.Action("Check automatic", 2);
                var selectorOwner = Get(selectorAction, "gameObject");
                check("gearbox inputs: missing host cannot use saved manual type", () =>
                {
                    action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 0, "Saved native cache did not warm.");
                    ((GearboxReplica)Get(f.Sync, "_gearboxReplica")).Clear();
                    Require(!f.Prepare() && !f.Reader.enabled && !f.SavedGearbox!.enabled, "Unknown gearbox bypassed native interlock.");
                    Require(ReferenceEquals(Get(selectorAction, "gameObject"), selectorOwner), "Protection rewrote local selector."); f.AssertSaved();
                });
                check("gearbox inputs: host arrival resumes without replaying selector or scratch", () =>
                {
                    var state = f.Reader.ActiveStateName; f.Reader.FsmVariables.FindFsmInt("Automatic").Value = 19;
                    f.SetGearbox(2); if (!f.Prepare()) f.Prepare();
                    Require(f.Reader.enabled && f.Reader.ActiveStateName == state && f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 19, "Arrival replayed a native decision.");
                    action.OnEnter(); Require(f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 2 && original.GameObject.Value == f.SavedGearbox!.gameObject
                        && f.Target(action) != original.GameObject.Value && f.Target(action).GetComponent<Rigidbody>() == null, "Host type did not replace saved cache."); f.AssertSaved();
                });
                foreach (int type in new[] { 0, 1, 2 })
                    foreach (string gear in new[] { "P", "N", "D", "R", "1", "2" })
                    {
                        int hostType = type; string localGear = gear;
                        check("gearbox inputs: native starter type=" + hostType + " selector=" + localGear, () =>
                        {
                            NativeBagPartChecks.Fire(selector, localGear); f.SetGearbox(hostType); Require(f.Prepare(), "Gearbox read failed."); f.Fire("Check automatic");
                            bool allowed = hostType <= 1 || localGear == "P" || localGear == "N";
                            Require(f.Reader.ActiveStateName == (allowed ? "Motor installed" : "ACC off"), "Native interlock decision differs from host type/local selector.");
                            Require(selector.FsmVariables.FindFsmString("GearLetter").Value == localGear && ReferenceEquals(Get(selectorAction, "gameObject"), selectorOwner), "Host input replaced local driver selector."); f.AssertSaved();
                        });
                    }
                check("gearbox inputs: blocked native attempt resumes only after host availability", () =>
                {
                    NativeBagPartChecks.Fire(selector, "D"); f.SetGearbox(0, false);
                    f.Reader.FsmVariables.FindFsmInt("Automatic").Value = 41; f.Fire("Check automatic");
                    Require(!f.Reader.enabled && f.Reader.ActiveStateName == "Check automatic" && f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 41,
                        "Unavailable type executed a pending native attempt.");
                    f.SetGearbox(2); Require(!f.Reader.enabled && f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 41, "Packet replayed the blocked attempt.");
                    if (!f.Prepare()) f.Prepare();
                    Require(f.Reader.enabled && f.Reader.ActiveStateName == "ACC off" && f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 2,
                        "Recovered native attempt bypassed automatic interlock."); f.AssertSaved();
                });
                check("gearbox inputs: unavailable stale and conflicting records cannot unlock starter", () =>
                {
                    f.SetGearbox(0, false); Require(!f.Prepare() && !f.Reader.enabled, "Unavailable type became manual.");
                    var stale = ((GearboxReplica)Get(f.Sync, "_gearboxReplica")).Get()!; stale.Flags = 1; stale.Type = 0;
                    Call(f.Sync, "OnGearboxState", stale); stale.Revision--; Call(f.Sync, "OnGearboxState", stale);
                    Require(!f.Prepare() && !f.Reader.enabled, "Stale state resumed starter.");
                    f.SetGearbox(2); if (!f.Prepare()) f.Prepare(); Require(f.Reader.enabled, "Fresh observation failed recovery."); f.AssertSaved();
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName;
                    check("gearbox inputs: changed " + field + " contains graph and repairs", () =>
                    {
                        var saved = Get(action, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmInt { Name = "Automatic", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } }
                            : (object)new FsmString { Value = "Other" };
                        Set(action, field, changed);
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Foreign gearbox signature escaped protection."); f.AssertSaved(); }
                        finally { Set(action, field, field == "gameObject" ? original : saved); if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Gearbox reader failed recovery.");
                    });
                }
                check("gearbox inputs: destroyed proxy and missing integer field repair", () =>
                {
                    if (!f.Prepare()) Require(f.Prepare(), "Reader did not settle after signature repair.");
                    var proxy = f.Target(action); proxy.GetComponent<PlayMakerFSM>().FsmVariables.IntVariables = new FsmInt[0];
                    f.Reader.FsmVariables.FindFsmInt("Automatic").Value = 41; f.Fire("Check automatic");
                    Require(!f.Reader.enabled && f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 41, "Missing proxy integer escaped native entry guard.");
                    if (!f.Prepare()) Require(f.Prepare(), "Proxy integer failed rebind.");
                    UnityEngine.Object.DestroyImmediate(f.Target(action)); Require(f.Prepare(), "Destroyed proxy did not repair."); action.OnEnter();
                    Require(f.Reader.FsmVariables.FindFsmInt("Automatic").Value == 2, "Repair lost host type."); f.AssertSaved();
                });
                check("gearbox inputs: disconnect restores original readers and clears host state", () =>
                {
                    Call(f.Sync, "ReleaseSession"); Require(((GearboxReplica)Get(f.Sync, "_gearboxReplica")).Get() == null
                        && ReferenceEquals(Get(action, "gameObject"), original) && ReferenceEquals(Get(selectorAction, "gameObject"), selectorOwner)
                        && !f.SavedGearbox!.enabled, "Disconnect lost gearbox ownership/protection."); f.AssertSaved();
                });
            }
            RunGearboxCapture(check);
        }

        private static void RunGearboxCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN130", "Starter", "gearbox-starter-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var data = f.MakeNativeGearbox(); var type = data.FsmVariables.FindFsmInt("Type"); var partType = f.SavedGearboxPart.FsmVariables.FindFsmInt("Type");
                var installed = data.FsmVariables.FindFsmBool("Installed");
                check("gearbox capture: native initialization and installation must finish", () =>
                {
                    Require(f.CaptureGearbox().Flags == 0, "Unstarted gearbox published defaults."); NativeBagPartChecks.Start(data);
                    NativeBagPartChecks.Fire(data, "Installed"); Require(f.CaptureGearbox().Flags == 0, "Intermediate gearbox was accepted.");
                    NativeBagPartChecks.Fire(data, "Update 2"); type.Value = partType.Value = 2;
                    Require(f.CaptureGearbox().Flags == 1 && f.CaptureGearbox().Type == 2, "Stable host gearbox missing.");
                });
                foreach (string stateName in new[] { "Install 1", "Install 2", "State 1", "Allow removal?", "Remove other", "Check Flywheel" })
                {
                    string state = stateName;
                    check("gearbox capture: transition " + state + " remains unavailable", () =>
                    { NativeBagPartChecks.Fire(data, state); Require(f.CaptureGearbox().Flags == 0 && f.CaptureGearbox().Type == 0, "Intermediate gearbox exposed a type."); });
                }
                check("gearbox capture: snapshots preserve changes and native idle retains its type", () =>
                {
                    NativeBagPartChecks.Fire(data, "Update 2"); var first = f.CaptureGearbox(); var publication = (GearboxPublication)Get(f.Sync, "_gearboxPublication");
                    publication.MarkBroadcast(first.Revision); type.Value = partType.Value = 1; var snapshot = f.CaptureGearbox();
                    Require(snapshot.Revision == first.Revision + 1 && publication.NeedsBroadcast && f.CaptureGearbox().Revision == snapshot.Revision, "Join swallowed live type change.");
                    installed.Value = false; NativeBagPartChecks.Fire(data, "Idle"); var idle = f.CaptureGearbox();
                    Require(idle.Flags == 1 && idle.Type == 1, "Removed transmission invented a new native type.");
                    installed.Value = true; NativeBagPartChecks.Fire(data, "Update 2");
                });
                foreach (string faultName in new[] { "inactive", "part inactive", "parent", "assembly", "duplicate", "part type", "active part", "missing type" })
                {
                    string fault = faultName;
                    check("gearbox capture: " + fault + " fails locally and recovers", () =>
                    {
                        PlayMakerFSM? duplicate = null; var ints = data.FsmVariables.IntVariables;
                        if (fault == "inactive") data.gameObject.SetActive(false);
                        if (fault == "part inactive") f.SavedGearboxPart.gameObject.SetActive(false);
                        if (fault == "parent") f.SavedGearboxPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedGearboxPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate") duplicate = Empty(data.gameObject, "Data");
                        if (fault == "part type") partType.Value = 2;
                        if (fault == "active part") data.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing type") data.FsmVariables.IntVariables = new FsmInt[0];
                        try { Require(f.CaptureGearbox().Flags == 0 && f.CaptureGearbox().Type == 0, "Broken gearbox kept valid type."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                            data.FsmVariables.IntVariables = ints; partType.Value = type.Value = 1;
                            data.gameObject.SetActive(true); f.SavedGearboxPart.gameObject.SetActive(true); f.SavedGearboxPart.transform.parent = data.transform;
                            f.SavedGearboxPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1; data.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedGearboxPart.gameObject;
                            NativeBagPartChecks.Fire(data, "Update 2");
                        }
                        Require(f.CaptureGearbox().Flags == 1, "Repaired gearbox remained unavailable.");
                    });
                }
                check("gearbox protection: native update writes wear oil and integer damage and removal detaches", () =>
                {
                    NativeBagPartChecks.Fire(data, "Idle"); data.FsmVariables.FindFsmFloat("Wear").Value = 65;
                    data.FsmVariables.FindFsmFloat("OilLevel").Value = 2; data.FsmVariables.FindFsmInt("DamageType").Value = 9;
                    NativeBagPartChecks.Fire(data, "Update 2");
                    Require(f.SavedGearboxPart.FsmVariables.FindFsmFloat("Wear").Value == 65 && f.SavedGearboxPart.FsmVariables.FindFsmFloat("OilLevel").Value == 2
                        && f.SavedGearboxPart.FsmVariables.FindFsmInt("DamageType").Value == 9, "Native update writers were not exercised.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = 64; NativeBagPartChecks.Fire(data, "Remove part");
                    Require(!installed.Value && f.SavedGearboxPart.transform.parent == null && f.SavedGearboxPart.FsmVariables.FindFsmFloat("Wear").Value == 64, "Native removal did not write and detach.");
                    f.SavedGearboxPart.transform.parent = data.transform; installed.Value = true; type.Value = partType.Value = 0;
                    foreach (var target in new[] { data, f.SavedGearboxPart })
                    { target.FsmVariables.FindFsmFloat("Wear").Value = 77; target.FsmVariables.FindFsmFloat("OilLevel").Value = 4; target.FsmVariables.FindFsmInt("DamageType").Value = 3; }
                    NativeBagPartChecks.Fire(data, "Update 2"); f.AssertSaved();
                });
                check("gearbox capture: hosts reject peer observations and guests cannot publish", () =>
                {
                    Call(f.Sync, "ClearGearbox"); Call(f.Sync, "OnGearboxState", new GearboxState { Revision = 500, Flags = 1, Type = 2 });
                    Require(((GearboxReplica)Get(f.Sync, "_gearboxReplica")).Get() == null, "Host accepted peer transmission.");
                    Property(f.Session, "IsHost", false); Require(Call(f.Sync, "BuildGearboxState") == null, "Guest published its saved gearbox.");
                    f.SetGearbox(2);
                });
                check("gearbox protection: admission stops update and removal through disconnect", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    data.FsmVariables.FindFsmFloat("Wear").Value = 12; data.FsmVariables.FindFsmFloat("OilLevel").Value = 1;
                    data.FsmVariables.FindFsmInt("DamageType").Value = 9;
                    Require(f.Prepare() && !data.enabled && !data.Fsm.RestartOnEnable, "Guest transmission remained active.");
                    data.Fsm.Update(); NativeBagPartChecks.Fire(data, "Update 2"); NativeBagPartChecks.Fire(data, "Remove part");
                    Require(f.SavedGearboxPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedGearboxPart.FsmVariables.FindFsmFloat("OilLevel").Value == 4
                        && f.SavedGearboxPart.FsmVariables.FindFsmInt("DamageType").Value == 3, "Paused mount still copied native condition.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = 77; data.FsmVariables.FindFsmFloat("OilLevel").Value = 4;
                    data.FsmVariables.FindFsmInt("DamageType").Value = 3; f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); data.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(data, "Remove part");
                    Require(!data.enabled, "Disconnect resumed saved transmission."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
