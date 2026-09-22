using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private bool _coolingAmbientCaptureFailed;
        private void CaptureCoolingAmbient(EngineSourceLookup lookup, EngineCoolingAmbientData rule, EngineBlockState state)
        {
            try
            {
                var obj = lookup.Find(rule.Path);
                if (obj == null || !obj.activeInHierarchy) { _coolingAmbientCaptureFailed = false; return; }
                if (ScenePath.Of(obj.transform) != rule.Path) throw new InvalidOperationException("Native cooling ambient path changed.");
                var source = EngineBlockDataFsm(obj, rule.Fsm);
                if (!source.enabled || !source.Fsm.Initialized || !source.Fsm.Started) { _coolingAmbientCaptureFailed = false; return; }
                foreach (string name in rule.States) GuestEngineInputState(source, name);
                if (Array.IndexOf(rule.States, source.ActiveStateName) < 0) throw new InvalidOperationException("Native cooling ambient state changed.");
                // RoofCheck already integrates shelter temperature gradually. Capture
                // its output instead of rerunning raycasts or replacing it with weather.
                float value = IntakeScalar(source, rule.Variable);
                state.CoolingAmbientTemperature = value; state.CoolingAmbientAvailable = true; _coolingAmbientCaptureFailed = false;
            }
            catch (Exception error)
            {
                if (!_coolingAmbientCaptureFailed) SyncEventLog.Record("cooling-ambient-unavailable", error.Message);
                _coolingAmbientCaptureFailed = true;
            }
        }
    }
}
