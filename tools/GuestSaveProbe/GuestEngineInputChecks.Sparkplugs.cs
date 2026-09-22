using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal FsmStateAction PlugRead(int slot, int field)
            {
                int cylinder = 5 - slot;
                return field == 0 ? Action("Reset", 8 + cylinder) : field == 1 ? Action("Cylinder" + cylinder, 3)
                    : field == 2 ? Action("Add to power " + cylinder, 2) : Action("Plug data", 6 + cylinder);
            }
            internal void AssertPlug(int slot, bool installed, float wear, float tightness, float durability)
            {
                int cylinder = 5 - slot;
                for (int field = 0; field < 4; field++) PlugRead(slot, field).OnEnter();
                Require(Reader.FsmVariables.FindFsmBool("Sparkplug" + cylinder).Value == installed
                    && Reader.FsmVariables.FindFsmFloat("Sparkplug" + cylinder + "Wear").Value == wear
                    && Reader.FsmVariables.FindFsmFloat("Tightness").Value == tightness
                    && Reader.FsmVariables.FindFsmFloat("Sparkplug" + cylinder + "Durability").Value == durability,
                    "Cylinder " + cylinder + " did not use host slot " + slot + " condition.");
            }
            internal void LoadPlugDecisions()
            {
                LoadPistonDecisions();
                for (int cylinder = 1; cylinder <= 4; cylinder++)
                {
                    string name = "Add to power " + cylinder; var state = NativeBagPartChecks.State(Reader, name);
                    foreach (int i in new[] { 4, 5 }) { state.Actions[i] = Import(name, i); state.Actions[i].Init(state); }
                    RestoreNativeTransitions(name);
                    string wear = "Plug wear" + (cylinder == 1 ? "" : " " + cylinder);
                    var wearState = NativeBagPartChecks.State(Reader, wear); wearState.Actions[0] = Import(wear, 0); wearState.Actions[0].Init(wearState);
                    string misfire = "Misfire " + (cylinder + 1); var target = NativeBagPartChecks.State(Reader, misfire);
                    for (int i = 0; i < target.Actions.Length; i++) { target.Actions[i] = Import(misfire, i); target.Actions[i].Init(target); }
                }
            }
            private void RestoreNativeTransitions(string state)
            {
                foreach (Dictionary<string, object> row in (IEnumerable)_row["states"])
                    if ((string)row["name"] == state)
                    {
                        var transitions = new List<FsmTransition>();
                        foreach (Dictionary<string, object> t in (IEnumerable)row["transitions"])
                            transitions.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent((string)t["event"]), ToState = (string)t["to"] });
                        NativeBagPartChecks.State(Reader, state).Transitions = transitions.ToArray(); return;
                    }
            }
        }

        private static void CheckPlugRecovery(Fixture f, Action<string, Action> check)
        {
            foreach (string reason in new[] { "assembly", "live assembly", "array family", "parent", "identity", "revision", "pending", "hidden" })
            {
                string fault = reason;
                check("sparkplug engine: " + fault + " mismatch clears all four values for one slot", () =>
                {
                    var v = f.Sparkplugs[1]; var part = v.Part; f.ReceiveSparkplug(2, 90);
                    if (fault == "assembly") f.ReceiveSparkplug(2, 90, assembly: 3);
                    if (fault == "live assembly") part.FsmVariables.FindFsmInt("AssemblyID").Value = 3;
                    if (fault == "array family") part.FsmVariables.FindFsmString("ArrayReference").Value = "Pistons";
                    if (fault == "parent") part.transform.SetParent(f.Sparkplugs[2].Mount.transform, false);
                    if (fault == "identity") part.FsmVariables.FindFsmString("ID").Value = "SPRKPLUG0999";
                    if (fault == "revision") Set(v.Binding, "AppliedRevision", (uint)0);
                    if (fault == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(v.PartId);
                    if (fault == "hidden") Set(v.Binding, "FittedPresentation", false);
                    try { Require(f.Prepare(), "Unavailable plug preparation failed."); f.AssertPlug(2, false, 0, 0, 0); f.AssertPlug(3, true, 90, 8, .75f); f.AssertSaved(); }
                    finally { part.FsmVariables.FindFsmString("ID").Value = v.NativeId; part.FsmVariables.FindFsmString("ArrayReference").Value = "Sparkplugs"; part.transform.SetParent(v.Mount.transform, false); f.ReceiveSparkplug(2, 90); f.Prepare(); }
                });
            }
            check("sparkplug engine: competing attachments close both affected slots", () =>
            {
                f.ReceiveSparkplug(2, 90, applied: false, mountSlot: 1, assembly: 1); Require(f.Prepare(), "Conflicting plug preparation failed.");
                f.AssertPlug(1, false, 0, 0, 0); f.AssertPlug(2, false, 0, 0, 0); f.AssertPlug(3, true, 90, 8, .75f);
                f.ReceiveSparkplug(2, 90); Require(f.Prepare(), "Conflict recovery failed."); f.AssertPlug(1, true, 90, 8, .75f); f.AssertSaved();
            });
            foreach (string reason in new[] { "swapped", "duplicate", "missing", "extra", "zero occupied", "duplicate array",
                "renamed array", "replaced array", "null array" })
            {
                string fault = reason;
                check("sparkplug engine: " + fault + " socket table pauses and safely recovers the consumer", () =>
                {
                    object first = f.SparkplugSlotArray[1], second = f.SparkplugSlotArray[2]; Component? duplicate = null;
                    Component? proxy = null;
                    foreach (var component in f.SlotDatabase.GetComponents<MonoBehaviour>())
                        if (component != null && component.GetType().Name == "PlayMakerArrayListProxy"
                            && Get(component, "referenceName") as string == "Sparkplugs") proxy = component;
                    Require(proxy != null, "Sparkplug slot component missing from fixture.");
                    var originalArray = Get(proxy!, "_arrayList");
                    if (fault == "swapped") { f.SparkplugSlotArray[1] = second; f.SparkplugSlotArray[2] = first; }
                    if (fault == "duplicate") f.SparkplugSlotArray[2] = first;
                    if (fault == "missing") f.SparkplugSlotArray[2] = null;
                    if (fault == "extra") f.SparkplugSlotArray.Add(first);
                    if (fault == "zero occupied") f.SparkplugSlotArray[0] = first;
                    if (fault == "duplicate array") { duplicate = f.SlotDatabase.AddComponent(f.SlotDatabase.GetComponent("PlayMakerArrayListProxy").GetType()); Set(duplicate, "referenceName", "Sparkplugs"); }
                    if (fault == "renamed array") Set(proxy!, "referenceName", "Changed reference");
                    if (fault == "replaced array") Set(proxy!, "_arrayList", new ArrayList { null, first, first, first, first });
                    if (fault == "null array") Set(proxy!, "_arrayList", null);
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Invalid socket table did not pause Cylinders."); f.Fire("Cylinder1"); Require(!f.Reader.enabled, "State entry bypassed invalid sockets."); f.AssertSaved(); }
                    finally
                    {
                        Set(proxy!, "referenceName", "Sparkplugs"); Set(proxy!, "_arrayList", originalArray);
                        f.SparkplugSlotArray[0] = null; f.SparkplugSlotArray[1] = first; f.SparkplugSlotArray[2] = second;
                        if (fault == "extra") f.SparkplugSlotArray.RemoveAt(5);
                        if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                        f.Prepare();
                    }
                    Require(f.Reader.enabled, "Repaired socket table remained paused."); f.AssertPlug(2, true, 90, 8, .75f);
                });
            }
            for (int i = 0; i < 4; i++)
            {
                int field = i;
                check("sparkplug engine: changed native read " + field + " pauses and recovers without saved writes", () =>
                {
                    var action = f.PlugRead(2, field); var name = Get(action, "variableName"); Set(action, "variableName", new FsmString { Value = "Other" });
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Changed plug read stayed active."); f.AssertSaved(); }
                    finally { Set(action, "variableName", name); f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired plug read stayed paused."); f.AssertPlug(2, true, 90, 8, .75f);
                });
            }
            check("sparkplug engine: rebuilding one proxy repairs all four warmed native caches", () =>
            {
                var old = f.Target(f.PlugRead(2, 0)); var other = f.Target(f.PlugRead(3, 0)); UnityEngine.Object.DestroyImmediate(old.GetComponent<PlayMakerFSM>());
                Require(f.Prepare(), "Plug cache repair failed."); f.AssertPlug(2, true, 90, 8, .75f);
                Require(f.Target(f.PlugRead(2, 0)) != old && f.Target(f.PlugRead(3, 0)) == other, "Repair rebuilt another cylinder's input.");
                for (int field = 0; field < 4; field++) Require(ReferenceEquals(Get(f.PlugRead(2, field), "goLastFrame"), f.Target(f.PlugRead(2, 0))), "Native cache kept destroyed proxy Data."); f.AssertSaved();
            });
            for (int i = 1; i <= 4; i++)
            {
                int slot = i;
                check("sparkplug engine: host capture and wire replay preserve slot " + slot + " scalar order", () =>
                {
                    var v = f.Sparkplugs[slot - 1]; Property(f.Session, "IsHost", true); Set(v.Binding, "Replica", false);
                    var wear = v.Part.FsmVariables.FindFsmFloat("Wear"); float previous = wear.Value;
                    try
                    {
                        wear.Value = 40 + slot; var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", v.PartId);
                        Require(state != null && state.Installed && state.AssemblyId == slot && state.NativeId == v.NativeId
                            && state.Scalars.Length == 3 && state.Scalars[0] == 40 + slot && state.Scalars[1] == 16 && state.Scalars[2] == 2, "Host plug capture lost native condition or slot.");
                        var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!)); wear.Value = 30;
                        var next = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", v.PartId); Require(next != null && next.Revision != state!.Revision, "Changed plug wear retained its revision.");
                        Call(f.Sync, "ApplyReplacementScalars", v.Binding, decoded); Require(wear.Value == 40 + slot, "Wire replay lost host wear."); f.AssertSaved();
                    }
                    finally { wear.Value = previous; Property(f.Session, "IsHost", false); Set(v.Binding, "Replica", true); }
                });
            }
        }

        internal static void RunSparkplugs(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN115", "Cylinders", "sparkplug-engine-input-probe.json"))
            {
                var owners = new object[4, 4];
                for (int slot = 1; slot <= 4; slot++) for (int field = 0; field < 4; field++) owners[slot - 1, field] = Get(f.PlugRead(slot, field), "gameObject");
                check("sparkplug engine: native caches warm against the four saved sockets", () =>
                {
                    for (int slot = 1; slot <= 4; slot++)
                    {
                        f.AssertPlug(slot, true, 77, 8, 2);
                        for (int field = 0; field < 4; field++) Require(ReferenceEquals(Get(f.PlugRead(slot, field), "goLastFrame"), f.Sparkplugs[slot - 1].Mount.gameObject), "Native read warmed another slot.");
                        Require(f.Sparkplugs[slot - 1].Mount.gameObject.name == "VINP_Sparkplug" + (5 - slot), "Fixture reversed the native cylinder mapping.");
                    }
                    f.AssertSaved();
                });
                check("sparkplug engine: unapplied host state uses four independent inert sources", () =>
                {
                    var targets = new HashSet<GameObject>();
                    for (int slot = 1; slot <= 4; slot++) f.ReceiveSparkplug(slot, 90, applied: false);
                    Require(f.Prepare(), "Plug preparation failed.");
                    for (int slot = 1; slot <= 4; slot++)
                    {
                        f.AssertPlug(slot, false, 0, 0, 0); var target = f.Target(f.PlugRead(slot, 0));
                        Require(targets.Add(target) && target.GetComponent<Rigidbody>() == null && !target.GetComponent<PlayMakerFSM>().enabled, "Plug sources share physics or mutable state.");
                        for (int field = 1; field < 4; field++) Require(f.Target(f.PlugRead(slot, field)) == target, "A cylinder reads inconsistent plug sources.");
                        Require(f.Sparkplugs[slot - 1].Part.FsmVariables.FindFsmBool("Installed") == null, "Fixture invented slotted Installed.");
                    }
                    Require(((IList)Get(f.Sync, "_guestEngineInputs")).Count == 30, "Cylinders lost an input source."); f.AssertSaved();
                });
                for (int i = 1; i <= 4; i++)
                {
                    int slot = i;
                    check("sparkplug engine: slot " + slot + " applies independent condition without overwriting scratch", () =>
                    {
                        f.ReceiveSparkplug(slot, 60 + slot, slot, .1f * slot); Require(f.Prepare(), "Applied plug preparation failed.");
                        f.AssertPlug(slot, true, 60 + slot, slot, .1f * slot);
                        f.ReceiveSparkplug(slot, 70 + slot, slot + 1, .2f * slot); Require(f.Prepare(), "Changed plug preparation failed.");
                        Require(f.Reader.FsmVariables.FindFsmFloat("Sparkplug" + (5 - slot) + "Wear").Value == 60 + slot
                            && f.Reader.FsmVariables.FindFsmFloat("Tightness").Value == slot, "Projection overwrote native scratch.");
                        f.AssertPlug(slot, true, 70 + slot, slot + 1, .2f * slot); f.AssertSaved();
                    });
                }
                for (int slot = 1; slot <= 8; slot++) f.ReceiveRocker(slot, 8);
                for (int slot = 1; slot <= 4; slot++) f.ReceivePiston(slot, 80);
                f.LoadPlugDecisions();
                for (int i = 1; i <= 4; i++) foreach (float value in new[] { .999f, 1f, 9.999f, 10f, 90f })
                {
                    int slot = i, cylinder = 5 - i; float wear = value;
                    check("sparkplug engine: cylinder " + cylinder + " wear " + wear + " follows native firing and misfire gates", () =>
                    {
                        for (int other = 1; other <= 4; other++) f.ReceiveSparkplug(other, other == slot ? wear : 90);
                        Require(f.Prepare(), "Wear update failed."); f.Fire("Reset"); f.Fire("Cylinder" + cylinder);
                        Require(f.Reader.FsmVariables.FindFsmBool("Cyl" + cylinder + "Fires").Value == (wear >= 1), "Native firing threshold diverged.");
                        bool random = f.Reader.ActiveStateName == "Random " + (cylinder + 1);
                        Require(random == (wear >= 1 && wear < 10), "Native worn-plug misfire eligibility diverged.");
                        float efficiency = wear < 1 ? .1f : (.1f + 80 + wear + 100) / 200;
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("Cyl" + cylinder + "efficiency").Value, efficiency), "Plug condition lost native efficiency arithmetic.");
                        if (random) { f.Fire("Misfire " + (cylinder + 1)); Require(!f.Reader.FsmVariables.FindFsmBool("Cyl" + cylinder + "Fires").Value
                            && Near(f.Reader.FsmVariables.FindFsmFloat("Cyl" + cylinder + "efficiency").Value, .01f), "Native misfire consequence diverged."); }
                        f.AssertSaved();
                    });
                }
                for (int i = 1; i <= 4; i++) foreach (float value in new[] { 0f, 7f, 8f })
                {
                    int slot = i, cylinder = 5 - i; float tightness = value;
                    check("sparkplug engine: cylinder " + cylinder + " tightness " + tightness + " controls native misfire eligibility", () =>
                    {
                        for (int other = 1; other <= 4; other++) f.ReceiveSparkplug(other, 90, other == slot ? tightness : 8);
                        Require(f.Prepare(), "Tightness update failed."); f.Fire("Reset"); f.Fire("Cylinder" + cylinder);
                        Require((f.Reader.ActiveStateName == "Random " + (cylinder + 1)) == (tightness < 8), "Native loose-plug branch diverged."); f.AssertSaved();
                    });
                }
                for (int i = 1; i <= 4; i++)
                {
                    int slot = i, cylinder = 5 - i;
                    check("sparkplug engine: cylinder " + cylinder + " durability arithmetic preserves saved wear", () =>
                    {
                        f.ReceiveSparkplug(slot, 90, durability: .15f * slot); Require(f.Prepare(), "Durability update failed.");
                        f.PlugRead(slot, 3).OnEnter(); f.Reader.FsmVariables.FindFsmFloat("SparkPlugWearRate").Value = .003f;
                        f.Reader.FsmVariables.FindFsmFloat("SparkPlugOilCont").Value = .1f;
                        string state = "Plug wear" + (cylinder == 1 ? "" : " " + cylinder); f.Fire(state);
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("Math1").Value, .003f * .15f * slot), "Native durability arithmetic diverged.");
                        Require(!f.Action(state, 1).Enabled && !f.Action(state, 2).Enabled, "Guest plug-wear writers were enabled."); f.AssertSaved();
                    });
                    check("sparkplug engine: removing slot " + slot + " blocks only its cylinder and refitting recovers", () =>
                    {
                        for (int other = 1; other <= 4; other++) f.ReceiveSparkplug(other, 90);
                        f.ReceiveSparkplug(slot, 90, fitted: false); Require(f.Prepare(), "Removal failed.");
                        f.Fire("Reset"); f.Fire("Cylinder" + cylinder); Require(!f.Reader.FsmVariables.FindFsmBool("Cyl" + cylinder + "Fires").Value, "Removed plug still fires.");
                        for (int other = 1; other <= 4; other++) f.AssertPlug(other, other != slot, other == slot ? 0 : 90, other == slot ? 0 : 8, other == slot ? 0 : .75f);
                        f.ReceiveSparkplug(slot, 90); Require(f.Prepare(), "Refit failed."); f.Fire("Reset"); f.Fire("Cylinder" + cylinder);
                        Require(f.Reader.FsmVariables.FindFsmBool("Cyl" + cylinder + "Fires").Value, "Refitted plug stayed unavailable."); f.AssertSaved();
                    });
                }
                CheckPlugRecovery(f, check);
                check("sparkplug engine: disconnect clears condition and restores original native targets", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Disconnect failed.");
                    for (int slot = 1; slot <= 4; slot++) f.AssertPlug(slot, false, 0, 0, 0);
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int slot = 1; slot <= 4; slot++)
                    {
                        for (int field = 0; field < 4; field++) Require(ReferenceEquals(Get(f.PlugRead(slot, field), "gameObject"), owners[slot - 1, field]), "Cleanup lost native input wrappers.");
                        f.AssertPlug(slot, true, 77, 8, 2);
                    }
                    f.AssertSaved();
                });
            }
        }
    }
}
