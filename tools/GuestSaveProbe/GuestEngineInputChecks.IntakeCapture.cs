using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunIntakeCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN125", "FuelLine", "intake-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                f.SavedEngineBlockPart.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" } };
                var head = f.MakeNativeCylinderHead(); head.transform.parent = f.SavedEngineBlockPart.transform; NativeBagPartChecks.Start(head); NativeBagPartChecks.Fire(head, "UPDATE");
                var carb = f.MakeNativeCarburettor(); carb.transform.parent = f.SavedCylinderHeadPart.transform; NativeBagPartChecks.Start(carb); NativeBagPartChecks.Fire(carb, "Update 2");
                var air = f.MakeNativeAirCleaner(); air.transform.parent = f.SavedCylinderHeadPart.transform;
                check("intake capture: air cleaner waits for initialization and settled native installation", () =>
                {
                    Require(f.CaptureEngineBlock().Flags == 27, "Unstarted filter invalidated carburettor or appeared fitted."); NativeBagPartChecks.Start(air);
                    NativeBagPartChecks.Fire(air, "Installed"); Require(f.CaptureEngineBlock().Flags == 27, "Intermediate filter accepted."); NativeBagPartChecks.Fire(air, "Update 2");
                    var state = f.CaptureEngineBlock(); Require(state.Flags == 59 && state.CarburettorPower == 101 && state.CarburettorTorque == 251 && state.CarburettorPowerAdd == -.01f
                        && state.AirCleanerPower == 103 && state.AirCleanerTorque == 253 && state.AirCleanerPowerAdd == -.03f, "Fitted host performance missing.");
                });
                foreach (string stateName in new[] { "Install 1", "Install 2", "Allow removal?", "Allow install?", "Far", "Near" })
                {
                    string name = stateName;
                    check("intake capture: filter transition " + name + " retains independent carburettor", () =>
                    {
                        NativeBagPartChecks.Fire(air, name); var state = f.CaptureEngineBlock(); Require(state.Flags == 27 && state.AirCleanerPower == 0 && state.AirCleanerTorque == 0
                            && state.AirCleanerPowerAdd == 0 && state.CarburettorPower == 101, "Transition leaked filter values or cleared carburettor."); NativeBagPartChecks.Fire(air, "Update 2");
                    });
                }
                foreach (string nativeId in new[] { "VIN1350", "VIN1359" })
                {
                    string id = nativeId;
                    check("intake capture: native air cleaner identity " + id, () =>
                    { f.SavedAirCleanerPart.FsmVariables.FindFsmString("ID").Value = id; Require(f.CaptureEngineBlock().Flags == 59, "Native filter rejected."); f.SavedAirCleanerPart.FsmVariables.FindFsmString("ID").Value = "VIN1359"; });
                }
                foreach (bool filter in new[] { false, true }) foreach (string fieldName in new[] { "DataPower", "DataTorque", "DataPowerAdd" })
                {
                    bool isFilter = filter; string field = fieldName; var mount = isFilter ? air : carb;
                    check("intake capture: " + (isFilter ? "filter " : "carburettor ") + field + " observes live mount and rejects nonfinite values atomically", () =>
                    {
                        var value = mount.FsmVariables.FindFsmFloat(field); float saved = value.Value;
                        var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); var first = f.CaptureEngineBlock(); publication.MarkBroadcast(first.Revision);
                        value.Value = saved + 1; var changed = f.CaptureEngineBlock(); Require(changed.Revision == first.Revision + 1 && publication.NeedsBroadcast, "Performance snapshot swallowed live update.");
                        Require(f.CaptureEngineBlock().Revision == changed.Revision && publication.NeedsBroadcast, "Same observation consumed broadcast.");
                        value.Value = float.NaN;
                        try
                        {
                            var state = f.CaptureEngineBlock(); Require(state.Flags == (isFilter ? 27 : 43), "Invalid performance disabled the wrong group.");
                            Require(isFilter ? state.AirCleanerPower == 0 && state.AirCleanerTorque == 0 && state.AirCleanerPowerAdd == 0 && state.CarburettorPower == 101
                                : state.CarburettorPower == 0 && state.CarburettorTorque == 0 && state.CarburettorPowerAdd == 0 && state.FuelChamber == 0 && state.CarbReserve == 0 && state.SettingMixture == 0 && state.AirCleanerPower == 103,
                                "Invalid intake published partial fields.");
                        }
                        finally { value.Value = saved; }
                        Require(f.CaptureEngineBlock().Flags == 59, "Repaired intake did not recover.");
                    });
                }
                foreach (string faultName in new[] { "inactive", "part inactive", "part parent", "assembly", "duplicate data", "duplicate mount", "part identity", "head identity", "missing active part", "missing mount" })
                {
                    string fault = faultName;
                    check("intake capture: filter " + fault + " is contained and repairs", () =>
                    {
                        PlayMakerFSM? duplicate = null; GameObject? extra = null;
                        if (fault == "inactive") air.gameObject.SetActive(false);
                        if (fault == "part inactive") f.SavedAirCleanerPart.gameObject.SetActive(false);
                        if (fault == "part parent") f.SavedAirCleanerPart.transform.parent = f.Extras.transform;
                        if (fault == "assembly") f.SavedAirCleanerPart.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate data") duplicate = Empty(air.gameObject, "Data");
                        if (fault == "duplicate mount") extra = Child(f.SavedCylinderHeadPart.gameObject, air.gameObject.name);
                        if (fault == "part identity") f.SavedAirCleanerPart.FsmVariables.FindFsmString("ID").Value = "VIN13501";
                        if (fault == "head identity") f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN11101";
                        if (fault == "missing active part") air.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") air.transform.parent = f.Extras.transform;
                        try { var state = f.CaptureEngineBlock(); Require(state.Flags == (fault == "head identity" ? 3 : 27) && state.AirCleanerPower == 0 && state.AirCleanerTorque == 0 && state.AirCleanerPowerAdd == 0, "Invalid filter retained performance or disabled unrelated state."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
                            air.gameObject.SetActive(true); f.SavedAirCleanerPart.gameObject.SetActive(true); air.transform.parent = f.SavedCylinderHeadPart.transform;
                            f.SavedAirCleanerPart.transform.parent = air.transform; f.SavedAirCleanerPart.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                            f.SavedAirCleanerPart.FsmVariables.FindFsmString("ID").Value = "VIN1359"; f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN1119";
                            air.FsmVariables.FindFsmGameObject("ActivePart").Value = f.SavedAirCleanerPart.gameObject; NativeBagPartChecks.Fire(air, "Update 2");
                        }
                        Require(f.CaptureEngineBlock().Flags == 59, "Repaired filter remained absent.");
                    });
                }
                check("intake capture: independent intake removal and whole head removal follow native attachment", () =>
                {
                    foreach (var mount in new[] { carb, air, head, block })
                    {
                        mount.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(mount, "Idle");
                        var state = f.CaptureEngineBlock(); Require(state.Flags == (mount == carb ? 43 : mount == air ? 27 : mount == head ? 3 : 1), "Removal cleared the wrong intake dependencies.");
                        mount.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(mount, mount == block ? "Update" : mount == head ? "UPDATE" : "Update 2");
                        Require(f.CaptureEngineBlock().Flags == 59, "Native refit failed to restore intake.");
                    }
                });
                check("intake protection: disabled filter wear stays inert while removal copies wear and clears performance", () =>
                {
                    air.FsmVariables.FindFsmFloat("Wear").Value = 65; air.Fsm.Update(); Require(!NativeBagPartChecks.State(air, "Update 2").Actions[0].Enabled
                        && f.SavedAirCleanerPart.FsmVariables.FindFsmFloat("Wear").Value == 77, "Fixture enabled disabled filter wear.");
                    NativeBagPartChecks.Fire(air, "Remove part"); Require(!air.FsmVariables.FindFsmBool("Installed").Value && f.SavedAirCleanerPart.transform.parent == null
                        && f.SavedAirCleanerPart.FsmVariables.FindFsmFloat("Wear").Value == 65 && air.FsmVariables.FindFsmFloat("DataPower").Value == 0
                        && air.FsmVariables.FindFsmFloat("DataTorque").Value == 0 && air.FsmVariables.FindFsmFloat("DataPowerAdd").Value == 0, "Native filter removal did not execute.");
                    air.FsmVariables.FindFsmBool("Installed").Value = true; air.FsmVariables.FindFsmFloat("Wear").Value = 77; f.SavedAirCleanerPart.FsmVariables.FindFsmFloat("Wear").Value = 77;
                    air.FsmVariables.FindFsmFloat("DataPower").Value = 103; air.FsmVariables.FindFsmFloat("DataTorque").Value = 253; air.FsmVariables.FindFsmFloat("DataPowerAdd").Value = -.03f;
                    f.SavedAirCleanerPart.transform.parent = air.transform; NativeBagPartChecks.Fire(air, "Update 2"); f.AssertSaved();
                });
                check("intake protection: moving head protects both saved intakes through disconnect", () =>
                {
                    f.SavedCylinderHeadPart.gameObject.name = "renamed intake assembly"; protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare() && !carb.enabled && !air.enabled && !air.Fsm.RestartOnEnable, "Moving filter failed guest protection.");
                    air.FsmVariables.FindFsmFloat("Wear").Value = 12; carb.FsmVariables.FindFsmFloat("Wear").Value = 12;
                    air.Fsm.Update(); carb.Fsm.Update(); NativeBagPartChecks.Fire(air, "Remove part"); NativeBagPartChecks.Fire(carb, "Remove part");
                    Require(f.SavedAirCleanerPart.FsmVariables.FindFsmFloat("Wear").Value == 77 && f.SavedCarburettorPart.FsmVariables.FindFsmFloat("Wear").Value == 77, "Guest intake wrote saved wear.");
                    air.FsmVariables.FindFsmFloat("Wear").Value = 77; carb.FsmVariables.FindFsmFloat("Wear").Value = 77; f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); air.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(air, "Remove part"); Require(!air.enabled, "Disconnect resumed filter removal."); f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
