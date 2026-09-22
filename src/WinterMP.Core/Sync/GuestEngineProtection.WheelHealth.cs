using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static FsmStateAction WheelPunctureWriter(PlayMakerFSM fsm)
        {
            var rule = FindRule(fsm) as GuestEngineWriterData;
            if (rule == null || rule.WheelHealthIndex < 0) throw new InvalidOperationException("Unknown tyre puncture writer.");
            foreach (var selected in rule.Actions)
                if (selected.State == "Flat friction" && selected.Index == 0 && selected.ActionType == "SetFsmFloat")
                {
                    var action = ValidateWrite(fsm, selected).Action;
                    var amount = Field<FsmFloat>(action, "setValue");
                    if (selected.TargetScalar != "TireHealth" || amount == null || amount.UseVariable || amount.Value != 0
                        || Field<bool>(action, "everyFrame") || !ReferenceEquals(Field<FsmOwnerDefault>(action, "gameObject").GameObject,
                            fsm.FsmVariables.FindFsmGameObject("ThisTire"))) throw new InvalidOperationException("Changed native puncture write.");
                    return action;
                }
            throw new InvalidOperationException("Missing native puncture write.");
        }

        private static void ObserveWheelPuncture(Binding binding, FsmState state)
        {
            if (binding.Rule is not GuestEngineWriterData rule || rule.WheelHealthIndex < 0 || state.Name != "Flat friction") return;
            WheelPunctureWriter(binding.Fsm);
            WorldSyncManager.Instance?.RecordWheelPuncture(binding.Fsm, rule.WheelHealthIndex);
        }

        internal static bool ApplyHostWheelPuncture(PlayMakerFSM fsm, VehicleWheelHealthSourceData source, Rigidbody body)
        {
            if (!Initialize() || GuestSaveGuard.ProtectWorld || CaptureHostWheelHealth(fsm, source, body) <= 0) return false;
            var action = WheelPunctureWriter(fsm);
            var target = ResolveStarterData(action);
            if (!action.Enabled || target == null || ScenePath.Of(target.transform) != source.TargetPath
                || target.gameObject != fsm.FsmVariables.FindFsmGameObject("ThisTire").Value)
                throw new InvalidOperationException("Native puncture target changed.");
            // Run only the audited zero-health write; native host Condition then
            // chooses its own flat/rim physics and publishes the result.
            OilHelper(action, "DoSetFsmFloat");
            return target.FsmVariables.FindFsmFloat("TireHealth").Value == 0;
        }

        internal static float CaptureHostWheelHealth(PlayMakerFSM fsm, VehicleWheelHealthSourceData source, Rigidbody body)
        {
            if (fsm == null || !fsm.enabled || !fsm.Fsm.Initialized || !fsm.Fsm.Started || !fsm.gameObject.activeInHierarchy
                || !fsm.transform.IsChildOf(body.transform) || ScenePath.Of(fsm.transform) != source.Path)
                throw new InvalidOperationException("Native wheel health consumer is not ready.");
            var rule = FindRule(fsm) as GuestEngineWriterData;
            if (rule == null || rule.WheelHealthIndex != source.Wheel || rule.WheelHealthReads.Count != 2)
                throw new InvalidOperationException("Native wheel health reader metadata is missing.");
            var target = fsm.FsmVariables.FindFsmGameObject("ThisTire");
            if (target == null || target.IsNone || !target.UseVariable || target.Value == null
                || !target.Value.transform.IsChildOf(body.transform) || ScenePath.Of(target.Value.transform) != source.TargetPath)
                throw new InvalidOperationException("Native wheel health mount changed.");
            PlayMakerFSM? data = null;
            foreach (var candidate in body.GetComponentsInChildren<PlayMakerFSM>(true))
                if (candidate.FsmName == "Data" && ScenePath.Of(candidate.transform) == source.TargetPath)
                {
                    if (data != null || candidate.gameObject != target.Value) throw new InvalidOperationException("Ambiguous wheel health mount.");
                    data = candidate;
                }
            if (data == null || !data.Fsm.Initialized || !data.Fsm.Started || !data.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Native wheel health mount is not ready.");
            FsmFloat? health = null;
            foreach (var scalar in data.FsmVariables.FloatVariables)
                if (scalar.Name == "TireHealth")
                { if (health != null) throw new InvalidOperationException("Ambiguous native tyre health."); health = scalar; }
            if (health == null || health.IsNone || !health.UseVariable || float.IsNaN(health.Value) || float.IsInfinity(health.Value))
                throw new InvalidOperationException("Native tyre health is not finite or ready.");
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(global, health)) throw new InvalidOperationException("Native tyre health aliases a global.");
            foreach (var scalar in fsm.FsmVariables.FloatVariables)
                if (ReferenceEquals(scalar, health)) throw new InvalidOperationException("Native tyre health aliases consumer scratch.");
            foreach (var reader in rule.WheelHealthReads)
            {
                var read = ValidateWheelHealthReader(fsm, reader).Action;
                var last = read.GetType().GetField("goLastFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var cached = read.GetType().GetField("fsm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (last == null || cached == null || (UnityEngine.Object)last.GetValue(read) == target.Value && !ReferenceEquals(cached.GetValue(read), data))
                    throw new InvalidOperationException("Native wheel health reader cache targets another mount.");
            }
            // Read the same mounted input as vanilla. Never prepare a paused FSM
            // or replay an install/wear action while building a snapshot.
            return health.Value;
        }

        private static bool PrepareWheelHealthInput(Binding binding)
        {
            if (binding.Rule is not GuestEngineWriterData rule || rule.WheelHealthIndex < 0) return true;
            var world = WorldSyncManager.Instance;
            return world == null || !world.TryReadHostWheelHealth(binding.Fsm, rule.WheelHealthIndex, out _, out bool ready) || ready;
        }
        private static Write ValidateWheelHealthReader(PlayMakerFSM fsm, GuestWheelHealthReadData rule)
        {
            var read = FindAction(fsm, rule.State, rule.Index, "GetFsmFloat");
            ValidateWheelHealthSignature(fsm, read.Action);
            return read;
        }

        private static void ValidateWheelHealthSignature(PlayMakerFSM owner, FsmStateAction action)
        {
            var reference = Field<FsmOwnerDefault>(action, "gameObject");
            if (!action.Enabled || !NamedTarget(reference, "ThisTire")
                || !ReferenceEquals(reference.GameObject, owner.FsmVariables.FindFsmGameObject("ThisTire"))
                || !Literal(Field<FsmString>(action, "fsmName"), "Data")
                || !Literal(Field<FsmString>(action, "variableName"), "TireHealth") || !Field<bool>(action, "everyFrame")
                || !SafeWheelHealthOutput(owner, Field<FsmFloat>(action, "storeValue")))
                throw new InvalidOperationException("Native wheel health read signature changed.");
        }

        internal static bool SafeWheelHealthOutput(PlayMakerFSM owner, FsmFloat? output)
        {
            if (output == null || output.IsNone || !ReferenceEquals(output, owner.FsmVariables.FindFsmFloat("Health"))) return false;
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(global, output)) return false;
            var target = owner.FsmVariables.FindFsmGameObject("ThisTire")?.Value;
            if (target != null)
                foreach (var source in target.GetComponents<PlayMakerFSM>())
                    foreach (var scalar in source.FsmVariables.FloatVariables)
                        if (ReferenceEquals(scalar, output)) return false;
            return true;
        }

        private static bool ProjectWheelHealthRead(FsmStateAction action, FsmFloat? output)
        {
            var owner = action.Fsm?.Owner as PlayMakerFSM;
            if (owner == null) return false;
            Bindings.TryGetValue(owner, out var existing);
            if (owner.FsmName != "Condition" && (existing == null || existing.WheelHealthReads.Count == 0)) return false;
            var rule = (FindRule(owner) ?? existing?.Rule) as GuestEngineWriterData;
            if (rule == null || rule.WheelHealthIndex < 0) return false;

            try
            {
                bool selected = false;
                if (existing != null)
                    foreach (var read in existing.WheelHealthReads)
                        if (ReferenceEquals(read.Action, action)) selected = true;
                foreach (var read in rule.WheelHealthReads)
                {
                    var state = FindState(owner, read.State);
                    if (FsmHook.IsNativeAction(state, read.Index, action)) selected = true;
                }
                if (!selected) return false;
                if (existing == null || !Current(existing) || !ReferenceEquals(existing.Rule, rule) || !ReferenceEquals(FindRule(owner), rule))
                    throw new InvalidOperationException("Wheel health read lacks current saved-tyre protection.");
                foreach (var read in existing.WheelHealthReads) ValidateWheelHealthSignature(owner, read.Action);
                foreach (var write in existing.Writes)
                    if (write.Action.Enabled || write.State.ActiveActions.Contains(write.Action))
                        throw new InvalidOperationException("Wheel health read has an active saved-tyre writer.");

                // Native fallback writes the same output as projection. Validate
                // it even without a shared report, including the local driver.
                var world = WorldSyncManager.Instance;
                if (world != null && world.TryReadHostWheelHealth(owner, rule.WheelHealthIndex, out float hostHealth, out bool ready))
                {
                    if (!ready) throw new GuestEngineInputsPendingException("Waiting for the host's tyre health.");
                    output!.Value = hostHealth;
                    return true;
                }
                if (world == null || !world.TryReadWheelHealthInput(owner, rule.WheelHealthIndex, out byte health)) return false;

                // Do not resolve or rewrite the native source/cache. Ownership,
                // missing data and disconnect can immediately resume native lookup.
                output!.Value = health;
                return true;
            }
            catch (Exception error) { Fail(owner, error); return true; }
        }
    }
}
