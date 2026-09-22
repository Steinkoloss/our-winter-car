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
        private static readonly Dictionary<FsmStateAction, NativeCoolingRead> CoolingReaders = new Dictionary<FsmStateAction, NativeCoolingRead>();
        private static readonly HashSet<Type> CoolingHookTypes = new HashSet<Type>();

        private static void EnsureCoolingHooks(NativeCoolingInputs b)
        {
            foreach (var read in b.Reads)
            {
                var type = read.Action.GetType(); if (CoolingHookTypes.Contains(type)) continue;
                var method = type.GetMethod(type.Name == "FloatCompare" ? "DoCompare" : "DoFloatOperator",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native cooling speed read boundary unavailable.");
                new Harmony("com.ourwintercar.wintermp.cooling-inputs").Patch(method,
                    prefix: new HarmonyMethod(typeof(NativeCoolingHooks), nameof(NativeCoolingHooks.BeforeCoolingRead)),
                    finalizer: new HarmonyMethod(typeof(NativeCoolingHooks), nameof(NativeCoolingHooks.AfterCoolingRead)));
                CoolingHookTypes.Add(type);
            }
        }

        // A distinct declaring type keeps Harmony __state separate from heat/wear.
        private static class NativeCoolingHooks
        {
            internal static void BeforeCoolingRead(FsmStateAction __instance, out NativeCoolingRead? __state)
            {
                __state = null;
                if (!CoolingReaders.TryGetValue(__instance, out var read)) return;
                var b = read.Owner; var item = b.Item;
                try
                {
                    var session = SessionManager.Instance; var world = WorldSyncManager.Instance;
                    bool host = session != null && session.IsHost && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
                    if (!VehicleWearSimulationPolicy.Delegated(host, GuestSaveGuard.ProtectWorld,
                        item.LocallyOwned || world == null || world.IsLocalPlayerDriving(item), item.RemoteOwner)) return;
                    ValidateCoolingInputs(b);
                    var sample = item.AcceptedVehicleState;
                    bool ready = VehicleWearSimulationPolicy.HasSample(sample, item.Id, item.RemoteOwner, Time.unscaledTime < item.RemoteEngineUntil)
                        && (read.Rpm || sample!.MovementSpeedAvailable);
                    read.Input.Value = ready ? (read.Rpm ? sample!.Rpm : sample!.MovementSpeedTenthsKmh * .1f) : 0f;
                    read.Depth++; __state = read; read.Field.SetValue(__instance, read.Input);
                }
                catch (Exception error)
                {
                    if (__state != null)
                        try { RestoreCoolingRead(__state); } catch (Exception restoreError) { NoteCoolingInputFailure(item, restoreError); }
                    __state = null; ClearCoolingInputs(item); NoteCoolingInputFailure(item, error);
                }
            }

            internal static Exception? AfterCoolingRead(Exception? __exception, NativeCoolingRead? __state)
            {
                if (__state != null)
                    try { RestoreCoolingRead(__state); }
                    catch (Exception error) { ClearCoolingInputs(__state.Owner.Item); NoteCoolingInputFailure(__state.Owner.Item, error); }
                return __exception;
            }
        }

        private static void RestoreCoolingRead(NativeCoolingRead read)
        {
            if (--read.Depth == 0 && ReferenceEquals(read.Field.GetValue(read.Action), read.Input))
                read.Field.SetValue(read.Action, read.Original);
        }
    }
}
