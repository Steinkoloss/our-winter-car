using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private bool _engineHeadCaptureFailed;
        private GameObject? CaptureCylinderHead(GameObject block, EngineMountedPartData rule)
        {
            try
            {
                var installed = ReadCylinderHead(block, rule);
                _engineHeadCaptureFailed = false;
                return installed;
            }
            catch (Exception error)
            {
                if (!_engineHeadCaptureFailed)
                {
                    _engineHeadCaptureFailed = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: cylinder-head input unavailable: " + error.Message);
                    SyncEventLog.Record("cylinder-head-unavailable", error.Message);
                }
                return null;
            }
        }

        private static GameObject? ReadCylinderHead(GameObject block, EngineMountedPartData rule)
        {
            var blockData = EngineBlockDataFsm(block, rule.Fsm);
            if (!GuestEngineProtection.NativeEnginePart(blockData.FsmVariables.FindFsmString("ID")?.Value, rule.RootPrefix)) return null;
            Transform? mount = null;
            foreach (Transform child in block.transform)
                if (child.name == rule.RelativePath)
                {
                    if (mount != null) throw new InvalidOperationException("Ambiguous cylinder-head mount.");
                    mount = child;
                }
            if (mount == null || !mount.gameObject.activeInHierarchy) return null;
            var data = EngineBlockDataFsm(mount.gameObject, rule.Fsm);
            var installed = data.FsmVariables.FindFsmBool("Installed"); var active = data.FsmVariables.FindFsmGameObject("ActivePart");
            if (installed == null || active == null) throw new InvalidOperationException("Missing cylinder-head inputs.");
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started || !installed.Value || data.ActiveStateName != rule.ReadyState) return null;
            var part = active.Value;
            if (part == null || !part.activeInHierarchy || part.transform.parent != mount) return null;
            var head = EngineBlockDataFsm(part, rule.Fsm);
            return head.FsmVariables.FindFsmInt("AssemblyID")?.Value == 1
                && GuestEngineProtection.NativeEnginePart(head.FsmVariables.FindFsmString("ID")?.Value, rule.PartPrefix) ? part : null;
        }
    }
}
