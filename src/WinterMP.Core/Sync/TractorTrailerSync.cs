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
    internal sealed partial class TractorTrailerSync
    {
        private TractorTrailerData? _c;
        private PlayMakerFSM? _hook, _detach, _remove, _hydraulics;
        private FsmBool? _attached;
        private Rigidbody[]? _bodies;
        private SyncedItem? _tractor;
        private Transform? _target;
        private CharacterJoint? _joint;
        private readonly FsmSuppressor _hookPause = new FsmSuppressor(), _detachPause = new FsmSuppressor();
        private readonly List<Action> _restore = new List<Action>();
        private readonly Dictionary<byte, uint> _requests = new Dictionary<byte, uint>();
        private TractorTrailerState? _current, _remote;
        private TrailerBodyPose[]? _targetBodies;
        private bool _failed, _guest, _localPhysics, _applying;
        private float _probeAt, _sendAt, _keepAliveAt;
        private uint _outSequence, _inSequence, _intentSequence, _presentedRevision;

        internal void Update(SessionManager session, ItemWorldSync items)
        {
            if (_failed || Application.loadedLevelName != "GAME" || !session.IsHost && session.PlayerCount == 0) return;
            try
            {
                if (_bodies == null && Time.unscaledTime >= _probeAt) { _probeAt = Time.unscaledTime + 2; Locate(session, items); }
                if (_bodies == null) return;
                if (session.IsHost)
                {
                    var state = Capture();
                    _tractor!.HostTrailerAttached = state.Attached;
                    if (_presentedRevision != state.Revision)
                    {
                        _presentedRevision = state.Revision; _outSequence = _inSequence = 0;
                        SetPhysics(state.Owner == session.LocalPlayerId, state.Bodies);
                        session.SendWorldMessage(state, Channel.ReliableOrdered);
                        SyncEventLog.Record("tractor-trailer", "revision=" + state.Revision + " attached=" + state.Attached + " owner=" + state.Owner);
                    }
                    else if (Time.unscaledTime >= _keepAliveAt)
                    { _keepAliveAt = Time.unscaledTime + 5; session.SendWorldMessage(state, Channel.ReliableOrdered); }
                }
                else if (_remote != null && _presentedRevision != _remote.Revision)
                {
                    PresentConnection(_remote);
                    _presentedRevision = _remote.Revision; _outSequence = _inSequence = 0;
                    _targetBodies = _remote.Bodies;
                    ApplyBodies(_remote.Bodies, 1);
                    SetPhysics(_remote.Owner == session.LocalPlayerId, _remote.Bodies);
                }
                var accepted = session.IsHost ? _current : _remote;
                if (accepted == null || !_localPhysics || session.PlayerCount == 0 || Time.unscaledTime < _sendAt) return;
                _sendAt = Time.unscaledTime + 1f / 15;
                var motion = new TractorTrailerMotion { Revision = accepted.Revision, Owner = session.LocalPlayerId, Sequence = Next(ref _outSequence), Bodies = Bodies() };
                if (TractorTrailerPolicy.Valid(motion)) session.SendWorldMessage(motion, Channel.UnreliableSequenced);
            }
            catch (Exception e) { Disable(e); }
        }

        internal void LateUpdate()
        {
            if (_failed || _localPhysics || _bodies == null || _targetBodies == null) return;
            try { ApplyBodies(_targetBodies, Mathf.Clamp01(Time.unscaledDeltaTime * 15)); }
            catch (Exception e) { Disable(e); }
        }

        private byte NativeOwner()
        {
            if (_attached!.Value && _tractor != null && !_tractor.LocallyOwned && _tractor.RemoteOwner != 255
                && ItemTransformPolicy.IsRemoteStreamLive(_tractor.LastRemoteAt, Time.unscaledTime, _tractor.RemoteIsDriver, _tractor.RemoteVehicleStream)) return _tractor.RemoteOwner;
            return 0;
        }

        private TractorTrailerState Capture()
        {
            bool attached = _attached!.Value;
            if ((_joint!.connectedBody == _tractor!.Body) != attached) throw new InvalidOperationException("Native tractor hitch flag and joint disagree.");
            byte owner = NativeOwner(); uint revision = _current?.Revision ?? 1;
            if (_current != null && (_current.Attached != attached || _current.Owner != owner)) Next(ref revision);
            _current = new TractorTrailerState { Revision = revision, Owner = owner, Attached = attached, ConnectedAnchor = _joint.connectedAnchor.ToNet(), Bodies = Bodies() };
            if (!TractorTrailerPolicy.Valid(_current)) throw new InvalidOperationException("Native trailer pose outside supported bounds.");
            return _current;
        }

        internal TractorTrailerState? Snapshot()
        {
            if (_failed || _bodies == null || SessionManager.Instance?.IsHost != true) return null;
            try { return Capture(); } catch (Exception e) { Disable(e); return null; }
        }

        internal void Receive(TractorTrailerState state)
        {
            if (_failed || SessionManager.Instance?.IsHost != false || !TractorTrailerPolicy.Valid(state)
                || _remote != null && !TractorTrailerPolicy.Newer(state.Revision, _remote.Revision)) return;
            _remote = state;
        }

        internal void Request(TractorTrailerIntent intent, byte actor)
        {
            var session = SessionManager.Instance;
            if (_failed || _bodies == null || session?.IsHost != true || !TractorTrailerPolicy.Valid(intent)) return;
            try
            {
                var state = Capture(); _requests.TryGetValue(actor, out uint last);
                bool fresh = GamblingSync.TryPlayerPosition(session, actor, out var position);
                bool accepted = TractorTrailerPolicy.CanRelease(state, intent, actor, last, fresh, (position - _remove!.transform.position).sqrMagnitude);
                if (actor == intent.PlayerId && TractorTrailerPolicy.Newer(intent.Sequence, last)) _requests[actor] = intent.Sequence;
                if (accepted)
                {
                    FsmHook.FireRemoteEntry(_remove, _c!["releaseState"]);
                    state = Capture();
                }
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                SyncEventLog.Record("tractor-trailer-release", "actor=" + actor + " sequence=" + intent.Sequence + " accepted=" + accepted);
            }
            catch (Exception e) { Disable(e); }
        }

        internal void ReceiveMotion(TractorTrailerMotion motion, byte actor)
        {
            var session = SessionManager.Instance;
            if (_failed || _bodies == null || session == null || !TractorTrailerPolicy.Valid(motion)) return;
            try
            {
                var state = session.IsHost ? _current : _remote;
                if (state == null || motion.Revision != state.Revision || motion.Owner != state.Owner
                    || !TractorTrailerPolicy.Newer(motion.Sequence, _inSequence)) return;
                if (session.IsHost)
                {
                    var p = motion.Bodies[0];
                    var target = p.Position.ToUnity() + p.Rotation.ToUnity() * _bodies[0].transform.InverseTransformPoint(_target!.position);
                    if (!TractorTrailerPolicy.CanMove(state, motion, actor, _inSequence, NativeOwner() == actor, (target - _hook!.transform.position).sqrMagnitude)) return;
                    session.SendWorldMessage(motion, Channel.UnreliableSequenced);
                }
                // A delayed grant must never replace a later revision's ownership.
                if (state.Owner == session.LocalPlayerId) return;
                _inSequence = motion.Sequence; _targetBodies = motion.Bodies;
            }
            catch (Exception e) { Disable(e); }
        }

        internal void PlayerAdmitted(byte actor) { _requests.Remove(actor); }

        private static uint Next(ref uint n) { n = unchecked(n + 1); if (n == 0) n = 1; return n; }
        private TrailerBodyPose[] Bodies()
        {
            var result = new TrailerBodyPose[3];
            for (int i = 0; i < result.Length; i++)
            {
                var b = _bodies![i];
                result[i] = new TrailerBodyPose { Position = b.position.ToNet(), Rotation = b.rotation.ToNet(),
                    Velocity = !_localPhysics && _targetBodies != null ? _targetBodies[i].Velocity : b.velocity.ToNet(),
                    AngularVelocity = !_localPhysics && _targetBodies != null ? _targetBodies[i].AngularVelocity : b.angularVelocity.ToNet() };
            }
            return result;
        }
        private void SetPhysics(bool local, TrailerBodyPose[] poses)
        {
            _localPhysics = local;
            for (int i = 0; i < 3; i++)
            {
                var body = _bodies![i]; body.isKinematic = !local;
                if (local) { body.velocity = poses[i].Velocity.ToUnity(); body.angularVelocity = poses[i].AngularVelocity.ToUnity(); body.WakeUp(); }
            }
        }
        private void ApplyBodies(TrailerBodyPose[] poses, float t)
        {
            // Parent-first world poses include the independent bed and support;
            // moving only FLATBED would drag their transforms through old joints.
            for (int i = 0; i < 3; i++)
            {
                var b = _bodies![i]; var p = poses[i].Position.ToUnity(); var q = poses[i].Rotation.ToUnity();
                float blend = (b.position - p).sqrMagnitude > 225 ? 1 : t;
                b.position = Vector3.Lerp(b.position, p, blend); b.rotation = Quaternion.Slerp(b.rotation, q, blend);
            }
        }

        private void Disable(Exception error)
        {
            if (_failed) return;
            Clear(); _failed = true;
            WinterMPPlugin.Log.LogError("Tractor trailer sync disabled; other world sync continues: " + error);
        }

        internal void Clear()
        {
            if (_tractor != null) _tractor.HostTrailerAttached = false;
            for (int i = _restore.Count - 1; i >= 0; i--)
                try { _restore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Trailer restore: " + e.Message); }
            _restore.Clear(); _hookPause.Restore(); _detachPause.Restore(); _requests.Clear();
            _bodies = null; _tractor = null; _target = null; _joint = null; _attached = null;
            _hook = _detach = _remove = _hydraulics = null; _c = null; _current = _remote = null; _targetBodies = null;
            _failed = _guest = _localPhysics = _applying = false; _probeAt = _sendAt = _keepAliveAt = 0;
            _outSequence = _inSequence = _intentSequence = _presentedRevision = 0;
        }
    }
}
