using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// The whole <c>Systems/Expenses</c> record as host-owned world state
    /// (COVERAGE-ROADMAP 3.6 Kela + R1.2 rent/eviction/housing benefit). The Kela claim,
    /// the weekly rent debit and the KICKOUT eviction each run per-client off the shared
    /// clock, so peers disagree on the claim, the rent debt and — worst — whether the
    /// eviction (furniture destruction + player relocation) happened. The <b>host</b> owns
    /// all three FSMs: it broadcasts the scalars on change + join, guests apply them and
    /// replay the terminal <c>Kick out</c> state once. Guests keep their own
    /// Rent/Livingsupport FSMs suppressed for the session — a guest plays in the HOST's
    /// world, so its local weekly ticks only produce throwaway divergence (Kela is left
    /// running: its vars are host-stomped anyway and its claim flow is mail-driven).
    /// A host-scalar-state broadcaster like <see cref="TaxiJobSync"/>.
    /// </summary>
    internal sealed partial class WelfareSync
    {
        private const string ExpensesPath = "Systems/Expenses";
        private const string EvictedStateName = "Kick out";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 3f;
        private const float KeepAliveSeconds = 30f;

        private PlayMakerFSM? _kela;
        private FsmInt? _unemployDays;
        private FsmFloat? _paidAmount;
        private FsmFloat? _weekly;
        private FsmBool? _continue;
        private PlayMakerFSM? _rent;
        private FsmFloat? _rentDebt;
        private FsmFloat? _rentPerWeek;
        private PlayMakerFSM? _living;
        private FsmFloat? _asumistukiPerWeek;
        private bool _loggedFound;

        private readonly FsmSuppressor _rentSuppressor = new FsmSuppressor();
        private readonly FsmSuppressor _livingSuppressor = new FsmSuppressor();

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastDays;
        private byte _lastFlags;
        private int _lastRentDebt;

        private bool Ready => _kela != null && _unemployDays != null;

        public void Clear()
        {
            ClearDebtLetter();
            _rentSuppressor.Restore();
            _livingSuppressor.Restore();
            _kela = null;
            _unemployDays = null; _paidAmount = _weekly = null; _continue = null;
            _rent = null; _rentDebt = _rentPerWeek = null;
            _living = null; _asumistukiPerWeek = null;
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
            _hasLast = false;
            _lastRentDebt = 0;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0)
            {
                _rentSuppressor.Restore();
                _livingSuppressor.Restore();
                return;
            }
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }
            UpdateDebtLetter(session);

            if (!session.IsHost)
            {
                // Freeze the guest's own weekly rent/benefit ticks in place. Skipped once
                // evicted: Kick out is terminal, and the suppressor was restored to replay it.
                if (_rent != null && !_rentSuppressor.Active && !IsRentEvicted())
                    _rentSuppressor.Suppress(_rent);
                if (_living != null && !_livingSuppressor.Active)
                    _livingSuppressor.Suppress(_living);
                return;
            }

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
                // FSM variables stay writable while the component is suppressed.
                if (_rentDebt != null) _rentDebt.Value = Mathf.Max(0f, message.RentDebt);
                if (_rentPerWeek != null && message.RentPerWeek > 0f) _rentPerWeek.Value = message.RentPerWeek;
                if (_asumistukiPerWeek != null && message.AsumistukiPerWeek > 0f)
                    _asumistukiPerWeek.Value = message.AsumistukiPerWeek;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("WelfareSync: apply failed: " + e.Message);
            }

            if (message.Evicted) ReplayEviction();
        }

        /// <summary>
        /// Host got evicted — run the same furniture-destroying terminal state here, once.
        /// The suppressor must be lifted first: a disabled FSM cannot process the entry.
        /// </summary>
        private void ReplayEviction()
        {
            if (_rent == null || IsRentEvicted()) return;

            _rentSuppressor.Restore();
            if (!FsmHook.EnsureRemoteEntry(_rent, EvictedStateName))
            {
                WinterMPPlugin.Log.LogWarning("WelfareSync: cannot replay eviction (no Kick out entry).");
                return;
            }

            var world = WorldSyncManager.Instance;
            if (world != null) world.ApplyingRemote = true;
            try { FsmHook.FireRemoteEntry(_rent, EvictedStateName); }
            finally { if (world != null) world.ApplyingRemote = false; }
            WinterMPPlugin.Log.LogInfo("WelfareSync: replayed host eviction.");
        }

        private bool IsRentEvicted()
        {
            try { return _rent != null && _rent.Fsm != null && _rent.Fsm.ActiveStateName == EvictedStateName; }
            catch { return false; }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            Locate();
            if (!Ready) return;
            var state = BuildState();
            if (state == null) return;

            bool changed = !_hasLast || _lastDays != state.UnemployDays || _lastFlags != state.Flags
                || _lastRentDebt != Mathf.RoundToInt(state.RentDebt);
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastDays = state.UnemployDays;
            _lastFlags = state.Flags;
            _lastRentDebt = Mathf.RoundToInt(state.RentDebt);
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private WelfareState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_continue != null && _continue.Value) flags |= WelfareState.FlagClaiming;
            if (IsRentEvicted()) flags |= WelfareState.FlagEvicted;
            return new WelfareState
            {
                Sequence = ++_outSequence,
                UnemployDays = _unemployDays!.Value,
                PaidAmount = _paidAmount != null ? _paidAmount.Value : 0f,
                Weekly = _weekly != null ? _weekly.Value : 0f,
                Flags = flags,
                RentDebt = _rentDebt != null ? _rentDebt.Value : 0f,
                RentPerWeek = _rentPerWeek != null ? _rentPerWeek.Value : 0f,
                AsumistukiPerWeek = _asumistukiPerWeek != null ? _asumistukiPerWeek.Value : 0f,
            };
        }

        private void Locate()
        {
            if (Ready && _rent != null && _living != null) return;
            GameObject? go;
            try { go = GameObject.Find(ExpensesPath); }
            catch { return; }
            if (go == null) return;

            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null) continue;
                var v = fsm.FsmVariables;
                if (_kela == null && fsm.FsmName == "Kela")
                {
                    _kela = fsm;
                    _unemployDays = v.FindFsmInt("UnemployDays");
                    _paidAmount = v.FindFsmFloat("PaidAmount");
                    _weekly = v.FindFsmFloat("Weekly");
                    _continue = v.FindFsmBool("Continue");
                }
                else if (_rent == null && fsm.FsmName == "Rent")
                {
                    _rent = fsm;
                    _rentDebt = v.FindFsmFloat("Debt");
                    _rentPerWeek = v.FindFsmFloat("RentPerWeek");
                }
                else if (_living == null && fsm.FsmName == "Livingsupport")
                {
                    _living = fsm;
                    _asumistukiPerWeek = v.FindFsmFloat("AsumistukiPerWeek");
                }
            }

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("WelfareSync: located Kela/Rent/Livingsupport expenses.");
            }
        }
    }
}
