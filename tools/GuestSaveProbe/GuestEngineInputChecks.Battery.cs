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
            internal PlayMakerFSM? SavedBattery;
            internal PlayMakerFSM SavedBatteryPart = null!;
            private uint _batteryRevision;
            private PlayMakerFSM EnsureSavedBattery()
            {
                if (SavedBattery != null) return SavedBattery;
                SavedBattery = Empty(PathObject(Car, "CORRIS/Assemblies/VINP_Battery"), "Data");
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                var protection = catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null);
                foreach (object rule in (IEnumerable)Get(protection, "PausedFsms"))
                    if ((string)Get(rule, "Path") == "CORRIS/Assemblies/VINP_Battery")
                    {
                        var states = new List<FsmState>();
                        foreach (string name in (string[])Get(rule, "RequiredStates"))
                            states.Add(new FsmState(SavedBattery.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                        SavedBattery.Fsm.States = states.ToArray(); SavedBattery.Fsm.StartState = "Idle";
                    }
                SavedBatteryPart = Data(Child(SavedBattery.gameObject, "saved battery"), null, 1);
                SavedBatteryPart.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = 1 } };
                SetBatteryFields(SavedBattery); SetBatteryFields(SavedBatteryPart);
                SavedBattery.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedBatteryPart.gameObject) };
                return SavedBattery;
            }
            internal static void SetBatteryFields(PlayMakerFSM data)
            {
                var floats = new List<FsmFloat>(data.FsmVariables.FloatVariables);
                foreach (string name in new[] { "Charge", "ChargeMax", "MaxVoltage", "DischargeRate" })
                {
                    var value = data.FsmVariables.FindFsmFloat(name);
                    if (value == null) { value = new FsmFloat { Name = name, UseVariable = true }; floats.Add(value); }
                    value.Value = name == "Charge" ? 77.25f : name == "DischargeRate" ? .1f : 127;
                }
                data.FsmVariables.FloatVariables = floats.ToArray();
                var installed = data.FsmVariables.FindFsmBool("Installed");
                if (installed == null) { installed = new FsmBool { Name = "Installed", UseVariable = true }; data.FsmVariables.BoolVariables = new[] { installed }; }
                installed.Value = true;
            }
            internal void SetBattery(bool installed, float charge, bool available = true)
            {
                var state = new BatteryState { Revision = ++_batteryRevision,
                    Flags = available ? (byte)(BatteryState.Available | (installed ? BatteryState.Installed : 0)) : (byte)0,
                    Charge = available && installed ? charge : 0 };
                Call(Sync, "OnBatteryState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((BatteryReplica)Get(Sync, "_batteryReplica")).Get()?.Revision == _batteryRevision, "Fixture battery state rejected.");
            }
            private void AssertSavedBattery()
            {
                if (SavedBattery == null) return;
                foreach (var data in new[] { SavedBattery, SavedBatteryPart })
                    Require(data.FsmVariables.FindFsmBool("Installed").Value && data.FsmVariables.FindFsmFloat("Charge").Value == 77.25f
                        && data.FsmVariables.FindFsmFloat("ChargeMax").Value == 127 && data.FsmVariables.FindFsmFloat("DischargeRate").Value == .1f,
                        "Engine simulation changed saved battery values.");
                Require(SavedBattery.FsmVariables.FindFsmGameObject("ActivePart").Value == SavedBatteryPart.gameObject
                    && SavedBatteryPart.transform.parent == SavedBattery.transform, "Engine input moved the saved battery.");
            }
            internal PlayMakerFSM MakeNativeBattery()
            {
                var obj = EnsureSavedBattery().gameObject; UnityEngine.Object.DestroyImmediate(SavedBattery); SavedBattery = null;
                var row = FindNativeRow("CORRIS/Assemblies/VINP_Battery", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row);
                data.Fsm.Init(data); SetBatteryFields(data);
                data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedBatteryPart.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]);
                    var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++)
                        actions[i] = state.Name == "Delay 2" || state.Name == "Died battery" ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state);
                    // Physical installation is a separate fixture boundary. Real drain
                    // and degradation actions run once without the native physics loop.
                    state.Transitions = new FsmTransition[0];
                }
                SavedBattery = data;
                Reader.FsmVariables.FindFsmGameObject("db_Battery").Value = obj;
                return data;
            }
            internal BatteryState CaptureBattery() => (BatteryState)PacketCodec.Decode(PacketCodec.Encode((BatteryState)Call(Sync, "BuildBatteryState")!));
            internal void ConfigureBatteryDecisions()
            {
                foreach (Dictionary<string, object> row in (IEnumerable)_row["states"])
                    if ((string)row["name"] == "Wiring" || (string)row["name"] == "Battery")
                    {
                        var state = NativeBagPartChecks.State(Reader, (string)row["name"]); var raw = (List<object>)row["actions"];
                        var actions = new FsmStateAction[raw.Count];
                        for (int i = 0; i < actions.Length; i++) actions[i] = NativeAction((Dictionary<string, object>)raw[i], Reader);
                        state.Actions = actions; foreach (var action in actions) action.Init(state);
                        RestoreNativeTransitions(state.Name);
                    }
                var running = NativeBagPartChecks.State(Reader, "Engine running?");
                foreach (Dictionary<string, object> row in (IEnumerable)_row["states"])
                    if ((string)row["name"] == running.Name)
                    { running.Actions[2] = NativeAction((Dictionary<string, object>)((List<object>)row["actions"])[2], Reader); running.Actions[2].Init(running); }
            }
        }

        internal static void RunBattery(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN133", "Electrics", "battery-engine-input-probe.json"))
            {
                var installed = f.Reader.FsmVariables.FindFsmBool("Battery"); var charge = f.Reader.FsmVariables.FindFsmFloat("Charge");
                var volts = f.Reader.FsmVariables.FindFsmFloat("Volts"); var shared = f.Reader.FsmVariables.FindFsmGameObject("db_Battery");
                check("battery inputs: warmed saved caches become inert host-only readers", () =>
                {
                    var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
                    var protection = policy.GetType().GetProperty("ProtectWorld", Members); object previous = protection.GetValue(policy, null);
                    try
                    {
                        protection.GetSetMethod(true).Invoke(policy, new object[] { false });
                        f.Action("Wiring", 0).OnEnter(); f.Action("Battery", 0).OnEnter(); f.Action("Engine running?", 1).OnEnter();
                        Require(installed.Value && charge.Value == 77.25f && volts.Value == 77.25f, "Native saved battery cache did not warm.");
                    }
                    finally { protection.GetSetMethod(true).Invoke(policy, new[] { previous }); }
                    ((BatteryReplica)Get(f.Sync, "_batteryReplica")).Clear(); Require(f.Prepare(), "Battery preparation failed.");
                    f.Action("Wiring", 0).OnEnter(); f.Action("Battery", 0).OnEnter(); f.Action("Engine running?", 1).OnEnter();
                    Require(!installed.Value && charge.Value == 0 && volts.Value == 0, "Missing host borrowed saved battery.");
                    var proxy = f.Target(f.Action("Battery", 0));
                    Require(proxy != shared.Value && proxy.GetComponent<Rigidbody>() == null && shared.Value == f.SavedBattery!.gameObject,
                        "Battery projection changed shared or physical ownership."); f.AssertSaved();
                });
                foreach (float value in new[] { 127f, 109.25f, 0f, -.25f })
                {
                    float host = value;
                    check("battery inputs: host charge " + host + " waits for each native read", () =>
                    {
                        float before = charge.Value; var proxy = f.Target(f.Action("Battery", 0));
                        f.SetBattery(true, host); Require(f.Prepare(), "Charge update failed.");
                        Require(charge.Value == before && f.Target(f.Action("Battery", 0)) == proxy, "Packet replayed charge calculation.");
                        f.Action("Wiring", 0).OnEnter(); f.Action("Battery", 0).OnEnter(); f.Action("Engine running?", 1).OnEnter();
                        Require(installed.Value && charge.Value == host && volts.Value == host, "Repeated battery reads disagree."); f.AssertSaved();
                    });
                }
                check("battery inputs: removal unavailable stale and conflict cannot retain power", () =>
                {
                    foreach (bool available in new[] { true, false })
                    {
                        f.SetBattery(false, 0, available); f.Prepare(); f.Action("Wiring", 0).OnEnter(); f.Action("Battery", 0).OnEnter();
                        Require(!installed.Value && charge.Value == 0, "Removed/unavailable battery retained power.");
                        var state = ((BatteryReplica)Get(f.Sync, "_batteryReplica")).Get()!; state.Flags = 3; state.Charge = 127;
                        Call(f.Sync, "OnBatteryState", state); state.Revision--; Call(f.Sync, "OnBatteryState", state);
                        f.Prepare(); f.Action("Wiring", 0).OnEnter(); Require(!installed.Value, "Conflicting or stale record restored battery.");
                    }
                    f.SetBattery(true, 127); f.Prepare(); f.Action("Wiring", 0).OnEnter(); Require(installed.Value, "Battery repair did not recover."); f.AssertSaved();
                });
                check("battery inputs: native voltage conversion reads host charge", () =>
                {
                    f.ConfigureBatteryDecisions(); f.SetBattery(true, 126.5f); Require(f.Prepare(), "Native decisions did not rebind.");
                    f.Action("Engine running?", 1).OnEnter(); f.Action("Engine running?", 2).OnEnter();
                    Require(Near(volts.Value, 12.65f), "Native divide-by-ten voltage changed."); f.AssertSaved();
                });
                foreach (int maskValue in new[] { 0, 1, 3, 7, 15 })
                {
                    int mask = maskValue;
                    check("battery inputs: native powered wiring gate " + mask, () =>
                    {
                        f.SetBattery((mask & 1) != 0, 127); f.SetWire(3, false, (mask & 2) != 0);
                        f.SetWire(4, false, (mask & 4) != 0); f.SetWire(5, (mask & 8) != 0, false);
                        ((FsmFloat)Get(f.Action("Battery", 1), "floatValue")).Value = 0;
                        f.Reader.FsmVariables.FindFsmFloat("VoltageLimit").Value = 84;
                        Require(f.Prepare(), "Powered wiring gate did not bind."); f.Fire("Wiring");
                        Require(f.Reader.ActiveStateName == (mask == 15 ? "Fuel gauge" : "Not Ok"), "Host installed battery or wiring gate was bypassed."); f.AssertSaved();
                    });
                }
                foreach (float value in new[] { 83f, 85f, 110f })
                {
                    float host = value;
                    check("battery inputs: native cold-adjusted charge gate " + host, () =>
                    {
                        f.SetBattery(true, host); f.Prepare(); ((FsmFloat)Get(f.Action("Battery", 1), "floatValue")).Value = -30;
                        f.Fire("Battery"); Require(Near(charge.Value, host - 20), "Native cold clamp was replaced.");
                        Require(f.Reader.ActiveStateName == (host - 20 > 84 ? "Fuel gauge" : "Not Ok"), "Native voltage gate ignored host charge."); f.AssertSaved();
                    });
                }
                check("battery inputs: proxy destruction repairs without resuming saved simulation", () =>
                {
                    UnityEngine.Object.DestroyImmediate(f.Target(f.Action("Battery", 0))); f.SetBattery(true, 122); Require(f.Prepare(), "Battery proxy did not repair.");
                    f.Action("Battery", 0).OnEnter(); Require(charge.Value == 122 && !f.SavedBattery!.enabled, "Proxy repair resumed saved battery."); f.AssertSaved();
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame" })
                {
                    string field = fieldName;
                    check("battery inputs: changed " + field + " contains the consumer and repairs", () =>
                    {
                        var action = f.Action("Battery", 0); var original = Get(action, field);
                        object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "Volts", UseVariable = true }
                            : new FsmString { Value = "Other", UseVariable = false };
                        Set(action, field, changed);
                        try { Require(!f.Prepare() && !f.Reader.enabled && !f.SavedBattery!.enabled, "Changed battery signature escaped protection."); f.AssertSaved(); }
                        finally { Set(action, field, original); if (!f.Prepare()) f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired battery reader remained paused."); action.OnEnter(); Require(charge.Value == 122, "Repaired reader lost host charge.");
                    });
                }
                check("battery inputs: disconnect clears host charge while saved battery stays paused", () =>
                {
                    Call(f.Sync, "ReleaseSession"); Require(((BatteryReplica)Get(f.Sync, "_batteryReplica")).Get() == null, "Host charge survived disconnect.");
                    Require(f.Target(f.Action("Battery", 0)) == shared.Value && !f.SavedBattery!.enabled, "Disconnect changed saved ownership/protection."); f.AssertSaved();
                });
            }
            RunBatteryCapture(check);
        }

        private static void RunBatteryCapture(Action<string, Action> check)
        {
            var saveGuard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
            var policy = saveGuard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool wasProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN133", "Electrics", "battery-engine-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var data = f.MakeNativeBattery(); var installed = data.FsmVariables.FindFsmBool("Installed"); var charge = data.FsmVariables.FindFsmFloat("Charge");
                check("battery capture: native initialization and attachment must finish", () =>
                {
                    Require(f.CaptureBattery().Flags == 0, "Unstarted battery published defaults."); NativeBagPartChecks.Start(data);
                    NativeBagPartChecks.Fire(data, "Install 2"); Require(f.CaptureBattery().Flags == 0, "Half-installed battery powered guests.");
                    NativeBagPartChecks.Fire(data, "Check joint"); charge.Value = 126.75f;
                    Require(f.CaptureBattery().Flags == 3 && f.CaptureBattery().Charge == 126.75f, "Host used stale part charge instead of mount charge.");
                });
                var maximum = data.FsmVariables.FindFsmFloat("ChargeMax");
                check("battery capture: mount maximum and maximum-only revisions reach snapshots", () =>
                {
                    maximum.Value = 127; var first = f.CaptureBattery();
                    var publication = (BatteryPublication)Get(f.Sync, "_batteryPublication"); publication.MarkBroadcast(first.Revision);
                    maximum.Value = 147.25f; var snapshot = f.CaptureBattery();
                    Require(snapshot.ChargeMax == 147.25f && snapshot.Revision == first.Revision + 1 && publication.NeedsBroadcast
                        && f.SavedBatteryPart.FsmVariables.FindFsmFloat("ChargeMax").Value == 127, "Host maximum came from the saved part or lost its revision.");
                    Require(f.CaptureBattery().Revision == snapshot.Revision && publication.NeedsBroadcast, "Snapshot consumed a pending maximum change.");
                    maximum.Value = 127;
                });
                foreach (string faultName in new[] { "NaN", "infinite", "missing" })
                {
                    string fault = faultName;
                    check("battery capture: " + fault + " maximum is unavailable and recovers", () =>
                    {
                        var original = data.FsmVariables.FloatVariables;
                        try
                        {
                            if (fault == "missing") data.FsmVariables.FloatVariables = new List<FsmFloat>(original).FindAll(v => v != maximum).ToArray();
                            else maximum.Value = fault == "NaN" ? float.NaN : float.PositiveInfinity;
                            var unavailable = f.CaptureBattery();
                            Require(unavailable.Flags == 0 && unavailable.Charge == 0 && unavailable.ChargeMax == 0, "Invalid maximum retained host power.");
                        }
                        finally { data.FsmVariables.FloatVariables = original; maximum.Value = 127; }
                        Require(f.CaptureBattery().Flags == 3 && f.CaptureBattery().ChargeMax == 127, "Maximum repair did not recover.");
                    });
                }
                foreach (string stateName in new[] { "Install 1", "Assemble 2", "Set joint 2", "Remove part", "Far", "Near" })
                {
                    string state = stateName;
                    check("battery capture: transition " + state + " is unavailable", () =>
                    { NativeBagPartChecks.Fire(data, state); Require(f.CaptureBattery().Flags == 0, "Intermediate battery accepted."); });
                }
                check("battery capture: stable removal snapshots preserve pending live changes", () =>
                {
                    NativeBagPartChecks.Fire(data, "Check joint"); var first = f.CaptureBattery(); var publication = (BatteryPublication)Get(f.Sync, "_batteryPublication");
                    publication.MarkBroadcast(first.Revision); charge.Value = 120;
                    var snapshot = f.CaptureBattery(); Require(snapshot.Revision == first.Revision + 1 && publication.NeedsBroadcast, "Snapshot consumed live charge change.");
                    Require(f.CaptureBattery().Revision == snapshot.Revision && publication.NeedsBroadcast, "Repeated snapshot lost pending charge.");
                    installed.Value = false; NativeBagPartChecks.Fire(data, "Remove part"); Require(f.CaptureBattery().Flags == 0, "Removal was published early.");
                    NativeBagPartChecks.Fire(data, "Idle"); var removed = f.CaptureBattery(); Require(removed.Flags == 1 && removed.Charge == 0 && removed.ChargeMax == 0, "Settled removal retained charge or maximum.");
                    installed.Value = true; NativeBagPartChecks.Fire(data, "Check joint"); Require(f.CaptureBattery().Flags == 3, "Reinstallation did not recover.");
                });
                foreach (string faultName in new[] { "inactive", "part inactive", "parent", "assembly", "duplicate", "charge", "active part" })
                {
                    string fault = faultName;
                    check("battery capture: " + fault + " fails locally and recovers", () =>
                    {
                        PlayMakerFSM? duplicate = null; var parent = f.SavedBatteryPart.transform.parent;
                        if (fault == "inactive") data.gameObject.SetActive(false);
                        if (fault == "part inactive") f.SavedBatteryPart.gameObject.SetActive(false);
                        if (fault == "parent") f.SavedBatteryPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedBatteryPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate") duplicate = Empty(data.gameObject, "Data");
                        if (fault == "charge") charge.Value = float.NaN;
                        if (fault == "active part") data.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        try { Require(f.CaptureBattery().Flags == 0 && f.CaptureBattery().Charge == 0, "Broken host source retained power."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                            data.gameObject.SetActive(true); f.SavedBatteryPart.gameObject.SetActive(true); f.SavedBatteryPart.transform.parent = parent;
                            f.SavedBatteryPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1; charge.Value = 120;
                            data.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedBatteryPart.gameObject; NativeBagPartChecks.Fire(data, "Check joint");
                        }
                        Require(f.CaptureBattery().Flags == 3, "Repaired host battery stayed unavailable.");
                    });
                }
                check("battery protection: native discharge writes before guest admission", () =>
                {
                    charge.Value = 120; NativeBagPartChecks.Fire(data, "Delay 2");
                    Require(Near(charge.Value, 119.9f) && Near(f.SavedBatteryPart.FsmVariables.FindFsmFloat("Charge").Value, 119.9f), "Native drain did not reach owned battery.");
                    NativeBagPartChecks.Fire(data, "Died battery");
                    Require(f.SavedBatteryPart.FsmVariables.FindFsmFloat("ChargeMax").Value < 127
                        && f.SavedBatteryPart.FsmVariables.FindFsmFloat("DischargeRate").Value > .1f, "Native degradation did not reach saved part.");
                });
                check("battery capture: hosts reject peer records and guests cannot publish", () =>
                {
                    Call(f.Sync, "ClearBattery"); Call(f.Sync, "OnBatteryState", new BatteryState { Revision = 500, Flags = 3, Charge = 127 });
                    Require(((BatteryReplica)Get(f.Sync, "_batteryReplica")).Get() == null, "Host accepted a peer battery record.");
                    Property(f.Session, "IsHost", false); Require(Call(f.Sync, "BuildBatteryState") == null, "Guest published its saved battery.");
                    Property(f.Session, "IsHost", true);
                });
                check("battery protection: admission blocks mounted drain and degradation through disconnect", () =>
                {
                    Fixture.SetBatteryFields(data); Fixture.SetBatteryFields(f.SavedBatteryPart);
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare() && !data.enabled && !data.Fsm.RestartOnEnable, "Guest admission left native battery running.");
                    foreach (string state in new[] { "Delay 2", "Died battery", "Remove part" }) NativeBagPartChecks.Fire(data, state);
                    f.AssertSaved(); Call(f.Sync, "ReleaseSession");
                    data.SendEvent("REMOVE"); NativeBagPartChecks.Fire(data, "Delay 2"); Require(!data.enabled, "Disconnect resumed battery drain."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { wasProtected }); }
        }
    }
}
