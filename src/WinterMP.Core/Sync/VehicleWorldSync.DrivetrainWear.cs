using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal sealed class NativeDrivetrainWear
        {
            internal SyncedItem Item = null!;
            internal PlayMakerFSM Fsm = null!;
            internal Rigidbody Body = null!;
            internal VehicleDrivetrainWearData Rule = null!;
            internal NativeDifferentialSpeed Source = null!;
            internal FsmState Sample = null!, Wear = null!, Wait = null!;
            internal FsmFloat Rate = null!;
            internal readonly List<DrivetrainRead> Reads = new List<DrivetrainRead>();
            internal readonly float[] Divisors = new float[3], WearValues = new float[3];
            internal readonly FsmFloat[] SavedWear = new FsmFloat[3];
            internal readonly FsmSuppressor Pause = new FsmSuppressor();
            internal FsmState? Blocked;
            internal bool Ready;
        }
        internal sealed class DrivetrainRead
        {
            internal NativeDrivetrainWear Owner = null!;
            internal FsmStateAction Action = null!;
            internal FieldInfo Field = null!;
            internal FsmFloat Original = null!;
            internal readonly FsmFloat Input = new FsmFloat();
            internal int Depth;
        }
        private static readonly Dictionary<PlayMakerFSM, NativeDrivetrainWear> DrivetrainGroups = new Dictionary<PlayMakerFSM, NativeDrivetrainWear>();
        private static readonly Dictionary<FsmStateAction, DrivetrainRead> DrivetrainReads = new Dictionary<FsmStateAction, DrivetrainRead>();
        private static readonly HashSet<Type> DrivetrainHookTypes = new HashSet<Type>();
        private static bool _drivetrainEntryHook;

        private static bool DelegatedDrivetrain(SyncedItem item)
        {
            var session = SessionManager.Instance; var world = WorldSyncManager.Instance;
            return VehicleWearSimulationPolicy.Delegated(session != null && session.IsHost && session.State == SessionState.Hosting,
                GuestSaveGuard.ProtectWorld, item.LocallyOwned || world == null || world.IsLocalPlayerDriving(item), item.RemoteOwner);
        }

        private static void EnsureDrivetrainWear(SyncedItem item)
        {
            var b = item.DrivetrainWear;
            if (b != null && (b.Body != item.Body || b.Fsm == null)) { ClearDrivetrainWear(item); b = null; }
            if (b != null && !DelegatedDrivetrain(item)) { b.Pause.Restore(); b.Blocked = null; return; }
            if (b != null && b.Ready && !b.Pause.Active) return;
            if (Time.unscaledTime < item.NextDrivetrainWearProbeAt) return;
            item.NextDrivetrainWearProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            try
            {
                var rule = SyncCatalog.VehicleDrivetrainWear; var sourceRule = SyncCatalog.VehicleDifferentialSpeed;
                if (rule == null || sourceRule == null || item.Body == null || !item.IsVehicle || item.Path != rule.RootPath) return;
                if (b == null)
                {
                    var fsm = FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), rule.Path, rule.Fsm);
                    b = new NativeDrivetrainWear { Item = item, Body = item.Body, Fsm = fsm, Rule = rule };
                    item.DrivetrainWear = b; DrivetrainGroups[fsm] = b;
                }
                InstallDrivetrainEntryHook();
                RetireDrivetrainReads(b); b.Rule = rule; b.Source = BindDifferentialSpeed(item, sourceRule);
                b.Sample = HeatState(b.Fsm, sourceRule.State); b.Wear = HeatState(b.Fsm, sourceRule.WearState); b.Wait = HeatState(b.Fsm, sourceRule.WaitState);
                b.Rate = b.Fsm.FsmVariables.FindFsmFloat("Rate");
                ValidateDrivetrainWear(b);
                AddDrivetrainRead(b, b.Sample.Actions[2]);
                for (int i = 0; i < 3; i++) AddDrivetrainRead(b, b.Wear.Actions[i * 2]);
                InstallDrivetrainHooks(b); b.Ready = true;
                b.Pause.Restore();
                var blocked = b.Blocked; b.Blocked = null;
                if (blocked != null && ReferenceEquals(b.Fsm.Fsm.ActiveState, blocked) && b.Fsm.enabled && b.Fsm.gameObject.activeInHierarchy)
                {
                    FsmExecutionStack.PushFsm(b.Fsm.Fsm);
                    try { blocked.OnEnter(); b.Fsm.Fsm.UpdateStateChanges(); }
                    finally { FsmExecutionStack.PopFsm(); }
                }
                SyncEventLog.Record("vehicle-drivetrain-wear-bound", item.Path);
            }
            catch (Exception error)
            {
                if (b != null && DelegatedDrivetrain(item)) PauseDrivetrainWear(b, null, error);
                else NoteDrivetrainWearFailure(item, error);
            }
        }

        private static void AddDrivetrainRead(NativeDrivetrainWear b, FsmStateAction action)
        {
            var read = new DrivetrainRead { Owner = b, Action = action, Original = b.Source.Output,
                Field = action.GetType().GetField("float1") };
            b.Reads.Add(read); DrivetrainReads[action] = read;
        }

        private static void InstallDrivetrainHooks(NativeDrivetrainWear b)
        {
            var harmony = new Harmony("com.ourwintercar.wintermp.drivetrain-wear");
            foreach (var read in b.Reads)
            {
                var type = read.Action.GetType(); if (DrivetrainHookTypes.Contains(type)) continue;
                string name = type.Name == "FloatCompare" ? "DoCompare" : "DoFloatOperator";
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null) throw new InvalidOperationException("Native drivetrain read boundary changed.");
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(DrivetrainHooks), nameof(DrivetrainHooks.BeforeRead)),
                    finalizer: new HarmonyMethod(typeof(DrivetrainHooks), nameof(DrivetrainHooks.AfterRead)));
                DrivetrainHookTypes.Add(type);
            }
        }

        private static void InstallDrivetrainEntryHook()
        {
            if (_drivetrainEntryHook) return;
            new Harmony("com.ourwintercar.wintermp.drivetrain-wear").Patch(typeof(FsmState).GetMethod("OnEnter"),
                prefix: new HarmonyMethod(typeof(DrivetrainHooks), nameof(DrivetrainHooks.BeforeEntry)));
            _drivetrainEntryHook = true;
        }

        private static float DrivetrainInput(NativeDrivetrainWear b)
        {
            for (int i = 0; i < 3; i++) b.WearValues[i] = b.SavedWear[i].Value;
            var item = b.Item;
            return VehicleDrivetrainWearPolicy.TryInput(item.AcceptedVehicleState, item.Id, item.RemoteOwner,
                Time.unscaledTime < item.RemoteEngineUntil, b.Divisors, b.WearValues, out float speed) ? speed : 0;
        }

        private static void PauseDrivetrainWear(NativeDrivetrainWear b, FsmState? state, Exception error)
        {
            b.Ready = false; if (state != null) b.Blocked = state;
            b.Pause.Suppress(b.Fsm); NoteDrivetrainWearFailure(b.Item, error);
        }
        private static void NoteDrivetrainWearFailure(SyncedItem item, Exception error)
        {
            item.NextDrivetrainWearProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
            if (Time.unscaledTime < item.NextDrivetrainWearErrorAt) return;
            item.NextDrivetrainWearErrorAt = Time.unscaledTime + 10;
            try { SyncEventLog.Record("vehicle-drivetrain-wear-unavailable", item.Path + ": " + error.Message); } catch { }
        }
        private static void RetireDrivetrainReads(NativeDrivetrainWear b)
        {
            foreach (var read in b.Reads) DrivetrainReads.Remove(read.Action);
            b.Reads.Clear(); b.Ready = false;
        }
        private static void ClearDrivetrainWear(SyncedItem item)
        {
            var b = item.DrivetrainWear;
            if (b != null) { RetireDrivetrainReads(b); DrivetrainGroups.Remove(b.Fsm); b.Pause.Restore(); b.Blocked = null; }
            item.DrivetrainWear = null; item.NextDrivetrainWearProbeAt = item.NextDrivetrainWearErrorAt = 0;
        }

        private static class DrivetrainHooks
        {
            internal static bool BeforeEntry(FsmState __instance)
            {
                var fsm = __instance?.Fsm?.Owner as PlayMakerFSM;
                if (fsm == null || !DrivetrainGroups.TryGetValue(fsm, out var b) || !DelegatedDrivetrain(b.Item)) return true;
                try
                {
                    if (!b.Ready || b.Pause.Active) { b.Blocked = __instance; b.Pause.Suppress(b.Fsm); return false; }
                    ValidateDrivetrainWear(b);
                    if (ReferenceEquals(__instance, b.Wear)) SyncEventLog.Record("vehicle-drivetrain-wear-input", "vehicle=" + b.Item.Id + " speed=" + DrivetrainInput(b));
                    return true;
                }
                catch (Exception error) { PauseDrivetrainWear(b, __instance, error); return false; }
            }
            internal static bool BeforeRead(FsmStateAction __instance, out DrivetrainRead? __state)
            {
                __state = null;
                if (!DrivetrainReads.TryGetValue(__instance, out var read) || !DelegatedDrivetrain(read.Owner.Item)) return true;
                if (!read.Owner.Ready || read.Owner.Pause.Active) return false;
                try
                {
                    read.Input.Value = DrivetrainInput(read.Owner); read.Depth++; __state = read;
                    read.Field.SetValue(__instance, read.Input); return true;
                }
                catch (Exception error) { PauseDrivetrainWear(read.Owner, null, error); return false; }
            }
            internal static Exception? AfterRead(Exception? __exception, DrivetrainRead? __state)
            {
                if (__state != null && --__state.Depth == 0)
                    try { if (ReferenceEquals(__state.Field.GetValue(__state.Action), __state.Input)) __state.Field.SetValue(__state.Action, __state.Original); }
                    catch (Exception error) { PauseDrivetrainWear(__state.Owner, null, error); }
                return __exception;
            }
        }
    }
}
