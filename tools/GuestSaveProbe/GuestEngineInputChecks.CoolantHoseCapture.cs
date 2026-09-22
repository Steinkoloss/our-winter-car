using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunCoolantHoseCapture(Action<string, Action> check)
        {
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN126", "Cooling", "coolant-hose-input-probe.json"))
            try
            {
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var hoses = new PlayMakerFSM[4]; for (byte i = 0; i < 4; i++) hoses[i] = f.MakeNativeCoolantHose(i);
                check("coolant hose capture: initialization and settled installation are required independently of engine", () =>
                {
                    Require(f.CaptureEngineBlock().CoolantHoseFlags == 0, "Unstarted hoses were captured.");
                    foreach (var hose in hoses) { NativeBagPartChecks.Start(hose); NativeBagPartChecks.Fire(hose, "Installed"); }
                    Require(f.CaptureEngineBlock().CoolantHoseFlags == 0, "Intermediate hoses were captured.");
                    for (int i = 0; i < 4; i++) NativeBagPartChecks.Fire(hoses[i], i < 2 ? "Update 2" : "Update");
                    var state = f.CaptureEngineBlock(); Require((state.Flags & 2) == 0 && state.CoolantHoseFlags == 15, "Hoses depended on missing engine block.");
                    foreach (float value in state.CoolantHoseTightness) Require(value == 16, "Mounted clamp totals were lost.");
                });
                for (int hoseIndex = 0; hoseIndex < 4; hoseIndex++)
                {
                    int index = hoseIndex; var hose = hoses[index]; string ready = index < 2 ? "Update 2" : "Update";
                    check("coolant hose capture: mounted clamp " + index + " changes revision without mutating saved part or consuming snapshot", () =>
                    {
                        var first = f.CaptureEngineBlock(); var pub = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); pub.MarkBroadcast(first.Revision);
                        hose.FsmVariables.FindFsmFloat("Tightness").Value = -2; var next = f.CaptureEngineBlock(); Require(next.CoolantHoseTightness[index] == -2 && next.Revision == first.Revision + 1
                            && f.SavedCoolantHoseParts[index].FsmVariables.FindFsmFloat("Tightness").Value == 16 && pub.NeedsBroadcast, "Capture used saved bolts or lost publication.");
                        next.CoolantHoseTightness[index] = 1; var snapshot = f.CaptureEngineBlock(); Require(snapshot.CoolantHoseTightness[index] == -2 && snapshot.Revision == next.Revision && pub.NeedsBroadcast, "Snapshot consumed or changed hose update.");
                        hose.FsmVariables.FindFsmFloat("Tightness").Value = 16;
                    });
                    foreach (string stateName in new[] { "Idle", "Install 1", "Install 2", "Allow removal?", "Allow install?", "Far", "Near" })
                    {
                        string name = stateName;
                        check("coolant hose capture: hose " + index + " transition " + name + " preserves other hoses", () =>
                        { NativeBagPartChecks.Fire(hose, name); var state = f.CaptureEngineBlock(); Require(state.CoolantHoseFlags == (15 & ~(1 << index)) && state.CoolantHoseTightness[index] == 0, "Transitional hose retained clamps or cleared other hoses."); NativeBagPartChecks.Fire(hose, ready); Require(f.CaptureEngineBlock().CoolantHoseFlags == 15, "Settled hose did not recover."); });
                    }
                    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    {
                        float value = invalid;
                        check("coolant hose capture: hose " + index + " nonfinite clamp " + value + " clears independently", () =>
                        {
                            hose.FsmVariables.FindFsmFloat("Tightness").Value = value;
                            try { var state = f.CaptureEngineBlock(); Require(state.CoolantHoseFlags == (15 & ~(1 << index)) && state.CoolantHoseTightness[index] == 0, "Invalid hose retained clamps or cleared others."); }
                            finally { hose.FsmVariables.FindFsmFloat("Tightness").Value = 16; } Require(f.CaptureEngineBlock().CoolantHoseFlags == 15, "Repaired hose failed recovery.");
                        });
                    }
                    check("coolant hose capture: hose " + index + " rejects mismatched identities and accepts original counter refits", () =>
                    {
                        var id = f.SavedCoolantHoseParts[index].FsmVariables.FindFsmString("ID");
                        foreach (string value in new[] { CoolantHosePrefixes[index] + "01", CoolantHosePrefixes[(index + 1) % 4] + "9" })
                        { id.Value = value; Require(f.CaptureEngineBlock().CoolantHoseFlags == (15 & ~(1 << index)), "Wrong hose identity supplied clamps."); }
                        foreach (string value in new[] { CoolantHosePrefixes[index] + "0", CoolantHosePrefixes[index] + "123" })
                        {
                            id.Value = value; Require(f.CaptureEngineBlock().CoolantHoseFlags == 15, "Canonical hose identity was lost.");
                            hose.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(hose, "Idle"); Require(f.CaptureEngineBlock().CoolantHoseFlags == (15 & ~(1 << index)), "Removed hose retained input.");
                            hose.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(hose, ready); Require(f.CaptureEngineBlock().CoolantHoseFlags == 15, "Refit failed.");
                        }
                        id.Value = CoolantHosePrefixes[index] + "9";
                    });
                    check("coolant hose protection: hose " + index + " preserves disabled wear and executes native removal before admission", () =>
                    {
                        hose.FsmVariables.FindFsmFloat("Wear").Value = 65; hose.Fsm.Update(); Require(!NativeBagPartChecks.State(hose, ready).Actions[0].Enabled && f.SavedCoolantHoseParts[index].FsmVariables.FindFsmFloat("Wear").Value == 77, "Disabled native hose wear ran.");
                        NativeBagPartChecks.Fire(hose, "Remove part"); Require(f.SavedCoolantHoseParts[index].FsmVariables.FindFsmFloat("Wear").Value == 65 && f.SavedCoolantHoseParts[index].transform.parent == null
                            && hose.FsmVariables.FindFsmFloat("Tightness").Value == 0 && !hose.FsmVariables.FindFsmBool("Installed").Value, "Native hose removal did not copy wear and detach.");
                        f.SavedCoolantHoseParts[index].FsmVariables.FindFsmFloat("Wear").Value = 77; f.SavedCoolantHoseParts[index].transform.parent = hose.transform;
                        NativeBagPartChecks.Fire(hose, "Install 2"); hose.FsmVariables.FindFsmFloat("Tightness").Value = 16; NativeBagPartChecks.Fire(hose, ready); f.AssertSaved();
                    });
                }
                var firstHose = hoses[0]; var parent = firstHose.transform.parent;
                foreach (string reason in new[] { "inactive mount", "inactive part", "wrong parent", "assembly", "duplicate Data", "missing part", "missing mount", "disabled mount", "missing tightness" })
                {
                    string fault = reason;
                    check("coolant hose capture: " + fault + " clears only affected hose", () =>
                    {
                        PlayMakerFSM? duplicate = null; var floats = firstHose.FsmVariables.FloatVariables; var part = f.SavedCoolantHoseParts[0];
                        if (fault == "inactive mount") firstHose.gameObject.SetActive(false); if (fault == "inactive part") part.gameObject.SetActive(false);
                        if (fault == "wrong parent") part.transform.parent = f.Extras.transform; if (fault == "assembly") part.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate Data") duplicate = Empty(firstHose.gameObject, "Data"); if (fault == "missing part") firstHose.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") firstHose.transform.parent = f.Extras.transform; if (fault == "disabled mount") firstHose.enabled = false;
                        if (fault == "missing tightness") firstHose.FsmVariables.FloatVariables = new FsmFloat[0];
                        try { var state = f.CaptureEngineBlock(); Require(state.CoolantHoseFlags == 14 && state.CoolantHoseTightness[0] == 0, "Bad hose retained input or cleared other hoses."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); firstHose.gameObject.SetActive(true); firstHose.enabled = true; part.gameObject.SetActive(true);
                            firstHose.transform.parent = parent; part.transform.parent = firstHose.transform; part.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                            firstHose.FsmVariables.FindFsmGameObject("ActivePart").Value = part.gameObject; firstHose.FsmVariables.FloatVariables = floats; NativeBagPartChecks.Fire(firstHose, "Update 2");
                        }
                        Require(f.CaptureEngineBlock().CoolantHoseFlags == 15, "Repaired hose stayed absent.");
                    });
                }
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                f.SavedEngineBlockPart.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" } };
                var head = f.MakeNativeCylinderHead(); head.transform.parent = f.SavedEngineBlockPart.transform; NativeBagPartChecks.Start(head); NativeBagPartChecks.Fire(head, "UPDATE");
                var carb = f.MakeNativeCarburettor(); carb.transform.parent = f.SavedCylinderHeadPart.transform; NativeBagPartChecks.Start(carb); NativeBagPartChecks.Fire(carb, "Update 2");
                check("coolant hose capture: all three carburettors supply live bolt totals independently of saved part", () =>
                {
                    foreach (string id in new[] { "VIN1130", "CARB2BRLa01", "CARB4BRLa02" })
                    {
                        f.SavedCarburettorPart.FsmVariables.FindFsmString("ID").Value = id; carb.FsmVariables.FindFsmFloat("Tightness").Value = 32;
                        var state = f.CaptureEngineBlock(); Require(state.Flags == 27 && state.CarburettorTightness == 32 && state.CoolantHoseFlags == 15
                            && f.SavedCarburettorPart.FsmVariables.FindFsmFloat("Tightness").Value == 0, "Carb clamp used saved data or wrong variant.");
                    }
                    f.SavedCarburettorPart.FsmVariables.FindFsmString("ID").Value = "VIN1139"; carb.FsmVariables.FindFsmFloat("Tightness").Value = 40;
                });
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    float value = invalid;
                    check("coolant hose capture: invalid carburettor clamp " + value + " clears intake atomically without clearing hoses", () =>
                    {
                        carb.FsmVariables.FindFsmFloat("Tightness").Value = value;
                        try { var state = f.CaptureEngineBlock(); Require(state.Flags == 11 && state.CarburettorTightness == 0 && state.FuelChamber == 0 && state.CarburettorPower == 0 && state.CoolantHoseFlags == 15, "Bad carb clamp retained partial intake or cleared hoses."); }
                        finally { carb.FsmVariables.FindFsmFloat("Tightness").Value = 40; } Require(f.CaptureEngineBlock().Flags == 27, "Carb clamp failed recovery.");
                    });
                }
                check("coolant hose capture: removing head clears carburettor clamp while fixed hoses remain", () =>
                {
                    head.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(head, "Idle"); var state = f.CaptureEngineBlock(); Require(state.CarburettorTightness == 0 && state.CoolantHoseFlags == 15, "Head removal retained carb clamp or removed hoses.");
                    head.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(head, "UPDATE"); Require(f.CaptureEngineBlock().CarburettorTightness == 40, "Refitted head lost carb clamp.");
                });
                check("coolant hose protection: admission and disconnect prevent saved hose removal", () =>
                {
                    protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false); Require(f.Prepare(), "Hose admission failed.");
                    foreach (var hose in hoses) { Require(!hose.enabled && !hose.Fsm.RestartOnEnable, "Hose Data was not paused."); hose.Fsm.Update(); NativeBagPartChecks.Fire(hose, "Remove part"); } f.AssertSaved();
                    Call(f.Sync, "ReleaseSession"); foreach (var hose in hoses) { hose.Fsm.Update(); hose.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(hose, "Remove part"); Require(!hose.enabled, "Disconnected hose resumed."); } f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
