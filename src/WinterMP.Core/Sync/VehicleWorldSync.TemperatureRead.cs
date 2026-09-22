using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private static readonly Dictionary<FsmStateAction, SyncedItem> TemperatureReaders = new Dictionary<FsmStateAction, SyncedItem>();
        private static bool _temperatureHookAttempted, _temperatureHookReady;

        private static bool EnsureTemperatureReadHook(Type type)
        {
            if (_temperatureHookAttempted) return _temperatureHookReady;
            _temperatureHookAttempted = true;
            try
            {
                var method = type.GetMethod("DoGetFsmFloat", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native temperature read boundary changed.");
                new Harmony("com.ourwintercar.wintermp.temperature-read").Patch(method,
                    postfix: new HarmonyMethod(typeof(VehicleWorldSync), nameof(AfterTemperatureRead)));
                _temperatureHookReady = true;
            }
            catch (Exception error) { SyncEventLog.Record("vehicle-temperature-hook-unavailable", error.Message); }
            return _temperatureHookReady;
        }

        private static bool HasRemoteTemperature(SyncedItem item)
        {
            if (UsesHostCoolant(item)) return false;
            var session = SessionManager.Instance;
            if (session == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                || item.LocallyOwned || item.Body == null || item.AcceptedVehicleState == null
                || Time.unscaledTime >= item.RemoteEngineUntil
                || (item.RemoteOwner != WorldSyncIds.NoOwner && item.RemoteOwner != item.AcceptedVehicleState.OwnerPlayerId)) return false;
            var world = WorldSyncManager.Instance;
            return world != null && !world.IsLocalPlayerDriving(item);
        }

        private static void ClearTemperatureReader(SyncedItem item)
        {
            var binding = item.NativeTemperature;
            if (binding != null && TemperatureReaders.TryGetValue(binding.Read, out var owner) && ReferenceEquals(owner, item))
                TemperatureReaders.Remove(binding.Read);
        }

        private static void AfterTemperatureRead(FsmStateAction __instance)
        {
            if (!TemperatureReaders.TryGetValue(__instance, out var item)) return;
            try
            {
                var binding = item.NativeTemperature;
                bool host = TryGetHostCoolant(item, out float celsius);
                if (binding == null || !ReferenceEquals(binding.Read, __instance) || (!host && !HasRemoteTemperature(item))) return;
                ValidateTemperatureBinding(item, binding, topology: false);
                // Substitute degrees at the native read boundary. The following
                // native scale, clamp and needle actions still run every frame.
                binding.Output.Value = host ? celsius : item.AcceptedVehicleState!.CoolantTemp / 255f * CoolantTempMaxC;
            }
            catch (Exception error)
            {
                ClearTemperatureReader(item);
                item.NativeTemperature = null;
                item.NextTemperatureProbeAt = 0f;
                try { SyncEventLog.Record("vehicle-temperature-read-unavailable", error.Message); }
                catch { /* A display failure cannot escape into native cooling or FSM dispatch. */ }
            }
        }
    }
}
