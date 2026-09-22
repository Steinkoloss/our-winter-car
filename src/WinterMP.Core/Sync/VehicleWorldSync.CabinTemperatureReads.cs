using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private static readonly Dictionary<FsmStateAction, NativeCabinTemperatureInputs> CabinTemperatureReaders = new Dictionary<FsmStateAction, NativeCabinTemperatureInputs>();
        private static readonly HashSet<Type> CabinTemperatureHookTypes = new HashSet<Type>();

        private static void EnsureCabinTemperatureHooks(NativeCabinTemperatureInputs b)
        {
            foreach (var action in new[] { b.CabinActions[4], b.HeaterActions[0] })
            {
                var type = action.GetType(); if (CabinTemperatureHookTypes.Contains(type)) continue;
                bool coolant = ReferenceEquals(action, b.HeaterActions[0]);
                var method = type.GetMethod(coolant ? "DoGetFsmFloat" : "DoFloatOperator",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native cabin thermal boundary changed.");
                var harmony = new Harmony("com.ourwintercar.wintermp.cabin-temperature-inputs");
                if (coolant) harmony.Patch(method, postfix: new HarmonyMethod(typeof(NativeCabinTemperatureHooks), nameof(NativeCabinTemperatureHooks.AfterCoolant)));
                else harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(NativeCabinTemperatureHooks), nameof(NativeCabinTemperatureHooks.BeforeEngine)),
                    finalizer: new HarmonyMethod(typeof(NativeCabinTemperatureHooks), nameof(NativeCabinTemperatureHooks.AfterEngine)));
                CabinTemperatureHookTypes.Add(type);
            }
        }

        // Harmony __state must remain separate from the engine, heat and wear hooks.
        private static class NativeCabinTemperatureHooks
        {
            private static bool Guest => SessionManager.Instance != null && !SessionManager.Instance.IsHost
                && SessionManager.Instance.State == SessionState.Connected;

            private static float Degrees(NativeCabinTemperatureInputs b, bool engine)
            {
                var state = b.Item.HostCoolant;
                return state != null && state.Valid && state.VehicleId == b.Item.Id && state.Flags == VehicleCoolantState.Available
                    ? engine ? state.EngineCelsius : state.Celsius : 0f;
            }

            internal static void BeforeEngine(FsmStateAction __instance, out NativeCabinTemperatureInputs? __state)
            {
                __state = null;
                if (!Guest || !CabinTemperatureReaders.TryGetValue(__instance, out var b)) return;
                try
                {
                    ValidateCabinTemperatureInputs(b);
                    b.Input.Value = Degrees(b, true); b.Depth++; __state = b; b.Operand.SetValue(__instance, b.Input);
                }
                catch (Exception error)
                {
                    if (__state != null) try { Restore(__state); } catch (Exception restore) { NoteCabinTemperatureFailure(b.Item, restore); }
                    __state = null; ClearCabinTemperatureInputs(b.Item); NoteCabinTemperatureFailure(b.Item, error);
                }
            }

            internal static Exception? AfterEngine(Exception? __exception, NativeCabinTemperatureInputs? __state)
            {
                if (__state != null)
                    try { Restore(__state); }
                    catch (Exception error) { ClearCabinTemperatureInputs(__state.Item); NoteCabinTemperatureFailure(__state.Item, error); }
                return __exception;
            }

            private static void Restore(NativeCabinTemperatureInputs b)
            {
                if (b.Depth > 0) b.Depth--;
                if (b.Depth == 0) b.Operand.SetValue(b.CabinActions[4], b.Engine);
            }

            internal static void AfterCoolant(FsmStateAction __instance)
            {
                if (!Guest || !CabinTemperatureReaders.TryGetValue(__instance, out var b)) return;
                try
                {
                    ValidateCabinTemperatureInputs(b);
                    // Heater scratch CoolantTemp is divided later in this state;
                    // replace the read result, never the native Cooling source.
                    b.Output.Value = Degrees(b, false);
                }
                catch (Exception error) { ClearCabinTemperatureInputs(b.Item); NoteCabinTemperatureFailure(b.Item, error); }
            }
        }
    }
}
