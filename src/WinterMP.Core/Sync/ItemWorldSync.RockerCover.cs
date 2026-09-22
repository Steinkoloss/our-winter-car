using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private bool _rockerCoverCaptureFailed;
        private void CaptureRockerCover(GameObject head, EngineMountedPartData rule, EngineBlockState state)
        {
            try
            {
                var data = ReadMountedIntake(head, rule, "Tightness");
                if (data != null)
                {
                    state.RockerCoverTightness = IntakeScalar(data, "Tightness");
                    state.RockerCoverInstalled = true;
                }
                _rockerCoverCaptureFailed = false;
            }
            catch (Exception error)
            {
                if (!_rockerCoverCaptureFailed)
                {
                    _rockerCoverCaptureFailed = true;
                    SyncEventLog.Record("rocker-cover-unavailable", error.Message);
                    WinterMPPlugin.Log.LogWarning("WorldSync: rocker-cover input unavailable: " + error.Message);
                }
            }
        }
    }
}
