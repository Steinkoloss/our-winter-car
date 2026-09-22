using System;
using System.Collections;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private sealed class SyncedValve
        {
            public PlayMakerFSM Fsm = null!, Data = null!;
            public FsmFloat Setting = null!, Rotation = null!, Step = null!;
            public FsmInt Index = null!;
            public Transform Visual = null!;
            public SphereCollider Pick = null!;
            public MonoBehaviour Proxy = null!;
            public PropertyInfo ArrayProperty = null!;
            public int Slot;
            public bool Failed;
            public ReplicaBoltGate? Gate;
            public float NextRequestAt;
            public ValveAdjustmentState? LastState;
            public FsmState[]? SavedStates;
            public FsmTransition[]? SavedTransitions;
            public string SavedStart = "";
            public float SavedSetting, SavedRotation;
            public Quaternion SavedPose;
            public bool SavedPick;
        }

        private static bool IsValveAdjustment(PlayMakerFSM fsm)
        {
            var c = SyncCatalog.ValveAdjustment;
            var data = c == null ? null : NativePartIdentity.FindData(fsm.transform);
            return c != null && data != null && fsm.FsmName == c.Fsm && fsm.transform.parent != null
                && ScenePath.RelativeTo(fsm.transform.parent, data.transform) == c.ParentPath;
        }

        private static void ValveRequire(bool valid, string reason)
        { if (!valid) throw new InvalidOperationException("Native valve " + reason + " changed."); }
        private static bool ValveFloat(FsmStateAction action, string field, string name) =>
            BoltField<FsmFloat>(action, field)?.UseVariable == true && BoltField<FsmFloat>(action, field)?.Name == name;
        private static bool ValveLiteral(FsmStateAction action, string field, float value) =>
            BoltField<FsmFloat>(action, field)?.UseVariable == false && BoltField<FsmFloat>(action, field)?.Value == value;
        private static bool ValveFalse(FsmStateAction action, string field) => action.GetType().GetField(field)?.GetValue(action) is bool value && !value;
        private static bool ValveTarget(FsmStateAction action, string name) =>
            BoltField<FsmOwnerDefault>(action, "gameObject")?.OwnerOption == OwnerDefaultOption.SpecifyGameObject
            && BoltField<FsmOwnerDefault>(action, "gameObject")?.GameObject.Name == name;
        private static bool ValveWrapped(FsmStateAction action, string field, string name)
        {
            var value = action.GetType().GetField(field)?.GetValue(action);
            return value != null && value.GetType().GetField("variableName")?.GetValue(value) as string == name
                && Equals(value.GetType().GetField("useVariable")?.GetValue(value), true)
                && Convert.ToInt32(value.GetType().GetField("type")?.GetValue(value)) == 0;
        }

        private static void ValidateValveGraph(PlayMakerFSM fsm)
        {
            var c = SyncCatalog.ValveAdjustment ?? throw new InvalidOperationException("Missing valve catalog.");
            foreach (string turn in new[] { "Tight?", "Loose?" })
            {
                var action = BoltActions(fsm, turn, new[] { turn == "Tight?" ? "FloatAdd" : "FloatSubtract" })[0];
                ValveRequire(ValveFloat(action, "floatVariable", "AdjustmentF")
                    && ValveFloat(action, turn == "Tight?" ? "add" : "subtract", "Step")
                    && ValveFalse(action, "everyFrame") && ValveFalse(action, "perSecond"), "turn");
                ValveSingleTransition(fsm, turn, "Calc pos");
            }
            var calc = BoltActions(fsm, "Calc pos", new[] { "FloatClamp", "ArrayListSet", "FloatOperator" });
            ValveRequire(ValveFloat(calc[0], "floatVariable", "AdjustmentF")
                && ValveLiteral(calc[0], "minValue", ValveAdjustmentPolicy.Minimum)
                && ValveLiteral(calc[0], "maxValue", ValveAdjustmentPolicy.Maximum) && ValveFalse(calc[0], "everyFrame"), "clamp");
            ValveArrayAction(calc[1], c.Reference, "variable");
            ValveRequire(ValveFalse(calc[1], "everyFrame") && ValveFloat(calc[2], "float1", "AdjustmentF")
                && ValveLiteral(calc[2], "float2", ValveAdjustmentPolicy.RotationScale)
                && Convert.ToInt32(calc[2].GetType().GetField("operation")?.GetValue(calc[2])) == 2
                && ValveFloat(calc[2], "storeResult", "Rot") && ValveFalse(calc[2], "everyFrame"), "rotation calculation");
            ValveSingleTransition(fsm, "Calc pos", "Set pos");
            var pose = BoltActions(fsm, "Set pos", new[] { "SetRotation" })[0];
            ValveRequire(ValveTarget(pose, "ThisBolt") && ValveFloat(pose, "zAngle", "Rot")
                && BoltField<FsmFloat>(pose, "xAngle")?.IsNone == true && BoltField<FsmFloat>(pose, "yAngle")?.IsNone == true
                && BoltField<FsmVector3>(pose, "vector")?.IsNone == true && BoltField<FsmQuaternion>(pose, "quaternion")?.IsNone == true
                && Convert.ToInt32(pose.GetType().GetField("space")?.GetValue(pose)) == 1
                && ValveFalse(pose, "everyFrame") && ValveFalse(pose, "lateUpdate")
                && FsmHook.FindState(fsm, "Set pos")!.Transitions.Length == 0, "local visual");
            var init = BoltActions(fsm, "Init", new[] { "GetChild", "GetScale", "GetName", "GetSubstring",
                "ConvertStringToInt", "ArrayListGet", "NextFrameEvent" });
            ValveRequire(BoltField<FsmOwnerDefault>(init[0], "gameObject")?.OwnerOption == OwnerDefaultOption.UseOwner
                && BoltField<FsmString>(init[0], "withTag")?.Value == "Untagged"
                && string.IsNullOrEmpty(BoltField<FsmString>(init[0], "childName")?.Value)
                && BoltField<FsmGameObject>(init[0], "storeResult")?.Name == "ThisBolt"
                && ValveTarget(init[1], "ThisBolt") && ValveFloat(init[1], "xScale", "Boltsize")
                && Convert.ToInt32(init[1].GetType().GetField("space")?.GetValue(init[1])) == 1
                && BoltField<FsmGameObject>(init[2], "gameObject")?.Name == "ThisBolt"
                && BoltField<FsmString>(init[2], "storeName")?.Name == "Name"
                && BoltField<FsmString>(init[3], "stringVariable")?.Name == "Name"
                && BoltField<FsmString>(init[3], "storeResult")?.Name == "Nmbr"
                && BoltField<FsmInt>(init[3], "startIndex")?.UseVariable == false && BoltField<FsmInt>(init[3], "startIndex")?.Value == 4
                && BoltField<FsmInt>(init[3], "length")?.UseVariable == false && BoltField<FsmInt>(init[3], "length")?.Value == 1
                && BoltField<FsmString>(init[4], "stringVariable")?.Name == "Nmbr"
                && BoltField<FsmInt>(init[4], "intVariable")?.Name == "Index"
                && BoltField<FsmEvent>(init[6], "sendEvent")?.Name == "FINISHED", "initialization");
            for (int i = 1; i <= 4; i++) ValveRequire(ValveFalse(init[i], "everyFrame"), "recurring initialization");
            ValveArrayAction(init[5], c.Reference, "result");
            ValveRequire(string.IsNullOrEmpty(BoltField<FsmEvent>(init[5], "failureEvent")?.Name), "array failure event");
            ValveSingleTransition(fsm, "Init", "Calc pos");
            foreach (string state in new[] { "On", "Off" })
            {
                var a = BoltActions(fsm, state, new[] { "SetProperty" })[0];
                var property = BoltField<FsmProperty>(a, "targetProperty");
                ValveRequire(property != null && property.TargetObject.Name == "Collider"
                    && property.PropertyName == "enabled" && property.BoolParameter.UseVariable == false
                    && property.BoolParameter.Value == (state == "On") && property.setProperty && ValveFalse(a, "everyFrame")
                    && FsmHook.FindState(fsm, state)!.Transitions.Length == 0, "tool collider");
            }
            ValveRequire(fsm.Fsm.StartState == "Init", "entry points");
            var entries = new System.Collections.Generic.HashSet<string>();
            foreach (var t in fsm.Fsm.GlobalTransitions)
            {
                ValveRequire(entries.Add(t.EventName) && ((t.EventName == "TIGHTEN" && t.ToState == "Tight?") || (t.EventName == "UNTIGHTEN" && t.ToState == "Loose?")
                    || (t.EventName == "REPAIRMODE_ON" && t.ToState == "On") || (t.EventName == "REPAIRMODE_OFF" && t.ToState == "Off")
                    || (t.EventName == FsmHook.RemoteEntryEventName("Set pos") && t.ToState == "Set pos")), "tool event");
            }
            ValveRequire(entries.Contains("TIGHTEN") && entries.Contains("UNTIGHTEN") && entries.Contains("REPAIRMODE_ON") && entries.Contains("REPAIRMODE_OFF"), "tool events");
        }

        private static void ValveSingleTransition(PlayMakerFSM fsm, string state, string to) =>
            ValveRequire(FsmHook.FindState(fsm, state)!.Transitions.Length == 1 && BoltTransition(fsm, state, "FINISHED", to), "transition " + state);
        private static void ValveArrayAction(FsmStateAction action, string reference, string value) =>
            ValveRequire(ValveTarget(action, "ThisPart") && BoltField<FsmInt>(action, "atIndex")?.Name == "Index"
                && BoltField<FsmInt>(action, "atIndex")?.UseVariable == true
                && BoltField<FsmString>(action, "reference")?.UseVariable == false && BoltField<FsmString>(action, "reference")?.Value == reference
                && ValveWrapped(action, value, "AdjustmentF"), "array binding");

        private static void BindValve(SyncedValve valve)
        {
            var fsm = valve.Fsm; var vars = fsm.FsmVariables;
            ValidateValveGraph(fsm);
            var c = SyncCatalog.ValveAdjustment!;
            valve.Data = NativePartIdentity.FindData(fsm.transform) ?? throw new InvalidOperationException("Missing valve head.");
            ValveRequire(IsValveAdjustment(fsm) && vars.FindFsmGameObject("ThisPart")?.Value == valve.Data.gameObject
                && (valve.Data.FsmVariables.FindFsmString("ID")?.Value ?? "").StartsWith(c.HeadPrefix, StringComparison.Ordinal), "head ownership");
            valve.Setting = vars.FindFsmFloat("AdjustmentF") ?? throw new InvalidOperationException("Missing valve setting.");
            valve.Rotation = vars.FindFsmFloat("Rot") ?? throw new InvalidOperationException("Missing valve rotation.");
            valve.Step = vars.FindFsmFloat("Step") ?? throw new InvalidOperationException("Missing valve step.");
            valve.Index = vars.FindFsmInt("Index") ?? throw new InvalidOperationException("Missing valve slot.");
            ValveRequire(valve.Step.Value == ValveAdjustmentPolicy.Step, "step");
            foreach (var proxy in valve.Data.GetComponents<MonoBehaviour>())
            {
                if (proxy == null || proxy.GetType().Name != "PlayMakerArrayListProxy"
                    || proxy.GetType().GetField("referenceName")?.GetValue(proxy) as string != c.Reference) continue;
                ValveRequire(valve.Proxy == null, "unique array"); valve.Proxy = proxy;
                valve.ArrayProperty = proxy.GetType().GetProperty("arrayList") ?? throw new InvalidOperationException("Missing valve array.");
            }
            for (int i = 0; i < fsm.transform.childCount; i++)
            {
                var child = fsm.transform.GetChild(i);
                if (child.tag != "Untagged") continue;
                ValveRequire(valve.Visual == null, "unique visual"); valve.Visual = child;
            }
            var visual = valve.Visual;
            if (visual == null) throw new InvalidOperationException("Missing valve visual.");
            ValveRequire(ReplicaBoltGate.TryIndex(visual.name, 4, 1, ValveAdjustmentPolicy.Count, out valve.Slot), "visual slot");
            ValveRequire(ValveAdjustmentPolicy.Read(ValveArray(valve), valve.Slot, out _), "eight float settings");
            valve.Pick = vars.FindFsmObject("Collider")?.Value as SphereCollider ?? throw new InvalidOperationException("Missing valve collider.");
            ValveRequire(valve.Pick.gameObject == fsm.gameObject && valve.Pick.isTrigger && fsm.gameObject.layer == 12
                && IsFinite(visual.localScale.x) && visual.localScale.x > 0, "tool pick");
            for (var parent = fsm.transform; parent != valve.Data.transform; parent = parent.parent)
                if (parent == null || parent.GetComponent<Rigidbody>() != null) throw new InvalidOperationException("Invalid valve visual hierarchy.");
            ValveRequire(valve.Index.Value == valve.Slot && vars.FindFsmGameObject("ThisBolt")?.Value == visual.gameObject, "initialized slot");
        }

        private static IList? ValveArray(SyncedValve valve) => valve.Proxy == null ? null : valve.ArrayProperty.GetValue(valve.Proxy, null) as IList;
        private bool ValveReady(SyncedValve v) => !v.Failed && v.Fsm != null && v.Data != null && v.Visual != null && v.Pick != null
            && v.Fsm.enabled && v.Fsm.gameObject.activeInHierarchy && v.Fsm.Fsm.Started
            && (v.Data.enabled || _bridge.CylinderHeadReady(v.Data)) && v.Data.Fsm.Started
            && (NativePartIdentity.Phase(v.Data) == NativePartPhase.Fitted || NativePartIdentity.Phase(v.Data) == NativePartPhase.Loose)
            && v.Fsm.ActiveStateName != "Init" && v.Fsm.ActiveStateName != "Calc pos"
            && v.Fsm.ActiveStateName != "Tight?" && v.Fsm.ActiveStateName != "Loose?";
        private static bool ValveOwnership(SyncedValve v) => v.Index.Value == v.Slot && v.Step.Value == ValveAdjustmentPolicy.Step
            && v.Visual.parent == v.Fsm.transform && v.Pick.gameObject == v.Fsm.gameObject
            && v.Fsm.FsmVariables.FindFsmGameObject("ThisPart")?.Value == v.Data.gameObject
            && v.Fsm.FsmVariables.FindFsmGameObject("ThisBolt")?.Value == v.Visual.gameObject
            && ScenePath.RelativeTo(v.Fsm.transform.parent, v.Data.transform) == SyncCatalog.ValveAdjustment?.ParentPath
            && v.Proxy != null && v.Proxy.gameObject == v.Data.gameObject;

        private static void FailValve(SyncedValve valve, Exception error)
        {
            if (valve.Failed) return;
            valve.Failed = true;
            if (valve.Pick != null) valve.Pick.enabled = false;
            if (valve.Fsm != null && valve.Gate != null) valve.Fsm.enabled = false;
            WinterMPPlugin.Log.LogWarning("WorldSync: valve adjustment disabled at " + (valve.Fsm == null ? "destroyed" : ScenePath.Of(valve.Fsm.transform)) + ": " + error.Message);
        }
    }
}
