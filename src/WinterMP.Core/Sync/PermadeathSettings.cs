using System;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    /// <summary>Reads the host's native setting; guests use session state without writing personal saves.</summary>
    internal static class PermadeathSettings
    {
        private const string Variable = "PlayerPermaDeath";
        private static bool _attempted, _ready;
        private static FieldInfo? _loadValue, _loadTag, _saveValue, _saveTag;

        internal static bool Initialize()
        {
            if (_attempted) return _ready;
            _attempted = true;
            try
            {
                var assembly = Assembly.Load("Assembly-CSharp");
                var load = assembly.GetType("HutongGames.PlayMaker.Actions.LoadBool", true);
                var save = assembly.GetType("HutongGames.PlayMaker.Actions.SaveBool", true);
                _loadValue = Field(load, "loadValue", typeof(FsmBool));
                _loadTag = Field(load, "uniqueTag", typeof(FsmString));
                _saveValue = Field(save, "saveValue", typeof(FsmBool));
                _saveTag = Field(save, "uniqueTag", typeof(FsmString));
                var harmony = new Harmony("com.ourwintercar.wintermp.death-settings");
                harmony.Patch(load.GetMethod("OnEnter", Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(PermadeathSettings), nameof(BeforeLoad)),
                    postfix: new HarmonyMethod(typeof(PermadeathSettings), nameof(AfterLoad)));
                harmony.Patch(save.GetMethod("OnEnter", Type.EmptyTypes),
                    postfix: new HarmonyMethod(typeof(PermadeathSettings), nameof(AfterSave)));
                _ready = true;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError("PermadeathSettings: native binding unavailable: " + e);
            }
            return _ready;
        }

        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = type.GetField(name);
            if (field == null || field.FieldType != expected)
                throw new InvalidOperationException("Native death setting field changed: " + type.Name + "." + name);
            return field;
        }

        public static bool TryRead(out bool enabled)
        {
            enabled = false;
            try
            {
                var globals = FsmVariables.GlobalVariables;
                var live = globals.FindFsmBool(Variable);
                if (Application.loadedLevelName == "GAME" && live != null)
                {
                    enabled = live.Value;
                    return true;
                }
                // Native Continue uses SavePlayerData + ?tag=PlayerPermaDeath.
                // A missing save is a new-character flow, not proof of normal death.
                string file = globals.FindFsmString("SavePlayerData")?.Value ?? "savefile.txt";
                string path = file + "?tag=" + Variable;
                if (!ES2.Exists(path)) return false;
                enabled = ES2.Load<bool>(path);
                return true;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("PermadeathSettings: native read failed: " + e.Message);
                return false;
            }
        }

        internal static void ApplyGuest(bool enabled)
        {
            var value = FsmVariables.GlobalVariables.FindFsmBool(Variable);
            if (value == null || value.Value == enabled) return;
            value.Value = enabled;
            WinterMPPlugin.Log.LogInfo("PermadeathSettings: applied host " + (enabled ? "PERMADEATH" : "normal death") + " to the running guest.");
        }

        private static FsmBool? Target(FsmStateAction action, FieldInfo? valueField, FieldInfo? tagField)
        {
            var value = valueField?.GetValue(action) as FsmBool;
            if (value == null || value.Name != Variable) return null;
            var tag = tagField?.GetValue(action) as FsmString;
            return tag != null && tag.Value == Variable
                && ReferenceEquals(value, FsmVariables.GlobalVariables.FindFsmBool(Variable)) ? value : null;
        }

        private static bool BeforeLoad(FsmStateAction __instance)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return true;
            try
            {
                var target = Target(__instance, _loadValue, _loadTag);
                if (target == null) return true;
                // Override before Finish can advance native startup/achievement/death
                // readers. A per-frame correction would leave a wrong-branch window.
                target.Value = session.PermanentDeathEnabled;
                __instance.Finish();
                return false;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError("PermadeathSettings: guest load override failed: " + e);
                return true;
            }
        }

        private static void AfterLoad(FsmStateAction __instance) => ObserveHost(__instance, _loadValue, _loadTag);
        private static void AfterSave(FsmStateAction __instance) => ObserveHost(__instance, _saveValue, _saveTag);

        private static void ObserveHost(FsmStateAction action, FieldInfo? valueField, FieldInfo? tagField)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            try
            {
                var value = Target(action, valueField, tagField);
                if (value != null) session.SetPermanentDeathEnabled(value.Value);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("PermadeathSettings: host observation failed: " + e.Message); }
        }
    }
}
