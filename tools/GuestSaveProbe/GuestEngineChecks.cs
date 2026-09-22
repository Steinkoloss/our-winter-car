using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
        private static readonly MethodInfo Import = typeof(NativeBagPartChecks).GetMethod("ReadAction", Static);
        private sealed class Writer
        {
            internal object Rule = null!;
            internal Dictionary<string, object> Row = null!;
            internal PlayMakerFSM Fsm = null!;
            internal readonly List<FsmStateAction> Selected = new List<FsmStateAction>();
        }

        internal static void Run(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
            var profile = catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null);
            var inputsProperty = catalog.GetProperty("GuestEngineInputs", Static);
            var inputs = inputsProperty.GetValue(null, null);
            var inputCallback = Guard.GetField("PrepareInputs", Static).GetValue(null);
            var inputOwner = (inputCallback as Delegate)?.Target;
            var saveGuard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
            var policy = saveGuard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members);
            bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            var source = ReadInput(); var rows = (List<object>)source["fsms"];
            var scene = new GameObject("CORRIS"); scene.SetActive(false);
            var targets = new GameObject("engine probe saved targets"); targets.SetActive(false);
            var writers = new List<Writer>(); var allScalars = new List<FsmFloat>();
            var targetObjects = new Dictionary<string, GameObject>();
            Func<string, GameObject> target = name =>
            {
                if (targetObjects.TryGetValue(name, out var present)) return present;
                var obj = Child(targets, name); var data = Empty(obj, "Data");
                var floats = new List<FsmFloat>();
                foreach (string scalar in new[] { "Wear", "TireHealth", "Oil", "OilLevel", "OilContamination", "OilViscosity", "Dirt", "Water", "Coolant", "SparkAngle", "Durability", "Tightness", "TightnessMax", "Charge", "ChargeMax" })
                { var value = new FsmFloat { Name = scalar, UseVariable = true, Value = scalar == "Wear" ? 90 : 1 }; floats.Add(value); allScalars.Add(value); }
                data.FsmVariables.FloatVariables = floats.ToArray();
                data.FsmVariables.IntVariables = new[] { new FsmInt { Name = "DamageType", UseVariable = true } };
                targetObjects.Add(name, obj); return obj;
            };
            try
            {
                // This fixture isolates native write protection; its partial Cylinders
                // graph omits readers exercised by GuestEngineInputChecks.
                inputOwner?.GetType().GetMethod("RestoreGuestEngineInputs", Members).Invoke(inputOwner, null);
                inputsProperty.GetSetMethod(true).Invoke(null, new[] { Activator.CreateInstance(inputs.GetType(), true) });
                // A disposable policy transition gives native callers a real solo warm-up
                // before the same already-running graphs become protected guest graphs.
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false });
                foreach (object rule in (IEnumerable)Get(profile, "Writers"))
                {
                    string path = (string)Get(rule, "Path"), name = (string)Get(rule, "Fsm");
                    var row = NativeBagPartChecks.Find(rows, path, name); var fsm = NativeBagPartChecks.MakeFsm(PathObject(scene, path), row);
                    var writer = new Writer { Rule = rule, Row = row, Fsm = fsm }; writers.Add(writer);
                    foreach (var reference in fsm.FsmVariables.GameObjectVariables)
                        if (reference.Name.Length != 0) reference.Value = target(reference.Name == "ThisTire" ? fsm.gameObject.name + " saved tyre" : reference.Name);
                    AddFloat(fsm, "RPM", 1800);

                }
                var wearing = Find(writers, "Wearing"); var clutch = Find(writers, "Wear"); var cylinders = Find(writers, "Cylinders");
                var pressure = Empty(wearing.Fsm.gameObject, "Pressure"); AddFloat(pressure, "OilPressureBar", 2.5f); AddFloat(pressure, "PressureLeak", 0);
                var mesh = Child(target("Distributor"), "native distributor mesh"); mesh.transform.localEulerAngles = new Vector3(0, 0, 10);
                cylinders.Fsm.FsmVariables.FindFsmGameObject("DistributorMesh").Value = mesh;
                var fallRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Systems/PartFallings", "Logic");
                var falling = NativeBagPartChecks.MakeFsm(PathObject(scene, (string)fallRow["path"]), fallRow);
                var boltsObject = target("loose original");
                falling.FsmVariables.FindFsmGameObject("Part").Value = boltsObject;
                falling.FsmVariables.FindFsmGameObject("ArrayPart").Value = boltsObject;
                var loosen = NativeBagPartChecks.State(falling, "Loosen part");
                scene.SetActive(true); targets.SetActive(true);
                foreach (var writer in writers) LoadWriterActions(writer);
                var nativeSet = NativeArraySet(falling, fallRow); loosen.Actions = new[] { nativeSet }; nativeSet.Init(loosen);
                loosen.Transitions = new FsmTransition[0];
                var bolts = AddArray(boltsObject, "Bolts", new ArrayList { 8 });
                foreach (var writer in writers) NativeBagPartChecks.Start(writer.Fsm);
                NativeBagPartChecks.Start(falling);
                Func<bool> prepare = () => (bool)Call("Prepare", true)!;
                var clutchWear = Data(target("db_Clutchdisc")).FsmVariables.FindFsmFloat("Wear");
                var clutchState = NativeBagPartChecks.State(clutch.Fsm, "Get data"); var activeWrite = clutchState.Actions[14];
                var heat = clutch.Fsm.FsmVariables.FindFsmFloat("Clutchheat");
                check("guest engine: solo preparation leaves every native writer enabled", () =>
                { Require(prepare() && EverySelected(writers, a => a.Enabled) && falling.enabled, "Solo engine calculations were suppressed."); });
                check("guest engine: installed oil wear math reduces native mount condition", () =>
                {
                    wearing.Fsm.FsmVariables.FindFsmFloat("Wear1").Value = 1;
                    wearing.Fsm.FsmVariables.FindFsmFloat("Wear2").Value = 2;
                    float before = Data(target("db_MainBearing1")).FsmVariables.FindFsmFloat("Wear").Value;
                    NativeBagPartChecks.Fire(wearing.Fsm, "Parts");
                    Require(Data(target("db_MainBearing1")).FsmVariables.FindFsmFloat("Wear").Value < before
                        && wearing.Fsm.ActiveStateName == "Wait", "Native positive wear path or its sibling calculations did not run.");
                });
                check("guest engine: native ongoing clutch wear is warmed before guest protection", () =>
                {
                    clutch.Fsm.FsmVariables.FindFsmFloat("Wear").Value = 100; heat.Value = 100;
                    float before = clutchWear.Value; NativeBagPartChecks.Fire(clutch.Fsm, "Get data"); Tick(clutch.Fsm);
                    Require(clutchState.ActiveActions.Contains(activeWrite) && clutchWear.Value < before && heat.Value < 100,
                        "Fixture did not warm an active native everyFrame writer and calculation sibling.");
                });
                check("guest engine: native active dispatcher still runs an action disabled after entry", () =>
                {
                    float before = clutchWear.Value; activeWrite.Enabled = false;
                    try
                    {
                        Tick(clutch.Fsm);
                        Require(clutchWear.Value < before && clutchState.ActiveActions.Contains(activeWrite),
                            "Native dispatch premise changed; Enabled-only regression needs reevaluation.");
                    }
                    finally { activeWrite.Enabled = true; }
                });
                check("guest engine: native part falling baseline changes the saved bolts array", () =>
                {
                    falling.FsmVariables.FindFsmInt("SelectedBolt").Value = 3;
                    NativeBagPartChecks.Fire(falling, "Loosen part"); Require((int)bolts[0] == 3, "Native ArrayListSet baseline did not write Bolts."); bolts[0] = 8;
                });
                RunConditionWritesSolo(check, writers);
                RunDrivetrainWritesSolo(check, writers);
                check("guest engine: guest admission synchronously retires active writes without pausing math", () =>
                {
                    Require((bool)saveGuard.GetMethod("TryBeginGuest", Static).Invoke(null, null),
                        "Guest admission did not validate installed writer metadata.");
                    Require(EverySelected(writers, a => !a.Enabled) && !clutchState.ActiveActions.Contains(activeWrite)
                        && clutch.Fsm.enabled && !falling.enabled && !falling.Fsm.RestartOnEnable,
                        "Preparation left an active write or disabled an entire calculation graph.");
                });
                RunConditionWritesProtected(check, writers, prepare);
                RunDrivetrainWritesProtected(check, writers, prepare);
                check("guest engine: warmed update preserves wear while native cooling calculation continues", () =>
                {
                    float before = clutchWear.Value; heat.Value = 100; Tick(clutch.Fsm);
                    Require(clutchWear.Value == before && heat.Value < 100 && clutchState.ActiveActions.Contains(clutchState.Actions[15]),
                        "Already-active writer continued or unrelated native math stopped.");
                });
                check("guest engine: protected oil loop preserves every part scalar and still reaches its wait", () =>
                {
                    var snapshot = Snapshot(allScalars); NativeBagPartChecks.Fire(wearing.Fsm, "Parts");
                    Require(Same(allScalars, snapshot) && wearing.Fsm.ActiveStateName == "Wait" && wearing.Fsm.enabled,
                        "Native oil loop wrote protected parts or lost its FINISHED flow.");
                });
                check("guest engine: native global event and reentry cannot restore clutch wear", () =>
                {
                    float before = clutchWear.Value; clutch.Fsm.SendEvent("CLUTCHON"); clutch.Fsm.SendEvent("CLUTCHOFF");
                    NativeBagPartChecks.Fire(clutch.Fsm, "Get data"); Tick(clutch.Fsm);
                    Require(clutchWear.Value == before && !activeWrite.Enabled && !clutchState.ActiveActions.Contains(activeWrite),
                        "Global event or state reentry restarted protected wear.");
                });
                check("guest engine: saved distributor timing and mesh pose both remain unchanged", () =>
                {
                    var before = mesh.transform.localRotation; float scalar = Data(target("Distributor")).FsmVariables.FindFsmFloat("SparkAngle").Value;
                    cylinders.Fsm.FsmVariables.FindFsmFloat("Angle").Value = 18; NativeBagPartChecks.Fire(cylinders.Fsm, "Random move");
                    Require(mesh.transform.localRotation == before && Data(target("Distributor")).FsmVariables.FindFsmFloat("SparkAngle").Value == scalar,
                        "Distributor drift changed a saved scalar or its owned mesh.");
                });
                check("guest engine: destructive part falling cannot alter bolts through direct events", () =>
                {
                    falling.SendEvent("LOOSENBOLT"); NativeBagPartChecks.Fire(falling, "Loosen part"); Tick(falling);
                    Require((int)bolts[0] == 8 && !falling.enabled, "Paused falling graph changed the saved Bolts list.");
                });
                check("guest engine: repaired falling graph remains permanently paused", () =>
                {
                    var states = falling.Fsm.States;
                    try
                    {
                        var changed = (FsmState[])states.Clone();
                        for (int i = 0; i < changed.Length; i++)
                            if (changed[i].Name == "Loosen tire") changed[i] = new FsmState(falling.Fsm) { Name = "Changed tire", Actions = new FsmStateAction[0] };
                        falling.Fsm.States = changed; Require(!prepare() && !falling.enabled, "Malformed falling graph was accepted.");
                    }
                    finally { falling.Fsm.States = states; }
                    bool ready = prepare(); if (!ready) ready = prepare();
                    Require(ready && !falling.enabled && !falling.Fsm.RestartOnEnable,
                        "Recovering a transient failure restored destructive part falling.");
                });
                check("guest engine: native re-enable attempts are retired on the next protected entry", () =>
                {
                    activeWrite.Enabled = true; NativeBagPartChecks.Fire(clutch.Fsm, "Probe idle");
                    float before = clutchWear.Value; NativeBagPartChecks.Fire(clutch.Fsm, "Get data"); Tick(clutch.Fsm);
                    Require(!activeWrite.Enabled && clutchWear.Value == before, "A re-enabled known action escaped the native entry guard.");
                });
                RunLateGraph(check, targets, Find(writers, "FuelLine"), target, prepare);
                RunDormantGraph(check, catalog, profile, targets, Find(writers, "FuelLine"), target, prepare);
                check("guest engine: failed protection preserves a supported fitted saved original", () =>
                    SavedPartIsolation(catalog, profile, targets, false));
                check("guest engine: missing metadata fails readiness without restoring completed guards", () =>
                {
                    var property = catalog.GetProperty("GuestEngineProtection", Static);
                    try
                    {
                        property.GetSetMethod(true).Invoke(null, new object?[] { null });
                        Require(!prepare() && EverySelected(writers, a => !a.Enabled) && !falling.enabled,
                            "Missing metadata reported success or restored protected native writes.");
                    }
                    finally { property.GetSetMethod(true).Invoke(null, new[] { profile }); }
                    Require(prepare(), "Restored profile could not recover readiness.");
                });
                check("guest engine: disconnected session keeps protection latched", () =>
                {
                    var session = SessionManager.Instance; var stateProperty = typeof(SessionManager).GetProperty("State", Members);
                    object previous = stateProperty.GetValue(session, null);
                    try
                    {
                        stateProperty.GetSetMethod(true).Invoke(session, new object[] { SessionState.Idle });
                        float before = clutchWear.Value; Require(prepare(), "Disconnected protection not ready.");
                        NativeBagPartChecks.Fire(clutch.Fsm, "Get data"); Tick(clutch.Fsm);
                        Require(clutchWear.Value == before && !falling.enabled, "Disconnect restored native persistence writers.");
                    }
                    finally { stateProperty.GetSetMethod(true).Invoke(session, new[] { previous }); }
                });
            }
            finally
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected });
                UnityEngine.Object.DestroyImmediate(scene); UnityEngine.Object.DestroyImmediate(targets);
                Call("Prepare", true);
                inputOwner?.GetType().GetMethod("RestoreGuestEngineInputs", Members).Invoke(inputOwner, null);
                inputsProperty.GetSetMethod(true).Invoke(null, new[] { inputs });
                Guard.GetField("PrepareInputs", Static).SetValue(null, inputCallback);
            }
            RunExternalBatteryWrites(check);
            RunBatteryReads(check);
            RunHeaterInputs(check);
            RunHeaterWiring(check);
            RunRearWindowHeater(check);
            RunStarterDraw(check);
        }

        private static void RunDormantGraph(Action<string, Action> check, Type catalog, object profile, GameObject targets,
            Writer source, Func<string, GameObject> target, Func<bool> prepare)
        {
            var seedObject = Child(targets, "serialized dormant engine seed"); seedObject.SetActive(false);
            var seed = NativeBagPartChecks.MakeFsm(seedObject, source.Row);
            seed.Fsm.Init(seed);
            var state = NativeBagPartChecks.State(seed, "Airfilter");
            var row = FindState(source.Row, "Airfilter"); var raw = (List<object>)row["actions"];
            seed.FsmVariables.FindFsmGameObject("db_Carb1").Value = target("dormant carb");
            var filter = target("dormant filter");
            Data(filter).FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
            seed.FsmVariables.FindFsmGameObject("db_Airfilter").Value = filter;
            seed.FsmVariables.FindFsmBool("Installed1").Value = false;
            state.Actions = new[] { NativeAction((Dictionary<string, object>)raw[0], seed), NativeAction((Dictionary<string, object>)raw[1], seed) };
            foreach (var action in state.Actions) action.Init(state);
            state.Transitions = new FsmTransition[0]; seed.Fsm.StartState = "Airfilter"; seed.enabled = true;
            var savedData = Data(target("dormant carb"));
            float wear = savedData.FsmVariables.FindFsmFloat("Wear").Value, oil = savedData.FsmVariables.FindFsmFloat("Oil").Value;
            string originalName = source.Fsm.gameObject.name;
            GameObject? clone = null;
            Func<PlayMakerFSM> create = () =>
            {
                foreach (var s in seed.Fsm.States) s.SaveActions();
                clone = (GameObject)UnityEngine.Object.Instantiate(seedObject);
                clone.transform.SetParent(source.Fsm.transform.parent, false); clone.name = originalName;
                var result = clone.GetComponent<PlayMakerFSM>();
                Require(!clone.activeInHierarchy && !result.Fsm.Initialized && !result.Fsm.Started
                    && !NativeBagPartChecks.State(result, "Airfilter").ActionsLoaded, "Fixture did not reproduce a never-awakened engine.");
                return result;
            };
            Func<bool> admit = () => (bool)Call("PrepareForAdmission")!;
            Action saved = () => Require(savedData.FsmVariables.FindFsmFloat("Wear").Value == wear
                && savedData.FsmVariables.FindFsmFloat("Oil").Value == oil, "Dormant preparation changed a saved engine scalar.");
            try
            {
                source.Fsm.gameObject.name = "dormant original engine";
                var fsm = create(); var pending = NativeBagPartChecks.State(fsm, "Airfilter");
                check("dormant engine: admission retains a paused serialized writer without decoding or starting it", () =>
                {
                    Require(admit() && !fsm.enabled && !fsm.Fsm.RestartOnEnable && !fsm.Fsm.Initialized
                        && !fsm.Fsm.Started && !pending.ActionsLoaded && !clone!.activeSelf,
                        "An unawakened writer blocked safe admission or ran during preparation."); saved();
                });
                check("dormant engine: containment does not claim simulation readiness", () =>
                { for (int i = 0; i < 5; i++) Require(!prepare() && admit(), "Containment and simulation readiness were conflated."); Require(!pending.ActionsLoaded, "Repeated readiness decoded dormant actions."); saved(); });
                check("dormant engine: activation readiness retains the pause without claiming simulation readiness", () =>
                {
                    Require(ActivationReady(true) && !prepare(), "Activation and simulation readiness were conflated.");
                    var deadline = Guard.GetField("_nextScanAt", Static); float previous = (float)deadline.GetValue(null);
                    try
                    {
                        float future = Time.unscaledTime + 100f; deadline.SetValue(null, future);
                        Require(ActivationReady(false) && (float)deadline.GetValue(null) == future,
                            "Paused activation readiness forced discovery or required a running engine.");
                        Require(!fsm.enabled && !fsm.Fsm.Initialized && !pending.ActionsLoaded && !clone!.activeSelf,
                            "Readiness itself awakened the serialized engine."); saved();
                    }
                    finally { deadline.SetValue(null, previous); }
                });
                check("dormant engine: a native re-enable attempt remains paused before activation", () =>
                { fsm.enabled = true; Require(admit() && !fsm.enabled && !pending.ActionsLoaded, "Re-enable escaped dormant containment."); saved(); });
                check("dormant engine: supported guest parts can be isolated while native writers remain paused", () =>
                {
                    SavedPartIsolation(catalog, profile, targets, true);
                    Require(!fsm.enabled && !fsm.Fsm.Initialized && !pending.ActionsLoaded,
                        "Saved-part isolation initialized or resumed the dormant engine."); saved();
                });
                check("dormant engine: changed identity cannot borrow pending admission", () =>
                {
                    clone!.name = "changed dormant engine";
                    try { Require(!admit() && !ActivationReady(false)
                        && !fsm.enabled && !pending.ActionsLoaded, "Changed identity retained pending admission or activation."); saved(); }
                    finally { clone.name = originalName; }
                    Require(admit(), "Restored dormant identity did not recover containment.");
                });
                check("dormant engine: missing metadata cannot admit a paused unresolved writer", () =>
                {
                    var property = catalog.GetProperty("GuestEngineProtection", Static);
                    try { property.GetSetMethod(true).Invoke(null, new object?[] { null }); Require(!admit()
                        && !ActivationReady(false) && !fsm.enabled, "Missing metadata admitted or activated a dormant graph."); saved(); }
                    finally { property.GetSetMethod(true).Invoke(null, new[] { profile }); }
                    Require(admit(), "Restored metadata did not recover dormant containment.");
                });
                check("dormant engine: replaced state definitions cannot reuse pending admission", () =>
                {
                    var original = fsm.Fsm.States[0];
                    fsm.Fsm.States[0] = new FsmState((Fsm)null!) { Name = original.Name };
                    try { Require(!admit() && !ActivationReady(false)
                        && !fsm.enabled && !pending.ActionsLoaded, "Replaced definitions inherited dormant admission or activation."); saved(); }
                    finally { fsm.Fsm.States[0] = original; }
                    Require(admit(), "Restored definitions did not recover containment.");
                });
                check("dormant engine: a missing required native state is rejected without decoding", () =>
                {
                    string name = pending.Name; pending.Name = "missing native entry";
                    try { Require(!admit() && !pending.ActionsLoaded, "Missing required state was treated as pending."); saved(); }
                    finally { pending.Name = name; }
                    Require(admit(), "Restored required state did not recover containment.");
                });
                check("dormant engine: native activation validates writes before resuming its first entry", () =>
                {
                    using (var ignition = new DormantIgnition(clone!))
                    {
                        ignition.Apply(true);
                        Require(ignition.Applied(true) && clone!.activeSelf,
                            "Remote ignition could not awaken a safely contained dormant engine. " + ignition.Status);
                    }
                    Require(fsm.Fsm.Initialized && !fsm.enabled && !fsm.Fsm.Started, "Activation bypassed the retained pause.");
                    bool ready = prepare(); if (!ready) ready = prepare(); Require(ready && fsm.enabled, "Initialized writer did not recover.");
                    NativeBagPartChecks.Start(fsm);
                    Require(!pending.Actions[0].Enabled && fsm.FsmVariables.FindFsmBool("Installed1").Value,
                        "Recovered native entry lost its safe read or retained its saved-part write."); saved();
                });
                UnityEngine.Object.DestroyImmediate(clone); clone = null; prepare();
                state.Actions[0].GetType().GetField("variableName").SetValue(state.Actions[0], new FsmString { Value = "Oil" });
                fsm = create();
                check("dormant engine: a changed serialized write remains blocked when it awakens", () =>
                {
                    Require(admit() && !fsm.enabled, "Dormant altered writer was not durably paused.");
                    using (var ignition = new DormantIgnition(clone!))
                    {
                        ignition.Apply(true); Require(ignition.Applied(true) && clone!.activeSelf, "Containment blocked native awakening. " + ignition.Status);
                    }
                    Require(!prepare() && !fsm.enabled, "Awakening admitted a changed native write.");
                    fsm.Fsm.Start(); Require(!fsm.enabled && !admit(), "Direct native start bypassed signature validation."); saved();
                });
                UnityEngine.Object.DestroyImmediate(clone); clone = null;
                check("dormant engine: destroyed deferred graphs do not block later admission", () =>
                { bool ready = admit(); if (!ready) ready = admit(); Require(ready, "Destroyed deferred graph retained its admission failure."); saved(); });
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(seedObject); source.Fsm.gameObject.name = originalName; prepare();
            }
        }

        private sealed class DormantIgnition : IDisposable
        {
            private readonly GameObject _object;
            private readonly PlayMakerFSM _power;
            private readonly object _item;
            private readonly MethodInfo _apply;

            internal DormantIgnition(GameObject engine)
            {
                // Power is already awake; only the protected engine under test
                // uses serialized actions that native activation must initialize.
                _object = new GameObject("dormant ignition probe");
                _power = Empty(_object, "Power");
                var acc = new FsmBool { Name = "ACC", UseVariable = true };
                _power.FsmVariables.BoolVariables = new[] { acc };
                _power.Fsm.States = new[] {
                    new FsmState(_power.Fsm) { Name = "OFF", Actions = new FsmStateAction[0] },
                    new FsmState(_power.Fsm) { Name = "ON", Actions = new FsmStateAction[0] } };
                _power.Fsm.StartState = "OFF"; _power.Fsm.Init(_power);
                acc = _power.FsmVariables.FindFsmBool("ACC");
                var native = Assembly.Load("Assembly-CSharp");
                foreach (var state in _power.Fsm.States)
                {
                    bool on = state.Name == "ON";
                    var set = (FsmStateAction)Activator.CreateInstance(native.GetType("HutongGames.PlayMaker.Actions.SetBoolValue", true));
                    set.Reset(); set.Enabled = true;
                    Set(set, "boolVariable", acc); Set(set, "boolValue", new FsmBool(on)); Set(set, "everyFrame", false);
                    var activate = (FsmStateAction)Activator.CreateInstance(native.GetType("HutongGames.PlayMaker.Actions.ActivateGameObject", true));
                    activate.Reset(); activate.Enabled = true;
                    Set(activate, "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = engine } });
                    Set(activate, "activate", new FsmBool(on));
                    state.Actions = new[] { set, activate };
                    foreach (var action in state.Actions) action.Init(state);
                }
                NativeBagPartChecks.Start(_power);
                Require(_power.Fsm.Initialized && _power.Fsm.Started && _object.activeInHierarchy,
                    "Ignition fixture did not initialize its native power controller.");
                _item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                Set(_item, "Path", "dormant ignition probe"); Set(_item, "ElectricityPowerFsm", _power);
                _apply = Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true).GetMethod("ApplyRemoteElectricity", Static);
            }

            internal void Apply(bool on) => _apply.Invoke(null, new[] { _item, (object)on });
            internal bool Applied(bool on) => (bool)Get(_item, "HasRemoteElectricsState")
                && (bool)Get(_item, "RemoteElectricsApplied") == on && _power.ActiveStateName == (on ? "ON" : "OFF")
                && _power.FsmVariables.FindFsmBool("ACC").Value == on;
            internal string Status
            {
                get
                {
                    var actions = NativeBagPartChecks.State(_power, "ON").Actions;
                    return "state=" + _power.ActiveStateName + "; ACC=" + _power.FsmVariables.FindFsmBool("ACC").Value
                        + "; applied=" + Get(_item, "HasRemoteElectricsState") + "; actions=" + actions.Length;
                }
            }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(_object);
        }

        private static bool ActivationReady(bool force)
            // Older binaries use simulation readiness here and reproduce the block.
            => (bool)(Guard.GetMethod("PrepareForActivation", Static) ?? Guard.GetMethod("Prepare", Static))
                .Invoke(null, new object[] { force });

        private static void LoadWriterActions(Writer writer)
        {
            var rule = writer.Rule; var row = writer.Row; var fsm = writer.Fsm;
            string name = fsm.FsmName, path = (string)Get(rule, "Path");
            bool drivetrain = path == "CORRIS/Simulation/Systems/Drivetrain" && name == "Wear";
            foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
            {
                string stateName = (string)stateRow["name"]; var state = NativeBagPartChecks.State(fsm, stateName);
                var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                for (int index = 0; index < raw.Count; index++)
                {
                    bool selected = Selected(rule, stateName, index);
                    bool math = name == "Wearing" || (path.EndsWith("/Clutch", StringComparison.Ordinal) && name == "Wear" && stateName == "Get data" && index == 15)
                        || drivetrain && !(stateName == "State 1" && index == 0);
                    bool tyreRead = name == "Condition" && (stateName == "State 1" && index == 12 || stateName == "Flat friction" && index == 3);
                    bool gearboxRead = fsm.gameObject.name == "GearboxDamage" && name == "Damage" && stateName == "Damage type" && index == 0;
                    bool drivetrainRead = false;
                    foreach (object read in (IEnumerable)Get(rule, "DrivetrainWearReads"))
                        drivetrainRead |= (string)Get(read, "State") == stateName && (int)Get(read, "Index") == index;
                    var oil = Get(rule, "GearboxOilRead");
                    drivetrainRead |= oil != null && (string)Get(oil, "State") == stateName && (int)Get(oil, "Index") == index;
                    actions[index] = selected || math || tyreRead || gearboxRead || drivetrainRead ? NativeAction((Dictionary<string, object>)raw[index], fsm) : new Quiet();
                    if (selected) writer.Selected.Add(actions[index]);
                }
                state.Actions = actions;
                foreach (var action in actions) { action.Enabled = true; action.Init(state); }
                if (name != "Wearing" && name != "Condition" && !drivetrain) state.Transitions = new FsmTransition[0];
            }
        }

        private static void RunLateGraph(Action<string, Action> check, GameObject targets,
            Writer source, Func<string, GameObject> target, Func<bool> prepare)
        {
            var obj = Child(targets, "late engine"); obj.SetActive(false);
            string sourceName = source.Fsm.gameObject.name;
            var fsm = NativeBagPartChecks.MakeFsm(obj, source.Row); var state = NativeBagPartChecks.State(fsm, "Airfilter");
            var stateRow = FindState(source.Row, "Airfilter");
            Func<FsmStateAction, FsmStateAction[]> actions = write => new[] { write,
                NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[1], fsm), new Quiet(),
                NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[3], fsm) };
            Action load = () =>
            {
                state.Actions = actions(NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[0], fsm));
                foreach (var action in state.Actions) action.Init(state); state.Transitions = new FsmTransition[0];
            };
            fsm.FsmVariables.FindFsmGameObject("db_Carb1").Value = target("late carb");
            var airfilter = target("late airfilter");
            Data(airfilter).FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
            fsm.FsmVariables.FindFsmGameObject("db_Airfilter").Value = airfilter;
            var sibling = fsm.FsmVariables.FindFsmBool("Installed1");
            var wear = Data(target("late carb")).FsmVariables.FindFsmFloat("Wear");
            try
            {
                check("guest engine: unrelated graph retains native writes despite guest protection", () =>
                {
                    obj.SetActive(true); load(); NativeBagPartChecks.Start(fsm); float before = wear.Value;
                    NativeBagPartChecks.Fire(fsm, "Airfilter"); Require(wear.Value < before && state.Actions[0].Enabled,
                        "Exact path protection disabled an unrelated native writer.");
                });
                check("guest engine: late graph is guarded on its first native entry before scanning", () =>
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                    source.Fsm.gameObject.name = sourceName + " earlier fixture";
                    obj = Child(source.Fsm.transform.parent.gameObject, sourceName); obj.SetActive(false);
                    fsm = NativeBagPartChecks.MakeFsm(obj, source.Row); state = NativeBagPartChecks.State(fsm, "Airfilter");
                    fsm.FsmVariables.FindFsmGameObject("db_Carb1").Value = target("late carb");
                    fsm.FsmVariables.FindFsmGameObject("db_Airfilter").Value = airfilter;
                    sibling = fsm.FsmVariables.FindFsmBool("Installed1");
                    obj.SetActive(true); load();
                    float before = wear.Value; NativeBagPartChecks.Start(fsm); NativeBagPartChecks.Fire(fsm, "Airfilter");
                    Require(wear.Value == before && !state.Actions[0].Enabled, "Late writer entered before the next scan.");
                });
                check("guest engine: changed action instance is refused before its first native write", () =>
                {
                    var changed = NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[0], fsm);
                    changed.GetType().GetField("variableName").SetValue(changed, new FsmString { Value = "Oil", UseVariable = false });
                    state.Actions = actions(changed); foreach (var action in state.Actions) action.Init(state);
                    sibling.Value = false; float before = Data(target("late carb")).FsmVariables.FindFsmFloat("Oil").Value;
                    NativeBagPartChecks.Fire(fsm, "Airfilter");
                    Require(!fsm.enabled && !prepare() && Data(target("late carb")).FsmVariables.FindFsmFloat("Oil").Value == before,
                        "Changed write target executed or partial failure allowed isolation readiness.");
                });
                check("guest engine: failed graph re-enable is contained inside the scan interval", () =>
                {
                    fsm.Fsm.RestartOnEnable = false; fsm.enabled = true;
                    Require(!(bool)Call("Prepare", false)! && !fsm.enabled,
                        "Failed-only native graph ran until the next discovery scan.");
                });
                check("guest engine: repaired native signature recovers only with writes disabled", () =>
                {
                    var repaired = NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[0], fsm);
                    state.Actions = actions(repaired); foreach (var action in state.Actions) action.Init(state); sibling.Value = false;
                    bool ready = prepare(); if (!ready) ready = prepare();
                    Require(ready && !repaired.Enabled && sibling.Value,
                        "Repaired entry did not resume its native sibling or resumed its protected write.");
                    float before = wear.Value; fsm.enabled = true; NativeBagPartChecks.Fire(fsm, "Airfilter");
                    Require(wear.Value == before, "Recovered graph applied a saved-part write.");
                });
                check("guest engine: inactive repaired entry resumes once without stale exit or event callbacks", () =>
                {
                    bool restart = fsm.Fsm.RestartOnEnable; var entry = new EntryCounter();
                    var nativeRow = (Dictionary<string, object>)((List<object>)stateRow["actions"])[0];
                    try
                    {
                        fsm.Fsm.RestartOnEnable = false;
                        state.Actions = actions(NativeAction(nativeRow, fsm)); state.Actions[2] = entry;
                        foreach (var action in state.Actions) action.Init(state);
                        NativeBagPartChecks.Fire(fsm, "Airfilter");
                        Require(entry.Entries == 1, "Recovery fixture did not establish an entered sibling.");
                        var changed = NativeAction(nativeRow, fsm);
                        changed.GetType().GetField("variableName").SetValue(changed, new FsmString { Value = "Oil", UseVariable = false });
                        state.Actions[0] = changed; changed.Init(state); sibling.Value = false;
                        NativeBagPartChecks.Fire(fsm, "Airfilter");
                        Require(!fsm.enabled, "Changed signature did not block its native entry.");
                        obj.SetActive(false);
                        int exits = entry.Exits, events = entry.Events;
                        var repaired = NativeAction(nativeRow, fsm); state.Actions[0] = repaired; repaired.Init(state);
                        bool ready = prepare(); if (!ready) ready = prepare();
                        Require(ready && !sibling.Value && entry.Entries == 1 && !repaired.Enabled,
                            "Repair entered an inactive native graph or left its write enabled.");
                        RunDeferredResumeChecks(check, fsm, state, entry, wear);
                        float before = wear.Value; obj.SetActive(true); Require(prepare(), "Reactivated repair stayed unavailable.");
                        Require(sibling.Value && entry.Entries == 2 && entry.Exits == exits && entry.Events == events
                            && !repaired.Enabled && wear.Value == before,
                            "Deferred entry was lost, duplicated, or replayed stale native callbacks: sibling=" + sibling.Value
                            + ", entries=" + entry.Entries + ", exits=" + entry.Exits + "/" + exits + ", events=" + entry.Events + "/" + events
                            + ", enabled=" + repaired.Enabled + ", wear=" + wear.Value + "/" + before + ".");
                        Require(prepare() && entry.Entries == 2, "Completed entry was replayed by the next preparation.");
                    }
                    finally
                    {
                        obj.SetActive(true); fsm.Fsm.RestartOnEnable = restart;
                        state.Actions = actions(NativeAction(nativeRow, fsm)); foreach (var action in state.Actions) action.Init(state);
                        prepare();
                    }
                });
                check("guest engine: in-place state replacement is guarded before its native entry", () =>
                {
                    var replacement = new FsmState(fsm.Fsm) { Name = "Airfilter", Transitions = new FsmTransition[0] };
                    var action = NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[0], fsm);
                    replacement.Actions = actions(action); foreach (var entry in replacement.Actions) entry.Init(replacement);
                    for (int i = 0; i < fsm.Fsm.States.Length; i++) if (fsm.Fsm.States[i].Name == "Airfilter") fsm.Fsm.States[i] = replacement;
                    float before = wear.Value; NativeBagPartChecks.Fire(fsm, "Probe idle"); NativeBagPartChecks.Fire(fsm, "Airfilter");
                    Require(!action.Enabled && wear.Value == before, "Changing an array element bypassed cached graph validation.");
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); source.Fsm.gameObject.name = sourceName; prepare(); }
        }

        private static PlayMakerFSM? _resumeLookupFsm;
        private static int _resumeLookups;
        private static void CountResumeLookup(PlayMakerFSM fsm) { if (ReferenceEquals(fsm, _resumeLookupFsm)) _resumeLookups++; }

        private static void RunDeferredResumeChecks(Action<string, Action> check, PlayMakerFSM fsm,
            FsmState state, EntryCounter entry, FsmFloat wear)
        {
            var blocked = (IDictionary)Guard.GetField("BlockedEntries", Static).GetValue(null);
            var deadline = Guard.GetField("_nextScanAt", Static); object previousDeadline = deadline.GetValue(null);
            var harmony = new Harmony("wintermp.probe.deferred-resume");
            var lookup = Guard.GetMethod("FindRule", Static);
            bool enabled = fsm.enabled, active = fsm.gameObject.activeSelf;
            string name = fsm.gameObject.name; float savedWear = wear.Value;
            int entries = entry.Entries, exits = entry.Exits, events = entry.Events;
            Action preserved = () => Require(entry.Entries == entries && entry.Exits == exits && entry.Events == events
                && wear.Value == savedWear && !state.Actions[0].Enabled && ReferenceEquals(blocked[fsm], state),
                "Deferred entry ran, lost its pending state, or changed saved wear.");
            Action routine = () => { _resumeLookups = 0; Call("Prepare", false); };
            try
            {
                Require(!active && enabled && ReferenceEquals(fsm.Fsm.ActiveState, state) && ReferenceEquals(blocked[fsm], state),
                    "Resume fixture must begin with a repaired inactive entry.");
                deadline.SetValue(null, Time.unscaledTime + 60f);
                _resumeLookupFsm = fsm;
                harmony.Patch(lookup, prefix: new HarmonyMethod(typeof(GuestEngineChecks).GetMethod(nameof(CountResumeLookup), Static)));
                check("guest engine resume: inactive pending entry skips routine identity lookup", () =>
                {
                    for (int i = 0; i < 5; i++) { routine(); Require(_resumeLookups == 0, "Inactive entry repeated a resume-only identity lookup."); preserved(); }
                });
                check("guest engine resume: disabled component retains its entry after object activation", () =>
                {
                    fsm.enabled = false; fsm.gameObject.SetActive(true);
                    for (int i = 0; i < 5; i++) { routine(); Require(_resumeLookups == 0 && !fsm.enabled, "Disabled entry repeated resume validation or re-enabled itself."); preserved(); }
                });
                check("guest engine resume: activation rechecks the live consumer path", () =>
                {
                    fsm.gameObject.name = name + " changed";
                    try { fsm.enabled = true; routine(); Require(_resumeLookups > 0, "Activation skipped live identity validation."); preserved(); }
                    finally { fsm.enabled = false; fsm.gameObject.name = name; }
                });
                check("guest engine resume: activation rejects a replaced protected action", () =>
                {
                    var original = state.Actions[0]; var replacement = new Quiet(); replacement.Init(state);
                    try
                    {
                        state.Actions[0] = replacement; fsm.enabled = true; routine();
                        Require(_resumeLookups == 0 && entry.Entries == entries && wear.Value == savedWear
                            && ReferenceEquals(blocked[fsm], state), "Changed action resumed or bypassed graph validation.");
                    }
                    finally { fsm.enabled = false; state.Actions[0] = original; }
                    preserved();
                });
                check("guest engine resume: disabled stale entry still receives normal cleanup", () =>
                {
                    blocked[fsm] = NativeBagPartChecks.State(fsm, "Probe idle");
                    try
                    {
                        routine(); Require(_resumeLookups > 0 && !blocked.Contains(fsm) && entry.Entries == entries
                            && wear.Value == savedWear && !fsm.enabled, "Stale entry cleanup changed or ran native actions.");
                    }
                    finally { blocked[fsm] = state; }
                });
                check("guest engine resume: eligible entry resumes once between discovery scans", () =>
                {
                    fsm.enabled = true; routine();
                    Require(_resumeLookups > 0 && entry.Entries == entries + 1 && !blocked.Contains(fsm)
                        && entry.Exits == exits && entry.Events == events && wear.Value == savedWear && !state.Actions[0].Enabled,
                        "Eligible entry failed to resume once with saved writes protected.");
                    routine(); Require(entry.Entries == entries + 1 && wear.Value == savedWear, "Completed entry was replayed.");
                });
            }
            finally
            {
                harmony.UnpatchSelf(); _resumeLookupFsm = null;
                fsm.gameObject.name = name; fsm.gameObject.SetActive(active); fsm.enabled = enabled;
                deadline.SetValue(null, previousDeadline);
            }
        }

        private static FsmStateAction NativeArraySet(PlayMakerFSM fsm, Dictionary<string, object> source)
        {
            var row = (Dictionary<string, object>)((List<object>)FindState(source, "Loosen part")["actions"])[4];
            var clone = new Dictionary<string, object>(row); var parameters = new List<object>();
            foreach (Dictionary<string, object> parameter in (IEnumerable)row["parameters"])
                if ((string)parameter["type"] != "FsmVar") parameters.Add(parameter);
            clone["parameters"] = parameters; var action = NativeAction(clone, fsm);
            action.GetType().GetField("variable").SetValue(action, new FsmVar { NamedVar = fsm.FsmVariables.FindFsmInt("SelectedBolt") });
            return action;
        }
        private static void SavedPartIsolation(Type catalog, object profile, GameObject root, bool allowPending)
        {
            var itemsType = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
            var vehiclesType = Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true);
            var bridgeType = Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true);
            var world = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true).GetProperty("Instance", Static).GetValue(null, null)
                ?? throw new InvalidOperationException("Probe world missing.");
            var bridge = Activator.CreateInstance(bridgeType, Members, null,
                new object[] { world, new Dictionary<PlayMakerFSM, bool>() }, null);
            var items = Activator.CreateInstance(itemsType, Members, null, new[] { bridge }, null);
            var vehicles = Activator.CreateInstance(vehiclesType, Members, null, new[] { bridge, items }, null);
            var fsms = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.FsmWorldSync", true), Members, null,
                new[] { bridge, vehicles }, null);
            InstanceCall(bridge, "BindItems", items); InstanceCall(bridge, "BindFsms", fsms);
            InstanceCall(items, "BindVehicles", vehicles);
            var mountObject = Child(root, "preserved crank mount"); var mount = Empty(mountObject, "Data");
            var saved = Child(mountObject, "preserved original crank"); var data = Empty(saved, "Data");
            data.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1027" } };
            data.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = allowPending ? 0 : 1 } };
            if (allowPending) saved.AddComponent<Rigidbody>().isKinematic = true;
            data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Consumed", UseVariable = true } };
            data.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "InstallPoint", UseVariable = true, Value = mountObject } };
            var occupant = new FsmGameObject { Name = "ActivePart", UseVariable = true, Value = saved };
            mount.FsmVariables.GameObjectVariables = new[] { occupant }; AddFloat(mount, "Wear", 75);
            object? rule = null; var config = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null);
            foreach (object candidate in (IEnumerable)Get(config, "Factories")) if ((string)Get(candidate, "Prefix") == "VIN102") rule = candidate;
            if (rule == null) throw new InvalidOperationException("Crankshaft replacement rule missing.");
            var factory = Activator.CreateInstance(itemsType.GetNestedType("ReplacementFactory", BindingFlags.NonPublic), true);
            Set(factory, "Rule", rule); Set(factory, "Fsm", data);
            var suppressor = Get(factory, "Suppressor"); Require((bool)InstanceCall(suppressor, "Suppress", data)!, "Fixture factory was not paused.");
            var identity = (WinterMP.Net.Sync.ReplacementPartRule)Get(rule, "Identity");
            Require(identity.TryId("VIN1027", out uint id), "Fixture native identity rejected.");
            var parts = (IDictionary)Get(items, "_nativeParts"); parts.Add(id, data);
            ((IDictionary)Get(items, "_replacementFactories")).Add(identity.FactoryId, factory);
            var phase = Core.GetType("WinterMP.Core.Sync.NativePartIdentity", true).GetMethod("Phase", Static).Invoke(null, new object[] { data });
            Require(phase.ToString() == (allowPending ? "Loose" : "Fitted"), "Fixture did not reach the required supported part classification.");
            var property = catalog.GetProperty("GuestEngineProtection", Static);
            var inputCallback = Guard.GetField("PrepareInputs", Static).GetValue(null);
            try
            {
                if (!allowPending) property.GetSetMethod(true).Invoke(null, new object?[] { null });
                InstanceCall(items, "IsolateGuestParts", SessionManager.Instance ?? throw new InvalidOperationException("Probe session missing."));
                if (allowPending)
                {
                    var storage = Get(items, "_guestPartStorage") as GameObject;
                    Require(storage != null && saved.transform.parent == storage.transform && !saved.activeInHierarchy
                        && !parts.Contains(id) && ((IDictionary)Get(items, "_isolatedGuestParts")).Count == 1
                        && !(bool)Get(factory, "Failed") && occupant.Value == saved && mount.FsmVariables.FindFsmFloat("Wear").Value == 75,
                        "Dormant containment blocked safe part isolation or altered saved state.");
                    Require((bool)InstanceCall(items, "RestoreIsolatedGuestParts")! && saved.transform.parent == mountObject.transform
                        && saved.activeSelf && occupant.Value == saved, "Isolated saved part did not restore intact.");
                    return;
                }
                Require(saved.transform.parent == mountObject.transform && saved.activeSelf && occupant.Value == saved && parts.Contains(id)
                    && ((IDictionary)Get(items, "_isolatedGuestParts")).Count == 0 && ((IDictionary)Get(items, "_isolatedGuestMounts")).Count == 0
                    && Get(items, "_guestPartStorage") == null && !(bool)Get(factory, "Failed") && mount.FsmVariables.FindFsmFloat("Wear").Value == 75,
                    "Failed engine protection moved, hid or discarded the fitted original.");
            }
            finally
            {
                property.GetSetMethod(true).Invoke(null, new[] { profile });
                InstanceCall(items, "RestoreIsolatedGuestParts");
                Guard.GetField("PrepareInputs", Static).SetValue(null, inputCallback);
                InstanceCall(suppressor, "Restore"); UnityEngine.Object.DestroyImmediate(mountObject); Call("Prepare", true);
            }
        }
        private static IList AddArray(GameObject obj, string name, ArrayList values)
        {
            Type? type = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if ((type = assembly.GetType("PlayMakerArrayListProxy")) != null) break;
            var proxy = obj.AddComponent(type ?? throw new InvalidOperationException("ArrayMaker missing."));
            type!.GetField("referenceName").SetValue(proxy, name); type.GetField("_arrayList").SetValue(proxy, values); return values;
        }
        private static Dictionary<string, object> ReadInput()
        {
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../guest-engine-probe.json")) }, null);
            return (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
        }
        private static FsmStateAction NativeAction(Dictionary<string, object> row, PlayMakerFSM fsm) => (FsmStateAction)Import.Invoke(null, new object[] { row, fsm });
        private static bool Selected(object rule, string state, int index)
        {
            foreach (string list in new[] { "Actions", "PoseActions", "EventActions" })
                foreach (object a in (IEnumerable)Get(rule, list)) if ((string)Get(a, "State") == state && (int)Get(a, "Index") == index) return true;
            return false;
        }
        private static Dictionary<string, object> FindState(Dictionary<string, object> row, string state)
        { foreach (Dictionary<string, object> s in (IEnumerable)row["states"]) if ((string)s["name"] == state) return s; throw new InvalidOperationException("Missing native state."); }
        private static Writer Find(List<Writer> writers, string name)
        { foreach (var writer in writers) if (writer.Fsm.FsmName == name) return writer; throw new InvalidOperationException("Missing writer."); }
        private static GameObject PathObject(GameObject root, string path)
        {
            var current = root; var parts = path.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.transform.Find(parts[i]); current = child != null ? child.gameObject : Child(current, parts[i]);
            }
            return current;
        }
        private static GameObject Child(GameObject parent, string name) { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
        private static PlayMakerFSM Empty(GameObject obj, string name)
        {
            var fsm = obj.AddComponent<PlayMakerFSM>(); fsm.enabled = false; typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = name; fsm.Fsm.StartState = "Probe idle";
            fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0] } }; return fsm;
        }
        private static PlayMakerFSM Data(GameObject obj) { foreach (var fsm in obj.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Data") return fsm; throw new InvalidOperationException("Missing Data."); }
        private static void AddFloat(PlayMakerFSM fsm, string name, float value)
        { var variables = new List<FsmFloat>(fsm.FsmVariables.FloatVariables) { new FsmFloat { Name = name, UseVariable = true, Value = value } }; fsm.FsmVariables.FloatVariables = variables.ToArray(); }
        private static bool EverySelected(List<Writer> writers, Predicate<FsmStateAction> predicate)
        { foreach (var writer in writers) foreach (var action in writer.Selected) if (!predicate(action)) return false; return true; }
        private static float[] Snapshot(List<FsmFloat> values) { var result = new float[values.Count]; for (int i = 0; i < values.Count; i++) result[i] = values[i].Value; return result; }
        private static bool Same(List<FsmFloat> values, float[] expected) { for (int i = 0; i < values.Count; i++) if (values[i].Value != expected[i]) return false; return true; }
        private static void Tick(PlayMakerFSM fsm) { fsm.Fsm.Update(); fsm.Fsm.FixedUpdate(); }
        private static object Get(object obj, string field) => obj.GetType().GetField(field, Members).GetValue(obj);
        private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Members).SetValue(obj, value);
        private static object? InstanceCall(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Members).Invoke(obj, args);
        private static object? Call(string method, params object[] args) => Guard.GetMethod(method, Static).Invoke(null, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private sealed class EntryCounter : FsmStateAction
        {
            internal int Entries, Exits, Events;
            public override void OnEnter() { Entries++; }
            public override void OnExit() { Exits++; }
            public override bool Event(FsmEvent value)
            {
                // The imported BoolTest intentionally emits AIRFILTER during a
                // successful entry; count only unexpected replay/transition events.
                if (value.Name != "AIRFILTER") Events++;
                return false;
            }
        }
        private sealed class Quiet : FsmStateAction { public override void OnEnter() { Finish(); } }
    }
}
