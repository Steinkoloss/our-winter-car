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
        private static readonly Dictionary<FsmStateAction, NativeWearRead> WearReaders = new Dictionary<FsmStateAction, NativeWearRead>();
        private static bool _wearHookAttempted, _wearHookReady;

        private static bool EnsureWearReadHook(Type type)
        {
            if (_wearHookAttempted) return _wearHookReady;
            _wearHookAttempted = true;
            var method = type.GetMethod("DoFloatOperator", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null) return false;
            new Harmony("com.ourwintercar.wintermp.wear-inputs").Patch(method,
                prefix: new HarmonyMethod(typeof(VehicleWorldSync), nameof(BeforeWearRead)),
                finalizer: new HarmonyMethod(typeof(VehicleWorldSync), nameof(AfterWearRead)));
            return _wearHookReady = true;
        }

        private static void BeforeWearRead(FsmStateAction __instance, out NativeWearRead? __state)
        {
            __state = null;
            if (!WearReaders.TryGetValue(__instance, out var read)) return;
            var item = read.Owner.Item;
            try
            {
                var session = SessionManager.Instance; var world = WorldSyncManager.Instance;
                bool host = session != null && session.IsHost && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
                bool local = item.LocallyOwned || world == null || world.IsLocalPlayerDriving(item);
                if (!VehicleWearSimulationPolicy.Delegated(host, GuestSaveGuard.ProtectWorld, local, item.RemoteOwner)) return;
                foreach (var sibling in read.Owner.Reads) ValidateWearRead(sibling);
                var sample = item.AcceptedVehicleState;
                bool ready = VehicleWearSimulationPolicy.HasSample(sample, item.Id, item.RemoteOwner, Time.unscaledTime < item.RemoteEngineUntil);
                // With a delegated car but no fresh sample, zero RPM
                // prevents a stale local simulator from accumulating new wear.
                read.Input.Value = ready ? sample!.Rpm : 0f;
                __state = read;
                read.Field.SetValue(__instance, read.Input);
            }
            catch (Exception error)
            {
                if (__state != null) RestoreWearRead(__state);
                __state = null;
                ClearWearInputs(item); NoteWearInputFailure(item, error);
            }
        }

        private static Exception? AfterWearRead(Exception? __exception, NativeWearRead? __state)
        {
            if (__state != null)
                try { RestoreWearRead(__state); }
                catch (Exception error) { ClearWearInputs(__state.Owner.Item); NoteWearInputFailure(__state.Owner.Item, error); }
            return __exception;
        }

        private static void RestoreWearRead(NativeWearRead read)
        {
            if (ReferenceEquals(read.Field.GetValue(read.Action), read.Input)) read.Field.SetValue(read.Action, read.Original);
        }
    }
}
