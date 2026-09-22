using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly bool[] _exhaustCaptureFailed = new bool[EngineBlockState.ExhaustPartCount];
        private void CaptureExhaust(EngineSourceLookup lookup, GameObject? head, EngineExhaustPartData[] sources, EngineBlockState state)
        {
            foreach (var source in sources)
            {
                try
                {
                    var rule = source.Mount; PlayMakerFSM? data;
                    if (rule.RootPrefix.Length != 0) data = head == null ? null : ReadMountedIntake(head, rule, "DataPower");
                    else
                    {
                        var mount = lookup.Find(rule.MountPath);
                        if (mount != null && ScenePath.Of(mount.transform) != rule.MountPath) throw new InvalidOperationException("Native exhaust path changed.");
                        data = mount == null || !mount.activeInHierarchy ? null : ReadMountedEnginePart(mount.transform, rule, "DataPower");
                    }
                    if (data != null)
                    {
                        float power = IntakeScalar(data, "DataPower"), torque = IntakeScalar(data, "DataTorque"), add = IntakeScalar(data, "DataPowerAdd");
                        int index = source.Index * 3; state.ExhaustPerformance[index] = power; state.ExhaustPerformance[index + 1] = torque; state.ExhaustPerformance[index + 2] = add;
                        state.ExhaustFlags |= (byte)(1 << source.Index);
                    }
                    _exhaustCaptureFailed[source.Index] = false;
                }
                catch (Exception error)
                {
                    if (!_exhaustCaptureFailed[source.Index])
                    {
                        _exhaustCaptureFailed[source.Index] = true;
                        WinterMPPlugin.Log.LogWarning("WorldSync: " + source.Name + " input unavailable: " + error.Message);
                        SyncEventLog.Record("exhaust-unavailable", source.Name + ": " + error.Message);
                    }
                }
            }
        }
    }
}
