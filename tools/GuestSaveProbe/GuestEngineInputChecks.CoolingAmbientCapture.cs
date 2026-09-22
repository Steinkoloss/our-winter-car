using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunCoolingAmbientCapture(Action<string, Action> check)
        {
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN126", "Cooling", "cooling-ambient-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var source = f.MakeNativeCoolingAmbient(); var temperature = source.FsmVariables.FindFsmFloat("TempCar");
                check("cooling ambient capture: unstarted and unknown-state producers remain unavailable", () =>
                {
                    Require(!f.CaptureEngineBlock().CoolingAmbientAvailable, "Unstarted RoofCheck supplied temperature."); NativeBagPartChecks.Start(source);
                    Require(!f.CaptureEngineBlock().CoolingAmbientAvailable, "Unknown startup state supplied temperature."); NativeBagPartChecks.Fire(source, "Cast ray");
                    var state = f.CaptureEngineBlock(); Require(state.Flags == 0 && state.CoolingAmbientAvailable && state.CoolingAmbientTemperature == -12, "Ambient depended on engine installation.");
                });
                check("cooling ambient capture: native sheltered warming uses roof temperature and heat step", () =>
                {
                    source.FsmVariables.FindFsmBool("RoofCover").Value = true; temperature.Value = -12;
                    NativeBagPartChecks.Fire(source, "Check roof"); Require(source.ActiveStateName == "Under roof" && temperature.Value == -9, "Native sheltered branch failed.");
                    for (int i = 0; i < 10; i++) NativeBagPartChecks.Fire(source, "Under roof");
                    var state = f.CaptureEngineBlock(); Require(state.CoolingAmbientAvailable && state.CoolingAmbientTemperature == 12, "Roof temperature clamp or capture diverged.");
                });
                check("cooling ambient capture: native outdoor cooling approaches ambient and retains native upper clamp", () =>
                {
                    source.FsmVariables.FindFsmBool("RoofCover").Value = false; temperature.Value = 12;
                    NativeBagPartChecks.Fire(source, "Check roof"); Require(source.ActiveStateName == "Under sky" && temperature.Value == 9, "Native outdoor branch failed.");
                    for (int i = 0; i < 12; i++) NativeBagPartChecks.Fire(source, "Under sky"); Require(f.CaptureEngineBlock().CoolingAmbientTemperature == -20, "Outdoor minimum diverged.");
                    temperature.Value = 50; NativeBagPartChecks.Fire(source, "Under sky"); Require(f.CaptureEngineBlock().CoolingAmbientTemperature == 25, "Native outdoor upper clamp diverged.");
                });
                check("cooling ambient capture: temperature changes publish copied revisions without consuming snapshots", () =>
                {
                    var first = f.CaptureEngineBlock(); var pub = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); pub.MarkBroadcast(first.Revision);
                    temperature.Value = -12.5f; var next = f.CaptureEngineBlock(); Require(next.Revision == first.Revision + 1 && next.CoolingAmbientTemperature == -12.5f && pub.NeedsBroadcast, "Ambient update lost revision.");
                    next.CoolingAmbientTemperature = 50; var snapshot = f.CaptureEngineBlock(); Require(snapshot.Revision == next.Revision && snapshot.CoolingAmbientTemperature == -12.5f && pub.NeedsBroadcast, "Snapshot consumed or changed ambient update.");
                });
                var grille = f.MakeNativeAirflow(0); NativeBagPartChecks.Start(grille); NativeBagPartChecks.Fire(grille, "Update 2");
                foreach (float v in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    float value = v;
                    check("cooling ambient capture: nonfinite " + value + " clears only ambient and recovers", () =>
                    {
                        temperature.Value = value; var state = f.CaptureEngineBlock(); Require(!state.CoolingAmbientAvailable && state.CoolingAmbientTemperature == 0 && state.CoolingAirflowFlags == 1 && state.GrilleAirflow == 25, "Invalid ambient leaked or cleared body input.");
                        temperature.Value = -12; Require(f.CaptureEngineBlock().CoolingAmbientAvailable, "Finite ambient failed recovery.");
                    });
                }
                foreach (string name in new[] { "inactive", "disabled", "moved", "duplicate", "missing field", "missing state" })
                {
                    string fault = name;
                    check("cooling ambient capture: " + fault + " producer clears independently and repairs", () =>
                    {
                        var parent = source.transform.parent; var floats = source.FsmVariables.FloatVariables; var states = source.Fsm.States; PlayMakerFSM? duplicate = null;
                        if (fault == "inactive") source.gameObject.SetActive(false); if (fault == "disabled") source.enabled = false;
                        if (fault == "moved") source.transform.parent = f.Extras.transform; if (fault == "duplicate") duplicate = Empty(source.gameObject, "Raycast");
                        if (fault == "missing field") source.FsmVariables.FloatVariables = new FsmFloat[0];
                        if (fault == "missing state") source.Fsm.States = new[] { NativeBagPartChecks.State(source, "Under sky") };
                        try { var state = f.CaptureEngineBlock(); Require(!state.CoolingAmbientAvailable && state.CoolingAmbientTemperature == 0 && state.CoolingAirflowFlags == 1, "Malformed ambient affected wrong source."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); source.gameObject.SetActive(true); source.enabled = true; source.transform.parent = parent;
                            source.FsmVariables.FloatVariables = floats; source.Fsm.States = states; NativeBagPartChecks.Fire(source, "Cast ray");
                        }
                        Require(f.CaptureEngineBlock().CoolingAmbientAvailable, "Restored ambient stayed unavailable.");
                    });
                }
                check("cooling ambient capture: unavailable and freezing temperatures have distinct publication revisions", () =>
                {
                    source.enabled = false; var missing = f.CaptureEngineBlock(); source.enabled = true; temperature.Value = 0; NativeBagPartChecks.Fire(source, "Cast ray");
                    var freezing = f.CaptureEngineBlock(); Require(!missing.CoolingAmbientAvailable && freezing.CoolingAmbientAvailable && freezing.CoolingAmbientTemperature == 0 && freezing.Revision == missing.Revision + 1, "Unavailable and freezing states collapsed.");
                });
                check("cooling ambient protection: guest projection leaves the native shelter producer running", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false); f.SetCoolingAmbient(true, -30);
                    Require(f.Prepare() && source.enabled, "Guest ambient projection paused shelter producer."); temperature.Value = -12; NativeBagPartChecks.Fire(source, "Under roof");
                    Require(temperature.Value == -9, "Guest native shelter progression was replaced."); f.Action("Reset", 1).OnEnter(); Require(f.CoolingValue("TempArea") == -30, "Local shelter producer overrode host cooling.");
                    Call(f.Sync, "ReleaseSession"); Require(source.enabled, "Disconnect paused the local shelter producer.");
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
