using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static void HandScrewString(FsmString? value, string expected) =>
            RequireFit(value != null && !value.UseVariable && value.Value == expected);

        private static void HandScrewEvent(FsmStateAction action, string field, string expected) =>
            RequireFit((PackageField<FsmEvent>(action, field)?.Name ?? string.Empty) == expected);

        private static void HandScrewFrame(FsmStateAction action, bool expected) =>
            RequireFit(action.GetType().GetField("everyFrame")?.GetValue(action) is bool frame && frame == expected);

        private static void HandScrewCompare(FsmStateAction action, string variable, float limit,
            string equal, string less, string greater)
        {
            RotationVariable(action, "float1", variable);
            RotationConstant(action, "float2", limit); RotationConstant(action, "tolerance", 0);
            HandScrewEvent(action, "equal", equal); HandScrewEvent(action, "lessThan", less);
            HandScrewEvent(action, "greaterThan", greater);
        }

        private static void HandScrewScalarTarget(FsmStateAction action, string fsm, string scalar)
        {
            HandScrewString(PackageField<FsmString>(action, "fsmName"), fsm);
            HandScrewString(PackageField<FsmString>(action, "variableName"), scalar);
        }

        private static void HandScrewRead(FsmStateAction action, PlayMakerFSM data, PartHandScrewData rule)
        {
            RequireFit(PackageField<FsmOwnerDefault>(action, "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner);
            HandScrewScalarTarget(action, data.FsmName, rule.Scalar);
            RotationVariable(action, "storeValue", rule.ScratchVariable); HandScrewFrame(action, false);
        }

        private static void ValidatePartHandScrewGraph(PlayMakerFSM screw, PlayMakerFSM data, PartHandScrewData rule)
        {
            RequireFit(screw.gameObject == data.gameObject && screw.FsmName == rule.Fsm
                && screw.FsmVariables.FindFsmFloat(rule.ScratchVariable) != null
                && screw.FsmVariables.FindFsmFloat(rule.RotationVariable) != null
                && data.FsmVariables.FindFsmFloat(rule.Scalar) != null);
            foreach (string state in new[] { rule.TightenState, rule.LoosenState })
            {
                bool tighten = state == rule.TightenState;
                var actions = NativePartActions(screw, state, "FloatCompare", "AddFsmFloat", "SendEventByName");
                foreach (var action in actions) HandScrewFrame(action, false);
                HandScrewCompare(actions[0], rule.ScratchVariable,
                    tighten ? PartHandScrewPolicy.MaximumTightness : PartHandScrewPolicy.MinimumTightness,
                    "OFF", tighten ? string.Empty : "OFF", tighten ? "OFF" : string.Empty);
                RequireFit(PackageField<FsmOwnerDefault>(actions[1], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                    && actions[1].GetType().GetField("perSecond")?.GetValue(actions[1]) is bool perSecond && !perSecond);
                HandScrewScalarTarget(actions[1], data.FsmName, rule.Scalar);
                RotationConstant(actions[1], "addValue", tighten ? PartHandScrewPolicy.TightnessStep : -PartHandScrewPolicy.TightnessStep);
                var target = PackageField<FsmEventTarget>(actions[2], "eventTarget");
                RequireFit(target != null && target.target == FsmEventTarget.EventTarget.GameObjectFSM
                    && target.gameObject.OwnerOption == OwnerDefaultOption.UseOwner
                    && !target.sendToChildren.UseVariable && !target.sendToChildren.Value);
                HandScrewString(target!.fsmName, data.FsmName);
                HandScrewString(PackageField<FsmString>(actions[2], "sendEvent"), SyncCatalog.ReplacementParts!["removeRecheckEvent"]);
                RotationConstant(actions[2], "delay", 0);
                RequireFitTransition(screw, state, "FINISHED", rule.PoseState);
                RequireFitTransition(screw, state, "OFF", rule.WaitState);
            }
            var pose = NativePartActions(screw, rule.PoseState, "GetFsmFloat", "FloatOperator", "FloatDivide", "SetRotation", "SetPosition");
            foreach (var action in pose) HandScrewFrame(action, false);
            HandScrewRead(pose[0], data, rule);
            RotationVariable(pose[1], "float1", rule.ScratchVariable); RotationConstant(pose[1], "float2", 20);
            RequireFit(Convert.ToInt32(pose[1].GetType().GetField("operation")?.GetValue(pose[1])) == 2);
            RotationVariable(pose[1], "storeResult", rule.RotationVariable);
            RotationVariable(pose[2], "floatVariable", rule.ScratchVariable); RotationConstant(pose[2], "divideBy", -400);
            foreach (int index in new[] { 3, 4 })
                RequireFit(PackageField<FsmOwnerDefault>(pose[index], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                    && Convert.ToInt32(pose[index].GetType().GetField("space")?.GetValue(pose[index])) == 1
                    && PackageField<FsmVector3>(pose[index], "vector")?.IsNone == true
                    && pose[index].GetType().GetField("lateUpdate")?.GetValue(pose[index]) is bool late && !late);
            // Unused axes are native "None" fields, so the fitted mount's zero
            // axes are preserved while only its screw axis changes.
            RequireFit(PackageField<FsmQuaternion>(pose[3], "quaternion")?.IsNone == true
                && PackageField<FsmFloat>(pose[3], "xAngle")?.IsNone == true
                && PackageField<FsmFloat>(pose[3], "yAngle")?.IsNone == true
                && PackageField<FsmFloat>(pose[4], "x")?.IsNone == true
                && PackageField<FsmFloat>(pose[4], "y")?.IsNone == true);
            RotationVariable(pose[3], "zAngle", rule.RotationVariable); RotationVariable(pose[4], "z", rule.ScratchVariable);
            RequireFitTransition(screw, rule.PoseState, "FINISHED", rule.WaitState);
            foreach (string state in new[] { rule.InitState, rule.WaitState })
            {
                var wait = NativePartActions(screw, state, "Wait")[0];
                RotationConstant(wait, "time", rule.Cooldown);
                RequireFit(wait.GetType().GetField("realTime")?.GetValue(wait) is bool realTime && realTime);
                HandScrewEvent(wait, "finishEvent", "FINISHED");
                RequireFitTransition(screw, state, "FINISHED", state == rule.InitState ? rule.PoseState : rule.PickState);
            }
            ValidatePartHandScrewInput(screw, data, rule);
            ValidatePartScrewNotification(data, rule.Scalar);
        }

        private static void ValidatePartHandScrewInput(PlayMakerFSM screw, PlayMakerFSM data, PartHandScrewData rule)
        {
            var pick = NativePartActions(screw, rule.PickState, "SetBoolValue", "MousePickEvent");
            var input = NativePartActions(screw, rule.InputState, "SetBoolValue", "FloatClamp", "GetFsmFloat", "GetAxis", "FloatCompare", "MousePickEvent");
            foreach (var action in new[] { pick[1], input[5] })
            {
                var mask = PackageField<FsmInt[]>(action, "layerMask");
                RequireFit(PackageField<FsmOwnerDefault>(action, "GameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                    && mask != null && mask.Length == 1 && !mask[0].UseVariable && mask[0].Value == 19
                    && PackageField<FsmBool>(action, "invertMask")?.UseVariable == false
                    && PackageField<FsmBool>(action, "invertMask")?.Value == false);
                RotationConstant(action, "rayDistance", 1); HandScrewFrame(action, true);
                HandScrewEvent(action, "mouseOver", action == pick[1] ? "FINISHED" : string.Empty);
                HandScrewEvent(action, "mouseOff", action == input[5] ? "OFF" : string.Empty);
                HandScrewEvent(action, "mouseDown", string.Empty); HandScrewEvent(action, "mouseUp", string.Empty);
            }
            RotationVariable(input[1], "floatVariable", rule.ScratchVariable);
            RotationConstant(input[1], "minValue", PartHandScrewPolicy.MinimumTightness);
            RotationConstant(input[1], "maxValue", PartHandScrewPolicy.MaximumTightness); HandScrewFrame(input[1], false);
            HandScrewRead(input[2], data, rule);
            HandScrewString(PackageField<FsmString>(input[3], "axisName"), "Mouse ScrollWheel");
            RotationConstant(input[3], "multiplier", 1); RotationVariable(input[3], "store", "Scroll");
            HandScrewFrame(input[3], true); HandScrewFrame(input[4], true);
            HandScrewCompare(input[4], "Scroll", 0, string.Empty, "UNTIGHTEN", "TIGHTEN");
            var tool = NativePartActions(screw, rule.ToolState, "FloatCompare")[0];
            HandScrewFrame(tool, false); HandScrewCompare(tool, "ToolWrenchSize", 0, "PROCEEDS", "FINISHED", "FINISHED");
            RequireFitTransition(screw, rule.PickState, "FINISHED", rule.ToolState);
            RequireFitTransition(screw, rule.ToolState, "PROCEEDS", rule.InputState);
            RequireFitTransition(screw, rule.ToolState, "FINISHED", rule.WaitState);
            RequireFitTransition(screw, rule.InputState, "TIGHTEN", rule.TightenState);
            RequireFitTransition(screw, rule.InputState, "UNTIGHTEN", rule.LoosenState);
            RequireFitTransition(screw, rule.InputState, "OFF", rule.PickState);
        }

        private static void ValidatePartScrewNotification(PlayMakerFSM data, string scalar)
        {
            var c = SyncCatalog.ReplacementParts!;
            RequireFitTransition(data, null, c["removeRecheckEvent"], c["removeTightnessState"]);
            var tight = NativePartActions(data, c["removeTightnessState"], "SetFsmFloat", "FloatCompare");
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(tight[0], "gameObject"), c["installPointVariable"]));
            HandScrewScalarTarget(tight[0], data.FsmName, scalar); RotationVariable(tight[0], "setValue", scalar);
            HandScrewFrame(tight[0], false); HandScrewFrame(tight[1], false);
            HandScrewCompare(tight[1], scalar, 1, "PROCEED", "BACK", "PROCEED");
            RequireFitTransition(data, c["removeTightnessState"], "PROCEED", c["removeBoltedState"]);
            RequireFitTransition(data, c["removeTightnessState"], "BACK", c["removeUnboltedState"]);
            foreach (string state in new[] { c["removeBoltedState"], c["removeUnboltedState"] })
            {
                var actions = new List<FsmStateAction>();
                foreach (var action in FsmHook.FindState(data, state)!.Actions)
                    if (!(action is FsmHookAction)) actions.Add(action);
                // The installed game leaves its old collider toggle disabled.
                // Enabling it would remove the hand pick after the first turn.
                RequireFit(actions.Count == 2 && actions[0].Enabled && actions[0].GetType().Name == "SetFsmBool"
                    && !actions[1].Enabled && actions[1].GetType().Name == "SetProperty"
                    && FitTargetVariable(PackageField<FsmOwnerDefault>(actions[0], "gameObject"), c["installPointVariable"]));
                HandScrewScalarTarget(actions[0], data.FsmName, "Bolted"); HandScrewFrame(actions[0], false);
                RequireFit(PackageField<FsmBool>(actions[0], "setValue")?.UseVariable == false
                    && PackageField<FsmBool>(actions[0], "setValue")?.Value == (state == c["removeBoltedState"]));
            }
        }
    }
}
