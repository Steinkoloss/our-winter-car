using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal FsmStateAction BearingRead(int slot) => Action("Pressure leak", 1 + 2 * slot);
            internal float ReadBearing(int slot)
            {
                BearingRead(slot).OnEnter(); return Reader.FsmVariables.FindFsmFloat("Condition").Value;
            }
        }

        internal static void RunBearings(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN102", "Wearing"))
            {
                var owners = new object[5]; for (int i = 1; i <= 5; i++) owners[i - 1] = Get(f.BearingRead(i), "gameObject");
                var pressure = Empty(f.Reader.gameObject, "Pressure"); pressure.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "PressureLeak", UseVariable = true } };
                NativeBagPartChecks.Start(pressure);
                // Include the native final output to the separate Pressure FSM;
                // its downstream oil-flow simulation remains outside this fixture.
                var state = NativeBagPartChecks.State(f.Reader, "Pressure leak"); state.Actions[16] = f.Import("Pressure leak", 16); state.Actions[16].Init(state);
                check("bearings: all five native reads warm their own saved mount caches", () =>
                {
                    for (int slot = 1; slot <= 5; slot++)
                    {
                        Require(f.ReadBearing(slot) == 77 && ReferenceEquals(Get(f.BearingRead(slot), "goLastFrame"), f.Bearings[slot - 1].Mount.gameObject), "Saved bearing cache used the wrong mount.");
                    }
                    f.AssertSaved();
                });
                check("bearings: absent host slots use independent zero-wear proxies", () =>
                {
                    Require(f.Prepare(), "Bearing preparation failed."); var targets = new HashSet<GameObject>();
                    for (int slot = 1; slot <= 5; slot++)
                    {
                        Require(f.ReadBearing(slot) == 0 && targets.Add(f.Target(f.BearingRead(slot))), "Absent bearings borrowed saved or shared inputs.");
                        var proxy = f.Target(f.BearingRead(slot)); var data = proxy.GetComponent<PlayMakerFSM>();
                        Require(!data.enabled && data.FsmVariables.FindFsmBool("Bolted") == null && proxy.GetComponent<Rigidbody>() == null, "Wear-only bearing proxy gained derived bolt or physics state.");
                    }
                    Require(((IList)Get(f.Sync, "_guestEngineInputs")).Count == 10, "Wearing did not retain all ten independent sources."); f.AssertSaved();
                });
                for (int i = 1; i <= 5; i++)
                {
                    int slot = i;
                    check("bearings: slot " + slot + " waits for applied state and preserves shared scratch", () =>
                    {
                        f.ReceiveBearing(slot, slot, false); Require(f.Prepare() && f.ReadBearing(slot) == 0, "Pending bearing supplied wear.");
                        f.ReceiveBearing(slot, slot); Require(f.Prepare() && f.ReadBearing(slot) == slot, "Applied bearing did not supply its condition.");
                        var proxy = f.Target(f.BearingRead(slot)); f.ReceiveBearing(slot, slot * 2); Require(f.Prepare(), "Bearing update failed.");
                        Require(f.Reader.FsmVariables.FindFsmFloat("Condition").Value == slot && f.Target(f.BearingRead(slot)) == proxy, "Projection overwrote scratch or rebuilt a healthy proxy.");
                        Require(f.ReadBearing(slot) == slot * 2, "Next native read lost updated wear."); f.AssertSaved();
                    });
                }
                foreach (float rpm in new[] { 0f, 800f, 6400f, 8000f })
                {
                    float speed = rpm;
                    check("bearings: native pressure accumulation and output at RPM " + speed + " use all host slots", () =>
                    {
                        f.Receive(13, 0, 0, true); float[] wear = { 1, 3, 7, 11, 17 };
                        for (int slot = 1; slot <= 5; slot++) f.ReceiveBearing(slot, wear[slot - 1]);
                        Require(f.Prepare(), "Pressure prerequisites failed."); ((FsmFloat)Get(f.Action("Pressure leak", 14), "float1")).Value = speed;
                        f.Fire("Pressure leak"); float expected = Math.Max(speed / 8000, .52f);
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("OilPressureCondition").Value, expected)
                            && Near(pressure.FsmVariables.FindFsmFloat("PressureLeak").Value, expected), "Native pressure accumulation/clamp/output diverged.");
                        Require(f.Reader.FsmVariables.FindFsmFloat("Condition").Value == 17 && f.Prepare() && f.Reader.FsmVariables.FindFsmFloat("Condition").Value == 17, "Preparation overwrote final native bearing scratch."); f.AssertSaved();
                    });
                }
                check("bearings: high combined condition uses the native maximum pressure clamp", () =>
                {
                    for (int slot = 1; slot <= 5; slot++) f.ReceiveBearing(slot, 90);
                    f.Prepare(); ((FsmFloat)Get(f.Action("Pressure leak", 14), "float1")).Value = 800; f.Fire("Pressure leak");
                    Require(pressure.FsmVariables.FindFsmFloat("PressureLeak").Value == 1, "Native maximum pressure clamp diverged."); f.AssertSaved();
                });
                for (int i = 1; i <= 5; i++)
                {
                    int slot = i;
                    check("bearings: slot " + slot + " removal affects only its own pressure contribution", () =>
                    {
                        for (int other = 1; other <= 5; other++) f.ReceiveBearing(other, other * 2);
                        f.Receive(13, 0, 0, true); f.ReceiveBearing(slot, 90, false, false); Require(f.Prepare(), "Bearing removal failed.");
                        for (int other = 1; other <= 5; other++) Require(f.ReadBearing(other) == (other == slot ? 0 : other * 2), "Removal changed another bearing's input.");
                        ((FsmFloat)Get(f.Action("Pressure leak", 14), "float1")).Value = 0; f.Fire("Pressure leak");
                        Require(Near(pressure.FsmVariables.FindFsmFloat("PressureLeak").Value, (43 - slot * 2) / 100f), "Removed bearing retained its pressure contribution.");
                        f.ReceiveBearing(slot, slot * 2); Require(f.Prepare() && f.ReadBearing(slot) == slot * 2, "Refitted bearing did not recover."); f.AssertSaved();
                    });
                }
                foreach (string reason in new[] { "assembly", "live assembly", "array family", "parent", "identity", "revision", "pending", "hidden" })
                {
                    string fault = reason;
                    check("bearings: " + fault + " mismatch closes only the affected slot", () =>
                    {
                        var v = f.Bearings[1]; var data = v.Part; f.ReceiveBearing(2, 4);
                        if (fault == "assembly") f.ReceiveBearing(2, 4, assembly: 3);
                        if (fault == "live assembly") data.FsmVariables.FindFsmInt("AssemblyID").Value = 3;
                        if (fault == "array family") data.FsmVariables.FindFsmString("ArrayReference").Value = "Rockers";
                        if (fault == "parent") data.transform.SetParent(f.Bearings[2].Mount.transform, false);
                        if (fault == "identity") data.FsmVariables.FindFsmString("ID").Value = "VIN104999";
                        if (fault == "revision") Set(v.Binding, "AppliedRevision", (uint)0);
                        if (fault == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(v.PartId);
                        if (fault == "hidden") Set(v.Binding, "FittedPresentation", false);
                        try { Require(f.Prepare() && f.ReadBearing(2) == 0 && f.ReadBearing(3) == 6, "Unavailable bearing borrowed input or cleared another slot."); f.AssertSaved(); }
                        finally { data.FsmVariables.FindFsmString("ID").Value = v.NativeId; data.FsmVariables.FindFsmString("ArrayReference").Value = "MainBearings"; data.transform.SetParent(v.Mount.transform, false); f.ReceiveBearing(2, 4); f.Prepare(); }
                    });
                }
                check("bearings: competing attachments close both affected slots until reassigned", () =>
                {
                    f.ReceiveBearing(2, 4, false, mountSlot: 1, assembly: 1); Require(f.Prepare(), "Conflicting bearing preparation failed.");
                    Require(f.ReadBearing(1) == 0 && f.ReadBearing(2) == 0 && f.ReadBearing(3) == 6, "Conflict supplied another slot's wear.");
                    f.ReceiveBearing(2, 4); Require(f.Prepare() && f.ReadBearing(1) == 2 && f.ReadBearing(2) == 4, "Bearing conflict did not recover."); f.AssertSaved();
                });
                foreach (string reason in new[] { "swapped", "duplicate", "missing", "extra", "zero occupied", "database", "duplicate array" })
                {
                    string fault = reason;
                    check("bearings: " + fault + " native slot table pauses and repairs Wearing", () =>
                    {
                        object first = f.BearingSlotArray[1], second = f.BearingSlotArray[2]; Component? duplicate = null;
                        if (fault == "swapped") { f.BearingSlotArray[1] = second; f.BearingSlotArray[2] = first; }
                        if (fault == "duplicate") f.BearingSlotArray[2] = first;
                        if (fault == "missing") f.BearingSlotArray[2] = null;
                        if (fault == "extra") f.BearingSlotArray.Add(first);
                        if (fault == "zero occupied") f.BearingSlotArray[0] = first;
                        if (fault == "database") f.SlotDatabase.name = "Wrong database";
                        if (fault == "duplicate array") { duplicate = f.SlotDatabase.AddComponent(f.SlotDatabase.GetComponent("PlayMakerArrayListProxy").GetType()); Set(duplicate, "referenceName", "MainBearings"); }
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Invalid slot table left Wearing enabled."); f.Fire("Pressure leak"); Require(!f.Reader.enabled, "Native state entry bypassed slot validation."); }
                        finally
                        {
                            f.BearingSlotArray[0] = null; f.BearingSlotArray[1] = first; f.BearingSlotArray[2] = second;
                            if (fault == "extra") f.BearingSlotArray.RemoveAt(6); f.SlotDatabase.name = "AssembyDatabase";
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); f.Prepare();
                        }
                        Require(f.Reader.enabled && f.ReadBearing(2) == 4, "Repaired bearing table stayed paused."); f.AssertSaved();
                    });
                }
                foreach (string reason in new[] { "template family", "template scalar", "reader" })
                {
                    string fault = reason;
                    check("bearings: changed " + fault + " pauses and repairs its native consumer", () =>
                    {
                        var reference = f.BearingTemplate.FsmVariables.FindFsmString("ArrayReference"); var floats = f.BearingTemplate.FsmVariables.FloatVariables;
                        var read = f.BearingRead(5); var variable = Get(read, "variableName");
                        if (fault == "template family") reference.Value = "Rockers";
                        if (fault == "template scalar") f.BearingTemplate.FsmVariables.FloatVariables = new FsmFloat[0];
                        if (fault == "reader") Set(read, "variableName", new FsmString { Value = "Tightness" });
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Changed native bearing source escaped validation."); f.AssertSaved(); }
                        finally { reference.Value = "MainBearings"; f.BearingTemplate.FsmVariables.FloatVariables = floats; Set(read, "variableName", variable); f.Prepare(); }
                        Require(f.Reader.enabled && f.ReadBearing(5) == 10, "Repaired bearing source failed to recover.");
                    });
                }
                check("bearings: moving the native block preserves all five relative slot identities", () =>
                {
                    var block = f.Bearings[0].Mount.transform.parent; var parent = block.parent; block.SetParent(f.Car.transform, false);
                    try { Require(f.Prepare(), "Moving block broke its bearing slot table."); for (int i = 1; i <= 5; i++) Require(f.ReadBearing(i) == i * 2, "Moved bearing lost input."); }
                    finally { block.SetParent(parent, false); } f.AssertSaved();
                });
                check("bearings: rebuilding one warmed proxy preserves other slot targets", () =>
                {
                    var previous = f.Target(f.BearingRead(3)); var other = f.Target(f.BearingRead(4)); UnityEngine.Object.DestroyImmediate(previous.GetComponent<PlayMakerFSM>());
                    Require(f.Prepare() && f.ReadBearing(3) == 6 && f.Target(f.BearingRead(3)) != previous && f.Target(f.BearingRead(4)) == other, "Bearing cache recovery replaced the wrong input.");
                    Require(ReferenceEquals(Get(f.BearingRead(3), "goLastFrame"), f.Target(f.BearingRead(3))), "Native read retained destroyed Data."); f.AssertSaved();
                });
                for (int i = 1; i <= 5; i++)
                {
                    int slot = i;
                    check("bearings: host capture and replay retain slot " + slot + " identity and scalar order", () =>
                    {
                        var v = f.Bearings[slot - 1]; Property(f.Session, "IsHost", true); Set(v.Binding, "Replica", false);
                        var wear = v.Part.FsmVariables.FindFsmFloat("Wear"); float saved = wear.Value;
                        try
                        {
                            wear.Value = 40 + slot; var captured = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", v.PartId);
                            Require(captured != null && captured.Installed && captured.AssemblyId == slot && captured.NativeId == v.NativeId
                                && captured.Scalars.Length == 2 && captured.Scalars[0] == 40 + slot && captured.Scalars[1] == 16, "Host bearing capture lost its slot or condition.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(captured!)); wear.Value = 30;
                            var changed = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", v.PartId);
                            Require(changed != null && changed.Revision != captured!.Revision, "Host bearing wear did not advance revision.");
                            Call(f.Sync, "ApplyReplacementScalars", v.Binding, decoded); Require(wear.Value == 40 + slot, "Bearing replay lost host condition."); f.AssertSaved();
                        }
                        finally { wear.Value = saved; Property(f.Session, "IsHost", false); Set(v.Binding, "Replica", true); }
                    });
                }
                check("bearings: disconnect clears all host slots and cleanup restores saved caches", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Bearing disconnect failed.");
                    for (int slot = 1; slot <= 5; slot++) Require(f.ReadBearing(slot) == 0, "Disconnected bearing retained input.");
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int slot = 1; slot <= 5; slot++) Require(ReferenceEquals(Get(f.BearingRead(slot), "gameObject"), owners[slot - 1]) && f.ReadBearing(slot) == 77, "Cleanup lost saved bearing cache.");
                    f.AssertSaved();
                });
            }
        }
    }
}
