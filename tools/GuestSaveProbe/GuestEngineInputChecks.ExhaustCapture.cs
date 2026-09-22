using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunExhaustCapture(Action<string, Action> check)
        {
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true); var policy = guard.GetField("Policy", Static).GetValue(null);
            var protectedProperty = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtected = (bool)protectedProperty.GetValue(policy, null);
            using (var f = new Fixture("VIN125", "FuelLine", "exhaust-input-probe.json"))
            try
            {
                var valves = f.AddConsumer("Valves");
                protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { false }); Property(f.Session, "IsHost", true);
                var block = f.MakeNativeBlock(); NativeBagPartChecks.Start(block); NativeBagPartChecks.Fire(block, "Update");
                f.SavedEngineBlockPart.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1010" } };
                var head = f.MakeNativeCylinderHead(); head.transform.parent = f.SavedEngineBlockPart.transform; NativeBagPartChecks.Start(head); NativeBagPartChecks.Fire(head, "UPDATE");
                var mounts = new PlayMakerFSM[4];
                for (byte group = 0; group < 4; group++) mounts[group] = f.MakeNativeExhaust(group, valves);
                mounts[0].transform.parent = f.SavedCylinderHeadPart.transform;
                for (byte g = 0; g < 4; g++)
                {
                    byte group = g; var mount = mounts[group];
                    check("exhaust capture: " + ExhaustStates[group] + " waits for started settled installation", () =>
                    {
                        byte prior = (byte)((1 << group) - 1);
                        Require(f.CaptureEngineBlock().ExhaustFlags == prior, "Unstarted exhaust appeared installed."); NativeBagPartChecks.Start(mount);
                        NativeBagPartChecks.Fire(mount, "Installed"); Require(f.CaptureEngineBlock().ExhaustFlags == prior, "Intermediate exhaust accepted.");
                        NativeBagPartChecks.Fire(mount, "Update 2"); var state = f.CaptureEngineBlock(); Require(state.Flags == 11 && state.ExhaustFlags == (prior | (1 << group))
                            && state.ExhaustPerformance[group * 3] == 70 + group && state.ExhaustPerformance[group * 3 + 1] == 170 + group
                            && state.ExhaustPerformance[group * 3 + 2] == .5f + group, "Settled exhaust performance missing.");
                    });
                }
                string[][] identities = { new[] { "VIN1140", "VIN114B0", "HEADERSa01", "HEADERSc01", "HEADERSd01" }, new[] { "VIN2130", "VIN213B0" }, new[] { "VIN2140", "EXHAUSTRa01" }, new[] { "VIN2190", "MUFFLERa01" } };
                for (byte g = 0; g < 4; g++) foreach (string nativeId in identities[g])
                {
                    byte group = g; string id = nativeId;
                    check("exhaust capture: native stock or upgraded identity " + id, () =>
                    {
                        var part = f.SavedExhaustParts[group]; var field = part.FsmVariables.FindFsmString("ID"); string original = field.Value; var floats = part.FsmVariables.FloatVariables;
                        try
                        {
                            field.Value = id;
                            if (!id.StartsWith("VIN", StringComparison.Ordinal)) part.FsmVariables.FloatVariables = Array.FindAll(floats, value => value.Name != "Wear");
                            Require(f.CaptureEngineBlock().ExhaustFlags == 15, "Supported exhaust identity rejected.");
                        }
                        finally { field.Value = original; part.FsmVariables.FloatVariables = floats; }
                    });
                }
                for (byte g = 0; g < 4; g++) foreach (string fieldName in new[] { "DataPower", "DataTorque", "DataPowerAdd" })
                {
                    byte group = g; string field = fieldName; var mount = mounts[group];
                    check("exhaust capture: " + ExhaustStates[group] + " " + field + " snapshots preserve publication and reject nonfinite triplets", () =>
                    {
                        var value = mount.FsmVariables.FindFsmFloat(field); float original = value.Value;
                        var publication = (EngineBlockPublication)Get(f.Sync, "_engineBlockPublication"); var first = f.CaptureEngineBlock(); publication.MarkBroadcast(first.Revision);
                        try
                        {
                            value.Value = original + 1; var changed = f.CaptureEngineBlock(); Require(changed.Revision == first.Revision + 1 && publication.NeedsBroadcast, "Snapshot consumed live exhaust change.");
                            Require(f.CaptureEngineBlock().Revision == changed.Revision && publication.NeedsBroadcast, "Repeat observation consumed publication.");
                            value.Value = float.NaN; var invalid = f.CaptureEngineBlock(); Require(invalid.ExhaustFlags == (15 & ~(1 << group)) && invalid.Flags == 11, "Invalid exhaust disabled unrelated inputs.");
                            for (int i = 0; i < 3; i++) Require(invalid.ExhaustPerformance[group * 3 + i] == 0, "Invalid exhaust published a partial triplet.");
                        }
                        finally { value.Value = original; }
                        Require(f.CaptureEngineBlock().ExhaustFlags == 15, "Repaired exhaust remained absent.");
                    });
                }
                for (byte g = 0; g < 4; g++) foreach (string faultName in new[] { "inactive", "part inactive", "part parent", "assembly", "duplicate data", "part identity", "missing active part", "missing mount", "transition" })
                {
                    byte group = g; string fault = faultName; var mount = mounts[group]; var part = f.SavedExhaustParts[group];
                    check("exhaust capture: " + ExhaustStates[group] + " " + fault + " clears only its group and repairs", () =>
                    {
                        var parent = mount.transform.parent; string id = part.FsmVariables.FindFsmString("ID").Value; PlayMakerFSM? duplicate = null;
                        if (fault == "inactive") mount.gameObject.SetActive(false);
                        if (fault == "part inactive") part.gameObject.SetActive(false);
                        if (fault == "part parent") part.transform.parent = f.Extras.transform;
                        if (fault == "assembly") part.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                        if (fault == "duplicate data") duplicate = Empty(mount.gameObject, "Data");
                        if (fault == "part identity") part.FsmVariables.FindFsmString("ID").Value = id.Substring(0, id.Length - 1) + "01";
                        if (fault == "missing active part") mount.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                        if (fault == "missing mount") mount.transform.parent = f.Extras.transform;
                        if (fault == "transition") NativeBagPartChecks.Fire(mount, "Allow removal?");
                        try
                        {
                            var state = f.CaptureEngineBlock(); Require(state.ExhaustFlags == (15 & ~(1 << group)) && state.Flags == 11, "Invalid source cleared the wrong exhaust sections.");
                            for (int i = 0; i < 3; i++) Require(state.ExhaustPerformance[group * 3 + i] == 0, "Invalid source retained performance.");
                        }
                        finally
                        {
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                            mount.gameObject.SetActive(true); part.gameObject.SetActive(true); mount.transform.parent = parent; part.transform.parent = mount.transform;
                            part.FsmVariables.FindFsmInt("AssemblyID").Value = 1; part.FsmVariables.FindFsmString("ID").Value = id;
                            mount.FsmVariables.FindFsmGameObject("ActivePart").Value = part.gameObject; NativeBagPartChecks.Fire(mount, "Update 2");
                        }
                        Require(f.CaptureEngineBlock().ExhaustFlags == 15, "Repaired exhaust source remained absent.");
                    });
                }
                check("exhaust capture: ambiguous moving headers and malformed head preserve fixed pipes", () =>
                {
                    var extra = Child(f.SavedCylinderHeadPart.gameObject, mounts[0].gameObject.name);
                    try { Require(f.CaptureEngineBlock().ExhaustFlags == 14, "Ambiguous headers were selected."); }
                    finally { UnityEngine.Object.DestroyImmediate(extra); }
                    var id = f.SavedCylinderHeadPart.FsmVariables.FindFsmString("ID"); string original = id.Value;
                    try { id.Value = "VIN11101"; var state = f.CaptureEngineBlock(); Require(state.Flags == 3 && state.ExhaustFlags == 14, "Malformed head retained headers or removed fixed pipes."); }
                    finally { id.Value = original; }
                    Require(f.CaptureEngineBlock().ExhaustFlags == 15, "Repaired head did not restore headers.");
                });
                check("exhaust capture: section head and block removal have independent dependencies", () =>
                {
                    foreach (var mount in new[] { mounts[0], mounts[1], mounts[2], mounts[3], head, block })
                    {
                        mount.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(mount, "Idle"); var state = f.CaptureEngineBlock();
                        int index = Array.IndexOf(mounts, mount); Require(state.ExhaustFlags == (index < 0 ? 14 : 15 & ~(1 << index)), "Removal discarded unrelated exhaust.");
                        mount.FsmVariables.FindFsmBool("Installed").Value = true; NativeBagPartChecks.Fire(mount, mount == block ? "Update" : mount == head ? "UPDATE" : "Update 2");
                        Require(f.CaptureEngineBlock().ExhaustFlags == 15, "Native exhaust refit failed.");
                    }
                });
                for (byte g = 0; g < 4; g++)
                {
                    byte group = g; var mount = mounts[group]; var part = f.SavedExhaustParts[group];
                    check("exhaust protection: " + ExhaustStates[group] + " preserves disabled wear and exercises native removal", () =>
                    {
                        mount.FsmVariables.FindFsmFloat("Wear").Value = 65; mount.Fsm.Update(); Require(!NativeBagPartChecks.State(mount, "Update 2").Actions[0].Enabled
                            && part.FsmVariables.FindFsmFloat("Wear").Value == 77, "Fixture enabled disabled exhaust wear.");
                        NativeBagPartChecks.Fire(mount, "Remove part"); Require(!mount.FsmVariables.FindFsmBool("Installed").Value && part.transform.parent == null
                            && part.FsmVariables.FindFsmFloat("Wear").Value == 65 && mount.FsmVariables.FindFsmFloat("DataPower").Value == 0
                            && mount.FsmVariables.FindFsmFloat("DataTorque").Value == 0 && mount.FsmVariables.FindFsmFloat("DataPowerAdd").Value == 0, "Native exhaust removal did not execute.");
                        mount.FsmVariables.FindFsmBool("Installed").Value = true; mount.FsmVariables.FindFsmFloat("Wear").Value = 77; part.FsmVariables.FindFsmFloat("Wear").Value = 77;
                        mount.FsmVariables.FindFsmFloat("DataPower").Value = 70 + group; mount.FsmVariables.FindFsmFloat("DataTorque").Value = 170 + group; mount.FsmVariables.FindFsmFloat("DataPowerAdd").Value = .5f + group;
                        part.transform.parent = mount.transform; NativeBagPartChecks.Fire(mount, "Update 2"); f.AssertSaved();
                    });
                }
                check("exhaust protection: moved headers and fixed pipe mounts remain protected through disconnect", () =>
                {
                    f.SavedCylinderHeadPart.gameObject.name = "renamed exhaust head"; protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { true }); Property(f.Session, "IsHost", false);
                    Require(f.Prepare(), "Exhaust admission failed.");
                    foreach (var mount in mounts)
                    {
                        Require(!mount.enabled && !mount.Fsm.RestartOnEnable, "Saved exhaust mount resumed."); mount.FsmVariables.FindFsmFloat("Wear").Value = 12;
                        mount.Fsm.Update(); NativeBagPartChecks.Fire(mount, "Remove part"); mount.FsmVariables.FindFsmFloat("Wear").Value = 77;
                    }
                    f.AssertSaved(); Call(f.Sync, "ReleaseSession");
                    foreach (var mount in mounts) { mount.SendEvent("BREAKOFF"); NativeBagPartChecks.Fire(mount, "Remove part"); Require(!mount.enabled, "Disconnect resumed exhaust removal."); }
                    f.AssertSaved();
                });
            }
            finally { protectedProperty.GetSetMethod(true).Invoke(policy, new object[] { savedProtected }); }
        }
    }
}
