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
        private static readonly Dictionary<PlayMakerFSM, uint> WireReadSources = new Dictionary<PlayMakerFSM, uint>(new FsmIdentity());

        private static bool ProjectWireRead(FsmStateAction action, BatteryReader read, NamedVariable output)
        {
            if (!read.Boolean || ((FsmString)read.Variable.GetValue(action)).Value != "Installed") return false;
            var fields = read.Source;
            var target = action.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)fields.Target.GetValue(action));
            if (target == null) return false;
            bool cached = target == (GameObject)fields.LastObject.GetValue(action);
            var source = cached ? (PlayMakerFSM)fields.CachedFsm.GetValue(action)
                : _resolveExternalFloatFsm!(target, ((FsmString)fields.FsmName.GetValue(action)).Value);
            if (source == null || !FindWireReadSource(source, out uint id)) return false;
            if (!SafeBatteryReadOutput(action, source, output, true))
                throw new InvalidOperationException("Wire read output aliases saved/global data or is not consumer-local.");
            foreach (var wire in WireReadSources.Keys)
                if (wire != null) foreach (var value in wire.FsmVariables.BoolVariables)
                    if (ReferenceEquals(value, output)) throw new InvalidOperationException("Wire read output aliases another saved circuit.");
            var state = WorldSyncManager.Instance?.ReadWiringInputs(id);
            bool installed = state != null && (state.Flags & WiringState.Installed) != 0;
            // Keep the real source cached, matching PlayMaker's same-object name
            // behavior. Only the consumer result comes from the host record.
            if (!cached) { fields.CachedFsm.SetValue(action, source); fields.LastObject.SetValue(action, target); }
            ((FsmBool)output).Value = installed;
            return true;
        }

        private static bool FindWireReadSource(PlayMakerFSM source, out uint id)
        {
            if (WireReadSources.TryGetValue(source, out id)) return true;
            var profile = SyncCatalog.GuestEngineInputs; if (profile == null) return false;
            foreach (var rule in profile.Wires)
                if (rule.ProjectNativeReads && MatchesNativeReadSource(source, rule.Fsm, rule.Path))
                {
                    id = rule.Id; WireReadSources.Add(source, id);
                    SyncEventLog.Record("guest-wire-read-source", rule.Path + "::" + rule.Fsm + " id=" + id);
                    return true;
                }
            return false;
        }

        private static void RetireWireReadSources()
        {
            foreach (var source in new List<PlayMakerFSM>(WireReadSources.Keys)) if (Destroyed(source)) WireReadSources.Remove(source);
        }
    }
}
