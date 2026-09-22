using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunOilpanCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN132", "Oil", "oilpan-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                f.SavedEngineBlockPart.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" } };
                var pan = f.MakeNativeOilpan(); pan.transform.parent = f.SavedEngineBlockPart.transform;
                check("oilpan capture: waits for native initialization and settled installation without a cylinder head", () =>
                {
                    Require(!f.CaptureEngineBlock().OilpanInstalled, "Unstarted oilpan was captured."); NativeBagPartChecks.Start(pan); NativeBagPartChecks.Fire(pan, "Installed");
                    Require(!f.CaptureEngineBlock().OilpanInstalled, "Intermediate oilpan was captured."); NativeBagPartChecks.Fire(pan, "Update 2");
                    var state = f.CaptureEngineBlock(); Require(state.Flags == 3 && state.OilpanInstalled && state.OilpanWear == 77 && state.OilpanTightness == 72
                        && state.Oil == 3.2f && state.OilContamination == .8f && state.OilViscosity == 12, "Settled oilpan lost host data or required a head.");
                });
                foreach (string stateName in new[] { "Idle", "Install 1", "Install 2", "Allow removal?", "Allow install?", "Far", "Near" })
                {
                    string name = stateName;
                    check("oilpan capture: transition " + name + " clears only oilpan inputs", () =>
                    { NativeBagPartChecks.Fire(pan, name); var state = f.CaptureEngineBlock(); Require(state.Flags == 3 && !state.OilpanInstalled && state.Oil == 0, "Intermediate oilpan retained oil."); NativeBagPartChecks.Fire(pan, "Update 2"); Require(f.CaptureEngineBlock().OilpanInstalled, "Settled oilpan failed recovery."); });
                }
                foreach (string name in new[] { "Wear", "Tightness", "Oil", "OilContamination", "OilViscosity" })
                {
                    string field = name;
                    check("oilpan capture: finite " + field + " updates revision and snapshots preserve pending broadcast", () =>
                    {
                        var scalar = pan.FsmVariables.FindFsmFloat(field); float original = scalar.Value; var first = f.CaptureEngineBlock();
                        var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); publication.MarkBroadcast(first.Revision);
                        scalar.Value = -2.5f; var changed = f.CaptureEngineBlock(); Require(changed.Revision == first.Revision + 1 && changed.OilpanInstalled && publication.NeedsBroadcast, "Oilpan change was not published.");
                        Require(f.CaptureEngineBlock().Revision == changed.Revision && publication.NeedsBroadcast, "Snapshot consumed oilpan publication."); scalar.Value = original;
                    });
                    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    {
                        float value = invalid;
                        check("oilpan capture: invalid " + field + " " + value + " clears the whole group and recovers", () =>
                        {
                            var scalar = pan.FsmVariables.FindFsmFloat(field); float original = scalar.Value; scalar.Value = value;
                            try { var state = f.CaptureEngineBlock(); Require(state.Flags == 3 && !state.OilpanInstalled && state.OilpanWear == 0 && state.OilpanTightness == 0 && state.Oil == 0 && state.OilContamination == 0 && state.OilViscosity == 0, "Invalid oilpan retained partial data."); }
                            finally { scalar.Value = original; } Require(f.CaptureEngineBlock().OilpanInstalled, "Repaired oilpan did not recover.");
                        });
                    }
                }
                foreach (string reason in new[] { "inactive mount", "inactive part", "wrong parent", "assembly", "duplicate Data", "duplicate mount", "part identity", "block identity", "missing part", "missing mount" })
                {
                    string fault = reason;
                    check("oilpan capture: " + fault + " cannot supply host oil", () =>
                    {
                        PlayMakerFSM? duplicate = null; GameObject? extra = null;
                        if (fault == "inactive mount") pan.gameObject.SetActive(false);
                        if (fault == "inactive part") f.SavedOilpanPart.gameObject.SetActive(false);
                        if (fault == "wrong parent") f.SavedOilpanPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedOilpanPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate Data") duplicate = Empty(pan.gameObject, "Data");
                        if (fault == "duplicate mount") extra = Child(f.SavedEngineBlockPart.gameObject, pan.gameObject.name);
                        if (fault == "part identity") f.SavedOilpanPart.FsmVariables.FindFsmString("ID").Value = "VIN10601";
                        if (fault == "block identity") f.SavedEngineBlockPart.FsmVariables.FindFsmString("ID").Value = "VIN10101";
                        if (fault == "missing part") pan.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") pan.transform.parent = f.Extras.transform;
                        try { Require(!f.CaptureEngineBlock().OilpanInstalled, "Invalid native oilpan supplied inputs."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
                            pan.gameObject.SetActive(true); f.SavedOilpanPart.gameObject.SetActive(true); pan.transform.parent = f.SavedEngineBlockPart.transform;
                            f.SavedOilpanPart.transform.parent = pan.transform; f.SavedOilpanPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                            f.SavedOilpanPart.FsmVariables.FindFsmString("ID").Value = "VIN1069"; f.SavedEngineBlockPart.FsmVariables.FindFsmString("ID").Value = "VIN1010";
                            pan.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedOilpanPart.gameObject; NativeBagPartChecks.Fire(pan, "Update 2");
                        }
                        Require(f.CaptureEngineBlock().OilpanInstalled, "Repaired oilpan remained absent.");
                    });
                }
                check("oilpan capture: original and counter part identities survive renamed engine movement", () =>
                {
                    f.SavedEngineBlockPart.gameObject.name = "renamed installed block";
                    foreach (string id in new[] { "VIN1060", "VIN106123" }) { f.SavedOilpanPart.FsmVariables.FindFsmString("ID").Value = id; Require(f.CaptureEngineBlock().OilpanInstalled, "Valid oilpan identity was lost."); }
                    f.SavedOilpanPart.FsmVariables.FindFsmString("ID").Value = "VIN1069";
                    block.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(block, "Idle"); Require(!f.CaptureEngineBlock().OilpanInstalled, "Removed block retained oilpan.");
                    block.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(block, "Update"); Require(f.CaptureEngineBlock().OilpanInstalled, "Refitted block lost oilpan.");
                });
                check("oilpan protection: native install update and removal copy and drain real saved part fields", () =>
                {
                    pan.FsmVariables.FindFsmFloat("Oil").Value = 1; pan.FsmVariables.FindFsmFloat("OilContamination").Value = 2; pan.FsmVariables.FindFsmFloat("OilViscosity").Value = 3;
                    NativeBagPartChecks.Fire(pan, "Update 2"); Require(f.SavedOilpanPart.FsmVariables.FindFsmFloat("OilLevel").Value == 1 && f.SavedOilpanPart.FsmVariables.FindFsmFloat("OilDirt").Value == 2 && f.SavedOilpanPart.FsmVariables.FindFsmFloat("OilViscosity").Value == 3, "Native Update failed to copy oil.");
                    f.ResetSavedOilpan(); pan.FsmVariables.FindFsmFloat("Oil").Value = 1; NativeBagPartChecks.Fire(pan, "Install 2"); Require(pan.FsmVariables.FindFsmFloat("Oil").Value == 3.2f, "Native install failed to read OilLevel.");
                    NativeBagPartChecks.Fire(pan, "Remove part"); Require(!pan.FsmVariables.FindFsmBool("Installed").Value && f.SavedOilpanPart.transform.parent == null
                        && f.SavedOilpanPart.FsmVariables.FindFsmFloat("OilLevel").Value == 0 && f.SavedOilpanPart.FsmVariables.FindFsmFloat("OilDirt").Value == 0 && f.SavedOilpanPart.FsmVariables.FindFsmFloat("OilViscosity").Value == 0, "Native removal failed to drain saved pan.");
                    f.ResetSavedOilpan(); NativeBagPartChecks.Fire(pan, "Update 2"); f.AssertSaved();
                });
                check("oilpan protection: moving saved block pauses writes and removal through disconnect", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare() && !pan.enabled && !pan.Fsm.RestartOnEnable, "Moving oilpan was not protected.");
                    pan.FsmVariables.FindFsmFloat("Oil").Value = 1; pan.FsmVariables.FindFsmFloat("Wear").Value = 12;
                    pan.Fsm.Update(); NativeBagPartChecks.Fire(pan, "Update 2"); NativeBagPartChecks.Fire(pan, "Remove part");
                    Require(f.SavedOilpanPart.FsmVariables.FindFsmFloat("OilLevel").Value == 3.2f && f.SavedOilpanPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedOilpanPart.transform.parent == pan.transform, "Guest pan changed saved values.");
                    pan.FsmVariables.FindFsmFloat("Oil").Value = 3.2f; pan.FsmVariables.FindFsmFloat("Wear").Value = 77; f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); pan.Fsm.Update(); pan.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(pan, "Remove part"); Require(!pan.enabled, "Disconnect resumed saved oilpan."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
