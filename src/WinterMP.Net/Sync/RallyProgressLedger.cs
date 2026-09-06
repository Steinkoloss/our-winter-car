using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Ordered host-clocked crossings. Admission resets report identity, never an existing race.</summary>
    public sealed class RallyProgressLedger
    {
        private sealed class Record
        {
            public byte Stage, Checkpoint, Count, Phase;
            public float StartedAt, FinishedAt;
            public uint Revision;
            public bool FinishPublished;
            public RallyIntent? Accepted;
        }
        private readonly Dictionary<byte, Record> _records = new Dictionary<byte, Record>();
        private ushort _sequence;

        public bool TryAccept(RallyIntent report, byte checkpointCount, float now, bool crossingVerified, out RallyState state)
        {
            state = new RallyState();
            if (!ValidReport(report) || checkpointCount < 1 || checkpointCount > 6
                || report.Checkpoint > checkpointCount || !Clock(now)) return false;
            _records.TryGetValue(report.PlayerId, out var record);
            var accepted = record?.Accepted;
            if (accepted != null)
            {
                if (report.ReportToken != accepted.ReportToken) return false;
                ushort delta = unchecked((ushort)(report.Sequence - accepted.Sequence));
                if (delta == 0)
                {
                    if (report.Stage != accepted.Stage || report.Checkpoint != accepted.Checkpoint) return false;
                    state = Snapshot(report.PlayerId, record!, now);
                    return true;
                }
                if (delta > short.MaxValue) return false;
            }
            if (!crossingVerified || !Advance(report.PlayerId, report.Stage, report.Checkpoint, checkpointCount, now)) return false;
            record = _records[report.PlayerId];
            record.Accepted = Copy(report);
            state = Snapshot(report.PlayerId, record, now);
            // The accepted finish is returned for broadcast. Snapshot enumeration never
            // consumes a host-local finish that still needs to reach connected guests.
            if (record.Phase == RallyState.PhaseFinished) record.FinishPublished = true;
            return true;
        }

        public bool AdvanceLocal(byte player, byte stage, byte checkpoint, byte count, float now)
        {
            if (player == byte.MaxValue || stage < 1 || stage > 3 || count < 1 || count > 6
                || checkpoint > count || !Clock(now)) return false;
            return Advance(player, stage, checkpoint, count, now);
        }

        private bool Advance(byte player, byte stage, byte checkpoint, byte count, float now)
        {
            _records.TryGetValue(player, out var record);
            if (checkpoint == 0)
            {
                // Preserve this player's revision across stage changes and repeated runs.
                _records[player] = new Record { Stage = stage, Count = count, Phase = RallyState.PhaseRacing,
                    StartedAt = now, Revision = record == null ? 0 : record.Revision };
                return true;
            }
            if (record == null || record.Stage != stage || record.Count != count || record.Phase != RallyState.PhaseRacing
                || checkpoint != record.Checkpoint + 1 || now < record.StartedAt) return false;
            record.Checkpoint = checkpoint;
            if (checkpoint == count) { record.Phase = RallyState.PhaseFinished; record.FinishedAt = now; }
            return true;
        }

        public IEnumerable<RallyState> Snapshots(float now, bool onlyDue)
        {
            if (!Clock(now)) yield break;
            foreach (var pair in _records)
            {
                var record = pair.Value;
                if (onlyDue && record.Phase == RallyState.PhaseFinished && record.FinishPublished) continue;
                var state = Snapshot(pair.Key, record, now);
                if (onlyDue && record.Phase == RallyState.PhaseFinished) record.FinishPublished = true;
                yield return state;
            }
        }

        private RallyState Snapshot(byte player, Record record, float now)
        {
            double end = record.Phase == RallyState.PhaseFinished ? record.FinishedAt : now;
            double elapsed = Math.Max(0, Math.Min(uint.MaxValue, Math.Round((end - record.StartedAt) * 100)));
            return new RallyState { PlayerId = player, Stage = record.Stage, Checkpoint = record.Checkpoint,
                Phase = record.Phase, Sequence = ++_sequence, Revision = ++record.Revision,
                ElapsedCentiseconds = (uint)elapsed, Flags = record.Accepted == null ? (byte)0 : RallyState.HasReport,
                ReportToken = record.Accepted == null ? 0 : record.Accepted.ReportToken,
                ReportSequence = record.Accepted == null ? (ushort)0 : record.Accepted.Sequence };
        }

        public void ForgetPlayer(byte player)
        {
            if (_records.TryGetValue(player, out var record)) record.Accepted = null;
        }
        public void Clear() { _records.Clear(); _sequence = 0; }
        internal static bool Clock(float now) => BankTransferPolicy.IsFinite(now) && now >= 0;
        internal static bool ValidReport(RallyIntent report) => report.PlayerId != byte.MaxValue && report.Stage >= 1
            && report.Stage <= 3 && report.Checkpoint <= 6 && report.ReportToken != 0;
        internal static RallyIntent Copy(RallyIntent r) => new RallyIntent { PlayerId = r.PlayerId, Stage = r.Stage,
            Checkpoint = r.Checkpoint, Sequence = r.Sequence, ReportToken = r.ReportToken };
    }

    /// <summary>Only the host feeds marker proximity from authenticated, driver-owned observed poses.</summary>
    public sealed class RallyCrossingEvidence
    {
        public const float LifetimeSeconds = 3f;
        private readonly Dictionary<uint, float> _seen = new Dictionary<uint, float>();
        public void Observe(byte player, byte stage, byte checkpoint, float poseTime, float now)
        {
            if (player == byte.MaxValue || stage < 1 || stage > 3 || checkpoint > 6
                || !Recent(poseTime, now)) return;
            uint key = Key(player, stage, checkpoint);
            if (!_seen.TryGetValue(key, out float old) || poseTime > old) _seen[key] = poseTime;
        }
        public bool Contains(byte player, byte stage, byte checkpoint, float now) =>
            _seen.TryGetValue(Key(player, stage, checkpoint), out float at) && Recent(at, now);
        private static bool Recent(float at, float now) => RallyProgressLedger.Clock(now) && RallyProgressLedger.Clock(at)
            && now >= at && now - at <= LifetimeSeconds;
        private static uint Key(byte player, byte stage, byte checkpoint) => (uint)(player << 16 | stage << 8 | checkpoint);
        public void ForgetPlayer(byte player)
        {
            for (byte stage = 1; stage <= 3; stage++)
                for (byte checkpoint = 0; checkpoint <= 6; checkpoint++) _seen.Remove(Key(player, stage, checkpoint));
        }
        public void Clear() => _seen.Clear();
    }
}
