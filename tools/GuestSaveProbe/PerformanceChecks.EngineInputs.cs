using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class PerformanceChecks
    {
        private sealed class UnrelatedScalarRead : FsmStateAction { }

        private static void MeasureProtectionPasses(List<string> rows, Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            var bindings = (IDictionary)guard.GetField("Bindings", Static).GetValue(null);
            var failures = (IDictionary)guard.GetField("Failures", Static).GetValue(null);
            Require(bindings.Count == 0 && failures.Count == 0);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protect = policy.GetType().GetProperty("ProtectWorld", Members);
            bool savedProtection = (bool)protect.GetValue(policy, null);
            var callback = guard.GetField("PrepareInputs", Static); var savedCallback = callback.GetValue(null);
            var deadline = guard.GetField("_nextScanAt", Static); var savedDeadline = deadline.GetValue(null);
            var ready = guard.GetField("_lastReady", Static); var savedReady = ready.GetValue(null);
            var errorDeadline = guard.GetField("_nextErrorAt", Static); var savedErrorDeadline = errorDeadline.GetValue(null);
            var prepare = (Func<bool, bool>)Delegate.CreateDelegate(typeof(Func<bool, bool>), guard.GetMethod("Prepare", Static));
            var bindingType = guard.GetNestedType("Binding", System.Reflection.BindingFlags.NonPublic);
            var root = new GameObject("Engine protection result fixture"); root.SetActive(false);
            try
            {
                Require((bool)guard.GetMethod("Initialize", Static).Invoke(null, null));
                protect.GetSetMethod(true).Invoke(policy, new object[] { true });
                deadline.SetValue(null, float.MaxValue); errorDeadline.SetValue(null, float.MaxValue); ready.SetValue(null, true);
                int callbacks = 0;
                Action<PlayMakerFSM> observe = _ => callbacks++;
                callback.SetValue(null, observe);
                var fsms = new List<PlayMakerFSM>();
                foreach (int count in new[] { 0, 32, 128 })
                {
                    while (fsms.Count < count)
                    {
                        var child = Child(root, "Consumer" + fsms.Count); var fsm = child.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                        typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm()); fsm.Fsm.Name = "Result fixture";
                        var binding = Activator.CreateInstance(bindingType, true);
                        bindingType.GetField("Fsm", Members).SetValue(binding, fsm);
                        bindingType.GetField("Rule", Members).SetValue(binding, new object());
                        bindingType.GetField("States", Members).SetValue(binding, fsm.Fsm.States);
                        bindings.Add(fsm, binding); fsms.Add(fsm);
                    }
                    check("engine protection pass: every current consumer is prepared " + count, () =>
                    { callbacks = 0; Require(prepare(false) && callbacks == count && bindings.Count == count && failures.Count == 0); });
                    Measure(rows, count, "guest-protection-ordinary-1000", () =>
                    { for (int i = 0; i < 1000; i++) if (!prepare(false)) throw new InvalidOperationException("Ordinary protection lost readiness."); });
                }
                check("engine protection pass: nested callback retains independent traversal", () =>
                {
                    bool nested = false; callbacks = 0;
                    callback.SetValue(null, (Action<PlayMakerFSM>)(_ =>
                    { callbacks++; if (!nested) { nested = true; Require(prepare(false)); } }));
                    try { Require(prepare(false) && callbacks == fsms.Count * 2); }
                    finally { callback.SetValue(null, observe); }
                });
                check("engine protection pass: registry changes keep this traversal and refresh the next", () =>
                {
                    var last = fsms[fsms.Count - 1]; var binding = bindings[last]; callbacks = 0;
                    callback.SetValue(null, (Action<PlayMakerFSM>)(_ => { callbacks++; bindings.Remove(last); }));
                    try
                    {
                        Require(prepare(false) && callbacks == fsms.Count);
                        callbacks = 0; Require(prepare(false) && callbacks == fsms.Count - 1);
                    }
                    finally { bindings[last] = binding; callback.SetValue(null, observe); }
                });
                check("engine protection pass: a failed callback pauses its graph and continues other consumers", () =>
                {
                    var failed = fsms[0]; callbacks = 0;
                    callback.SetValue(null, (Action<PlayMakerFSM>)(fsm =>
                    { callbacks++; if (ReferenceEquals(fsm, failed)) throw new InvalidOperationException("Expected result fixture failure."); }));
                    try { Require(!prepare(false) && callbacks == fsms.Count && failures.Contains(failed) && !failed.enabled); }
                    finally { callback.SetValue(null, observe); }
                });
            }
            finally
            {
                callback.SetValue(null, savedCallback); deadline.SetValue(null, savedDeadline);
                ready.SetValue(null, savedReady); errorDeadline.SetValue(null, savedErrorDeadline);
                protect.GetSetMethod(true).Invoke(policy, new object[] { savedProtection });
                failures.Clear(); bindings.Clear(); UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void MeasureUnrelatedEngineInputs(List<string> rows, object items, Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
            var policy = guard.GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members);
            bool saved = (bool)protection.GetValue(policy, null);
            var root = new GameObject("Performance engine guard"); root.SetActive(false);
            var prepare = (Action<PlayMakerFSM>)Delegate.CreateDelegate(typeof(Action<PlayMakerFSM>), items,
                items.GetType().GetMethod("PrepareGuestEngineInputFsm", Members));
            try
            {
                var parent = root;
                for (int depth = 0; depth < 6; depth++) parent = Child(parent, "Nested" + depth);
                var fsm = parent.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                fsm.Fsm.Name = "PerformanceIdle";
                protection.GetSetMethod(true).Invoke(policy, new object[] { true });
                Measure(rows, 10000, "guest-engine-unrelated-callbacks", () =>
                { for (int i = 0; i < 10000; i++) prepare(fsm); });
                var profile = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("GuestEngineInputs", Static).GetValue(null, null);
                var entries = (IList)profile.GetType().GetField("Entries", Members).GetValue(profile);
                fsm.Fsm.Name = (string)entries[0].GetType().GetField("Fsm", Members).GetValue(entries[0]);
                Measure(rows, 10000, "guest-engine-same-name-wrong-path", () =>
                { for (int i = 0; i < 10000; i++) prepare(fsm); });
                check("performance engine inputs: unrelated callbacks do not create proxies", () =>
                    Require(((ICollection)items.GetType().GetField("_guestEngineInputs", Members).GetValue(items)).Count == 0));
                var state = new FsmState(fsm.Fsm) { Name = "Unrelated scalar read" };
                var action = new UnrelatedScalarRead(); state.Actions = new FsmStateAction[] { action };
                fsm.Fsm.States = new[] { state }; fsm.Fsm.StartState = state.Name;
                fsm.Fsm.Init(fsm); action.Init(state);
                var project = (Func<FsmStateAction, bool>)Delegate.CreateDelegate(typeof(Func<FsmStateAction, bool>),
                    Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true).GetMethod("ProjectDrivetrainWearRead", Static));
                foreach (string consumer in new[] { "Data", "Cylinders", "Transmission" })
                {
                    fsm.Fsm.Name = consumer;
                    Measure(rows, 10000, "guest-unrelated-drivetrain-reads-" + consumer, () =>
                    { for (int i = 0; i < 10000; i++) if (project(action)) throw new InvalidOperationException("Unrelated scalar read was intercepted."); });
                    check("performance drivetrain reads: " + consumer + " at an unrelated path retains native lookup", () =>
                        Require(!project(action) && !fsm.Fsm.Started && state.ActiveActions.Count == 0));
                }
            }
            finally
            {
                protection.GetSetMethod(true).Invoke(policy, new object[] { saved });
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
