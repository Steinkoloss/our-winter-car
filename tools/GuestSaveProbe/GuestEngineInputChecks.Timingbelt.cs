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
            internal Variant AddTimingBelt()
            {
                var variant = AddVariant("VIN107", Mount, "VIN1078"); NativeBagPartChecks.Start(variant.Part); return variant;
            }
        }

        internal static void RunTimingbelt(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN107"))
            {
                const string label = "timing belt combustion: "; var cam = f.Auxiliary("VIN115").Variants[0];
                check(label + "native readers warm saved installation and wear caches", () =>
                {
                    f.ReadAll(); Require(f.Installed && f.Wear == 77, "Saved timing-belt baseline failed.");
                    foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Mount.gameObject), "Native belt cache was not warmed."); f.AssertSaved();
                });
                check(label + "absent host belt supplies neutral bool and wear without saved writes", () =>
                {
                    Require(f.Prepare(), "Timing-belt input preparation failed."); f.ReadAll(); AssertTimingBeltAbsent(f);
                    Require(f.Proxy != f.Mount.gameObject && !f.ProxyData.enabled && f.Proxy.GetComponent<Rigidbody>() == null
                        && f.ProxyData.FsmVariables.BoolVariables.Length == 1 && f.ProxyData.FsmVariables.FloatVariables.Length == 1, "Timing-belt inputs are not inert."); f.AssertSaved();
                });
                check(label + "pending fitted state waits for the matching applied revision", () =>
                {
                    f.Receive(90, 0, 0, false); f.Prepare(); f.ReadAll(); AssertTimingBeltAbsent(f);
                    f.MarkApplied(); Require(f.Prepare(), "Applied timing belt failed preparation."); f.ReadAll();
                    Require(f.Installed && f.Wear == 90, "Applied timing-belt inputs were lost."); f.AssertSaved();
                });
                check(label + "updated host wear waits for normal reads without replaying calculation scratch", () =>
                {
                    var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                    f.Receive(80, 0, 0, true); Require(f.Prepare(), "Timing belt update failed.");
                    Require(f.Wear == 90 && f.Proxy == proxy && f.Reader.ActiveStateName == active, "Projection replayed native timing calculations.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 80, "Native reader missed updated host belt wear."); f.AssertSaved();
                });
                check(label + "native powertrain gate follows removal and refit with an applied host cam", () =>
                {
                    f.ReceiveVariant(cam, 90, 1, 2, true);
                    foreach (bool installed in new[] { true, false, true })
                    {
                        f.Receive(80, 0, 0, true, installed); Require(f.Prepare(), "Belt installation update failed."); f.Fire("Powertrain");
                        Require(f.Reader.ActiveStateName == (installed ? "Crank" : "Not Ok"), "Timing belt bypassed the native installation gate.");
                        if (!installed) Require(f.Starter.FsmVariables.FindFsmBool("ShutOff").Value, "Missing belt did not stop native starting."); f.AssertSaved();
                    }
                });
                foreach (float wear in new[] { .999f, 1f, 1.001f })
                foreach (float tolerance in new[] { .999f, 1f, 1.001f })
                {
                    float condition = wear, camTolerance = tolerance;
                    check(label + "wear " + condition + " and cam tolerance " + camTolerance + " follow native failure boundaries", () =>
                    {
                        f.Receive(condition, 0, 0, true); f.ReceiveVariant(cam, 90, 1, camTolerance, true); Require(f.Prepare(), "Belt/cam update failed.");
                        f.Fire("Timing belt"); string expected = condition < 1 ? (camTolerance <= 1 ? "State 1" : "Not Ok") : "Crank";
                        Require(f.Reader.ActiveStateName == expected, "Native broken-belt/cam branch diverged: " + f.Reader.ActiveStateName + " expected " + expected); f.AssertSaved();
                    });
                }
                check(label + "native wear arithmetic leaves saved belt and distributor writers disabled", () =>
                {
                    f.Receive(80, 0, 0, true); f.Prepare();
                    var rpm = (FsmFloat)Get(f.Action("Timing belt wear", 0), "float1"); rpm.Value = 4350;
                    // Exported globals have separate placeholders in the fixture;
                    // both native calculations consume the same RPM in the game.
                    Set(f.Action("Timing belt wear", 2), "float1", rpm);
                    f.Action("Timing belt wear", 0).OnEnter(); Require(Near(f.Wear, 4350f / 2310000), "Native timing-belt wear rate changed.");
                    f.Fire("Timing belt wear"); Require(Near(f.Wear, .001f), "Native belt/distributor wear arithmetic changed.");
                    foreach (int index in new[] { 1, 3 }) Require(!f.Action("Timing belt wear", index).Enabled, "Native guest wear writer was enabled.");
                    Require(f.Target(f.Action("Timing belt wear", 1)) == f.Mount.gameObject, "Saved wear writer was redirected into the input proxy.");
                    Require(f.Prepare() && Near(f.Wear, .001f), "Input preparation overwrote wear calculation scratch."); f.AssertSaved();
                });
                check(label + "competing fitted host belts stay absent until a unique attachment remains", () =>
                {
                    var other = f.AddTimingBelt();
                    f.ReceiveVariant(other, 81, 0, 0, false); f.Prepare(); f.ReadAll(); AssertTimingBeltAbsent(f);
                    f.ReceiveVariant(other, 81, 0, 0, false, false); Require(f.Prepare(), "Conflicting belt removal failed.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 80, "Unique timing belt did not recover."); f.AssertSaved();
                });
                foreach (string unavailable in new[] { "pending", "revision", "identity", "parent", "hidden" })
                {
                    string reason = unavailable;
                    check(label + reason + " cannot expose unapplied belt condition", () =>
                    {
                        f.Receive(80, 0, 0, true); string id = f.Part.FsmVariables.FindFsmString("ID").Value;
                        if (reason == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                        if (reason == "revision") Set(f.Binding, "AppliedRevision", (uint)Get(f.Binding, "AppliedRevision") - 1);
                        if (reason == "identity") f.Part.FsmVariables.FindFsmString("ID").Value = "VIN10799";
                        if (reason == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                        if (reason == "hidden") Set(f.Binding, "FittedPresentation", false);
                        try { Require(f.Prepare(), "Unavailable timing-belt preparation failed."); f.ReadAll(); AssertTimingBeltAbsent(f); f.AssertSaved(); }
                        finally { f.Part.FsmVariables.FindFsmString("ID").Value = id; f.Part.transform.SetParent(f.Mount.transform, false); f.MarkApplied(); f.Prepare(); }
                    });
                }
                check(label + "host publishes actual belt wear with unchanged scalar order and revision tracking", () =>
                {
                    Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false);
                    float wear = f.Part.FsmVariables.FindFsmFloat("Wear").Value;
                    try
                    {
                        f.Part.FsmVariables.FindFsmFloat("Wear").Value = 42.5f; f.Part.FsmVariables.FindFsmFloat("Tightness").Value = 0;
                        var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                        Require(state != null && state.Scalars.Length == 2 && state.Scalars[0] == 42.5f && state.Scalars[1] == 0, "Host substituted timing-belt wear or changed scalar order.");
                        var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!));
                        f.Part.FsmVariables.FindFsmFloat("Wear").Value = 42;
                        var next = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                        Require(next != null && next.Revision != state!.Revision, "Changed timing-belt wear did not advance gameplay revision.");
                        Call(f.Sync, "ApplyReplacementScalars", f.Binding, decoded);
                        Require(f.Part.FsmVariables.FindFsmFloat("Wear").Value == 42.5f, "Decoded belt condition did not apply to the replica."); f.AssertSaved();
                    }
                    finally { f.Part.FsmVariables.FindFsmFloat("Wear").Value = wear; Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); }
                });
                check(label + "changed native reader signature pauses and repairs its consumer", () =>
                {
                    var reader = f.Readers[1]; var field = Get(reader, "variableName"); Set(reader, "variableName", new FsmString { Value = "Durability" });
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Changed timing-belt reader escaped validation."); f.AssertSaved(); }
                    finally { Set(reader, "variableName", field); f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired timing-belt reader stayed paused."); f.ReadAll(); Require(f.Installed && f.Wear == 80, "Repaired reader did not recover host condition.");
                });
                check(label + "destroyed proxy rebuilds both warmed native target caches", () =>
                {
                    var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Timing-belt cache repair failed."); f.ReadAll();
                    Require(f.Proxy != proxy && f.Installed && f.Wear == 80, "Destroyed timing-belt proxy remained cached.");
                    foreach (var reader in f.Readers) Require(ReferenceEquals(Get(reader, "goLastFrame"), f.Proxy), "Native target cache was not rebuilt."); f.AssertSaved();
                });
                check(label + "disconnect clears inputs and cleanup restores saved targets and caches", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Timing-belt disconnect failed."); f.ReadAll(); AssertTimingBeltAbsent(f);
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Timing-belt cleanup lost original owner wrappers.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 77, "Timing-belt cleanup retained proxy caches."); f.AssertSaved();
                });
            }
        }

        private static void AssertTimingBeltAbsent(Fixture f) => Require(!f.Installed && f.Wear == 0, "Unavailable timing belt exposed saved or unapplied state.");
    }
}
