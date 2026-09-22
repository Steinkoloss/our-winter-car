using System;
using System.Collections.Generic;
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
    /// <summary>The native host alone consumes the trailer load and creates delivery offers.</summary>
    internal sealed partial class FirewoodDeliverySync
    {
        private FirewoodDeliveryData? _c;
        private PlayMakerFSM? _load, _ground;
        private FsmFloat[] _values = new FsmFloat[0];
        private FsmBool? _unload;
        private Rigidbody? _bed;
        private Transform? _bedPile;
        private GameObject? _prefab;
        private readonly FsmSuppressor _loadPause = new FsmSuppressor(), _groundPause = new FsmSuppressor();
        private readonly List<GameObject> _piles = new List<GameObject>(), _originalPiles = new List<GameObject>();
        private readonly List<bool> _originalPileVisible = new List<bool>();
        private readonly Dictionary<byte, uint> _requests = new Dictionary<byte, uint>();
        private readonly List<KeyValuePair<FsmState, FsmStateAction>> _hooks = new List<KeyValuePair<FsmState, FsmStateAction>>();
        private float[]? _originalValues;
        private float _originalMass, _originalScale;
        private bool _originalUnload, _shownUnload, _disabled, _guest;
        private uint _epoch = 1, _sequence;
        private float _probeAt, _sendAt, _keepAliveAt;
        private FirewoodLoadState? _current, _remote, _sent;
        private bool Ready => _load != null && _ground != null && _originalValues != null;

        internal void Update(SessionManager session)
        {
            if (_disabled || (!session.IsHost && session.PlayerCount == 0)) return;
            try
            {
                if (!Ready && Time.unscaledTime >= _probeAt) { _probeAt = Time.unscaledTime + 2; Locate(session.IsHost); }
                if (!Ready) return;
                if (!session.IsHost) { Present(session); return; }
                if (Time.unscaledTime < _sendAt) return;
                _sendAt = Time.unscaledTime + .2f;
                var state = Capture();
                if (session.PlayerCount == 0 || (ReferenceEquals(_sent, state) && Time.unscaledTime < _keepAliveAt)) return;
                session.SendWorldMessage(state, Channel.ReliableOrdered); _sent = state; _keepAliveAt = Time.unscaledTime + 5;
            }
            catch (Exception e) { Disable(e); }
        }

        internal FirewoodLoadState? BuildSnapshot()
        {
            var session = SessionManager.Instance;
            if (_disabled || session == null || !session.IsHost) return null;
            try { if (!Ready) Locate(true); return Ready ? Capture() : null; }
            catch (Exception e) { Disable(e); return null; }
        }

        internal void Apply(FirewoodLoadState state)
        {
            if (_disabled || SessionManager.Instance?.IsHost != false || !FirewoodLoadPolicy.Valid(state)) return;
            if (_remote != null && !FirewoodLoadPolicy.Newer(state.Revision, _remote.Revision)) return;
            _remote = FirewoodLoadPolicy.Copy(state);
        }

        internal void OnPlayerAdmitted(byte playerId) { _requests.Remove(playerId); }

        internal void Request(FirewoodUnloadIntent request)
        {
            var session = SessionManager.Instance;
            if (_disabled || session == null || !session.IsHost || !Ready) return;
            try
            {
                var state = Capture();
                bool accepted = false;
                foreach (var player in session.Players)
                {
                    if (player.PlayerId != request.PlayerId) continue;
                    float age = Time.unscaledTime - player.LastTransformTime;
                    bool fresh = !player.IsDead && player.LastTransformTime > 0 && age >= 0 && age <= 2;
                    bool sequence = !_requests.TryGetValue(request.PlayerId, out var last) || FirewoodLoadPolicy.Newer(request.Sequence, last);
                    if (sequence && FirewoodLoadPolicy.CanUnload(request, state.Epoch, state.Logs, state.Unloading,
                        _load!.enabled && _load.gameObject.activeInHierarchy && (_load.ActiveStateName == _c!["idle"] || state.Unloading), fresh, (player.Position - _load.transform.position).sqrMagnitude))
                    {
                        _requests[request.PlayerId] = request.Sequence;
                        _unload!.Value = request.Unload; accepted = true;
                    }
                    break;
                }
                SyncEventLog.Record("firewood-delivery", "unload player=" + request.PlayerId + " epoch=" + request.Epoch + " accepted=" + accepted);
                session.SendWorldMessage(Capture(), Channel.ReliableOrdered);
            }
            catch (Exception e) { Disable(e); }
        }

        private FirewoodLoadState Capture()
        {
            var pile = _ground!.FsmVariables.FindFsmGameObject(_c!["newGroundPile"]).Value;
            if (pile != null && !_piles.Contains(pile)) _piles.Add(pile);
            var live = new List<FirewoodPile>();
            foreach (var obj in _piles)
            {
                if (obj == null) continue;
                var mesh = obj.transform.Find(_c["meshChild"]);
                if (mesh == null) throw new InvalidOperationException("Native delivery pile mesh missing.");
                var p = obj.transform.position; var q = obj.transform.rotation;
                live.Add(new FirewoodPile { Position = new NetVector3(p.x, p.y, p.z), Rotation = new NetQuaternion(q.x, q.y, q.z, q.w), Scale = mesh.localScale.z });
            }
            var state = new FirewoodLoadState { Epoch = _epoch, Logs = _values[0].Value, Firewood = _values[1].Value,
                Mass = _bed!.mass, BedScale = _bedPile!.localScale.z, Unloaded = _values[4].Value, Unloading = _unload!.Value, Piles = live.ToArray() };
            if (!FirewoodLoadPolicy.Valid(state)) throw new InvalidOperationException("Native firewood load outside supported bounds.");
            if (_current != null && FirewoodLoadPolicy.Same(_current, state)) return _current;
            state.Revision = unchecked((_current?.Revision ?? 0) + 1); _current = state; return state;
        }

        private void Present(SessionManager session)
        {
            var state = _remote;
            if (_unload!.Value != _shownUnload && state != null)
            {
                session.SendWorldMessage(new FirewoodUnloadIntent { PlayerId = session.LocalPlayerId,
                    Epoch = state.Epoch, Sequence = unchecked(++_sequence), Unload = _unload.Value }, Channel.ReliableOrdered);
            }
            // Guest native hatch/tilt writes are requests. They cannot start a local
            // delivery while acknowledgement and the host's native simulation run.
            _shownUnload = state != null && state.Unloading; _unload.Value = _shownUnload;
            if (state == null) return;
            _values[0].Value = state.Logs; _values[1].Value = state.Firewood; _values[2].Value = state.Mass;
            _values[3].Value = state.BedScale; _values[4].Value = state.Unloaded;
            _bed!.mass = state.Mass; Scale(_bedPile!, state.BedScale);
            while (_piles.Count > state.Piles.Length)
            { int i = _piles.Count - 1; if (_piles[i] != null) UnityEngine.Object.Destroy(_piles[i]); _piles.RemoveAt(i); }
            for (int i = 0; i < state.Piles.Length; i++)
            {
                if (i == _piles.Count) _piles.Add(null!);
                if (_piles[i] == null)
                {
                    _piles[i] = (GameObject)UnityEngine.Object.Instantiate(_prefab!);
                    _piles[i].name = "WinterMP wood pile";
                }
                var p = state.Piles[i];
                _piles[i].transform.position = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
                _piles[i].transform.rotation = new Quaternion(p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W);
                Scale(_piles[i].transform.Find(_c!["meshChild"]), p.Scale);
            }
        }

        private static void Scale(Transform target, float z) { var s = target.localScale; s.z = z; target.localScale = s; }
        private void Disable(Exception e)
        {
            if (_disabled) return;
            _disabled = true;
            WinterMPPlugin.Log.LogError("Firewood delivery sync disabled; other world sync continues: " + e);
        }

        internal void Clear()
        {
            try
            {
                foreach (var hook in _hooks)
                {
                    var actions = new List<FsmStateAction>(hook.Key.Actions); actions.Remove(hook.Value); hook.Key.Actions = actions.ToArray();
                }
                if (_guest && _originalValues != null)
                {
                    if (_load != null)
                    {
                        for (int i = 0; i < _values.Length; i++) _values[i].Value = _originalValues[i];
                        _unload!.Value = _originalUnload;
                    }
                    if (_bed != null) _bed.mass = _originalMass;
                    if (_bedPile != null) Scale(_bedPile, _originalScale);
                    foreach (var pile in _piles) if (pile != null) UnityEngine.Object.Destroy(pile);
                    for (int i = 0; i < _originalPiles.Count; i++) if (_originalPiles[i] != null) _originalPiles[i].SetActive(_originalPileVisible[i]);
                }
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Firewood delivery restore failed: " + e.Message); }
            finally
            {
                _loadPause.Restore(); _groundPause.Restore();
                _hooks.Clear(); _piles.Clear(); _originalPiles.Clear(); _originalPileVisible.Clear(); _requests.Clear();
                _originalValues = null; _values = new FsmFloat[0]; _load = _ground = null; _c = null; _bed = null; _bedPile = null; _prefab = null; _unload = null;
                _current = _remote = _sent = null; _epoch = 1; _sequence = 0; _probeAt = _sendAt = _keepAliveAt = 0; _shownUnload = _disabled = _guest = false;
            }
        }
    }
}
