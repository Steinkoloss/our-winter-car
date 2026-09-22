using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunCarburettorCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN125", "FuelLine", "carburettor-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                f.SavedEngineBlockPart.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" } };
                var head = f.MakeNativeCylinderHead(); head.transform.parent = f.SavedEngineBlockPart.transform;
                NativeBagPartChecks.Start(head); NativeBagPartChecks.Fire(head, "UPDATE");
                var data = f.MakeNativeCarburettor(); data.transform.parent = f.SavedCylinderHeadPart.transform;
                var installed = data.FsmVariables.FindFsmBool("Installed"); var chamber = data.FsmVariables.FindFsmFloat("FuelChamber");
                var reserve = data.FsmVariables.FindFsmFloat("CarbReserve"); var mixture = data.FsmVariables.FindFsmFloat("SettingMixture");
                check("carburettor capture: initialization and native attachment must settle", () =>
                {
                    Require(f.CaptureEngineBlock().Flags == 11, "Unstarted carburettor changed block/head inputs."); NativeBagPartChecks.Start(data);
                    NativeBagPartChecks.Fire(data, "Installed"); Require(f.CaptureEngineBlock().Flags == 11, "Intermediate carburettor published.");
                    NativeBagPartChecks.Fire(data, "Update 2"); var state = f.CaptureEngineBlock();
                    Require(state.Flags == 27 && state.FuelChamber == 17 && state.CarbReserve == .17f && state.SettingMixture == 16.5f, "Host mount values not captured.");
                });
                foreach (string stateName in new[] { "Install 1", "Install 2", "Allow removal?", "Allow install?", "Load 2", "Save 2", "Init 2", "Remove other" })
                {
                    string state = stateName;
                    check("carburettor capture: transitional " + state + " exposes no fuel or tuning", () =>
                    { NativeBagPartChecks.Fire(data, state); var value = f.CaptureEngineBlock(); Require(value.Flags == 11 && value.FuelChamber == 0 && value.CarbReserve == 0 && value.SettingMixture == 0, "Transitional carburettor published partial inputs."); });
                }
                foreach (string nativeId in new[] { "VIN1130", "VIN1139", "CARB2BRLa01", "CARB4BRLa012" })
                {
                    string id = nativeId;
                    check("carburettor capture: native fitted variant " + id, () =>
                    {
                        f.SavedCarburettorPart.FsmVariables.FindFsmString("ID").Value = id; NativeBagPartChecks.Fire(data, "Update 2");
                        Require(f.CaptureEngineBlock().Flags == 27, "Native carburettor variant rejected.");
                        f.SavedCarburettorPart.FsmVariables.FindFsmString("ID").Value = "VIN1139";
                    });
                }
                check("carburettor capture: each scalar update survives snapshots and changes the revision", () =>
                {
                    var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication");
                    foreach (var value in new[] { chamber, reserve, mixture })
                    {
                        var first = f.CaptureEngineBlock(); publication.MarkBroadcast(first.Revision); value.Value += .01f;
                        var next = f.CaptureEngineBlock(); Require(next.Revision == first.Revision + 1 && publication.NeedsBroadcast, "Snapshot swallowed scalar update.");
                        Require(f.CaptureEngineBlock().Revision == next.Revision && publication.NeedsBroadcast, "Unchanged capture consumed broadcast.");
                    }
                    chamber.Value = 17; reserve.Value = .17f; mixture.Value = 16.5f;
                });
                foreach (string faultName in new[] { "inactive", "part inactive", "parent", "assembly", "duplicate", "duplicate mount", "part identity", "head identity", "missing part", "missing mount", "FuelChamber", "CarbReserve", "SettingMixture" })
                {
                    string fault = faultName;
                    check("carburettor capture: " + fault + " leaves block and head available", () =>
                    {
                        PlayMakerFSM? duplicate = null; GameObject? extra = null; var scalar = data.FsmVariables.FindFsmFloat(fault);
                        if (fault == "inactive") data.gameObject.SetActive(false);
                        if (fault == "part inactive") f.SavedCarburettorPart.gameObject.SetActive(false);
                        if (fault == "parent") f.SavedCarburettorPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedCarburettorPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate") duplicate = Empty(data.gameObject, "Data");
                        if (fault == "duplicate mount") extra = Child(f.SavedCylinderHeadPart.gameObject, data.gameObject.name);
                        if (fault == "part identity") f.SavedCarburettorPart.FsmVariables.FindFsmString("ID").Value = "CARB2BRLa001";
                        if (fault == "head identity") f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN1120";
                        if (fault == "missing part") data.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") data.transform.parent = f.Extras.transform;
                        if (scalar != null) scalar.Value = float.NaN;
                        try
                        {
                            var value = f.CaptureEngineBlock(); Require(value.Flags == (fault == "head identity" ? 3 : 11)
                                && value.FuelChamber == 0 && value.CarbReserve == 0 && value.SettingMixture == 0, "Invalid carburettor published partial fields or disabled its block.");
                        }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
                            data.gameObject.SetActive(true); f.SavedCarburettorPart.gameObject.SetActive(true); data.transform.parent = f.SavedCylinderHeadPart.transform;
                            f.SavedCarburettorPart.transform.parent = data.transform; f.SavedCarburettorPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                            f.SavedCarburettorPart.FsmVariables.FindFsmString("ID").Value = "VIN1139"; f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN1119";
                            data.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedCarburettorPart.gameObject;
                            chamber.Value = 17; reserve.Value = .17f; mixture.Value = 16.5f; NativeBagPartChecks.Fire(data, "Update 2");
                        }
                        Require(f.CaptureEngineBlock().Flags == 27, "Repaired carburettor remained absent.");
                    });
                }
                check("carburettor capture: head and block removal clear all dependent fuel inputs", () =>
                {
                    foreach (var parent in new[] { head, block })
                    {
                        parent.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(parent, "Idle");
                        var state = f.CaptureEngineBlock(); Require(state.Flags == (parent == head ? 3 : 1) && state.FuelChamber == 0 && state.CarbReserve == 0 && state.SettingMixture == 0, "Detached assembly retained carburettor fuel.");
                        parent.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(parent, parent == head ? "UPDATE" : "Update");
                        Require(f.CaptureEngineBlock().Flags == 27, "Assembly refit failed to recover carburettor.");
                    }
                });
                check("carburettor protection: native wear update and removal are live before admission", () =>
                {
                    data.FsmVariables.FindFsmFloat("Wear").Value = 65; data.Fsm.Update();
                    Require(NativeBagPartChecks.State(data, "Update 2").Actions[0].Enabled && f.SavedCarburettorPart.FsmVariables.FindFsmFloat("Wear").Value == 65, "Continuous native wear did not run.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = 64; NativeBagPartChecks.Fire(data, "Remove part");
                    Require(!installed.Value && chamber.Value == 0 && reserve.Value == 0 && f.SavedCarburettorPart.transform.parent == null
                        && f.SavedCarburettorPart.FsmVariables.FindFsmFloat("Wear").Value == 64, "Native removal failed to clear fuel, copy wear and detach.");
                    installed.Value = true; chamber.Value = 17; reserve.Value = .17f; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                    f.SavedCarburettorPart.transform.parent = data.transform; NativeBagPartChecks.Fire(data, "Update 2"); f.AssertSaved();
                });
                check("carburettor protection: moving saved head pauses wear and removal through disconnect", () =>
                {
                    f.SavedCylinderHeadPart.gameObject.name = "renamed saved head"; protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare() && !data.enabled && !data.Fsm.RestartOnEnable, "Moved carburettor mount was not paused.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = 12; data.Fsm.Update(); NativeBagPartChecks.Fire(data, "Remove part");
                    Require(f.SavedCarburettorPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedCarburettorPart.transform.parent == data.transform
                        && chamber.Value == 17 && reserve.Value == .17f, "Guest saved carburettor changed after admission.");
                    data.FsmVariables.FindFsmFloat("Wear").Value = 77; f.AssertSaved(); Call(f.Sync, "ReleaseSession");
                    data.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(data, "Remove part"); Require(!data.enabled, "Disconnect resumed carburettor."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
