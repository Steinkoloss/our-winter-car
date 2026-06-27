using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// After GAME loads, calls ES2.LoadAll for the save in the background. INTENDED to re-hydrate
    /// tags skipped by the boot whitelist — but note ES2.LoadAll only returns an ES2Data dictionary
    /// and pushes nothing back into the live world (the game's per-object PlayMaker loaders already
    /// ran and took their default branch). So this is NOT a safety net for the ES2 skip and cannot
    /// undo a skipped tag. Dormant by default; only runs when the unsafe ES2 whitelist is opted in.
    /// </summary>
    internal static class Es2DeferredHydrator
    {
        private const string SaveFileName = "savefile.txt";

        public static bool Enabled { get; set; } = true;
        public static float DelaySeconds { get; set; } = 3f;

        public static IEnumerator Run(ManualLogSource log, bool whitelistWasActive)
        {
            if (!Enabled || !whitelistWasActive)
                yield break;

            if (!SessionGate.ShouldRunDeferredHydrate())
            {
                if (LoadPipeline.LogTimings)
                    log.LogInfo("FastBoot: deferred ES2 hydrate skipped (MP guest — host snapshot owns world).");
                yield break;
            }

            if (DelaySeconds > 0f)
                yield return new WaitForSeconds(DelaySeconds);

            string savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            if (!File.Exists(savePath))
            {
                log.LogWarning("FastBoot: deferred ES2 hydrate skipped — savefile missing.");
                yield break;
            }

            float startedAt = Time.realtimeSinceStartup;
            Es2HydratePolicy.BeginDeferredHydrate();
            try
            {
                if (!Es2LoadInvoker.TryLoadAll(savePath, log))
                    log.LogWarning("FastBoot: deferred ES2 hydrate could not invoke ES2.LoadAll.");
            }
            catch (Exception e)
            {
                log.LogWarning("FastBoot: deferred ES2 hydrate failed: " + e.Message);
            }
            finally
            {
                Es2HydratePolicy.EndContinueHydrate();
                Es2HydratePolicy.EndDeferredHydrate();
            }

            if (LoadPipeline.LogTimings)
            {
                log.LogInfo(
                    "FastBoot: deferred ES2 hydrate finished at "
                    + Time.realtimeSinceStartup.ToString("0.0")
                    + "s ("
                    + (Time.realtimeSinceStartup - startedAt).ToString("0.0")
                    + "s).");
            }

            yield return null;
        }
    }

    internal static class Es2LoadInvoker
    {
        public static bool TryLoadAll(string savePath, ManualLogSource log)
        {
            Assembly? es2Asm = FindEs2Assembly();
            if (es2Asm == null) return false;

            Type? es2 = es2Asm.GetType("ES2");
            if (es2 == null) return false;

            MethodInfo? loadAll = FindLoadAll(es2);
            if (loadAll == null) return false;

            string[] identifiers = BuildIdentifiers(savePath);
            for (int i = 0; i < identifiers.Length; i++)
            {
                try
                {
                    loadAll.Invoke(null, new object[] { identifiers[i] });
                    if (LoadPipeline.LogTimings)
                    {
                        log.LogInfo(
                            "FastBoot: deferred ES2.LoadAll(\""
                            + identifiers[i]
                            + "\") completed.");
                    }

                    return true;
                }
                catch (Exception e)
                {
                    if (LoadPipeline.LogTimings)
                    {
                        log.LogInfo(
                            "FastBoot: deferred ES2.LoadAll(\""
                            + identifiers[i]
                            + "\") failed: "
                            + e.Message);
                    }
                }
            }

            return false;
        }

        private static string[] BuildIdentifiers(string savePath)
        {
            string fileName = Path.GetFileName(savePath);
            string withoutExt = Path.GetFileNameWithoutExtension(savePath);
            return new[]
            {
                savePath,
                fileName,
                withoutExt,
            };
        }

        private static MethodInfo? FindLoadAll(Type es2)
        {
            foreach (MethodInfo method in es2.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "LoadAll", StringComparison.Ordinal))
                    continue;

                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    return method;
            }

            return null;
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
}
