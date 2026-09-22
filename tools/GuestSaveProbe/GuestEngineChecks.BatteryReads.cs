using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineChecks
    {
        private sealed class NativeBatteryReader
        {
            internal string Label = "", State = "", Scalar = "";
            internal PlayMakerFSM Fsm = null!;
            internal FsmStateAction Action = null!;
            internal bool Boolean;
            internal FsmOwnerDefault Target => (FsmOwnerDefault)Get(Action, "gameObject");
            internal FsmString TargetName => (FsmString)Get(Action, "fsmName");
            internal float Value => Boolean ? (((FsmBool)Get(Action, "storeValue")).Value ? 1 : 0) : ((FsmFloat)Get(Action, "storeValue")).Value;
            internal void Fire() { NativeBagPartChecks.Fire(Fsm, "Probe idle"); NativeBagPartChecks.Fire(Fsm, State); }
            internal void Assert(float expected)
            {
                Fire(); Require(Value == expected && Action.Enabled && Fsm.enabled, Label + " entry read " + Value + " instead of " + expected);
                if (Boolean) ((FsmBool)Get(Action, "storeValue")).Value = expected != 1;
                else ((FsmFloat)Get(Action, "storeValue")).Value = -333;
                Action.OnUpdate(); Require(Value == expected, Label + " update did not read " + expected);
            }
        }

        private static void RunBatteryReads(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var inputsProperty = catalog.GetProperty("GuestEngineInputs", Static); var inputs = inputsProperty.GetValue(null, null);
            var callback = Guard.GetField("PrepareInputs", Static).GetValue(null);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protect = policy.GetType().GetProperty("ProtectWorld", Members); object oldProtect = protect.GetValue(policy, null);
            var session = SessionManager.Instance ?? throw new InvalidOperationException("Native probe session is unavailable.");
            var host = typeof(SessionManager).GetProperty("IsHost", Members); object oldHost = host.GetValue(session, null);
            var connected = typeof(SessionManager).GetProperty("State", Members); object oldState = connected.GetValue(session, null);
            var world = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true).GetProperty("Instance", Static).GetValue(null, null);
            object oldItems = Get(world, "_items"), oldReady = Get(world, "_syncReady");
            var bridge = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true), Members, null,
                new object[] { world, new Dictionary<PlayMakerFSM, bool>() }, null);
            var items = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true), Members, null, new[] { bridge }, null);
            var root = new GameObject("CORRIS"); root.SetActive(false);
            var consumers = new GameObject("battery input consumers"); consumers.SetActive(false);
            var battery = Empty(PathObject(root, "CORRIS/Assemblies/VINP_Battery"), "Data");
            var other = Empty(battery.gameObject, "Other"); var ordinary = Empty(Child(consumers, "ordinary source"), "Data");
            foreach (var fsm in new[] { battery, other, ordinary })
            {
                AddFloat(fsm, "Charge", fsm == battery ? 77.25f : 41); AddFloat(fsm, "ChargeMax", fsm == battery ? 127 : 42);
                fsm.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = fsm == battery } };
                fsm.Fsm.Init(fsm);
            }
            var readers = new List<NativeBatteryReader>(); uint revision = 0;
            Action<byte, float, float> receive = (flags, charge, maximum) => InstanceCall(world, "OnBatteryState",
                PacketCodec.Decode(PacketCodec.Encode(new BatteryState { Revision = ++revision, Flags = flags, Charge = charge, ChargeMax = maximum })));
            Action assertSaved = () => Require(battery.FsmVariables.FindFsmFloat("Charge").Value == 77.25f
                && battery.FsmVariables.FindFsmFloat("ChargeMax").Value == 127 && battery.FsmVariables.FindFsmBool("Installed").Value,
                "Battery input projection overwrote saved source values.");
            try
            {
                Guard.GetField("PrepareInputs", Static).SetValue(null, null); Set(world, "_items", items); Set(world, "_syncReady", true);
                host.GetSetMethod(true).Invoke(session, new object[] { false }); connected.GetSetMethod(true).Invoke(session, new object[] { SessionState.Connected });
                protect.GetSetMethod(true).Invoke(policy, new object[] { false });
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var parser = Activator.CreateInstance(readerType, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../battery-accessory-inputs-probe.json")) }, null);
                var fixture = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(parser, null);
                var rows = (List<object>)fixture["fsms"];
                foreach (Dictionary<string, object> spec in (IEnumerable)fixture["reads"])
                {
                    string path = (string)spec["path"], name = (string)spec["fsm"], stateName = (string)spec["state"];
                    int index = Convert.ToInt32(spec["index"]); var row = NativeBagPartChecks.Find(rows, path, name);
                    var fsm = NativeBagPartChecks.MakeFsm(Child(consumers, "consumer " + readers.Count), row);
                    if ((string)spec["scalar"] == "Amps")
                    {
                        // Native UseOwner ignores the serialized Battery object:
                        // this control reads the current-meter FSM on PowerON.
                        var meter = Empty(fsm.gameObject, "Amps"); AddFloat(meter, "Amps", 333); meter.Fsm.Init(meter);
                    }
                    foreach (var value in fsm.FsmVariables.GameObjectVariables) value.Value = battery.gameObject;
                    fsm.Fsm.Init(fsm); var state = NativeBagPartChecks.State(fsm, stateName);
                    var action = NativeAction((Dictionary<string, object>)((List<object>)FindState(row, stateName)["actions"])[index], fsm);
                    state.Actions = new[] { action }; state.Transitions = new FsmTransition[0]; action.Init(state);
                    readers.Add(new NativeBatteryReader { Label = path + "::" + name + "/" + stateName + "#" + index,
                        Fsm = fsm, State = stateName, Scalar = (string)spec["scalar"], Boolean = (string)spec["type"] == "GetFsmBool", Action = action });
                }
                root.SetActive(true); consumers.SetActive(true);
                foreach (var reader in readers) NativeBagPartChecks.Start(reader.Fsm);
                check("battery reads: retained native audit includes 22 projected reads and one unrelated scalar", () =>
                { Require(readers.Count == 23 && readers.FindAll(r => r.Scalar != "Amps").Count == 22, "Incomplete read inventory."); });
                foreach (var reader in readers)
                    check("battery reads: native solo " + reader.Label, () => reader.Assert(reader.Boolean ? 1 : reader.Scalar == "Charge" ? 77.25f : reader.Scalar == "ChargeMax" ? 127 : 333));
                protect.GetSetMethod(true).Invoke(policy, new object[] { true });
                check("battery reads: unseeded guest has no local battery power", () =>
                { foreach (var reader in readers) reader.Assert(reader.Scalar == "Amps" ? 333 : 0); assertSaved(); });
                receive(3, 105.5f, 147.25f);
                foreach (var reader in readers)
                    check("battery reads: host entry/update " + reader.Label, () =>
                    { reader.Assert(reader.Boolean ? 1 : reader.Scalar == "Charge" ? 105.5f : reader.Scalar == "ChargeMax" ? 147.25f : 333); assertSaved(); });
                foreach (byte flags in new byte[] { 1, 0, 3 })
                    check("battery reads: host availability " + flags + " reaches every native consumer", () =>
                    {
                        receive(flags, flags == 3 ? -1.25f : 0, flags == 3 ? 129.75f : 0);
                        foreach (var reader in readers) reader.Assert(reader.Scalar == "Amps" ? 333 : flags != 3 ? 0 : reader.Boolean ? 1 : reader.Scalar == "Charge" ? -1.25f : 129.75f);
                        assertSaved();
                    });
                receive(3, 105.5f, 147.25f);
                check("battery reads: stale/conflicting packets cannot replace host inputs", () =>
                {
                    InstanceCall(world, "OnBatteryState", new BatteryState { Revision = revision - 1, Flags = 3, Charge = 1, ChargeMax = 2 });
                    InstanceCall(world, "OnBatteryState", new BatteryState { Revision = revision, Flags = 3, Charge = 105.5f, ChargeMax = 2 });
                    readers.Find(r => r.Scalar == "ChargeMax")!.Assert(147.25f);
                });
                RunBatteryReadLifecycle(check, readers, battery, ordinary, session, items, receive, assertSaved);
                RunBatteryReadCalculations(check, readers, rows, receive, assertSaved);
                InstanceCall(items, "ClearBattery");
                RunReadSourceFiltering(check, readers.Find(r => r.Boolean)!, inputs);
                receive(3, 105.5f, 147.25f);
                check("battery reads: remembered source survives rename and unavailable catalog", () =>
                {
                    var parent = battery.transform.parent;
                    try
                    {
                        battery.transform.parent = consumers.transform; battery.gameObject.name = "saved battery moved";
                        inputsProperty.GetSetMethod(true).Invoke(null, new object?[] { null });
                        readers.Find(r => r.Scalar == "Charge")!.Assert(105.5f); assertSaved();
                    }
                    finally { battery.transform.parent = parent; battery.gameObject.name = "VINP_Battery"; inputsProperty.GetSetMethod(true).Invoke(null, new[] { inputs }); }
                });
            }
            finally
            {
                inputsProperty.GetSetMethod(true).Invoke(null, new[] { inputs }); Set(world, "_items", oldItems); Set(world, "_syncReady", oldReady);
                host.GetSetMethod(true).Invoke(session, new[] { oldHost }); connected.GetSetMethod(true).Invoke(session, new[] { oldState });
                protect.GetSetMethod(true).Invoke(policy, new[] { oldProtect });
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(consumers); Call("Prepare", true);
                Guard.GetField("PrepareInputs", Static).SetValue(null, callback);
            }
        }

        private sealed class ReadSourceCase
        {
            internal string Label, Path, Fsm, Scalar;
            internal ReadSourceCase(string label, object rule, string path = "Path", string scalar = "Installed")
            { Label = label; Path = (string)Get(rule, path); Fsm = (string)Get(rule, "Fsm"); Scalar = scalar; }
        }
        private static Transform? _readSourceTransform;
        private static int _readSourcePaths;
        private static void CountReadSourcePath(Transform transform) { if (transform == _readSourceTransform) _readSourcePaths++; }

        private static void RunReadSourceFiltering(Action<string, Action> check, NativeBatteryReader reader, object inputs)
        {
            var heater = Get(inputs, "Heater");
            var cases = new List<ReadSourceCase> { new ReadSourceCase("battery", Get(inputs, "Battery")),
                new ReadSourceCase("heater", heater), new ReadSourceCase("rear window", Get(heater, "RearWindow"), scalar: "HeatingSprites") };
            foreach (var wire in (IEnumerable)Get(inputs, "Wires"))
                if ((bool)Get(wire, "ProjectNativeReads")) { cases.Add(new ReadSourceCase("wire", wire)); break; }
            foreach (var hose in (IEnumerable)Get(Get(inputs, "Block"), "CoolantHoses"))
                if ((bool)Get(hose, "ProjectNativeReads")) { cases.Add(new ReadSourceCase("heater hose", Get(hose, "Mount"), "MountPath")); break; }
            Require(cases.Count == 5, "Missing native read-source families.");
            var paths = Core.GetType("WinterMP.Core.Sync.ScenePath", true);
            var path = (Func<Transform, string>)Delegate.CreateDelegate(typeof(Func<Transform, string>), paths.GetMethod("Of", Static));
            var scan = (Func<IEnumerable<UnityEngine.Object>>)Delegate.CreateDelegate(typeof(Func<IEnumerable<UnityEngine.Object>>), paths.GetMethod("ScanFsms", Static));
            var profile = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("GuestEngineInputs", Static);
            var variable = (FsmString)Get(reader.Action, "variableName");
            string scalar = variable.Value, targetName = reader.TargetName.Value; var target = reader.Target.GameObject.Value;
            var harmony = new Harmony("wintermp.probe.read-source-filter");
            try
            {
                harmony.Patch(paths.GetMethod("Of", Static), prefix: new HarmonyMethod(typeof(GuestEngineChecks).GetMethod(nameof(CountReadSourcePath), Static)));
                foreach (var spec in cases)
                {
                    var root = new GameObject(spec.Path.Split('/')[0]); root.SetActive(false);
                    try
                    {
                        var obj = PathObject(root, spec.Path); var parent = obj.transform.parent; string name = obj.name;
                        var elsewhere = Child(root, "unrelated source parent");
                        var source = Empty(obj, spec.Fsm);
                        source.FsmVariables.BoolVariables = new[] { new FsmBool { Name = spec.Scalar, UseVariable = true, Value = true } };
                        source.Fsm.Init(source); variable.Value = spec.Scalar; reader.TargetName.Value = spec.Fsm;
                        reader.Target.GameObject.Value = obj; _readSourceTransform = obj.transform;
                        Action saved = () => Require(source.FsmVariables.FindFsmBool(spec.Scalar).Value, "Read projection changed saved source data.");
                        check("read source filter: " + spec.Label + " unrelated name keeps native values without full paths", () =>
                        {
                            obj.name = "unrelated native read source"; _readSourcePaths = 0;
                            reader.Assert(1); Require(_readSourcePaths == 0, "Unrelated source still built its complete path."); saved();
                        });
                        check("read source filter: " + spec.Label + " matching leaf still requires the correct parent", () =>
                        {
                            obj.name = name; obj.transform.SetParent(elsewhere.transform, false); _readSourcePaths = 0;
                            reader.Assert(1); Require(_readSourcePaths > 0, "Matching leaf bypassed the full path check."); saved();
                        });
                        check("read source filter: " + spec.Label + " duplicate siblings cannot borrow the unindexed source", () =>
                        {
                            obj.transform.SetParent(parent, false); var duplicate = Child(parent.gameObject, name);
                            try { _readSourcePaths = 0; reader.Assert(1); Require(_readSourcePaths > 0, "Duplicate source bypassed path validation."); saved(); }
                            finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                        });
                        check("read source filter: " + spec.Label + " repairs immediately after earlier rejection", () =>
                        { _readSourcePaths = 0; reader.Assert(0); Require(_readSourcePaths > 0, "Repaired source was not discovered."); saved(); });
                        check("read source filter: " + spec.Label + " remembered source survives movement and catalog loss", () =>
                        {
                            obj.name = "moved remembered source"; obj.transform.SetParent(elsewhere.transform, false);
                            try
                            {
                                profile.GetSetMethod(true).Invoke(null, new object?[] { null }); _readSourcePaths = 0;
                                reader.Assert(0); Require(_readSourcePaths == 0, "Remembered source lost its latched identity."); saved();
                            }
                            finally { profile.GetSetMethod(true).Invoke(null, new[] { inputs }); }
                        });
                        check("read source filter: " + spec.Label + " active discovery retains its path snapshot", () =>
                        {
                            var fresh = Empty(Child(parent.gameObject, name), spec.Fsm);
                            fresh.FsmVariables.BoolVariables = new[] { new FsmBool { Name = spec.Scalar, UseVariable = true, Value = true } };
                            fresh.Fsm.Init(fresh); reader.Target.GameObject.Value = fresh.gameObject; _readSourceTransform = fresh.transform;
                            using (var objects = scan().GetEnumerator())
                            {
                                Require(objects.MoveNext() && path(fresh.transform) == spec.Path, "Discovery fixture did not capture the native source.");
                                fresh.gameObject.name = "renamed after discovery capture"; _readSourcePaths = 0;
                                reader.Assert(0); Require(_readSourcePaths > 0, "Discovery rejected its own cached path after a live rename.");
                            }
                            reader.Assert(0);
                            Require(fresh.FsmVariables.FindFsmBool(spec.Scalar).Value, "Discovered source was overwritten."); saved();
                        });
                    }
                    finally { _readSourceTransform = null; UnityEngine.Object.DestroyImmediate(root); }
                }
            }
            finally
            {
                harmony.UnpatchSelf(); _readSourceTransform = null;
                variable.Value = scalar; reader.TargetName.Value = targetName; reader.Target.GameObject.Value = target;
                profile.GetSetMethod(true).Invoke(null, new[] { inputs });
            }
        }

        private static void RunBatteryReadLifecycle(Action<string, Action> check, List<NativeBatteryReader> readers,
            PlayMakerFSM battery, PlayMakerFSM ordinary, SessionManager session,
            object items, Action<byte, float, float> receive, Action assertSaved)
        {
            foreach (var reader in new[] { readers.Find(r => r.Boolean)!, readers.Find(r => r.Scalar == "Charge")! })
            {
                float expected = reader.Boolean ? 1 : 105.5f, ordinaryValue = reader.Boolean ? 0 : 41;
                check("battery reads: " + reader.Action.GetType().Name + " native cache and target fallback", () =>
                {
                    reader.TargetName.Value = "Other"; reader.Assert(expected); reader.TargetName.Value = "Data";
                    reader.Target.GameObject.Value = ordinary.gameObject; reader.Assert(ordinaryValue);
                    reader.TargetName.Value = "Other"; reader.Target.GameObject.Value = battery.gameObject; reader.Assert(ordinaryValue);
                    reader.TargetName.Value = "Data"; reader.Assert(ordinaryValue);
                    foreach (string name in new[] { "Data", "", "Missing FSM" })
                    {
                        reader.Target.GameObject.Value = ordinary.gameObject; reader.Fire();
                        reader.TargetName.Value = name; reader.Target.GameObject.Value = battery.gameObject; reader.Assert(expected);
                    }
                    reader.TargetName.Value = "Data"; assertSaved();
                });
                check("battery reads: " + reader.Action.GetType().Name + " rejects saved/global output aliases", () =>
                {
                    object output = Get(reader.Action, "storeValue");
                    try
                    {
                        Set(reader.Action, "storeValue", reader.Boolean ? (object)battery.FsmVariables.FindFsmBool("Installed") : battery.FsmVariables.FindFsmFloat("Charge"));
                        var installed = battery.FsmVariables.FindFsmBool("Installed");
                        if (reader.Boolean) installed.Value = false;
                        try { reader.Fire(); reader.Action.OnUpdate(); Require(!reader.Boolean || !installed.Value, "Saved bool output changed."); }
                        finally { installed.Value = true; }
                        assertSaved();
                        var globals = FsmVariables.GlobalVariables;
                        if (reader.Boolean)
                        {
                            var original = globals.BoolVariables; var value = (FsmBool)output; value.Value = false; bool before = value.Value;
                            try { globals.BoolVariables = new List<FsmBool>(original) { value }.ToArray(); Set(reader.Action, "storeValue", value); reader.Fire(); Require(value.Value == before, "Global bool output changed."); }
                            finally { globals.BoolVariables = original; }
                        }
                        else
                        {
                            var original = globals.FloatVariables; var value = (FsmFloat)output; value.Value = 17;
                            try { globals.FloatVariables = new List<FsmFloat>(original) { value }.ToArray(); Set(reader.Action, "storeValue", value); reader.Fire(); Require(value.Value == 17, "Global float output changed."); }
                            finally { globals.FloatVariables = original; }
                        }
                    }
                    finally { Set(reader.Action, "storeValue", output); }
                    reader.Assert(expected);
                });
                check("battery reads: " + reader.Action.GetType().Name + " late source works before discovery", () =>
                {
                    battery.gameObject.name = "previous battery";
                    var late = Empty(Child(battery.transform.parent.gameObject, "VINP_Battery"), "Data");
                    try
                    {
                        AddFloat(late, "Charge", 67); late.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true } };
                        late.Fsm.Init(late); reader.Target.GameObject.Value = late.gameObject; reader.Assert(expected);
                        Require(late.FsmVariables.FindFsmFloat("Charge").Value == 67 && !late.FsmVariables.FindFsmBool("Installed").Value, "Late source was overwritten.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(late.gameObject); battery.gameObject.name = "VINP_Battery"; reader.Target.GameObject.Value = battery.gameObject; }
                    reader.Assert(expected);
                });
            }
            check("battery reads: disconnect and host mode preserve native reads; a fresh guest starts unpowered", () =>
            {
                var state = typeof(SessionManager).GetProperty("State", Members); var host = typeof(SessionManager).GetProperty("IsHost", Members);
                var sample = readers.Find(r => r.Scalar == "Charge")!;
                state.GetSetMethod(true).Invoke(session, new object[] { SessionState.Idle }); sample.Assert(77.25f);
                InstanceCall(items, "ClearBattery"); state.GetSetMethod(true).Invoke(session, new object[] { SessionState.Connected }); sample.Assert(0);
                receive(3, 105.5f, 147.25f); sample.Assert(105.5f);
                host.GetSetMethod(true).Invoke(session, new object[] { true }); sample.Assert(77.25f);
                host.GetSetMethod(true).Invoke(session, new object[] { false }); sample.Assert(105.5f); assertSaved();
            });
        }

        private static void RunBatteryReadCalculations(Action<string, Action> check, List<NativeBatteryReader> readers,
            List<object> rows, Action<byte, float, float> receive, Action assertSaved)
        {
            foreach (string stateName in new[] { "Check charge", "Voltage" })
            {
                var reader = readers.Find(r => r.State == stateName)!;
                string path = stateName == "Voltage" ? "CORRIS/Simulation/Systems/Cooling/RadiatorFan" : "CORRIS/Functions/InteriorLight/Light";
                var row = NativeBagPartChecks.Find(rows, path, reader.Fsm.FsmName);
                var state = NativeBagPartChecks.State(reader.Fsm, stateName); var originalActions = state.Actions;
                var compare = NativeAction((Dictionary<string, object>)((List<object>)FindState(row, stateName)["actions"])[1], reader.Fsm);
                var proceed = new FsmState(reader.Fsm.Fsm) { Name = "Probe proceed", Actions = new FsmStateAction[] { new EntryCounter() }, Transitions = new FsmTransition[0] };
                var off = new FsmState(reader.Fsm.Fsm) { Name = "Probe off", Actions = new FsmStateAction[] { new EntryCounter() }, Transitions = new FsmTransition[0] };
                reader.Fsm.Fsm.States = new List<FsmState>(reader.Fsm.Fsm.States) { proceed, off }.ToArray();
                foreach (var terminal in new[] { proceed, off }) terminal.Actions[0].Init(terminal);
                state.Actions = new[] { reader.Action, compare }; foreach (var action in state.Actions) action.Init(state);
                state.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("PROCEED"), ToState = proceed.Name },
                    new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = off.Name } };
                try
                {
                    foreach (float charge in new[] { 70f, 90f })
                        check("battery reads: native " + stateName + " branch uses host charge " + charge, () =>
                        {
                            receive(3, charge, 147.25f); reader.Fire();
                            Require(reader.Fsm.ActiveStateName == (charge == 90 ? proceed.Name : off.Name), "Native voltage comparison used the saved battery."); assertSaved();
                        });
                }
                finally { state.Actions = originalActions; state.Transitions = new FsmTransition[0]; }
            }
            var amps = readers.Find(r => r.Scalar == "ChargeMax")!;
            var ampsRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Electricity/PowerON", "Amps");
            var ampsState = NativeBagPartChecks.State(amps.Fsm, "State 3"); var previous = ampsState.Actions;
            var raw = (List<object>)FindState(ampsRow, "State 3")["actions"];
            var actions = new List<FsmStateAction>(); foreach (Dictionary<string, object> action in raw) actions.Add(NativeAction(action, amps.Fsm));
            ampsState.Actions = actions.ToArray(); foreach (var action in actions) action.Init(ampsState);
            try
            {
                foreach (float charge in new[] { -1f, 100f, 200f })
                    check("battery reads: native current calculation clamps host charge " + charge + " without a saved write", () =>
                    {
                        receive(3, charge, 147.25f); amps.Fire();
                        Require(amps.Fsm.FsmVariables.FindFsmFloat("ChargeOld").Value == Mathf.Clamp(charge, 0, 147.25f)
                            && amps.Fsm.FsmVariables.FindFsmFloat("ChargeMax").Value == 147.25f,
                            "Native current calculation did not use host charge and maximum."); assertSaved();
                    });
            }
            finally { ampsState.Actions = previous; }
        }
    }
}
