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
        internal static void RunAlternatorMechanical(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN133"))
            {
                const string label = "alternator mechanical inputs: ";
                var race = f.Alternative!;
                check(label + "native readers first cache the guest saved alternator", () =>
                {
                    f.ReadAll(); Require(f.Installed && f.Wear == 77 && Near(AlternatorFriction(f), 4.2f), "Native alternator cache did not warm.");
                    foreach (var read in f.Readers) Require(ReferenceEquals(Get(read, "goLastFrame"), f.Mount.gameObject), "Native reader cache was not warmed.");
                });
                check(label + "absent host alternator supplies a neutral independent proxy", () =>
                {
                    Require(f.Prepare(), "Alternator input binding failed."); f.ReadAll(); AssertAlternatorAbsent(f);
                    Require(f.Proxy != f.Mount.gameObject && !f.ProxyData.enabled && f.Proxy.GetComponent<Rigidbody>() == null, "Alternator inputs are not inert.");
                    Require(((IList)Get(f.Sync, "_guestEngineInputs")).Count == 10, "Oil did not retain all ten independent engine sources."); f.AssertSaved();
                });
                check(label + "pending stock input waits for matching applied identity", () =>
                {
                    f.Receive(90, 16, 7, false, efficiency: 3.4f); Require(f.Prepare(), "Pending alternator preparation failed.");
                    f.ReadAll(); AssertAlternatorAbsent(f); f.MarkApplied(); Require(f.Prepare(), "Applied alternator preparation failed.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 90 && Near(AlternatorFriction(f), 3.4f), "Applied stock values were lost."); f.AssertSaved();
                });
                check(label + "new state waits for native reads without replacing calculation scratch", () =>
                {
                    var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                    f.Receive(91, 16, 8, true, efficiency: 3.1f); Require(f.Prepare(), "Alternator update failed.");
                    Require(f.Proxy == proxy && f.Reader.ActiveStateName == active && f.Wear == 90 && Near(AlternatorFriction(f), 3.4f), "Projection replayed calculations.");
                    f.ReadAll(); Require(f.Wear == 91 && Near(AlternatorFriction(f), 3.1f), "Updated actual friction did not reach native reader."); f.AssertSaved();
                });
                foreach (bool upgraded in new[] { false, true })
                {
                    if (upgraded) { f.Receive(91, 0, 8, false, false, 3.1f); f.Prepare(); }
                    foreach (float wear in new[] { 4.999f, 5f, 5.001f })
                    {
                        float value = wear;
                        check(label + (upgraded ? "upgraded" : "stock") + " wear " + value + " follows native seizure decision", () =>
                        {
                            if (upgraded) f.ReceiveVariant(race, value, .9f, 3f, true);
                            else f.Receive(value, 16, 7, true, efficiency: 3.4f);
                            Require(f.Prepare(), "Alternator wear update failed.");
                            f.Reader.FsmVariables.FindFsmBool("SeizeAlternator").Value = false;
                            f.Reader.FsmVariables.FindFsmFloat("Amperes").Value = .3f;
                            f.Fire("Alternator");
                            Require(f.Reader.ActiveStateName == (value <= 5 ? "Alternator seize" : "Wearing 3"), "Native alternator wear boundary diverged.");
                            Require(f.Reader.FsmVariables.FindFsmBool("SeizeAlternator").Value == (value <= 5)
                                && Near(f.Reader.FsmVariables.FindFsmFloat("FrictionAlternator").Value, value <= 5 ? .2f : .15f), "Native seizure or load calculation diverged.");
                            f.ReadAll(); Require(Near(AlternatorFriction(f), upgraded ? 3f : 3.4f), "Variant friction reader retained the previous source."); f.AssertSaved();
                        });
                    }
                }
                check(label + "competing pending variant closes input until the conflict is removed", () =>
                {
                    f.Receive(90, 16, 7, false, efficiency: 3.4f); Require(f.Prepare(), "Conflict preparation failed."); f.ReadAll(); AssertAlternatorAbsent(f);
                    f.Fire("Alternator"); Require(f.Reader.ActiveStateName == "Fan belt", "Missing alternator bypassed native installation branch.");
                    f.Receive(90, 0, 7, false, false, 3.4f); Require(f.Prepare(), "Conflict removal failed."); f.ReadAll();
                    Require(f.Installed && Near(AlternatorFriction(f), 3), "Unique upgraded alternator did not recover."); f.AssertSaved();
                });
                check(label + "stock and upgraded host publications preserve actual friction and rotation", () =>
                {
                    foreach (var part in new[] { f.Part, race.Part })
                    {
                        object binding = part == f.Part ? f.Binding : race.Binding; uint id = part == f.Part ? f.PartId : race.PartId;
                        Property(f.Session, "IsHost", true); Set(binding, "Replica", false);
                        part.FsmVariables.FindFsmFloat("Friction").Value = 2.83f; part.FsmVariables.FindFsmFloat("SettingRotation").Value = 11;
                        try
                        {
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                            Require(state != null && state.Scalars.Length == 6 && state.Scalars[2] == 11 && Near(state.Scalars[3], 2.83f), "Host substituted template friction or shifted existing rotation.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!));
                            part.FsmVariables.FindFsmFloat("Friction").Value = 6;
                            var next = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                            Require(next != null && next.Revision != state!.Revision, "Changed friction did not advance gameplay revision.");
                            Call(f.Sync, "ApplyReplacementScalars", binding, decoded);
                            Require(Near(part.FsmVariables.FindFsmFloat("Friction").Value, 2.83f)
                                && part.FsmVariables.FindFsmFloat("SettingRotation").Value == 11, "Replica application lost actual host friction or rotation.");
                        }
                        finally { Property(f.Session, "IsHost", false); Set(binding, "Replica", true); }
                    }
                    f.AssertSaved();
                });
                foreach (string unavailable in new[] { "pending", "revision", "identity", "parent" })
                {
                    string reason = unavailable;
                    check(label + reason + " cannot expose unapplied upgraded data", () =>
                    {
                        f.ReceiveVariant(race, 90, .9f, 3, true);
                        string nativeId = race.Part.FsmVariables.FindFsmString("ID").Value;
                        if (reason == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(race.PartId);
                        if (reason == "revision") Set(race.Binding, "AppliedRevision", race.Revision - 1);
                        if (reason == "identity") race.Part.FsmVariables.FindFsmString("ID").Value = "ALTERNATOR099";
                        if (reason == "parent") race.Part.transform.SetParent(f.Extras.transform, false);
                        try { Require(f.Prepare(), "Unavailable alternator failed neutral preparation."); f.ReadAll(); AssertAlternatorAbsent(f); f.AssertSaved(); }
                        finally
                        {
                            race.Part.FsmVariables.FindFsmString("ID").Value = nativeId; race.Part.transform.SetParent(f.Mount.transform, false);
                            f.ReceiveVariant(race, 90, .9f, 3, true); f.Prepare();
                        }
                        f.ReadAll(); Require(f.Installed, "Repaired alternator did not recover.");
                    });
                }
                check(label + "variant mount disagreement pauses Oil and repairs without saved writes", () =>
                {
                    var mount = ((PlayMakerFSM)Get(race.Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP");
                    mount.Value = f.Original.gameObject;
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Inconsistent upgraded mount escaped validation."); f.AssertSaved(); }
                    finally { mount.Value = f.Mount.gameObject; f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired variant mount stayed paused."); f.ReadAll(); Require(f.Installed, "Repaired variant lost input.");
                });
                check(label + "three Oil sources share only normal calculation scratch", () =>
                {
                    var water = f.Auxiliary("VIN126").Variants[0]; var oil = f.Auxiliary("VIN132").Variants[0];
                    f.ReceiveVariant(water, 81, .7f, 1.8f, true); f.ReceiveVariant(oil, 82, 1.1f, 0, true);
                    Require(f.Prepare(), "Three-source preparation failed."); f.ReadAll(); var alternator = f.Proxy;
                    var waterRead = f.Action("Water Pump", 3); var oilRead = f.Action("Oil pump?", 2);
                    Require(f.Target(waterRead) != alternator && f.Target(oilRead) != alternator && f.Target(waterRead) != f.Target(oilRead), "Oil sources reused a proxy.");
                    waterRead.OnEnter(); Require(f.Wear == 81 && f.Prepare() && f.Wear == 81, "Projection overwrote water-pump scratch.");
                    oilRead.OnEnter(); Require(f.Wear == 82, "Oil pump lost its own read."); f.ReadAll(); Require(f.Wear == 90, "Alternator lost its own read."); f.AssertSaved();
                });
                check(label + "changed alternator reader pauses Oil while companion Wearing continues", () =>
                {
                    var wearing = f.AddConsumer("Wearing");
                    try
                    {
                        Require(f.Prepare(), "Companion preparation failed."); var read = f.Readers[0]; var original = Get(read, "variableName");
                        Set(read, "variableName", new FsmString { Value = "Efficiency" });
                        try { Require(!f.Prepare() && !f.Reader.enabled && wearing.enabled, "Reader failure escaped consumer containment."); f.AssertSaved(); }
                        finally { Set(read, "variableName", original); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired Oil reader stayed paused.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(wearing); f.Prepare(); }
                });
                check(label + "destroyed proxy rebuilds the warmed native target cache", () =>
                {
                    f.ReadAll(); var previous = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData);
                    Require(f.Prepare(), "Alternator proxy rebuild failed."); f.ReadAll();
                    Require(f.Proxy != previous && f.Installed && Near(AlternatorFriction(f), 3), "Native cache retained destroyed alternator Data."); f.AssertSaved();
                });
                check(label + "removal and disconnect clear input before restoring original targets", () =>
                {
                    f.ReceiveVariant(race, 90, .9f, 3, false, false); Require(f.Prepare(), "Alternator removal failed."); f.ReadAll(); AssertAlternatorAbsent(f);
                    f.ReceiveVariant(race, 90, .9f, 3, true); f.Prepare(); Property(f.Session, "State", SessionState.Idle);
                    Require(f.Prepare(), "Alternator disconnect failed."); f.ReadAll(); AssertAlternatorAbsent(f);
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Cleanup lost original reader owner.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 77 && Near(AlternatorFriction(f), 4.2f), "Cleanup did not restore saved native caches."); f.AssertSaved();
                });
            }
        }

        private static float AlternatorFriction(Fixture f) => f.Reader.FsmVariables.FindFsmFloat("AlternatorFrictionRate").Value;
        private static void AssertAlternatorAbsent(Fixture f)
        { Require(!f.Installed && f.Wear == 0 && AlternatorFriction(f) == 0, "Unavailable alternator exposed saved or unapplied inputs."); }
    }
}
