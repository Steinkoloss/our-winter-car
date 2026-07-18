using System;
using System.Reflection;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Reads/writes the save's permadeath flag (<c>UniqueTagPlayerPermaDeath</c>, set at
    /// character creation). Guests mirror the host's value for the session (PLAN.md §4.4).
    /// </summary>
    internal static class PermadeathSettings
    {
        private const string Es2Tag = "UniqueTagPlayerPermaDeath";

        private static Type? _es2Type;
        private static MethodInfo? _exists;
        private static MethodInfo? _loadBool;
        private static MethodInfo? _saveBool;
        private static bool _resolved;

        public static bool TryRead(out bool enabled)
        {
            enabled = false;
            if (!EnsureEs2()) return false;

            try
            {
                if (_exists != null)
                {
                    var exists = _exists.Invoke(null, new object[] { Es2Tag });
                    if (exists is bool b && !b) return true;
                }

                if (_loadBool != null)
                {
                    var value = _loadBool.Invoke(null, new object[] { Es2Tag });
                    if (value is bool flag)
                    {
                        enabled = flag;
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("PermadeathSettings: ES2 read failed: " + e.Message);
            }

            return false;
        }

        public static bool TryWrite(bool enabled)
        {
            if (!EnsureEs2()) return false;

            try
            {
                if (_saveBool != null)
                {
                    _saveBool.Invoke(null, new object[] { enabled, Es2Tag });
                    WinterMPPlugin.Log.LogInfo(
                        "PermadeathSettings: wrote " + (enabled ? "ON" : "OFF") + " to save tag.");
                    return true;
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("PermadeathSettings: ES2 write failed: " + e.Message);
            }

            return false;
        }

        /// <summary>Fire Steam achievement bookkeeping events so local UI matches the save flag.</summary>
        public static void SyncAchievementFsm(bool enabled)
        {
            try
            {
                var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null) continue;
                    if (fsm.FsmName != "Achi") continue;

                    string path;
                    try
                    {
                        path = ScenePath.Of(fsm.transform);
                    }
                    catch
                    {
                        continue;
                    }

                    if (path.IndexOf("Systems/Steam", StringComparison.OrdinalIgnoreCase) < 0
                        && path.IndexOf("/Steam", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    fsm.SendEvent(enabled ? "_DEATHON" : "_DEATHOFF");
                    return;
                }
            }
            catch
            {
                // PlayMaker not ready.
            }
        }

        private static bool EnsureEs2()
        {
            if (_resolved) return _es2Type != null;
            _resolved = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "ES2") continue;
                _es2Type = assembly.GetType("ES2");
                break;
            }

            if (_es2Type == null) return false;

            _exists = _es2Type.GetMethod(
                "Exists",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            MethodInfo? loadGeneric = _es2Type.GetMethod(
                "Load",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            if (loadGeneric != null && loadGeneric.IsGenericMethodDefinition)
                _loadBool = loadGeneric.MakeGenericMethod(typeof(bool));

            MethodInfo? saveGeneric = _es2Type.GetMethod(
                "Save",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool), typeof(string) },
                null);
            if (saveGeneric != null && saveGeneric.IsGenericMethodDefinition)
                _saveBool = saveGeneric.MakeGenericMethod(typeof(bool));

            return _loadBool != null && _saveBool != null;
        }
    }
}
