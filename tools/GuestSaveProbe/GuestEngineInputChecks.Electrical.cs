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
        private static bool ElectricalNativeAction(string state, int index) =>
            (state == "Check alternator" && index >= 1) || state == "Alternator damage" || state == "Run on alternator" || state == "State 2"
            || (state == "Alternator eff" && index < 6) || state == "Wear"
            || (state == "Charge battery" && index >= 1 && index <= 6)
            || (state == "Engine off" && index == 1) || (state == "No charge" && index == 0);

        internal static void RunAlternatorElectrical(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN133", "Electrics"))
            {
                const string label = "alternator electrical inputs: "; var race = f.Alternative!;
                check(label + "seven native readers warm saved bool and float caches", () =>
                {
                    f.ReadAll(); AssertElectrical(f, 77, 2, 3, true, true);
                    Require(f.Part.FsmVariables.FindFsmBool("Damaged") == null && race.Part.FsmVariables.FindFsmBool("Damaged") == null, "Fixture invented a loose-part damage flag.");
                    foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Mount.gameObject), "Electrical cache was not warmed.");
                });
                check(label + "missing host part is absent and damaged at both native checkpoints", () =>
                {
                    Require(f.Prepare(), "Electrical preparation failed."); f.ReadAll(); AssertElectrical(f, 0, 0, 0, false, true);
                    Require(f.ProxyData.FsmVariables.BoolVariables.Length == 2 && f.ProxyData.FsmVariables.FloatVariables.Length == 3, "Repeated readers duplicated proxy fields.");
                    foreach (var action in f.Readers) Require(f.Target(action) == f.Proxy, "Repeated electrical reader retained saved target.");
                    f.Fire("Alternator damage"); Require(f.Reader.ActiveStateName == "No charge", "Missing alternator remained chargeable.");
                    f.Fire("Run on alternator"); Require(f.Reader.ActiveStateName == "Engine off" && f.Starter.FsmVariables.FindFsmBool("ShutOff").Value, "Missing alternator kept running through second damage read."); AssertElectricalSaved(f);
                });
                check(label + "pending state becomes available only after application", () =>
                {
                    f.Receive(90, 16, 7, false, efficiency: 3.4f, durability: .7f, electricalEfficiency: 350);
                    Require(f.Prepare(), "Pending electrical state failed."); f.ReadAll(); AssertElectrical(f, 0, 0, 0, false, true);
                    f.MarkApplied(); Require(f.Prepare(), "Applied electrical state failed."); f.ReadAll(); AssertElectrical(f, 90, .7f, 350, true, false); AssertElectricalSaved(f);
                });
                check(label + "updates preserve scratch until normal repeated reads", () =>
                {
                    var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                    f.Receive(91, 16, 7, true, efficiency: 3.4f, durability: .6f, electricalEfficiency: 320, damaged: true);
                    Require(f.Prepare(), "Electrical update failed."); AssertElectrical(f, 90, .7f, 350, true, false);
                    Require(proxy == f.Proxy && active == f.Reader.ActiveStateName, "Electrical update replayed native graph.");
                    f.ReadAll(); AssertElectrical(f, 91, .6f, 320, true, true); AssertElectricalSaved(f);
                });
                foreach (bool upgraded in new[] { false, true })
                {
                    if (upgraded) { f.Receive(91, 0, 7, false, false); f.Prepare(); }
                    foreach (bool damaged in new[] { false, true, false })
                    {
                        bool value = damaged;
                        check(label + (upgraded ? "upgraded" : "stock") + " actual damage " + value + " controls charge and continued running", () =>
                        {
                            if (upgraded) f.ReceiveVariant(race, 90, .9f, 3, true, electricalEfficiency: 293, damaged: value);
                            else f.Receive(90, 16, 7, true, efficiency: 3.4f, damaged: value);
                            Require(f.Prepare(), "Damage/repair update failed."); f.ReadAll();
                            AssertElectrical(f, 90, upgraded ? .9f : .7f, upgraded ? 293 : 350, true, value);
                            f.Fire("Alternator damage"); Require(f.Reader.ActiveStateName == (value ? "No charge" : "Engine running?"), "Native charging damage branch ignored host flag.");
                            f.Fire("Run on alternator"); Require(f.Reader.ActiveStateName == (value ? "Engine off" : "Delay"), "Repeated damage read diverged.");
                            Require(Near(f.Reader.FsmVariables.FindFsmFloat("Math1").Value, .0025f * (upgraded ? .9f : .7f)), "Native running wear calculation missed durability."); AssertElectricalSaved(f);
                        });
                    }
                }
                check(label + "native wear efficiency voltage and charging arithmetic use accepted values", () =>
                {
                    f.ReadAll(); f.Fire("Alternator eff");
                    Require(Near(f.Reader.FsmVariables.FindFsmFloat("AlternatorEfficiency").Value, 3850)
                        && Near(f.Reader.FsmVariables.FindFsmFloat("AlternatorVolts").Value, 13.65f), "Native efficiency/voltage arithmetic diverged.");
                    ((FsmFloat)Get(f.Action("Charge battery", 1), "float1")).Value = 1000;
                    ((FsmFloat)Get(f.Action("Charge battery", 2), "float1")).Value = 80;
                    f.Fire("Charge battery"); Require(Near(f.Reader.FsmVariables.FindFsmFloat("Charging").Value, 131f / 570f), "Native charging cap diverged.");
                    f.Fire("Wear"); Require(Near(f.Reader.FsmVariables.FindFsmFloat("Math1").Value, .0021f * .9f), "Native wear calculation ignored upgraded durability."); AssertElectricalSaved(f);
                });
                check(label + "native installation checks retain controlled belt wiring and battery gates", () =>
                {
                    f.Reader.FsmVariables.FindFsmBool("Battery").Value = true; f.Fire("Check alternator");
                    Require(f.Reader.ActiveStateName == "Engine running?", "Installed healthy alternator did not pass native prerequisite gate.");
                    f.Reader.FsmVariables.FindFsmBool("Battery").Value = false; f.Fire("Check alternator");
                    Require(f.Reader.ActiveStateName == "No charge", "Alternator projection bypassed native battery prerequisite.");
                    f.Reader.FsmVariables.FindFsmBool("Battery").Value = true;
                    f.ReceiveVariant(race, 90, .9f, 3, false, false); f.Prepare(); f.Fire("State 2");
                    Require(f.Reader.ActiveStateName == "Engine off", "Second native Installed reader kept absent alternator running.");
                    f.ReceiveVariant(race, 90, .9f, 3, true); f.Prepare(); AssertElectricalSaved(f);
                });
                check(label + "pending competing variant blocks all electrical inputs until removal", () =>
                {
                    f.Receive(90, 16, 7, false); f.Prepare(); f.ReadAll(); AssertElectrical(f, 0, 0, 0, false, true);
                    f.Receive(90, 0, 7, false, false); f.Prepare(); f.ReadAll(); AssertElectrical(f, 90, .9f, 293, true, false); AssertElectricalSaved(f);
                });
                foreach (string reason in new[] { "pending", "revision", "identity", "parent" })
                {
                    string change = reason;
                    check(label + change + " cannot expose unapplied healthy state", () =>
                    {
                        f.ReceiveVariant(race, 90, .9f, 3, true); string id = race.Part.FsmVariables.FindFsmString("ID").Value;
                        if (change == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(race.PartId);
                        if (change == "revision") Set(race.Binding, "AppliedRevision", race.Revision - 1);
                        if (change == "identity") race.Part.FsmVariables.FindFsmString("ID").Value = "ALTERNATOR099";
                        if (change == "parent") race.Part.transform.SetParent(f.Extras.transform, false);
                        try { Require(f.Prepare(), "Unavailable electrical source failed neutral preparation."); f.ReadAll(); AssertElectrical(f, 0, 0, 0, false, true); }
                        finally
                        {
                            race.Part.FsmVariables.FindFsmString("ID").Value = id; race.Part.transform.SetParent(f.Mount.transform, false);
                            f.ReceiveVariant(race, 90, .9f, 3, true); f.Prepare();
                        }
                        AssertElectricalSaved(f);
                    });
                }
                check(label + "second damage reader signature failure pauses only Electrics", () =>
                {
                    var oil = f.AddConsumer("Oil");
                    try
                    {
                        Require(f.Prepare(), "Companion Oil binding failed."); var action = f.Readers[5]; var output = Get(action, "storeValue");
                        Set(action, "storeValue", new FsmBool { Name = "Damaged", UseVariable = true });
                        try { Require(!f.Prepare() && !f.Reader.enabled && oil.enabled, "Repeated bool read escaped scoped validation."); }
                        finally { Set(action, "storeValue", output); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired repeated bool reader stayed paused."); AssertElectricalSaved(f);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(oil); f.Prepare(); }
                });
                check(label + "missing saved mount bool defers reads and recovers without changing the save", () =>
                {
                    var bools = f.Mount.FsmVariables.BoolVariables; var keep = new List<FsmBool>();
                    foreach (var value in bools) if (value.Name != "Damaged") keep.Add(value);
                    f.Mount.FsmVariables.BoolVariables = keep.ToArray();
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Missing native mount bool escaped validation."); }
                    finally { f.Mount.FsmVariables.BoolVariables = bools; f.Prepare(); }
                    Require(f.Reader.enabled, "Restored native bool did not recover."); AssertElectricalSaved(f);
                });
                check(label + "whole-proxy rebuild repairs both warmed damage caches", () =>
                {
                    f.ReadAll(); var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Electrical proxy rebuild failed."); f.ReadAll();
                    Require(f.Proxy != proxy, "Destroyed proxy was reused."); AssertElectrical(f, 90, .9f, 293, true, false);
                    foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Proxy), "Repeated reader retained old cache."); AssertElectricalSaved(f);
                });
                RunAlternatorCaptureChecks(f, check);
                check(label + "disconnect neutralizes repeated reads and cleanup restores all saved targets", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Electrical disconnect failed."); f.ReadAll(); AssertElectrical(f, 0, 0, 0, false, true);
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Electrical cleanup lost original owner.");
                    f.ReadAll(); AssertElectrical(f, 77, 2, 3, true, true); AssertElectricalSaved(f);
                });
            }
        }

        private static void AssertElectrical(Fixture f, float wear, float durability, float efficiency, bool installed, bool damaged)
        {
            Require(f.Wear == wear && Near(f.Durability, durability) && Near(f.Reader.FsmVariables.FindFsmFloat("Efficiency").Value, efficiency)
                && f.Reader.FsmVariables.FindFsmBool("Installed1").Value == installed && f.Reader.FsmVariables.FindFsmBool("Installed2").Value == installed
                && f.Reader.FsmVariables.FindFsmBool("Damaged").Value == damaged, "Electrical inputs differ from accepted host state.");
        }
        private static void AssertElectricalSaved(Fixture f)
        {
            f.AssertSaved(); Require(f.Mount.FsmVariables.FindFsmBool("Damaged").Value, "Electrical projection changed saved damage.");
            foreach (string state in new[] { "Wear", "Run on alternator" })
                Require(!f.Action(state, 1).Enabled && f.Target(f.Action(state, 1)) == f.Mount.gameObject, "Saved alternator wear writer changed target or became enabled.");
        }
    }
}
