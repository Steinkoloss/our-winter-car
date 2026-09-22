using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        // Retain replaced actions until restoration so a late native callback
        // cannot fall through to its restored, saved-part owner wrapper.
        private readonly Dictionary<FsmStateAction, GuestEngineInputBinding> _guestEngineInputReaders =
            new Dictionary<FsmStateAction, GuestEngineInputBinding>();
        private readonly HashSet<FsmStateAction> _preparingEngineReads = new HashSet<FsmStateAction>();
        private uint _engineReadFailures;

        internal bool CanDeferGuestEngineInputs(PlayMakerFSM fsm)
        {
            if (!_guestEngineInputOriginalsReady || _guestEngineInputsRestoring
                || _guestEngineInputScope == null || !ReferenceEquals(_guestEngineInputScope, SyncCatalog.GuestEngineInputs)) return false;
            // Valve SetFloatValue actions read shared scratch directly, outside
            // GetFsm*. Keep their existing refresh path.
            foreach (var valve in _guestValveInputs) if (ReferenceEquals(valve.Fsm, fsm)) return false;
            bool found = false;
            foreach (var binding in _guestEngineInputs)
                if (ReferenceEquals(binding.Fsm, fsm))
                {
                    if (!binding.InputsReady || binding.Rebind) return false;
                    found = true;
                }
            return found;
        }

        internal bool PrepareGuestEngineInputRead(FsmStateAction action)
        {
            _guestEngineInputReaders.TryGetValue(action, out var binding);
            bool entered = false;
            try
            {
                if (binding == null)
                {
                    // A reinitialized state can call its new action before the
                    // next frame or state-entry guard has rebound that slot.
                    foreach (var candidate in _guestEngineInputs)
                    {
                        if (!ReferenceEquals(action.Fsm.Owner, candidate.Fsm)) continue;
                        foreach (var read in candidate.Reads)
                            if (FsmHook.IsNativeAction(GuestEngineInputState(candidate.Fsm, read.Rule.State), read.Rule.ActionIndex, action))
                            { binding = candidate; break; }
                        if (binding != null) break;
                    }
                    if (binding == null) return true;
                }
                if (!_guestEngineInputOriginalsReady || _guestEngineInputsRestoring
                    || !ReferenceEquals(_guestEngineInputScope, SyncCatalog.GuestEngineInputs)
                    || _guestEngineInputScope == null || !_guestEngineInputScope.Entries.Contains(binding.Rule)
                    || !_guestEngineInputs.Contains(binding) || binding.Fsm == null
                    || !ReferenceEquals(action.Fsm.Owner, binding.Fsm))
                    throw new InvalidOperationException("Retired or changed native engine input reader.");
                entered = _preparingEngineReads.Add(action);
                if (!entered) throw new InvalidOperationException("Reentrant native engine input read.");
                uint failures = _engineReadFailures;
                // A previous readiness result never authorizes a native read:
                // validate live identity and refresh only this read's source.
                PrepareGuestEngineInputSource(binding.Fsm, binding.Rule, binding, null);
                if (!_guestEngineInputReaders.TryGetValue(action, out var current)
                    || !_guestEngineInputs.Contains(current) || !current.InputsReady || failures != _engineReadFailures)
                    throw new InvalidOperationException("Native engine reader was replaced during preparation.");
                // Refresh/binding can invoke native code. Observe changes made
                // there before allowing the actual reader to use its target.
                ValidateGuestEngineInputs(current);
                return true;
            }
            catch (Exception error)
            {
                _engineReadFailures = unchecked(_engineReadFailures + 1);
                if (binding != null) binding.InputsReady = false;
                var owner = binding != null ? binding.Fsm : action.Fsm?.Owner as PlayMakerFSM;
                if (owner != null) GuestEngineProtection.Fail(owner, error);
                else GuestEngineProtection.NoteFailure("native engine input read", error);
                return false;
            }
            finally { if (entered) _preparingEngineReads.Remove(action); }
        }
    }
}
