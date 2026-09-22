using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private bool _radiatorCaptureFailed;
        private void CaptureRadiator(EngineSourceLookup lookup, EngineMountedPartData rule, EngineBlockState state)
        {
            try
            {
                var mount = lookup.Find(rule.MountPath);
                if (mount != null && ScenePath.Of(mount.transform) != rule.MountPath) throw new InvalidOperationException("Native radiator path changed.");
                var data = mount == null || !mount.activeInHierarchy ? null : ReadMountedEnginePart(mount.transform, rule, "Coolant");
                if (data != null)
                {
                    float wear = IntakeScalar(data, "Wear"), coolant = IntakeScalar(data, "Coolant"), pressure = IntakeScalar(data, "PressureCap"), fan = IntakeScalar(data, "FlectEfficiency");
                    state.RadiatorWear = wear; state.RadiatorCoolant = coolant; state.RadiatorPressureCap = pressure; state.RadiatorFlectEfficiency = fan;
                    state.RadiatorInstalled = true;
                }
                _radiatorCaptureFailed = false;
            }
            catch (Exception ex)
            {
                if (!_radiatorCaptureFailed) SyncEventLog.Record("radiator-capture-unavailable", ex.Message);
                _radiatorCaptureFailed = true;
            }
        }
    }
}
