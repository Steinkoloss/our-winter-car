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
            internal FsmStateAction PistonWearRead(int slot) => Action("Reset", 4 + slot);
            internal FsmStateAction PistonInstalledRead(int slot) => Action("Cylinder" + slot, 2);
            internal float ReadPistonWear(int slot) { PistonWearRead(slot).OnEnter(); return Reader.FsmVariables.FindFsmFloat("Piston" + slot + "Wear").Value; }
            internal bool ReadPistonInstalled(int slot) { PistonInstalledRead(slot).OnEnter(); return Reader.FsmVariables.FindFsmBool("Installed1").Value; }
            internal void LoadPistonDecisions()
            {
                LoadCylinderRockerDecisions(); Reader.FsmVariables.FindFsmFloat("BaseEfficiency").Value = 100;
                var reset = NativeBagPartChecks.State(Reader, "Reset");
                for (int i = 0; i < reset.Actions.Length; i++)
                    if (i < 5 || i >= 13) { reset.Actions[i] = Import("Reset", i); reset.Actions[i].Init(reset); }
                for (int slot = 1; slot <= 4; slot++)
                {
                    string name = "Add to power " + slot; var state = NativeBagPartChecks.State(Reader, name);
                    foreach (int index in new[] { 0, 1, 3 }) { state.Actions[index] = Import(name, index); state.Actions[index].Init(state); }
                }
            }
        }

        private static FsmStateAction MixturePistonRead(PlayMakerFSM mixture, int slot) => NativeBagPartChecks.State(mixture, "Pistons").Actions[(slot - 1) * 2];
        private static float ReadMixturePiston(PlayMakerFSM mixture, int slot) { MixturePistonRead(mixture, slot).OnEnter(); return mixture.FsmVariables.FindFsmFloat("Math1").Value; }
        private static void AssertPistonInput(Fixture f, PlayMakerFSM mixture, int slot, bool installed, float wear)
        {
            Require(f.ReadPistonInstalled(slot) == installed && f.ReadPistonWear(slot) == wear && ReadMixturePiston(mixture, slot) == wear, "Piston slot " + slot + " consumers disagree with applied host state.");
        }

        internal static void RunPistons(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN115", "Cylinders", "piston-engine-input-probe.json"))
            {
                var mixture = f.AddConsumer("Mixture"); var owners = new object[4, 3];
                var oilpan = Data(Child(f.Extras, "saved oilpan for piston smoke checks"), null, 0);
                var oil = new FsmFloat { Name = "Oil", UseVariable = true, Value = 4 }; var dirt = new FsmFloat { Name = "OilContamination", UseVariable = true, Value = 2 };
                var floats = new List<FsmFloat>(oilpan.FsmVariables.FloatVariables) { oil, dirt }; oilpan.FsmVariables.FloatVariables = floats.ToArray();
                NativeBagPartChecks.Start(oilpan); mixture.FsmVariables.FindFsmGameObject("db_Oilpan").Value = oilpan.gameObject;
                Action saved = () => { f.AssertSaved(); Require(oil.Value == 4 && dirt.Value == 2, "Guest smoke checks changed saved oil/contamination."); };
                for (int slot = 1; slot <= 4; slot++) { owners[slot - 1, 0] = Get(f.PistonWearRead(slot), "gameObject"); owners[slot - 1, 1] = Get(f.PistonInstalledRead(slot), "gameObject"); owners[slot - 1, 2] = Get(MixturePistonRead(mixture, slot), "gameObject"); }
                check("pistons: both consumers warm native reads against the correct saved mounts", () =>
                {
                    for (int slot = 1; slot <= 4; slot++)
                    {
                        AssertPistonInput(f, mixture, slot, true, 77);
                        foreach (var action in new[] { f.PistonWearRead(slot), f.PistonInstalledRead(slot), MixturePistonRead(mixture, slot) })
                            Require(ReferenceEquals(Get(action, "goLastFrame"), f.Pistons[slot - 1].Mount.gameObject), "Native piston cache warmed the wrong mount.");
                    }
                    saved();
                });
                check("pistons: absent host parts use eight distinct inert sources without saved installation", () =>
                {
                    Require(f.Prepare(), "Piston preparation failed."); var targets = new HashSet<GameObject>();
                    for (int slot = 1; slot <= 4; slot++)
                    {
                        AssertPistonInput(f, mixture, slot, false, 0);
                        foreach (var action in new[] { f.PistonWearRead(slot), MixturePistonRead(mixture, slot) })
                        {
                            var target = f.Target(action); Require(targets.Add(target) && target.GetComponent<Rigidbody>() == null && !target.GetComponent<PlayMakerFSM>().enabled, "Piston inputs share mutable or physical state.");
                        }
                        Require(f.Pistons[slot - 1].Part.FsmVariables.FindFsmBool("Installed") == null, "Fixture invented native part Installed.");
                    }
                    Require(((IList)Get(f.Sync, "_guestEngineInputs")).Count == 35, "Cylinders/Mixture did not retain all sources."); saved();
                });
                for (int i = 1; i <= 4; i++)
                {
                    int slot = i;
                    check("pistons: slot " + slot + " waits for applied state and preserves both consumers' scratch", () =>
                    {
                        f.ReceivePiston(slot, 60 + slot, false); Require(f.Prepare(), "Pending piston preparation failed."); AssertPistonInput(f, mixture, slot, false, 0);
                        f.ReceivePiston(slot, 60 + slot); Require(f.Prepare(), "Applied piston preparation failed."); AssertPistonInput(f, mixture, slot, true, 60 + slot);
                        f.ReceivePiston(slot, 70 + slot); Require(f.Prepare(), "Piston update failed.");
                        Require(f.Reader.FsmVariables.FindFsmFloat("Piston" + slot + "Wear").Value == 60 + slot && mixture.FsmVariables.FindFsmFloat("Math1").Value == 60 + slot, "Projection overwrote native scratch before its reader.");
                        AssertPistonInput(f, mixture, slot, true, 70 + slot); saved();
                    });
                }
                for (int slot = 1; slot <= 8; slot++) f.ReceiveRocker(slot, 8);
                f.LoadPistonDecisions(); Require(f.Prepare(), "Native piston decisions failed preparation.");
                for (int i = 1; i <= 4; i++) foreach (float value in new[] { 9.999f, 10f, 10.001f, 90f })
                {
                    int slot = i; float wear = value;
                    check("pistons: cylinder " + slot + " wear " + wear + " follows native firing, efficiency and smoke decisions", () =>
                    {
                        for (int other = 1; other <= 4; other++) f.ReceivePiston(other, other == slot ? wear : 80);
                        Require(f.Prepare(), "Piston condition update failed."); f.Fire("Reset"); f.Fire("Cylinder" + slot);
                        Require(f.Reader.FsmVariables.FindFsmBool("Cyl" + slot + "Fires").Value == (wear >= 10), "Native piston firing threshold diverged.");
                        float expected = wear < 10 ? .1f : (.1f + wear + 100 + 100) / 200;
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("Cyl" + slot + "efficiency").Value, expected), "Native piston efficiency arithmetic diverged.");
                        NativeBagPartChecks.Fire(mixture, "Pistons"); Require(mixture.ActiveStateName == (wear < 10 ? "Oil smoke" : "Wait 2"), "Native piston smoke threshold diverged.");
                        Require(!NativeBagPartChecks.State(mixture, "Oil smoke").Actions[1].Enabled && !NativeBagPartChecks.State(mixture, "Oil smoke").Actions[2].Enabled, "Guest smoke enabled saved oil writers."); saved();
                    });
                }
                for (int i = 1; i <= 4; i++)
                {
                    int slot = i;
                    check("pistons: removing slot " + slot + " blocks its cylinder without borrowing stale wear", () =>
                    {
                        for (int other = 1; other <= 4; other++) f.ReceivePiston(other, 80);
                        f.ReceivePiston(slot, 80, false, false); Require(f.Prepare(), "Piston removal failed."); f.Fire("Reset"); f.Fire("Cylinder" + slot);
                        Require(!f.Reader.FsmVariables.FindFsmBool("Cyl" + slot + "Fires").Value, "Removed piston still fires.");
                        for (int other = 1; other <= 4; other++) AssertPistonInput(f, mixture, other, other != slot, other == slot ? 0 : 80);
                        NativeBagPartChecks.Fire(mixture, "Pistons"); Require(mixture.ActiveStateName == "Oil smoke", "Missing piston did not take the native zero-wear branch.");
                        f.ReceivePiston(slot, 80); Require(f.Prepare(), "Piston refit failed."); f.Fire("Reset"); f.Fire("Cylinder" + slot);
                        Require(f.Reader.FsmVariables.FindFsmBool("Cyl" + slot + "Fires").Value, "Refitted piston did not recover."); saved();
                    });
                }
                foreach (string reason in new[] { "assembly", "live assembly", "family", "parent", "identity", "revision", "pending", "hidden" })
                {
                    string fault = reason;
                    check("pistons: " + fault + " mismatch closes the same slot in both consumers", () =>
                    {
                        var v = f.Pistons[1]; var part = v.Part; f.ReceivePiston(2, 80);
                        if (fault == "assembly") f.ReceivePiston(2, 80, assembly: 3);
                        if (fault == "live assembly") part.FsmVariables.FindFsmInt("AssemblyID").Value = 3;
                        if (fault == "family") part.FsmVariables.FindFsmString("ArrayReference").Value = "MainBearings";
                        if (fault == "parent") part.transform.SetParent(f.Pistons[2].Mount.transform, false);
                        if (fault == "identity") part.FsmVariables.FindFsmString("ID").Value = "VIN103999";
                        if (fault == "revision") Set(v.Binding, "AppliedRevision", (uint)0);
                        if (fault == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(v.PartId);
                        if (fault == "hidden") Set(v.Binding, "FittedPresentation", false);
                        try { Require(f.Prepare(), "Unavailable piston preparation failed."); AssertPistonInput(f, mixture, 2, false, 0); AssertPistonInput(f, mixture, 3, true, 80); saved(); }
                        finally { part.FsmVariables.FindFsmString("ID").Value = v.NativeId; part.FsmVariables.FindFsmString("ArrayReference").Value = "Pistons"; part.transform.SetParent(v.Mount.transform, false); f.ReceivePiston(2, 80); f.Prepare(); }
                    });
                }
                check("pistons: competing attachments close both affected slots until reassigned", () =>
                {
                    f.ReceivePiston(2, 80, false, mountSlot: 1, assembly: 1); Require(f.Prepare(), "Piston conflict failed preparation.");
                    AssertPistonInput(f, mixture, 1, false, 0); AssertPistonInput(f, mixture, 2, false, 0); AssertPistonInput(f, mixture, 3, true, 80);
                    f.ReceivePiston(2, 80); Require(f.Prepare(), "Piston conflict recovery failed."); AssertPistonInput(f, mixture, 1, true, 80); AssertPistonInput(f, mixture, 2, true, 80); saved();
                });
                foreach (string reason in new[] { "swapped", "duplicate", "missing", "extra", "zero occupied", "duplicate array" })
                {
                    string fault = reason;
                    check("pistons: " + fault + " slot table pauses both consumers and repairs without stale state", () =>
                    {
                        object first = f.PistonSlotArray[1], second = f.PistonSlotArray[2]; Component? duplicate = null;
                        if (fault == "swapped") { f.PistonSlotArray[1] = second; f.PistonSlotArray[2] = first; }
                        if (fault == "duplicate") f.PistonSlotArray[2] = first;
                        if (fault == "missing") f.PistonSlotArray[2] = null;
                        if (fault == "extra") f.PistonSlotArray.Add(first);
                        if (fault == "zero occupied") f.PistonSlotArray[0] = first;
                        if (fault == "duplicate array") { duplicate = f.SlotDatabase.AddComponent(f.SlotDatabase.GetComponent("PlayMakerArrayListProxy").GetType()); Set(duplicate, "referenceName", "Pistons"); }
                        try { Require(!f.Prepare() && !f.Reader.enabled && !mixture.enabled, "Invalid piston table left a consumer enabled."); f.Fire("Cylinder1"); NativeBagPartChecks.Fire(mixture, "Pistons"); Require(!f.Reader.enabled && !mixture.enabled, "Native state entry bypassed slot validation."); saved(); }
                        finally { f.PistonSlotArray[0] = null; f.PistonSlotArray[1] = first; f.PistonSlotArray[2] = second; if (fault == "extra") f.PistonSlotArray.RemoveAt(5); if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate); f.Prepare(); }
                        Require(f.Reader.enabled && mixture.enabled, "Repaired piston table stayed paused."); AssertPistonInput(f, mixture, 2, true, 80);
                    });
                }
                foreach (bool cylinder in new[] { true, false })
                {
                    bool primary = cylinder;
                    check("pistons: changed " + (primary ? "cylinder" : "mixture") + " read pauses only that consumer", () =>
                    {
                        var action = primary ? f.PistonInstalledRead(3) : MixturePistonRead(mixture, 3); var name = Get(action, "variableName"); Set(action, "variableName", new FsmString { Value = "Tightness" });
                        try { Require(!f.Prepare() && f.Reader.enabled == !primary && mixture.enabled == primary, "Piston signature failure escaped consumer scope."); if (primary) Require(ReadMixturePiston(mixture, 3) == 80, "Healthy mixture lost piston input."); else Require(f.ReadPistonWear(3) == 80, "Healthy cylinders lost piston input."); saved(); }
                        finally { Set(action, "variableName", name); f.Prepare(); } AssertPistonInput(f, mixture, 3, true, 80);
                    });
                }
                check("pistons: moving paired crank pivots preserves all four relative slot identities", () =>
                {
                    var pivots = f.Pistons[0].Mount.transform.parent.parent; var position = pivots.localPosition; pivots.localPosition = new Vector3(1, 2, 3);
                    try { Require(f.Prepare(), "Piston pivot movement failed."); for (int slot = 1; slot <= 4; slot++) AssertPistonInput(f, mixture, slot, true, 80); }
                    finally { pivots.localPosition = position; } saved();
                });
                check("pistons: rebuilding a cylinder proxy replaces both native caches while preserving mixture", () =>
                {
                    var previous = f.Target(f.PistonWearRead(2)); var other = f.Target(MixturePistonRead(mixture, 2)); UnityEngine.Object.DestroyImmediate(previous.GetComponent<PlayMakerFSM>());
                    Require(f.Prepare(), "Piston cache repair failed."); AssertPistonInput(f, mixture, 2, true, 80);
                    Require(f.Target(f.PistonWearRead(2)) != previous && f.Target(f.PistonInstalledRead(2)) == f.Target(f.PistonWearRead(2)) && f.Target(MixturePistonRead(mixture, 2)) == other, "Piston repair rebuilt the wrong sources.");
                    Require(ReferenceEquals(Get(f.PistonInstalledRead(2), "goLastFrame"), f.Target(f.PistonWearRead(2))), "Native installation cache retained destroyed Data."); saved();
                });
                for (int i = 1; i <= 4; i++)
                {
                    int slot = i;
                    check("pistons: host capture and replay preserve slot " + slot + " condition and original scalar order", () =>
                    {
                        var v = f.Pistons[slot - 1]; Property(f.Session, "IsHost", true); Set(v.Binding, "Replica", false); var wear = v.Part.FsmVariables.FindFsmFloat("Wear"); float original = wear.Value;
                        try
                        {
                            wear.Value = 40 + slot; var captured = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", v.PartId);
                            Require(captured != null && captured.Installed && captured.AssemblyId == slot && captured.NativeId == v.NativeId && captured.Scalars.Length == 2 && captured.Scalars[0] == 40 + slot && captured.Scalars[1] == 16, "Host piston capture lost its slot or condition.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(captured!)); wear.Value = 30; var next = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", v.PartId);
                            Require(next != null && next.Revision != captured!.Revision, "Host piston wear did not advance revision."); Call(f.Sync, "ApplyReplacementScalars", v.Binding, decoded); Require(wear.Value == 40 + slot, "Piston replay lost condition."); saved();
                        }
                        finally { wear.Value = original; Property(f.Session, "IsHost", false); Set(v.Binding, "Replica", true); }
                    });
                }
                check("pistons: disconnect clears both consumers and cleanup restores all saved reader targets", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Piston disconnect failed."); for (int slot = 1; slot <= 4; slot++) AssertPistonInput(f, mixture, slot, false, 0);
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int slot = 1; slot <= 4; slot++)
                    {
                        Require(ReferenceEquals(Get(f.PistonWearRead(slot), "gameObject"), owners[slot - 1, 0]) && ReferenceEquals(Get(f.PistonInstalledRead(slot), "gameObject"), owners[slot - 1, 1]) && ReferenceEquals(Get(MixturePistonRead(mixture, slot), "gameObject"), owners[slot - 1, 2]), "Piston cleanup lost native owner wrappers.");
                        AssertPistonInput(f, mixture, slot, true, 77);
                    }
                    saved();
                });
            }
        }
    }
}
