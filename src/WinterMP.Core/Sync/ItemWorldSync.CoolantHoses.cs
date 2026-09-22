using System;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly bool[] _coolantHoseCaptureFailed = new bool[EngineBlockState.CoolantHoseCount];

        internal bool ReadHeaterHoseInstalled(byte index)
        {
            if (index != 2 && index != 3) return false;
            var state = _engineBlockReplica.Inputs;
            return state != null && (state.CoolantHoseFlags & (1 << index)) != 0;
        }

        private void CaptureCoolantHoses(EngineSourceLookup lookup, EngineCoolantHoseData[] sources, EngineBlockState state)
        {
            foreach (var source in sources)
            {
                try
                {
                    var rule = source.Mount; var mount = lookup.Find(rule.MountPath);
                    if (mount != null && ScenePath.Of(mount.transform) != rule.MountPath) throw new InvalidOperationException("Native coolant hose path changed.");
                    var data = mount == null || !mount.activeInHierarchy ? null : ReadMountedEnginePart(mount.transform, rule, "Tightness");
                    if (data != null)
                    {
                        state.CoolantHoseTightness[source.Index] = IntakeScalar(data, "Tightness");
                        state.CoolantHoseFlags |= (byte)(1 << source.Index);
                    }
                    _coolantHoseCaptureFailed[source.Index] = false;
                }
                catch (Exception error)
                {
                    if (!_coolantHoseCaptureFailed[source.Index]) SyncEventLog.Record("coolant-hose-unavailable", source.Name + ": " + error.Message);
                    _coolantHoseCaptureFailed[source.Index] = true;
                }
            }
        }
    }
}
