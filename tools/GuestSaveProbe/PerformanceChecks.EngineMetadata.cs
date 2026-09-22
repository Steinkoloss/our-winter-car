using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class PerformanceChecks
    {
        private static void MeasureEngineMetadata(List<string> rows, Action<string, Action> check)
        {
            var protection = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            var field = protection.GetMethod("Field", Static);
            var readFloat = (Func<FsmStateAction, string, FsmFloat>)Delegate.CreateDelegate(typeof(Func<FsmStateAction, string, FsmFloat>), field.MakeGenericMethod(typeof(FsmFloat)));
            var readBool = (Func<FsmStateAction, string, FsmBool>)Delegate.CreateDelegate(typeof(Func<FsmStateAction, string, FsmBool>), field.MakeGenericMethod(typeof(FsmBool)));
            var readFlag = (Func<FsmStateAction, string, bool>)Delegate.CreateDelegate(typeof(Func<FsmStateAction, string, bool>), field.MakeGenericMethod(typeof(bool)));
            var type = Type.GetType("HutongGames.PlayMaker.Actions.GetFsmFloat, Assembly-CSharp", true);
            var action = (FsmStateAction)Activator.CreateInstance(type);
            var other = (FsmStateAction)Activator.CreateInstance(type);
            var output = type.GetField("storeValue"); var everyFrame = type.GetField("everyFrame");
            var first = new FsmFloat { Value = 1 }; var second = new FsmFloat { Value = 2 };
            output.SetValue(action, first); output.SetValue(other, second);
            check("engine metadata: same type keeps each instance's field values", () =>
                Require(ReferenceEquals(readFloat(action, "storeValue"), first) && ReferenceEquals(readFloat(other, "storeValue"), second)));
            check("engine metadata: replacing a field wrapper is observed immediately", () =>
            { output.SetValue(action, second); Require(ReferenceEquals(readFloat(action, "storeValue"), second)); });
            check("engine metadata: null field values stay null", () =>
            { output.SetValue(action, null); Require(readFloat(action, "storeValue") == null); output.SetValue(action, first); });
            check("engine metadata: value type fields are read live", () =>
            { everyFrame.SetValue(action, true); Require(readFlag(action, "everyFrame")); everyFrame.SetValue(action, false); Require(!readFlag(action, "everyFrame")); });
            check("engine metadata: a warmed field still rejects the wrong requested type", () =>
                ExpectMetadataFailure(() => readBool(action, "storeValue")));
            check("engine metadata: missing fields remain rejected", () =>
                ExpectMetadataFailure(() => readFloat(action, "missingProbeField")));
            var boolean = (FsmStateAction)Activator.CreateInstance(Type.GetType("HutongGames.PlayMaker.Actions.GetFsmBool, Assembly-CSharp", true));
            check("engine metadata: field names do not cross action types after a failed read", () =>
            {
                ExpectMetadataFailure(() => readFloat(boolean, "storeValue"));
                var value = new FsmBool { Value = true }; boolean.GetType().GetField("storeValue").SetValue(boolean, value);
                Require(ReferenceEquals(readBool(boolean, "storeValue"), value) && ReferenceEquals(readFloat(action, "storeValue"), first));
            });
            var root = new GameObject("engine metadata fixture"); root.SetActive(false);
            try
            {
                var fsm = root.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                var state = new FsmState(fsm.Fsm) { Name = "Read", Actions = new[] { action } };
                fsm.Fsm.Name = "Metadata probe"; fsm.Fsm.States = new[] { state }; fsm.Fsm.StartState = state.Name;
                state.Fsm = fsm.Fsm; action.Init(state); other.Init(state); boolean.Init(state);
                var find = protection.GetMethod("FindAction", Static);
                var args = new object[] { fsm, "Read", 0, "GetFsmFloat" };
                Action lookup = () => find.Invoke(null, args);
                lookup();
                check("engine metadata: warmed native type still checks the requested action name", () =>
                { args[3] = "GetFsmBool"; ExpectMetadataFailure(lookup); args[3] = "GetFsmFloat"; lookup(); });
                check("engine metadata: replacing an action reads the current native slot", () =>
                {
                    state.Actions[0] = other;
                    var selected = find.Invoke(null, args);
                    Require(ReferenceEquals(selected.GetType().GetField("Action", Members).GetValue(selected), other));
                    state.Actions[0] = action;
                });
                check("engine metadata: a different action type cannot reuse the warmed signature", () =>
                { state.Actions[0] = boolean; ExpectMetadataFailure(lookup); args[3] = "GetFsmBool"; lookup(); args[3] = "GetFsmFloat"; state.Actions[0] = action; });
                check("engine metadata: a matching type name from another assembly is rejected", () =>
                {
                    var counterfeit = new HutongGames.PlayMaker.Actions.MetadataCounterfeit(); counterfeit.Init(state);
                    state.Actions[0] = counterfeit; args[3] = "MetadataCounterfeit"; ExpectMetadataFailure(lookup);
                    args[3] = null!; ExpectMetadataFailure(lookup);
                    state.Actions[0] = action; args[3] = "GetFsmFloat"; lookup();
                });
                Measure(rows, 10000, "engine-native-action-metadata", () => { for (int i = 0; i < 10000; i++) lookup(); });
                Measure(rows, 10000, "engine-native-field-metadata", () =>
                { for (int i = 0; i < 10000; i++) { readFloat(action, "storeValue"); readFlag(action, "everyFrame"); } });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ExpectMetadataFailure(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            catch (TargetInvocationException error) { if (error.InnerException is InvalidOperationException) return; throw; }
            throw new InvalidOperationException("Changed native metadata was accepted.");
        }
    }
}

namespace HutongGames.PlayMaker.Actions
{
    internal sealed class MetadataCounterfeit : FsmStateAction { }
}
