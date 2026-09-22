using System;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static FieldInfo? _gearboxConditionOutput;

        private static void InitializeGearboxConditionRead(Harmony harmony, Assembly assembly)
        {
            var type = assembly.GetType("HutongGames.PlayMaker.Actions.GetFsmInt", true);
            var method = type.GetMethod("DoGetFsmInt", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (!typeof(FsmStateAction).IsAssignableFrom(type) || method == null
                || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                throw new InvalidOperationException("Native gearbox integer read boundary changed.");
            _gearboxConditionOutput = ExternalFloatField(type, "storeValue", typeof(FsmInt));
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(GuestEngineProtection), nameof(BeforeGearboxConditionRead)));
        }

        private static Write ValidateGearboxConditionReader(PlayMakerFSM fsm, GuestGearboxConditionReadData rule)
        {
            var read = FindAction(fsm, rule.State, rule.Index, "GetFsmInt");
            ValidateGearboxConditionSignature(fsm, read.Action);
            return read;
        }

        private static void ValidateGearboxConditionSignature(PlayMakerFSM owner, FsmStateAction action)
        {
            var target = Field<FsmOwnerDefault>(action, "gameObject");
            var output = Field<FsmInt>(action, "storeValue");
            if (!action.Enabled || !NamedTarget(target, "db_Gearbox")
                || !ReferenceEquals(target.GameObject, owner.FsmVariables.FindFsmGameObject("db_Gearbox"))
                || !Literal(Field<FsmString>(action, "fsmName"), "Data")
                || !Literal(Field<FsmString>(action, "variableName"), "DamageType")
                || Field<bool>(action, "everyFrame") || output == null || output.IsNone
                || !ReferenceEquals(output, owner.FsmVariables.FindFsmInt("DamageType")))
                throw new InvalidOperationException("Native gearbox condition read signature changed.");
            foreach (var global in FsmVariables.GlobalVariables.IntVariables)
                if (ReferenceEquals(global, output)) throw new InvalidOperationException("Gearbox condition output aliases a global.");
            var source = owner.FsmVariables.FindFsmGameObject("db_Gearbox")?.Value;
            if (source != null)
                foreach (var fsm in source.GetComponents<PlayMakerFSM>())
                    foreach (var scalar in fsm.FsmVariables.IntVariables)
                        if (ReferenceEquals(scalar, output)) throw new InvalidOperationException("Gearbox condition output aliases saved data.");
        }

        private static bool BeforeGearboxConditionRead(FsmStateAction __instance)
        {
            var session = SessionManager.Instance;
            if (!GuestSaveGuard.ProtectWorld || session == null || session.IsHost || session.State != SessionState.Connected) return true;
            var owner = __instance.Fsm?.Owner as PlayMakerFSM;
            if (owner == null || owner.FsmName != "Damage") return true;
            try
            {
                Bindings.TryGetValue(owner, out var existing);
                var rule = (FindRule(owner) ?? existing?.Rule) as GuestEngineWriterData;
                var reader = rule?.GearboxConditionRead;
                if (reader == null) return true;
                var state = FindState(owner, reader.State);
                if (!FsmHook.IsNativeAction(state, reader.Index, __instance)) return true;
                var world = WorldSyncManager.Instance;
                if (world == null || !world.TryReadGearboxConditionInput(owner, out byte damage)) return true;
                if (existing == null || !Current(existing) || !ReferenceEquals(existing.Rule, rule) || !ReferenceEquals(FindRule(owner), rule)
                    || existing.GearboxConditionRead == null || !ReferenceEquals(existing.GearboxConditionRead.Action, __instance))
                    throw new InvalidOperationException("Gearbox condition read lacks current saved-wear protection.");
                ValidateGearboxConditionSignature(owner, __instance);
                foreach (var write in existing.Writes)
                    if ((write.Action.Enabled || write.State.ActiveActions.Contains(write.Action)) && !IsGearboxUseObserver(write))
                        throw new InvalidOperationException("Gearbox condition read has an active saved-wear writer.");
                var output = (FsmInt)_gearboxConditionOutput!.GetValue(__instance);


                // Keep native source and cache intact so withdrawal, ownership
                // changes and disconnect immediately resume ordinary lookup.
                output.Value = damage;
                return false;
            }
            catch (Exception error) { Fail(owner, error); return false; }
        }
    }
}
