using System;
using System.Collections;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        internal static void RunRockers(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN115", "Cylinders"))
            {
                var owners = new object[8];
                for (int i = 1; i <= 8; i++) owners[i - 1] = Get(f.RockerRead(i), "gameObject");
                check("rockers: eight warmed native readers initially use their own saved mounts", () =>
                {
                    for (int i = 1; i <= 8; i++)
                    {
                        Require(f.ReadRocker(i), "Saved rocker baseline failed.");
                        Require(ReferenceEquals(Get(f.RockerRead(i), "goLastFrame"), f.Rockers[i - 1].Mount.gameObject), "Rocker read cached a different slot.");
                    }
                    f.AssertRockersSaved();
                });
                check("rockers: absent host parts cannot borrow saved Bolted flags", () =>
                {
                    Require(f.Prepare(), "Rocker source preparation failed.");
                    var proxies = new System.Collections.Generic.HashSet<GameObject>();
                    for (int i = 1; i <= 8; i++)
                    {
                        Require(!f.ReadRocker(i), "Absent rocker borrowed saved Bolted.");
                        Require(proxies.Add(f.Target(f.RockerRead(i))), "Two rocker slots share a proxy.");
                    }
                    Require(((IList)Get(f.Sync, "_guestEngineInputs")).Count == 30, "Cylinders did not retain all independent sources.");
                    f.AssertRockersSaved();
                });
                foreach (float tightness in new[] { -.25f, 0f, .999f, 1f, 1.25f, 8f })
                {
                    float value = tightness;
                    check("rockers: native producer and projected tightness " + value + " agree", () =>
                    {
                        f.RockerTemplate.FsmVariables.FindFsmFloat("Tightness").Value = value; NativeBagPartChecks.Fire(f.RockerTemplate, "Tightness?");
                        bool native = f.RockerOutput.FsmVariables.FindFsmBool("Bolted").Value;
                        var state = f.ReceiveRocker(1, value); Require(f.Prepare(), "Rocker tightness update failed.");
                        Require(native == (value >= 1) && f.ReadRocker(1) == native && PartSlotPolicy.RockerBoltedInput(state, 1, value) == native, "Native rocker threshold disagrees with projection.");
                        f.AssertRockersSaved();
                    });
                }
                for (int i = 1; i <= 8; i++) f.ReceiveRocker(i, 8);
                for (int i = 1; i <= 4; i++) f.ReceivePiston(i, 100);
                f.LoadCylinderRockerDecisions(); Require(f.Prepare(), "Native cylinder decisions failed preparation.");
                for (int i = 1; i <= 8; i++)
                {
                    int slot = i;
                    check("rockers: slot " + slot + " controls only its own cylinder inlet or exhaust gate", () =>
                    {
                        int cylinder = (slot + 1) / 2; f.Fire("Cylinder" + cylinder);
                        Require(f.Reader.ActiveStateName == "Add to power " + cylinder, "Fitted rocker pair did not pass its native cylinder gate.");
                        f.ReceiveRocker(slot, .999f); Require(f.Prepare(), "Rocker loosening failed."); Require(!f.ReadRocker(slot), "Loose rocker remained bolted.");
                        f.Fire("Cylinder" + cylinder); Require(f.Reader.ActiveStateName != "Add to power " + cylinder, "Loose rocker let its cylinder fire.");
                        for (int other = 1; other <= 8; other++) if (other != slot) Require(f.ReadRocker(other), "Rocker loosening cleared a different slot.");
                        f.ReceiveRocker(slot, 1); Require(f.Prepare(), "Rocker tightening failed."); f.Fire("Cylinder" + cylinder);
                        Require(f.Reader.ActiveStateName == "Add to power " + cylinder, "Rocker at exactly one failed native cylinder gate.");
                        f.ReceiveRocker(slot, 8); f.Prepare(); f.AssertRockersSaved();
                    });
                }
                check("rockers: a pending revision and removal close input before old presentation changes", () =>
                {
                    f.ReceiveRocker(3, 8, false); Require(f.Prepare() && !f.ReadRocker(3), "Pending rocker supplied Bolted.");
                    f.ReceiveRocker(3, 8); Require(f.Prepare() && f.ReadRocker(3), "Applied rocker did not recover.");
                    f.ReceiveRocker(3, 0, false, false); Require(f.Prepare() && !f.ReadRocker(3), "Removed rocker retained its fitted input.");
                    Require(f.Rockers[2].Part.transform.parent == f.Rockers[2].Mount.transform, "Removal fixture moved the presentation early.");
                    f.ReceiveRocker(3, 8); f.Prepare(); f.AssertRockersSaved();
                });
                check("rockers: latest bolt receipts update Bolted without rewriting shared native scratch", () =>
                {
                    uint id = f.Rockers[3].PartId; Require(f.ReadRocker(4), "Initial rocker receipt baseline failed.");
                    Call(f.Bridge, "ObserveReplacementTightness", id, 0f); Require(f.Prepare(), "Latest rocker receipt preparation failed.");
                    Require(f.Reader.FsmVariables.FindFsmBool("Installed4").Value, "Projection overwrote calculation scratch before a native read.");
                    Require(!f.ReadRocker(4), "Latest loose receipt was ignored.");
                    Call(f.Bridge, "ObserveReplacementTightness", id, 1f); Require(f.Prepare() && f.ReadRocker(4), "Latest tightening receipt failed.");
                    Call(f.Bridge, "ObserveReplacementTightness", id, 8f); f.Prepare(); f.AssertRockersSaved();
                });
                check("rockers: assembly index, array family and actual parent must match the accepted slot", () =>
                {
                    f.ReceiveRocker(2, 8, assembly: 3); Require(f.Prepare() && !f.ReadRocker(2), "Wrong host assembly index supplied input.");
                    f.ReceiveRocker(2, 8); var part = f.Rockers[1].Part; var reference = part.FsmVariables.FindFsmString("ArrayReference"); reference.Value = "Mainbearings";
                    try { Require(f.Prepare() && !f.ReadRocker(2), "Wrong replica array family supplied input."); }
                    finally { reference.Value = "Rockers"; }
                    part.transform.SetParent(f.Rockers[2].Mount.transform, false);
                    try { Require(f.Prepare() && !f.ReadRocker(2), "Wrong actual mount supplied input."); }
                    finally { part.transform.SetParent(f.Rockers[1].Mount.transform, false); }
                    Require(f.Prepare() && f.ReadRocker(2), "Repaired rocker assignment did not recover."); f.AssertRockersSaved();
                });
                check("rockers: conflicting host attachments close the occupied slot without borrowing another", () =>
                {
                    f.ReceiveRocker(2, 8, false, mountSlot: 1, assembly: 1); Require(f.Prepare(), "Conflicting rocker preparation failed.");
                    Require(!f.ReadRocker(1) && !f.ReadRocker(2) && f.ReadRocker(3), "Conflicting rocker states did not isolate the affected slots.");
                    f.ReceiveRocker(2, 8); Require(f.Prepare() && f.ReadRocker(1) && f.ReadRocker(2), "Rocker conflict removal failed."); f.AssertRockersSaved();
                });
                check("rockers: native head movement preserves all eight relative slot identities", () =>
                {
                    var head = f.Rockers[0].Mount.transform.parent.parent.parent; var parent = head.parent;
                    Require(head.name == "VIN1110", "Rocker fixture lost its native head root."); head.SetParent(f.Car.transform, false);
                    try { Require(f.Prepare(), "Moving the head broke its slot table."); for (int i = 1; i <= 8; i++) Require(f.ReadRocker(i), "Moved rocker slot became absent."); }
                    finally { head.SetParent(parent, false); }
                    f.AssertRockersSaved();
                });
                foreach (string fault in new[] { "swapped", "duplicate", "missing", "extra", "zero occupied", "database", "duplicate array" })
                {
                    string scenario = fault;
                    check("rockers: " + scenario + " slot table pauses and recovers the native consumer", () =>
                    {
                        object first = f.SlotArray[1], second = f.SlotArray[2]; Component? duplicate = null;
                        if (scenario == "swapped") { f.SlotArray[1] = second; f.SlotArray[2] = first; }
                        if (scenario == "duplicate") f.SlotArray[2] = first;
                        if (scenario == "missing") f.SlotArray[2] = null;
                        if (scenario == "extra") f.SlotArray.Add(first);
                        if (scenario == "zero occupied") f.SlotArray[0] = first;
                        if (scenario == "database") f.SlotDatabase.name = "Wrong database";
                        if (scenario == "duplicate array")
                        {
                            duplicate = f.SlotDatabase.AddComponent(f.SlotDatabase.GetComponent("PlayMakerArrayListProxy").GetType()); Set(duplicate, "referenceName", "Rockers");
                        }
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Malformed slot table left native cylinders enabled."); f.Fire("Cylinder1"); Require(!f.Reader.enabled, "Native state entry bypassed the slot guard."); }
                        finally
                        {
                            f.SlotArray[0] = null; f.SlotArray[1] = first; f.SlotArray[2] = second;
                            if (scenario == "extra") f.SlotArray.RemoveAt(9); f.SlotDatabase.name = "AssembyDatabase";
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                            f.Prepare();
                        }
                        Require(f.Reader.enabled, "Repaired rocker table did not resume."); for (int i = 1; i <= 8; i++) Require(f.ReadRocker(i), "Repaired table retained stale rocker input.");
                        f.AssertRockersSaved();
                    });
                }
                check("rockers: changed native producer threshold blocks an unsafe derived Bolted input", () =>
                {
                    var compare = NativeBagPartChecks.State(f.RockerTemplate, "Tightness?").Actions[1]; var value = (FsmFloat)Get(compare, "float2"); value.Value = 2;
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Changed native threshold silently used the old derivation."); }
                    finally { value.Value = 1; f.Prepare(); }
                    Require(f.Reader.enabled && f.ReadRocker(1), "Repaired native rocker producer failed to recover."); f.AssertRockersSaved();
                });
                check("rockers: one invalid reader pauses cylinders while cam wear keeps running", () =>
                {
                    var wearing = f.AddConsumer("Wearing"); f.Receive(90, 16, .8f, true, efficiency: 1); f.Prepare();
                    var action = f.RockerRead(8); var name = Get(action, "variableName"); Set(action, "variableName", new FsmString { Value = "Installed" });
                    try
                    {
                        Require(!f.Prepare() && !f.Reader.enabled && wearing.enabled, "Rocker failure escaped cylinder scope.");
                        NativeBagPartChecks.State(wearing, "State 4").Actions[0].OnEnter();
                        Require(Near(wearing.FsmVariables.FindFsmFloat("DurabilityCamshaft").Value, .8f), "Healthy wear consumer stopped receiving input.");
                    }
                    finally { Set(action, "variableName", name); f.Prepare(); UnityEngine.Object.DestroyImmediate(wearing); f.Prepare(); }
                    Require(f.Reader.enabled && f.ReadRocker(8), "Repaired rocker reader did not recover."); f.AssertRockersSaved();
                });
                check("rockers: one destroyed proxy rebuilds without replacing other slot targets", () =>
                {
                    var previous = f.Target(f.RockerRead(5)); var unchanged = f.Target(f.RockerRead(6)); UnityEngine.Object.DestroyImmediate(previous);
                    Require(f.Prepare() && f.ReadRocker(5), "Destroyed rocker proxy failed recovery.");
                    Require(f.Target(f.RockerRead(5)) != previous && f.Target(f.RockerRead(6)) == unchanged, "Rocker proxy recovery rebuilt the wrong slot."); f.AssertRockersSaved();
                });
                check("rockers: disconnect neutralizes all slots and cleanup restores original native targets", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Rocker disconnect failed.");
                    for (int i = 1; i <= 8; i++) Require(!f.ReadRocker(i), "Disconnected rocker retained input.");
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 1; i <= 8; i++) Require(ReferenceEquals(Get(f.RockerRead(i), "gameObject"), owners[i - 1]) && f.ReadRocker(i), "Rocker cleanup lost saved native cache target.");
                    f.AssertRockersSaved();
                });
            }
        }
    }
}
