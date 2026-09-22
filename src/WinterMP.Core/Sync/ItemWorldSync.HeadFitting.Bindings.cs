using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private PlayMakerFSM? _headFitData, _headFitMount;
        private bool _headFitFailed;

        private PlayMakerFSM? GetHeadFitMount(PlayMakerFSM data)
        {
            if (_headFitFailed || !HeadIds(out _, out uint parent) || !_nativeParts.TryGetValue(parent, out var block)
                || block == null || data == null || !data.Fsm.Initialized || !data.Fsm.Started) return null;
            var point = ScenePath.FindRelative(block.transform, SyncCatalog.CylinderHead!.MountPath);
            if (point == null || !point.gameObject.activeInHierarchy
                || data.FsmVariables.FindFsmGameObject("InstallPoint")?.Value != point.gameObject
                || data.FsmVariables.FindFsmGameObject("Owner")?.Value != data.gameObject) return null;
            if (_headFitData == data && _headFitMount != null && _headFitMount.gameObject == point.gameObject) return _headFitMount;
            try
            {
                var mount = EngineBlockDataFsm(point.gameObject, "Data");
                if (!mount.Fsm.Initialized || !mount.Fsm.Started) return null;
                ValidateHeadFitBindings(data, mount);
                _headFitData = data; _headFitMount = mount; return mount;
            }
            catch (Exception e)
            {
                _headFitFailed = true;
                WinterMPPlugin.Log.LogWarning("WorldSync: cylinder head interaction disabled: " + e.Message); return null;
            }
        }

        private static void ValidateHeadFitBindings(PlayMakerFSM data, PlayMakerFSM mount)
        {
            var c = SyncCatalog.ReplacementParts ?? throw new InvalidOperationException("Missing assembly bindings.");
            ValidatePartFitMount(mount, c);
            RequireFitTransition(data, null, c["fitEvent"], c["fitCheckState"]);
            RequireFitTransition(data, c["fitCheckState"], "FINISHED", c["itemStopState"]);
            var entry = NativePartActions(data, c["fitCheckState"], "GetFsmBool", "BoolTest", "SetFsmGameObject", "SendEventByName");
            RequireHeadTarget(entry[0], "InstallPoint", "Installed");
            RequireFit(PackageField<FsmBool>(entry[0], "storeValue")?.Name == "Installed"
                && PackageField<FsmBool>(entry[1], "boolVariable")?.Name == "Installed"
                && PackageField<FsmEvent>(entry[1], "isTrue")?.Name == "FINISHED"
                && string.IsNullOrEmpty(PackageField<FsmEvent>(entry[1], "isFalse")?.Name));
            RequireHeadTarget(entry[2], "InstallPoint", "ActivePart");
            RequireFit(PackageField<FsmGameObject>(entry[2], "setValue")?.Name == "Owner");
            RequireHeadSend(entry[3], "InstallPoint", "CHECK");
            foreach (string name in new[] { c["fitAllowState"], c["removeAllowState"] })
            {
                var allow = NativePartActions(mount, name, "GetFsmBool", "BoolNoneTrue");
                RequireHeadTarget(allow[0], "db_Installed1", "Installed");
                var result = mount.FsmVariables.FindFsmBool("InstalledPart1");
                RequireFit(result != null && ReferenceEquals(PackageField<FsmBool>(allow[0], "storeValue"), result));
                var checks = PackageField<FsmBool[]>(allow[1], "boolVariables");
                bool install = name == c["fitAllowState"];
                RequireFit(checks != null && checks.Length == (install ? 2 : 1) && ReferenceEquals(checks[checks.Length - 1], result)
                    && (!install || ReferenceEquals(checks[0], mount.FsmVariables.FindFsmBool("Installed")))
                    && PackageField<FsmEvent>(allow[1], "sendEvent")?.Name == "PROCEED");
                foreach (var action in allow) RequireFitOneShot(action);
            }
            RequireFitTransition(mount, null, "REMOVE", c["removeAllowState"]);
            RequireFitTransition(mount, c["removeAllowState"], "FINISHED", "UPDATE");
            RequireFitTransition(mount, c["removeAllowState"], "PROCEED", "Remove part");
            RequireFitTransition(data, c["removeMouseOverState"], "PROCEED", c["removeState"]);
            var remove = NativePartActions(data, c["removeState"], "SetFsmGameObject", "SendEventByName", "NextFrameEvent");
            RequireHeadTarget(remove[0], "InstallPoint", "ActivePart");
            RequireFit(PackageField<FsmGameObject>(remove[0], "setValue")?.Name == "Owner");
            RequireHeadSend(remove[1], "InstallPoint", "REMOVE");
            var tight = NativePartActions(data, c["removeTightnessState"], "SetFsmFloat", "FloatCompare");
            RequireHeadTarget(tight[0], "InstallPoint", "Tightness");
            var value = data.FsmVariables.FindFsmFloat("Tightness");
            RequireFit(value != null && ReferenceEquals(PackageField<FsmFloat>(tight[0], "setValue"), value)
                && ReferenceEquals(PackageField<FsmFloat>(tight[1], "float1"), value)
                && PackageField<FsmFloat>(tight[1], "float2")?.UseVariable == false
                && PackageField<FsmFloat>(tight[1], "float2")?.Value == 1
                && PackageField<FsmEvent>(tight[1], "lessThan")?.Name == "BACK"
                && PackageField<FsmEvent>(tight[1], "equal")?.Name == "PROCEED"
                && PackageField<FsmEvent>(tight[1], "greaterThan")?.Name == "PROCEED");
            RequireFitTransition(data, c["removeTightnessState"], "BACK", c["removeUnboltedState"]);
            RequireFitTransition(data, c["removeTightnessState"], "PROCEED", c["removeBoltedState"]);
            RequireFit(FsmHook.EnsureRemoteEntry(data, c["removeState"]));
        }
        private static void RequireHeadTarget(FsmStateAction action, string target, string variable)
        {
            RequireFit(FitTargetVariable(PackageField<FsmOwnerDefault>(action, "gameObject"), target)
                && PackageField<FsmString>(action, "fsmName")?.Value == "Data"
                && PackageField<FsmString>(action, "variableName")?.Value == variable);
        }
        private static void RequireHeadSend(FsmStateAction action, string target, string evt)
        {
            var send = PackageField<FsmEventTarget>(action, "eventTarget");
            RequireFit(send != null && send.target == FsmEventTarget.EventTarget.GameObjectFSM
                && FitTargetVariable(send.gameObject, target) && send.fsmName.Value == "Data"
                && PackageField<FsmString>(action, "sendEvent")?.Value == evt && PackageField<FsmFloat>(action, "delay")?.Value == 0);
            RequireFitOneShot(action);
        }
        private static bool HeadAtMount(PlayMakerFSM data, PlayMakerFSM mount)
        {
            var state = FsmHook.FindState(mount, SyncCatalog.ReplacementParts!["fitNearState"]);
            var compare = state == null ? null : FsmHook.NativeAction(state, 2);
            var tolerance = compare == null ? null : PackageField<FsmFloat>(compare, "float2");
            return tolerance != null && WinterMP.Net.Sync.PartFitLedger.WithinMount(data.transform.position.ToNet(), mount.transform.position.ToNet(), tolerance.Value);
        }
    }
}
