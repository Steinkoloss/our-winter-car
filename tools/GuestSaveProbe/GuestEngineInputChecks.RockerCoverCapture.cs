using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunRockerCoverCapture(Action<string, Action> check)
        {
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN132", "Oil", "rocker-cover-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                f.SavedEngineBlockPart.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" } };
                var head = f.MakeNativeCylinderHead(); head.transform.parent = f.SavedEngineBlockPart.transform; NativeBagPartChecks.Start(head); NativeBagPartChecks.Fire(head, "UPDATE");
                var cover = f.MakeNativeRockerCover(); cover.transform.parent = f.SavedCylinderHeadPart.transform;
                check("rocker cover capture: waits for initialization and settled native installation", () =>
                {
                    Require(!f.CaptureEngineBlock().RockerCoverInstalled, "Unstarted cover was captured."); NativeBagPartChecks.Start(cover); NativeBagPartChecks.Fire(cover, "Installed");
                    Require(!f.CaptureEngineBlock().RockerCoverInstalled, "Intermediate cover was captured."); NativeBagPartChecks.Fire(cover, "Update 2");
                    var state = f.CaptureEngineBlock(); Require(state.Flags == 11 && state.RockerCoverInstalled && state.RockerCoverTightness == 64, "Settled native cover was lost.");
                });
                foreach (string stateName in new[] { "Idle", "Install 1", "Install 2", "Allow removal?", "Allow install?", "Far", "Near" })
                {
                    string name = stateName;
                    check("rocker cover capture: transition " + name + " clears only the cover", () =>
                    { NativeBagPartChecks.Fire(cover, name); var state = f.CaptureEngineBlock(); Require(state.Flags == 11 && !state.RockerCoverInstalled && state.RockerCoverTightness == 0, "Transitional cover supplied bolts."); NativeBagPartChecks.Fire(cover, "Update 2"); Require(f.CaptureEngineBlock().RockerCoverInstalled, "Settled cover did not recover."); });
                }
                check("rocker cover capture: mounted tightness changes revision and snapshots preserve pending broadcast", () =>
                {
                    var first = f.CaptureEngineBlock(); var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); publication.MarkBroadcast(first.Revision);
                    cover.FsmVariables.FindFsmFloat("Tightness").Value = 48; var next = f.CaptureEngineBlock(); Require(next.RockerCoverTightness == 48 && next.Revision == first.Revision + 1
                        && f.SavedRockerCoverPart.FsmVariables.FindFsmFloat("Tightness").Value == 64 && publication.NeedsBroadcast, "Capture used saved part rather than mounted bolt total.");
                    next.RockerCoverTightness = 1; var snapshot = f.CaptureEngineBlock(); Require(snapshot.RockerCoverTightness == 48 && snapshot.Revision == next.Revision && publication.NeedsBroadcast, "Snapshot consumed or changed live cover state.");
                    cover.FsmVariables.FindFsmFloat("Tightness").Value = 64;
                });
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    float value = invalid;
                    check("rocker cover capture: nonfinite tightness " + value + " clears and recovers independently", () =>
                    {
                        cover.FsmVariables.FindFsmFloat("Tightness").Value = value;
                        try { var state = f.CaptureEngineBlock(); Require(state.Flags == 11 && !state.RockerCoverInstalled && state.RockerCoverTightness == 0, "Invalid cover disabled its head or retained bolts."); }
                        finally { cover.FsmVariables.FindFsmFloat("Tightness").Value = 64; } Require(f.CaptureEngineBlock().RockerCoverInstalled, "Repaired cover failed recovery.");
                    });
                }
                foreach (string reason in new[] { "inactive mount", "inactive part", "wrong parent", "assembly", "duplicate Data", "duplicate mount", "part identity", "head identity", "missing part", "missing mount", "disabled mount", "missing tightness" })
                {
                    string fault = reason;
                    check("rocker cover capture: " + fault + " cannot supply tightness", () =>
                    {
                        PlayMakerFSM? duplicate = null; GameObject? extra = null; var floats = cover.FsmVariables.FloatVariables;
                        if (fault == "inactive mount") cover.gameObject.SetActive(false); if (fault == "inactive part") f.SavedRockerCoverPart.gameObject.SetActive(false);
                        if (fault == "wrong parent") f.SavedRockerCoverPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedRockerCoverPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate Data") duplicate = Empty(cover.gameObject, "Data");
                        if (fault == "duplicate mount") extra = Child(f.SavedCylinderHeadPart.gameObject, cover.gameObject.name);
                        if (fault == "part identity") f.SavedRockerCoverPart.FsmVariables.FindFsmString("ID").Value = "VIN11801";
                        if (fault == "head identity") f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN11101";
                        if (fault == "missing part") cover.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") cover.transform.parent = f.Extras.transform;
                        if (fault == "disabled mount") cover.enabled = false;
                        if (fault == "missing tightness") cover.FsmVariables.FloatVariables = new FsmFloat[0];
                        try { var state = f.CaptureEngineBlock(); Require(!state.RockerCoverInstalled && state.RockerCoverTightness == 0, "Invalid native cover supplied bolts."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
                            cover.gameObject.SetActive(true); cover.enabled = true; f.SavedRockerCoverPart.gameObject.SetActive(true); cover.transform.parent = f.SavedCylinderHeadPart.transform;
                            f.SavedRockerCoverPart.transform.parent = cover.transform; f.SavedRockerCoverPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                            f.SavedRockerCoverPart.FsmVariables.FindFsmString("ID").Value = "VIN1189"; f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN1119";
                            cover.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedRockerCoverPart.gameObject; cover.FsmVariables.FloatVariables = floats; NativeBagPartChecks.Fire(cover, "Update 2");
                        }
                        Require(f.CaptureEngineBlock().RockerCoverInstalled, "Repaired cover remained absent.");
                    });
                }
                check("rocker cover capture: original counter and renamed identities follow head and block removal", () =>
                {
                    f.SavedCylinderHeadPart.gameObject.name = "renamed covered head";
                    foreach (string id in new[] { "VIN1180", "VIN118123" }) { f.SavedRockerCoverPart.FsmVariables.FindFsmString("ID").Value = id; Require(f.CaptureEngineBlock().RockerCoverInstalled, "Valid cover identity was lost."); }
                    f.SavedRockerCoverPart.FsmVariables.FindFsmString("ID").Value = "VIN1189";
                    foreach (var mount in new[] { cover, head, block })
                    {
                        mount.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(mount, "Idle"); Require(!f.CaptureEngineBlock().RockerCoverInstalled, "Removed assembly retained cover input.");
                        mount.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(mount, mount == block ? "Update" : mount == head ? "UPDATE" : "Update 2");
                        Require(f.CaptureEngineBlock().RockerCoverInstalled, "Refitted assembly lost its cover.");
                    }
                });
                check("rocker cover protection: native disabled wear stays inert while removal copies wear hides cap and detaches", () =>
                {
                    cover.FsmVariables.FindFsmFloat("Wear").Value = 65; cover.Fsm.Update(); Require(!NativeBagPartChecks.State(cover, "Update 2").Actions[0].Enabled && f.SavedRockerCoverPart.FsmVariables.FindFsmFloat("Wear").Value == 77, "Fixture enabled disabled wear action.");
                    NativeBagPartChecks.Fire(cover, "Remove part"); Require(f.SavedRockerCoverPart.FsmVariables.FindFsmFloat("Wear").Value == 65 && !f.SavedRockerCoverCap.activeSelf && !cover.FsmVariables.FindFsmBool("Installed").Value
                        && f.SavedRockerCoverPart.transform.parent == null && cover.FsmVariables.FindFsmFloat("Tightness").Value == 0, "Native cover removal did not execute.");
                    f.SavedRockerCoverPart.FsmVariables.FindFsmFloat("Wear").Value = 77; f.SavedRockerCoverPart.transform.parent = cover.transform;
                    NativeBagPartChecks.Fire(cover, "Install 2"); Require(cover.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedRockerCoverCap.activeSelf, "Native cover install did not restore wear/cap.");
                    cover.FsmVariables.FindFsmFloat("Tightness").Value = 64; NativeBagPartChecks.Fire(cover, "Update 2"); f.AssertSaved();
                });
                check("rocker cover protection: moving saved head prevents removal through disconnect", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare() && !cover.enabled && !cover.Fsm.RestartOnEnable, "Moving cover was not protected.");
                    cover.FsmVariables.FindFsmFloat("Wear").Value = 12; cover.Fsm.Update(); NativeBagPartChecks.Fire(cover, "Remove part");
                    Require(f.SavedRockerCoverPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedRockerCoverPart.transform.parent == cover.transform && f.SavedRockerCoverCap.activeSelf, "Guest cover changed saved data or cap.");
                    cover.FsmVariables.FindFsmFloat("Wear").Value = 77; f.AssertSaved(); Call(f.Sync, "ReleaseSession"); cover.Fsm.Update(); cover.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(cover, "Remove part");
                    Require(!cover.enabled, "Disconnect resumed saved cover."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
