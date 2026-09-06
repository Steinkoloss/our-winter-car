using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Retain native crossing edges until their exact host acknowledgment, in checkpoint order.</summary>
    public sealed class RallyProgressReplica
    {
        public const float RetrySeconds = .25f, TimeoutSeconds = 10f;
        private sealed class Observation { public bool Started; public byte Mask; }
        private sealed class Pending
        {
            public RallyIntent Report = null!;
            public float ObservedAt;
            public bool Sent;
        }
        private readonly Dictionary<byte, Observation> _observed = new Dictionary<byte, Observation>();
        private readonly Dictionary<byte, RallyState> _states = new Dictionary<byte, RallyState>();
        private readonly List<Pending> _pending = new List<Pending>();
        private ulong _token;
        private ushort _sequence;
        private float _retryAt;
        public bool Failed { get; private set; }
        public int PendingCount => _pending.Count;

        public RallyProgressReplica(ulong reportToken) { Clear(reportToken); }

        public void Observe(byte player, byte stage, bool started, byte checkpointMask, float now)
        {
            if (player == byte.MaxValue || stage < 1 || stage > 3 || checkpointMask > 63 || !RallyProgressLedger.Clock(now)) return;
            if (!_observed.TryGetValue(stage, out var previous))
            {
                // Existing save/reconnect flags are a baseline, never a fresh race start.
                _observed.Add(stage, new Observation { Started = started, Mask = checkpointMask });
                return;
            }
            if (started && !previous.Started)
            {
                _pending.Clear(); Failed = false; _retryAt = 0;
                Enqueue(player, stage, 0, now);
            }
            if (!Failed)
                for (byte checkpoint = 1; checkpoint <= 6; checkpoint++)
                {
                    int bit = 1 << (checkpoint - 1);
                    if ((checkpointMask & bit) != 0 && (previous.Mask & bit) == 0) Enqueue(player, stage, checkpoint, now);
                }
            previous.Started = started; previous.Mask = checkpointMask;
        }

        private void Enqueue(byte player, byte stage, byte checkpoint, float now)
        {
            foreach (var pending in _pending)
                if (pending.Report.Stage == stage && pending.Report.Checkpoint == checkpoint) return;
            // Three stages, at most one start and six crossings each.
            if (_pending.Count >= 21) { Failed = true; _pending.Clear(); return; }
            _pending.Add(new Pending { ObservedAt = now, Report = new RallyIntent { PlayerId = player,
                Stage = stage, Checkpoint = checkpoint, Sequence = ++_sequence, ReportToken = _token } });
        }

        public RallyIntent? Take(float now)
        {
            if (!RallyProgressLedger.Clock(now) || _pending.Count == 0 || Failed) return null;
            var head = _pending[0];
            if (now - head.ObservedAt >= TimeoutSeconds) { Failed = true; _pending.Clear(); return null; }
            if (now < _retryAt) return null;
            _retryAt = now + RetrySeconds; head.Sent = true;
            return RallyProgressLedger.Copy(head.Report);
        }

        public bool Receive(RallyState state)
        {
            if (!Valid(state)) return false;
            if (_states.TryGetValue(state.PlayerId, out var previous))
            {
                uint delta = unchecked(state.Revision - previous.Revision);
                if (delta == 0 || delta > int.MaxValue) return false;
            }
            _states[state.PlayerId] = Copy(state);
            if (_pending.Count > 0 && (state.Flags & RallyState.HasReport) != 0)
            {
                var head = _pending[0]; var report = head.Report;
                if (head.Sent && state.PlayerId == report.PlayerId && state.Stage == report.Stage
                    && state.Checkpoint == report.Checkpoint && state.ReportToken == report.ReportToken
                    && state.ReportSequence == report.Sequence)
                { _pending.RemoveAt(0); _retryAt = 0; }
            }
            return true;
        }

        public RallyState? Latest(byte player) => _states.TryGetValue(player, out var state) ? Copy(state) : null;
        public static bool Valid(RallyState state) => state.PlayerId != byte.MaxValue && state.Stage >= 1 && state.Stage <= 3
            && (state.Phase == RallyState.PhaseRacing || state.Phase == RallyState.PhaseFinished)
            && state.Checkpoint <= 6 && (state.Phase != RallyState.PhaseFinished || state.Checkpoint > 0)
            && state.Flags <= RallyState.HasReport
            && (state.Flags == RallyState.HasReport ? state.ReportToken != 0 : state.ReportToken == 0 && state.ReportSequence == 0);
        private static RallyState Copy(RallyState s) => new RallyState { PlayerId = s.PlayerId, Stage = s.Stage,
            Phase = s.Phase, Checkpoint = s.Checkpoint, Sequence = s.Sequence, ElapsedCentiseconds = s.ElapsedCentiseconds,
            Revision = s.Revision, ReportToken = s.ReportToken, ReportSequence = s.ReportSequence, Flags = s.Flags };
        public void Clear(ulong reportToken)
        {
            if (reportToken == 0) throw new ArgumentOutOfRangeException(nameof(reportToken));
            ResetView(); _token = reportToken; _sequence = 0;
        }
        public void ResetView()
        {
            // A scene rebind inside the same connection must not reuse report ids
            // or change the token already admitted by the host.
            _observed.Clear(); _states.Clear(); _pending.Clear(); _retryAt = 0; Failed = false;
        }
    }
}
