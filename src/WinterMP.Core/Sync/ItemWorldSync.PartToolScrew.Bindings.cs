using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static void ValidatePartToolScrew(PlayMakerFSM screw, PlayMakerFSM data, PartToolScrewData rule)
        {
            RequireFit(screw.gameObject == data.gameObject && screw.FsmName == rule.Fsm
                && screw.FsmVariables.FindFsmFloat(rule.ScratchVariable) != null);
            foreach (bool tighten in new[] { false, true })
            {
                string state = tighten ? rule.TightenState : rule.LoosenState;
                var turn = NativePartActions(screw, state, "FloatCompare", "AddFsmFloat", "SendEventByName");
                HandScrewCompare(turn[0], rule.ScratchVariable, tighten ? 8 : 0, "OFF", tighten ? "" : "OFF", tighten ? "OFF" : "");
                RequireFit(PackageField<FsmOwnerDefault>(turn[1], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                    && turn[1].GetType().GetField("perSecond")?.GetValue(turn[1]) is bool seconds && !seconds);
                HandScrewScalarTarget(turn[1], data.FsmName, rule.Scalar); RotationConstant(turn[1], "addValue", tighten ? 1 : -1);
                var target = PackageField<FsmEventTarget>(turn[2], "eventTarget");
                RequireFit(target != null && target.target == FsmEventTarget.EventTarget.GameObjectFSM
                    && target.gameObject.OwnerOption == OwnerDefaultOption.UseOwner
                    && !target.sendToChildren.UseVariable && !target.sendToChildren.Value);
                HandScrewString(target!.fsmName, data.FsmName);
                HandScrewString(PackageField<FsmString>(turn[2], "sendEvent"), "BOLTING"); RotationConstant(turn[2], "delay", 0);
                foreach (var action in turn) HandScrewFrame(action, false);
                RequireFitTransition(screw, null, tighten ? "TIGHTEN" : "UNTIGHTEN", state);
                RequireFitTransition(screw, state, "FINISHED", rule.PoseState); RequireFitTransition(screw, state, "OFF", rule.IdleState);
            }
            var pose = NativePartActions(screw, rule.PoseState, "SetLayer", "GetFsmFloat", "SetRandomRotation", "FloatDivide", "SetPosition");
            RequireFit(PackageField<FsmOwnerDefault>(pose[0], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                && Convert.ToInt32(pose[0].GetType().GetField("layer")?.GetValue(pose[0])) == 12);
            ValidateToolScrewRead(pose[1], data, rule);
            RequireFit(PackageField<FsmOwnerDefault>(pose[2], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner);
            foreach (string axis in new[] { "x", "y", "z" })
                RequireFit(PackageField<FsmBool>(pose[2], axis)?.UseVariable == false
                    && PackageField<FsmBool>(pose[2], axis)?.Value == (axis == "z"));
            RotationVariable(pose[3], "floatVariable", rule.ScratchVariable); RotationConstant(pose[3], "divideBy", -400); HandScrewFrame(pose[3], false);
            RequireFit(PackageField<FsmOwnerDefault>(pose[4], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                && Convert.ToInt32(pose[4].GetType().GetField("space")?.GetValue(pose[4])) == 1
                && PackageField<FsmVector3>(pose[4], "vector")?.IsNone == true
                && PackageField<FsmFloat>(pose[4], "x")?.IsNone == true && PackageField<FsmFloat>(pose[4], "y")?.IsNone == true
                && pose[4].GetType().GetField("lateUpdate")?.GetValue(pose[4]) is bool late && !late);
            RotationVariable(pose[4], "z", rule.ScratchVariable); HandScrewFrame(pose[4], false);
            RequireFitTransition(screw, rule.PoseState, "FINISHED", rule.IdleState);
            var idle = NativePartActions(screw, rule.IdleState, "FloatClamp", "GetFsmFloat");
            RotationVariable(idle[0], "floatVariable", rule.ScratchVariable);
            RotationConstant(idle[0], "minValue", 0); RotationConstant(idle[0], "maxValue", 8); HandScrewFrame(idle[0], false);
            ValidateToolScrewRead(idle[1], data, rule);
            ValidatePartScrewNotification(data, rule.Scalar);
        }

        private static void ValidateToolScrewRead(FsmStateAction read, PlayMakerFSM data, PartToolScrewData rule)
        {
            RequireFit(PackageField<FsmOwnerDefault>(read, "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner);
            HandScrewScalarTarget(read, data.FsmName, rule.Scalar); RotationVariable(read, "storeValue", rule.ScratchVariable); HandScrewFrame(read, false);
        }

        private static void ValidateSparkplugTool(PlayMakerFSM tool, PlayMakerFSM raycast, PartToolScrewData rule)
        {
            RequireFit(tool.gameObject == raycast.gameObject && tool.FsmName == rule.ToolFsm && raycast.FsmName == rule.RaycastFsm);
            var names = NativePartActions(tool, "Check bolt Name", "GetName", "StringCompare", "StringCompare");
            RequireFit(PackageField<FsmGameObject>(names[0], "gameObject")?.Name == "Bolt"
                && PackageField<FsmString>(names[0], "storeName")?.Name == "Name");
            HandScrewString(PackageField<FsmString>(names[2], "compareTo"), "spark plug(Clone)");
            HandScrewEvent(names[2], "equalEvent", "SPARKPLUG"); HandScrewFrame(names[2], false);
            RequireFitTransition(tool, "Check bolt Name", "SPARKPLUG", "Sparkplug tool?");
            var size = NativePartActions(tool, "Sparkplug tool?", "FloatCompare")[0];
            RotationConstant(size, "float1", PartToolScrewPolicy.ToolSize); RotationVariable(size, "float2", "ToolWrenchSize");
            RotationConstant(size, "tolerance", PartToolScrewPolicy.ToolTolerance);
            HandScrewEvent(size, "equal", "FINISHED"); HandScrewEvent(size, "lessThan", "FAIL"); HandScrewEvent(size, "greaterThan", "FAIL");
            HandScrewFrame(size, false); RequireFitTransition(tool, "Sparkplug tool?", "FINISHED", "Ratchet?");
            RequireFitTransition(tool, "Sparkplug tool?", "FAIL", "Wait bolt");
            foreach (string state in new[] { "Tighten", "Untighten", "Tighten 2", "Untighten 2" })
            {
                var send = NativePartActions(tool, state, "SendEvent")[0]; var target = PackageField<FsmEventTarget>(send, "eventTarget");
                RequireFit(target != null && target.target == FsmEventTarget.EventTarget.GameObjectFSM
                    && FitTargetVariable(target.gameObject, "Bolt") && !target.sendToChildren.UseVariable && !target.sendToChildren.Value);
                HandScrewString(target!.fsmName, rule.Fsm); HandScrewEvent(send, "sendEvent", state.StartsWith("Tighten", StringComparison.Ordinal) ? "TIGHTEN" : "UNTIGHTEN");
                RotationConstant(send, "delay", 0); HandScrewFrame(send, false);
            }
            var pick = NativePartActions(raycast, "State 1", "MousePick", "SetFsmGameObject");
            RotationConstant(pick[0], "rayDistance", 1); HandScrewFrame(pick[0], true);
            var mask = PackageField<FsmInt[]>(pick[0], "layerMask");
            RequireFit(mask != null && mask.Length == 1 && !mask[0].UseVariable && mask[0].Value == 12
                && PackageField<FsmBool>(pick[0], "invertMask")?.UseVariable == false && PackageField<FsmBool>(pick[0], "invertMask")?.Value == false
                && PackageField<FsmGameObject>(pick[0], "storeGameObject")?.Name == "Bolt"
                && PackageField<FsmOwnerDefault>(pick[1], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner);
            HandScrewScalarTarget(pick[1], rule.ToolFsm, "Bolt"); HandScrewFrame(pick[1], true);
            RequireFit(PackageField<FsmGameObject>(pick[1], "setValue")?.UseVariable == true && PackageField<FsmGameObject>(pick[1], "setValue")?.Name == "Bolt");
        }
    }
}
