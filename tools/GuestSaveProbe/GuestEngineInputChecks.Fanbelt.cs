using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        internal static void RunFanbelt(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "Oil", "Valves", "Cooling", "Electrics" })
            using (var f = new Fixture("FANBELT0", consumer))
            {
                string label = "fan belt " + consumer + ": ";
                check(label + "native installed caches start on saved mount", () =>
                {
                    f.ReadAll(); Require(f.Installed, "Native saved belt baseline failed.");
                    foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Mount.gameObject), "Native belt cache did not warm."); f.AssertSaved();
                });
                check(label + "missing host belt neutralizes every read using one inert bool proxy", () =>
                {
                    Require(f.Prepare(), "Belt input preparation failed."); f.ReadAll(); Require(!f.Installed, "Saved belt leaked into input.");
                    Require(f.Proxy != f.Mount.gameObject && !f.ProxyData.enabled && f.Proxy.GetComponent<Rigidbody>() == null
                        && f.ProxyData.FsmVariables.BoolVariables.Length == 1 && f.ProxyData.FsmVariables.FloatVariables.Length == 0, "Belt projection is not an inert installation input.");
                    foreach (var action in f.Readers) Require(f.Target(action) == f.Proxy, "Repeated belt read missed its proxy."); f.AssertSaved();
                });
                check(label + "pending fitted receipt stays absent until its application", () =>
                {
                    f.Receive(90, 0, 0, false); Require(f.Prepare(), "Pending belt failed preparation."); f.ReadAll(); Require(!f.Installed, "Unapplied belt became installed.");
                    f.MarkApplied(); Require(f.Prepare(), "Applied belt failed preparation."); f.ReadAll(); Require(f.Installed, "Applied belt stayed absent."); f.AssertSaved();
                });
                check(label + "native decision follows removal and refitting", () =>
                {
                    if (consumer == "Cooling") f.ReceiveVariant(f.Auxiliary("VIN126").Variants[0], 90, .7f, 1.8f, true);
                    if (consumer == "Electrics") f.ReceiveVariant(f.Auxiliary("VIN133").Variants[0], 90, .7f, 3.4f, true, electricalEfficiency: 350);
                    foreach (bool installed in new[] { true, false, true })
                    {
                        f.Receive(90, 0, 0, true, installed); Require(f.Prepare(), "Belt fit/removal failed preparation.");
                        CheckNativeBeltDecision(f, consumer, installed); f.AssertSaved();
                    }
                });
                check(label + "appearance receipts and duplicates keep the applied belt available when display is disabled", () =>
                {
                    // The display subsystem may be disabled independently. It must
                    // not interrupt an otherwise valid mechanical installation.
                    var mesh = Child(f.Part.gameObject, "loose belt mesh");
                    var objects = new List<FsmGameObject>(f.Part.FsmVariables.GameObjectVariables) { ObjectVar("Mesh", mesh) };
                    f.Part.FsmVariables.GameObjectVariables = objects.ToArray(); Set(f.Binding, "BeltVisualFailed", true);
                    var state = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                    uint applied = (uint)Get(f.Binding, "AppliedRevision");
                    foreach (bool visible in new[] { true, false, true })
                    {
                        state.PresentationRevision++; state.BeltVisual = new PartBeltVisualState { Visible = visible, Running = visible, Scale = .8f, Pitch = 1, ScrollSpeed = -90 };
                        foreach (int duplicate in new[] { 0, 1 })
                        {
                            Call(f.Sync, "OnReplacementPartState", state);
                            var accepted = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                            Require(accepted.PresentationRevision == state.PresentationRevision && PartBeltVisualPolicy.Same(accepted.BeltVisual, state.BeltVisual), "Appearance receipt was not accepted.");
                            Require(!((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Contains(f.PartId)
                                && (uint)Get(f.Binding, "AppliedRevision") == applied, "Appearance receipt queued gameplay application.");
                            Require(f.Prepare(), "Appearance preparation failed."); f.ReadAll(); Require(f.Installed, "Appearance receipt interrupted belt installation.");
                        }
                    }
                    f.AssertSaved();
                });
                foreach (string reason in new[] { "pending", "revision", "identity", "parent", "body" })
                {
                    string change = reason;
                    check(label + change + " remains unavailable even after a duplicate receipt", () =>
                    {
                        var state = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                        string nativeId = f.Part.FsmVariables.FindFsmString("ID").Value;
                        if (change == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                        if (change == "revision") Set(f.Binding, "AppliedRevision", state.Revision - 1);
                        if (change == "identity") f.Part.FsmVariables.FindFsmString("ID").Value = "FANBELT099";
                        if (change == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                        if (change == "body") UnityEngine.Object.DestroyImmediate(f.Part.GetComponent<Rigidbody>());
                        try
                        {
                            Call(f.Sync, "OnReplacementPartState", state); Require(f.Prepare(), "Unavailable belt failed neutral preparation.");
                            f.ReadAll(); Require(!f.Installed, "Duplicate receipt bypassed application/identity/attachment guard."); f.AssertSaved();
                        }
                        finally
                        {
                            if (f.Part.GetComponent<Rigidbody>() == null) f.Part.gameObject.AddComponent<Rigidbody>().isKinematic = true;
                            f.Part.FsmVariables.FindFsmString("ID").Value = nativeId; f.Part.transform.SetParent(f.Mount.transform, false); f.MarkApplied(); f.Prepare();
                        }
                    });
                }
                check(label + "new gameplay revision still requires application after visual updates", () =>
                {
                    f.Receive(80, 0, 0, false); Require(f.Prepare(), "Changed belt failed preparation."); f.ReadAll(); Require(!f.Installed, "Changed gameplay bypassed application.");
                    f.MarkApplied(); f.Prepare(); f.ReadAll(); Require(f.Installed, "Applied changed belt did not recover."); f.AssertSaved();
                });
                check(label + "changed bool reader pauses its consumer and recovers", () =>
                {
                    var reader = f.Readers[f.Readers.Length - 1]; var field = Get(reader, "variableName");
                    Set(reader, "variableName", new FsmString { Value = "Bolted" });
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Changed belt read escaped validation."); f.AssertSaved(); }
                    finally { Set(reader, "variableName", field); f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired belt reader remained paused."); f.ReadAll(); Require(f.Installed, "Repaired input remained absent.");
                });
                check(label + "destroyed proxy rebuilds all warmed bool caches", () =>
                {
                    var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Belt proxy rebuild failed."); f.ReadAll();
                    Require(f.Proxy != proxy && f.Installed, "Destroyed belt proxy stayed cached.");
                    foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Proxy), "Native bool cache retained destroyed Data."); f.AssertSaved();
                });
                check(label + "disconnect clears host input and cleanup restores saved targets", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare() == (f.Reader.FsmName != "Cooling") && (f.Reader.FsmName != "Cooling" || !f.Reader.enabled), "Disconnected Cooling must pause until cleanup."); f.ReadAll(); Require(!f.Installed, "Disconnected belt stayed installed.");
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Belt cleanup lost original target.");
                    f.ReadAll(); Require(f.Installed, "Cleanup did not restore saved bool cache."); f.AssertSaved();
                });
            }
        }

        private static void CheckNativeBeltDecision(Fixture f, string consumer, bool installed)
        {
            var vars = f.Reader.FsmVariables;
            if (consumer == "Oil")
            {
                vars.FindFsmFloat("FrictionAlternator").Value = .5f; vars.FindFsmFloat("FrictionWaterpump").Value = .4f;
                f.Fire("Fan belt"); Require(f.Reader.ActiveStateName == (installed ? "Oil filter" : "No belt"), "Oil chose the wrong native belt branch.");
                Require(Near(vars.FindFsmFloat("FrictionAlternator").Value, installed ? .5f : .09f)
                    && Near(vars.FindFsmFloat("FrictionWaterpump").Value, installed ? .4f : .09f), "Native belt load arithmetic diverged.");
            }
            else if (consumer == "Valves")
            {
                vars.FindFsmFloat("PowerAdd").Value = 2; f.Fire("Fan belt");
                Require(f.Reader.ActiveStateName == (installed ? "Radiator fan" : "Carburettor")
                    && Near(vars.FindFsmFloat("PowerAdd").Value, installed ? 2 : 2.13f), "Native belt power adjustment diverged.");
            }
            else if (consumer == "Cooling")
            {
                f.PumpRpm = 2000; f.Fire("Water Pump 2");
                Require(Near(vars.FindFsmFloat("Circulation").Value, installed ? 1.8f : .0001f), "Belt did not gate native water-pump circulation.");
                vars.FindFsmFloat("CoolingFanRate").Value = 2; vars.FindFsmFloat("WaterLevel").Value = .5f;
                vars.FindFsmFloat("CoolingFanModifier").Value = 1000;
                ((FsmFloat)Get(f.Action("Fan", 5), "float1")).Value = 3000; f.Fire("Fan");
                Require(f.Reader.ActiveStateName == "Flect" && Near(vars.FindFsmFloat("CoolingFanRate").Value, installed ? 3 : 1), "Second belt read did not gate native fan cooling.");
            }
            else
            {
                vars.FindFsmBool("Battery").Value = true; f.Fire("Check alternator");
                Require(f.Reader.ActiveStateName == (installed ? "Engine running?" : "No charge"), "Belt did not gate native alternator charging.");
                f.Fire("State 2"); Require(f.Reader.ActiveStateName == (installed ? "Delay" : "Engine off"), "Second belt read did not gate continued running.");
            }
        }
    }
}
