using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using WinterMP.Net;

namespace WinterMP.Core.Session
{
    internal static class GuestSaveGuard
    {
        private const string HarmonyId = "com.ourwintercar.wintermp.guest-save";
        private static readonly GuestSavePolicy Policy = new GuestSavePolicy();
        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);
        private static bool _attempted;
        private static bool _ready;

        internal static bool ProtectWorld { get { return Policy.ProtectWorld; } }
        internal static bool CanHost { get { return Policy.CanHost; } }
        internal const string RestartToHost = "Restart the game to host your own save after a guest session.";
        internal const string Unavailable = "Cannot join: local save protection could not start. See the game log.";

        internal static bool TryBeginGuest()
        {
            Initialize();
            bool alreadyProtected = ProtectWorld;
            if (!Policy.TryBeginGuest(_ready)) return false;
            if (!alreadyProtected)
                WinterMPPlugin.Log.LogInfo("GuestSaveGuard: local world saves protected until game restart.");
            return true;
        }

        internal static void Initialize()
        {
            try { EnsureInstalled(); }
            catch (Exception e)
            {
                // Assembly/JIT failures can happen before EnsureInstalled's body.
                _attempted = true;
                _ready = false;
                WinterMPPlugin.Log.LogError("GuestSaveGuard: joining disabled; save library unavailable: " + e);
            }
        }

        private static void EnsureInstalled()
        {
            if (_attempted) return;
            _attempted = true;
            try
            {
                Assembly es2 = typeof(ES2Writer).Assembly;
                Type fileStream = es2.GetType("ES2FileStream", true);
                Type prefsStream = es2.GetType("ES2PlayerPrefsStream", true);
                var harmony = new Harmony(HarmonyId);
                Patch(harmony, typeof(ES2Writer), "Save", new[] { typeof(bool) }, nameof(BeforeSave));
                Patch(harmony, fileStream, "CreateWriteStream", Type.EmptyTypes, nameof(BeforeWriteStream));
                Patch(harmony, fileStream, "Store", Type.EmptyTypes, nameof(BeforeMutation));
                Patch(harmony, prefsStream, "Store", Type.EmptyTypes, nameof(BeforeMutation));
                Patch(harmony, typeof(ES2File), "Delete", new[] { typeof(ES2Settings) }, nameof(BeforeMutation));
                Patch(harmony, typeof(ES2File), "DeleteFile", new[] { typeof(ES2Settings) }, nameof(BeforeMutation));
                Patch(harmony, typeof(ES2File), "Rename", new[] { typeof(ES2Settings), typeof(ES2Settings) }, nameof(BeforeMutation));
                Patch(harmony, typeof(ES2File), "MoveFile", new[] { typeof(ES2Settings), typeof(ES2Settings) }, nameof(BeforeMutation));
                Patch(harmony, typeof(ES2), "DeleteDefaultFolder", Type.EmptyTypes, nameof(BeforeMutation));
                _ready = true;
                WinterMPPlugin.Log.LogInfo("GuestSaveGuard: all 9 ES2 persistence guards installed.");
            }
            catch (Exception e)
            {
                // Keep any installed prefixes; none activates unless all guards
                // succeeded. A failed binding prevents guest admission, not hosting.
                WinterMPPlugin.Log.LogError("GuestSaveGuard: joining disabled; persistence binding failed: " + e);
            }
        }

        private static void Patch(Harmony harmony, Type type, string name, Type[] arguments, string prefix)
        {
            MethodInfo? target = type.GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                null, arguments, null);
            if (target == null || target.IsGenericMethod || target.GetMethodBody() == null)
                throw new InvalidOperationException("Missing managed save boundary: " + type.FullName + "." + name);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(GuestSaveGuard), prefix));
        }

        private static bool BeforeSave(ES2Writer __instance)
        {
            if (Policy.CanPersist(__instance.settings != null
                && __instance.settings.saveLocation == ES2Settings.SaveLocation.Memory)) return true;
            ReportBlocked("writer-save");
            return false;
        }

        private static bool BeforeWriteStream(ref Stream __result)
        {
            if (!ProtectWorld) return true;
            // LowMemory opens/truncates <save>tmp before Save(). Give the native
            // serializer a disposable buffer without changing shared ES2 settings.
            __result = new MemoryStream();
            return false;
        }

        private static bool BeforeMutation(MethodBase __originalMethod)
        {
            if (!ProtectWorld) return true;
            ReportBlocked(__originalMethod.DeclaringType.Name + "." + __originalMethod.Name);
            return false;
        }

        private static void ReportBlocked(string operation)
        {
            try
            {
                // A vanilla SAVE broadcast can invoke thousands of writes. Keep
                // one diagnostic per boundary for the entire protected process.
                if (Reported.Add(operation))
                    WinterMPPlugin.Log.LogInfo("GuestSaveGuard: suppressed " + operation + "; local save preserved.");
            }
            catch { /* Logging must never reopen the persistence boundary. */ }
        }
    }
}
