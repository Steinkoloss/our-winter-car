using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HarmonyLib;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static Action? _inputPathMutation;

        private static void MutateInputPath()
        {
            var mutation = _inputPathMutation; _inputPathMutation = null; mutation?.Invoke();
        }

        private static void ValidateBoundInput(object sync, object binding)
        {
            var method = Items.GetMethod("ValidateGuestEngineInputs", Members);
            // Keep the diagnostic identical when comparing the previous binary.
            var arguments = new object[method.GetParameters().Length]; arguments[0] = binding;
            method.Invoke(sync, arguments);
        }

        private static void CheckReaderPathValidation(Fixture f, Action<string, Action> check)
        {
            foreach (string change in new[] { "object rename", "parent change", "FSM rename" })
            {
                string scenario = change;
                check("engine inputs: same-frame " + scenario + " rejects the previous consumer path and recovers", () =>
                {
                    Require(f.Prepare(), "Reader was not ready before mutation.");
                    string name = f.Reader.gameObject.name, fsmName = f.Reader.FsmName;
                    var parent = f.Reader.transform.parent;
                    if (scenario == "object rename") f.Reader.gameObject.name = "Changed consumer";
                    else if (scenario == "parent change") f.Reader.transform.SetParent(f.Extras.transform, false);
                    else f.Reader.FsmName = "Changed consumer";
                    try
                    {
                        Require(!f.Prepare() && !f.Reader.enabled, "Previous path concealed a changed consumer.");
                        f.AssertSaved();
                    }
                    finally
                    {
                        f.Reader.gameObject.name = name; f.Reader.FsmName = fsmName;
                        f.Reader.transform.SetParent(parent, false);
                        Require(f.Prepare() && f.Reader.enabled, "Restored consumer did not recover.");
                    }
                });
            }
            foreach (string boundary in new[] { "BindGuestEngineInputs", "UpdateGuestEngineInputValues" })
            {
                string method = boundary;
                check("engine inputs: consumer movement after " + method + " cannot reuse the selected path", () =>
                {
                    Require(f.Prepare(), "Reader was not ready before boundary check.");
                    var bindings = (IList)Get(f.Sync, "_guestEngineInputs");
                    int count = 0;
                    foreach (var binding in bindings)
                        if (ReferenceEquals(Get(binding, "Fsm"), f.Reader)) count++;
                    Require(count > 1, "Boundary fixture needs multiple sources for this native reader.");
                    if (method == "BindGuestEngineInputs")
                        foreach (var binding in bindings)
                            if (ReferenceEquals(Get(binding, "Fsm"), f.Reader)) Set(binding, "Rebind", true);
                    var harmony = new Harmony("WinterMP.InputPathBoundaryProbe");
                    var original = Items.GetMethod(method, Members);
                    string savedName = f.Reader.gameObject.name;
                    bool changed = false, rejected = false;
                    _inputPathMutation = () => { changed = true; f.Reader.gameObject.name = "Changed during source preparation"; };
                    try
                    {
                        harmony.Patch(original, postfix: new HarmonyMethod(typeof(GuestEngineInputChecks).GetMethod("MutateInputPath", Static)));
                        try { Call(f.Sync, "PrepareGuestEngineInputFsm", f.Reader); }
                        catch (System.Reflection.TargetInvocationException error)
                        { Require(error.InnerException is InvalidOperationException, "Boundary failed for an unrelated reason."); rejected = true; }
                        Require(changed && rejected, "An intervening native change reused the selected path.");
                        f.AssertSaved();
                    }
                    finally
                    {
                        harmony.Unpatch(original, HarmonyPatchType.All, harmony.Id);
                        _inputPathMutation = null; f.Reader.gameObject.name = savedName;
                        Require(f.Prepare() && f.Reader.enabled, "Boundary repair did not recover the reader.");
                        f.AssertSaved();
                    }
                });
            }
        }

        internal static void MeasureBoundInputs(Action<int, string, Action> measure, Action<string, Action> check)
        {
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members);
            bool saved = (bool)protection.GetValue(policy, null);
            bool breakdown = Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-bound-input-breakdown") >= 0;
            try
            {
                protection.GetSetMethod(true).Invoke(policy, new object[] { true });
                foreach (string spec in new[] { "VIN131:Cylinders", "VIN115:Cylinders", "VIN115:Valves" })
                {
                    if (breakdown && spec != "VIN131:Cylinders") continue;
                    var pair = spec.Split(':');
                    using (var f = new Fixture(pair[0], pair[1]))
                    {
                        Require(f.Prepare(), "Bound input benchmark could not prepare its native fixture.");
                        var prepare = (Action<PlayMakerFSM>)Delegate.CreateDelegate(typeof(Action<PlayMakerFSM>), f.Sync,
                            Items.GetMethod("PrepareGuestEngineInputFsm", Members));
                        var validate = Items.GetMethod("ValidateGuestEngineInputs", Members);
                        var bindings = (IList)Get(f.Sync, "_guestEngineInputs");
                        var arguments = new object[validate.GetParameters().Length];
                        if (breakdown) MeasureInputComponents(f, bindings, measure);
                        measure(1000, "guest-bound-validation-" + spec, () =>
                        {
                            for (int i = 0; i < 1000; i++) foreach (var binding in bindings)
                            { arguments[0] = binding; validate.Invoke(f.Sync, arguments); }
                        });
                        measure(1000, "guest-bound-prepare-" + spec, () =>
                        { for (int i = 0; i < 1000; i++) prepare(f.Reader); });
                        check("performance bound inputs " + spec + ": repeated validation preserves reader targets and saved parts", () =>
                        {
                            Require(bindings.Count > 0 && f.Reader.enabled && !f.ProxyData.enabled,
                                "Benchmark lost its protected native reader or enabled its proxy.");
                            foreach (var reader in f.Readers) Require(f.Target(reader) == f.Proxy, "Benchmark changed a native reader target.");
                            f.AssertSaved();
                        });
                    }
                }
            }
            finally { protection.GetSetMethod(true).Invoke(policy, new object[] { saved }); }
        }

        internal static void MeasureEngineBlockReads(Action<int, string, Action> measure, Action<string, Action> check)
        {
            var block = new EngineBlockReplica();
            var state = new EngineBlockState { Revision = uint.MaxValue, Flags = 63, Wear = 27,
                CoolantHoseFlags = 15, ExhaustFlags = 15, ValvesAvailable = true };
            state.CoolantHoseTightness[0] = 1; state.ValveSettings[0] = 2; state.ExhaustPerformance[0] = 3;
            Require(block.Receive(state), "Snapshot benchmark state was rejected.");
            // Reflection selects the same logical read in both binaries, outside
            // timing. Covariance keeps the loop independent of the new type.
            var property = typeof(EngineBlockReplica).GetProperty("Inputs");
            Func<object> read = property == null ? (Func<object>)(() => block.Get()!)
                : (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), block, property.GetGetMethod());
            measure(10000, "guest-component-engine-block-snapshots", () =>
            { for (int i = 0; i < 10000; i++) Require(block.Get() != null, "Snapshot disappeared."); });
            measure(10000, "guest-component-engine-block-input-reads", () =>
            { for (int i = 0; i < 10000; i++) Require(read() != null, "Inputs disappeared."); });
            object first = read();
            check("engine snapshot: native readers see accepted scalars and array values", () =>
            {
                Require(SnapshotValue(first, "Wear") == 27, "Wrong snapshot wear.");
                Require(SnapshotValue(first, "CoolantHoseTightness", 0) == 1, "Wrong hose input.");
                Require(SnapshotValue(first, "ValveSettings", 0) == 2, "Wrong valve input.");
                Require(SnapshotValue(first, "ExhaustPerformance", 0) == 3, "Wrong exhaust input.");
            });
            check("engine snapshot: native input and defensive-copy mutations stay isolated", () =>
            {
                state.Wear = 28; state.CoolantHoseTightness[0] = 4;
                var copy = block.Get()!; copy.Wear = 29; copy.ValveSettings[0] = 5; copy.ExhaustPerformance[0] = 6;
                Require(SnapshotValue(read(), "Wear") == 27 && SnapshotValue(first, "CoolantHoseTightness", 0) == 1
                    && SnapshotValue(first, "ValveSettings", 0) == 2 && SnapshotValue(read(), "ExhaustPerformance", 0) == 3,
                    "External mutation changed accepted inputs.");
            });
            check("engine snapshot: native readers retain old inputs across revision wrap and clear", () =>
            {
                Require(!block.Receive(state), "Conflicting revision was accepted.");
                state.Revision = 0; Require(block.Receive(state), "Wrapped revision was rejected.");
                Require(SnapshotValue(read(), "Wear") == 28 && SnapshotValue(first, "Wear") == 27, "Revision replacement changed old inputs.");
                block.Clear(); Require(read() == null && SnapshotValue(first, "Wear") == 27, "Clear changed acquired inputs.");
            });
        }

        private static float SnapshotValue(object snapshot, string name, int index = -1)
        {
            if (snapshot is EngineBlockState state)
            {
                var value = typeof(EngineBlockState).GetField(name).GetValue(state);
                return index < 0 ? (float)value : ((float[])value)[index];
            }
            if (index < 0) return (float)snapshot.GetType().GetProperty(name).GetValue(snapshot, null);
            string method = name == "ValveSettings" ? "ValveSettingAt" : name + "At";
            return (float)snapshot.GetType().GetMethod(method).Invoke(snapshot, new object[] { index });
        }

        private static void MeasureInputComponents(Fixture fixture, IList bindings, Action<int, string, Action> measure)
        {
            foreach (string name in new[] { "GuestEngineInputActionsCurrent", "ValidateGuestEngineInputMount", "UpdateGuestEngineInputValues" })
            {
                var method = Items.GetMethod(name, Members | Static);
                var arguments = new object[1];
                measure(1000, "guest-component-" + name, () =>
                {
                    for (int i = 0; i < 1000; i++) foreach (var binding in bindings)
                    { arguments[0] = binding; method.Invoke(method.IsStatic ? null : fixture.Sync, arguments); }
                });
            }
            var paths = Core.GetType("WinterMP.Core.Sync.ScenePath", true);
            var path = (Func<Transform, string>)Delegate.CreateDelegate(typeof(Func<Transform, string>), paths.GetMethod("Of", Static));
            var readers = new List<Transform>();
            foreach (var binding in bindings) readers.Add(((PlayMakerFSM)Get(binding, "Fsm")).transform);
            measure(1000, "guest-component-reader-paths", () =>
            { for (int i = 0; i < 1000; i++) foreach (var reader in readers) path(reader); });
        }
    }
}
