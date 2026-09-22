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
            internal Variant AddFluidPart()
            {
                var variant = AddVariant(_familyPrefix, Mount, _familyPrefix + "8"); NativeBagPartChecks.Start(variant.Part); return variant;
            }
            internal void AssertFluidReads(bool installed, float condition, float tightness)
            {
                Require(Installed == installed, "Fluid input attachment gate diverged.");
                foreach (var action in Readers)
                {
                    string field = ((FsmString)Get(action, "variableName")).Value;
                    var output = Get(action, "storeValue");
                    if (field == "Installed") Require(((FsmBool)output).Value == installed, "Native installation read diverged.");
                    else Require(Near(((FsmFloat)output).Value, field == "Tightness" ? tightness : condition), "Native " + field + " read diverged.");
                }
            }
        }

        internal static void RunFluids(Action<string, Action> check)
        {
            foreach (string spec in new[] { "VIN134:Cylinders", "VIN134:Oil", "VIN129:Cooling", "VIN128:Cooling", "OILFILTR0:Oil" })
            {
                var pair = spec.Split(':'); string prefix = pair[0], consumer = pair[1];
                using (var f = new Fixture(prefix, consumer))
                {
                    string label = "fluid " + spec + ": ";
                    check(label + "native reads warm saved mount caches", () =>
                    {
                        f.ReadAll(); f.AssertFluidReads(true, prefix == "OILFILTR0" ? 6 : 77, 8);
                        foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Mount.gameObject), "Saved native cache did not warm."); f.AssertSaved();
                    });
                    check(label + "absent and pending parts wait for matching applied host state", () =>
                    {
                        Require(f.Prepare(), "Fluid binding failed."); f.ReadAll(); f.AssertFluidReads(false, 0, 0);
                        f.Receive(80, 8, 0, false); f.Prepare(); f.ReadAll(); f.AssertFluidReads(false, 0, 0);
                        f.MarkApplied(); Require(f.Prepare(), "Applied fluid input failed."); f.ReadAll(); f.AssertFluidReads(true, 80, 8);
                        Require(f.Proxy != f.Mount.gameObject && !f.ProxyData.enabled && f.Proxy.GetComponent<Rigidbody>() == null, "Fluid proxy is not inert."); f.AssertSaved();
                    });
                    check(label + "host update waits for the next native read without overwriting scratch", () =>
                    {
                        var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                        f.Receive(81, 7, 0, true); Require(f.Prepare(), "Fluid update failed.");
                        Require(f.Proxy == proxy && f.Reader.ActiveStateName == active, "Projection replayed native state."); f.AssertFluidReads(true, 80, 8);
                        f.ReadAll(); f.AssertFluidReads(true, 81, 7); f.AssertSaved();
                    });
                    RunFluidNativeDecisions(f, prefix, consumer, label, check);
                    check(label + "competing pending host copies block input until one attachment remains", () =>
                    {
                        f.Receive(81, 7, 0, true); var other = f.AddFluidPart(); f.ReceiveVariant(other, 82, 0, 0, false);
                        f.Prepare(); f.ReadAll(); f.AssertFluidReads(false, 0, 0);
                        f.ReceiveVariant(other, 82, 0, 0, false, false); Require(f.Prepare(), "Conflicting attachment removal failed.");
                        f.ReadAll(); f.AssertFluidReads(true, 81, 7); f.AssertSaved();
                    });
                    foreach (string reason in new[] { "pending", "revision", "identity", "parent", "hidden" })
                    {
                        string change = reason;
                        check(label + change + " cannot expose saved or unapplied part data", () =>
                        {
                            f.Receive(81, 7, 0, true); string id = f.Part.FsmVariables.FindFsmString("ID").Value;
                            if (change == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                            if (change == "revision") Set(f.Binding, "AppliedRevision", (uint)0);
                            if (change == "identity") f.Part.FsmVariables.FindFsmString("ID").Value = prefix + "99";
                            if (change == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                            if (change == "hidden") Set(f.Binding, "FittedPresentation", false);
                            try { Require(f.Prepare(), "Unavailable fluid preparation failed."); f.ReadAll(); f.AssertFluidReads(false, 0, 0); f.AssertSaved(); }
                            finally { f.Part.FsmVariables.FindFsmString("ID").Value = id; f.Part.transform.SetParent(f.Mount.transform, false); f.MarkApplied(); f.Prepare(); }
                        });
                    }
                    if (prefix != "VIN134" || consumer == "Cylinders") check(label + "host capture and replay preserve actual condition and scalar order", () =>
                    {
                        Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false);
                        var condition = f.Part.FsmVariables.FindFsmFloat(prefix == "OILFILTR0" ? "Dirt" : "Wear");
                        float saved = condition.Value, tightness = f.Part.FsmVariables.FindFsmFloat("Tightness").Value;
                        try
                        {
                            condition.Value = 52; f.Part.FsmVariables.FindFsmFloat("Tightness").Value = 7;
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                            Require(state != null && state.Scalars.Length == 2 && state.Scalars[0] == 52 && state.Scalars[1] == 7, "Host fluid capture lost condition or scalar order.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!)); condition.Value = 51;
                            var next = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                            Require(next != null && next.Revision != state!.Revision, "Host condition change did not advance revision.");
                            Call(f.Sync, "ApplyReplacementScalars", f.Binding, decoded); Require(condition.Value == 52, "Replica lost decoded condition."); f.AssertSaved();
                        }
                        finally { condition.Value = saved; f.Part.FsmVariables.FindFsmFloat("Tightness").Value = tightness; Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); }
                    });
                    check(label + "changed native reader pauses and repairs its consumer", () =>
                    {
                        var reader = f.Readers[0]; var name = Get(reader, "variableName"); Set(reader, "variableName", new FsmString { Value = "Bolted" });
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Changed fluid input escaped validation."); f.AssertSaved(); }
                        finally { Set(reader, "variableName", name); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired fluid consumer stayed paused."); f.ReadAll(); f.AssertFluidReads(true, 81, 7);
                    });
                    check(label + "destroyed proxy replaces every warmed native cache", () =>
                    {
                        var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Fluid cache repair failed."); f.ReadAll();
                        Require(f.Proxy != proxy, "Destroyed fluid proxy remained cached."); f.AssertFluidReads(true, 81, 7);
                        foreach (var reader in f.Readers) Require(ReferenceEquals(Get(reader, "goLastFrame"), f.Proxy), "Native cache retained old Data."); f.AssertSaved();
                    });
                    check(label + "disconnect clears host inputs and cleanup restores saved caches", () =>
                    {
                        Property(f.Session, "State", SessionState.Idle); Require(f.Prepare() == (f.Reader.FsmName != "Cooling") && (f.Reader.FsmName != "Cooling" || !f.Reader.enabled), "Disconnected Cooling must pause until cleanup."); f.ReadAll(); f.AssertFluidReads(false, 0, 0);
                        Call(f.Sync, "RestoreGuestEngineInputs");
                        for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Cleanup lost native owner wrappers.");
                        f.ReadAll(); f.AssertFluidReads(true, prefix == "OILFILTR0" ? 6 : 77, 8); f.AssertSaved();
                    });
                }
            }
        }

        private static void RunFluidNativeDecisions(Fixture f, string prefix, string consumer, string label, Action<string, Action> check)
        {
            if (prefix == "VIN134")
            {
                check(label + "native installation gate follows removal and refitting", () =>
                {
                    foreach (bool fitted in new[] { true, false, true })
                    {
                        f.Receive(81, 7, 0, true, fitted); Require(f.Prepare(), "Head gasket attachment update failed."); f.Fire(consumer == "Oil" ? "Headgasket" : "Head gasket");
                        string expected = consumer == "Oil" ? (fitted ? "Wearing" : "Oil leak ") : fitted ? "Gasket wear" : "Remove power";
                        Require(f.Reader.ActiveStateName == expected, "Native head gasket installation branch diverged: " + f.Reader.ActiveStateName); f.AssertSaved();
                    }
                });
                if (consumer == "Cylinders") foreach (float value in new[] { .999f, 1f, 1.001f })
                {
                    float condition = value;
                    check(label + "wear " + condition + " follows the native damage boundary", () =>
                    {
                        f.Receive(condition, 7, 0, true); Require(f.Prepare(), "Gasket wear update failed."); f.Fire("Gasket damage");
                        Require(f.Reader.ActiveStateName == (condition <= 1 ? "Break 3" : "Gasket wear"), "Native head gasket wear branch diverged."); f.AssertSaved();
                    });
                }
            }
            if (prefix == "VIN129")
            {
                foreach (float condition in new[] { 6.999f, 7f, 7.001f, 14.999f, 15f, 15.001f })
                    foreach (float degrees in new[] { 79.999f, 80f, 80.001f })
                    {
                        float wear = condition, temperature = degrees;
                        check(label + "wear " + wear + " at " + temperature + " degrees follows native thermostat decisions", () =>
                        {
                            f.Receive(wear, 7, 0, true); Require(f.Prepare(), "Thermostat update failed."); SetThermostatTemperature(f, temperature);
                            f.Fire("Thermostat"); bool closed = wear < 7 || (wear >= 15 && temperature <= 80);
                            Require(f.Reader.ActiveStateName == (closed ? "Closed" : "Water Pump 2"), "Native thermostat branch diverged: " + f.Reader.ActiveStateName);
                            if (closed) Require(!f.Reader.FsmVariables.FindFsmBool("ThermostatOpen").Value && Near(f.Reader.FsmVariables.FindFsmFloat("Circulation").Value, .0001f), "Native closed circulation diverged.");
                            f.ReadAll(); f.AssertFluidReads(true, wear, 7); f.AssertSaved();
                        });
                    }
                check(label + "absent thermostat uses the native open branch", () =>
                {
                    f.Receive(0, 7, 0, true, false); f.Prepare(); SetThermostatTemperature(f, 0); f.Fire("Thermostat");
                    Require(f.Reader.ActiveStateName == "Water Pump 2", "Absent thermostat did not open."); f.AssertSaved();
                });
            }
            if (prefix == "VIN128" || prefix == "OILFILTR0")
            {
                foreach (float value in prefix == "VIN128" ? new[] { 15.999f, 16f, 16.001f } : new[] { 0f, 4f, 8f })
                {
                    float tightness = value;
                    check(label + "tightness " + tightness + " feeds native leak checks", () =>
                    {
                        f.Receive(81, tightness, 0, true); Require(f.Prepare(), "Fluid tightness update failed."); AssertFluidTightnessDecision(f, prefix, tightness); f.AssertSaved();
                    });
                }
                check(label + "latest accepted tightening receipt precedes an older part snapshot", () =>
                {
                    f.Receive(81, 0, 0, true); Call(f.Bridge, "ObserveReplacementTightness", f.PartId, 7f); Require(f.Prepare(), "Tightness receipt failed.");
                    AssertFluidTightnessDecision(f, prefix, 7); f.ReadAll(); f.AssertFluidReads(true, 81, 7); f.AssertSaved();
                });
            }
            if (prefix == "OILFILTR0") foreach (float value in new[] { 99.999f, 100f, 100.001f })
            {
                float dirt = value;
                check(label + "dirt " + dirt + " follows the native contamination boundary", () =>
                {
                    f.Receive(dirt, 7, 0, true); Require(f.Prepare(), "Filter dirt update failed."); f.Reader.FsmVariables.FindFsmFloat("OilFilteringRate").Value = .25f;
                    f.Fire("Oil filter"); Require(f.Reader.ActiveStateName == (dirt > 100 ? "Add contamination" : "Oil contamination"), "Native oil filter dirt branch diverged.");
                    Require(f.Reader.FsmVariables.FindFsmFloat("OilFiltering").Value == .25f, "Native filtering rate changed."); f.AssertSaved();
                });
            }
        }

        private static void SetThermostatTemperature(Fixture f, float temperature)
        {
            ((FsmFloat)Get(f.Action("Thermostat", 2), "float1")).Value = temperature;
            ((FsmFloat)Get(f.Action("Thermostat", 8), "float1")).Value = temperature;
            f.Reader.FsmVariables.FindFsmFloat("ThermostatOpening").Value = 80;
        }

        private static void AssertFluidTightnessDecision(Fixture f, string prefix, float tightness)
        {
            f.Fire(prefix == "VIN128" ? "Housing tightness" : "Oilfilter leak");
            if (prefix == "VIN128") Require(f.Reader.ActiveStateName == (tightness < 16 ? "State 3" : "Pump tightness"), "Native housing leak branch diverged.");
            else
            {
                Require(Near(f.Reader.FsmVariables.FindFsmFloat("LeakOilfilter").Value, (8 - tightness) / 800), "Native oil filter leak arithmetic diverged.");
                Require(Near(f.Tightness, 8 - tightness) && f.Prepare() && Near(f.Tightness, 8 - tightness), "Projection overwrote native leak scratch.");
            }
        }
    }
}
