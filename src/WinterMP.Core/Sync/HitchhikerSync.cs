using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared hitchhiker variant/stage/payout as host-owned world state
    /// (COVERAGE-ROADMAP 3.4). The variant (ordinary vs. KiljuMurderer/suicide) and the
    /// drunk/story stage branch per-client, so peers meet a different hiker. The <b>host</b>
    /// owns it: it reads the <c>Hitchhiker</c> Logic/Save/Timer FSMs and broadcasts the stage
    /// + variant + paid flag on change + join; guests apply. The body pose streams over
    /// <see cref="NpcTransform"/> via the ScriptedMover path (registered in NpcTrafficSync).
    /// </summary>
    internal sealed class HitchhikerSync
    {
        private const string HikerPath = "JOBS/KILJUGUY/HikerPivot/Hitchhiker";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;

        private PlayMakerFSM? _logic;
        private PlayMakerFSM? _save;
        private PlayMakerFSM? _timer;
        private FsmInt? _drunkStage;
        private FsmInt? _movingStage;
        private FsmInt? _money;
        private FsmBool? _paid;
        private FsmBool? _activate;
        private FsmBool? _angryKilju;
        private FsmBool? _suicide;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastStage;
        private byte _lastFlags;

        private bool Ready => _logic != null && _drunkStage != null;

        public void Clear()
        {
            _logic = _save = _timer = null;
            _drunkStage = _movingStage = _money = null;
            _paid = _activate = _angryKilju = _suicide = null;
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
            _hasLast = false;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        public HitchhikerState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(HitchhikerState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            try
            {
                if (_drunkStage != null) _drunkStage.Value = message.DrunkStage;
                if (_movingStage != null) _movingStage.Value = message.MovingStage;
                if (_money != null) _money.Value = message.Money;
                if (_paid != null) _paid.Value = (message.Flags & HitchhikerState.FlagPaid) != 0;
                if (_activate != null) _activate.Value = (message.Flags & HitchhikerState.FlagActive) != 0;
                if (_angryKilju != null) _angryKilju.Value = (message.Flags & HitchhikerState.FlagAngryKilju) != 0;
                if (_suicide != null) _suicide.Value = (message.Flags & HitchhikerState.FlagSuicide) != 0;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("HitchhikerSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            Locate();
            if (!Ready) return;
            var state = BuildState();
            if (state == null) return;

            bool changed = !_hasLast || _lastStage != state.DrunkStage || _lastFlags != state.Flags;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastStage = state.DrunkStage;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private HitchhikerState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_paid != null && _paid.Value) flags |= HitchhikerState.FlagPaid;
            if (_activate != null && _activate.Value) flags |= HitchhikerState.FlagActive;
            if (_angryKilju != null && _angryKilju.Value) flags |= HitchhikerState.FlagAngryKilju;
            if (_suicide != null && _suicide.Value) flags |= HitchhikerState.FlagSuicide;
            return new HitchhikerState
            {
                Sequence = ++_outSequence,
                DrunkStage = _drunkStage!.Value,
                MovingStage = _movingStage != null ? _movingStage.Value : 0,
                Money = _money != null ? _money.Value : 0,
                Flags = flags,
            };
        }

        private void Locate()
        {
            if (Ready && _save != null && _timer != null) return;

            GameObject? go;
            try { go = GameObject.Find(HikerPath); }
            catch { return; }
            if (go == null) return;

            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null) continue;
                if (_logic == null && fsm.FsmName == "Logic") _logic = fsm;
                if (_save == null && fsm.FsmName == "Save") _save = fsm;
                if (_timer == null && fsm.FsmName == "Timer") _timer = fsm;
            }

            if (_logic != null)
            {
                if (_drunkStage == null) _drunkStage = _logic.FsmVariables.FindFsmInt("DrunkStage");
                if (_paid == null) _paid = _logic.FsmVariables.FindFsmBool("Paid");
                if (_activate == null) _activate = _logic.FsmVariables.FindFsmBool("Activate");
            }
            if (_save != null)
            {
                if (_movingStage == null) _movingStage = _save.FsmVariables.FindFsmInt("MovingStage");
                if (_angryKilju == null) _angryKilju = _save.FsmVariables.FindFsmBool("AngryKilju");
                if (_suicide == null) _suicide = _save.FsmVariables.FindFsmBool("Suicide");
            }
            if (_timer != null && _money == null)
                _money = _timer.FsmVariables.FindFsmInt("Money");

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("HitchhikerSync: located hitchhiker.");
            }
        }
    }
}
