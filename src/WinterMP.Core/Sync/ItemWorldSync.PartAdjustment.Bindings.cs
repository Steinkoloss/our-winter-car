using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class PartHandRotation
        {
            internal PlayMakerFSM Hand = null!, Bolt = null!;
            internal SphereCollider Pick = null!;
            internal FsmFloat Scalar = null!;
            internal int ScalarIndex;
            internal float NextRequestAt;
        }

        private static PartHandRotation? GetPartHandRotation(ReplacementBinding part)
        {
            var rule = part.Factory.Rule.HandRotation;
            if (rule == null || part.HandRotationFailed || part.Data == null) return null;
            if (part.HandRotation != null) return part.HandRotation;
            try
            {
                var pivot = ScenePath.FindRelative(part.Data.transform, rule.Path);
                var boltObject = ScenePath.FindRelative(part.Data.transform, rule.BoltPath);
                RequireFit(pivot != null && boltObject != null);
                var hand = FindPartAdjustmentFsm(pivot!, rule.Fsm);
                var bolt = FindPartAdjustmentFsm(boltObject!, "Screw");
                var pick = pivot!.GetComponent<SphereCollider>();
                var scalar = part.Data.FsmVariables.FindFsmFloat(rule.Scalar);
                RequireFit(pick != null && scalar != null && pick.radius > 0 && pivot.gameObject.layer == 19
                    && hand.FsmVariables.FindFsmGameObject("ThisPart")?.Value == part.Data.gameObject
                    && bolt.FsmVariables.FindFsmGameObject("ThisPart")?.Value == part.Data.gameObject
                    && bolt.FsmVariables.FindFsmInt("BoltTightness") != null
                    && bolt.FsmVariables.FindFsmObject("ColliderPivot")?.Value == pick);
                ValidatePartRotationGraph(hand, part.Data, rule.Scalar, SyncCatalog.ReplacementParts!["installPointVariable"]);
                var result = new PartHandRotation { Hand = hand, Bolt = bolt, Pick = pick!, Scalar = scalar!,
                    ScalarIndex = Array.IndexOf(part.Factory.Rule.Scalars, rule.Scalar) };
                RequireFit(result.ScalarIndex >= 0);
                if (!part.Replica)
                {
                    RequireFit(FsmHook.EnsureRemoteEntry(hand, "Clockwise") && FsmHook.EnsureRemoteEntry(hand, "Counterwise"));
                }
                part.HandRotation = result;
                return result;
            }
            catch (Exception e)
            {
                part.HandRotationFailed = true;
                WinterMPPlugin.Log.LogWarning("WorldSync: hand rotation disabled for " + part.NativeId + ": " + e.Message);
                SyncEventLog.Record("part-adjust-disabled", part.NativeId + " " + e.Message);
                return null;
            }
        }

        private static PlayMakerFSM FindPartAdjustmentFsm(Transform node, string name)
        {
            PlayMakerFSM? result = null;
            foreach (var fsm in node.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == name) { RequireFit(result == null); result = fsm; }
            return result ?? throw new InvalidOperationException("Missing hand rotation FSM.");
        }

        private static void RotationConstant(FsmStateAction action, string field, float expected)
        {
            var value = PackageField<FsmFloat>(action, field);
            RequireFit(value != null && !value.UseVariable && value.Value == expected);
        }

        private static void RotationVariable(FsmStateAction action, string field, string name)
        {
            var value = PackageField<FsmFloat>(action, field);
            RequireFit(value != null && value.UseVariable && value.Name == name);
        }

        private static void ValidatePartRotationGraph(PlayMakerFSM hand, PlayMakerFSM data, string scalar, string installPointVariable)
        {
            foreach (string stateName in new[] { "Clockwise", "Counterwise" })
            {
                var actions = NativePartActions(hand, stateName, "SetFloatValue", "GetRotation", "FloatAdd", "FloatClamp", "SetRotation");
                foreach (var action in actions) RequireFitOneShot(action);
                RotationVariable(actions[0], "floatVariable", "Scroll"); RotationConstant(actions[0], "floatValue", 0);
                RotationVariable(actions[1], "yAngle", "Rotation");
                RotationVariable(actions[2], "floatVariable", "Rotation");
                RotationConstant(actions[2], "add", stateName == "Clockwise" ? PartAdjustmentPolicy.RotationStep : -PartAdjustmentPolicy.RotationStep);
                RequireFit(actions[2].GetType().GetField("perSecond")?.GetValue(actions[2]) is bool perSecond && !perSecond);
                RotationVariable(actions[3], "floatVariable", "Rotation");
                RotationConstant(actions[3], "minValue", PartAdjustmentPolicy.MinimumRotation);
                RotationConstant(actions[3], "maxValue", PartAdjustmentPolicy.MaximumRotation);
                RotationVariable(actions[4], "yAngle", "Rotation");
                RotationConstant(actions[4], "xAngle", 0); RotationConstant(actions[4], "zAngle", 0);
                RequireFit(actions[4].GetType().GetField("lateUpdate")?.GetValue(actions[4]) is bool lateUpdate && !lateUpdate);
                foreach (int index in new[] { 1, 4 })
                    RequireFit(PackageField<FsmOwnerDefault>(actions[index], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                        && Convert.ToInt32(actions[index].GetType().GetField("space")?.GetValue(actions[index])) == 1
                        && PackageField<FsmQuaternion>(actions[index], "quaternion")?.IsNone == true
                        && PackageField<FsmVector3>(actions[index], "vector")?.IsNone == true);
                RequireFitTransition(hand, stateName, "FINISHED", "Wait");
            }
            var wait = NativePartActions(hand, "Wait", "SetFsmFloat", "SetFsmFloat", "Wait");
            for (int i = 0; i < 2; i++)
            {
                RequireFitOneShot(wait[i]);
                RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(wait[i], "gameObject"), i == 0 ? "ThisPart" : "VINP")
                    && PackageField<FsmString>(wait[i], "fsmName")?.Value == data.FsmName
                    && PackageField<FsmString>(wait[i], "fsmName")?.UseVariable == false
                    && PackageField<FsmString>(wait[i], "variableName")?.Value == scalar
                    && PackageField<FsmString>(wait[i], "variableName")?.UseVariable == false);
                RotationVariable(wait[i], "setValue", "Rotation");
            }
            RotationConstant(wait[2], "time", .1f);
            RequireFit(wait[2].GetType().GetField("realTime")?.GetValue(wait[2]) is bool realTime && realTime
                && PackageField<FsmEvent>(wait[2], "finishEvent")?.Name == "FINISHED");
            RequireFitTransition(hand, "Wait", "FINISHED", "Input");
            var init = NativePartActions(hand, "State 2", "GetFsmGameObject");
            RequireFitOneShot(init[0]);
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(init[0], "gameObject"), "ThisPart")
                && PackageField<FsmString>(init[0], "fsmName")?.Value == data.FsmName
                && PackageField<FsmString>(init[0], "variableName")?.Value == installPointVariable
                && PackageField<FsmGameObject>(init[0], "storeValue")?.Name == "VINP");
            var pick = NativePartActions(hand, "Wait Player", "SetBoolValue", "MousePickEvent")[1];
            var mask = PackageField<FsmInt[]>(pick, "layerMask");
            RequireFit(PackageField<FsmOwnerDefault>(pick, "GameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                && mask != null && mask.Length == 1 && !mask[0].UseVariable && mask[0].Value == 19
                && PackageField<FsmBool>(pick, "invertMask")?.Value == false);
            RotationConstant(pick, "rayDistance", 1);
        }
    }
}
