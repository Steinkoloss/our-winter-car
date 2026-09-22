using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static IEnumerable<GuestDrivetrainReadData> DrivetrainReaders(GuestEngineWriterData rule)
        {
            foreach (var read in rule.DrivetrainWearReads) yield return read;
            if (rule.GearboxOilRead != null) yield return rule.GearboxOilRead;
        }
        private static readonly Dictionary<FsmStateAction, PlayMakerFSM> ProtectedDrivetrainEvents = new Dictionary<FsmStateAction, PlayMakerFSM>();

        private static void InitializeDrivetrainEventGuard(Harmony harmony, Assembly assembly)
        {
            var type = assembly.GetType("HutongGames.PlayMaker.Actions.SendEventByName", true);
            var method = type.GetMethod("OnEnter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (method == null || method.GetMethodBody() == null) throw new InvalidOperationException("Native named event boundary changed.");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(GuestEngineProtection), nameof(BeforeProtectedEvent)));
        }

        private static Write ValidateProtectedEvent(PlayMakerFSM owner, GuestEngineEventActionData rule)
        {
            var write = FindAction(owner, rule.State, rule.Index, "SendEventByName"); var action = write.Action;
            var target = Field<FsmEventTarget>(action, "eventTarget"); var delay = Field<FsmFloat>(action, "delay");
            var delayed = action.GetType().GetField("delayedEvent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (target == null || (int)target.target != 2 || target.fsmComponent != null
                || !NamedTarget(target.gameObject, rule.TargetVariable)
                || !ReferenceEquals(target.gameObject.GameObject, owner.FsmVariables.FindFsmGameObject(rule.TargetVariable))
                || !Literal(target.fsmName, rule.TargetFsm) || !FalseLiteral(target.sendToChildren) || !FalseLiteral(target.excludeSelf)
                || !Literal(Field<FsmString>(action, "sendEvent"), rule.Event) || delay == null || delay.UseVariable || delay.Value != 0
                || Field<bool>(action, "everyFrame") || delayed == null || delayed.GetValue(action) != null)
                throw new InvalidOperationException("Changed native failure event at " + rule.State + "#" + rule.Index + ".");
            ProtectedDrivetrainEvents[action] = owner;
            return write;
        }
        private static bool FalseLiteral(FsmBool value) => value != null && !value.UseVariable && !value.Value;

        private static bool BeforeProtectedEvent(FsmStateAction __instance)
        {
            if (!GuestSaveGuard.ProtectWorld) return true;
            // A repaired graph can replace its action array while an old callback
            // still references the original saved-part event.
            if (ProtectedDrivetrainEvents.ContainsKey(__instance)) return false;
            var owner = __instance.Fsm?.Owner as PlayMakerFSM; if (owner == null) return true;
            try
            {
                Bindings.TryGetValue(owner, out var previous);
                if (previous != null)
                    foreach (var action in previous.Writes) if (ReferenceEquals(action.Action, __instance)) return false;
                var rule = (FindRule(owner) ?? previous?.Rule) as GuestEngineWriterData;
                if (rule == null) return true;
                foreach (var action in rule.EventActions)
                {
                    var state = FindState(owner, action.State);
                    if (FsmHook.IsNativeAction(state, action.Index, __instance)) return false;
                }
                return true;
            }
            catch (Exception error) { Fail(owner, error); return false; }
        }

        private static void RetireDrivetrainEvents()
        {
            foreach (var pair in new List<KeyValuePair<FsmStateAction, PlayMakerFSM>>(ProtectedDrivetrainEvents))
                if (pair.Value == null) ProtectedDrivetrainEvents.Remove(pair.Key);
        }

        private static Write ValidateDrivetrainReader(PlayMakerFSM owner, GuestDrivetrainReadData rule)
        {
            var read = FindAction(owner, rule.State, rule.Index, "GetFsmFloat");
            var action = read.Action; var target = Field<FsmOwnerDefault>(action, "gameObject");
            var output = Field<FsmFloat>(action, "storeValue");
            if (!action.Enabled || !NamedTarget(target, rule.Variable)
                || !ReferenceEquals(target.GameObject, owner.FsmVariables.FindFsmGameObject(rule.Variable))
                || !Literal(Field<FsmString>(action, "fsmName"), "Data") || !Literal(Field<FsmString>(action, "variableName"), rule.Scalar)
                || Field<bool>(action, "everyFrame") || output == null || output.IsNone || !output.UseVariable
                || !ReferenceEquals(output, owner.FsmVariables.FindFsmFloat(rule.Output)))
                throw new InvalidOperationException("Native drivetrain wear reader changed.");
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(global, output)) throw new InvalidOperationException("Drivetrain wear output aliases a global.");
            foreach (var reference in owner.FsmVariables.GameObjectVariables)
                if (reference.Value != null)
                    foreach (var source in reference.Value.GetComponents<PlayMakerFSM>())
                        foreach (var value in source.FsmVariables.FloatVariables)
                            if (ReferenceEquals(value, output)) throw new InvalidOperationException("Drivetrain wear output aliases saved data.");
            return read;
        }

        private static void ValidateDrivetrainProtection(Binding binding)
        {
            if (binding.DrivetrainWearReads.Count == 0) return;
            if (binding.Rule is not GuestEngineWriterData rule || !ReferenceEquals(FindRule(binding.Fsm), rule))
                throw new InvalidOperationException("Drivetrain consumer identity changed.");
            foreach (var reader in DrivetrainReaders(rule)) ValidateDrivetrainReader(binding.Fsm, reader);
            foreach (var action in rule.EventActions) ValidateProtectedEvent(binding.Fsm, action);
            foreach (var action in rule.Actions) ValidateWrite(binding.Fsm, action);
        }

        private static string? PrepareBoundInputs(Binding binding, bool deferInputs = false)
        {
            if (!deferInputs || !(PrepareInputs?.Target is ItemWorldSync inputs) || !inputs.CanDeferGuestEngineInputs(binding.Fsm))
                PrepareInputs?.Invoke(binding.Fsm);
            // Missing shared inputs are an ordinary wait, often checked every
            // frame. Keep signature failures exceptional without throwing here.
            if (!PrepareWheelHealthInput(binding)) return "Waiting for the host's tyre health.";
            if (binding.DrivetrainWearReads.Count == 0) return null;
            ValidateDrivetrainProtection(binding);
            var session = SessionManager.Instance; var world = WorldSyncManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected || world == null) return null;
            if (world.TryReadDrivetrainWearInput(binding.Fsm, 0, out _, out bool ready) && !ready)
                return "Waiting for the host's drivetrain wear.";
            if (((GuestEngineWriterData)binding.Rule).GearboxOilRead != null
                && world.TryReadGearboxOilInput(binding.Fsm, out _, out bool oilReady) && !oilReady)
                return "Waiting for the host's gearbox oil.";
            return null;
        }

        private static bool ProjectDrivetrainWearRead(FsmStateAction action)
        {
            var owner = action.Fsm?.Owner as PlayMakerFSM; if (owner == null) return false;
            Bindings.TryGetValue(owner, out var binding);
            // Every native GetFsmFloat reaches this hook. Only current catalog
            // consumer names or a remembered consumer need a full path lookup.
            // Recheck both on each call so renames and catalog repairs stay live.
            bool candidate = binding?.Rule is GuestEngineWriterData remembered && remembered.DrivetrainWearReads.Count != 0;
            var profile = SyncCatalog.GuestEngineProtection;
            if (!candidate && profile != null)
            {
                string name = owner.FsmName;
                foreach (var writer in profile.Writers)
                    if (writer.DrivetrainWearReads.Count != 0 && writer.Fsm == name) { candidate = true; break; }
            }
            if (!candidate) return false;
            var rule = (FindRule(owner) ?? binding?.Rule) as GuestEngineWriterData;
            if (rule == null || rule.DrivetrainWearReads.Count == 0) return false;
            try
            {
                GuestDrivetrainReadData? selected = null;
                foreach (var read in DrivetrainReaders(rule))
                {
                    var state = FindState(owner, read.State);
                    if (FsmHook.IsNativeAction(state, read.Index, action)) selected = read;
                }
                if (selected == null) return false;
                if (binding == null || !Current(binding)) throw new InvalidOperationException("Drivetrain read has no current protection.");
                ValidateDrivetrainProtection(binding);
                foreach (var write in binding.Writes)
                    if ((write.Action.Enabled || write.State.ActiveActions.Contains(write.Action)) && !IsGearboxUseObserver(write))
                        throw new InvalidOperationException("Drivetrain saved action is still active.");
                var world = WorldSyncManager.Instance;
                if (world == null) return false;
                float wear; bool ready;
                bool registered = selected.Scalar == "OilLevel" ? world.TryReadGearboxOilInput(owner, out wear, out ready)
                    : world.TryReadDrivetrainWearInput(owner, selected.Part, out wear, out ready);
                if (!registered) return false;
                if (!ready) throw new GuestEngineInputsPendingException("Waiting for the host's drivetrain wear.");
                // Native source and cache remain intact. Only the reader's local
                // output changes; guarded failure actions cannot mutate saved parts.
                Field<FsmFloat>(action, "storeValue").Value = wear;
                return true;
            }
            catch (Exception error) { Fail(owner, error); return true; }
        }
    }
}
