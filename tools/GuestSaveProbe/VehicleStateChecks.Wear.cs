using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunWearChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode)
        {
            string[] savedFields = { "Body", "Path", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string name in savedFields) saved[name] = Get(item, name);
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool protectedBefore = (bool)protectedProperty.GetValue(policy, null);
            Action<bool> protect = value => protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { value });
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new WearFixture())
            try
            {
                reset(); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item;
                protect(false); mode(true, 255); f.InitDurability();
                check("host wear: native host fixture retains all fourteen enabled wear writers", () =>
                {
                    int writers = 0;
                    foreach (var state in f.Wearing.Fsm.States) foreach (var action in state.Actions)
                        if (action.GetType().Name == "SubtractFsmFloat")
                        { Require(action.Enabled, "Guest startup protection disabled a host fixture writer."); writers++; }
                    Require(writers == 14, "The fixture lost a native wear target.");
                });
                ushort sequence = 0;
                Action<ushort> receive = rpm => {
                    mode(true, 1); var state = State(1, sequence++, rpm);
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", state)!, "Driver telemetry was rejected."); f.Rebind(); };
                foreach (ushort rpm in new ushort[] { 0, 180, 2500, 6500 })
                {
                    ushort value = rpm;
                    check("host wear: native pressure and fourteen part results match local " + value + " RPM", () =>
                    {
                        mode(true, 255); f.Rpm.Value = value; f.ResetParts(); var expected = f.Cycle(); float pressure = f.PressureValue.Value;
                        f.Rpm.Value = 0; f.ResetParts(); receive(value); var actual = f.Cycle();
                        for (int i = 0; i < expected.Length; i++) Require(actual[i] == expected[i], "Delegated part wear differs at slot " + i);
                        Require(f.PressureValue.Value == pressure && f.Rpm.Value == 0 && f.EngineTemp.Value == 90,
                            "Pressure differs or telemetry changed the host RPM/temperature globals.");
                        Require(value == 0 || actual[0] < 100, "Running native cycle did not lower host bearing wear.");
                        Require(f.Oilpan.FsmVariables.FindFsmFloat("Oil").Value == 2 && f.Oilpan.FsmVariables.FindFsmFloat("Wear").Value == 100,
                            "Scoped RPM projection changed oil quantity or oilpan wear.");
                        f.OriginalInputs();
                    });
                }
                check("host wear: pressure keeps the host engine temperature while a guest simulates RPM", () =>
                {
                    receive(3000); f.Rpm.Value = 0;
                    f.EngineTemp.Value = 40; NativeBagPartChecks.Fire(f.Pressure, "Oil pressure"); float cold = f.PressureValue.Value;
                    f.EngineTemp.Value = 150; f.Pressure.Fsm.Update(); float hot = f.PressureValue.Value;
                    Require(cold > hot && f.EngineTemp.Value == 150 && f.Rpm.Value == 0, "Driver RPM replaced or ignored authoritative host heat.");
                    f.EngineTemp.Value = 90;
                });
                check("host wear: the host oil quantity controls native wear under delegated RPM", () =>
                {
                    f.ResetParts(); var ordinary = f.Cycle();
                    f.Oilpan.FsmVariables.FindFsmFloat("Oil").Value = .1f;
                    try { f.ResetParts(); var lowOil = f.Cycle(); Require(lowOil[0] < ordinary[0], "Guest RPM bypassed host oil-level wear arithmetic."); }
                    finally { f.Oilpan.FsmVariables.FindFsmFloat("Oil").Value = 2; }
                });
                check("host wear: packets alone do not apply wear and the native wait is retained", () =>
                {
                    f.ResetParts(); receive(3000); receive(3200); receive(3400);
                    Require(f.AllUnworn(), "Receiving telemetry changed a host part.");
                    f.Cycle(); var values = f.WearValues();
                    Require(f.Wearing.ActiveStateName == "Wait", "Native cycle lost its wait state.");
                    var wait = NativeBagPartChecks.State(f.Wearing, "Wait").Actions[0];
                    Require(Mathf.Abs(((FsmFloat)Get(wait, "time")).Value - 1.1f) < .0001f, "Native wear cadence was changed.");
                    f.Wearing.Fsm.Update(); var after = f.WearValues();
                    for (int i = 0; i < values.Length; i++) Require(values[i] == after[i], "Native wait applied another wear cycle immediately.");
                });
                check("host wear: stale and forged telemetry cannot alter accepted wear input", () =>
                {
                    var accepted = (VehicleState)Get(item, "AcceptedVehicleState");
                    var duplicate = State(1, accepted.Sequence, 65000);
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleState", duplicate)!, "Duplicate report accepted.");
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleState", State(2, 0, 65000))!, "Former/foreign owner accepted.");
                    Require(((VehicleState)Get(item, "AcceptedVehicleState")).Rpm == 3400, "Rejected report changed active input.");
                });
                check("host wear: expired missing and mismatched inputs prevent stale local RPM wear", () =>
                {
                    f.Rpm.Value = 6000;
                    object accepted = Get(item, "AcceptedVehicleState"); float until = (float)Get(item, "RemoteEngineUntil");
                    Action stopped = () => { f.ResetParts(); f.Cycle(); Require(f.AllUnworn(), "Stale input continued wearing host parts."); f.OriginalInputs(); };
                    Set(item, "RemoteEngineUntil", -1f); stopped(); Set(item, "RemoteEngineUntil", until);
                    Set(item, "AcceptedVehicleState", null); stopped(); Set(item, "AcceptedVehicleState", accepted);
                    Set(item, "RemoteOwner", (byte)2); stopped(); Set(item, "RemoteOwner", (byte)1);
                });
                check("host wear: the next driver's sequence zero replaces the previous source", () =>
                {
                    mode(true, 2); Require((bool)Call(vehicles, "OnRemoteVehicleState", State(2, 0, 1200))!, "New driver could not start its stream.");
                    f.Rebind(); f.Rpm.Value = 0; f.ResetParts(); f.Cycle(); Require(!f.AllUnworn(), "Fresh new driver did not drive host wear.");
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleState", State(1, sequence++, 65000))!, "Old driver resumed wear control.");
                });
                check("host wear: local ownership and seating restore native RPM immediately", () =>
                {
                    Action native = () => { f.Rpm.Value = 0; f.ResetParts(); f.Cycle(); Require(f.AllUnworn(), "Remote input overrode the local driver."); };
                    Set(item, "LocallyOwned", true); native(); Set(item, "LocallyOwned", false);
                    var world = World.GetProperty("Instance", Static).GetValue(null, null);
                    var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent;
                    player.SetParent(f.Body.transform, false); try { native(); } finally { player.SetParent(parent, false); }
                    f.ResetParts(); f.Cycle(); Require(!f.AllUnworn(), "Observer delegation failed after leaving local seat.");
                });
                check("host wear: guests and disconnected sessions retain native inputs", () =>
                {
                    Action native = () => { f.Rpm.Value = 0; f.ResetParts(); f.Cycle(); Require(f.AllUnworn(), "Guest or disconnected session projected driver RPM."); };
                    mode(false, 2); native(); mode(true, 2); SetProperty(session, "State", SessionState.Idle); native(); mode(true, 2);
                    protect(true);
                    try
                    {
                        // Only inspect the arithmetic boundary here; the existing guest
                        // writer guards are separately covered by the full native suite.
                        f.Rpm.Value = 0; f.Pressure.Fsm.Update();
                        Require(f.Pressure.FsmVariables.FindFsmFloat("Math2").Value == 0, "Protected guest save enabled host RPM projection.");
                    }
                    finally { protect(false); }
                });
                check("host wear: native helper failure finalizer restores the original global reference", () =>
                {
                    var read = NativeBagPartChecks.State(f.Pressure, "Oil pressure").Actions[1];
                    object?[] args = { read, null }; Vehicles.GetMethod("BeforeWearRead", Static).Invoke(null, args);
                    Require(args[1] != null && !ReferenceEquals(Get(read, "float1"), f.Rpm), "Input boundary did not substitute a scoped value.");
                    var error = new InvalidOperationException("probe native failure");
                    object returned = Vehicles.GetMethod("AfterWearRead", Static).Invoke(null, new object[] { error, args[1]! });
                    Require(ReferenceEquals(returned, error) && ReferenceEquals(Get(read, "float1"), f.Rpm), "Finalizer lost native failure or left a proxy attached.");
                });
                check("host wear: pending oil-pressure state stays unloaded and recovers after initialization", () =>
                {
                    var states = f.Pressure.Fsm.States;
                    var pending = new FsmState((Fsm)null!) { Name = "Oil pressure" };
                    try
                    {
                        CallStatic("ClearWearInputs", item); f.Pressure.Fsm.States = new[] { pending };
                        for (int i = 0; i < 20; i++) f.Rebind();
                        Require(Get(item, "NativeWear") == null && !pending.ActionsLoaded,
                            "Wear discovery deserialized a pending oil-pressure state.");
                    }
                    finally { f.Pressure.Fsm.States = states; f.Rebind(); }
                    Require(Get(item, "NativeWear") != null, "Initialized wear inputs did not recover."); f.OriginalInputs();
                });
                foreach (string field in new[] { "float1", "operation", "storeResult", "everyFrame" })
                {
                    string name = field;
                    check("host wear: changed " + name + " disables the entire projection and recovers", () =>
                    {
                        var read = NativeBagPartChecks.State(f.Pressure, "Oil pressure").Actions[1]; object before = Get(read, name);
                        object value = name == "everyFrame" ? (object)false : name == "operation" ? Enum.ToObject(before.GetType(), 0) : new FsmFloat(1);
                        try { Set(read, name, value); f.Rebind(); Require(Get(item, "NativeWear") == null, "Changed reader kept some wear inputs active."); }
                        finally { Set(read, name, before); }
                        f.Rebind(); Require(Get(item, "NativeWear") != null, "Repaired wear graph did not recover."); f.OriginalInputs();
                    });
                }
                check("host wear: a changed sibling reader rejects the whole group at the native boundary", () =>
                {
                    var sibling = NativeBagPartChecks.State(f.Wearing, "Pressure leak").Actions[14];
                    object before = Get(sibling, "everyFrame"); f.Rpm.Value = 0;
                    try
                    {
                        Set(sibling, "everyFrame", true); f.Pressure.Fsm.Update();
                        Require(Get(item, "NativeWear") == null && f.Pressure.FsmVariables.FindFsmFloat("Math2").Value == 0,
                            "An intact reader applied guest RPM after another member changed.");
                    }
                    finally { Set(sibling, "everyFrame", before); }
                    f.Rebind(); Require(Get(item, "NativeWear") != null, "Complete repaired group did not recover.");
                });
                check("host wear: duplicate or moved source is rejected without partial bindings", () =>
                {
                    var duplicate = f.Pressure.gameObject.AddComponent<PlayMakerFSM>(); duplicate.enabled = false; duplicate.FsmName = "Pressure";
                    try { f.Rebind(); Require(Get(item, "NativeWear") == null, "Duplicate source was accepted."); }
                    finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                    string name = f.Pressure.gameObject.name; f.Pressure.gameObject.name = "moved";
                    try { f.Rebind(); Require(Get(item, "NativeWear") == null, "Moved source was accepted."); }
                    finally { f.Pressure.gameObject.name = name; }
                    f.Rebind(); Require(Get(item, "NativeWear") != null, "Restored source did not recover.");
                });
                RunOilContaminationChecks(check, f, receive, mode, session, protect);
                check("host wear: stream cleanup removes scoped readers", () =>
                {
                    Call(vehicles, "ClearVehicleStateStreams"); Require(Get(item, "NativeWear") == null, "Cleanup retained the native wear binding.");
                    f.Rpm.Value = 0; f.ResetParts(); f.Cycle(); Require(f.AllUnworn(), "Cleared stream continued applying guest RPM."); f.OriginalInputs();
                });
            }
            finally
            {
                CallStatic("ClearWearInputs", item); foreach (string name in savedFields) Set(item, name, saved[name]);
                protect(protectedBefore); reset();
            }
        }

        private static void RunOilContaminationChecks(Action<string, Action> check, WearFixture f,
            Action<ushort> receive, Action<bool, byte> mode, SessionManager session, Action<bool> protect)
        {
            check("host oil: native filtering and contamination writers remain enabled on the host", () =>
            {
                var actions = NativeBagPartChecks.State(f.Oil, "Oil contamination").Actions;
                Require(actions.Length == 5 && actions[0].Enabled && actions[1].Enabled && actions[4].Enabled,
                    "The fixture lost a native oil/filter writer.");
                Require(f.Oil.FsmVariables.FindFsmFloat("OilFilteringRate").Value == .01f, "Native filtering rate changed.");
            });
            foreach (ushort rpm in new ushort[] { 0, 180, 2500, 6500, 65535 })
                foreach (float dirt in new[] { 50f, 100f, 101f })
                {
                    ushort value = rpm; float filter = dirt;
                    check("host oil: native local and delegated results match at " + value + " RPM and filter dirt " + filter, () =>
                    {
                        mode(true, 255); f.Rpm.Value = value; f.ResetOil(filter); var expected = f.OilCycle();
                        f.Rpm.Value = 0; f.ResetOil(filter); receive(value);
                        Require(f.OilContamination.Value == 2 && f.FilterDirt.Value == filter, "Telemetry directly changed oil or the filter.");
                        var actual = f.OilCycle();
                        for (int i = 0; i < expected.Length; i++) Require(actual[i] == expected[i], "Delegated native oil result differs at " + i);
                        float filtering = filter > 100 ? 0 : .01f;
                        float rate = Mathf.Clamp(value / 250000f, .01f, 1);
                        // Legacy Mono can retain more precision in expressions than in
                        // FsmFloat storage. Native-vs-native equality above stays exact.
                        Require(Mathf.Abs(actual[0] - ((2f - filtering) + rate)) < .00001f
                            && Mathf.Abs(actual[1] - (filter + filtering)) < .00001f && Mathf.Abs(actual[2] - rate) < .00001f,
                            "Oil contamination or filter dirt was not applied exactly once by native actions: contamination=" + actual[0].ToString("R")
                            + ", dirt=" + actual[1].ToString("R") + ", rate=" + actual[2].ToString("R") + ", state=" + f.Oil.ActiveStateName
                            + ", filtering=" + f.Oil.FsmVariables.FindFsmFloat("OilFiltering").Value.ToString("R") + ".");
                        Require(f.Oil.ActiveStateName == "Wait" && f.Rpm.Value == 0 && f.EngineTemp.Value == 90,
                            "Native cadence or host globals changed.");
                        Require(f.Oilpan.FsmVariables.FindFsmFloat("Oil").Value == 2 && f.Oilpan.FsmVariables.FindFsmFloat("Wear").Value == 100,
                            "Contamination projection changed oil quantity or oilpan wear.");
                        f.OriginalInputs();
                    });
                }
            check("host oil: repeat packets and native wait do not add contamination ticks", () =>
            {
                f.ResetOil(50); receive(6000); receive(6200); receive(6500);
                Require(f.OilContamination.Value == 2 && f.FilterDirt.Value == 50, "Repeated packets applied oil writes.");
                var before = f.OilCycle(); f.Oil.Fsm.Update();
                Require(f.OilContamination.Value == before[0] && f.FilterDirt.Value == before[1] && f.Oil.ActiveStateName == "Wait",
                    "The native wait repeated its persistent writes.");
                var wait = NativeBagPartChecks.State(f.Oil, "Wait").Actions[1];
                Require(Mathf.Abs(((FsmFloat)Get(wait, "time")).Value - 1.1f) < .0001f, "Oil wait changed.");
            });
            check("host oil: missing expired and wrong-owner samples retain native zero-RPM filtering", () =>
            {
                mode(true, 255); f.Rpm.Value = 0; f.ResetOil(101); var expected = f.OilCycle(); receive(6500); f.Rpm.Value = 6000;
                var accepted = Get(f.Item, "AcceptedVehicleState"); float until = (float)Get(f.Item, "RemoteEngineUntil");
                Action stopped = () => {
                    f.ResetOil(101); var actual = f.OilCycle();
                    for (int i = 0; i < expected.Length; i++) Require(actual[i] == expected[i], "Stale RPM changed native stopped-engine oil behavior.");
                    f.OriginalInputs(); };
                Set(f.Item, "RemoteEngineUntil", -1f); stopped(); Set(f.Item, "RemoteEngineUntil", until);
                Set(f.Item, "AcceptedVehicleState", null); stopped(); Set(f.Item, "AcceptedVehicleState", accepted);
                Set(f.Item, "RemoteOwner", (byte)2); stopped(); Set(f.Item, "RemoteOwner", (byte)1);
            });
            check("host oil: local ownership and disconnect return to native RPM", () =>
            {
                receive(6500); f.Rpm.Value = 0;
                Action native = () => { f.ResetOil(50); f.OilCycle(); Require(f.OilRate.Value == .01f, "Remote RPM survived local takeover/disconnect."); };
                Set(f.Item, "LocallyOwned", true); native(); Set(f.Item, "LocallyOwned", false);
                SetProperty(session, "State", SessionState.Idle); native(); mode(true, 1);
            });
            check("host oil: a changed contamination divisor disables every wear input and repairs", () =>
            {
                var read = NativeBagPartChecks.State(f.Oil, "Oil contamination").Actions[2]; var before = Get(read, "float2");
                receive(6500); f.Rpm.Value = 0;
                try
                {
                    Set(read, "float2", new FsmFloat(1)); f.Pressure.Fsm.Update();
                    Require(Get(f.Item, "NativeWear") == null && f.Pressure.FsmVariables.FindFsmFloat("Math2").Value == 0,
                        "Pressure kept remote RPM with a changed oil calculation.");
                }
                finally { Set(read, "float2", before); }
                f.Rebind(); Require(Get(f.Item, "NativeWear") != null, "Repaired seven-reader group did not recover.");
                f.ResetOil(50); f.OilCycle(); Require(f.OilRate.Value == 6500f / 250000, "Repaired oil input did not resume.");
                f.OriginalInputs();
            });
            check("host oil: protected guest writes stay blocked through disconnect", () =>
            {
                protect(true); mode(false, 1);
                try
                {
                    foreach (bool connected in new[] { true, false })
                    {
                        if (!connected) SetProperty(session, "State", SessionState.Idle);
                        f.Rpm.Value = 6500; f.ResetOil(50); f.OilCycle();
                        Require(f.OilContamination.Value == 2 && f.FilterDirt.Value == 50,
                            "Native guest oil/filter writers escaped save protection.");
                        var actions = NativeBagPartChecks.State(f.Oil, "Oil contamination").Actions;
                        Require(f.Oil.ActiveStateName == "Wait" && !actions[0].Enabled && !actions[1].Enabled && !actions[4].Enabled,
                            "The guest oil loop was paused instead of retaining native math with protected writers.");
                        f.OriginalInputs();
                    }
                }
                finally { protect(false); mode(true, 1); f.OilContamination.Value = 0; }
            });
        }

        private sealed class WearFixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly float _rpmBefore, _tempBefore;
            private readonly List<PlayMakerFSM> _parts = new List<PlayMakerFSM>();
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Pressure, Wearing, Oilpan, Oil, Oilfilter, EngineFriction;
            internal readonly FsmFloat Rpm, EngineTemp, PressureValue;
            internal FsmFloat OilContamination => Oilpan.FsmVariables.FindFsmFloat("OilContamination");
            internal FsmFloat FilterDirt => Oilfilter.FsmVariables.FindFsmFloat("Dirt");
            internal FsmFloat OilRate => Oil.FsmVariables.FindFsmFloat("OilContaminationRate");
            internal object Item = null!;

            internal WearFixture()
            {
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../vehicle-wear-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                Rpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM"); EngineTemp = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp");
                _rpmBefore = Rpm.Value; _tempBefore = EngineTemp.Value; Rpm.Value = 0; EngineTemp.Value = 90;
                _root = new GameObject("CORRIS"); _root.SetActive(false); Body = _root.AddComponent<Rigidbody>(); Body.useGravity = false; Body.isKinematic = true;
                var sim = Child(_root, "Simulation"); var engine = Child(sim, "Engine"); var oil = Child(engine, "Oil");
                var pressureRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/Oil", "Pressure");
                var wearingRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/Oil", "Wearing");
                var oilRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/Oil", "Oil");
                Pressure = NativeBagPartChecks.MakeFsm(oil, pressureRow); Wearing = NativeBagPartChecks.MakeFsm(oil, wearingRow);
                Oil = NativeBagPartChecks.MakeFsm(oil, oilRow);
                PlayMakerFSM? oilpan = null;
                foreach (var reference in Wearing.FsmVariables.GameObjectVariables)
                {
                    if (!reference.Name.StartsWith("db_", StringComparison.Ordinal)) continue;
                    var part = Child(_root, reference.Name); var data = Data(part, "Data");
                    reference.Value = part; if (reference.Name == "db_Oilpan") oilpan = data; else _parts.Add(data);
                }
                Oilpan = oilpan ?? throw new InvalidOperationException("Missing oilpan reference.");
                Oilfilter = Data(Child(_root, "host oil filter"), "Data");
                EngineFriction = Data(Child(sim, "CarData"), "EngineFriction");
                Oil.FsmVariables.FindFsmGameObject("db_Oilpan").Value = Oilpan.gameObject;
                Oil.FsmVariables.FindFsmGameObject("db_Oilfilter").Value = Oilfilter.gameObject;
                Oil.FsmVariables.FindFsmGameObject("CarData").Value = EngineFriction.gameObject;
                _root.SetActive(true); Pressure.Fsm.Init(Pressure); Wearing.Fsm.Init(Wearing); Oil.Fsm.Init(Oil);
                foreach (var part in _parts) { part.Fsm.Init(part); NativeBagPartChecks.Start(part); }
                Oilpan.Fsm.Init(Oilpan); NativeBagPartChecks.Start(Oilpan);
                Oilfilter.Fsm.Init(Oilfilter); NativeBagPartChecks.Start(Oilfilter);
                EngineFriction.Fsm.Init(EngineFriction); NativeBagPartChecks.Start(EngineFriction);
                Load(Pressure, pressureRow); Load(Wearing, wearingRow);
                // Import the whole graph for writer-guard validation, but only enter
                // filter -> contamination -> cool-engine friction -> wait in this fixture.
                Load(Oil, oilRow);
                Pressure.FsmVariables.FindFsmFloat("Oil").Value = 2;
                PressureValue = Pressure.FsmVariables.FindFsmFloat("OilPressureBar");
                Require(_parts.Count == 14, "Wear fixture must retain fourteen separate native writer targets.");
            }
            private static GameObject Child(GameObject parent, string name)
            { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
            private static FsmFloat Float(string name, float value) => new FsmFloat { Name = name, UseVariable = true, Value = value };
            private static PlayMakerFSM Data(GameObject obj, string name)
            {
                var data = obj.AddComponent<PlayMakerFSM>(); data.enabled = false;
                Set(data, "fsm", new Fsm()); data.FsmName = name; data.Fsm.StartState = "Idle";
                data.Fsm.States = new[] { new FsmState(data.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
                data.FsmVariables.FloatVariables = new[] { Float("Wear", 100), Float("Oil", 2), Float("OilContamination", 0),
                    Float("Durability", 1), Float("Dirt", 50), Float("FrictionBase", 0) };
                return data;
            }
            private void Load(PlayMakerFSM fsm, Dictionary<string, object> row)
            {
                var names = new List<string>(); foreach (Dictionary<string, object> state in (IEnumerable)row["states"]) names.Add((string)state["name"]);
                NativeBagPartChecks.LoadActions(fsm, row, names.ToArray());
                BindGlobals(fsm);
            }
            private void BindGlobals(PlayMakerFSM fsm)
            {
                foreach (var state in fsm.Fsm.States)
                    foreach (var action in state.Actions)
                        foreach (var field in action.GetType().GetFields())
                            if (field.FieldType == typeof(FsmFloat) && field.GetValue(action) is FsmFloat value && value.UseVariable)
                            { if (value.Name == "RPM") field.SetValue(action, Rpm); else if (value.Name == "EngineTemp") field.SetValue(action, EngineTemp); }
            }
            internal void InitDurability()
            {
                NativeBagPartChecks.Start(Pressure); NativeBagPartChecks.Start(Wearing); NativeBagPartChecks.Start(Oil);
                NativeBagPartChecks.Fire(Wearing, "State 4");
            }
            internal void ResetParts() { foreach (var part in _parts) part.FsmVariables.FindFsmFloat("Wear").Value = 100; Pressure.FsmVariables.FindFsmFloat("PressureLeak").Value = 1; }
            internal float[] Cycle() { NativeBagPartChecks.Fire(Pressure, "Oil pressure"); NativeBagPartChecks.Fire(Wearing, "State 1"); return WearValues(); }
            internal void ResetOil(float dirt) { OilContamination.Value = 2; FilterDirt.Value = dirt; }
            internal float[] OilCycle()
            {
                NativeBagPartChecks.Fire(Oil, "Oil filter");
                return new[] { OilContamination.Value, FilterDirt.Value, OilRate.Value };
            }
            internal float[] WearValues() { var values = new float[_parts.Count]; for (int i = 0; i < values.Length; i++) values[i] = _parts[i].FsmVariables.FindFsmFloat("Wear").Value; return values; }
            internal bool AllUnworn() { foreach (float value in WearValues()) if (value != 100) return false; return true; }
            internal void Rebind()
            {
                Set(Item, "NextWearInputProbeAt", 0f); CallStatic("EnsureWearInputs", Item);
                if (Get(Item, "NativeWear") == null) { Set(Item, "NextWearInputProbeAt", 0f); CallStatic("EnsureWearInputs", Item); }
            }
            internal void OriginalInputs()
            {
                var binding = Get(Item, "NativeWear"); if (binding == null) return;
                foreach (object read in (IEnumerable)Get(binding, "Reads"))
                {
                    var field = (System.Reflection.FieldInfo)Get(read, "Field");
                    Require(ReferenceEquals(field.GetValue(Get(read, "Action")), Rpm), "Scoped read left an operand redirected.");
                }
            }
            public void Dispose() { Rpm.Value = _rpmBefore; EngineTemp.Value = _tempBefore; UnityEngine.Object.DestroyImmediate(_root); }
        }
    }
}
