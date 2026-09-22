using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineChecks
    {
        private static IEnumerable<Writer> ConditionWriters(List<Writer> writers)
        {
            foreach (var writer in writers)
                if (writer.Fsm.FsmName == "Condition" || writer.Fsm.gameObject.name == "GearboxDamage") yield return writer;
        }

        private static FsmFloat ConditionSaved(Writer writer)
        {
            bool tyre = writer.Fsm.FsmName == "Condition";
            var target = writer.Fsm.FsmVariables.FindFsmGameObject(tyre ? "ThisTire" : "db_Gearbox").Value;
            return Data(target).FsmVariables.FindFsmFloat(tyre ? "TireHealth" : "Wear");
        }

        private static void RunConditionWritesSolo(Action<string, Action> check, List<Writer> writers)
        {
            foreach (var writer in ConditionWriters(writers))
            {
                var fsm = writer.Fsm; var saved = ConditionSaved(writer);
                if (fsm.FsmName == "Condition")
                {
                    check("condition writes: " + fsm.gameObject.name + " native solo puncture zeros the referenced saved tyre", () =>
                    {
                        saved.Value = 80; NativeBagPartChecks.Fire(fsm, "State 1"); fsm.SendEvent("PUNCTURE");
                        Require(fsm.ActiveStateName == "Check rim", "Audited PUNCTURE transition changed.");
                        // The branch selector and sound/physics effects are omitted;
                        // native local transitions still enter the real flat writer.
                        fsm.SendEvent("SOUND");
                        Require(fsm.ActiveStateName == "Flat friction" && saved.Value == 0
                            && fsm.FsmVariables.FindFsmFloat("Health").Value == 0, "Native flat write/read baseline did not run.");
                    });
                    check("condition writes: " + fsm.gameObject.name + " native solo wear stays active before admission", () =>
                    {
                        saved.Value = 80; fsm.FsmVariables.FindFsmFloat("FinalWear").Value = 10;
                        NativeBagPartChecks.Fire(fsm, "State 1"); Tick(fsm);
                        var state = NativeBagPartChecks.State(fsm, "State 1");
                        Require(saved.Value < 80 && saved.Value > 0 && state.ActiveActions.Contains(state.Actions[11])
                            && state.ActiveActions.Contains(state.Actions[12])
                            && fsm.FsmVariables.FindFsmFloat("Health").Value == saved.Value,
                            "Native per-second wear or its every-frame read did not warm.");
                    });
                }
                else check("condition writes: native solo reverse-gear failure reduces saved gearbox wear", () =>
                {
                    saved.Value = 90; NativeBagPartChecks.Fire(fsm, "Reverse");
                    Require(Math.Abs(saved.Value - 89.9475f) < .0001f, "Native gearbox subtraction did not run.");
                });
            }
        }

        private static void RunConditionWritesProtected(Action<string, Action> check, List<Writer> writers, Func<bool> prepare)
        {
            foreach (var writer in ConditionWriters(writers))
            {
                var fsm = writer.Fsm; var saved = ConditionSaved(writer); bool tyre = fsm.FsmName == "Condition";
                string name = fsm.gameObject.name, stateName = tyre ? "State 1" : "Reverse";
                int index = tyre ? 11 : 6;
                var state = NativeBagPartChecks.State(fsm, stateName); var write = state.Actions[index];
                check("condition writes: " + name + " guest admission retires the native saved-part writer", () =>
                {
                    float before = saved.Value;
                    if (!tyre) NativeBagPartChecks.Fire(fsm, stateName);
                    Tick(fsm);
                    Require(saved.Value == before && !write.Enabled && !state.ActiveActions.Contains(write)
                        && fsm.enabled, "Saved-part writer survived admission or entire simulation was paused.");
                    if (tyre) Require(state.ActiveActions.Contains(state.Actions[12])
                        && fsm.FsmVariables.FindFsmFloat("Health").Value == before, "Unrelated native reader was suppressed.");
                });
                if (tyre) check("condition writes: " + name + " guest puncture and repair events preserve original tyre health", () =>
                {
                    float before = saved.Value;
                    fsm.SendEvent("PUNCTURE"); Require(fsm.ActiveStateName == "Check rim", "PUNCTURE did not use the native local edge.");
                    fsm.SendEvent("SOUND"); Tick(fsm);
                    Require(fsm.ActiveStateName == "Flat friction" && saved.Value == before
                        && !NativeBagPartChecks.State(fsm, "Flat friction").Actions[0].Enabled,
                        "Flat entry overwrote a guest's original tyre.");
                    fsm.SendEvent("FIXED"); Tick(fsm);
                    Require(fsm.ActiveStateName == "State 1" && saved.Value == before && !write.Enabled,
                        "FIXED reentry restored guest wear.");
                });
                check("condition writes: " + name + " reenabled action cannot escape the native entry guard", () =>
                {
                    float before = saved.Value; NativeBagPartChecks.Fire(fsm, "Probe idle");
                    foreach (var action in writer.Selected) action.Enabled = true;
                    NativeBagPartChecks.Fire(fsm, stateName); Tick(fsm);
                    Require(saved.Value == before && writer.Selected.TrueForAll(a => !a.Enabled)
                        && !state.ActiveActions.Contains(write), "Reentry restored a saved-part mutation.");
                });
                check("condition writes: " + name + " replaced action with changed destination pauses only its graph", () =>
                {
                    float before = saved.Value;
                    var raw = (Dictionary<string, object>)((List<object>)FindState(writer.Row, stateName)["actions"])[index];
                    var changed = NativeAction(raw, fsm); Set(changed, "variableName", new FsmString { Value = "OtherSavedScalar" });
                    NativeBagPartChecks.Fire(fsm, "Probe idle"); state.Actions[index] = changed; changed.Init(state);
                    try
                    {
                        NativeBagPartChecks.Fire(fsm, stateName);
                        Require(!fsm.enabled && !fsm.Fsm.RestartOnEnable && saved.Value == before,
                            "Changed native destination was admitted or saved part changed.");
                    }
                    finally { state.Actions[index] = write; }
                    Require(prepare() && fsm.enabled && !write.Enabled && saved.Value == before,
                        "Restored signature did not recover without replaying wear.");
                });
            }
        }
    }
}
