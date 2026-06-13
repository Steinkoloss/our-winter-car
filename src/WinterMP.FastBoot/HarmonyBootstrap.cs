using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WinterMP.FastBoot
{
    internal static class HarmonyBootstrap
    {
        private const string HarmonyId = "com.ourwintercar.wintermp.fastboot.pipeline";

        public static bool Apply(ManualLogSource log)
        {
            FastBootLog.Host = log;
            try
            {
                var harmony = new Harmony(HarmonyId);
                PatchApplicationLoadLevel(harmony, log);
                TryPatchEs2(harmony, log);
                log.LogInfo("FastBoot: load pipeline Harmony patches applied.");
                return true;
            }
            catch (Exception e)
            {
                log.LogWarning("FastBoot: Harmony patches not applied: " + e.Message);
                return false;
            }
        }

        private static void PatchApplicationLoadLevel(Harmony harmony, ManualLogSource log)
        {
            MethodInfo? loadInt = AccessTools.Method(typeof(Application), "LoadLevel", new[] { typeof(int) });
            MethodInfo? loadString = AccessTools.Method(typeof(Application), "LoadLevel", new[] { typeof(string) });
            if (loadInt == null || loadString == null)
            {
                log.LogWarning("FastBoot: Application.LoadLevel not found.");
                return;
            }

            harmony.Patch(
                loadInt,
                prefix: new HarmonyMethod(typeof(ApplicationLoadLevelPatches), nameof(ApplicationLoadLevelPatches.PrefixInt)),
                postfix: new HarmonyMethod(typeof(ApplicationLoadLevelPatches), nameof(ApplicationLoadLevelPatches.PostfixInt)));

            harmony.Patch(
                loadString,
                prefix: new HarmonyMethod(typeof(ApplicationLoadLevelPatches), nameof(ApplicationLoadLevelPatches.PrefixString)));

            log.LogInfo("FastBoot: patched Application.LoadLevel.");
        }

        private static void TryPatchEs2(Harmony harmony, ManualLogSource log)
        {
            Assembly? es2Asm = FindEs2Assembly();
            if (es2Asm == null)
            {
                log.LogWarning("FastBoot: ES2.dll not loaded — ES2 patches skipped.");
                return;
            }

            Type? reader = es2Asm.GetType("ES2Reader");
            Type? es2 = es2Asm.GetType("ES2");
            if (reader == null || es2 == null) return;

            PatchEs2ReaderInstance(harmony, reader);
            PatchEs2Static(harmony, es2, log);
        }

        private static void PatchEs2ReaderInstance(Harmony harmony, Type reader)
        {
            MethodInfo? tagExists = reader.GetMethod(
                "TagExists",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            if (tagExists != null)
            {
                harmony.Patch(
                    tagExists,
                    prefix: new HarmonyMethod(typeof(Es2TagExistsPatch), nameof(Es2TagExistsPatch.Prefix)));
            }

            MethodInfo? getTags = reader.GetMethod(
                "GetTags",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (getTags != null)
            {
                harmony.Patch(
                    getTags,
                    postfix: new HarmonyMethod(typeof(Es2GetTagsPatch), nameof(Es2GetTagsPatch.Postfix)));
            }
        }

        private static void PatchEs2Static(Harmony harmony, Type es2, ManualLogSource log)
        {
            int existsPatches = 0;

            foreach (MethodInfo method in es2.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (string.Equals(method.Name, "Exists", StringComparison.Ordinal))
                {
                    ParameterInfo[] ps = method.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                    {
                        harmony.Patch(
                            method,
                            prefix: new HarmonyMethod(typeof(Es2StaticExistsPatch), nameof(Es2StaticExistsPatch.Prefix)));
                        existsPatches++;
                    }

                    continue;
                }

                if (!string.Equals(method.Name, "LoadAll", StringComparison.Ordinal)) continue;

                ParameterInfo[] loadAllPs = method.GetParameters();
                if (loadAllPs.Length < 1 || loadAllPs[0].ParameterType != typeof(string)) continue;

                harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(typeof(Es2HydrateWindowPatch), nameof(Es2HydrateWindowPatch.LoadAllPrefix)),
                    postfix: new HarmonyMethod(typeof(Es2HydrateWindowPatch), nameof(Es2HydrateWindowPatch.LoadAllPostfix)));
            }

            int loadPatches = Es2GenericLoadPatcher.Patch(harmony, es2, log);

            log.LogInfo(
                "FastBoot: patched ES2Reader TagExists/GetTags; ES2 Exists x"
                + existsPatches
                + ", Load* x"
                + loadPatches
                + ", LoadAll hydrate window.");
        }

        private static Assembly? FindEs2Assembly()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(asm.GetName().Name, "ES2", StringComparison.OrdinalIgnoreCase))
                    return asm;
            }

            return null;
        }
    }

    internal static class ApplicationLoadLevelPatches
    {
        public static bool PrefixInt(int index)
        {
            if (index != LoadPipeline.GameLevelIndex)
            {
                if (LoadPipeline.HasActivePreload)
                    LoadPipeline.AbandonPreload();
                return true;
            }

            return !LoadPipeline.TrySkipSyncLoadLevel(index);
        }

        public static void PostfixInt(int index)
        {
            if (LoadPipeline.LogTimings && index == LoadPipeline.GameLevelIndex)
            {
                FastBootLog.Host.LogInfo(
                    "FastBoot: Application.LoadLevel("
                    + index
                    + ") returned at "
                    + Time.realtimeSinceStartup.ToString("0.0")
                    + "s.");
            }
        }

        public static bool PrefixString(string name)
        {
            if (!string.Equals(name, BootPhase.Game, StringComparison.Ordinal))
            {
                if (LoadPipeline.HasActivePreload)
                    LoadPipeline.AbandonPreload();
                return true;
            }

            return !LoadPipeline.TrySkipSyncLoadLevel(LoadPipeline.GameLevelIndex);
        }
    }

    internal static class Es2HydrateWindowPatch
    {
        public static void LoadAllPrefix()
        {
            Es2HydratePolicy.BeginContinueHydrate();
            LoadPipeline.NoteEs2OpenBegin();
        }

        public static void LoadAllPostfix()
        {
            LoadPipeline.NoteEs2OpenEnd();
            if (LoadPipeline.LogTimings)
            {
                FastBootLog.Host.LogInfo(
                    "FastBoot: ES2.LoadAll finished at "
                    + Time.realtimeSinceStartup.ToString("0.0")
                    + "s ("
                    + LoadPipeline.Es2OpenCount
                    + " hydrate pass).");
            }
        }
    }

    internal static class Es2TagExistsPatch
    {
        public static bool Prefix(string tag, ref bool __result)
        {
            return !Es2HydratePolicy.TryShortCircuitTag(tag, out __result);
        }
    }

    internal static class Es2StaticExistsPatch
    {
        public static bool Prefix(string identifier, ref bool __result)
        {
            return !Es2HydratePolicy.TryShortCircuitTag(identifier, out __result);
        }
    }

    internal static class Es2GetTagsPatch
    {
        public static void Postfix(ref string[] __result)
        {
            if (!Es2HydratePolicy.ShouldApply) return;
            __result = Es2HydratePolicy.FilterTags(__result);
        }
    }

    internal static class FastBootLog
    {
        public static ManualLogSource Host = BepInEx.Logging.Logger.CreateLogSource("WinterMP FastBoot");
    }
}
