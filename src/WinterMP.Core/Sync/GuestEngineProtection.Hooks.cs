using System;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private const string EntryHookHarmonyId = "com.ourwintercar.wintermp.guest-engine";
        private static bool _entryHookAttempted;
        private static bool _entryHookReady;

        internal static bool Initialize()
        {
            if (_entryHookAttempted) return _entryHookReady;
            _entryHookAttempted = true;
            try
            {
                MethodInfo? target = typeof(FsmState).GetMethod(nameof(FsmState.OnEnter),
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (target == null || target.IsGenericMethod || target.GetMethodBody() == null)
                    throw new InvalidOperationException("Missing managed PlayMaker state-entry boundary.");

                var harmony = new Harmony(EntryHookHarmonyId);
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(GuestEngineProtection), nameof(BeforeStateEnter)));
                InitializeExternalFloatHooks(harmony);
                _entryHookReady = true;
                return true;
            }
            catch (Exception e)
            {
                try { NoteFailure("state-entry-hook", e); }
                catch { /* A diagnostic failure must not permit guest admission. */ }
                return false;
            }
        }

        private static bool BeforeStateEnter(FsmState __instance)
        {
            if (!GuestSaveGuard.ProtectWorld) return true;
            PlayMakerFSM? owner = null;
            try
            {
                if (__instance == null || !__instance.IsInitialized) return true;
                owner = __instance.Fsm.Owner as PlayMakerFSM;
                return owner == null || GuardStateEntry(owner, __instance);
            }
            catch (Exception e)
            {
                try { NoteFailure("state-entry", e); }
                catch { /* Logging must not escape into native state dispatch. */ }
                try { return owner == null || !IsProtectedFsm(owner); }
                catch { return true; }
            }
        }

        private static void InitializeEngineInputReads(Harmony harmony, Assembly assembly)
        {
            foreach (string name in new[] { "GetFsmBool", "GetFsmFloat", "GetFsmInt", "GetFsmString" })
            {
                var type = assembly.GetType("HutongGames.PlayMaker.Actions." + name, true);
                var method = type.GetMethod("Do" + name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (!typeof(FsmStateAction).IsAssignableFrom(type) || method == null
                    || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native engine input read boundary changed: " + name);
                // Refresh before another projection hook resolves the reader's
                // target; a rejected read must also skip those later prefixes.
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GuestEngineProtection), nameof(BeforeEngineInputRead))
                    { priority = Priority.First });
            }
        }

        private static bool BeforeEngineInputRead(FsmStateAction __instance)
        {
            if (!GuestSaveGuard.ProtectWorld || !(PrepareInputs?.Target is ItemWorldSync inputs)) return true;
            return inputs.PrepareGuestEngineInputRead(__instance);
        }
    }
}
