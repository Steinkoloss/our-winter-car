using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunCoolingAirflowCapture(Action<string, Action> check)
        {
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN126", "Cooling", "cooling-airflow-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var mounts = new PlayMakerFSM[4]; for (byte i = 0; i < 4; i++) mounts[i] = f.MakeNativeAirflow(i);
                check("cooling airflow capture: native scene cover Init records exact identity before display rename", () =>
                {
                    var part = f.SavedAirflowParts[1]; part.gameObject.name = "BLOCKOFF0";
                    part.FsmVariables.GameObjectVariables = new[] { ObjectVar("Owner", part.gameObject) };
                    var row = f.FindNativeRow("CARPARTS/StartParts/BLOCKOFF0", "Data");
                    var init = NativeBagPartChecks.MakeFsm(Child(f.Extras, "cover identity audit"), row); init.Fsm.Init(init);
                    init.gameObject.name = "BLOCKOFF0";
                    var state = NativeBagPartChecks.State(init, "Init");
                    var raw = (System.Collections.Generic.List<object>)((System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.List<object>)row["states"])[0])["actions"];
                    state.Actions = new[] { NativeAction((System.Collections.Generic.Dictionary<string, object>)raw[0], init), NativeAction((System.Collections.Generic.Dictionary<string, object>)raw[1], init) };
                    foreach (var action in state.Actions) action.Init(state); state.Transitions = new FsmTransition[0];
                    NativeBagPartChecks.Start(init); NativeBagPartChecks.Fire(init, "Init");
                    Require(init.FsmVariables.FindFsmString("ID").Value == "BLOCKOFF0", "Native scene cover ID was guessed incorrectly.");
                    init.gameObject.name = "Grille Cover(VINXX)"; Require(init.FsmVariables.FindFsmString("ID").Value == "BLOCKOFF0", "Display rename changed cover ID.");
                    UnityEngine.Object.DestroyImmediate(init.gameObject);
                });
                check("cooling airflow capture: initialized settled mounts remain independent of engine", () =>
                {
                    Require(f.CaptureEngineBlock().CoolingAirflowFlags == 0, "Unstarted airflow mounts were captured.");
                    foreach (var mount in mounts) { NativeBagPartChecks.Start(mount); NativeBagPartChecks.Fire(mount, "Installed"); }
                    Require(f.CaptureEngineBlock().CoolingAirflowFlags == 0, "Intermediate airflow mounts were captured.");
                    foreach (var mount in mounts) NativeBagPartChecks.Fire(mount, "Update 2");
                    var state = f.CaptureEngineBlock(); Require(state.Flags == 0 && state.CoolingAirflowFlags == 15 && state.GrilleAirflow == 25 && state.HoodAirflow == 900 && state.FiberglassHoodAirflow == 700, "Airflow depended on missing engine or used wrong source.");
                });
                for (int mountIndex = 0; mountIndex < 4; mountIndex++)
                {
                    int index = mountIndex; var mount = mounts[index]; var part = f.SavedAirflowParts[index];
                    foreach (string stateName in new[] { "Idle", "Install 1", "Install 2", "Installed", "Allow removal?", "Allow install?", "Far", "Near" })
                    {
                        string name = stateName;
                        check("cooling airflow capture: mount " + index + " transition " + name + " clears independently", () =>
                        {
                            NativeBagPartChecks.Fire(mount, name); Require(f.CaptureEngineBlock().CoolingAirflowFlags == (15 & ~(1 << index)), "Transitional airflow retained input or cleared other mounts.");
                            NativeBagPartChecks.Fire(mount, "Update 2"); Require(f.CaptureEngineBlock().CoolingAirflowFlags == 15, "Settled airflow did not recover.");
                        });
                    }
                    if (index != 1)
                    {
                        check("cooling airflow capture: live mount modifier " + index + " changes revision without consuming snapshot", () =>
                        {
                            var first = f.CaptureEngineBlock(); var pub = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); pub.MarkBroadcast(first.Revision);
                            mount.FsmVariables.FindFsmFloat("CoolingAirRateModifier").Value = -40; var next = f.CaptureEngineBlock(); float value = index == 0 ? next.GrilleAirflow : index == 2 ? next.HoodAirflow : next.FiberglassHoodAirflow;
                            Require(value == -40 && next.Revision == first.Revision + 1 && pub.NeedsBroadcast, "Capture used saved modifier or lost revision.");
                            next.GrilleAirflow = 99; next.HoodAirflow = 99; next.FiberglassHoodAirflow = 99; var snapshot = f.CaptureEngineBlock();
                            Require(snapshot.Revision == next.Revision && (index == 0 ? snapshot.GrilleAirflow : index == 2 ? snapshot.HoodAirflow : snapshot.FiberglassHoodAirflow) == -40 && pub.NeedsBroadcast, "Snapshot consumed update or retained caller mutation.");
                            mount.FsmVariables.FindFsmFloat("CoolingAirRateModifier").Value = SavedAirflow[index]; f.AssertSaved();
                        });
                        foreach (float v in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                        {
                            float value = v;
                            check("cooling airflow capture: mount " + index + " nonfinite modifier " + value + " clears independently", () =>
                            {
                                mount.FsmVariables.FindFsmFloat("CoolingAirRateModifier").Value = value;
                                try { var state = f.CaptureEngineBlock(); Require(state.CoolingAirflowFlags == (15 & ~(1 << index)) && (index == 0 ? state.GrilleAirflow : index == 2 ? state.HoodAirflow : state.FiberglassHoodAirflow) == 0, "Invalid airflow leaked or removed other mounts."); }
                                finally { mount.FsmVariables.FindFsmFloat("CoolingAirRateModifier").Value = SavedAirflow[index]; } Require(f.CaptureEngineBlock().CoolingAirflowFlags == 15, "Finite airflow failed recovery.");
                            });
                        }
                    }
                    foreach (string reason in new[] { "inactive mount", "inactive part", "wrong parent", "assembly", "duplicate Data", "duplicate part Data", "missing part", "missing mount", "disabled mount", "missing required field" })
                    {
                        string fault = reason;
                        check("cooling airflow capture: mount " + index + " rejects " + fault + " and recovers", () =>
                        {
                            PlayMakerFSM? duplicate = null; var floats = mount.FsmVariables.FloatVariables; var strings = part.FsmVariables.StringVariables; var parent = mount.transform.parent;
                            if (fault == "inactive mount") mount.gameObject.SetActive(false); if (fault == "inactive part") part.gameObject.SetActive(false);
                            if (fault == "wrong parent") part.transform.parent = f.Extras.transform; if (fault == "assembly") part.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                            if (fault == "duplicate Data") duplicate = Empty(mount.gameObject, "Data"); if (fault == "duplicate part Data") duplicate = Empty(part.gameObject, "Data");
                            if (fault == "missing part") mount.FsmVariables.FindFsmGameObject("ActivePart").Value = null; if (fault == "missing mount") mount.transform.parent = f.Extras.transform;
                            if (fault == "disabled mount") mount.enabled = false;
                            if (fault == "missing required field") { if (index == 1) part.FsmVariables.StringVariables = new FsmString[0]; else mount.FsmVariables.FloatVariables = new FsmFloat[0]; }
                            try { Require(f.CaptureEngineBlock().CoolingAirflowFlags == (15 & ~(1 << index)), "Invalid airflow source affected wrong group."); }
                            finally
                            {
                                if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); mount.gameObject.SetActive(true); mount.enabled = true; part.gameObject.SetActive(true);
                                mount.transform.parent = parent; part.transform.parent = mount.transform; part.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                                mount.FsmVariables.FindFsmGameObject("ActivePart").Value = part.gameObject; mount.FsmVariables.FloatVariables = floats; part.FsmVariables.StringVariables = strings; NativeBagPartChecks.Fire(mount, "Update 2");
                            }
                            Require(f.CaptureEngineBlock().CoolingAirflowFlags == 15, "Repaired airflow remained absent.");
                        });
                    }
                    check("cooling airflow capture: mount " + index + " accepts audited identities and rejects malformed or foreign parts", () =>
                    {
                        var id = part.FsmVariables.FindFsmString("ID");
                        string[][] accepted = { new[] { "VIN4130", "VIN413123", "VIN413B0", "VIN413B2", "VIN413C0", "VIN413C3", "VIN413D0", "VIN413D4" }, new[] { "BLOCKOFF0" }, new[] { "VIN4110", "VIN411123" }, new[] { "HOODa01", "HOODa0123" } };
                        string[][] rejected = { new[] { "VIN41301", "VIN413B01", "VIN4110" }, new[] { "BLOCKOFF00", "BLOCKOFF1", "Grille Cover(VINXX)" }, new[] { "VIN41101", "VIN4130" }, new[] { "HOODa0", "HOODa00", "HOODa001", "VIN4110" } };
                        foreach (string value in rejected[index]) { id.Value = value; Require(f.CaptureEngineBlock().CoolingAirflowFlags == (15 & ~(1 << index)), "Wrong native airflow identity accepted."); }
                        foreach (string value in accepted[index]) { id.Value = value; part.gameObject.name = "renamed airflow part"; Require(f.CaptureEngineBlock().CoolingAirflowFlags == 15, "Audited airflow identity rejected after rename."); }
                        id.Value = AirflowIds[index];
                    });
                    check("cooling airflow protection: native mount " + index + " removal and refit execute before admission", () =>
                    {
                        NativeBagPartChecks.Fire(mount, "Remove part"); Require(!mount.FsmVariables.FindFsmBool("Installed").Value && part.transform.parent == null && f.CaptureEngineBlock().CoolingAirflowFlags == (15 & ~(1 << index)), "Native airflow removal did not detach and clear input.");
                        NativeBagPartChecks.Fire(mount, "Install 2"); NativeBagPartChecks.Fire(mount, "Installed"); NativeBagPartChecks.Fire(mount, "Update 2");
                        Require(f.CaptureEngineBlock().CoolingAirflowFlags == 15, "Native airflow refit lost source."); f.AssertSaved();
                    });
                }
                check("cooling airflow protection: admission and disconnect block native removal on all four mounts", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false); Require(f.Prepare(), "Airflow admission failed.");
                    foreach (var mount in mounts) { Require(!mount.enabled && !mount.Fsm.RestartOnEnable, "Airflow mount not paused."); mount.Fsm.Update(); NativeBagPartChecks.Fire(mount, "Remove part"); } f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); foreach (var mount in mounts) { mount.Fsm.Update(); mount.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(mount, "Remove part"); Require(!mount.enabled, "Disconnected airflow mount resumed."); } f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
