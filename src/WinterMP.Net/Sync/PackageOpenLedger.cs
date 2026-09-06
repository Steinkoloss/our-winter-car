using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>One immutable in-flight operation per guest; retries never execute it again.</summary>
    public sealed class PackageOpenLedger
    {
        private sealed class Entry
        {
            public PackageOpenRequest Request = null!;
            public PackageOpenReceipt Receipt = null!;
        }
        private readonly Dictionary<byte, Entry> _entries = new Dictionary<byte, Entry>();

        public PackageOpenReceipt? Inspect(PackageOpenRequest request, byte actor, out bool canBegin)
        {
            canBegin = false;
            if (!Valid(request, actor)) return null;
            if (!_entries.TryGetValue(actor, out var entry)) { canBegin = true; return null; }
            var previous = entry.Request;
            if (request.Token != previous.Token) return null;
            uint delta = unchecked(request.Sequence - previous.Sequence);
            if (delta == 0) return SameRequest(request, previous) ? Copy(entry.Receipt) : null;
            if (delta > int.MaxValue) return null;
            if (entry.Receipt.Status == PackageOpenStatus.Pending) return Receipt(request, PackageOpenStatus.Busy);
            canBegin = true; return null;
        }

        public PackageOpenReceipt? Begin(PackageOpenRequest request, byte actor, PackageOpenStatus status)
        {
            var replay = Inspect(request, actor, out bool canBegin);
            if (!canBegin) return replay;
            if (status == PackageOpenStatus.Accepted || (byte)status > (byte)PackageOpenStatus.Failed) return null;
            var receipt = Receipt(request, status);
            // A busy native factory is transient. Retrying the same request must
            // revalidate it, rather than remember Busy forever or consume a part.
            if (status != PackageOpenStatus.Busy)
                _entries[actor] = new Entry { Request = Copy(request), Receipt = Copy(receipt) };
            return receipt;
        }

        public PackageOpenReceipt? Complete(PackageOpenRequest request, bool accepted, uint producedItemId = 0)
        {
            if (!_entries.TryGetValue(request.PlayerId, out var entry) || !SameRequest(request, entry.Request)) return null;
            if (entry.Receipt.Status != PackageOpenStatus.Pending) return Copy(entry.Receipt);
            entry.Receipt.Status = accepted ? PackageOpenStatus.Accepted : PackageOpenStatus.Failed;
            entry.Receipt.ProducedItemId = accepted ? producedItemId : 0;
            return Copy(entry.Receipt);
        }
        public void ForgetPlayer(byte playerId) => _entries.Remove(playerId);
        public void Clear() => _entries.Clear();

        public static PackageOpenStatus Check(PackageOpenRequest request, PackageState? state,
            bool available, bool nearby, bool ready)
        {
            if (!available || state == null || FactoryItemIdentity.ItemId(state.FactoryId, state.NativeId) != request.ItemId)
                return PackageOpenStatus.Unavailable;
            if (request.ExpectedRevision != state.Revision) return PackageOpenStatus.Stale;
            if (state.Quantity == 0) return PackageOpenStatus.Empty;
            if (!nearby) return PackageOpenStatus.TooFar;
            return ready ? PackageOpenStatus.Pending : PackageOpenStatus.Busy;
        }
        public static bool Terminal(PackageOpenStatus status) => status != PackageOpenStatus.Pending && status != PackageOpenStatus.Busy;
        private static bool Valid(PackageOpenRequest r, byte actor) => r != null && actor > 0 && actor < byte.MaxValue
            && r.PlayerId == actor && r.Token != 0;
        private static bool SameRequest(PackageOpenRequest a, PackageOpenRequest b) => a.PlayerId == b.PlayerId
            && a.Token == b.Token && a.Sequence == b.Sequence && a.ItemId == b.ItemId && a.ExpectedRevision == b.ExpectedRevision;
        public static PackageOpenRequest Copy(PackageOpenRequest r) => new PackageOpenRequest {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, ItemId = r.ItemId, ExpectedRevision = r.ExpectedRevision };
        public static PackageOpenReceipt Receipt(PackageOpenRequest r, PackageOpenStatus status) => new PackageOpenReceipt {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, ItemId = r.ItemId, Status = status };
        public static PackageOpenReceipt Copy(PackageOpenReceipt r) => new PackageOpenReceipt {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, ItemId = r.ItemId, Status = r.Status, ProducedItemId = r.ProducedItemId };
    }

    public sealed class PackageOpenClient
    {
        private readonly ulong _token;
        private uint _sequence;
        private PackageOpenRequest? _pending;
        private float _nextSend;
        public PackageOpenClient(ulong token)
        {
            if (token == 0) throw new ArgumentException("Opening token must be nonzero.");
            _token = token;
        }
        public bool TryBegin(byte playerId, uint itemId, uint revision)
        {
            if (_pending != null || playerId == 0 || playerId == byte.MaxValue) return false;
            _pending = new PackageOpenRequest { PlayerId = playerId, Token = _token, Sequence = unchecked(++_sequence),
                ItemId = itemId, ExpectedRevision = revision };
            _nextSend = 0; return true;
        }
        public PackageOpenRequest? Poll(float now)
        {
            if (_pending == null || float.IsNaN(now) || float.IsInfinity(now) || now < _nextSend) return null;
            _nextSend = now + .5f; return PackageOpenLedger.Copy(_pending);
        }
        public bool Receive(PackageOpenReceipt receipt)
        {
            if (_pending == null || receipt.Token != _pending.Token || receipt.PlayerId != _pending.PlayerId
                || receipt.Sequence != _pending.Sequence || receipt.ItemId != _pending.ItemId
                || (byte)receipt.Status > (byte)PackageOpenStatus.Failed || !PackageOpenLedger.Terminal(receipt.Status)) return false;
            _pending = null; return true;
        }
    }
}
