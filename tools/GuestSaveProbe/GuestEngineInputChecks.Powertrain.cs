using System;
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
            internal Variant AddPowertrainPart()
            {
                var variant = AddVariant(_familyPrefix, Mount, _familyPrefix + "8"); NativeBagPartChecks.Start(variant.Part); return variant;
            }
        }

        internal static void RunPowertrain(Action<string, Action> check)
        {
            foreach (string spec in new[] { "VIN102:Cylinders", "VIN102:Oil", "VIN102:Wearing", "VIN105:Cylinders", "VIN109:Cylinders", "VIN110:Cylinders", "VIN110:FuelLine" })
            {
                var pair = spec.Split(':'); string prefix = pair[0], consumer = pair[1];
                bool wearInput = prefix == "VIN102" || consumer == "FuelLine";
                using (var f = new Fixture(prefix, consumer))
                {
                    string label = "powertrain " + spec + ": ";
                    check(label + "native reads warm the saved mount caches", () =>
                    {
                        f.ReadAll(); Require(f.Installed && (!wearInput || f.Wear == 77), "Native saved powertrain baseline failed.");
                        foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Mount.gameObject), "Saved native cache did not warm."); f.AssertSaved();
                    });
                    check(label + "absent and pending parts wait for matching applied host state", () =>
                    {
                        Require(f.Prepare(), "Powertrain binding failed."); f.ReadAll(); AssertPowertrainAbsent(f, wearInput);
                        f.Receive(80, 0, 0, false); f.Prepare(); f.ReadAll(); AssertPowertrainAbsent(f, wearInput);
                        f.MarkApplied(); Require(f.Prepare(), "Applied powertrain input failed."); f.ReadAll();
                        Require(f.Installed && (!wearInput || f.Wear == 80), "Applied powertrain state was lost.");
                        Require(f.Proxy != f.Mount.gameObject && !f.ProxyData.enabled && f.Proxy.GetComponent<Rigidbody>() == null, "Powertrain proxy is not inert."); f.AssertSaved();
                    });
                    check(label + "host update preserves native scratch until the next read", () =>
                    {
                        var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                        f.Receive(81, 0, 0, true); Require(f.Prepare(), "Powertrain update failed.");
                        Require(f.Proxy == proxy && f.Reader.ActiveStateName == active && (!wearInput || f.Wear == 80), "Projection replayed native calculations.");
                        f.ReadAll(); Require(f.Installed && (!wearInput || f.Wear == 81), "Updated native input was lost."); f.AssertSaved();
                    });
                    if (consumer == "Cylinders") check(label + "native installation gate follows host removal and refitting", () =>
                    {
                        f.ReceiveVariant(f.Auxiliary("VIN115").Variants[0], 90, 1, 2, true);
                        foreach (bool fitted in new[] { true, false, true })
                        {
                            f.Receive(81, 0, 0, true, fitted); Require(f.Prepare(), "Powertrain attachment update failed."); f.Fire("Powertrain");
                            Require(f.Reader.ActiveStateName == (fitted ? "Flywheel" : "Not Ok"), "Native powertrain installation gate diverged.");
                            if (!fitted) Require(f.Starter.FsmVariables.FindFsmBool("ShutOff").Value, "Absent component did not stop native starting."); f.AssertSaved();
                        }
                    });
                    RunPowertrainNativeDecisions(f, prefix, consumer, label, check);
                    check(label + "competing pending host copies block input until one attachment remains", () =>
                    {
                        f.Receive(81, 0, 0, true); var other = f.AddPowertrainPart(); f.ReceiveVariant(other, 82, 0, 0, false);
                        f.Prepare(); f.ReadAll(); AssertPowertrainAbsent(f, wearInput);
                        f.ReceiveVariant(other, 82, 0, 0, false, false); Require(f.Prepare(), "Conflicting attachment removal failed.");
                        f.ReadAll(); Require(f.Installed && (!wearInput || f.Wear == 81), "Unique applied attachment did not recover."); f.AssertSaved();
                    });
                    foreach (string reason in new[] { "pending", "identity", "parent" })
                    {
                        string change = reason;
                        check(label + change + " cannot expose saved or unapplied part data", () =>
                        {
                            f.Receive(81, 0, 0, true); string id = f.Part.FsmVariables.FindFsmString("ID").Value;
                            if (change == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                            if (change == "identity") f.Part.FsmVariables.FindFsmString("ID").Value = prefix + "99";
                            if (change == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                            try { Require(f.Prepare(), "Unavailable powertrain preparation failed."); f.ReadAll(); AssertPowertrainAbsent(f, wearInput); f.AssertSaved(); }
                            finally { f.Part.FsmVariables.FindFsmString("ID").Value = id; f.Part.transform.SetParent(f.Mount.transform, false); f.MarkApplied(); f.Prepare(); }
                        });
                    }
                    if (consumer == "Cylinders") check(label + "host capture preserves actual wear and original scalar order", () =>
                    {
                        Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false); float saved = f.Part.FsmVariables.FindFsmFloat("Wear").Value;
                        try
                        {
                            f.Part.FsmVariables.FindFsmFloat("Wear").Value = 52; f.Part.FsmVariables.FindFsmFloat("Tightness").Value = 0;
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                            Require(state != null && state.Scalars.Length == 2 && state.Scalars[0] == 52 && state.Scalars[1] == 0, "Host powertrain capture changed scalar order or condition.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!)); f.Part.FsmVariables.FindFsmFloat("Wear").Value = 51;
                            var next = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                            Require(next != null && next.Revision != state!.Revision, "Host wear change did not advance revision.");
                            Call(f.Sync, "ApplyReplacementScalars", f.Binding, decoded); Require(f.Part.FsmVariables.FindFsmFloat("Wear").Value == 52, "Replica lost decoded host condition."); f.AssertSaved();
                        }
                        finally { f.Part.FsmVariables.FindFsmFloat("Wear").Value = saved; Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); }
                    });
                    check(label + "changed native reader pauses and repairs its consumer", () =>
                    {
                        var reader = f.Readers[0]; var name = Get(reader, "variableName"); Set(reader, "variableName", new FsmString { Value = "Bolted" });
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Changed powertrain input escaped validation."); f.AssertSaved(); }
                        finally { Set(reader, "variableName", name); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired powertrain consumer stayed paused."); f.ReadAll(); Require(f.Installed, "Repaired input did not recover.");
                    });
                    check(label + "destroyed proxy replaces every warmed native cache", () =>
                    {
                        var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Powertrain cache repair failed."); f.ReadAll();
                        Require(f.Proxy != proxy && f.Installed && (!wearInput || f.Wear == 81), "Destroyed powertrain proxy remained cached.");
                        foreach (var reader in f.Readers) Require(ReferenceEquals(Get(reader, "goLastFrame"), f.Proxy), "Native cache retained old Data."); f.AssertSaved();
                    });
                    check(label + "disconnect clears host inputs and cleanup restores saved caches", () =>
                    {
                        Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Powertrain disconnect failed."); f.ReadAll(); AssertPowertrainAbsent(f, wearInput);
                        Call(f.Sync, "RestoreGuestEngineInputs");
                        for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Cleanup lost native owner wrappers.");
                        f.ReadAll(); Require(f.Installed && (!wearInput || f.Wear == 77), "Cleanup did not restore saved cache."); f.AssertSaved();
                    });
                }
            }
        }

        private static void RunPowertrainNativeDecisions(Fixture f, string prefix, string consumer, string label, Action<string, Action> check)
        {
            if (prefix == "VIN102" && consumer != "Wearing" || consumer == "FuelLine")
            {
                float threshold = consumer == "Oil" ? 5 : consumer == "FuelLine" ? 2 : 1;
                if (consumer == "FuelLine") f.ReceiveVariant(f.Auxiliary("VIN125").Variants[0], 90, .7f, 145, true);
                foreach (float condition in new[] { threshold - .001f, threshold, threshold + .001f })
                {
                    float wear = condition;
                    check(label + "wear " + wear + " follows the native failure threshold", () =>
                    {
                        f.Receive(wear, 0, 0, true); Require(f.Prepare(), "Powertrain wear update failed.");
                        if (consumer == "FuelLine") { f.Action("State 2", 1).OnEnter(); f.Reader.FsmVariables.FindFsmFloat("Power").Value = 0; }
                        f.Fire(consumer == "Cylinders" ? "Crank" : consumer == "Oil" ? "Crank wear" : "Fuel Usage");
                        string expected = consumer == "Cylinders" ? (wear < 1 ? "Break 1" : "Flywheel")
                            : consumer == "Oil" ? (wear <= 5 ? "Shake" : "Major damage?") : wear < 2 ? "Low fuel" : "Check speed";
                        Require(f.Reader.ActiveStateName == expected, "Native condition branch diverged: " + f.Reader.ActiveStateName + " expected " + expected); f.AssertSaved();
                    });
                }
            }
            if (consumer == "Wearing")
            {
                for (int i = 1; i <= 5; i++) f.ReceiveBearing(i, 10);
                foreach (float condition in new[] { 20f, 45f, 80f })
                {
                    float wear = condition;
                    check(label + "wear " + wear + " contributes to native pressure alongside controlled bearings", () =>
                    {
                        f.Receive(wear, 0, 0, true); Require(f.Prepare(), "Crank pressure input failed.");
                        ((FsmFloat)Get(f.Action("Pressure leak", 14), "float1")).Value = 800;
                        f.Fire("Pressure leak"); float expected = Math.Min(1, (wear + 50) / 100);
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("OilPressureCondition").Value, expected), "Native pressure accumulation/clamp diverged.");
                        Require(f.Wear == 10 && f.Prepare() && f.Wear == 10, "Crank projection overwrote the final bearing scratch value.");
                        f.ReadAll(); Require(f.Wear == wear, "Crank read did not regain its scratch value."); f.AssertSaved();
                    });
                }
            }
        }

        private static void AssertPowertrainAbsent(Fixture f, bool wear) => Require(!f.Installed && (!wear || f.Wear == 0), "Unavailable powertrain part exposed saved or unapplied inputs.");
    }
}
