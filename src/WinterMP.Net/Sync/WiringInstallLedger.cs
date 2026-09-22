using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>One immutable in-flight operation per guest; retries never execute it again.</summary>
    public sealed class WiringInstallLedger
    {
        private sealed class Entry
        {
            public WiringInstallRequest Request = null!;
            public WiringInstallReceipt Receipt = null!;
        }
        private readonly Dictionary<byte, Entry> _entries = new Dictionary<byte, Entry>();

        public WiringInstallReceipt? Inspect(WiringInstallRequest request, byte actor, out bool canBegin)
        {
            canBegin = false;
            if (!Valid(request, actor)) return null;
            if (!_entries.TryGetValue(actor, out var entry)) { canBegin = true; return null; }
            var previous = entry.Request;
            if (request.Token != previous.Token) return null;
            uint delta = unchecked(request.Sequence - previous.Sequence);
            if (delta == 0) return SameRequest(request, previous) ? Copy(entry.Receipt) : null;
            if (delta > int.MaxValue) return null;
            if (entry.Receipt.Status == WiringInstallStatus.Pending) return Receipt(request, WiringInstallStatus.Busy);
            canBegin = true; return null;
        }

        public WiringInstallReceipt? Begin(WiringInstallRequest request, byte actor, WiringInstallStatus status)
        {
            var replay = Inspect(request, actor, out bool canBegin);
            if (!canBegin) return replay;
            if (status == WiringInstallStatus.Accepted || (byte)status > (byte)WiringInstallStatus.Failed) return null;
            var receipt = Receipt(request, status);
            // An unavailable endpoint is transient; retries must revalidate its
            // native prerequisites without forgetting a completed installation.
            if (status != WiringInstallStatus.Busy)
                _entries[actor] = new Entry { Request = Copy(request), Receipt = Copy(receipt) };
            return receipt;
        }

        public WiringInstallReceipt? Complete(WiringInstallRequest request, bool accepted)
        {
            if (!_entries.TryGetValue(request.PlayerId, out var entry) || !SameRequest(request, entry.Request)) return null;
            if (entry.Receipt.Status != WiringInstallStatus.Pending) return Copy(entry.Receipt);
            entry.Receipt.Status = accepted ? WiringInstallStatus.Accepted : WiringInstallStatus.Failed;
            return Copy(entry.Receipt);
        }
        public void ForgetPlayer(byte playerId) => _entries.Remove(playerId);
        public void Clear() => _entries.Clear();

        public static WiringInstallStatus Check(WiringInstallRequest request, WiringState? state,
            bool available, bool nearby, bool ready)
        {
            if (!available || state == null || request.SourceId != 5 || state.SourceId != request.SourceId
                || !WiringPolicy.Valid(state) || (state.Flags & WiringState.Available) == 0)
                return WiringInstallStatus.Unavailable;
            if (request.ExpectedRevision != state.Revision) return WiringInstallStatus.Stale;
            if ((state.Flags & WiringState.Installed) != 0) return WiringInstallStatus.Installed;
            if (!nearby) return WiringInstallStatus.TooFar;
            return ready && (state.Flags & WiringState.Connectable) != 0 ? WiringInstallStatus.Pending : WiringInstallStatus.Busy;
        }
        public static bool Terminal(WiringInstallStatus status) => status != WiringInstallStatus.Pending && status != WiringInstallStatus.Busy;
        private static bool Valid(WiringInstallRequest r, byte actor) => r != null && actor > 0 && actor < byte.MaxValue
            && r.PlayerId == actor && r.Token != 0 && r.SourceId == 5;
        private static bool SameRequest(WiringInstallRequest a, WiringInstallRequest b) => a.PlayerId == b.PlayerId
            && a.Token == b.Token && a.Sequence == b.Sequence && a.SourceId == b.SourceId && a.ExpectedRevision == b.ExpectedRevision;
        public static WiringInstallRequest Copy(WiringInstallRequest r) => new WiringInstallRequest {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, SourceId = r.SourceId, ExpectedRevision = r.ExpectedRevision };
        public static WiringInstallReceipt Receipt(WiringInstallRequest r, WiringInstallStatus status) => new WiringInstallReceipt {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, SourceId = r.SourceId, Status = status };
        public static WiringInstallReceipt Copy(WiringInstallReceipt r) => new WiringInstallReceipt {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, SourceId = r.SourceId, Status = r.Status };
    }

    public sealed class WiringInstallClient
    {
        private readonly ulong _token;
        private uint _sequence;
        private WiringInstallRequest? _pending;
        private float _nextSend;
        public WiringInstallClient(ulong token)
        {
            if (token == 0) throw new ArgumentException("Opening token must be nonzero.");
            _token = token;
        }
        public bool TryBegin(byte playerId, uint sourceId, uint revision)
        {
            if (_pending != null || playerId == 0 || playerId == byte.MaxValue || sourceId != 5) return false;
            _pending = new WiringInstallRequest { PlayerId = playerId, Token = _token, Sequence = unchecked(++_sequence),
                SourceId = sourceId, ExpectedRevision = revision };
            _nextSend = 0; return true;
        }
        public WiringInstallRequest? Poll(float now)
        {
            if (_pending == null || float.IsNaN(now) || float.IsInfinity(now) || now < _nextSend) return null;
            _nextSend = now + .5f; return WiringInstallLedger.Copy(_pending);
        }
        public bool Receive(WiringInstallReceipt receipt)
        {
            if (_pending == null || receipt.Token != _pending.Token || receipt.PlayerId != _pending.PlayerId
                || receipt.Sequence != _pending.Sequence || receipt.SourceId != _pending.SourceId
                || (byte)receipt.Status > (byte)WiringInstallStatus.Failed || !WiringInstallLedger.Terminal(receipt.Status)) return false;
            _pending = null; return true;
        }
    }
}
