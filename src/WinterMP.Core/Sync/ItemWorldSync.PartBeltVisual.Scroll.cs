using System;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static bool BindPartBeltScroll(PartBeltSource source, PartBeltVisualData rule)
        {
            PlayMakerFSM? scroll = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || fsm.FsmName != rule.ScrollFsm || ScenePath.Of(fsm.transform) != rule.ScrollPath) continue;
                RequireFit(scroll == null); scroll = fsm;
            }
            if (scroll == null || !scroll.Fsm.Initialized) return false;
            var actions = NativePartActions(scroll, "State 1", "SetFloatValue", "SetFloatValue", "FloatAdd", "FloatOperator",
                "SetTextureOffset", "SetTextureOffset", "FloatCompare");
            for (int i = 0; i < 2; i++)
            {
                RotationVariable(actions[i], "floatVariable", i == 0 ? "Data" : "Offset");
                RotationConstant(actions[i], "floatValue", 0); HandScrewFrame(actions[i], false);
            }
            RotationVariable(actions[2], "floatVariable", "Data"); RotationVariable(actions[2], "add", "RPM");
            HandScrewFrame(actions[2], true);
            RequireFit(actions[2].GetType().GetField("perSecond")?.GetValue(actions[2]) is bool perSecond && perSecond);
            RotationVariable(actions[3], "float1", "Data"); RotationVariable(actions[3], "float2", "AnimMultiplier");
            RotationVariable(actions[3], "storeResult", "Offset"); HandScrewFrame(actions[3], true);
            RequireFit(Convert.ToInt32(actions[3].GetType().GetField("operation")?.GetValue(actions[3])) == 2);
            for (int i = 4; i < 6; i++)
            {
                var index = PackageField<FsmInt>(actions[i], "materialIndex");
                RequireFit(index != null && !index.UseVariable && index.Value == 0);
                HandScrewString(PackageField<FsmString>(actions[i], "namedTexture"), "_MainTex");
                RotationVariable(actions[i], "offsetX", "Offset"); RotationConstant(actions[i], "offsetY", 0);
                HandScrewFrame(actions[i], true);
            }
            var target = PackageField<FsmOwnerDefault>(actions[5], "gameObject");
            RequireFit(target != null && target.OwnerOption == OwnerDefaultOption.SpecifyGameObject
                && !target.GameObject.UseVariable && source.Renderer != null
                && scroll.Fsm.GetOwnerDefaultTarget(target) == source.Renderer.gameObject);
            HandScrewCompare(actions[6], "Offset", -1000, string.Empty, "RESET", string.Empty);
            HandScrewFrame(actions[6], true);
            RequireFitTransition(scroll, "State 1", "RESET", "Delay");
            var wait = NativePartActions(scroll, "Delay", "Wait")[0];
            RotationConstant(wait, "time", .1f); HandScrewEvent(wait, "finishEvent", "FINISHED");
            RequireFit(wait.GetType().GetField("realTime")?.GetValue(wait) is bool realTime && realTime);
            RequireFitTransition(scroll, "Delay", "FINISHED", "State 1");

            var rpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM");
            var multiplier = scroll.FsmVariables.FindFsmFloat("AnimMultiplier");
            RequireFit(rpm != null && multiplier != null && scroll.FsmVariables.FindFsmFloat("RPM") == null
                && scroll.FsmVariables.FindFsmFloat("Data") != null && scroll.FsmVariables.FindFsmFloat("Offset") != null);
            // Observe the native rate without touching either belt's material.
            // The guest advances only its owned material from the host's rate.
            source.Scroll = scroll; source.ScrollRpm = rpm!; source.ScrollMultiplier = multiplier!;
            return true;
        }
    }
}
