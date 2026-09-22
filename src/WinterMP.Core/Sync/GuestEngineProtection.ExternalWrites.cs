using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private sealed class ExternalFloatWriter
        {
            internal FieldInfo Target = null!, FsmName = null!, LastObject = null!, CachedFsm = null!;
            internal bool Integer;
        }

        private static readonly Dictionary<Type, ExternalFloatWriter> ExternalFloatWriters = new Dictionary<Type, ExternalFloatWriter>();
        private static readonly HashSet<PlayMakerFSM> ExternalFloatTargets = new HashSet<PlayMakerFSM>(new FsmIdentity());
        private static readonly HashSet<PlayMakerFSM> ExternalIntTargets = new HashSet<PlayMakerFSM>(new FsmIdentity());
        private static Func<GameObject, string, PlayMakerFSM>? _resolveExternalFloatFsm;

        private static void InitializeExternalFloatHooks(Harmony harmony)
        {
            var assembly = typeof(PlayMakerFSM).Assembly;
            foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
                if (candidate.GetName().Name == "Assembly-CSharp") { assembly = candidate; break; }
            var helpers = typeof(ActionHelpers);
            var resolve = helpers.GetMethod("GetGameObjectFsm", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(GameObject), typeof(string) }, null);
            if (resolve == null || resolve.ReturnType != typeof(PlayMakerFSM) || resolve.GetMethodBody() == null)
                throw new InvalidOperationException("Missing native scalar destination resolver.");
            _resolveExternalFloatFsm = (Func<GameObject, string, PlayMakerFSM>)Delegate.CreateDelegate(
                typeof(Func<GameObject, string, PlayMakerFSM>), resolve);
            foreach (string name in new[] { "SetFsmFloat", "AddFsmFloat", "SubtractFsmFloat", "SetFsmInt" })
            {
                var type = assembly.GetType("HutongGames.PlayMaker.Actions." + name, true);
                // Native AddFsmFloat uses the same misleading helper name as SubtractFsmFloat.
                string method = name == "SetFsmInt" ? "DoSetFsmInt" : name == "SetFsmFloat" ? "DoSetFsmFloat" : "DoSubtractFsmFloat";
                var target = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (!typeof(FsmStateAction).IsAssignableFrom(type) || target == null || target.ReturnType != typeof(void) || target.GetMethodBody() == null)
                    throw new InvalidOperationException("Native external scalar writer changed: " + name);
                var fields = new ExternalFloatWriter {
                    Integer = name == "SetFsmInt",
                    Target = ExternalFloatField(type, "gameObject", typeof(FsmOwnerDefault)),
                    FsmName = ExternalFloatField(type, "fsmName", typeof(FsmString)),
                    LastObject = ExternalFloatField(type, "goLastFrame", typeof(GameObject)),
                    CachedFsm = ExternalFloatField(type, "fsm", typeof(PlayMakerFSM)) };
                ExternalFloatWriters[type] = fields;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(GuestEngineProtection), nameof(BeforeExternalFloatWrite)));
            }
            InitializeBatteryReads(harmony, assembly);
            InitializeEngineInputReads(harmony, assembly);
            InitializeGearboxConditionRead(harmony, assembly);
            InitializeDrivetrainEventGuard(harmony, assembly);
        }

        private static FieldInfo ExternalFloatField(Type type, string name, Type expected)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expected) throw new InvalidOperationException("Native scalar writer field changed: " + name);
            return field;
        }

        private static bool BeforeExternalFloatWrite(FsmStateAction __instance)
        {
            try
            {
                if (!ApplyingHostGearboxUse && IsDelegatedGearboxUse(__instance)) return false;
                if (!GuestSaveGuard.ProtectWorld) return true;
                if (ObserveGearboxUse(__instance)) return false;
                if (ObserveStarterDraw(__instance)) return false;
                if (!ExternalFloatWriters.TryGetValue(__instance.GetType(), out var fields))
                    throw new InvalidOperationException("Unknown native external scalar writer.");
                var target = __instance.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)fields.Target.GetValue(__instance));
                if (target == null) return true;
                // Match the native cache: changing fsmName on the same object does
                // not re-resolve its destination. Moving to another object does.
                var destination = target == (GameObject)fields.LastObject.GetValue(__instance)
                    ? (PlayMakerFSM)fields.CachedFsm.GetValue(__instance)
                    : _resolveExternalFloatFsm!(target, ((FsmString)fields.FsmName.GetValue(__instance)).Value);
                if (destination == null) return true;
                RememberExternalFloatTarget(destination, FindRule(destination));
                return !(fields.Integer ? ExternalIntTargets : ExternalFloatTargets).Contains(destination);
            }
            catch (Exception error)
            {
                NoteFailure("external scalar destination", error);
                // An unresolved destination cannot be proven safe for this write.
                return false;
            }
        }

        private static void RememberExternalFloatTarget(PlayMakerFSM fsm, object? rule)
        {
            if (rule is GuestEnginePausedFsmData paused && paused.BlockExternalFloatWrites && ExternalFloatTargets.Add(fsm))
                SyncEventLog.Record("guest-engine-external-writes-blocked", paused.Path + "::" + paused.Fsm);
            if (rule is GuestEnginePausedFsmData integers && integers.BlockExternalIntWrites && ExternalIntTargets.Add(fsm))
                SyncEventLog.Record("guest-engine-external-int-writes-blocked", integers.Path + "::" + integers.Fsm);
        }

        private static void RetireExternalFloatTargets()
        {
            // Keep live identities through renaming, reparenting, catalog repair
            // and disconnect, just like the paused saved mount itself.
            ExternalFloatTargets.RemoveWhere(fsm => fsm == null);
            ExternalIntTargets.RemoveWhere(fsm => fsm == null);
            BatteryReadSources.RemoveWhere(fsm => fsm == null);
            HeaterReadSources.RemoveWhere(fsm => fsm == null);
            RearWindowReadSources.RemoveWhere(fsm => fsm == null);
            RetireWireReadSources();
            RetireHeaterHoseReadSources();
            RetireStarterObservers();
            RetireDrivetrainEvents();
            RetireGearboxUseObservers();
        }
    }
}
