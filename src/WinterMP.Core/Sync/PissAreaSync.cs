using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// The five native indoor stain transforms are live state. Scale1..5 are
    /// save/load caches: updating those alone neither displays nor persists a stain.
    /// </summary>
    internal sealed partial class PissAreaSync
    {
        private const string PissPath = "YARD/PissAreas";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = .5f;
        private const float KeepAliveSeconds = .5f;
        private const float ScaleQuant = 20f;

        private PlayMakerFSM? _logic;
        private readonly FsmFloat?[] _scales = new FsmFloat?[5];
        private readonly GameObject?[] _stains = new GameObject?[5];
        private Vector3[]? _guestOriginalScales;
        private static readonly string[] StainVariables = { "Stain1s3", "Stain2s5", "Stain3s7", "Stain4s4", "Stain5s4" };
        private static readonly float[] MaximumScales = { 3, 5, 7, 4, 4 };
        private bool _loggedFound;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;

        private bool _hasLast;
        private readonly byte[] _lastSent = new byte[5];

        private bool Ready
        {
            get {
                if (_logic == null || !_logic.Fsm.Initialized || !_logic.Fsm.Started) return false;
                for (int i = 0; i < 5; i++) if (_scales[i] == null || _stains[i] == null) return false;
                string state = _logic.ActiveStateName;
                return state != "State 1" && state != "1" && state != "2" && state != "3" && state != "4" && state != "5";
            }
        }

        public void Clear()
        {
            RestoreNative();
            if (_guestOriginalScales != null) {
                for (int i = 0; i < 5; i++) {
                    try { if (_stains[i] != null) _stains[i]!.transform.localScale = _guestOriginalScales[i]; }
                    catch (System.Exception e) { WinterMPPlugin.Log.LogError("PissArea guest restore: " + e); }
                }
                _guestOriginalScales = null;
            }
            _admissions.Clear(); _challenges.Clear();
            _epoch = _revision = _guestSequence = 0; _guestAdmission = 0;
            _executing = false; _pending = 0; _pendingArea = 0; _lastGuestSend = 0;
            _logic = null;
            for (int i = 0; i < 5; i++) { _scales[i] = null; _stains[i] = null; _lastSent[i] = 0; }
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = 0;
            _hasLast = false;
        }

        public void Update(SessionManager session)
        {
            if (!Active(session)) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }
            BindNative();
            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        public PissAreaState? BuildSnapshot()
        {
            var session = SessionManager.Instance;
            if (!Active(session) || !session!.IsHost || _executing) return null;
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(PissAreaState message)
        {
            var session = SessionManager.Instance;
            if (!Active(session) || session!.IsHost || _executing || message == null) return;
            Locate();
            if (!Ready) return;
            if (message.Epoch == 0 || message.Revision == 0 || (_epoch != 0 && _epoch != message.Epoch)
                || (_epoch != 0 && message.Revision <= _revision) || !ValidAdmissions(message.Admissions)) return;

            byte[] values = { message.Scale1, message.Scale2, message.Scale3, message.Scale4, message.Scale5 };
            for (int i = 0; i < 5; i++) if (values[i] > MaximumScales[i] * ScaleQuant) return;
            var before = new Vector3[5];
            for (int i = 0; i < 5; i++) before[i] = _stains[i]!.transform.localScale;
            _executing = true;
            try
            {
                for (int i = 0; i < 5; i++)
                    _stains[i]!.transform.localScale = new Vector3(values[i] / ScaleQuant, values[i] / ScaleQuant, 1);
                if (_guestOriginalScales == null) _guestOriginalScales = before;
                _epoch = message.Epoch; _revision = message.Revision;
                ulong admission = 0;
                foreach (var entry in message.Admissions)
                    if (entry.Actor == session.LocalPlayerId) {
                        admission = entry.Token;
                        if (_guestAdmission != admission) { _guestSequence = entry.HighWater; _pending = 0; }
                        else _guestSequence = System.Math.Max(_guestSequence, entry.HighWater);
                    }
                _guestAdmission = admission;
            }
            catch (System.Exception e)
            {
                for (int i = 0; i < 5; i++) {
                    try { _stains[i]!.transform.localScale = before[i]; } catch { }
                }
                WinterMPPlugin.Log.LogDebug("PissAreaSync: apply failed: " + e.Message);
            }
            finally { _executing = false; }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            byte[] values = { state.Scale1, state.Scale2, state.Scale3, state.Scale4, state.Scale5 };
            bool changed = !_hasLast;
            for (int i = 0; i < 5 && !changed; i++) changed = _lastSent[i] != values[i];
            if (!changed && !keepAlive) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            RememberSent(state);
        }

        private PissAreaState? BuildState()
        {
            var session = SessionManager.Instance;
            if (!Ready || !Active(session) || !session!.IsHost || _revision == uint.MaxValue) return null;
            for (int i = 0; i < 5; i++) if (!Finite(_stains[i]!.transform.localScale.x)) return null;
            if (_epoch == 0) { _epoch = (uint)NewToken(); if (_epoch == 0) _epoch = 1; }
            RefreshAdmissions(session);
            byte Q(int i) => (byte)Mathf.Clamp(_stains[i]!.transform.localScale.x * ScaleQuant, 0f, 255f);
            uint revision = ++_revision;
            _challenges[revision] = Time.unscaledTime;
            var expired = new System.Collections.Generic.List<uint>();
            foreach (var pair in _challenges) if (Time.unscaledTime - pair.Value > 1f) expired.Add(pair.Key);
            foreach (uint key in expired) _challenges.Remove(key);
            return new PissAreaState
            {
                Epoch = _epoch, Revision = revision, Admissions = CaptureAdmissions(),
                Sequence = ++_outSequence,
                Scale1 = Q(0), Scale2 = Q(1), Scale3 = Q(2), Scale4 = Q(3), Scale5 = Q(4),
            };
        }

        private void Locate()
        {
            if (Ready) return;
            GameObject? go;
            try { go = GameObject.Find(PissPath); }
            catch { return; }
            if (go == null) return;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Logic") continue;
                _logic = fsm;
                for (int i = 0; i < 5; i++) {
                    _scales[i] = fsm.FsmVariables.FindFsmFloat("Scale" + (i + 1));
                    _stains[i] = fsm.FsmVariables.FindFsmGameObject(StainVariables[i])?.Value;
                }
                break;
            }
            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("PissAreaSync: located yard piss areas.");
            }
        }
    }
}
