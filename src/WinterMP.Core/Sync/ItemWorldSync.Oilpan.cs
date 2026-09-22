using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private bool _oilpanCaptureFailed;
        private void CaptureOilpan(GameObject? block, EngineMountedPartData rule, EngineBlockState state)
        {
            try
            {
                var data = block == null ? null : ReadMountedIntake(block, rule, "OilLevel");
                if (data != null)
                {
                    float wear = IntakeScalar(data, "Wear"), tightness = IntakeScalar(data, "Tightness"), oil = IntakeScalar(data, "Oil"), contamination = IntakeScalar(data, "OilContamination"), viscosity = IntakeScalar(data, "OilViscosity");
                    state.OilpanWear = wear; state.OilpanTightness = tightness; state.Oil = oil; state.OilContamination = contamination; state.OilViscosity = viscosity;
                    state.OilpanInstalled = true;
                }
                _oilpanCaptureFailed = false;
            }
            catch (Exception error)
            {
                if (!_oilpanCaptureFailed)
                {
                    _oilpanCaptureFailed = true; SyncEventLog.Record("oilpan-unavailable", error.Message);
                    WinterMPPlugin.Log.LogWarning("WorldSync: oilpan input unavailable: " + error.Message);
                }
            }
        }
    }
}
