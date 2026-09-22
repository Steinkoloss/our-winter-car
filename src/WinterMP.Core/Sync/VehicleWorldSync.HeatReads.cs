using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private static readonly Dictionary<FsmStateAction, NativeHeatRead> HeatReaders = new Dictionary<FsmStateAction, NativeHeatRead>();
        private static readonly HashSet<Type> HeatHookTypes = new HashSet<Type>();

        private static void EnsureHeatHooks(NativeHeatBinding binding)
        {
            foreach (var read in binding.Reads)
            {
                Type type = read.Action.GetType(); if (HeatHookTypes.Contains(type)) continue;
                string name = type.Name == "FloatCompare" ? "DoCompare" : "DoFloatOperator";
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native heat read boundary unavailable.");
                new Harmony("com.ourwintercar.wintermp.heat-inputs").Patch(method,
                    prefix: new HarmonyMethod(typeof(NativeHeatHooks), nameof(NativeHeatHooks.BeforeHeatRead)),
                    finalizer: new HarmonyMethod(typeof(NativeHeatHooks), nameof(NativeHeatHooks.AfterHeatRead)));
                HeatHookTypes.Add(type);
            }
        }

        // Harmony keys __state by the patch's declaring type. Keep this separate
        // from the wear prefix/finalizer on the same native FloatOperator helper.
        private static class NativeHeatHooks
        {
            internal static void BeforeHeatRead(FsmStateAction __instance, out NativeHeatRead? __state)
            {
                __state = null;
                if (!HeatReaders.TryGetValue(__instance, out var read)) return;
                var b = read.Owner; var item = b.Item;
                try
                {
                    var session = SessionManager.Instance; var world = WorldSyncManager.Instance;
                    bool host = session != null && session.IsHost && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
                    if (!VehicleWearSimulationPolicy.Delegated(host, GuestSaveGuard.ProtectWorld,
                        item.LocallyOwned || world == null || world.IsLocalPlayerDriving(item), item.RemoteOwner)) return;
                    ValidateHeatBinding(b);
                    var sample = item.AcceptedVehicleState;
                    bool ready = VehicleWearSimulationPolicy.HasSample(sample, item.Id, item.RemoteOwner, Time.unscaledTime < item.RemoteEngineUntil)
                        && sample!.TorqueAvailable;
                    // An incomplete sample drives the native stop threshold. It cannot
                    // combine another driver's torque with the current owner's RPM.
                    read.Input.Value = ready ? (read.Torque ? sample!.EngineTorque : sample!.Rpm) : 0f;
                    read.Depth++; __state = read;
                    foreach (var field in read.Fields) field.SetValue(__instance, read.Input);
                }
                catch (Exception error)
                {
                    if (__state != null)
                        try { RestoreHeatRead(__state); }
                        catch (Exception restoreError) { NoteHeatFailure(item, restoreError); }
                    __state = null; ClearHeatBinding(item); NoteHeatFailure(item, error);
                }
            }

            internal static Exception? AfterHeatRead(Exception? __exception, NativeHeatRead? __state)
            {
                if (__state != null)
                    try { RestoreHeatRead(__state); }
                    catch (Exception error) { ClearHeatBinding(__state.Owner.Item); NoteHeatFailure(__state.Owner.Item, error); }
                return __exception;
            }

        }

        private static void RestoreHeatRead(NativeHeatRead read)
        {
            // FloatCompare can enter a new native state before its helper returns.
            // A nested read must leave the outer call's owned operands intact.
            if (--read.Depth != 0) return;
            foreach (var field in read.Fields)
                if (ReferenceEquals(field.GetValue(read.Action), read.Input)) field.SetValue(read.Action, read.Original);
        }
    }
}
