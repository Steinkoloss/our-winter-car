using System;
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
        internal static void RunCamshaft(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "Cylinders", "Wearing", "Valves" })
            using (var f = new Fixture("VIN115", consumer))
            {
                string label = "camshaft " + consumer + ": ";
                check(label + "native readers begin with the saved camshaft and warm their caches", () =>
                {
                    f.ReadAll(); AssertCamValues(f, 77, 2, 3, "51003100");
                    foreach (var read in f.Readers) Require(ReferenceEquals(Get(read, "goLastFrame"), f.Mount.gameObject), "Cam reader did not warm saved target.");
                    f.AssertSaved();
                });
                check(label + "missing and pending host parts use neutral inputs until fully applied", () =>
                {
                    Require(f.Prepare(), "Cold cam binding failed."); f.ReadAll(); AssertCamUnavailable(f);
                    f.Receive(90, 16, 1.1f, false, efficiency: 2); Require(f.Prepare(), "Pending cam preparation failed."); f.ReadAll(); AssertCamUnavailable(f);
                    f.MarkApplied(); Require(f.Prepare(), "Applied cam preparation failed."); f.ReadAll();
                    Require(f.Installed, "Applied cam remained absent."); AssertCamValues(f, 90, 1.1f, 2, "55003500"); f.AssertSaved();
                });
                check(label + "nested head attachment survives moving the assembled cylinder head", () =>
                {
                    var accepted = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                    Require(accepted.ParentId == f.AnchorId && accepted.ParentPath == "CamParent/VINP_CamshaftSprocket/VINP_CamShaft", "Cam fixture lost the real nested head address.");
                    var parent = f.Anchor.transform.parent; f.Anchor.transform.SetParent(f.Car.transform, false);
                    try { Require(f.Prepare(), "Moved head rejected live cam references."); f.ReadAll(); Require(f.Installed, "Moved head lost host cam attachment."); }
                    finally { f.Anchor.transform.SetParent(parent, false); }
                    f.AssertSaved();
                });
                if (consumer == "Cylinders")
                {
                    check(label + "native powertrain gate refuses an absent camshaft", () =>
                    {
                        for (int i = 1; i <= 7; i++) f.Reader.FsmVariables.FindFsmBool("Installed" + i).Value = true;
                        f.Fire("Powertrain"); Require(f.Reader.ActiveStateName == "Crank", "Present cam did not pass controlled powertrain gate.");
                        f.Receive(90, 0, 1.1f, false, false, 2); Require(f.Prepare(), "Cam removal failed.");
                        f.Fire("Powertrain"); Require(f.Reader.ActiveStateName == "Not Ok" && !f.Installed && f.Starter.FsmVariables.FindFsmBool("ShutOff").Value, "Absent cam bypassed native powertrain gate.");
                        f.Receive(90, 16, 1.1f, true, efficiency: 2); f.Prepare();
                    });
                    foreach (float wear in new[] { 6f, 7f, 8f })
                    {
                        float value = wear;
                        check(label + "wear " + value + " uses the native damaged-cam threshold", () =>
                        {
                            f.Receive(value, 16, 1.1f, true, efficiency: 2); Require(f.Prepare(), "Cam wear update failed."); f.Fire("Cam wear");
                            Require(f.Reader.ActiveStateName == (value <= 7 ? "Random symtp" : "Head gasket"), "Cam wear threshold diverged."); f.AssertSaved();
                        });
                    }
                }
                if (consumer == "Wearing") check(label + "native durability arithmetic preserves saved cam wear", () =>
                {
                    f.ReadAll(); f.Reader.FsmVariables.FindFsmFloat("Wear2").Value = .25f;
                    f.Action("Durability 2", 0).OnEnter();
                    Require(Near(f.Reader.FsmVariables.FindFsmFloat("Multiplier").Value, .275f), "Cam durability arithmetic ignored host data.");
                    Require(!f.Action("Durability 2", 1).Enabled && f.Target(f.Action("Durability 2", 1)) == f.Mount.gameObject, "Saved cam writer was enabled or retargeted."); f.AssertSaved();
                });
                if (consumer == "Valves") check(label + "stock profile parses its power and torque RPM through native actions", () =>
                {
                    f.Fire("Get cam profile"); AssertCamRpm(f, 5500, 3500);
                    f.SetValves(new float[] { 6.5f, 5, 5, 5, 5, 5, 5, 5 }); f.Fire("Cyl 1 intake");
                    Require(f.Reader.ActiveStateName == "Cyl 1 exhaust", "Stock tolerance falsely selected tight valves."); f.AssertSaved();
                });
                f.Receive(90, 0, 1.1f, false, false, 2); f.Prepare();
                var profiles = new[] { "60003840", "70004480", "75004800", "80005120" };
                var tolerances = new[] { 1.75f, 1.25f, 1f, .75f };
                for (int i = 0; i < f.CamVariants.Count; i++)
                {
                    int index = i; var variant = f.CamVariants[i];
                    check(label + variant.NativeId + " replaces the previous variant through the same proxy", () =>
                    {
                        if (index > 0) f.ReceiveVariant(f.CamVariants[index - 1], 90, .8f, tolerances[index - 1], false, false, profiles[index - 1]);
                        var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                        f.ReceiveVariant(variant, 92 + index, .8f, tolerances[index], true, camProfile: profiles[index]);
                        f.AssertVariantReady(variant); Require(f.Prepare(), "Cam upgrade preparation failed.");
                        Require(f.Proxy == proxy && f.Reader.ActiveStateName == active, "Cam swap rebuilt or replayed native graph.");
                        f.ReadAll(); Require(f.Installed, "Cam upgrade stayed absent."); AssertCamValues(f, 92 + index, .8f, tolerances[index], profiles[index]);
                        if (consumer == "Valves")
                        {
                            f.Fire("Get cam profile"); AssertCamRpm(f, int.Parse(profiles[index].Substring(0, 4)), int.Parse(profiles[index].Substring(4, 4)));
                            f.SetValves(new float[] { 6.5f, 5, 5, 5, 5, 5, 5, 5 }); f.Fire("Cyl 1 intake");
                            Require(f.Reader.ActiveStateName == (index == 0 ? "Cyl 1 exhaust" : "Cyl1 power 9"), "Native valve tolerance did not follow upgrade.");
                            Require(!f.Action("Cyl1 power 9", 2).Enabled, "Valve wear bypassed saved-part protection.");
                        }
                        if (consumer == "Cylinders")
                        {
                            f.Fire("Break 2"); Require(f.Reader.ActiveStateName == (tolerances[index] <= 1 ? "State 1" : "Not Ok"), "Native broken-belt tolerance did not follow upgrade.");
                        }
                        f.AssertSaved();
                    });
                }
                var race = f.CamVariants[3];
                check(label + "a competing pending camshaft closes input until the conflict is removed", () =>
                {
                    f.Receive(90, 16, 1.1f, false, efficiency: 2); Require(f.Prepare(), "Conflicting cam preparation failed."); f.ReadAll(); AssertCamUnavailable(f);
                    f.Receive(90, 0, 1.1f, false, false, 2); Require(f.Prepare(), "Cam conflict removal failed."); f.ReadAll();
                    Require(f.Installed, "Unique race cam did not recover."); AssertCamValues(f, 95, .8f, .75f, "80005120"); f.AssertSaved();
                });
                check(label + "host publication and replica application preserve actual cam fields", () =>
                {
                    foreach (var part in new[] { f.Part, race.Part })
                    {
                        object binding = part == f.Part ? f.Binding : race.Binding; uint id = part == f.Part ? f.PartId : race.PartId;
                        Property(f.Session, "IsHost", true); Set(binding, "Replica", false);
                        part.FsmVariables.FindFsmFloat("Durability").Value = .93f; part.FsmVariables.FindFsmFloat("ValveTolerance").Value = 1.4f;
                        part.FsmVariables.FindFsmString("CamProfile").Value = "56003600";
                        try
                        {
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                            Require(state != null && state.Scalars.Length == 4 && Near(state.Scalars[2], .93f) && Near(state.Scalars[3], 1.4f) && state.CamProfile == "56003600", "Host substituted prefab defaults for actual cam data.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!));
                            part.FsmVariables.FindFsmString("CamProfile").Value = "80005120";
                            Call(f.Sync, "ApplyReplacementScalars", binding, decoded);
                            Require(part.FsmVariables.FindFsmString("CamProfile").Value == "56003600", "Owned replica lost host cam profile.");
                        }
                        finally { Property(f.Session, "IsHost", false); Set(binding, "Replica", true); }
                    }
                    f.AssertSaved();
                });
                check(label + "changed factory mount and replica identity cannot supply cam input", () =>
                {
                    var reference = ((PlayMakerFSM)Get(f.CamVariants[0].Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP"); reference.Value = f.Original.gameObject;
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Inconsistent cam factory mount escaped validation."); }
                    finally { reference.Value = f.Mount.gameObject; f.Prepare(); }
                    var id = race.Part.FsmVariables.FindFsmString("ID"); string original = id.Value; id.Value = "CAMTUNEd099";
                    try { Require(f.Prepare(), "Unavailable cam identity failed neutral preparation."); f.ReadAll(); AssertCamUnavailable(f); }
                    finally { id.Value = original; f.Prepare(); }
                    f.ReadAll(); Require(f.Installed, "Repaired cam identity did not recover."); f.AssertSaved();
                });
                if (consumer == "Valves")
                {
                    check(label + "string reader changes pause only valves while combustion and wear continue", () =>
                    {
                        var cylinders = f.AddConsumer("Cylinders"); var wearing = f.AddConsumer("Wearing");
                        try
                        {
                            Require(f.Prepare(), "Three cam consumers failed to prepare.");
                            var read = f.Readers[1]; var output = Get(read, "storeValue"); Set(read, "storeValue", new FsmString { Name = "CamProfile", UseVariable = true });
                            try
                            {
                                Require(!f.Prepare() && !f.Reader.enabled && cylinders.enabled && wearing.enabled, "Changed string output escaped consumer containment.");
                                NativeBagPartChecks.State(cylinders, "Cam wear").Actions[0].OnEnter(); NativeBagPartChecks.State(wearing, "State 4").Actions[0].OnEnter();
                                Require(cylinders.FsmVariables.FindFsmFloat("Wear").Value == 95 && Near(wearing.FsmVariables.FindFsmFloat("DurabilityCamshaft").Value, .8f), "Healthy consumer lost accepted cam state.");
                            }
                            finally { Set(read, "storeValue", output); f.Prepare(); }
                            Require(f.Reader.enabled, "Repaired string reader stayed paused.");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(cylinders); UnityEngine.Object.DestroyImmediate(wearing); f.Prepare(); }
                        f.AssertSaved();
                    });
                    check(label + "destroyed string proxy rebuilds its native cached target", () =>
                    {
                        var previous = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Cam string proxy rebuild failed.");
                        f.Fire("Get cam profile"); Require(f.Proxy != previous, "Native string cache retained the destroyed Data component."); AssertCamRpm(f, 8000, 5120); f.AssertSaved();
                    });
                }
                check(label + "removal and disconnect clear inputs before cleanup restores saved caches", () =>
                {
                    f.ReceiveVariant(race, 95, .8f, .75f, false, false, "80005120"); Require(f.Prepare(), "Cam removal failed."); f.ReadAll(); AssertCamUnavailable(f);
                    if (consumer == "Valves") { f.Fire("Get cam profile"); AssertCamRpm(f, 0, 0); }
                    f.ReceiveVariant(race, 95, .8f, .75f, true, camProfile: "80005120"); f.Prepare();
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Cam disconnect failed."); f.ReadAll(); AssertCamUnavailable(f);
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Cam cleanup lost original owner wrapper.");
                    f.ReadAll(); AssertCamValues(f, 77, 2, 3, "51003100"); f.AssertSaved();
                });
            }
        }

        private static void AssertCamValues(Fixture f, float wear, float durability, float tolerance, string profile)
        {
            if (f.Reader.FsmName == "Wearing") Require(Near(f.Durability, durability), "Wrong projected cam durability.");
            else
            {
                Require(Near(f.Reader.FsmVariables.FindFsmFloat("ValveTolerance").Value, tolerance), "Wrong projected cam tolerance.");
                if (f.Reader.FsmName == "Cylinders") Require(f.Wear == wear, "Wrong projected cam wear.");
                else Require(f.Reader.FsmVariables.FindFsmString("CamProfile").Value == profile, "Wrong projected cam profile.");
            }
        }
        private static void AssertCamUnavailable(Fixture f)
        { Require(!f.Installed, "Unavailable cam still supplies installed input."); AssertCamValues(f, 0, 0, 0, "00000000"); }
        private static void AssertCamRpm(Fixture f, float power, float torque)
        { Require(f.Reader.FsmVariables.FindFsmFloat("CamRPMPower").Value == power && f.Reader.FsmVariables.FindFsmFloat("CamRPMTorque").Value == torque, "Native cam profile RPM conversion diverged."); }
    }
}
