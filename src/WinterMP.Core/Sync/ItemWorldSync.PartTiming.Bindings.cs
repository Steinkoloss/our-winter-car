using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static void ValidatePartDistributorTimingGraph(PlayMakerFSM hand, PlayMakerFSM data, PartDistributorTimingData rule)
        {
            var mesh = ScenePath.FindRelative(data.transform, rule.MeshPath);
            var pick = data.GetComponent<SphereCollider>();
            RequireFit(hand.gameObject == data.gameObject && hand.FsmName == rule.Fsm && mesh != null && mesh != data.transform
                && pick != null && hand.FsmVariables.FindFsmGameObject(rule.MeshVariable)?.Value == mesh!.gameObject
                && hand.FsmVariables.FindFsmGameObject(rule.MountVariable) != null
                && hand.FsmVariables.FindFsmFloat(rule.RotationVariable) != null
                && hand.FsmVariables.FindFsmFloat(rule.ScrollVariable) != null
                && hand.FsmVariables.FindFsmFloat(rule.TightnessVariable) != null);
            foreach (string state in new[] { rule.ClockwiseState, rule.CounterwiseState })
            {
                var turn = NativePartActions(hand, state, "GetRotation", "FloatAdd");
                ValidateTimingRotation(turn[0], rule.RotationVariable, set: false);
                RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(turn[0], "gameObject"), rule.MeshVariable));
                RotationVariable(turn[1], "floatVariable", rule.RotationVariable);
                RotationConstant(turn[1], "add", state == rule.ClockwiseState ? PartAdjustmentPolicy.TimingStep : -PartAdjustmentPolicy.TimingStep);
                HandScrewFrame(turn[1], false);
                RequireFit(turn[1].GetType().GetField("perSecond")?.GetValue(turn[1]) is bool perSecond && !perSecond);
                RequireFitTransition(hand, state, "FINISHED", rule.WaitState);
            }
            var wait = NativePartActions(hand, rule.WaitState, "SetFloatValue", "FloatClamp", "SetRotation", "SetFsmFloat", "SetFsmFloat", "Wait");
            RotationVariable(wait[0], "floatVariable", rule.ScrollVariable); RotationConstant(wait[0], "floatValue", 0);
            HandScrewFrame(wait[0], false);
            RotationVariable(wait[1], "floatVariable", rule.RotationVariable);
            RotationConstant(wait[1], "minValue", PartAdjustmentPolicy.MinimumTiming);
            RotationConstant(wait[1], "maxValue", PartAdjustmentPolicy.MaximumTiming); HandScrewFrame(wait[1], false);
            ValidateTimingRotation(wait[2], rule.RotationVariable, set: true);
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(wait[2], "gameObject"), rule.MeshVariable));
            for (int i = 3; i <= 4; i++)
            {
                var target = PackageField<FsmOwnerDefault>(wait[i], "gameObject");
                RequireFit(i == 3 ? FitTargetVariable(target, rule.MountVariable) : target?.OwnerOption == OwnerDefaultOption.UseOwner);
                HandScrewScalarTarget(wait[i], data.FsmName, rule.Scalar);
                RotationVariable(wait[i], "setValue", rule.RotationVariable); HandScrewFrame(wait[i], false);
            }
            ValidateTimingWait(wait[5], rule.Cooldown);
            RequireFitTransition(hand, rule.WaitState, "FINISHED", rule.InputState);

            var installed = NativePartActions(data, rule.InstalledPoseState, "SetRotation", "EnableFSM", "ActivateGameObject", "SetProperty");
            ValidateTimingRotation(installed[0], rule.Scalar, set: true);
            RequireFit(data.Fsm.GetOwnerDefaultTarget(PackageField<FsmOwnerDefault>(installed[0], "gameObject")) == mesh!.gameObject);
            ValidatePartDistributorTimingInput(hand, data, rule, pick!);
        }

        private static void ValidateTimingRotation(FsmStateAction action, string scalar, bool set)
        {
            HandScrewFrame(action, false);
            RequireFit(Convert.ToInt32(action.GetType().GetField("space")?.GetValue(action)) == 1
                && PackageField<FsmQuaternion>(action, "quaternion")?.IsNone == true
                && PackageField<FsmVector3>(action, "vector")?.IsNone == true);
            if (set) { RotationConstant(action, "xAngle", 0); RotationConstant(action, "yAngle", 0); }
            else RequireFit(PackageField<FsmFloat>(action, "xAngle")?.IsNone == true
                && PackageField<FsmFloat>(action, "yAngle")?.IsNone == true);
            RotationVariable(action, "zAngle", scalar);
            if (set) RequireFit(action.GetType().GetField("lateUpdate")?.GetValue(action) is bool late && !late);
        }

        private static void ValidateTimingWait(FsmStateAction action, float seconds)
        {
            RotationConstant(action, "time", seconds);
            RequireFit(action.GetType().GetField("realTime")?.GetValue(action) is bool realTime && realTime);
            HandScrewEvent(action, "finishEvent", "FINISHED");
        }

        private static void ValidateTimingCollider(FsmStateAction action, SphereCollider pick, bool enabled)
        {
            var property = PackageField<FsmProperty>(action, "targetProperty");
            RequireFit(property != null && property.setProperty && property.PropertyName == "enabled"
                && property.TargetTypeName == "UnityEngine.SphereCollider" && property.TargetObject.Value == pick
                && !property.BoolParameter.UseVariable && property.BoolParameter.Value == enabled);
            HandScrewFrame(action, false);
        }

        private static void ValidateTimingRead(FsmStateAction action, PlayMakerFSM data, PartDistributorTimingData rule, bool frame)
        {
            RequireFit(PackageField<FsmOwnerDefault>(action, "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner);
            HandScrewScalarTarget(action, data.FsmName, rule.TightnessVariable);
            RotationVariable(action, "storeValue", rule.TightnessVariable); HandScrewFrame(action, frame);
        }

        private static void ValidateTimingPick(FsmStateAction action, bool input)
        {
            var mask = PackageField<FsmInt[]>(action, "layerMask");
            RequireFit(PackageField<FsmOwnerDefault>(action, "GameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                && mask != null && mask.Length == 1 && !mask[0].UseVariable && mask[0].Value == 19
                && PackageField<FsmBool>(action, "invertMask")?.UseVariable == false
                && PackageField<FsmBool>(action, "invertMask")?.Value == false);
            RotationConstant(action, "rayDistance", 1); HandScrewFrame(action, true);
            HandScrewEvent(action, "mouseOver", input ? string.Empty : "FINISHED");
            HandScrewEvent(action, "mouseOff", input ? "FINISHED" : string.Empty);
            HandScrewEvent(action, "mouseDown", string.Empty); HandScrewEvent(action, "mouseUp", string.Empty);
        }

        private static void ValidatePartDistributorTimingInput(PlayMakerFSM hand, PlayMakerFSM data,
            PartDistributorTimingData rule, SphereCollider pick)
        {
            var loose = NativePartActions(hand, rule.TightnessState, "SetProperty", "GetFsmFloat", "FloatCompare");
            ValidateTimingCollider(loose[0], pick, false); ValidateTimingRead(loose[1], data, rule, true);
            HandScrewCompare(loose[2], rule.TightnessVariable, 8, string.Empty, "PROCEED", string.Empty);
            HandScrewFrame(loose[2], true);
            RequireFitTransition(hand, rule.TightnessState, "PROCEED", rule.BindState);
            var bind = NativePartActions(hand, rule.BindState, "GetFsmGameObject", "SetProperty");
            RequireFit(PackageField<FsmOwnerDefault>(bind[0], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner);
            HandScrewScalarTarget(bind[0], data.FsmName, SyncCatalog.ReplacementParts!["installPointVariable"]);
            RequireFit(PackageField<FsmGameObject>(bind[0], "storeValue")?.Name == rule.MountVariable
                && PackageField<FsmGameObject>(bind[0], "storeValue")?.UseVariable == true);
            HandScrewFrame(bind[0], false); ValidateTimingCollider(bind[1], pick, true);
            RequireFitTransition(hand, rule.BindState, "FINISHED", rule.PickState);

            var pickState = NativePartActions(hand, rule.PickState, "SetBoolValue", "MousePickEvent");
            ValidateTimingPick(pickState[1], false);
            RequireFitTransition(hand, rule.PickState, "FINISHED", rule.ToolState);
            var tool = NativePartActions(hand, rule.ToolState, "FloatCompare", "GetFsmFloat", "FloatCompare");
            HandScrewCompare(tool[0], "ToolWrenchSize", 0, string.Empty, "FINISHED", "FINISHED"); HandScrewFrame(tool[0], false);
            ValidateTimingRead(tool[1], data, rule, false);
            HandScrewCompare(tool[2], rule.TightnessVariable, 8, "FINISHED", "PROCEED", "FINISHED"); HandScrewFrame(tool[2], false);
            RequireFitTransition(hand, rule.ToolState, "FINISHED", rule.DelayState);
            RequireFitTransition(hand, rule.ToolState, "PROCEED", rule.InputState);
            ValidateTimingWait(NativePartActions(hand, rule.DelayState, "Wait")[0], 1);
            RequireFitTransition(hand, rule.DelayState, "FINISHED", rule.TightnessState);

            var input = NativePartActions(hand, rule.InputState, "SetBoolValue", "GetAxis", "FloatCompare", "MousePickEvent");
            HandScrewString(PackageField<FsmString>(input[1], "axisName"), "Mouse ScrollWheel");
            RotationConstant(input[1], "multiplier", 1); RotationVariable(input[1], "store", rule.ScrollVariable); HandScrewFrame(input[1], true);
            HandScrewCompare(input[2], rule.ScrollVariable, 0, string.Empty, "CLOCKWISE", "COUNTERWISE"); HandScrewFrame(input[2], true);
            ValidateTimingPick(input[3], true);
            RequireFitTransition(hand, rule.InputState, "CLOCKWISE", rule.ClockwiseState);
            RequireFitTransition(hand, rule.InputState, "COUNTERWISE", rule.CounterwiseState);
            RequireFitTransition(hand, rule.InputState, "FINISHED", rule.PickState);
        }
    }
}
