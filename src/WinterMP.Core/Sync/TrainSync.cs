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
    internal sealed class TrainSync
    {
        private TrainData? _c;
        private Rigidbody? _body;
        private Transform? _east, _west, _targetEast, _targetWest;
        private GameObject? _mesh, _lights;
        private PlayMakerFSM? _move, _player;
        private AudioSource? _sound, _horn;
        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly List<Action> _restore = new List<Action>();
        private TrainState? _remote, _sent;
        private uint _sequence, _hornSequence, _presentedHorn;
        private byte _presentedPhase = 255;
        private float _probeAt, _sendAt, _reliableAt, _receivedAt;
        private bool _failed, _guest;
        internal uint NetId => StableHash.Fnv1a32("train:" + (_c?["root"] ?? "TRAIN"));

        internal static bool Owns(Transform node)
        {
            var c = SyncCatalog.Train;
            if (c == null) return false;
            string path = ScenePath.Of(node);
            return path == c["root"] || path.StartsWith(c["root"] + "/", StringComparison.Ordinal);
        }
        internal void Update(SessionManager session)
        {
            if (_failed || Application.loadedLevelName != "GAME") return;
            try
            {
                if (_body == null && Time.unscaledTime >= _probeAt) { _probeAt = Time.unscaledTime + 2; Locate(session); }
                if (_body == null) return;
                if (_player!.ActiveStateName == _c!["die"] && DeathSyncManager.Instance?.IsLocalDead == false)
                {
                    var death = _player.FsmVariables.FindFsmGameObject("Death")?.Value;
                    if (death != null && !death.activeSelf) FsmHook.FireRemoteEntry(_player, _c["idle"]);
                }
                if (_guest) { Present(); return; }
                if (session.PlayerCount == 0 || Time.unscaledTime < _sendAt) return;
                _sendAt = Time.unscaledTime + .05f;
                var state = Capture();
                bool reliable = _sent == null || !TrainPolicy.SameLifecycle(state, _sent) || Time.unscaledTime >= _reliableAt;
                if (reliable) _reliableAt = Time.unscaledTime + 2;
                session.SendWorldMessage(state, reliable ? Channel.ReliableOrdered : Channel.UnreliableSequenced);
                _sent = state;
            }
            catch (Exception e) { Disable(e); }
        }
        internal void FixedUpdate()
        {
            if (_failed || !_guest || _body == null || _remote == null) return;
            try
            {
                float age = Time.unscaledTime - _receivedAt;
                if (!TrainPolicy.Fresh(age)) return;
                _body.velocity = _body.angularVelocity = Vector3.zero;
                _body.MovePosition(TrainPolicy.Predict(_remote, age).ToUnity());
                _body.MoveRotation(_remote.Rotation.ToUnity());
            }
            catch (Exception e) { Disable(e); }
        }
        private TrainState Capture()
        {
            byte phase = Phase();
            Vector3 velocity = (phase & 1) != 0 ? Vector3.zero : ((phase == 0 ? _targetWest : _targetEast)!.position - _body!.position).normalized * 30;
            ushort mask = 0;
            for (int i = 0; i < _colliders.Count; i++) if (_colliders[i].enabled && _colliders[i].gameObject.activeInHierarchy) mask |= (ushort)(1 << i);
            var state = new TrainState { Sequence = Next(ref _sequence), HornSequence = _hornSequence, Phase = phase,
                Flags = (byte)((_body!.gameObject.activeSelf ? 1 : 0) | (_mesh!.activeSelf ? 2 : 0) | (_lights!.activeSelf ? 4 : 0)), ColliderMask = mask,
                Position = _body.position.ToNet(), Rotation = _body.rotation.ToNet(), Velocity = velocity.ToNet(), Volume = _sound!.volume };
            if (!TrainPolicy.Valid(state)) throw new InvalidOperationException("Native train state outside supported bounds.");
            return state;
        }
        private byte Phase()
        {
            string state = _move!.ActiveStateName;
            string[] phases = { _c!["westbound"], _c["westWait"], _c["eastbound"], _c["eastWait"] };
            for (byte i = 0; i < phases.Length; i++) if (state == phases[i]) return i;
            throw new InvalidOperationException("Unknown native train movement phase: " + state);
        }
        internal TrainState? Snapshot()
        {
            if (_failed || _body == null || _guest) return null;
            try { return Capture(); } catch (Exception e) { Disable(e); return null; }
        }
        internal void Receive(TrainState state)
        {
            if (_failed || SessionManager.Instance?.IsHost != false || !TrainPolicy.Valid(state)
                || _remote != null && !TrainPolicy.Newer(state.Sequence, _remote.Sequence)) return;
            if (_remote == null) _presentedHorn = state.HornSequence;
            _remote = state; _receivedAt = Time.unscaledTime;
        }
        private void Present()
        {
            var state = _remote;
            bool fresh = state != null && TrainPolicy.Fresh(Time.unscaledTime - _receivedAt);
            for (int i = 0; i < _colliders.Count; i++) _colliders[i].enabled = fresh && (state!.ColliderMask & (1 << i)) != 0;
            _sound!.volume = _horn!.volume = fresh ? state!.Volume : 0;
            if (!fresh) { _body!.velocity = _body.angularVelocity = Vector3.zero; return; }
            bool snap = _presentedPhase != state!.Phase || (_body!.position - state.Position.ToUnity()).sqrMagnitude > 400;
            if (_presentedPhase != state.Phase)
            {
                _body!.transform.parent = state.Phase < 2 ? _east : _west;
                _presentedPhase = state.Phase;
                SyncEventLog.Record("train-phase", state.Phase.ToString());
            }
            _body!.gameObject.SetActive((state.Flags & 1) != 0);
            _mesh!.SetActive((state.Flags & 2) != 0); _lights!.SetActive((state.Flags & 4) != 0);
            if (snap) { _body.position = state.Position.ToUnity(); _body.rotation = state.Rotation.ToUnity(); }
            if (TrainPolicy.Newer(state.HornSequence, _presentedHorn))
            { _presentedHorn = state.HornSequence; _horn.PlayOneShot(_horn.clip); }
        }

        private void Locate(SessionManager session)
        {
            _c = SyncCatalog.Train;
            if (_c == null) return;
            var root = GameObject.Find("/" + _c["root"]); if (root == null) return;
            _east = root.transform.Find(_c["east"]); _west = root.transform.Find(_c["west"]);
            _targetEast = root.transform.Find(_c["targetEast"]); _targetWest = root.transform.Find(_c["targetWest"]);
            if (_east == null || _west == null || _targetEast == null || _targetWest == null) throw new InvalidOperationException("Missing train endpoints.");
            var eastBody = _east.Find(_c["body"]); var westBody = _west.Find(_c["body"]);
            if (eastBody != null && westBody != null) throw new InvalidOperationException("Ambiguous native train.");
            var node = eastBody != null ? eastBody : westBody; if (node == null) return;
            var move = Fsm(node, _c["move"]); if (!move.Fsm.Started) return;
            _body = node.GetComponent<Rigidbody>(); if (_body == null) throw new InvalidOperationException("Train body missing.");
            _move = move; _player = Fsm(node, _c["player"]);
            _mesh = Child(node, _c["mesh"]).gameObject; _lights = Child(node, _c["lights"]).gameObject;
            _sound = Child(node, _c["sound"]).GetComponent<AudioSource>(); _horn = Child(node, _c["horn"]).GetComponent<AudioSource>();
            if (_sound == null || _horn == null || _horn.clip == null) throw new InvalidOperationException("Missing train audio.");
            var reset = Fsm(node, _c["reset"]); var whistle = Fsm(node, _c["whistle"]);
            var raycast = Fsm(Child(node, _c["raycastPath"]), _c["raycastFsm"]);
            Validate(move, reset, _player, whistle, raycast);
            foreach (string path in _c.Colliders)
            {
                var collider = Child(node, path).GetComponent<BoxCollider>();
                if (collider == null || collider.isTrigger) throw new InvalidOperationException("Native train collision shape changed.");
                _colliders.Add(collider);
            }
            if (node.GetComponentsInChildren<Collider>(true).Length != _colliders.Count) throw new InvalidOperationException("Native train collider inventory changed.");
            _guest = !session.IsHost;
            if (_guest)
            {
                foreach (var f in new[] { move, reset, whistle, raycast, Fsm(node, _c["tunnel"]), Fsm(Child(node, _c["lightFsmPath"]), _c["lightFsm"]) })
                {
                    var pause = new FsmSuppressor(); if (!pause.Suppress(f)) throw new InvalidOperationException("Cannot pause guest train controller.");
                    _restore.Add(pause.Restore);
                }
                var body = _body; var parent = node.parent; var position = node.position; var rotation = node.rotation;
                bool kinematic = body.isKinematic, gravity = body.useGravity; var constraints = body.constraints; var interpolation = body.interpolation;
                var velocity = body.velocity; var angular = body.angularVelocity;
                _restore.Add(() => {
                    if (body == null) return;
                    body.interpolation = RigidbodyInterpolation.None;
                    body.transform.parent = parent; body.transform.position = position; body.transform.rotation = rotation;
                    body.position = position; body.rotation = rotation; body.constraints = constraints;
                    body.useGravity = gravity; body.isKinematic = kinematic; body.interpolation = interpolation;
                    if (!kinematic) { body.velocity = velocity; body.angularVelocity = angular; }
                });
                body.velocity = body.angularVelocity = Vector3.zero; body.useGravity = false;
                // Unity 5 does not deliver the native CollisionEvent against the
                // player's CharacterController when this body is kinematic.
                body.isKinematic = false; body.interpolation = RigidbodyInterpolation.Interpolate;
                foreach (var go in new[] { node.gameObject, _mesh, _lights })
                { bool active = go.activeSelf; _restore.Add(() => { if (go != null) go.SetActive(active); }); }
                foreach (var collider in _colliders)
                { bool enabled = collider.enabled; _restore.Add(() => { if (collider != null) collider.enabled = enabled; }); collider.enabled = false; }
                foreach (var audio in new[] { _sound, _horn })
                { float volume = audio.volume; _restore.Add(() => { if (audio != null) audio.volume = volume; }); }
                _horn.Stop();
            }
            else
            {
                foreach (var f in new[] { whistle, raycast })
                {
                    if (!FsmHook.OnStateEnter(f, _c["hornState"], () => Next(ref _hornSequence), out var hook)) throw new InvalidOperationException("Cannot observe train horn.");
                    var state = FsmHook.FindState(f, _c["hornState"])!;
                    _restore.Add(() => { var actions = new List<FsmStateAction>(state.Actions); actions.Remove(hook!); state.Actions = actions.ToArray(); });
                }
            }
            SyncEventLog.Record("train-bound", _guest ? "host replica" : "native authority");
        }
        private static Transform Child(Transform root, string path) => root.Find(path) ?? throw new InvalidOperationException("Missing train child " + path);
        private static PlayMakerFSM Fsm(Transform root, string name)
        {
            PlayMakerFSM? result = null;
            foreach (var f in root.GetComponents<PlayMakerFSM>()) if (f.FsmName == name)
            { if (result != null) throw new InvalidOperationException("Duplicate train FSM."); result = f; }
            return result ?? throw new InvalidOperationException("Missing train FSM " + name);
        }
        private static FsmState State(PlayMakerFSM f, string name, params string[] types)
        {
            var s = FsmHook.FindState(f, name) ?? throw new InvalidOperationException("Missing train state " + name);
            if (!s.IsInitialized || s.Actions.Length != types.Length) throw new InvalidOperationException("Train action count changed: " + name);
            for (int i = 0; i < types.Length; i++) if (s.Actions[i].GetType().Name != types[i]) throw new InvalidOperationException("Train action changed: " + name);
            return s;
        }
        private static T? Field<T>(FsmStateAction action, string name) where T : class => action.GetType().GetField(name)?.GetValue(action) as T;
        private void Validate(PlayMakerFSM move, PlayMakerFSM reset, PlayMakerFSM player, PlayMakerFSM whistle, PlayMakerFSM raycast)
        {
            foreach (string state in new[] { _c!["westbound"], _c["eastbound"] })
            {
                var s = State(move, state, "SetParent", "MoveTowards");
                if (Field<FsmFloat>(s.Actions[1], "maxSpeed")?.Value != 30 || Field<FsmFloat>(s.Actions[1], "finishDistance")?.Value != .1f)
                    throw new InvalidOperationException("Native train movement rate changed.");
            }
            foreach (string state in new[] { _c["westWait"], _c["eastWait"] }) State(move, state, "ActivateGameObject", "Wait");
            State(reset, _c["idle"], "SetPosition", "SetRotation");
            State(player, _c["idle"], "CollisionEvent");
            var death = State(player, _c["die"], "SetFsmBool", "ActivateGameObject");
            if (Field<FsmString>(death.Actions[0], "variableName")?.Value != "Train") throw new InvalidOperationException("Native train death cause changed.");
            State(whistle, _c["hornState"], "AudioPlay", "Wait"); State(raycast, _c["hornState"], "AudioPlay", "Wait");
            if (!FsmHook.EnsureRemoteEntry(player, _c["idle"])) throw new InvalidOperationException("Train collision recovery unavailable.");
            Phase();
        }
        private static uint Next(ref uint value) { if (++value == 0) ++value; return value; }
        private void Disable(Exception e)
        {
            WinterMPPlugin.Log.LogError("Train sync disabled: " + e); SyncEventLog.Record("train-disabled", e.Message);
            Clear(); _failed = true;
        }
        internal void Clear()
        {
            for (int i = _restore.Count - 1; i >= 0; i--) try { _restore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Train restore: " + e.Message); }
            _restore.Clear(); _colliders.Clear(); _body = null;
            _east = _west = _targetEast = _targetWest = null; _mesh = _lights = null; _sound = _horn = null; _move = _player = null; _c = null;
            _remote = _sent = null; _sequence = _hornSequence = _presentedHorn = 0; _presentedPhase = 255;
            _probeAt = _sendAt = _reliableAt = _receivedAt = 0; _failed = _guest = false;
        }
    }
}
