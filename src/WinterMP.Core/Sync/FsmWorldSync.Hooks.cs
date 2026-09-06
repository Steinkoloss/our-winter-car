using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private sealed class OwnedHook
        {
            public PlayMakerFSM Fsm = null!;
            public FsmState State = null!;
            public FsmStateAction Action = null!;
        }
        private readonly List<OwnedHook> _ownedHooks = new List<OwnedHook>();
        private readonly HashSet<PlayMakerFSM> _registeredFsms = new HashSet<PlayMakerFSM>();

        private bool BeginRegistration(PlayMakerFSM fsm)
        {
            if (_bridge.HookedFsms.ContainsKey(fsm)) return false;
            // A previous attempt can fail after its first state was hooked. Retry
            // from native/foreign actions, without accumulating duplicate callbacks.
            RemoveOwnedHooks(fsm);
            return true;
        }

        private void MarkRegistered(PlayMakerFSM fsm)
        {
            _registeredFsms.Add(fsm);
            _bridge.HookedFsms[fsm] = true;
        }

        private bool HookState(PlayMakerFSM fsm, string state, Action callback)
        {
            if (!FsmHook.OnStateEnter(fsm, state, callback, out var hook) || hook == null) return false;
            _ownedHooks.Add(new OwnedHook { Fsm = fsm, State = FsmHook.FindState(fsm, state)!, Action = hook });
            return true;
        }

        private void RemoveOwnedHooks(PlayMakerFSM? fsm)
        {
            for (int i = _ownedHooks.Count - 1; i >= 0; i--)
            {
                var owned = _ownedHooks[i];
                if (!ReferenceEquals(fsm, null) && owned.Fsm != fsm) continue;
                // A failed native-array write must not leave a callback sending
                // events against a cleared registry in the next session.
                owned.Action.Enabled = false;
                if (owned.Fsm != null)
                {
                    try
                    {
                        var actions = new List<FsmStateAction>(owned.State.Actions);
                        actions.Remove(owned.Action);
                        owned.State.Actions = actions.ToArray();
                    }
                    catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: could not remove FSM hook: " + e.Message); }
                }
                _ownedHooks.RemoveAt(i);
            }
        }

        private void ClearFsmHooks()
        {
            RemoveOwnedHooks(null);
            foreach (var fsm in _registeredFsms) _bridge.HookedFsms.Remove(fsm);
            _registeredFsms.Clear();
        }

        internal void ForgetNativePartBindings(PlayMakerFSM root, bool descendants)
        {
            var fsms = new HashSet<PlayMakerFSM>(descendants
                ? root.GetComponentsInChildren<PlayMakerFSM>(true) : new[] { root });
            RequireNoPartEntries(_controls, fsms, x => x.Fsm);
            RequireNoPartEntries(_doors, fsms, x => x.Fsm);
            RequireNoPartEntries(_buys, fsms, x => x.Fsm);
            RequireNoPartEntries(_ignitions, fsms, x => x.Fsm);
            RequireNoPartEntries(_starters, fsms, x => x.Fsm);
            foreach (var fsm in fsms)
            {
                RetainIsolatedPartView(fsm);
                RemoveOwnedHooks(fsm);
                _bridge.HookedFsms.Remove(fsm); _registeredFsms.Remove(fsm);
            }
            RemovePartEntries(_bolts, fsms, x => x.Fsm);
        }

        private static void RequireNoPartEntries<T>(Dictionary<uint, T> entries, HashSet<PlayMakerFSM> fsms, Func<T, PlayMakerFSM> getFsm)
        {
            foreach (var entry in entries.Values)
                if (fsms.Contains(getFsm(entry))) throw new InvalidOperationException("Native part contains another synchronized subsystem.");
        }

        private static void RemovePartEntries<T>(Dictionary<uint, T> entries, HashSet<PlayMakerFSM> fsms, Func<T, PlayMakerFSM> getFsm)
        {
            foreach (uint id in new List<uint>(entries.Keys))
                if (fsms.Contains(getFsm(entries[id]))) entries.Remove(id);
        }
    }
}
