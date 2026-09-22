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
        private static readonly Dictionary<FsmStateAction, NativeEngineTemperatureRead> EngineTemperatureReaders = new Dictionary<FsmStateAction, NativeEngineTemperatureRead>();
        private static readonly HashSet<Type> EngineTemperatureHookTypes = new HashSet<Type>();

        private static void EnsureEngineTemperatureHooks(NativeEngineTemperatureInputs b)
        {
            foreach (var read in b.Reads)
            {
                var type = read.Action.GetType(); if (EngineTemperatureHookTypes.Contains(type)) continue;
                string[] names = read.Type == "SetFloatValue" ? new[] { "OnEnter", "OnUpdate" }
                    : new[] { read.Type == "FloatClamp" ? "DoClamp" : read.Type == "FloatCompare" ? "DoCompare" : "DoFloatOperator" };
                foreach (string name in names)
                {
                    var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        null, Type.EmptyTypes, null);
                    if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                        throw new InvalidOperationException("Native engine temperature read boundary changed.");
                    new Harmony("com.ourwintercar.wintermp.engine-temperature-inputs").Patch(method,
                        prefix: new HarmonyMethod(typeof(NativeEngineTemperatureHooks), nameof(NativeEngineTemperatureHooks.Before)),
                        finalizer: new HarmonyMethod(typeof(NativeEngineTemperatureHooks), nameof(NativeEngineTemperatureHooks.After)));
                }
                EngineTemperatureHookTypes.Add(type);
            }
        }

        // Separate Harmony state from the existing host RPM/torque read hooks.
        private static class NativeEngineTemperatureHooks
        {
            internal static void Before(FsmStateAction __instance, out NativeEngineTemperatureRead? __state)
            {
                __state = null;
                if (!EngineTemperatureReaders.TryGetValue(__instance, out var read)) return;
                var session = SessionManager.Instance;
                if (session == null || session.IsHost || session.State != SessionState.Connected) return;
                try
                {
                    if (read.Owner.Electrical) ValidateElectricalTemperatureInputs(read.Owner);
                    else ValidateEngineTemperatureInputs(read.Owner);
                    var state = read.Owner.Item.HostCoolant;
                    bool ready = state != null && state.Valid && state.VehicleId == read.Owner.Item.Id && state.Flags == VehicleCoolantState.Available;
                    read.Input.Value = ready ? state!.EngineCelsius : 0;
                    read.Depth++; __state = read; read.Field.SetValue(__instance, read.Input);
                }
                catch (Exception error)
                {
                    if (__state != null)
                        try { RestoreEngineTemperatureRead(__state); } catch (Exception restore) { NoteTemperatureGroupFailure(read.Owner, restore); }
                    __state = null; ClearTemperatureGroup(read.Owner); NoteTemperatureGroupFailure(read.Owner, error);
                }
            }

            internal static Exception? After(Exception? __exception, NativeEngineTemperatureRead? __state)
            {
                if (__state != null)
                    try { RestoreEngineTemperatureRead(__state); }
                    catch (Exception error) { ClearTemperatureGroup(__state.Owner); NoteTemperatureGroupFailure(__state.Owner, error); }
                return __exception;
            }
        }

        private static void RestoreEngineTemperatureRead(NativeEngineTemperatureRead read)
        {
            if (--read.Depth == 0 && ReferenceEquals(read.Field.GetValue(read.Action), read.Input))
                read.Field.SetValue(read.Action, read.Original);
        }
    }
}
