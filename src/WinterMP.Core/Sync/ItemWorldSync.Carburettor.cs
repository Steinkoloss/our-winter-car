using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Sync;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private bool _carburettorCaptureFailed;
        private void CaptureCarburettor(GameObject head, EngineMountedPartData rule, EngineBlockState state)
        {
            try
            {
                var data = ReadMountedIntake(head, rule, "SettingMixture");
                if (data == null) { _carburettorCaptureFailed = false; return; }
                float nextChamber = IntakeScalar(data, "FuelChamber"), nextReserve = IntakeScalar(data, "CarbReserve"), nextMixture = IntakeScalar(data, "SettingMixture"), tightness = IntakeScalar(data, "Tightness");
                CaptureIntakePerformance(data, state, false);
                state.CarburettorTightness = tightness; state.FuelChamber = nextChamber; state.CarbReserve = nextReserve; state.SettingMixture = nextMixture;
                state.Flags |= EngineBlockState.CarburettorInstalled; _carburettorCaptureFailed = false;
            }
            catch (Exception error)
            {
                if (!_carburettorCaptureFailed)
                {
                    _carburettorCaptureFailed = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: carburettor input unavailable: " + error.Message);
                    SyncEventLog.Record("carburettor-unavailable", error.Message);
                }
            }
        }
        private static float IntakeScalar(PlayMakerFSM data, string name)
        {
            var value = data.FsmVariables.FindFsmFloat(name);
            if (value == null || float.IsNaN(value.Value) || float.IsInfinity(value.Value)) throw new InvalidOperationException("Invalid intake " + name + ".");
            return value.Value;
        }
        private static PlayMakerFSM? ReadMountedIntake(GameObject head, EngineMountedPartData rule, string requiredPartField)
        {
            if (!GuestEngineProtection.NativeEnginePart(EngineBlockDataFsm(head, rule.Fsm).FsmVariables.FindFsmString("ID")?.Value, rule.RootPrefix)) return null;
            Transform? mount = null;
            foreach (Transform child in head.transform)
                if (child.name == rule.RelativePath)
                {
                    if (mount != null) throw new InvalidOperationException("Ambiguous intake mount.");
                    mount = child;
                }
            if (mount == null || !mount.gameObject.activeInHierarchy) return null;
            return ReadMountedEnginePart(mount, rule, requiredPartField);
        }
        private static PlayMakerFSM? ReadMountedEnginePart(Transform mount, EngineMountedPartData rule, string requiredPartField)
        {
            var data = EngineBlockDataFsm(mount.gameObject, rule.Fsm);
            var installed = data.FsmVariables.FindFsmBool("Installed"); var active = data.FsmVariables.FindFsmGameObject("ActivePart");
            if (installed == null || active == null) throw new InvalidOperationException("Missing intake inputs.");
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started || !installed.Value || data.ActiveStateName != rule.ReadyState) return null;
            var part = active.Value;
            if (part == null || !part.activeInHierarchy || part.transform.parent != mount) return null;
            var carburettor = EngineBlockDataFsm(part, rule.Fsm);
            return carburettor.FsmVariables.FindFsmInt("AssemblyID")?.Value == 1
                && IntakeIdentity(carburettor.FsmVariables.FindFsmString("ID")?.Value, rule)
                && carburettor.FsmVariables.FindFsmFloat(requiredPartField) != null ? data : null;
        }
        private static bool IntakeIdentity(string? id, EngineMountedPartData rule)
        {
            if (GuestEngineProtection.NativeEnginePart(id, rule.PartPrefix)) return true;
            if (id == null) return false;
            foreach (string prefix in rule.AlternatePartPrefixes)
                if (prefix.StartsWith("VIN", StringComparison.Ordinal) ? GuestEngineProtection.NativeEnginePart(id, prefix) : FactoryItemIdentity.IsNativeId(id, prefix)) return true;
            return false;
        }
    }
}
