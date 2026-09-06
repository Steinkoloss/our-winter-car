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
    internal sealed partial class VenttiSync
    {
        private readonly VenttiSceneReplica _sceneReplica = new VenttiSceneReplica();
        private readonly List<ReactionPose> _reactionPoses = new List<ReactionPose>();
        private readonly Dictionary<Animation, bool> _reactionAnimations = new Dictionary<Animation, bool>();
        private readonly Dictionary<PlayMakerFSM, FsmSuppressor> _reactionFsms = new Dictionary<PlayMakerFSM, FsmSuppressor>();
        private readonly Dictionary<FsmStateAction, bool> _reactionActions = new Dictionary<FsmStateAction, bool>();
        private VenttiReactionData? _reactionConfig;
        private Transform? _reactionRoot;
        private VenttiSceneState? _lastSceneSent;
        private uint _sceneSequence;
        private float _sceneProbeAt, _sceneTickAt, _sceneKeepAliveAt;
        private bool _reactionsReady, _reactionsFailed, _reactionHost;

        private sealed class ReactionPose
        {
            public Transform Target = null!;
            public Vector3 Position, Velocity, AngularVelocity;
            public Quaternion Rotation;
            public bool Active, Kinematic;
            public Rigidbody? Body;
        }

        public void LateUpdate(SessionManager session)
        {
            if (!_gameReady || _gameFailed || _reactionsFailed || session.PlayerCount == 0) return;
            try
            {
                _reactionHost = session.IsHost;
                if (!_reactionsReady && Time.unscaledTime >= _sceneProbeAt)
                {
                    _sceneProbeAt = Time.unscaledTime + 2f;
                    SetupReactions();
                }
                if (!_reactionsReady) return;
                if (_reactionHost) PublishScene(session);
                else ApplyScene();
                UpdateSounds();
            }
            catch (Exception e) { DisableReactions(e); }
        }

        private void SetupReactions()
        {
            _reactionConfig = _gameConfig?.Reactions;
            if (_reactionConfig == null || _anchor == null) return;
            for (var t = _anchor; t != null; t = t.parent)
                if (ScenePath.Of(t) == _reactionConfig.RootPath) { _reactionRoot = t; break; }
            if (_reactionRoot == null) return;
            var targets = new List<Transform>();
            foreach (string path in _reactionConfig.Poses)
            {
                var target = _reactionRoot.Find(path);
                if (target == null) return;
                targets.Add(target);
            }
            // The complete parent-first layout is bound before any guest animation or
            // physics writer is paused. Late joins use the host's current pose, not a replay.
            foreach (var target in targets)
            {
                var body = target.GetComponent<Rigidbody>();
                _reactionPoses.Add(new ReactionPose
                {
                    Target = target, Position = target.localPosition, Rotation = target.localRotation,
                    Active = target.gameObject.activeSelf, Body = body, Kinematic = body == null || body.isKinematic,
                    Velocity = body == null ? Vector3.zero : body.velocity,
                    AngularVelocity = body == null ? Vector3.zero : body.angularVelocity,
                });
            }
            if (!_reactionHost)
            {
                foreach (var animation in targets[0].GetComponentsInChildren<Animation>(true))
                {
                    _reactionAnimations.Add(animation, animation.enabled);
                    animation.enabled = false;
                }
                foreach (var fsm in targets[0].GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                    foreach (var state in fsm.Fsm.States)
                        foreach (var action in state.Actions)
                        {
                            if (!_reactionActions.ContainsKey(action)) _reactionActions.Add(action, action.Enabled);
                            action.Enabled = false;
                        }
                    var pause = new FsmSuppressor();
                    if (!pause.Suppress(fsm)) throw new InvalidOperationException("Cannot pause guest Ventti NPC.");
                    _reactionFsms.Add(fsm, pause);
                }
                FreezeReactionWriters();
            }
            _reactionsReady = true;
            if (_reactionHost) InstallSoundHooks();
            WinterMPPlugin.Log.LogInfo("VenttiSync: NPC/furniture reactions bound (" + targets.Count + " poses).");
        }

        private void FreezeReactionWriters()
        {
            foreach (var entry in _reactionFsms)
                if (entry.Key != null && entry.Key.enabled) entry.Key.enabled = false;
            foreach (var entry in _reactionAnimations)
                if (entry.Key != null && entry.Key.enabled) entry.Key.enabled = false;
            foreach (var pose in _reactionPoses)
                if (pose.Body != null && !pose.Body.isKinematic) pose.Body.isKinematic = true;
        }

        private VenttiSceneState? ReadScene()
        {
            if (!_reactionsReady || _reactionConfig == null) return null;
            var poses = new VenttiPose[_reactionPoses.Count];
            for (int i = 0; i < poses.Length; i++)
            {
                var target = _reactionPoses[i].Target;
                if (target == null) return null;
                bool world = i < VenttiSceneState.WorldPoseCount;
                var p = world ? target.position : target.localPosition;
                var r = world ? target.rotation : target.localRotation;
                poses[i] = new VenttiPose { Position = new NetVector3(p.x, p.y, p.z),
                    Rotation = new NetQuaternion(r.x, r.y, r.z, r.w), Active = target.gameObject.activeSelf ? (byte)1 : (byte)0 };
            }
            var state = new VenttiSceneState { TableId = _tableId, LayoutId = _reactionConfig.LayoutId, Poses = poses };
            if (!VenttiSceneReplica.IsValid(state)) throw new InvalidOperationException("Invalid native Ventti reaction pose.");
            return state;
        }

        private void PublishScene(SessionManager session)
        {
            float now = Time.unscaledTime;
            if (now < _sceneTickAt) return;
            bool near = false;
            foreach (var player in session.Players)
                if (player.PlayerId != session.LocalPlayerId && player.LastTransformTime > 0 && now - player.LastTransformTime <= 2f)
                    for (int i = 0; i < VenttiSceneState.WorldPoseCount; i++)
                        if ((player.Position - _reactionPoses[i].Target.position).sqrMagnitude <= 6400f) near = true;
            _sceneTickAt = now + (near ? .1f : 1f);
            var state = ReadScene();
            if (state == null) return;
            bool changed = _lastSceneSent == null || !VenttiSceneReplica.Same(_lastSceneSent, state);
            if (!changed && now < _sceneKeepAliveAt) return;
            state.Sequence = ++_sceneSequence;
            session.SendWorldMessage(state, changed ? Channel.UnreliableSequenced : Channel.ReliableOrdered);
            _lastSceneSent = state;
            _sceneKeepAliveAt = now + 2f;
        }

        internal VenttiSceneState? BuildSceneSnapshot()
        {
            if (!_reactionHost || _reactionsFailed) return null;
            try
            {
                var state = ReadScene();
                if (state != null) state.Sequence = ++_sceneSequence;
                return state;
            }
            catch (Exception e) { DisableReactions(e); return null; }
        }

        public void OnSceneState(VenttiSceneState state)
        {
            var session = SessionManager.Instance;
            var config = SyncCatalog.VenttiTable?.Reactions;
            if (_reactionsFailed || session == null || session.IsHost || config == null) return;
            EnsureBuilt();
            _sceneReplica.Receive(_tableId, config.LayoutId, config.Poses.Length, state, Time.unscaledTime);
        }

        private void ApplyScene()
        {
            FreezeReactionWriters();
            var state = _sceneReplica.Current;
            if (state == null) return;
            var before = _sceneReplica.Previous;
            float blend = _sceneReplica.Blend(Time.unscaledTime);
            for (int i = 0; i < state.Poses.Length; i++)
            {
                var slot = _reactionPoses[i];
                if (slot.Target == null) continue;
                var pose = state.Poses[i];
                var position = Vector(pose.Position);
                var rotation = Rotation(pose.Rotation);
                if (before != null && before.Poses[i].Active == pose.Active)
                {
                    var previous = before.Poses[i];
                    // Never sweep a teleported rigidbody through the intervening world.
                    if (i >= VenttiSceneState.WorldPoseCount || (Vector(previous.Position) - position).sqrMagnitude < 625f)
                    {
                        position = Vector3.Lerp(Vector(previous.Position), position, blend);
                        rotation = Quaternion.Slerp(Rotation(previous.Rotation), rotation, blend);
                    }
                }
                if (i < VenttiSceneState.WorldPoseCount) { slot.Target.position = position; slot.Target.rotation = rotation; }
                else { slot.Target.localPosition = position; slot.Target.localRotation = rotation; }
                if (slot.Body != null) { slot.Body.position = slot.Target.position; slot.Body.rotation = slot.Target.rotation; }
                bool active = pose.Active != 0;
                if (slot.Target.gameObject.activeSelf != active) slot.Target.gameObject.SetActive(active);
            }
        }

        private static Vector3 Vector(NetVector3 p) => new Vector3(p.X, p.Y, p.Z);
        private static Quaternion Rotation(NetQuaternion q)
        {
            float length = Mathf.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            return new Quaternion(q.X / length, q.Y / length, q.Z / length, q.W / length);
        }

        private void DisableReactions(Exception e)
        {
            if (_reactionsFailed) return;
            _reactionsFailed = true;
            DisableSounds(e);
            WinterMPPlugin.Log.LogError("VenttiSync: NPC reactions disabled: " + e);
            SyncEventLog.Record("ventti-reactions-disabled", e.Message);
        }

        private void ClearReactions()
        {
            ClearSounds();
            if (!_reactionHost)
            {
                foreach (var pose in _reactionPoses)
                {
                    try
                    {
                        if (pose.Target == null) continue;
                        pose.Target.localPosition = pose.Position; pose.Target.localRotation = pose.Rotation;
                        if (pose.Body != null)
                        {
                            pose.Body.position = pose.Target.position; pose.Body.rotation = pose.Target.rotation;
                            pose.Body.isKinematic = pose.Kinematic;
                            if (!pose.Kinematic) { pose.Body.velocity = pose.Velocity; pose.Body.angularVelocity = pose.AngularVelocity; }
                        }
                        if (pose.Target.gameObject.activeSelf != pose.Active) pose.Target.gameObject.SetActive(pose.Active);
                    }
                    catch (Exception e) { WinterMPPlugin.Log.LogWarning("VenttiSync: reaction restore: " + e.Message); }
                }
                foreach (var entry in _reactionAnimations) if (entry.Key != null) entry.Key.enabled = entry.Value;
                foreach (var entry in _reactionActions) entry.Key.Enabled = entry.Value;
                foreach (var pause in _reactionFsms.Values) pause.Restore();
            }
            _reactionPoses.Clear(); _reactionAnimations.Clear(); _reactionActions.Clear(); _reactionFsms.Clear();
            _sceneReplica.Clear(); _reactionConfig = null; _reactionRoot = null; _lastSceneSent = null;
            _sceneSequence = 0; _sceneProbeAt = _sceneTickAt = _sceneKeepAliveAt = 0;
            _reactionsReady = _reactionsFailed = _reactionHost = false;
        }
    }
}
