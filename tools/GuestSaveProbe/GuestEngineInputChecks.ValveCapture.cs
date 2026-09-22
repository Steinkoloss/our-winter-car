using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunValveCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protect = policy.GetType().GetProperty("ProtectWorld", Members); bool previous = (bool)protect.GetValue(policy, null);
            using (var f = new Fixture("VIN125", "FuelLine", "valve-adjustment-probe.json"))
            try
            {
                f.AddConsumer("Valves"); protect.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                f.SavedEngineBlockPart.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" } };
                var head = f.MakeNativeCylinderHead(); head.transform.parent = f.SavedEngineBlockPart.transform; NativeBagPartChecks.Start(head); NativeBagPartChecks.Fire(head, "UPDATE");
                check("valve capture: settled host head supplies its eight actual array values", () =>
                {
                    var state = f.CaptureEngineBlock(); Require(state.ValvesAvailable && state.Flags == 11, "Host valve array unavailable.");
                    for (int i = 0; i < 8; i++) Require(state.ValveSettings[i] == 8f + i, "Host valve slot changed."); f.AssertSaved();
                });
                for (int slot = 0; slot < 8; slot++)
                {
                    int index = slot;
                    check("valve capture: slot " + index + " snapshot leaves live broadcast pending and rejects malformed arrays atomically", () =>
                    {
                        var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); var first = f.CaptureEngineBlock(); publication.MarkBroadcast(first.Revision);
                        try
                        {
                            f.SavedValves![index] = 3.5f; var changed = f.CaptureEngineBlock(); Require(changed.Revision == first.Revision + 1 && changed.ValveSettings[index] == 3.5f && publication.NeedsBroadcast, "Snapshot consumed valve change.");
                            Require(f.CaptureEngineBlock().Revision == changed.Revision && publication.NeedsBroadcast, "Repeat snapshot consumed valve broadcast.");
                            foreach (object invalid in new object[] { float.NaN, float.PositiveInfinity, 3, "3" })
                            {
                                f.SavedValves[index] = invalid; var state = f.CaptureEngineBlock(); Require(!state.ValvesAvailable && state.Flags == 11, "Bad valve array disabled head or remained available.");
                                foreach (float value in state.ValveSettings) Require(value == 0, "Invalid valve array published a partial value set.");
                            }
                        }
                        finally { f.SavedValves![index] = 8f + index; }
                        Require(f.CaptureEngineBlock().ValvesAvailable, "Repaired valve array stayed unavailable."); f.AssertSaved();
                    });
                }
                foreach (string faultName in new[] { "missing array", "duplicate array", "short", "long", "head removed", "block removed", "transition", "bad identity", "detached head" })
                {
                    string fault = faultName;
                    check("valve capture: " + fault + " clears the complete tuning set and repairs", () =>
                    {
                        var proxy = f.SavedCylinderHeadPart.gameObject.GetComponent("PlayMakerArrayListProxy"); Component? duplicate = null;
                        if (fault == "missing array") Set(proxy, "referenceName", "Other");
                        if (fault == "duplicate array") { duplicate = f.SavedCylinderHeadPart.gameObject.AddComponent(proxy.GetType()); Set(duplicate, "referenceName", "Valves"); }
                        if (fault == "short") f.SavedValves!.RemoveAt(7); if (fault == "long") f.SavedValves!.Add(16f);
                        if (fault == "head removed") head.FsmVariables.FindFsmBool("Installed").Value = false;
                        if (fault == "block removed") block.FsmVariables.FindFsmBool("Installed").Value = false;
                        if (fault == "transition") NativeBagPartChecks.Fire(head, "Installed");
                        if (fault == "bad identity") f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN11101";
                        if (fault == "detached head") f.SavedCylinderHeadPart.transform.parent = f.Extras.transform;
                        try { var state = f.CaptureEngineBlock(); Require(!state.ValvesAvailable, "Malformed or detached head retained valve settings."); foreach (float value in state.ValveSettings) Require(value == 0, "Missing head leaked tuning."); }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); Set(proxy, "referenceName", "Valves");
                            f.SavedValves!.Clear(); for (int i = 0; i < 8; i++) f.SavedValves.Add(8f + i);
                            head.FsmVariables.FindFsmBool("Installed").Value = true; block.FsmVariables.FindFsmBool("Installed").Value = true;
                            f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID").Value = "VIN1119"; f.SavedCylinderHeadPart.transform.parent = head.transform; NativeBagPartChecks.Fire(head, "UPDATE");
                        }
                        Require(f.CaptureEngineBlock().ValvesAvailable, "Repaired valve source did not return."); f.AssertSaved();
                    });
                }
                check("valve capture: live head movement and returned packet arrays do not change saved tuning", () =>
                {
                    string name = f.SavedCylinderHeadPart.name; f.SavedCylinderHeadPart.name = "moved valve head";
                    try { var state = f.CaptureEngineBlock(); Require(state.ValvesAvailable, "Renamed head lost valve settings."); state.ValveSettings[0] = 999; Require(f.CaptureEngineBlock().ValveSettings[0] == 8, "Snapshot array mutated native settings."); }
                    finally { f.SavedCylinderHeadPart.name = name; } f.AssertSaved();
                });
            }
            finally { protect.GetSetMethod(true).Invoke(policy, new object[] { previous }); }
        }
    }
}
