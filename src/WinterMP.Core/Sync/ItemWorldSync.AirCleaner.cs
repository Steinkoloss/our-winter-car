using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private bool _airCleanerCaptureFailed;
        private void CaptureAirCleaner(GameObject head, EngineMountedPartData rule, EngineBlockState state)
        {
            try
            {
                var data = ReadMountedIntake(head, rule, "DataPower");
                if (data != null)
                {
                    CaptureIntakePerformance(data, state, true);
                    state.Flags |= EngineBlockState.AirCleanerInstalled;
                }
                _airCleanerCaptureFailed = false;
            }
            catch (Exception error)
            {
                if (!_airCleanerCaptureFailed)
                {
                    _airCleanerCaptureFailed = true;
                    WinterMPPlugin.Log.LogWarning("WorldSync: air-cleaner input unavailable: " + error.Message);
                    SyncEventLog.Record("air-cleaner-unavailable", error.Message);
                }
            }
        }
        private static void CaptureIntakePerformance(PlayMakerFSM data, EngineBlockState state, bool airCleaner)
        {
            float power = IntakeScalar(data, "DataPower"), torque = IntakeScalar(data, "DataTorque"), add = IntakeScalar(data, "DataPowerAdd");
            if (airCleaner) { state.AirCleanerPower = power; state.AirCleanerTorque = torque; state.AirCleanerPowerAdd = add; }
            else { state.CarburettorPower = power; state.CarburettorTorque = torque; state.CarburettorPowerAdd = add; }
        }
    }
}
