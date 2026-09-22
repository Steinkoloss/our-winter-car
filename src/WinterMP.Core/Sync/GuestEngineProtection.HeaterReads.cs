using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private static readonly HashSet<PlayMakerFSM> HeaterReadSources = new HashSet<PlayMakerFSM>(new FsmIdentity());
        private static readonly HashSet<PlayMakerFSM> RearWindowReadSources = new HashSet<PlayMakerFSM>(new FsmIdentity());
        private static readonly Dictionary<PlayMakerFSM, byte> HeaterHoseReadSources = new Dictionary<PlayMakerFSM, byte>(new FsmIdentity());

        private static bool ProjectHeaterRead(FsmStateAction action, BatteryReader read, NamedVariable output)
        {
            string variable = ((FsmString)read.Variable.GetValue(action)).Value;
            bool rearWindow = read.Boolean && variable == "HeatingSprites";
            if (!rearWindow && (read.Boolean ? variable != "Installed" : variable != "Wear")) return false;
            var fields = read.Source;
            var target = action.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)fields.Target.GetValue(action));
            if (target == null) return false;
            bool cached = target == (GameObject)fields.LastObject.GetValue(action);
            var source = cached ? (PlayMakerFSM)fields.CachedFsm.GetValue(action)
                : _resolveExternalFloatFsm!(target, ((FsmString)fields.FsmName.GetValue(action)).Value);
            if (source == null) return false;
            if (rearWindow) return ProjectRearWindowRead(action, source, output, fields, target, cached);
            if (read.Boolean && ProjectHeaterHoseRead(action, source, output, fields, target, cached)) return true;
            if (!IsHeaterReadSource(source)) return false;
            if (!SafeBatteryReadOutput(action, source, output, read.Boolean))
                throw new InvalidOperationException("Heater read output aliases saved/global data or is not consumer-local.");
            var state = WorldSyncManager.Instance?.ReadHeaterInputs();
            bool installed = state != null && state.Valid && (state.Flags & HeaterState.Installed) != 0;
            // Keep native same-object caching even while reading network state.
            if (!cached) { fields.CachedFsm.SetValue(action, source); fields.LastObject.SetValue(action, target); }
            if (read.Boolean) ((FsmBool)output).Value = installed;
            else ((FsmFloat)output).Value = installed ? state!.Wear : 0;
            return true;
        }

        private static bool IsHeaterReadSource(PlayMakerFSM source)
        {
            if (HeaterReadSources.Contains(source)) return true;
            var rule = SyncCatalog.GuestEngineInputs?.Heater;
            if (rule == null || !MatchesNativeReadSource(source, rule.Fsm, rule.Path)) return false;
            RememberExternalFloatTarget(source, FindRule(source));
            if (!ExternalFloatTargets.Contains(source)) throw new InvalidOperationException("Heater input source lacks write protection.");
            HeaterReadSources.Add(source);
            SyncEventLog.Record("guest-heater-read-source", rule.Path + "::" + rule.Fsm);
            return true;
        }

        private static bool ProjectRearWindowRead(FsmStateAction action, PlayMakerFSM source, NamedVariable output,
            ExternalFloatWriter fields, GameObject target, bool cached)
        {
            if (!RearWindowReadSources.Contains(source))
            {
                var rule = SyncCatalog.GuestEngineInputs?.Heater?.RearWindow;
                if (rule == null || !MatchesNativeReadSource(source, rule.Fsm, rule.Path)) return false;
                RearWindowReadSources.Add(source);
                SyncEventLog.Record("guest-rear-window-read-source", rule.Path + "::" + rule.Fsm);
            }
            if (!SafeBatteryReadOutput(action, source, output, true))
                throw new InvalidOperationException("Rear-window element output aliases saved/global data or is not consumer-local.");
            var state = WorldSyncManager.Instance?.ReadHeaterInputs();
            if (!cached) { fields.CachedFsm.SetValue(action, source); fields.LastObject.SetValue(action, target); }
            ((FsmBool)output).Value = state != null && state.Valid && state.RearWindowFlags == (HeaterState.Available | HeaterState.Installed);
            return true;
        }

        private static bool ProjectHeaterHoseRead(FsmStateAction action, PlayMakerFSM source, NamedVariable output,
            ExternalFloatWriter fields, GameObject target, bool cached)
        {
            if (!FindHeaterHoseReadSource(source, out byte index)) return false;
            if (!SafeBatteryReadOutput(action, source, output, true))
                throw new InvalidOperationException("Heater hose output aliases saved/global data or is not consumer-local.");
            foreach (var hose in HeaterHoseReadSources.Keys)
                if (hose != null) foreach (var value in hose.FsmVariables.BoolVariables)
                    if (ReferenceEquals(value, output)) throw new InvalidOperationException("Heater hose output aliases another saved mount.");
            if (!cached) { fields.CachedFsm.SetValue(action, source); fields.LastObject.SetValue(action, target); }
            ((FsmBool)output).Value = WorldSyncManager.Instance?.ReadHeaterHoseInstalled(index) ?? false;
            return true;
        }

        private static bool FindHeaterHoseReadSource(PlayMakerFSM source, out byte index)
        {
            if (HeaterHoseReadSources.TryGetValue(source, out index)) return true;
            var block = SyncCatalog.GuestEngineInputs?.Block; if (block == null) return false;
            foreach (var hose in block.CoolantHoses)
                if (hose.ProjectNativeReads && MatchesNativeReadSource(source, hose.Mount.Fsm, hose.Mount.MountPath))
                {
                    index = hose.Index; HeaterHoseReadSources.Add(source, index);
                    SyncEventLog.Record("guest-heater-hose-read-source", hose.Mount.MountPath + "::" + hose.Mount.Fsm);
                    return true;
                }
            return false;
        }

        private static void RetireHeaterHoseReadSources()
        {
            foreach (var source in new List<PlayMakerFSM>(HeaterHoseReadSources.Keys))
                if (Destroyed(source)) HeaterHoseReadSources.Remove(source);
        }
    }
}
