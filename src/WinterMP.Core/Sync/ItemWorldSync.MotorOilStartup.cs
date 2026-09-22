using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class OilStartup
        {
            internal PlayMakerFSM Source = null!;
            internal FsmSuppressor Pause = new FsmSuppressor();
            internal FsmTransition[] Globals = null!;
        }
        private static readonly Dictionary<PlayMakerFSM, OilStartup> OilStarting = new Dictionary<PlayMakerFSM, OilStartup>();
        private static bool _oilStartupReady;

        internal static void InitializeMotorOilStartup()
        {
            if (_oilStartupReady) return;
            try
            {
                var entry = typeof(FsmState).GetMethod(nameof(FsmState.OnEnter), BindingFlags.Public | BindingFlags.Instance,
                    null, Type.EmptyTypes, null) ?? throw new InvalidOperationException("Missing oil startup boundary.");
                new Harmony("com.ourwintercar.wintermp.oil-startup").Patch(entry,
                    prefix: new HarmonyMethod(typeof(ItemWorldSync), nameof(BeforeOilStartup)),
                    postfix: new HarmonyMethod(typeof(ItemWorldSync), nameof(AfterOilStartup)));
                _oilStartupReady = true;
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Motor-oil startup hook unavailable: " + e.Message); }
        }

        private static void BeforeOilStartup(FsmState __instance)
        {
            if (__instance.Name != "Load" || SessionManager.Instance?.IsHost != true) return;
            var c = SyncCatalog.MotorOil;
            var use = __instance.Fsm.Owner as PlayMakerFSM;
            if (c == null || use == null || use.FsmName != c["use"]
                || !FactoryItemIdentity.IsNativeId(use.FsmVariables.FindFsmString(c["id"])?.Value ?? "", c["prefix"])
                || OilStarting.ContainsKey(use)) return;
            try
            {
                var load = PackageStateActions(use, "Load", "LoadFloat", "LoadInt", "LoadTransform", "SetScale", "Wait");
                if (!ReferenceEquals(PackageField<FsmFloat>(load.Actions[0], "loadValue"), OilScalar(use, "Fluid")))
                    throw new InvalidOperationException("Native oil load destination changed.");
                var trigger = use.FsmVariables.FindFsmGameObject("Trigger")?.Value;
                if (trigger == null || trigger.transform.parent != use.transform || trigger.name != c["trigger"])
                    throw new InvalidOperationException("Native oil startup source changed.");
                var source = EngineBlockDataFsm(trigger, c["data"]);
                var pending = new OilStartup { Source = source, Globals = use.Fsm.GlobalTransitions };
                OilStarting.Add(use, pending);
                if (!pending.Pause.Suppress(source)) throw new InvalidOperationException("Cannot pause oil startup writer.");
                if (!source.Fsm.Initialized) source.Fsm.Init(source);
                // Load waits one second. A nearby tilted bottle can otherwise
                // copy the child's default 4 L back and interrupt Load/Get data
                // with its delayed GLOBALEVENT before the native handoff finishes.
                var globals = new List<FsmTransition>();
                foreach (var transition in pending.Globals) if (transition.EventName != "GLOBALEVENT") globals.Add(transition);
                use.Fsm.GlobalTransitions = globals.ToArray();
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Motor-oil startup isolated: " + e.Message); }
        }

        private static void AfterOilStartup(FsmState __instance)
        {
            var c = SyncCatalog.MotorOil;
            if (c == null || (__instance.Name != c["copy"] && __instance.Name != c["empty"])) return;
            var use = __instance.Fsm.Owner as PlayMakerFSM;
            if (use == null || !OilStarting.TryGetValue(use, out var pending)) return;
            try
            {
                if (pending.Source != null)
                {
                    OilScalar(pending.Source, "Fluid").Value = OilScalar(use, "Fluid").Value;
                    OilScalar(pending.Source, "Viscosity").Value = OilScalar(use, "Viscosity").Value;
                }
                use.Fsm.GlobalTransitions = pending.Globals;
                pending.Pause.Restore();
                OilStarting.Remove(use);
                SyncEventLog.Record("motor-oil-loaded", use.FsmVariables.FindFsmString(c["id"]).Value);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Motor-oil startup handoff isolated: " + e.Message); }
        }

        private static void ClearMotorOilStartup()
        {
            // World discovery resets once on entry to GAME, after native Load
            // may already be waiting. Keep those in-flight native handoffs alive.
            if (SessionManager.Instance?.IsHost == true && UnityEngine.Application.loadedLevelName == "GAME") return;
            foreach (var pair in OilStarting)
            {
                if (pair.Key != null) pair.Key.Fsm.GlobalTransitions = pair.Value.Globals;
                pair.Value.Pause.Restore();
            }
            OilStarting.Clear();
        }
    }
}
