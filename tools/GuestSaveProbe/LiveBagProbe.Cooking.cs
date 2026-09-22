using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool CookingProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_COOKING_TEST") == "1";
        private static readonly string[] CookingPaths = { "HOMENEW/Functions/ElectricThings/OvenStove", "YARD/Building/KITCHEN/OvenStove" };
        private static bool CookingCommand(string[] args, List<string> rows)
        {
            if (!CookingProbe || !args[1].StartsWith("cooking-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            int oven = int.Parse(args[2]);
            var sim = Find(CookingPaths[oven] + "/Simulation", "Data");
            switch (args[1])
            {
                case "cooking-visit":
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    Player.position = sim.transform.position + new Vector3(0, 0, 1.5f); break;
                case "cooking-away":
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    Player.position = sim.transform.position + new Vector3(30, 0, 0); break;
                case "cooking-request":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest fixture only.");
                    SessionManager.Instance.SendWorldMessage(new WinterMP.Net.Messages.StoveKnobIntent {
                        ApplianceId = WinterMP.Net.StableHash.Fnv1a32(CookingPaths[oven]), PlayerId = byte.Parse(args[6]),
                        Plate = byte.Parse(args[3]), Direction = byte.Parse(args[4]), Sequence = ushort.Parse(args[5]) }, WinterMP.Net.Channel.ReliableOrdered); break;
                case "cooking-checks": CookingNativeChecks(oven, rows); break;
                case "cooking-smoke":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    CookingAction(sim, args[3] == "on" ? "Smoke" : "Stove light off").OnEnter(); break;
                case "cooking-drift":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest fixture only.");
                    var stove = Get(CookingBound(oven), "Stove");
                    var smoke = (EllipsoidParticleEmitter)Get(stove, "Smoke");
                    smoke.emit = !(bool)Get(stove, "OldSmoke");
                    var hazards = (FsmFloat[])Get(stove, "FireHazards");
                    var oldHazards = (float[])Get(stove, "OldFireHazards");
                    for (int i = 0; i < hazards.Length; i++) hazards[i].Value = oldHazards[i] + 20 + i;
                    rows.Add("cooking-original-effects|" + oven + "|" + Get(stove, "OldSmoke") + "|"
                        + string.Join(",", Array.ConvertAll(oldHazards, value => value.ToString("R", CultureInfo.InvariantCulture))));
                    break;
                case "cooking-turn":
                    Enter(Find(CookingPaths[oven] + "/KnobPower" + args[3], "Screw"), args[4] == "up" ? "Screw" : "Unscrew"); break;
                case "cooking-source":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    var factory = Find("Spawner/CreateMooseMeat", "MooseMeat");
                    factory.FsmVariables.FindFsmGameObject("SpawnPoint").Value = sim.transform.parent.Find("HotPlate1").gameObject;
                    factory.SendEvent("SPAWNITEM"); break;
                case "cooking-heat":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    sim.FsmVariables.FindFsmFloat("HotPlate1Heat").Value = float.Parse(args[3], CultureInfo.InvariantCulture); break;
                case "cooking-place":
                case "cooking-remove":
                    var binding = ((IDictionary)Get(Items, "_meat"))[uint.Parse(args[3])];
                    var body = (Rigidbody)Get(binding, "Body");
                    var tracked = ((IDictionary)Get(Items, "_items"))[uint.Parse(args[3])];
                    Call(Items, "ClaimItem", SessionManager.Instance!, tracked, body, Time.unscaledTime);
                    body.transform.position = sim.transform.parent.Find("HotPlate1/GrillTrigger").position
                        + (args[1] == "cooking-remove" ? Vector3.up * 2 : Vector3.zero);
                    body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll;
                    break;
                case "cooking-eat":
                    var meal = ((IDictionary)Get(Items, "_meat"))[uint.Parse(args[3])];
                    Enter((PlayMakerFSM)Get(meal, "Use"), "Eat"); break;
                default: throw new InvalidOperationException("Unknown cooking command.");
            }
            return true;
        }

        private static void CookingSnapshot(List<string> rows)
        {
            RequirePersistenceSandbox();
            if (Application.loadedLevelName != "GAME") return;
            rows.Add("cooking-ready|" + typeof(WinterMP.Core.Sync.PlayerSyncManager).GetProperty("IsLocalSpawnReady", Members)
                .GetValue(WinterMP.Core.Sync.PlayerSyncManager.Instance, null));
            if (Player != null) rows.Add("cooking-local-pose|" + Vector(Player.position));
            for (int n = 0; n < CookingPaths.Length; n++)
            {
                var sim = Find(CookingPaths[n] + "/Simulation", "Data");
                var bound = FindCookingBound(n); var binding = bound == null ? null : Get(bound, "Stove");
                var received = bound == null ? null : Get(bound, "StoveReceived") as WinterMP.Net.Messages.ApplianceState;
                rows.Add("cooking-sync|" + n + "|" + (binding != null) + "|" + (bound != null && (bool)Get(bound, "StoveFailed")) + "|" + (received?.StoveRevision ?? 0));
                var property = (FsmProperty)CookingAction(sim, "Stove light off").GetType().GetField("targetProperty").GetValue(CookingAction(sim, "Stove light off"));
                var emitter = (EllipsoidParticleEmitter)property.TargetObject.Value;
                var fireHazards = new string[4];
                for (int i = 0; i < 4; i++) fireHazards[i] = sim.FsmVariables.FindFsmFloat("HotPlate" + (i + 1) + "FireHazard").Value.ToString("R", CultureInfo.InvariantCulture);
                rows.Add("cooking-effects|" + n + "|" + emitter.emit + "|" + string.Join(",", fireHazards));
                rows.Add("cooking-oven|" + n + "|" + sim.gameObject.activeInHierarchy + "|" + sim.enabled + "|" + sim.ActiveStateName
                    + "|" + Vector(sim.transform.position) + "|" + sim.FsmVariables.FindFsmBool("Fuse").Value
                    + "|" + sim.FsmVariables.FindFsmGameObject("StoveLight").Value.activeSelf);
                for (int i = 1; i <= 4; i++)
                {
                    var knob = Find(CookingPaths[n] + "/KnobPower" + i, "Screw");
                    rows.Add("cooking-plate|" + n + "|" + i + "|" + knob.gameObject.activeInHierarchy + "|" + knob.enabled
                        + "|" + knob.FsmVariables.FindFsmFloat("Rot").Value + "|" + knob.FsmVariables.FindFsmFloat("Data").Value
                        + "|" + sim.FsmVariables.FindFsmFloat("HotPlate" + i + "Heat").Value
                        + "|" + sim.transform.parent.Find("HotPlate" + i + "/GrillTrigger").gameObject.activeSelf
                        + "|" + sim.transform.parent.Find("HotPlate" + i + "/FireTrigger").gameObject.activeSelf);
                }
            }
            foreach (DictionaryEntry pair in (IDictionary)Get(Items, "_meat"))
            {
                var body = Get(pair.Value, "Body") as Rigidbody;
                if (body == null) continue;
                var use = (PlayMakerFSM)Get(pair.Value, "Use");
                PlayMakerFSM? fire = null; foreach (var f in body.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Fire") fire = f;
                rows.Add("cooking-food|" + pair.Key + "|" + Get(pair.Value, "NativeId") + "|" + body.name + "|" + use.ActiveStateName
                    + "|" + use.FsmVariables.FindFsmInt("Type").Value + "|" + use.FsmVariables.FindFsmFloat("Condition").Value
                    + "|" + Vector(body.position) + "|" + (fire != null ? fire.ActiveStateName + ":" + fire.FsmVariables.FindFsmFloat("Grill").Value + "/" + fire.FsmVariables.FindFsmFloat("GrillingTime").Value : "none"));
            }
            foreach (var peer in SessionManager.Instance!.Players) rows.Add("cooking-peer|" + peer.PlayerId + "|" + Vector(peer.Position) + "|" + (Time.unscaledTime - peer.LastTransformTime));
        }

        private static FsmStateAction CookingAction(PlayMakerFSM sim, string name)
        {
            var state = Hook.GetMethod("FindState", Members).Invoke(null, new object[] { sim, name });
            return (FsmStateAction)Hook.GetMethod("NativeAction", Members).Invoke(null, new object[] { state, 0 });
        }

        private static object CookingBound(int oven) => FindCookingBound(oven) ?? throw new InvalidOperationException("Stove registration missing.");

        private static object? FindCookingBound(int oven)
        {
            var appliances = Get(WinterMP.Core.Sync.WorldSyncManager.Instance!, "_appliances");
            foreach (var bound in (IList)Get(appliances, "_ovens"))
                if ((string)Get(bound, "ContainerPath") == CookingPaths[oven]) return bound;
            return null;
        }

        private static void CookingNativeChecks(int oven, List<string> rows)
        {
            if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host checks only.");
            var appliances = Get(WinterMP.Core.Sync.WorldSyncManager.Instance!, "_appliances");
            var validate = appliances.GetType().GetMethod("ValidateKnob", Members);
            var catalog = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("Stoves", Members).GetValue(null, null);
            var binding = Get(CookingBound(oven), "Stove");
            foreach (var knob in (Array)Get(binding, "Knobs"))
            {
                void Validate() { validate.Invoke(null, new[] { knob, catalog }); }
                void Refuse(string label)
                {
                    bool rejected = false;
                    try { Validate(); } catch (System.Reflection.TargetInvocationException error) { rejected = error.InnerException is InvalidOperationException; }
                    if (!rejected) throw new InvalidOperationException("Changed native binding accepted: " + label);
                    rows.Add("cooking-check|" + label);
                }
                Validate(); rows.Add("cooking-check|native knob action and target bindings validated");
                var step = (FsmFloat)Get(knob, "Step"); float old = step.Value;
                try { step.Value = old + 1; Refuse("changed native step refused"); } finally { step.Value = old; }
                var action = ((FsmStateAction[])Get(knob, "UpActions"))[0]; bool enabled = action.Enabled;
                try { action.Enabled = false; Refuse("disabled native step refused"); } finally { action.Enabled = enabled; }
            }
            var bound = CookingBound(oven);
            var stoveField = bound.GetType().GetField("Stove", Members);
            var failedField = bound.GetType().GetField("StoveFailed", Members);
            bool failed = (bool)failedField.GetValue(bound);
            var report = new WinterMP.Net.Messages.ApplianceFireReport {
                ApplianceId = (uint)Get(bound, "Id"), PlayerId = 1, Plate = 1, Sequence = 123 };
            try
            {
                foreach (bool unavailable in new[] { false, true })
                {
                    stoveField.SetValue(bound, unavailable ? null : binding);
                    foreach (bool broken in new[] { false, true })
                    {
                        failedField.SetValue(bound, broken);
                        if ((bool)Call(appliances, "OnHostFireReport", report))
                            throw new InvalidOperationException("Legacy guest fire report accepted for a native stove.");
                        rows.Add("cooking-check|legacy fire refused: unavailable=" + unavailable + " failed=" + broken);
                    }
                }
            }
            finally { stoveField.SetValue(bound, binding); failedField.SetValue(bound, failed); }
        }
    }
}
