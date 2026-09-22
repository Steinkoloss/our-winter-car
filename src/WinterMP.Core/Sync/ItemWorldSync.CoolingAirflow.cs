using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly bool[] _coolingAirflowCaptureFailed = new bool[4];
        private void CaptureCoolingAirflow(EngineSourceLookup lookup, EngineCoolingAirflowData[] sources, EngineBlockState state)
        {
            foreach (var source in sources)
            {
                try
                {
                    var rule = source.Mount; var mount = lookup.Find(rule.MountPath);
                    if (mount != null && ScenePath.Of(mount.transform) != rule.MountPath) throw new InvalidOperationException("Native cooling airflow path changed.");
                    // Grille prefabs lack the modifier field read by Install 2. Cooling
                    // consumes the mount's value; Tightness identifies the native part.
                    var data = mount == null || !mount.activeInHierarchy ? null : source.Index == 1 ? ReadGrilleBlockoff(mount, rule)
                        : ReadMountedEnginePart(mount.transform, rule, source.Index == 0 ? "Tightness" : "CoolingAirRateModifier");
                    if (data != null && source.Index == 3 && !FactoryItemIdentity.IsNativeId(EngineBlockDataFsm(data.FsmVariables.FindFsmGameObject("ActivePart").Value, rule.Fsm).FsmVariables.FindFsmString("ID").Value, rule.PartPrefix)) data = null;
                    if (data != null)
                    {
                        float modifier = source.Index == 1 ? 0 : IntakeScalar(data, "CoolingAirRateModifier");
                        if (source.Index == 0) state.GrilleAirflow = modifier;
                        else if (source.Index == 2) state.HoodAirflow = modifier;
                        else if (source.Index == 3) state.FiberglassHoodAirflow = modifier;
                        state.CoolingAirflowFlags |= (byte)(1 << source.Index);
                    }
                    _coolingAirflowCaptureFailed[source.Index] = false;
                }
                catch (Exception error)
                {
                    if (!_coolingAirflowCaptureFailed[source.Index]) SyncEventLog.Record("cooling-airflow-unavailable", source.Name + ": " + error.Message);
                    _coolingAirflowCaptureFailed[source.Index] = true;
                }
            }
        }
        private static PlayMakerFSM? ReadGrilleBlockoff(GameObject mount, EngineMountedPartData rule)
        {
            var data = EngineBlockDataFsm(mount, rule.Fsm);
            var installed = data.FsmVariables.FindFsmBool("Installed"); var active = data.FsmVariables.FindFsmGameObject("ActivePart");
            if (installed == null || active == null) throw new InvalidOperationException("Missing grille cover inputs.");
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started || !installed.Value || data.ActiveStateName != rule.ReadyState) return null;
            var part = active.Value;
            if (part == null || !part.activeInHierarchy || part.transform.parent != mount.transform) return null;
            var item = EngineBlockDataFsm(part, rule.Fsm);
            // The unique scene cover captures BLOCKOFF0 before its display rename;
            // it has no float fields and is not spawned by a replacement factory.
            return item.FsmVariables.FindFsmInt("AssemblyID")?.Value == 1
                && item.FsmVariables.FindFsmString("ID")?.Value == rule.PartPrefix + "0" ? data : null;
        }
    }
}
