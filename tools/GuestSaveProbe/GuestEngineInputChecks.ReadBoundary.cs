using System;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static int _readBoundaryRefreshes;
        private static void CountReadBoundaryRefresh() => _readBoundaryRefreshes++;

        private static void CheckInputReadBoundary(Fixture f, Action<string, Action> check)
        {
            void Reset()
            {
                f.Receive(90, 8, 11, true); Require(f.Prepare(), "Read boundary fixture did not recover.");
                f.ReadAll(); f.AssertSaved();
            }
            Reset();
            check("engine read boundary: recurring protection defers ready inputs but a native read refreshes its source", () =>
            {
                var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
                var deadline = guard.GetField("_nextScanAt", Static); var previous = deadline.GetValue(null);
                var harmony = new Harmony("WinterMP.InputReadCountProbe");
                var method = Items.GetMethod("UpdateGuestEngineInputValues", Members);
                try
                {
                    deadline.SetValue(null, float.MaxValue); _readBoundaryRefreshes = 0;
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(GuestEngineInputChecks).GetMethod("CountReadBoundaryRefresh", Static)));
                    Require((bool)Call(f.Sync, "PrepareGuestEngineInputs", false, false)!, "Recurring protection failed.");
                    Require(_readBoundaryRefreshes == 0, "Recurring protection refreshed already-guarded inputs.");
                    f.Readers[1].OnEnter();
                    Require(_readBoundaryRefreshes == 1 && f.Angle == 11, "Native reader did not refresh exactly its own source.");
                    f.AssertSaved();
                }
                finally { harmony.UnpatchSelf(); deadline.SetValue(null, previous); Reset(); }
            });
            check("engine read boundary: native read sees a host change between periodic updates", () =>
            {
                try { f.Receive(90, 8, 19, true); f.Readers[1].OnEnter(); Require(f.Angle == 19, "Native read used the previous host angle."); f.AssertSaved(); }
                finally { Reset(); }
            });
            check("engine read boundary: native read sees removal before presentation or periodic update", () =>
            {
                try { f.Receive(90, 0, 11, false, false); f.Readers[0].OnEnter(); Require(!f.Installed, "Native read kept the removed host part installed."); f.AssertSaved(); }
                finally { Reset(); }
            });
            foreach (string scenario in new[] { "consumer rename", "mount rename", "saved output alias" })
            {
                string change = scenario;
                check("engine read boundary: direct read contains " + change + " without a periodic update", () =>
                {
                    var action = f.Readers[1]; string consumer = f.Reader.gameObject.name, mount = f.Mount.gameObject.name;
                    var output = Get(action, "storeValue");
                    var savedOutput = f.Mount.FsmVariables.FindFsmFloat("SparkAngle")
                        ?? throw new InvalidOperationException("Saved alias fixture is missing its scalar.");
                    float savedValue = savedOutput.Value;
                    if (change == "consumer rename") f.Reader.gameObject.name = "Changed reader";
                    else if (change == "mount rename") f.Mount.gameObject.name = "Changed mount";
                    else Set(action, "storeValue", savedOutput);
                    try { action.OnEnter(); Require(!f.Reader.enabled, "Changed native input was allowed to read."); f.AssertSaved(); }
                    finally { f.Reader.gameObject.name = consumer; f.Mount.gameObject.name = mount; Set(action, "storeValue", output); savedOutput.Value = savedValue; Reset(); }
                });
            }
            foreach (bool stale in new[] { false, true })
            {
                bool retired = stale;
                check("engine read boundary: " + (retired ? "retired callback cannot follow the restored saved target" : "replacement action binds before its first direct read"), () =>
                {
                    var state = NativeBagPartChecks.State(f.Reader, "Spark angle?"); var previous = state.Actions[1];
                    var action = f.Import("Spark angle?", 1); state.Actions[1] = action; action.Init(state); f.Readers[1] = action;
                    try
                    {
                        if (retired) Require(f.Prepare(), "Replacement preparation failed.");
                        f.Reader.FsmVariables.FindFsmFloat("Angle").Value = 73;
                        if (retired)
                        {
                            previous.OnEnter();
                            Require(!f.Reader.enabled && f.Angle == 73, "Retired read changed native scratch or remained enabled.");
                        }
                        else
                        {
                            action.OnEnter();
                            Require(f.Angle == 11 && f.Target(action) == f.Proxy, "New action read the saved mount before rebinding.");
                        }
                        f.AssertSaved();
                    }
                    finally { state.Actions[1] = previous; f.Readers[1] = previous; Reset(); }
                });
            }
            foreach (bool recursive in new[] { false, true })
            {
                bool reenter = recursive;
                check("engine read boundary: " + (reenter ? "nested failed read also blocks its outer read" : "refresh callback cannot conceal a changed consumer"), () =>
                {
                    var harmony = new Harmony("WinterMP.InputReadMutationProbe");
                    var method = Items.GetMethod("UpdateGuestEngineInputValues", Members);
                    string name = f.Reader.gameObject.name;
                    f.Reader.FsmVariables.FindFsmFloat("Angle").Value = 73;
                    _inputPathMutation = () => { if (reenter) f.Readers[1].OnEnter(); else f.Reader.gameObject.name = "Changed during read refresh"; };
                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(GuestEngineInputChecks).GetMethod("MutateInputPath", Static)));
                        f.Readers[1].OnEnter();
                        Require(!f.Reader.enabled && f.Angle == 73, "Refresh callback left the outer native read usable."); f.AssertSaved();
                    }
                    finally { harmony.UnpatchSelf(); _inputPathMutation = null; f.Reader.gameObject.name = name; Reset(); }
                });
            }
            check("engine read boundary: missing metadata for a replacement state pauses its consumer", () =>
            {
                var state = NativeBagPartChecks.State(f.Reader, "Spark angle?");
                string name = state.Name; var previous = state.Actions[1];
                var action = f.Import(name, 1); action.Init(state);
                state.Actions[1] = action; state.Name = "Changed native state";
                try { action.OnEnter(); Require(!f.Reader.enabled, "Unresolved replacement read left its consumer enabled."); f.AssertSaved(); }
                finally { state.Name = name; state.Actions[1] = previous; Reset(); }
            });
        }
    }
}
