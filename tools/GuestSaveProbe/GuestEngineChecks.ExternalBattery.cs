using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineChecks
    {
        private sealed class ExternalBatteryWriter
        {
            internal string Label = "", State = "", Scalar = "";
            internal PlayMakerFSM Fsm = null!;
            internal FsmStateAction Action = null!;
            internal FsmFloat Progress = null!;
            internal FsmOwnerDefault Target => (FsmOwnerDefault)Get(Action, "gameObject");
            internal FsmString TargetName => (FsmString)Get(Action, "fsmName");
            internal void Fire() { NativeBagPartChecks.Fire(Fsm, "Probe idle"); NativeBagPartChecks.Fire(Fsm, State); }
        }

        private static void RunExternalBatteryWrites(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var profileProperty = catalog.GetProperty("GuestEngineProtection", Static);
            var profile = profileProperty.GetValue(null, null);
            var callback = Guard.GetField("PrepareInputs", Static).GetValue(null);
            var saveGuard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
            var policy = saveGuard.GetField("Policy", Static).GetValue(null);
            var protect = policy.GetType().GetProperty("ProtectWorld", Members);
            bool previousProtection = (bool)protect.GetValue(policy, null);
            var session = SessionManager.Instance ?? throw new InvalidOperationException("Native probe session is unavailable.");
            var sessionState = typeof(SessionManager).GetProperty("State", Members);
            object previousState = sessionState.GetValue(session, null);
            var root = new GameObject("CORRIS"); root.SetActive(false);
            var consumers = new GameObject("external battery consumers"); consumers.SetActive(false);
            var battery = Empty(PathObject(root, "CORRIS/Assemblies/VINP_Battery"), "Data");
            var other = Empty(battery.gameObject, "Other");
            var ordinary = Empty(Child(consumers, "ordinary destination"), "Data");
            foreach (var fsm in new[] { battery, other, ordinary })
                foreach (string scalar in new[] { "Charge", "ChargeMax", "BoltPositive", "BoltNegative", "DischargeRate" }) AddFloat(fsm, scalar, 100);
            foreach (object rule in (IEnumerable)Get(profile, "PausedFsms"))
                if ((string)Get(rule, "Path") == "CORRIS/Assemblies/VINP_Battery")
                {
                    var states = new List<FsmState>(battery.Fsm.States);
                    foreach (string name in (string[])Get(rule, "RequiredStates"))
                        states.Add(new FsmState(battery.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                    battery.Fsm.States = states.ToArray();
                }
            var writers = new List<ExternalBatteryWriter>();
            try
            {
                Guard.GetField("PrepareInputs", Static).SetValue(null, null);
                protect.GetSetMethod(true).Invoke(policy, new object[] { false });
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../battery-external-writes-probe.json")) }, null);
                var fixture = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
                var rows = (List<object>)fixture["fsms"];
                foreach (Dictionary<string, object> spec in (IEnumerable)fixture["writers"])
                {
                    string path = (string)spec["path"], name = (string)spec["fsm"], stateName = (string)spec["state"];
                    int index = Convert.ToInt32(spec["index"]);
                    var row = NativeBagPartChecks.Find(rows, path, name);
                    var fsm = NativeBagPartChecks.MakeFsm(Child(consumers, "consumer " + writers.Count), row);
                    foreach (var value in fsm.FsmVariables.GameObjectVariables) value.Value = battery.gameObject;
                    fsm.Fsm.Init(fsm);
                    var state = NativeBagPartChecks.State(fsm, stateName);
                    var action = NativeAction((Dictionary<string, object>)((List<object>)FindState(row, stateName)["actions"])[index], fsm);
                    string amount = action.GetType().Name == "SetFsmFloat" ? "setValue" : action.GetType().Name == "AddFsmFloat" ? "addValue" : "subtractValue";
                    Set(action, amount, new FsmFloat { Value = amount == "setValue" ? 71 : 3 });
                    var progress = new FsmFloat();
                    var sibling = (FsmStateAction)Activator.CreateInstance(action.GetType().Assembly.GetType("HutongGames.PlayMaker.Actions.FloatAdd", true));
                    sibling.Reset(); Set(sibling, "floatVariable", progress); Set(sibling, "add", new FsmFloat { Value = 1 });
                    Set(sibling, "everyFrame", true); Set(sibling, "perSecond", false);
                    state.Actions = new[] { action, sibling }; state.Transitions = new FsmTransition[0];
                    foreach (var a in state.Actions) a.Init(state);
                    writers.Add(new ExternalBatteryWriter { Label = path + "::" + name + "/" + stateName + "#" + index,
                        Fsm = fsm, State = stateName, Scalar = (string)spec["scalar"], Action = action, Progress = progress });
                }
                root.SetActive(true); consumers.SetActive(true);
                NativeBagPartChecks.Start(battery); NativeBagPartChecks.Start(other); NativeBagPartChecks.Start(ordinary);
                foreach (var writer in writers) NativeBagPartChecks.Start(writer.Fsm);
                check("external battery: complete retained native audit has 31 writers", () =>
                { Require(writers.Count == 31 && Time.deltaTime > 0, "Incomplete battery writer fixture or zero native delta time."); });
                foreach (var writer in writers)
                    check("external battery: native solo writes " + writer.Label, () =>
                    {
                        var value = battery.FsmVariables.FindFsmFloat(writer.Scalar); value.Value = 100;
                        writer.Fire(); Tick(writer.Fsm);
                        Require(value.Value != 100 && writer.Progress.Value > 0 && writer.Action.Enabled,
                            "Native writer or calculation sibling did not run before protection.");
                    });
                check("external battery: guest admission protects the destination before the next consumer update", () =>
                {
                    Require((bool)saveGuard.GetMethod("TryBeginGuest", Static).Invoke(null, null), "Guest admission rejected fixture.");
                    Require(!battery.enabled && !battery.Fsm.RestartOnEnable, "Saved battery simulation was not paused.");
                    foreach (var w in writers)
                    {
                        var value = battery.FsmVariables.FindFsmFloat(w.Scalar); value.Value = 100;
                        float progress = w.Progress.Value; Tick(w.Fsm);
                        Require(value.Value == 100 && w.Progress.Value > progress, "Already-active external write escaped protection or sibling stopped.");
                    }
                });
                foreach (var writer in writers)
                    check("external battery: protected entry/update preserves " + writer.Label, () =>
                    {
                        var value = battery.FsmVariables.FindFsmFloat(writer.Scalar); value.Value = 100;
                        float progress = writer.Progress.Value; writer.Fire(); Tick(writer.Fsm);
                        Require(value.Value == 100 && writer.Progress.Value > progress && writer.Action.Enabled && writer.Fsm.enabled,
                            "External charge/bolt write escaped or its consumer was disabled.");
                    });
                RunExternalBatteryLifecycle(check, writers, battery, other, ordinary, consumers, catalog, profile, session, sessionState);
            }
            finally
            {
                profileProperty.GetSetMethod(true).Invoke(null, new[] { profile });
                sessionState.GetSetMethod(true).Invoke(session, new[] { previousState });
                protect.GetSetMethod(true).Invoke(policy, new object[] { previousProtection });
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(consumers); Call("Prepare", true);
                Guard.GetField("PrepareInputs", Static).SetValue(null, callback);
            }
        }

        private static void RunExternalBatteryLifecycle(Action<string, Action> check, List<ExternalBatteryWriter> writers,
            PlayMakerFSM battery, PlayMakerFSM other, PlayMakerFSM ordinary, GameObject consumers,
            Type catalog, object profile, SessionManager session, System.Reflection.PropertyInfo sessionState)
        {
            foreach (string type in new[] { "SetFsmFloat", "AddFsmFloat", "SubtractFsmFloat" })
            {
                var writer = writers.Find(w => w.Action.GetType().Name == type)!;
                var value = battery.FsmVariables.FindFsmFloat(writer.Scalar); var otherValue = other.FsmVariables.FindFsmFloat(writer.Scalar);
                var ordinaryValue = ordinary.FsmVariables.FindFsmFloat(writer.Scalar);
                check("external battery: " + type + " retains cached battery despite a changed FSM name", () =>
                {
                    value.Value = 100; writer.TargetName.Value = "Other"; writer.Fire(); Tick(writer.Fsm);
                    Require(value.Value == 100, "Changing the name bypassed the native cached battery destination.");
                    writer.TargetName.Value = "Data";
                });
                check("external battery: " + type + " permits another destination and follows retargeting back", () =>
                {
                    writer.Target.GameObject.Value = ordinary.gameObject; ordinaryValue.Value = 100; writer.Fire(); Tick(writer.Fsm);
                    Require(ordinaryValue.Value != 100, "Protected consumer could not write an ordinary target.");
                    writer.Target.GameObject.Value = battery.gameObject; value.Value = 100; writer.Fire(); Tick(writer.Fsm);
                    Require(value.Value == 100, "Returning to the protected battery was not guarded.");
                });
                check("external battery: " + type + " permits a sibling FSM and respects its native cache", () =>
                {
                    writer.Target.GameObject.Value = ordinary.gameObject; writer.Fire();
                    writer.TargetName.Value = "Other"; writer.Target.GameObject.Value = battery.gameObject;
                    otherValue.Value = 100; writer.Fire(); Tick(writer.Fsm); Require(otherValue.Value != 100, "Unprotected sibling FSM was blocked.");
                    writer.TargetName.Value = "Data"; otherValue.Value = 100; value.Value = 100; writer.Fire(); Tick(writer.Fsm);
                    Require(otherValue.Value != 100 && value.Value == 100, "Guard did not follow native same-object FSM cache.");
                    writer.Target.GameObject.Value = ordinary.gameObject; writer.Fire(); writer.Target.GameObject.Value = battery.gameObject;
                });
                check("external battery: " + type + " covers empty or missing FSM-name fallback", () =>
                {
                    foreach (string name in new[] { "", "Missing native FSM" })
                    {
                        writer.Target.GameObject.Value = ordinary.gameObject; writer.Fire();
                        writer.Target.GameObject.Value = battery.gameObject; writer.TargetName.Value = name;
                        value.Value = 100; writer.Fire(); Tick(writer.Fsm); Require(value.Value == 100, "Native first-FSM fallback escaped.");
                    }
                    writer.TargetName.Value = "Data";
                });
                check("external battery: " + type + " permits a null destination without stopping siblings", () =>
                {
                    writer.Target.GameObject.Value = null; float progress = writer.Progress.Value; writer.Fire(); Tick(writer.Fsm);
                    Require(writer.Progress.Value > progress && writer.Fsm.enabled, "Null native target stalled the consumer.");
                    writer.Target.GameObject.Value = battery.gameObject;
                });
            }
            var sample = writers.Find(w => w.Action.GetType().Name == "SubtractFsmFloat")!;
            var saved = battery.FsmVariables.FindFsmFloat(sample.Scalar);
            check("external battery: consumer moves and renaming do not alter destination protection", () =>
            {
                sample.Fsm.gameObject.name = "StockRadio99"; sample.Fsm.transform.parent = battery.transform;
                saved.Value = 100; sample.Fire(); Tick(sample.Fsm); Require(saved.Value == 100, "A moved consumer changed saved charge.");
                sample.Fsm.transform.parent = consumers.transform;
            });
            var parent = battery.transform.parent; string originalName = battery.name;
            check("external battery: remembered target survives movement, catalog loss and disconnect", () =>
            {
                battery.transform.parent = consumers.transform; battery.gameObject.name = "retained saved battery";
                catalog.GetProperty("GuestEngineProtection", Static).GetSetMethod(true).Invoke(null, new object?[] { null });
                sessionState.GetSetMethod(true).Invoke(session, new object[] { SessionState.Idle });
                foreach (var writer in writers)
                {
                    var value = battery.FsmVariables.FindFsmFloat(writer.Scalar); value.Value = 100;
                    writer.Fire(); Tick(writer.Fsm); Require(value.Value == 100, "Latched target protection was lost.");
                }
                catalog.GetProperty("GuestEngineProtection", Static).GetSetMethod(true).Invoke(null, new[] { profile });
                battery.transform.parent = parent; battery.gameObject.name = originalName;
            });
            check("external battery: a new target is protected before any discovery or valid state binding", () =>
            {
                battery.gameObject.name = "old battery";
                var replacement = Empty(Child(parent.gameObject, "VINP_Battery"), "Data"); AddFloat(replacement, sample.Scalar, 100);
                replacement.Fsm.Init(replacement);
                try
                {
                    sample.Target.GameObject.Value = replacement.gameObject; sample.Fire(); Tick(sample.Fsm);
                    Require(replacement.FsmVariables.FindFsmFloat(sample.Scalar).Value == 100,
                        "Undiscovered/incomplete battery destination accepted an external write.");
                }
                finally { sample.Target.GameObject.Value = battery.gameObject; UnityEngine.Object.DestroyImmediate(replacement.gameObject); battery.gameObject.name = originalName; }
            });
            check("external battery: owner-default writes on the mount remain blocked", () =>
            {
                sample.Fsm.transform.parent = parent; sample.Fsm.gameObject.name = "separate writer";
                // The native resolver uses the action's owning object, including
                // a writer component added later to the protected battery object.
                var fsm = Empty(battery.gameObject, "External probe writer"); fsm.Fsm.Init(fsm);
                var write = (FsmStateAction)Activator.CreateInstance(sample.Action.GetType()); write.Reset();
                Set(write, "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner });
                Set(write, "fsmName", new FsmString { Value = "Data" }); Set(write, "variableName", new FsmString { Value = sample.Scalar });
                Set(write, "subtractValue", new FsmFloat { Value = 3 });
                var state = new FsmState(fsm.Fsm) { Name = "Write", Actions = new[] { write }, Transitions = new FsmTransition[0] };
                fsm.Fsm.States = new[] { fsm.Fsm.States[0], state }; write.Init(state); NativeBagPartChecks.Start(fsm);
                saved.Value = 100; NativeBagPartChecks.Fire(fsm, "Write"); Require(saved.Value == 100, "Owner-default target escaped.");
                UnityEngine.Object.DestroyImmediate(fsm); sample.Fsm.transform.parent = consumers.transform;
            });
        }
    }
}
