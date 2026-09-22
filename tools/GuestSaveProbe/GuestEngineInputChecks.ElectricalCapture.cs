using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private static void RunAlternatorCaptureChecks(Fixture f, Action<string, Action> check)
        {
            foreach (bool upgraded in new[] { false, true })
            {
                var part = upgraded ? f.Alternative!.Part : f.Part; object binding = upgraded ? f.Alternative!.Binding : f.Binding;
                object factory = upgraded ? f.Alternative!.Factory : f.Factory; uint id = upgraded ? f.Alternative!.PartId : f.PartId;
                check("alternator electrical capture: " + (upgraded ? "upgraded" : "stock") + " reads actual host mount damage and part scalars", () =>
                {
                    var savedMount = f.Mount.FsmVariables.GameObjectVariables; var savedPart = part.FsmVariables.GameObjectVariables;
                    float durability = part.FsmVariables.FindFsmFloat("Durability").Value, efficiency = part.FsmVariables.FindFsmFloat("Efficiency").Value;
                    Property(f.Session, "IsHost", true); Set(binding, "Replica", false);
                    f.Mount.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", part.gameObject), ObjectVar("AssemblyPoint", f.Mount.gameObject) };
                    part.FsmVariables.GameObjectVariables = new[] { ObjectVar("InstallPoint", f.Mount.gameObject) };
                    part.FsmVariables.FindFsmFloat("Durability").Value = .83f; part.FsmVariables.FindFsmFloat("Efficiency").Value = 312;
                    try
                    {
                        uint revision = 0;
                        foreach (bool damaged in new[] { false, true, false })
                        {
                            f.Mount.FsmVariables.FindFsmBool("Damaged").Value = damaged;
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                            Require(state != null && PartAttachmentPolicy.HasAttachment(state) && state.AlternatorDamaged == damaged
                                && Near(state.Scalars[4], .83f) && state.Scalars[5] == 312, "Host electrical capture substituted saved/prefab values.");
                            if (revision != 0) Require(state!.Revision == revision + 1, "Damage/repair did not advance host gameplay revision."); revision = state!.Revision;
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state));
                            Call(f.Sync, "ApplyReplacementScalars", binding, decoded);
                            Require(decoded.AlternatorDamaged == damaged && part.FsmVariables.FindFsmBool("Damaged") == null, "Mount flag leaked into part Data.");
                        }
                        f.Mount.FsmVariables.FindFsmGameObject("ActivePart").Value = f.Original.gameObject;
                        var pending = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                        Require(pending != null && !PartAttachmentPolicy.HasAttachment(pending) && pending.AlternatorDamaged == null, "Unresolved host fitting borrowed another occupant's flag.");
                    }
                    finally
                    {
                        f.Mount.FsmVariables.GameObjectVariables = savedMount; part.FsmVariables.GameObjectVariables = savedPart;
                        f.Mount.FsmVariables.FindFsmBool("Damaged").Value = true;
                        part.FsmVariables.FindFsmFloat("Durability").Value = durability; part.FsmVariables.FindFsmFloat("Efficiency").Value = efficiency;
                        Property(f.Session, "IsHost", false); Set(binding, "Replica", true); Set(factory, "Failed", false); f.Prepare();
                    }
                    AssertElectricalSaved(f);
                });
            }
            foreach (string fault in new[] { "missing damage bool", "factory mount mismatch", "duplicate mount Data" })
            {
                string scenario = fault;
                check("alternator electrical capture: " + scenario + " contains failure without publishing false health", () =>
                {
                    var savedMount = f.Mount.FsmVariables.GameObjectVariables; var savedPart = f.Part.FsmVariables.GameObjectVariables;
                    var savedBools = f.Mount.FsmVariables.BoolVariables;
                    var factoryMount = ((PlayMakerFSM)Get(f.Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP"); PlayMakerFSM? duplicate = null;
                    Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false);
                    f.Mount.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", f.Part.gameObject), ObjectVar("AssemblyPoint", f.Mount.gameObject) };
                    f.Part.FsmVariables.GameObjectVariables = new[] { ObjectVar("InstallPoint", f.Mount.gameObject) };
                    try
                    {
                        if (scenario == "missing damage bool") f.Mount.FsmVariables.BoolVariables = new[] { f.Mount.FsmVariables.FindFsmBool("Installed") };
                        if (scenario == "factory mount mismatch") factoryMount.Value = f.Original.gameObject;
                        if (scenario == "duplicate mount Data") duplicate = Empty(f.Mount.gameObject, "Data");
                        Require(Call(f.Sync, "BuildReplacementPartState", f.PartId) == null && (bool)Get(f.Factory, "Failed"), "Invalid host damage source published apparently healthy state.");
                        Require(!(bool)Get(f.Alternative!.Factory, "Failed"), "Host capture failure disabled another family.");
                    }
                    finally
                    {
                        if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                        factoryMount.Value = f.Mount.gameObject; f.Mount.FsmVariables.BoolVariables = savedBools;
                        f.Mount.FsmVariables.GameObjectVariables = savedMount; f.Part.FsmVariables.GameObjectVariables = savedPart;
                        Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); Set(f.Factory, "Failed", false); f.Prepare();
                    }
                    AssertElectricalSaved(f);
                });
            }
        }
    }
}
