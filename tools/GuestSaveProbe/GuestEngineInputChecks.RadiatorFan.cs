using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        internal static void RunRadiatorFan(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "Valves", "Cooling" })
            using (var f = new Fixture("VIN137", consumer, "radiator-fan-engine-input-probe.json"))
            {
                string label = "radiator fan " + consumer + ": ";
                check(label + "saved target cache warms before projection", () =>
                {
                    f.ReadAll(); Require(f.Installed && ReferenceEquals(Get(f.Readers[0], "goLastFrame"), f.Mount.gameObject), "Saved fan cache did not warm."); f.AssertSaved();
                });
                check(label + "missing host fan uses an inert installation proxy", () =>
                {
                    Require(f.Prepare(), "Fan projection failed."); f.ReadAll(); Require(!f.Installed, "Saved fan leaked into input.");
                    Require(f.Proxy != f.Mount.gameObject && !f.ProxyData.enabled && f.Proxy.GetComponent<Rigidbody>() == null
                        && f.ProxyData.FsmVariables.BoolVariables.Length == 1 && f.ProxyData.FsmVariables.FloatVariables.Length == 0, "Fan proxy was not a read-only installation input."); f.AssertSaved();
                });
                check(label + "pending host receipt waits for application", () =>
                {
                    f.Receive(90, 16, 0, false); Require(f.Prepare(), "Pending fan preparation failed."); f.ReadAll(); Require(!f.Installed, "Unapplied fan became installed.");
                    f.MarkApplied(); Require(f.Prepare(), "Applied fan preparation failed."); f.ReadAll(); Require(f.Installed, "Applied fan stayed absent."); f.AssertSaved();
                });
                check(label + "an applied water-pump pulley cannot install or remove the separate fan", () =>
                {
                    var pulley = f.AddFanPulley();
                    f.Receive(90, 16, 0, true, false); f.ReceiveVariant(pulley, 90, 0, 0, true);
                    Require(f.Prepare(), "Independent pulley preparation failed."); f.ReadAll();
                    Require(!f.Installed, "An installed pulley was mistaken for the radiator fan.");
                    f.Receive(90, 16, 0, true); f.ReceiveVariant(pulley, 90, 0, 0, true, false);
                    Require(f.Prepare(), "Separate fan preparation failed."); f.ReadAll();
                    Require(f.Installed, "Removing the pulley removed the fan's installation input."); f.AssertSaved();
                });
                foreach (bool belt in new[] { false, true })
                foreach (bool fan in new[] { false, true })
                {
                    bool hasBelt = belt, hasFan = fan;
                    check(label + "native arithmetic with belt " + hasBelt + " and fan " + hasFan, () =>
                    {
                        f.ReceiveVariant(f.Auxiliary("FANBELT0").Variants[0], 90, 0, 0, true, hasBelt);
                        f.Receive(90, 16, 0, true, hasFan); Require(f.Prepare(), "Fan/belt preparation failed.");
                        CheckNativeFanDecision(f, f.Reader, hasFan, hasBelt); f.AssertSaved();
                    });
                }
                foreach (string reason in new[] { "pending", "revision", "identity", "parent", "inactive", "ownership" })
                {
                    string change = reason;
                    check(label + change + " cannot supply an installed fan", () =>
                    {
                        var id = f.Part.FsmVariables.FindFsmString("ID"); string original = id.Value;
                        if (change == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                        if (change == "revision") Set(f.Binding, "AppliedRevision", (uint)0);
                        if (change == "identity") id.Value = "VIN13799";
                        if (change == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                        if (change == "inactive") f.Part.gameObject.SetActive(false);
                        if (change == "ownership") Set(f.Binding, "Replica", false);
                        try { Require(f.Prepare(), "Unavailable fan preparation failed."); f.ReadAll(); Require(!f.Installed, "Unavailable fan leaked through " + change); f.AssertSaved(); }
                        finally
                        {
                            id.Value = original; f.Part.transform.SetParent(f.Mount.transform, false); f.Part.gameObject.SetActive(true);
                            Set(f.Binding, "Replica", true); f.MarkApplied(); f.Prepare();
                        }
                    });
                }
                check(label + "nested mount remains bound when the engine block moves", () =>
                {
                    var parent = Child(f.Extras, "moved engine block"); f.Anchor.transform.SetParent(parent.transform, false);
                    Require(f.Prepare(), "Moved native anchor was lost."); f.ReadAll(); Require(f.Installed, "Moving the block removed its fan input."); f.AssertSaved();
                });
                check(label + "competing pending fan neutralizes the mount until removed", () =>
                {
                    var competitor = f.AddFanCompetitor(); f.ReceiveVariant(competitor, 90, 0, 0, false);
                    Require(f.Prepare(), "Conflicting fan preparation failed."); f.ReadAll(); Require(!f.Installed, "Conflicting pending attachment was ignored.");
                    f.ReceiveVariant(competitor, 90, 0, 0, true, false); Require(f.Prepare(), "Conflict removal failed.");
                    f.ReadAll(); Require(f.Installed, "Unique fan did not recover."); f.AssertSaved();
                });
                check(label + "gameplay updates require application without changing saved wear", () =>
                {
                    f.Receive(12, 4, 0, false); f.Prepare(); f.ReadAll(); Require(!f.Installed, "Updated fan bypassed application.");
                    f.MarkApplied(); f.Prepare(); f.ReadAll(); Require(f.Installed, "Applied update did not restore fan."); f.AssertSaved();
                });
                check(label + "changed reader signature pauses and repairs the consumer", () =>
                {
                    var read = f.Readers[0]; var field = Get(read, "variableName"); Set(read, "variableName", new FsmString { Value = "Bolted" });
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Changed fan reader escaped validation."); f.AssertSaved(); }
                    finally { Set(read, "variableName", field); f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired fan reader stayed paused."); f.ReadAll(); Require(f.Installed, "Fan input did not recover.");
                });
                check(label + "companion consumer owns an independent proxy and repairs only the lost cache", () =>
                {
                    var companion = f.AddConsumer(consumer == "Valves" ? "Cooling" : "Valves"); Require(f.Prepare(), "Companion preparation failed.");
                    var other = NativeBagPartChecks.State(companion, consumer == "Valves" ? "Fan" : "Radiator fan").Actions[consumer == "Valves" ? 1 : 0]; other.OnEnter();
                    var otherProxy = companion.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)Get(other, "gameObject")); var proxy = f.Proxy;
                    Require(otherProxy != proxy && otherProxy != f.Mount.gameObject, "Consumers shared the fan proxy.");
                    UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Fan cache rebuild failed."); f.ReadAll(); other.OnEnter();
                    Require(f.Proxy != proxy && f.Installed && ReferenceEquals(Get(f.Readers[0], "goLastFrame"), f.Proxy)
                        && ReferenceEquals(Get(other, "goLastFrame"), otherProxy), "Repair disturbed another consumer or retained destroyed Data.");
                    foreach (bool installed in new[] { false, true })
                    {
                        f.Receive(90, 16, 0, true, installed); f.Prepare();
                        CheckNativeFanDecision(f, f.Reader, installed, true); CheckNativeFanDecision(f, companion, installed, true); f.AssertSaved();
                    }
                });
                check(label + "disconnect neutralizes and cleanup restores original cached targets", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); f.Prepare(); f.ReadAll(); Require(!f.Installed, "Disconnected fan stayed installed.");
                    Call(f.Sync, "RestoreGuestEngineInputs"); Require(ReferenceEquals(Get(f.Readers[0], "gameObject"), f.OriginalOwners[0]), "Fan cleanup lost the original owner wrapper.");
                    f.ReadAll(); Require(f.Installed && ReferenceEquals(Get(f.Readers[0], "goLastFrame"), f.Mount.gameObject), "Fan cleanup retained the proxy cache."); f.AssertSaved();
                });
            }
        }

        private static void CheckNativeFanDecision(Fixture f, PlayMakerFSM reader, bool fan, bool belt)
        {
            var vars = reader.FsmVariables;
            if (reader.FsmName == "Valves")
            {
                vars.FindFsmFloat("PowerAdd").Value = 2; NativeBagPartChecks.Fire(reader, "Fan belt");
                Require(reader.ActiveStateName == "Carburettor" && Near(vars.FindFsmFloat("PowerAdd").Value, !belt ? 2.13f : !fan ? 2.09f : 2), "Native fan/belt power adjustment diverged.");
            }
            else
            {
                vars.FindFsmFloat("CoolingFanRate").Value = 2; vars.FindFsmFloat("WaterLevel").Value = .5f;
                vars.FindFsmFloat("CoolingFanModifier").Value = 1000;
                ((FsmFloat)Get(NativeBagPartChecks.State(reader, "Fan").Actions[5], "float1")).Value = 3000;
                NativeBagPartChecks.Fire(reader, "Fan");
                // Native Fan first scales the existing rate by water level, then
                // overwrites it from RPM only when both drive parts are installed.
                Require(reader.ActiveStateName == "Flect" && Near(vars.FindFsmFloat("CoolingFanRate").Value, fan && belt ? 3 : 1), "Native fan cooling arithmetic diverged.");
            }
        }

        private sealed partial class Fixture
        {
            internal Variant AddFanPulley()
            {
                var mount = Data(Child(Mount.transform.parent.gameObject, "VINP_WaterpumpPulley"), null, 0);
                Seed(mount, 77, 8, 2, true);
                var variant = AddVariant("VIN127", mount); NativeBagPartChecks.Start(variant.Part); return variant;
            }

            internal Variant AddFanCompetitor()
            {
                var variant = AddVariant("VIN137", Mount, "VIN1378"); NativeBagPartChecks.Start(variant.Part); return variant;
            }
        }
    }
}
