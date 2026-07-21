using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared Kela welfare / unemployment claim as host-owned world state
    /// (COVERAGE-ROADMAP 3.6). The claim + recurring benefit run per-client off the shared
    /// clock, so peers disagree on the claim and the benefit income diverges. The <b>host</b>
    /// owns <c>Systems/Expenses :: Kela</c> and broadcasts the claim state on change + join;
    /// guests apply it. The weekly benefit credits the shared wallet via the host's own
    /// expenses logic. A host-scalar-state broadcaster like <see cref="TaxiJobSync"/>.
    /// </summary>
    internal sealed class WelfareSync
    {
        private const string ExpensesPath = "Systems/Expenses";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 3f;
        private const float KeepAliveSeconds = 30f;

        private PlayMakerFSM? _kela;
        private FsmInt? _unemployDays;
        private FsmFloat? _paidAmount;
        private FsmFloat? _weekly;
        private FsmBool? _continue;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastDays;
        private byte _lastFlags;

        private bool Ready => _kela != null && _unemployDays != null;

        public void Clear()
        {
            _kela = null;
            _unemployDays = null; _paidAmount = _weekly = null; _continue = null;
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

        public WelfareState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(WelfareState message)
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
                if (_unemployDays != null) _unemployDays.Value = message.UnemployDays;
                if (_paidAmount != null) _paidAmount.Value = message.PaidAmount;
                if (_weekly != null) _weekly.Value = message.Weekly;
                if (_continue != null) _continue.Value = message.Claiming;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("WelfareSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            Locate();
            if (!Ready) return;
            var state = BuildState();
            if (state == null) return;

            bool changed = !_hasLast || _lastDays != state.UnemployDays || _lastFlags != state.Flags;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastDays = state.UnemployDays;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private WelfareState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_continue != null && _continue.Value) flags |= WelfareState.FlagClaiming;
            return new WelfareState
            {
                Sequence = ++_outSequence,
                UnemployDays = _unemployDays!.Value,
                PaidAmount = _paidAmount != null ? _paidAmount.Value : 0f,
                Weekly = _weekly != null ? _weekly.Value : 0f,
                Flags = flags,
            };
        }

        private void Locate()
        {
            if (Ready) return;
            GameObject? go;
            try { go = GameObject.Find(ExpensesPath); }
            catch { return; }
            if (go == null) return;

            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "Kela") continue;
                _kela = fsm;
                var v = fsm.FsmVariables;
                _unemployDays = v.FindFsmInt("UnemployDays");
                _paidAmount = v.FindFsmFloat("PaidAmount");
                _weekly = v.FindFsmFloat("Weekly");
                _continue = v.FindFsmBool("Continue");
                break;
            }

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("WelfareSync: located Kela expenses.");
            }
        }
    }
}
