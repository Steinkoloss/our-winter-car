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
        private static readonly Dictionary<FsmStateAction, NativeElectricalRead> ElectricalReaders = new Dictionary<FsmStateAction, NativeElectricalRead>();
        private static readonly HashSet<Type> ElectricalHookTypes = new HashSet<Type>();

        private static void EnsureElectricalHooks(NativeElectricalInputs b)
        {
            foreach (var read in b.Reads)
            {
                var type = read.Action.GetType(); if (ElectricalHookTypes.Contains(type)) continue;
                var method = type.GetMethod(type.Name == "FloatCompare" ? "DoCompare" : "DoFloatOperator",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native electrical RPM boundary changed.");
                new Harmony("com.ourwintercar.wintermp.electrical-inputs").Patch(method,
                    prefix: new HarmonyMethod(typeof(NativeElectricalHooks), nameof(NativeElectricalHooks.Before)),
                    finalizer: new HarmonyMethod(typeof(NativeElectricalHooks), nameof(NativeElectricalHooks.After)));
                ElectricalHookTypes.Add(type);
            }
        }

        // Harmony __state must be separate from the thermal, cooling and wear hooks.
        private static class NativeElectricalHooks
        {
            internal static void Before(FsmStateAction __instance, out NativeElectricalRead? __state)
            {
                __state = null;
                if (!ElectricalReaders.TryGetValue(__instance, out var read)) return;
                var b = read.Owner; var item = b.Item;
                try
                {
                    var session = SessionManager.Instance; var world = WorldSyncManager.Instance;
                    bool host = session != null && session.IsHost && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
                    if (!VehicleWearSimulationPolicy.Delegated(host, GuestSaveGuard.ProtectWorld,
                        item.LocallyOwned || world == null || world.IsLocalPlayerDriving(item), item.RemoteOwner)) return;
                    ValidateElectricalInputs(b);
                    var sample = item.AcceptedVehicleState;
                    bool ready = VehicleWearSimulationPolicy.HasSample(sample, item.Id, item.RemoteOwner, Time.unscaledTime < item.RemoteEngineUntil);
                    read.Input.Value = ready ? sample!.Rpm : 0;
                    read.Depth++; __state = read; read.Field.SetValue(__instance, read.Input);
                }
                catch (Exception error)
                {
                    if (__state != null) try { RestoreElectricalRead(__state); } catch (Exception restore) { NoteElectricalInputFailure(item, restore); }
                    __state = null; ClearElectricalInputs(item); NoteElectricalInputFailure(item, error);
                }
            }

            internal static Exception? After(Exception? __exception, NativeElectricalRead? __state)
            {
                if (__state != null)
                    try { RestoreElectricalRead(__state); }
                    catch (Exception error) { ClearElectricalInputs(__state.Owner.Item); NoteElectricalInputFailure(__state.Owner.Item, error); }
                return __exception;
            }
        }

        private static void RestoreElectricalRead(NativeElectricalRead read)
        {
            if (--read.Depth == 0 && ReferenceEquals(read.Field.GetValue(read.Action), read.Input)) read.Field.SetValue(read.Action, read.Owner.Rpm);
        }
    }
}
