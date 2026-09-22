using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private sealed class BatteryReader
        {
            internal ExternalFloatWriter Source = null!;
            internal FieldInfo Variable = null!, Output = null!;
            internal bool Boolean;
        }

        private static readonly Dictionary<Type, BatteryReader> BatteryReaders = new Dictionary<Type, BatteryReader>();
        private static readonly HashSet<PlayMakerFSM> BatteryReadSources = new HashSet<PlayMakerFSM>(new FsmIdentity());

        private static void InitializeBatteryReads(Harmony harmony, Assembly assembly)
        {
            foreach (string name in new[] { "GetFsmFloat", "GetFsmBool" })
            {
                var type = assembly.GetType("HutongGames.PlayMaker.Actions." + name, true);
                var method = type.GetMethod("Do" + name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (!typeof(FsmStateAction).IsAssignableFrom(type) || method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new InvalidOperationException("Native battery read boundary changed: " + name);
                bool boolean = name == "GetFsmBool";
                BatteryReaders[type] = new BatteryReader { Boolean = boolean,
                    Source = new ExternalFloatWriter {
                        Target = ExternalFloatField(type, "gameObject", typeof(FsmOwnerDefault)),
                        FsmName = ExternalFloatField(type, "fsmName", typeof(FsmString)),
                        LastObject = ExternalFloatField(type, "goLastFrame", typeof(GameObject)),
                        CachedFsm = ExternalFloatField(type, "fsm", typeof(PlayMakerFSM)) },
                    Variable = ExternalFloatField(type, "variableName", typeof(FsmString)),
                    Output = ExternalFloatField(type, "storeValue", boolean ? typeof(FsmBool) : typeof(FsmFloat)) };
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GuestEngineProtection), nameof(BeforeBatteryRead)));
            }
        }

        private static bool BeforeBatteryRead(FsmStateAction __instance)
        {
            var session = SessionManager.Instance;
            if (!GuestSaveGuard.ProtectWorld || session == null || session.IsHost || session.State != SessionState.Connected) return true;
            try
            {
                if (!BatteryReaders.TryGetValue(__instance.GetType(), out var read))
                    throw new InvalidOperationException("Unknown native battery reader.");
                var output = read.Output.GetValue(__instance) as NamedVariable;
                if (!read.Boolean && ProjectWheelHealthRead(__instance, output as FsmFloat)) return false;
                if (!read.Boolean && ProjectDrivetrainWearRead(__instance)) return false;
                if (output == null || !read.Boolean && ((FsmFloat)output).IsNone) return true;
                if (ProjectHeaterRead(__instance, read, output) || ProjectWireRead(__instance, read, output)) return false;
                string variable = ((FsmString)read.Variable.GetValue(__instance)).Value;
                if (read.Boolean ? variable != "Installed" : variable != "Charge" && variable != "ChargeMax") return true;
                var fields = read.Source;
                var target = __instance.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)fields.Target.GetValue(__instance));
                if (target == null) return true;
                bool cached = target == (GameObject)fields.LastObject.GetValue(__instance);
                var source = cached ? (PlayMakerFSM)fields.CachedFsm.GetValue(__instance)
                    : _resolveExternalFloatFsm!(target, ((FsmString)fields.FsmName.GetValue(__instance)).Value);
                if (source == null || !IsBatteryReadSource(source)) return true;
                if (!SafeBatteryReadOutput(__instance, source, output, read.Boolean))
                    throw new InvalidOperationException("Battery read output aliases a saved/global variable or is not local to its consumer.");

                var state = WorldSyncManager.Instance?.ReadBatteryInputs();
                bool installed = state != null && state.Valid && (state.Flags & BatteryState.Installed) != 0;
                // Preserve native caching even though this read never touches the
                // saved source. Same-object fsmName changes must keep the old FSM.
                if (!cached) { fields.CachedFsm.SetValue(__instance, source); fields.LastObject.SetValue(__instance, target); }
                if (read.Boolean) ((FsmBool)output).Value = installed;
                else ((FsmFloat)output).Value = installed ? variable == "Charge" ? state!.Charge : state!.ChargeMax : 0;
                return false;
            }
            catch (Exception error)
            {
                NoteFailure("saved electrical input projection", error);
                return false;
            }
        }

        private static bool MatchesNativeReadSource(PlayMakerFSM source, string fsm, string path)
            // Most native Installed reads address unrelated parts. Reject only
            // impossible names; candidates still need the current complete path.
            => source.FsmName == fsm && ScenePath.MayMatchLeafName(path, source.gameObject.name)
                && ScenePath.Of(source.transform) == path;

        private static bool IsBatteryReadSource(PlayMakerFSM source)
        {
            if (BatteryReadSources.Contains(source)) return true;
            var rule = SyncCatalog.GuestEngineInputs?.Battery;
            if (rule == null || !MatchesNativeReadSource(source, rule.Fsm, rule.Path)) return false;
            RememberExternalFloatTarget(source, FindRule(source));
            if (!ExternalFloatTargets.Contains(source)) throw new InvalidOperationException("Battery read source lacks native write protection.");
            BatteryReadSources.Add(source);
            SyncEventLog.Record("guest-battery-read-source", rule.Path + "::" + rule.Fsm);
            return true;
        }

        private static bool SafeBatteryReadOutput(FsmStateAction action, PlayMakerFSM source, NamedVariable output, bool boolean)
        {
            var owner = action.Fsm.Owner as PlayMakerFSM;
            if (owner == null || owner == source) return false;
            bool local = false;
            if (boolean)
            {
                foreach (var value in source.FsmVariables.BoolVariables) if (ReferenceEquals(value, output)) return false;
                foreach (var value in owner.FsmVariables.BoolVariables) if (ReferenceEquals(value, output)) local = true;
                foreach (var value in FsmVariables.GlobalVariables.BoolVariables) if (ReferenceEquals(value, output)) return false;
                foreach (var target in ExternalFloatTargets)
                    if (target != null) foreach (var value in target.FsmVariables.BoolVariables) if (ReferenceEquals(value, output)) return false;
            }
            else
            {
                foreach (var value in owner.FsmVariables.FloatVariables) if (ReferenceEquals(value, output)) local = true;
                foreach (var value in FsmVariables.GlobalVariables.FloatVariables) if (ReferenceEquals(value, output)) return false;
                foreach (var target in ExternalFloatTargets)
                    if (target != null) foreach (var value in target.FsmVariables.FloatVariables) if (ReferenceEquals(value, output)) return false;
            }
            return local;
        }
    }
}
