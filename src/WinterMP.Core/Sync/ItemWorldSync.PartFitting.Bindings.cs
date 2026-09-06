using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static void ValidatePartFitEntry(ReplacementBinding part)
        {
            if (part.FitValidated || part.FitFailed) return;
            var c = SyncCatalog.ReplacementParts!;
            try
            {
                if (part.Factory.Rule.SlotCount != 0)
                { ValidatePartSlotEntry(part, c); part.FitValidated = true; return; }
                var state = PackageStateActions(part.Data, c["fitCheckState"], "GetFsmBool", "BoolTest", "SetFsmGameObject", "SendEventByName");
                RequireFitTransition(part.Data, null, c["fitEvent"], c["fitCheckState"]);
                RequireFitTransition(part.Data, c["fitCheckState"], "FINISHED", c["itemStopState"]);
                foreach (int index in new[] { 0, 2 })
                    RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(state.Actions[index], "gameObject"), c["installPointVariable"])
                        && PackageField<FsmString>(state.Actions[index], "fsmName")?.Value == c["itemFsm"]);
                RequireFit(PackageField<FsmString>(state.Actions[0], "variableName")?.Value == c["mountInstalledVariable"]
                    && PackageField<FsmBool>(state.Actions[0], "storeValue")?.Name == c["installedVariable"]
                    && PackageField<FsmBool>(state.Actions[1], "boolVariable")?.Name == c["installedVariable"]
                    && PackageField<FsmEvent>(state.Actions[1], "isTrue")?.Name == "FINISHED"
                    && string.IsNullOrEmpty(PackageField<FsmEvent>(state.Actions[1], "isFalse")?.Name)
                    && PackageField<FsmString>(state.Actions[2], "variableName")?.Value == c["mountPartVariable"]
                    && PackageField<FsmGameObject>(state.Actions[2], "setValue")?.Name == c["fitOwnerVariable"]
                    && PackageField<FsmGameObject>(state.Actions[2], "setValue")?.UseVariable == true);
                var send = state.Actions[3]; var target = PackageField<FsmEventTarget>(send, "eventTarget");
                RequireFit(target != null && target.target == FsmEventTarget.EventTarget.GameObjectFSM
                    && FitTargetVariable(target.gameObject, c["installPointVariable"]) && target.fsmName.Value == c["itemFsm"]
                    && PackageField<FsmString>(send, "sendEvent")?.Value == c["fitMountCheckEvent"]
                    && PackageField<FsmFloat>(send, "delay")?.Value == 0);
                foreach (var action in state.Actions) RequireFitOneShot(action);
                part.FitValidated = true;
            }
            catch (Exception e) { DisablePartFit(part, e.Message); }
        }

        private PlayMakerFSM? GetPartFitMount(ReplacementBinding part, out byte slot)
        {
            slot = 0;
            var c = SyncCatalog.ReplacementParts;
            if (c == null || part.Factory.Failed || part.Data == null || part.FitFailed) return null;
            if (part.Data.FsmVariables.FindFsmGameObject(c["fitOwnerVariable"])?.Value != part.Data.gameObject) return null;
            ValidatePartFitEntry(part);
            if (!part.FitValidated) return null;
            if (part.Factory.Rule.SlotCount != 0) return GetPartSlotMount(part, out slot);
            var install = part.Data.FsmVariables.FindFsmGameObject(c["installPointVariable"])?.Value;
            if (install == null) return null;
            bool fixedMount = false;
            foreach (var reference in part.Factory.Rule.References)
                if (reference.Target == c["installPointVariable"]
                    && part.Factory.Fsm.FsmVariables.FindFsmGameObject(reference.Source)?.Value == install) fixedMount = true;
            if (!fixedMount) return null;
            if (part.FitMount != null && part.FitMount.gameObject == install) return part.FitMount;
            foreach (var mount in install.GetComponents<PlayMakerFSM>())
            {
                if (mount.FsmName != c["itemFsm"] || !PartFitMountReady(mount)) continue;
                try
                {
                    ValidatePartFitMount(mount, c);
                    part.FitMount = mount;
                    return mount;
                }
                catch (Exception e) { DisablePartFit(part, e.Message); return null; }
            }
            return null;
        }

        private static void ValidatePartFitMount(PlayMakerFSM mount, ReplacementPartsData c, bool slots = false)
        {
            RequireFit(mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"]) != null
                && mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"]) != null
                && mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"]) != null);
            var idle = PackageStateActions(mount, c["fitMountIdleState"], "GetOwner", "SetBoolValue");
            RequireFit(PackageField<FsmGameObject>(idle.Actions[0], "storeGameObject")?.Name == c["mountPointVariable"]);
            RequireFitTransition(mount, c["fitMountIdleState"], c["fitMountCheckEvent"], c["fitAllowState"]);
            RequireFitTransition(mount, c["fitAllowState"], c["fitConfirmEvent"], c["fitFarState"]);
            RequireFitTransition(mount, c["fitAllowState"], "FINISHED", c["fitMountIdleState"]);
            RequireFitTransition(mount, null, c["fitCancelEvent"], c["fitMountIdleState"]);
            var allow = FsmHook.FindState(mount, c["fitAllowState"])!;
            RequireFit(allow.Actions.Length > 0);
            foreach (var action in allow.Actions)
            {
                string name = action.GetType().Name;
                RequireFit(action.Enabled && (name == "GetFsmBool" || name == "BoolTest" || name == "BoolNoneTrue"
                    || name == "BoolAnyTrue" || name == "BoolAllTrue"));
                RequireFitOneShot(action);
            }
            if (slots) { ValidatePartSlotMount(mount, c); return; }
            ValidatePartFitDistance(mount, c["fitFarState"], c, false);
            ValidatePartFitDistance(mount, c["fitNearState"], c, true);
            RequireFitTransition(mount, c["fitFarState"], "FINISHED", c["fitNearState"]);
            RequireFitTransition(mount, c["fitNearState"], "FINISHED", c["fitFarState"]);
            RequireFitTransition(mount, c["fitNearState"], c["fitConfirmEvent"], c["fitInstallState"]);
            var install = PackageStateActions(mount, c["fitInstallState"], "SetBoolValue", "SetFsmInt", "SendEventByName");
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(install.Actions[1], "gameObject"), c["mountPartVariable"])
                && PackageField<FsmString>(install.Actions[1], "fsmName")?.Value == c["itemFsm"]
                && PackageField<FsmString>(install.Actions[1], "variableName")?.Value == c["assemblyVariable"]);
            var assembly = PackageField<FsmInt>(install.Actions[1], "setValue");
            RequireFit(assembly != null && !assembly.UseVariable && assembly.Value > 0);
            var send = install.Actions[2]; var target = PackageField<FsmEventTarget>(send, "eventTarget");
            RequireFit(target != null && target.target == FsmEventTarget.EventTarget.GameObjectFSM
                && FitTargetVariable(target.gameObject, c["mountPartVariable"]) && target.fsmName.Value == c["itemFsm"]
                && PackageField<FsmString>(send, "sendEvent")?.Value == c["fitInstallEvent"]
                && PackageField<FsmFloat>(send, "delay")?.Value == 0);
            foreach (var action in install.Actions) RequireFitOneShot(action);
        }

        private static void ValidatePartFitDistance(PlayMakerFSM mount, string name, ReplacementPartsData c, bool near)
        {
            var state = PackageStateActions(mount, name, "SetBoolValue", "GetDistance", "FloatCompare", "GetMouseButtonDown", "GetMouseButtonDown");
            var distance = state.Actions[1]; var compare = state.Actions[2];
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(distance, "gameObject"), c["mountPartVariable"])
                && PackageField<FsmGameObject>(distance, "target")?.Name == c["mountPointVariable"]
                && PackageField<FsmGameObject>(distance, "target")?.UseVariable == true
                && PackageField<FsmFloat>(distance, "storeResult")?.Name == c["fitDistanceVariable"]
                && PackageField<FsmFloat>(compare, "float1")?.Name == c["fitDistanceVariable"]
                && PackageField<FsmFloat>(compare, "float2")?.Name == c["fitToleranceVariable"]
                && PackageField<FsmFloat>(compare, "float2")?.UseVariable == true
                && PackageField<FsmFloat>(compare, "tolerance")?.Value == 0
                && PackageField<FsmEvent>(compare, near ? "greaterThan" : "lessThan")?.Name == "FINISHED"
                && string.IsNullOrEmpty(PackageField<FsmEvent>(compare, "equal")?.Name)
                && string.IsNullOrEmpty(PackageField<FsmEvent>(compare, near ? "lessThan" : "greaterThan")?.Name));
            for (int i = 3; i < 5; i++)
                RequireFit(Convert.ToInt32(state.Actions[i].GetType().GetField("button")!.GetValue(state.Actions[i])) == i - 3
                    && PackageField<FsmEvent>(state.Actions[i], "sendEvent")?.Name == (near && i == 3 ? c["fitConfirmEvent"] : c["fitCancelEvent"]));
        }

        private static void RequireFitTransition(PlayMakerFSM fsm, string? from, string evt, string to)
        {
            var transitions = from == null ? fsm.Fsm.GlobalTransitions : FsmHook.FindState(fsm, from)?.Transitions;
            if (transitions != null)
                foreach (var transition in transitions)
                    if (transition.EventName == evt) { RequireFit(transition.ToState == to); return; }
            throw new InvalidOperationException("Missing fitting transition: " + from + " / " + evt);
        }
        private static void RequireFitOneShot(FsmStateAction action)
        {
            object? value = action.GetType().GetField("everyFrame")?.GetValue(action);
            RequireFit(!(value is bool frame && frame) && !(value is FsmBool fsmFrame && fsmFrame.Value));
        }
        private static void RequireFit(bool valid)
        {
            if (!valid) throw new InvalidOperationException("Native part-fitting bindings changed.");
        }
        private static bool FitTargetVariable(FsmOwnerDefault? target, string variable) => target != null
            && target.OwnerOption == OwnerDefaultOption.SpecifyGameObject && target.GameObject.UseVariable && target.GameObject.Name == variable;
        private static void DisablePartFit(ReplacementBinding part, string reason)
        {
            if (part.FitFailed) return;
            part.FitFailed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: fitting unavailable for " + part.NativeId + ": " + reason);
            SyncEventLog.Record("part-fit-disabled", part.NativeId + " " + reason);
        }
        private static void ValidatePartSlotEntry(ReplacementBinding part, ReplacementPartsData c)
        {
            RequireFit(part.Data.FsmVariables.FindFsmString(c["slotReferenceVariable"])?.Value == part.Factory.Rule.SlotReference);
            RequireFitTransition(part.Data, null, c["fitEvent"], c["fitFarState"]);
            RequireFitTransition(part.Data, c["fitFarState"], "FINISHED", c["itemStopState"]);
            var actions = NativePartActions(part.Data, c["fitFarState"], "SetFsmGameObject", "SetFsmString", "SendEventByName");
            RequireSlotTarget(actions[0], c["slotDatabaseVariable"], c["slotInstallerFsm"], c["mountPartVariable"]);
            RequireFit(PackageField<FsmGameObject>(actions[0], "setValue")?.UseVariable == true
                && PackageField<FsmGameObject>(actions[0], "setValue")?.Name == c["fitOwnerVariable"]);
            RequireSlotTarget(actions[1], c["slotDatabaseVariable"], c["slotInstallerFsm"], c["slotInstallerReferenceVariable"]);
            RequireFit(PackageField<FsmString>(actions[1], "setValue")?.UseVariable == true
                && PackageField<FsmString>(actions[1], "setValue")?.Name == c["slotReferenceVariable"]);
            RequireSlotSend(actions[2], c["slotDatabaseVariable"], c["slotInstallerFsm"], c["fitMountCheckEvent"]);
            foreach (var action in actions) RequireFitOneShot(action);
        }

        private static void ValidatePartSlotInstaller(PlayMakerFSM installer, ReplacementPartsData c)
        {
            RequireFitTransition(installer, null, c["fitMountCheckEvent"], c["fitFarState"]);
            foreach (string state in new[] { c["fitFarState"], c["fitNearState"] })
                RequireFitTransition(installer, state, c["slotStopEvent"], c["slotInstallerIdleState"]);
            RequireFitTransition(installer, c["fitFarState"], "FINISHED", c["fitNearState"]);
            RequireFitTransition(installer, c["fitNearState"], "FINISHED", c["fitFarState"]);
            var idle = NativePartActions(installer, c["slotInstallerIdleState"], "SetBoolValue", "SetGameObject", "SetGameObject", "SetIntValue");
            RequireFit(PackageField<FsmBool>(idle[0], "boolVariable")?.Name == c["mountInstalledVariable"]
                && PackageField<FsmBool>(idle[0], "boolValue")?.Value == false);
            for (int i = 1; i <= 2; i++)
                RequireFit(PackageField<FsmGameObject>(idle[i], "variable")?.Name == c[i == 1 ? "mountPartVariable" : "mountPointVariable"]
                    && PackageField<FsmGameObject>(idle[i], "gameObject")?.UseVariable == false
                    && PackageField<FsmGameObject>(idle[i], "gameObject")?.Value == null);
            RequireFit(PackageField<FsmInt>(idle[3], "intVariable")?.Name == c["slotInstallerIndexVariable"]
                && PackageField<FsmInt>(idle[3], "intValue")?.Value == 0);
            foreach (var action in idle) RequireFitOneShot(action);
            var far = NativePartActions(installer, c["fitFarState"], "SetFsmBool", "ArrayListGetClosestGameObject", "GetFsmBool", "BoolTest", "GetDistance", "FloatCompare");
            var near = NativePartActions(installer, c["fitNearState"], "SetFsmInt", "SetFsmBool", "SendEventByName", "GetDistance", "FloatCompare");
            RequireSlotTarget(far[0], c["mountPointVariable"], c["itemFsm"], c["slotAllowVariable"]);
            RequireFit(PackageField<FsmBool>(far[0], "setValue")?.Value == false);
            RequireSlotTarget(far[2], c["mountPointVariable"], c["itemFsm"], c["mountInstalledVariable"]);
            RequireFit(PackageField<FsmBool>(far[2], "storeValue")?.Name == c["mountInstalledVariable"]
                && PackageField<FsmBool>(far[3], "boolVariable")?.Name == c["mountInstalledVariable"]
                && PackageField<FsmEvent>(far[3], "isTrue")?.Name == c["slotStopEvent"]
                && string.IsNullOrEmpty(PackageField<FsmEvent>(far[3], "isFalse")?.Name));
            foreach (int i in new[] { 0, 2, 3 }) RequireFitOneShot(far[i]);
            var closest = far[1];
            RequireFit(PackageField<FsmOwnerDefault>(closest, "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                && PackageField<FsmString>(closest, "reference")?.Name == c["slotInstallerReferenceVariable"]
                && PackageField<FsmGameObject>(closest, "distanceFrom")?.Name == c["mountPartVariable"]
                && PackageField<FsmVector3>(closest, "orDistanceFromVector3")?.UseVariable == false
                && PackageField<FsmVector3>(closest, "orDistanceFromVector3")?.Value == Vector3.zero
                && PackageField<FsmGameObject>(closest, "closestGameObject")?.Name == c["mountPointVariable"]
                && PackageField<FsmInt>(closest, "closestIndex")?.Name == c["slotInstallerIndexVariable"]);
            RequireSlotTarget(near[0], c["mountPointVariable"], c["itemFsm"], c["assemblyVariable"]);
            RequireFit(PackageField<FsmInt>(near[0], "setValue")?.Name == c["slotInstallerIndexVariable"]
                && PackageField<FsmInt>(near[0], "setValue")?.UseVariable == true);
            RequireSlotTarget(near[1], c["mountPointVariable"], c["itemFsm"], c["slotAllowVariable"]);
            RequireFit(PackageField<FsmBool>(near[1], "setValue")?.Value == true);
            RequireSlotSend(near[2], c["mountPointVariable"], c["itemFsm"], c["fitMountCheckEvent"]);
            RequireFitOneShot(near[0]); RequireFitOneShot(near[2]);
            ValidateSlotDistance(far[4], far[5], c, false);
            ValidateSlotDistance(near[3], near[4], c, true);
        }

        private static void ValidateSlotDistance(FsmStateAction distance, FsmStateAction compare, ReplacementPartsData c, bool near)
        {
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(distance, "gameObject"), c["mountPartVariable"])
                && PackageField<FsmGameObject>(distance, "target")?.Name == c["mountPointVariable"]
                && PackageField<FsmFloat>(distance, "storeResult")?.Name == c["fitDistanceVariable"]
                && PackageField<FsmFloat>(compare, "float1")?.Name == c["fitDistanceVariable"]
                && PackageField<FsmFloat>(compare, "float2")?.UseVariable == true
                && PackageField<FsmFloat>(compare, "float2")?.Name == c["fitToleranceVariable"]
                && PackageField<FsmFloat>(compare, "tolerance")?.Value == 0
                && PackageField<FsmEvent>(compare, near ? "greaterThan" : "lessThan")?.Name == "FINISHED"
                && string.IsNullOrEmpty(PackageField<FsmEvent>(compare, "equal")?.Name)
                && string.IsNullOrEmpty(PackageField<FsmEvent>(compare, near ? "lessThan" : "greaterThan")?.Name));
        }

        private static void ValidatePartSlotMount(PlayMakerFSM mount, ReplacementPartsData c)
        {
            RequireFit(mount.FsmVariables.FindFsmBool(c["slotAllowVariable"]) != null
                && mount.FsmVariables.FindFsmInt(c["assemblyVariable"]) != null);
            foreach (bool near in new[] { false, true })
            {
                string name = c[near ? "fitNearState" : "fitFarState"];
                var actions = NativePartActions(mount, name, "SetBoolValue", "BoolTest", "GetMouseButtonDown", "GetMouseButtonDown");
                RequireFit(PackageField<FsmBool>(actions[1], "boolVariable")?.Name == c["slotAllowVariable"]
                    && PackageField<FsmEvent>(actions[1], near ? "isFalse" : "isTrue")?.Name == "FINISHED"
                    && string.IsNullOrEmpty(PackageField<FsmEvent>(actions[1], near ? "isTrue" : "isFalse")?.Name));
                for (int i = 2; i < 4; i++)
                    RequireFit(Convert.ToInt32(actions[i].GetType().GetField("button")!.GetValue(actions[i])) == i - 2
                        && PackageField<FsmEvent>(actions[i], "sendEvent")?.Name == c[near && i == 2 ? "fitConfirmEvent" : "fitCancelEvent"]);
                RequireFitTransition(mount, name, "FINISHED", c[near ? "fitFarState" : "fitNearState"]);
            }
            RequireFitTransition(mount, c["fitNearState"], c["fitConfirmEvent"], c["fitInstallState"]);
            var install = NativePartActions(mount, c["fitInstallState"], "SetBoolValue", "GetFsmGameObject", "SetFsmGameObject", "SendEventByName", "SetFsmInt", "SendEventByName");
            RequireSlotTarget(install[1], c["slotDatabaseVariable"], c["slotInstallerFsm"], c["mountPartVariable"]);
            RequireFit(PackageField<FsmGameObject>(install[1], "storeValue")?.Name == c["mountPartVariable"]);
            RequireSlotTarget(install[2], c["mountPartVariable"], c["itemFsm"], c["installPointVariable"]);
            RequireFit(PackageField<FsmGameObject>(install[2], "setValue")?.Name == c["mountPointVariable"]
                && PackageField<FsmGameObject>(install[2], "setValue")?.UseVariable == true);
            RequireSlotSend(install[3], c["slotDatabaseVariable"], c["slotInstallerFsm"], c["slotStopEvent"]);
            RequireSlotTarget(install[4], c["mountPartVariable"], c["itemFsm"], c["assemblyVariable"]);
            RequireFit(PackageField<FsmInt>(install[4], "setValue")?.Name == c["assemblyVariable"]
                && PackageField<FsmInt>(install[4], "setValue")?.UseVariable == true);
            RequireSlotSend(install[5], c["mountPartVariable"], c["itemFsm"], c["fitInstallEvent"]);
            foreach (var action in install) RequireFitOneShot(action);
        }

        private static void RequireSlotTarget(FsmStateAction action, string target, string fsm, string variable)
        {
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(action, "gameObject"), target)
                && PackageField<FsmString>(action, "fsmName")?.Value == fsm
                && PackageField<FsmString>(action, "variableName")?.Value == variable);
        }

        private static void RequireSlotSend(FsmStateAction action, string targetVariable, string fsm, string evt)
        {
            var target = PackageField<FsmEventTarget>(action, "eventTarget");
            RequireFit(target != null && target.target == FsmEventTarget.EventTarget.GameObjectFSM
                && FitTargetVariable(target.gameObject, targetVariable) && target.fsmName.Value == fsm
                && PackageField<FsmString>(action, "sendEvent")?.Value == evt
                && PackageField<FsmFloat>(action, "delay")?.Value == 0);
        }
        private static FsmStateAction[] NativePartActions(PlayMakerFSM data, string state, params string[] types)
        {
            var native = new List<FsmStateAction>();
            var found = FsmHook.FindState(data, state) ?? throw new InvalidOperationException("Missing native part state: " + state);
            foreach (var action in found.Actions) if (!(action is FsmHookAction)) native.Add(action);
            RequireFit(native.Count == types.Length);
            for (int i = 0; i < types.Length; i++) RequireFit(native[i].Enabled && native[i].GetType().Name == types[i]);
            return native.ToArray();
        }

    }
}
