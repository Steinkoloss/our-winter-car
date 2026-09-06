using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VenttiSync
    {
        private readonly VenttiPropertyReplica _propertyReplica = new VenttiPropertyReplica();
        private readonly FsmInt?[] _propertyKeys = new FsmInt?[3];
        private readonly int[] _originalKeys = new int[3];
        private readonly PropertyAccess[] _propertyAccess = { new PropertyAccess(), new PropertyAccess(), new PropertyAccess() };
        private bool _keysSaved, _propertyDisabled;
        private float _nextPropertyProbeAt, _nextPropertyTickAt, _nextPropertyKeepAliveAt;
        private uint _outPropertySequence;
        private VenttiPropertyState? _lastSentProperty;

        private bool PropertyKeysReady => _propertyKeys[0] != null && _propertyKeys[1] != null && _propertyKeys[2] != null;

        private void UpdateProperties(SessionManager session)
        {
            if (_propertyDisabled || Time.unscaledTime < _nextPropertyTickAt) return;
            _nextPropertyTickAt = Time.unscaledTime + 1f;
            try
            {
                LocateProperties();
                if (!session.IsHost) { ApplyProperties(); return; }
                var state = ReadProperties();
                if (state == null) return;
                bool changed = _lastSentProperty == null || !VenttiPropertyReplica.SameProperties(_lastSentProperty, state);
                if (Time.unscaledTime < _nextPropertyKeepAliveAt && !changed) return;
                state.Sequence = ++_outPropertySequence;
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                _lastSentProperty = state;
                _nextPropertyKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
                if (changed) SyncEventLog.Record("ventti-property",
                    $"keys 0x{state.Keys:X2} known 0x{state.KnownAccess:X2} access 0x{state.Access:X2}");
            }
            catch (Exception e) { DisableProperties(e); }
        }

        internal VenttiPropertyState? BuildPropertySnapshot()
        {
            var session = SessionManager.Instance;
            if (_propertyDisabled || session == null || !session.IsHost) return null;
            try
            {
                LocateProperties();
                var state = ReadProperties();
                // A join must not consume changes still owed to connected guests.
                if (state != null) state.Sequence = ++_outPropertySequence;
                return state;
            }
            catch (Exception e) { DisableProperties(e); return null; }
        }

        internal void OnPropertyState(VenttiPropertyState state)
        {
            var session = SessionManager.Instance;
            if (_propertyDisabled || session == null || session.IsHost || !_propertyReplica.Receive(state)) return;
            try { LocateProperties(); ApplyProperties(); }
            catch (Exception e) { DisableProperties(e); }
        }

        private VenttiPropertyState? ReadProperties()
        {
            if (!PropertyKeysReady || _manager == null) return null;
            var state = new VenttiPropertyState();
            for (int i = 0; i < _propertyKeys.Length; i++)
            {
                var key = _propertyKeys[i];
                if (key == null || (key.Value != 0 && key.Value != 1)) return null;
                if (key.Value == 1) state.Keys |= (byte)(1 << i);
            }
            for (int i = 0; i < _propertyAccess.Length; i++)
            {
                if (!_propertyAccess[i].Ready) continue;
                byte bit = (byte)(1 << i);
                state.KnownAccess |= bit;
                if (_propertyAccess[i].Value) state.Access |= bit;
            }
            return state;
        }

        private void ApplyProperties()
        {
            var state = _propertyReplica.Current;
            if (state == null || !PropertyKeysReady || _manager == null) return;
            // Receiving a title is never permission to execute the guest's transfer
            // resolver: those states also charge money, alter stress and affect saves.
            GuestLockdown();
            if (!ManagerLocked) return;
            if (!_keysSaved)
            {
                for (int i = 0; i < _propertyKeys.Length; i++)
                {
                    var key = _propertyKeys[i];
                    if (key != null) _originalKeys[i] = key.Value;
                }
                _keysSaved = true;
            }
            for (int i = 0; i < _propertyKeys.Length; i++)
            {
                var key = _propertyKeys[i];
                if (key != null) key.Value = (state.Keys & (1 << i)) != 0 ? 1 : 0;
            }
            for (int i = 0; i < _propertyAccess.Length; i++)
                if ((state.KnownAccess & (1 << i)) != 0)
                    _propertyAccess[i].Apply((state.Access & (1 << i)) != 0);
        }

        private void LocateProperties()
        {
            var bindings = SyncCatalog.VenttiProperty;
            if (bindings == null || Time.unscaledTime < _nextPropertyProbeAt) return;
            _nextPropertyProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
            var globals = FsmVariables.GlobalVariables;
            if (globals != null)
            {
                if (_propertyKeys[0] == null) _propertyKeys[0] = globals.FindFsmInt(bindings["rusckoKey"]);
                if (_propertyKeys[1] == null) _propertyKeys[1] = globals.FindFsmInt(bindings["satsumaKey"]);
                if (_propertyKeys[2] == null) _propertyKeys[2] = globals.FindFsmInt(bindings["homeKey"]);
            }
            if (_manager != null && _propertyAccess[0].Ready && _propertyAccess[1].Ready && _propertyAccess[2].Ready) return;

            // Inactive table/LOD objects are still valid property state. Find() would
            // miss them and make an absent binding look like the host revoked access.
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (_manager == null && path == bindings["managerPath"] && fsm.FsmName == bindings["managerFsm"])
                    _manager = fsm;
                if (!_propertyAccess[0].Ready)
                {
                    var target = FindPropertyAncestor(fsm.transform, path, bindings["sleepPath"]);
                    if (target != null) _propertyAccess[0].Bind(target, null);
                }
                if (!_propertyAccess[1].Ready)
                {
                    var target = FindPropertyAncestor(fsm.transform, path, bindings["hatchPath"]);
                    if (target != null) _propertyAccess[1].Bind(target, null);
                }
                if (!_propertyAccess[2].Ready && path == bindings["loggingPath"] && fsm.FsmName == bindings["loggingFsm"])
                    _propertyAccess[2].Bind(fsm.gameObject, fsm);
            }
        }

        private static GameObject? FindPropertyAncestor(Transform transform, string path, string targetPath)
        {
            if (path != targetPath && !path.StartsWith(targetPath + "/", StringComparison.Ordinal)) return null;
            // Cabin Sleep has only a child FSM (SleepTrigger/Activate). Bind the
            // parent toggled by the wager, including while that parent is inactive.
            for (var current = transform; current != null; current = current.parent)
                if (ScenePath.Of(current) == targetPath) return current.gameObject;
            return null;
        }

        private void RestoreProperties()
        {
            for (int i = 0; i < _propertyKeys.Length; i++)
            {
                var key = _propertyKeys[i];
                try { if (_keysSaved && key != null) key.Value = _originalKeys[i]; }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("VenttiSync: key restore: " + e.Message); }
            }
            _keysSaved = false;
            foreach (var access in _propertyAccess)
            {
                try { access.Restore(); }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("VenttiSync: access restore: " + e.Message); }
            }
        }

        private void DisableProperties(Exception error)
        {
            _propertyDisabled = true;
            RestoreProperties();
            WinterMPPlugin.Log.LogError("VenttiSync: property sync disabled: " + error);
            SyncEventLog.Record("ventti-property", "disabled: " + error.Message);
        }

        private void ClearProperties()
        {
            RestoreProperties();
            _propertyReplica.Clear();
            for (int i = 0; i < _propertyKeys.Length; i++) _propertyKeys[i] = null;
            foreach (var access in _propertyAccess) access.Clear();
            _propertyDisabled = false;
            _outPropertySequence = 0;
            _lastSentProperty = null;
            _nextPropertyProbeAt = _nextPropertyTickAt = _nextPropertyKeepAliveAt = 0f;
        }

        private sealed class PropertyAccess
        {
            private GameObject? _object;
            private PlayMakerFSM? _fsm;
            private bool _isFsm, _saved, _original;
            internal bool Ready => _object != null && (!_isFsm || _fsm != null);
            internal bool Value => _isFsm ? _fsm != null && _fsm.enabled : _object != null && _object.activeSelf;
            internal void Bind(GameObject obj, PlayMakerFSM? fsm)
            {
                _object = obj; _fsm = fsm; _isFsm = fsm != null; _saved = false;
            }
            internal void Apply(bool value)
            {
                if (!Ready) return;
                if (!_saved) { _original = Value; _saved = true; }
                Set(value);
            }
            private void Set(bool value)
            {
                if (!Ready || Value == value) return;
                var world = WorldSyncManager.Instance;
                bool previous = world != null && world.ApplyingRemote;
                if (world != null) world.ApplyingRemote = true;
                try
                {
                    if (_isFsm && _fsm != null) _fsm.enabled = value;
                    else if (_object != null) _object.SetActive(value);
                }
                finally { if (world != null) world.ApplyingRemote = previous; }
            }
            internal void Restore() { if (_saved) Set(_original); _saved = false; }
            internal void Clear() { _object = null; _fsm = null; _saved = false; }
        }
    }
}
