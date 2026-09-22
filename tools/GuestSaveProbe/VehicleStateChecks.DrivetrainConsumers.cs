using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private sealed class DrivetrainQuiet : FsmStateAction { public override void OnEnter() { Finish(); } }
        private sealed class DetachProbe : FsmStateAction { internal int Count; public override void OnEnter() { Count++; Finish(); } }

        private static void RunDrivetrainConsumerChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode)
        {
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var profile = catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var property = policy.GetType().GetProperty("ProtectWorld", Members); bool priorProtection = (bool)property.GetValue(policy, null);
            Action<bool> protect = value => property.GetSetMethod(true).Invoke(policy, new object[] { value });
            Func<bool> prepare = () => (bool)guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true });
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items"); uint oldId = (uint)Get(item, "Id"), id = StableHash.Fnv1a32("vehicle:CORRIS");
            var oldBody = Get(item, "Body"); var oldPath = Get(item, "Path");
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            try
            {
                reset(); protect(false);
                using (var f = new DifferentialFixture(true))
                {
                    items.Remove(oldId); items[id] = item; Set(item, "Id", id); Set(item, "Path", "CORRIS"); Set(item, "Body", f.Body);
                    var gearData = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
                    var damage = new FsmInt { Name = "DamageType", UseVariable = true, Value = 7 }; gearData.FsmVariables.IntVariables = new[] { damage };
                    var oil = new FsmFloat { Name = "OilLevel", UseVariable = true, Value = 1 };
                    gearData.FsmVariables.FloatVariables = new[] { gearData.FsmVariables.FindFsmFloat("Wear"), oil };
                    foreach (object paused in (IEnumerable)Get(profile, "PausedFsms"))
                        if ((string)Get(paused, "Path") == "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox")
                        {
                            var states = new List<FsmState>(gearData.Fsm.States);
                            foreach (string name in (string[])Get(paused, "RequiredStates"))
                                if (!states.Exists(s => s.Name == name)) states.Add(new FsmState(gearData.Fsm) { Name = name, Actions = new FsmStateAction[0] });
                            gearData.Fsm.States = states.ToArray();
                        }
                    var shaftData = f.Fsm.FsmVariables.FindFsmGameObject("db_Driveshaft").Value.GetComponent<PlayMakerFSM>();
                    var detached = new DetachProbe(); var detachedState = new FsmState(shaftData.Fsm) { Name = "Detached", Actions = new FsmStateAction[] { detached } };
                    detached.Init(detachedState); shaftData.Fsm.States = new List<FsmState>(shaftData.Fsm.States) { detachedState }.ToArray();
                    shaftData.Fsm.GlobalTransitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("BREAKOFF"), ToState = "Detached" } };
                    var consumers = new List<PlayMakerFSM>(); var rules = new List<object>();
                    foreach (object rule in (IEnumerable)Get(profile, "Writers"))
                    {
                        if (((IList)Get(rule, "DrivetrainWearReads")).Count == 0) continue;
                        rules.Add(rule); consumers.Add(MakeDrivetrainConsumer(f, rule));
                    }
                    var transmission = consumers[0]; var automatic = consumers[1];
                    var setDamage = NativeBagPartChecks.State(transmission, "Gearbox damage").Actions[5];
                    var detach = NativeBagPartChecks.State(transmission, "Shaft break").Actions[0];
                    transmission.FsmVariables.FindFsmInt("Damage").Value = 2;
                    automatic.FsmVariables.FindFsmFloat("OilLeakRate").Value = .125f;
                    Action<PlayMakerFSM, string> fire = (fsm, state) => { NativeBagPartChecks.Fire(fsm, "Probe idle"); NativeBagPartChecks.Fire(fsm, state); };
                    uint revision = 0;
                    Action<float, float, float> receive = (shaft, gear, axle) =>
                    {
                        var state = new VehicleDrivetrainWearState { VehicleId = id, Revision = ++revision, Flags = 1,
                            DriveshaftWear = shaft, GearboxWear = gear, RearAxleWear = axle, GearboxOilAvailable = true, GearboxOilLevel = 6.3f };
                        Require((bool)Call(vehicles, "OnVehicleDrivetrainWearState", state)!, "Host wear rejected."); prepare();
                    };
                    const string prefix = "drivetrain consumers: ";
                    check(prefix + "native solo readers writers and failure event execute before protection", () =>
                    {
                        fire(transmission, "Gearbox damage"); Require(damage.Value == 2 && transmission.FsmVariables.FindFsmFloat("Wear").Value == 90, "Native integer write/read baseline failed.");
                        fire(automatic, "State 1"); Require(oil.Value == .875f, "Native oil drain baseline failed.");
                        fire(transmission, "Shaft break"); Require(detached.Count == 1, "Native BREAKOFF baseline failed.");
                        damage.Value = 7; oil.Value = 1;
                    });
                    check(prefix + "missing host wear pauses registered consumers after preserving saved actions", () =>
                    {
                        mode(false, 1); protect(true); prepare();
                        Require(!transmission.enabled && !automatic.enabled && !setDamage.Enabled && !detach.Enabled, "Unseeded consumers remained active.");
                        Require(damage.Value == 7 && oil.Value == 1 && detached.Count == 1, "Admission changed saved parts.");
                    });
                    var speeds = new[] { 0f, .9999f, 1f, 6.9999f, 7f, 14.9999f, 15f, 100f, -1f };
                    foreach (float wear in speeds)
                        check(prefix + "all four native readers use exact host wear " + wear, () =>
                        {
                            receive(wear, wear + .125f, wear - .125f); Require(transmission.enabled && automatic.enabled, "Seeded consumers did not recover.");
                            for (int group = 0; group < consumers.Count; group++)
                                foreach (object read in (IEnumerable)Get(rules[group], "DrivetrainWearReads"))
                                {
                                    var fsm = consumers[group]; var action = NativeBagPartChecks.State(fsm, (string)Get(read, "State")).Actions[(int)Get(read, "Index")];
                                    var target = Get(action, "gameObject"); var cachedGo = Get(action, "goLastFrame"); var cachedFsm = Get(action, "fsm");
                                    fire(fsm, (string)Get(read, "State"));
                                    int part = (int)Get(read, "Part"); float expected = part == 0 ? wear : part == 1 ? wear + .125f : wear - .125f;
                                    Require(((FsmFloat)Get(action, "storeValue")).Value == expected, "Wrong host wear on native reader.");
                                    Require(ReferenceEquals(Get(action, "gameObject"), target) && ReferenceEquals(Get(action, "goLastFrame"), cachedGo)
                                        && ReferenceEquals(Get(action, "fsm"), cachedFsm), "Projection changed native source or cache.");
                                }
                            f.Unchanged(); Require(damage.Value == 7 && oil.Value == 1 && detached.Count == 1, "Shared wear mutated saved parts.");
                        });
                    check(prefix + "driver handoff local ownership and parking preserve host input", () =>
                    {
                        receive(23, 24, 25);
                        foreach (byte owner in new byte[] { 1, 2, 255 })
                            foreach (bool local in new[] { false, true })
                            { mode(false, owner); Set(item, "LocallyOwned", local); fire(transmission, "Driveshaft"); Require(transmission.FsmVariables.FindFsmFloat("Wear").Value == 23, "Driving lease replaced host wear."); }
                        Set(item, "LocallyOwned", false);
                    });
                    check(prefix + "withdrawal pauses consumers and newer state recovers without replaying saved actions", () =>
                    {
                        Call(vehicles, "OnVehicleDrivetrainWearState", new VehicleDrivetrainWearState { VehicleId = id, Revision = ++revision });
                        Require(!transmission.enabled && !automatic.enabled, "Withdrawal left old host wear active.");
                        receive(10, 11, 12); fire(transmission, "Shaft break"); fire(automatic, "State 3");
                        Require(detached.Count == 1 && oil.Value == 1 && damage.Value == 7, "Recovery replayed a saved action.");
                    });
                    check(prefix + "reenabled and direct failure calls remain blocked", () =>
                    {
                        setDamage.Enabled = true; detach.Enabled = true; fire(transmission, "Gearbox damage"); fire(transmission, "Shaft break");
                        setDamage.OnEnter(); detach.OnEnter();
                        Require(!setDamage.Enabled && !detach.Enabled && damage.Value == 7 && detached.Count == 1, "Native direct or reenabled failure escaped protection.");
                    });
                    foreach (string field in new[] { "variableName", "fsmName", "everyFrame", "storeValue" })
                        check(prefix + "changed reader " + field + " pauses and repairs", () =>
                        {
                            var action = NativeBagPartChecks.State(transmission, "Driveshaft").Actions[2]; var prior = Get(action, field);
                            Set(action, field, field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat(0) : new FsmString { Value = "Changed" });
                            try { fire(transmission, "Driveshaft"); Require(!transmission.enabled, "Changed reader was admitted."); f.Unchanged(); }
                            finally { Set(action, field, prior); }
                            prepare(); Require(transmission.enabled, "Repaired reader remained paused.");
                        });
                    check(prefix + "aliased saved and global reader outputs never receive host wear", () =>
                    {
                        var action = NativeBagPartChecks.State(transmission, "Driveshaft").Actions[2]; var output = (FsmFloat)Get(action, "storeValue");
                        var globals = FsmVariables.GlobalVariables.FloatVariables; float before = output.Value;
                        FsmVariables.GlobalVariables.FloatVariables = new List<FsmFloat>(globals) { output }.ToArray();
                        try { fire(transmission, "Driveshaft"); Require(!transmission.enabled && output.Value == before, "Global output was written."); }
                        finally { FsmVariables.GlobalVariables.FloatVariables = globals; prepare(); }
                        var saved = gearData.FsmVariables.FloatVariables; gearData.FsmVariables.FloatVariables = new List<FsmFloat>(saved) { output }.ToArray();
                        try { fire(transmission, "Driveshaft"); Require(!transmission.enabled && output.Value == before, "Saved alias was written."); }
                        finally { gearData.FsmVariables.FloatVariables = saved; prepare(); }
                    });
                    foreach (string fault in new[] { "renamed FSM", "moved consumer", "missing protection profile", "unbound consumer" })
                        check(prefix + "direct read cannot bypass " + fault + " protection", () =>
                        {
                            var action = NativeBagPartChecks.State(transmission, "Driveshaft").Actions[2];
                            var output = (FsmFloat)Get(action, "storeValue"); float before = output.Value;
                            string name = transmission.FsmName, objectName = transmission.gameObject.name;
                            var metadata = catalog.GetProperty("GuestEngineProtection", Static);
                            var bindings = (IDictionary)guard.GetField("Bindings", Static).GetValue(null);
                            var binding = bindings[transmission];
                            if (fault == "renamed FSM") transmission.FsmName = "Unrelated reader";
                            if (fault == "moved consumer") transmission.gameObject.name = "Moved drivetrain";
                            if (fault == "missing protection profile") metadata.GetSetMethod(true).Invoke(null, new object[] { null! });
                            if (fault == "unbound consumer") bindings.Remove(transmission);
                            try
                            {
                                action.OnEnter();
                                Require(!transmission.enabled && !transmission.Fsm.RestartOnEnable && output.Value == before,
                                    "Direct callback escaped its changed consumer protection.");
                                f.Unchanged(); Require(damage.Value == 7 && oil.Value == 1 && detached.Count == 1, "Direct callback changed saved parts.");
                            }
                            finally
                            {
                                transmission.FsmName = name; transmission.gameObject.name = objectName;
                                metadata.GetSetMethod(true).Invoke(null, new[] { profile }); bindings[transmission] = binding;
                                prepare();
                            }
                            Require(transmission.enabled, "Repaired direct reader did not recover.");
                        });
                    foreach (string field in new[] { "delay", "sendEvent", "everyFrame" })
                        check(prefix + "changed failure event " + field + " pauses before dispatch and recovers", () =>
                        {
                            var prior = Get(detach, field); Set(detach, field, field == "delay" ? (object)new FsmFloat(2) : field == "everyFrame" ? true : new FsmString { Value = "OTHER" });
                            try { fire(transmission, "Shaft break"); Require(!transmission.enabled && detached.Count == 1, "Changed event escaped guard."); }
                            finally { Set(detach, field, prior); }
                            prepare(); Require(transmission.enabled && detached.Count == 1, "Event repair dispatched a failure.");
                        });
                    check(prefix + "external integer cache cannot write a remembered renamed gearbox", () =>
                    {
                        string name = gearData.gameObject.name; var fsmName = Get(setDamage, "fsmName");
                        gearData.gameObject.name = "renamed saved gearbox"; Set(setDamage, "fsmName", new FsmString { Value = "Other" });
                        try { setDamage.OnEnter(); Require(damage.Value == 7, "Warm cached protected integer destination was written."); }
                        finally { gearData.gameObject.name = name; Set(setDamage, "fsmName", fsmName); prepare(); }
                    });
                    check(prefix + "unprotected integer destination still accepts native writes", () =>
                    {
                        var scratch = new FsmInt { Name = "DamageType", UseVariable = true, Value = 9 };
                        var oldInts = shaftData.FsmVariables.IntVariables; shaftData.FsmVariables.IntVariables = new[] { scratch };
                        var target = (FsmOwnerDefault)Get(setDamage, "gameObject"); var oldTarget = target.GameObject.Value;
                        try { target.GameObject.Value = shaftData.gameObject; setDamage.OnEnter(); Require(scratch.Value == 2 && damage.Value == 7, "Integer guard blocked an unrelated destination."); }
                        finally { target.GameObject.Value = oldTarget; shaftData.FsmVariables.IntVariables = oldInts; prepare(); }
                    });
                    check(prefix + "replaced failure action cannot dispatch through a retained callback", () =>
                    {
                        var state = NativeBagPartChecks.State(transmission, "Shaft break");
                        var row = NativeBagPartChecks.Find(f.Rows, (string)Get(rules[0], "Path"), "Transmission");
                        Dictionary<string, object>? raw = null;
                        foreach (Dictionary<string, object> candidate in (IEnumerable)row["states"])
                            if ((string)candidate["name"] == state.Name) raw = (Dictionary<string, object>)((IList)candidate["actions"])[0];
                        var replacement = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { raw!, transmission });
                        replacement.Init(state); state.Actions[0] = replacement;
                        try { prepare(); detach.OnEnter(); replacement.OnEnter(); Require(transmission.enabled && !replacement.Enabled && detached.Count == 1, "Replaced failure callback escaped protection."); }
                        finally { state.Actions[0] = detach; prepare(); }
                    });
                    check(prefix + "missing wear profile pauses registered consumers until repair", () =>
                    {
                        var metadata = catalog.GetProperty("VehicleDrivetrainWear", Static); var old = metadata.GetValue(null, null);
                        metadata.GetSetMethod(true).Invoke(null, new object[] { null! });
                        try { prepare(); Require(!transmission.enabled && !automatic.enabled, "Missing metadata admitted saved guest wear."); }
                        finally { metadata.GetSetMethod(true).Invoke(null, new[] { old }); prepare(); }
                        Require(transmission.enabled && automatic.enabled, "Restored metadata did not recover consumers.");
                    });
                    RunGearboxOilConsumerChecks(check, f, transmission, automatic, prepare, fire, (oilValue, available) =>
                    {
                        Require((bool)Call(vehicles, "OnVehicleDrivetrainWearState", new VehicleDrivetrainWearState {
                            VehicleId = id, Revision = ++revision, Flags = 1, DriveshaftWear = 9, GearboxWear = 10, RearAxleWear = 11,
                            GearboxOilAvailable = available, GearboxOilLevel = available ? oilValue : 0 })!, "Host oil rejected.");
                    }, mode, item);
                    check(prefix + "disconnect retains saved guards while native reader lookup resumes", () =>
                    {
                        SetProperty(session, "State", SessionState.Idle); prepare(); fire(transmission, "Gearbox damage"); fire(transmission, "Shaft break");
                        Require(transmission.FsmVariables.FindFsmFloat("Wear").Value == 90 && damage.Value == 7 && detached.Count == 1, "Disconnected native lookup or saved protection changed.");
                        mode(false, 1);
                    });
                    protect(false);
                }
            }
            finally
            {
                Call(vehicles, "ClearVehicleStateStreams"); items.Remove(id); items[oldId] = item; Set(item, "Id", oldId); Set(item, "Body", oldBody); Set(item, "Path", oldPath);
                protect(priorProtection); reset();
            }
        }

        private static PlayMakerFSM MakeDrivetrainConsumer(DifferentialFixture f, object rule)
        {
            string name = (string)Get(rule, "Fsm"), path = (string)Get(rule, "Path");
            var obj = f.Fsm.gameObject;
            if (name == "3 speed") { obj = new GameObject("GearboxAutomatic"); obj.transform.SetParent(f.Fsm.transform, false); }
            var row = NativeBagPartChecks.Find(f.Rows, path, name); var fsm = NativeBagPartChecks.MakeFsm(obj, row);
            foreach (var reference in fsm.FsmVariables.GameObjectVariables)
            {
                string variable = reference.Name == "db_Rearaxle" ? "db_RearAxle" : reference.Name;
                reference.Value = f.Fsm.FsmVariables.FindFsmGameObject(variable)?.Value;
            }
            fsm.Fsm.Init(fsm);
            var states = new List<FsmState>(fsm.Fsm.States) { new FsmState(fsm.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0] } };
            fsm.Fsm.States = states.ToArray(); fsm.Fsm.StartState = "Probe idle";
            foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
            {
                string stateName = (string)stateRow["name"]; var state = NativeBagPartChecks.State(fsm, stateName); var actions = new List<FsmStateAction>();
                int index = 0;
                foreach (Dictionary<string, object> raw in (IEnumerable)stateRow["actions"])
                {
                    bool selected = false;
                    foreach (string list in new[] { "Actions", "EventActions", "DrivetrainWearReads" })
                        foreach (object candidate in (IEnumerable)Get(rule, list)) selected |= (string)Get(candidate, "State") == stateName && (int)Get(candidate, "Index") == index;
                    var oil = Get(rule, "GearboxOilRead");
                    selected |= oil != null && (string)Get(oil, "State") == stateName && (int)Get(oil, "Index") == index;
                    var action = selected || ((string)raw["type"]).EndsWith(".FloatCompare", StringComparison.Ordinal)
                        ? (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { raw, fsm }) : new DrivetrainQuiet();
                    actions.Add(action); index++;
                }
                state.Actions = actions.ToArray(); state.Transitions = new FsmTransition[0]; foreach (var action in state.Actions) action.Init(state);
            }
            NativeBagPartChecks.Start(fsm); return fsm;
        }
    }
}
