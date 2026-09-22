using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunRadiatorCapture(Action<string, Action> check)
        {
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN126", "Cooling", "radiator-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var radiator = f.MakeNativeRadiator(); var parent = radiator.transform.parent;
                check("radiator capture: waits for initialization and settled installation without engine block", () =>
                {
                    Require(!f.CaptureEngineBlock().RadiatorInstalled, "Unstarted radiator was captured."); NativeBagPartChecks.Start(radiator); NativeBagPartChecks.Fire(radiator, "Installed");
                    Require(!f.CaptureEngineBlock().RadiatorInstalled, "Intermediate radiator was captured."); NativeBagPartChecks.Fire(radiator, "Update 2");
                    var state = f.CaptureEngineBlock(); Require((state.Flags & 2) == 0 && state.RadiatorInstalled && state.RadiatorWear == 77 && state.RadiatorCoolant == 6.2f
                        && state.RadiatorPressureCap == 13 && state.RadiatorFlectEfficiency == .7f, "Fixed radiator depended on the missing engine or lost native fields.");
                });
                foreach (string stateName in new[] { "Idle", "Install 1", "Install 2", "Allow removal?", "Allow install?", "Far", "Near", "Remove other" })
                {
                    string name = stateName;
                    check("radiator capture: transition " + name + " clears and recovers", () =>
                    { NativeBagPartChecks.Fire(radiator, name); var state = f.CaptureEngineBlock(); Require(!state.RadiatorInstalled && state.RadiatorCoolant == 0, "Transitional radiator supplied coolant."); NativeBagPartChecks.Fire(radiator, "Update 2"); Require(f.CaptureEngineBlock().RadiatorInstalled, "Settled radiator did not recover."); });
                }
                foreach (string fieldName in RadiatorFields)
                {
                    string field = fieldName;
                    check("radiator capture: mounted " + field + " advances revision while snapshots preserve broadcast", () =>
                    {
                        var scalar = radiator.FsmVariables.FindFsmFloat(field); float old = scalar.Value;
                        var first = f.CaptureEngineBlock(); var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); publication.MarkBroadcast(first.Revision);
                        scalar.Value = -2.5f; var next = f.CaptureEngineBlock(); Require(next.RadiatorInstalled && next.Revision == first.Revision + 1 && publication.NeedsBroadcast
                            && f.SavedRadiatorPart.FsmVariables.FindFsmFloat(field).Value == old, "Capture used saved part rather than live mount.");
                        var snapshot = f.CaptureEngineBlock(); Require(snapshot.Revision == next.Revision && publication.NeedsBroadcast, "Snapshot consumed radiator update."); scalar.Value = old;
                    });
                    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    {
                        float value = invalid;
                        check("radiator capture: nonfinite " + field + " " + value + " clears atomically and recovers", () =>
                        {
                            var scalar = radiator.FsmVariables.FindFsmFloat(field); float old = scalar.Value; scalar.Value = value;
                            try { var state = f.CaptureEngineBlock(); Require(!state.RadiatorInstalled && state.RadiatorWear == 0 && state.RadiatorCoolant == 0 && state.RadiatorPressureCap == 0 && state.RadiatorFlectEfficiency == 0, "Invalid radiator retained partial data."); }
                            finally { scalar.Value = old; } Require(f.CaptureEngineBlock().RadiatorInstalled, "Repaired radiator failed recovery.");
                        });
                    }
                }
                foreach (string reason in new[] { "inactive mount", "inactive part", "wrong parent", "assembly", "duplicate Data", "part identity", "missing part", "missing mount", "disabled mount", "missing field" })
                {
                    string fault = reason;
                    check("radiator capture: " + fault + " cannot supply cooling", () =>
                    {
                        PlayMakerFSM? duplicate = null; var floats = radiator.FsmVariables.FloatVariables;
                        if (fault == "inactive mount") radiator.gameObject.SetActive(false); if (fault == "inactive part") f.SavedRadiatorPart.gameObject.SetActive(false);
                        if (fault == "wrong parent") f.SavedRadiatorPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedRadiatorPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate Data") duplicate = Empty(radiator.gameObject, "Data");
                        if (fault == "part identity") f.SavedRadiatorPart.FsmVariables.FindFsmString("ID").Value = "RADIATORa001";
                        if (fault == "missing part") radiator.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") radiator.transform.parent = f.Extras.transform;
                        if (fault == "disabled mount") radiator.enabled = false;
                        if (fault == "missing field") radiator.FsmVariables.FloatVariables = new FsmFloat[0];
                        try { var state = f.CaptureEngineBlock(); Require(!state.RadiatorInstalled && state.RadiatorCoolant == 0, "Invalid native radiator supplied coolant."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                            radiator.gameObject.SetActive(true); radiator.enabled = true; f.SavedRadiatorPart.gameObject.SetActive(true); radiator.transform.parent = parent;
                            f.SavedRadiatorPart.transform.parent = radiator.transform; f.SavedRadiatorPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                            f.SavedRadiatorPart.FsmVariables.FindFsmString("ID").Value = "VIN2019";
                            radiator.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedRadiatorPart.gameObject; radiator.FsmVariables.FloatVariables = floats; NativeBagPartChecks.Fire(radiator, "Update 2");
                        }
                        Require(f.CaptureEngineBlock().RadiatorInstalled, "Repaired radiator remained absent.");
                    });
                }
                check("radiator capture: original replacement and upgraded identities survive rename and refit", () =>
                {
                    f.SavedRadiatorPart.gameObject.name = "renamed radiator";
                    foreach (string id in new[] { "VIN2010", "VIN201123", "RADIATORa01", "RADIATORb02" })
                    {
                        f.SavedRadiatorPart.FsmVariables.FindFsmString("ID").Value = id; Require(f.CaptureEngineBlock().RadiatorInstalled, "Audited radiator variant was rejected.");
                        radiator.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(radiator, "Idle"); Require(!f.CaptureEngineBlock().RadiatorInstalled, "Removed radiator retained inputs.");
                        radiator.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(radiator, "Update 2"); Require(f.CaptureEngineBlock().RadiatorInstalled, "Refitted radiator lost input.");
                    }
                    f.SavedRadiatorPart.FsmVariables.FindFsmString("ID").Value = "VIN2019";
                });
                check("radiator protection: native live wear and clamped coolant copies execute before admission", () =>
                {
                    radiator.FsmVariables.FindFsmFloat("Wear").Value = 65; radiator.FsmVariables.FindFsmFloat("Coolant").Value = 20; radiator.Fsm.Update();
                    Require(f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Wear").Value == 65 && f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Coolant").Value == 8.5f, "Native radiator continuous writes did not execute.");
                    radiator.FsmVariables.FindFsmFloat("Wear").Value = 77; radiator.FsmVariables.FindFsmFloat("Coolant").Value = 6.2f; radiator.Fsm.Update(); f.AssertSaved();
                });
                check("radiator protection: native removal drains coolant copies wear hides cap and detaches", () =>
                {
                    radiator.FsmVariables.FindFsmFloat("Wear").Value = 65; NativeBagPartChecks.Fire(radiator, "Remove part");
                    Require(f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Wear").Value == 65 && f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Coolant").Value == 0 && !f.SavedRadiatorCap.activeSelf
                        && !radiator.FsmVariables.FindFsmBool("Installed").Value && f.SavedRadiatorPart.transform.parent == null && radiator.FsmVariables.FindFsmFloat("Coolant").Value == 0, "Native radiator removal did not execute.");
                    f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Wear").Value = 77; f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Coolant").Value = 6.2f; f.SavedRadiatorPart.transform.parent = radiator.transform;
                    NativeBagPartChecks.Fire(radiator, "Install 2"); Require(radiator.FsmVariables.FindFsmFloat("Coolant").Value == 6.2f && f.SavedRadiatorCap.activeSelf, "Native radiator install did not restore data/cap.");
                    NativeBagPartChecks.Fire(radiator, "Update 2"); f.AssertSaved();
                });
                check("radiator protection: admission drains active writers and prevents removal through disconnect", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare() && !radiator.enabled && !radiator.Fsm.RestartOnEnable, "Saved radiator was not paused.");
                    radiator.FsmVariables.FindFsmFloat("Wear").Value = 12; radiator.FsmVariables.FindFsmFloat("Coolant").Value = 1; radiator.Fsm.Update(); NativeBagPartChecks.Fire(radiator, "Remove part");
                    Require(f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedRadiatorPart.FsmVariables.FindFsmFloat("Coolant").Value == 6.2f
                        && f.SavedRadiatorPart.transform.parent == radiator.transform && f.SavedRadiatorCap.activeSelf, "Guest radiator changed saved data or cap.");
                    radiator.FsmVariables.FindFsmFloat("Wear").Value = 77; radiator.FsmVariables.FindFsmFloat("Coolant").Value = 6.2f; f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); radiator.Fsm.Update(); radiator.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(radiator, "Remove part"); Require(!radiator.enabled, "Disconnect resumed saved radiator."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
