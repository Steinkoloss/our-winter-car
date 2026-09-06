using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Host and guest openings share one reservation and one consumption revision per bag.</summary>
    public sealed class BagOpenLedger
    {
        private sealed class Entry
        {
            public BagOpenRequest Request = null!;
            public BagOpenReceipt Receipt = null!;
        }
        private readonly Dictionary<byte, Entry> _entries = new Dictionary<byte, Entry>();
        private readonly Dictionary<uint, Entry> _pending = new Dictionary<uint, Entry>();
        private readonly Dictionary<uint, uint> _consumedRevisions = new Dictionary<uint, uint>();

        public BagOpenReceipt? Inspect(BagOpenRequest request, byte actor, out bool canBegin)
        {
            canBegin = false;
            if (!Valid(request, actor)) return null;
            if (!_entries.TryGetValue(actor, out var entry)) { canBegin = true; return null; }
            uint delta = unchecked(request.Sequence - entry.Request.Sequence);
            if (delta == 0) return SameRequest(request, entry.Request) ? Copy(entry.Receipt) : null;
            if (delta > int.MaxValue) return null;
            canBegin = true; return null;
        }

        public BagOpenReceipt? Begin(BagOpenRequest request, byte actor, BagOpenStatus status)
        {
            var replay = Inspect(request, actor, out bool canBegin);
            if (!canBegin) return replay;
            if (status == BagOpenStatus.Applied || (byte)status > (byte)BagOpenStatus.Failed) return null;
            if (status == BagOpenStatus.Pending)
            {
                if (_consumedRevisions.TryGetValue(request.ItemId, out uint consumed)
                    && (request.ExpectedRevision == consumed || unchecked(request.ExpectedRevision - consumed) > int.MaxValue))
                    status = BagOpenStatus.Stale;
                else if (_pending.ContainsKey(request.ItemId) || ActorPending(actor))
                    status = BagOpenStatus.Busy;
            }
            var entry = new Entry { Request = Copy(request), Receipt = Receipt(request, status) };
            // Rejected requests are immutable too: a lost Busy receipt cannot turn
            // into a later opening after the other player's operation settles.
            _entries[actor] = entry;
            if (status == BagOpenStatus.Pending) _pending.Add(request.ItemId, entry);
            return Copy(entry.Receipt);
        }

        public BagOpenReceipt? Complete(BagOpenRequest request, bool applied)
        {
            if (_pending.TryGetValue(request.ItemId, out var entry) && SameRequest(request, entry.Request))
            {
                entry.Receipt.Status = applied ? BagOpenStatus.Applied : BagOpenStatus.Failed;
                if (applied) _consumedRevisions[request.ItemId] = request.ExpectedRevision;
                _pending.Remove(request.ItemId);
                return Copy(entry.Receipt);
            }
            return _entries.TryGetValue(request.PlayerId, out entry) && SameRequest(request, entry.Request)
                ? Copy(entry.Receipt) : null;
        }

        private bool ActorPending(byte actor)
        {
            foreach (var entry in _pending.Values) if (entry.Request.PlayerId == actor) return true;
            return false;
        }

        // A disconnected player's native operation may still be finishing. Its bag
        // reservation survives admission cleanup until Complete or session Clear.
        public void ForgetPlayer(byte playerId) => _entries.Remove(playerId);
        public void Clear() { _entries.Clear(); _pending.Clear(); _consumedRevisions.Clear(); }

        public static BagOpenStatus Check(BagOpenRequest request, BagState? state,
            bool available, bool nearby, bool owner, bool ready)
        {
            if (!available || state == null || !BagStatePolicy.IsValidState(state) || state.ItemId != request.ItemId)
                return BagOpenStatus.Unavailable;
            if (request.ExpectedRevision != state.Revision) return BagOpenStatus.Stale;
            if (state.Remaining == 0) return BagOpenStatus.Unavailable;
            if (!nearby) return BagOpenStatus.OutOfReach;
            if (!owner) return BagOpenStatus.NotOwner;
            return ready ? BagOpenStatus.Pending : BagOpenStatus.Busy;
        }

        public static bool Terminal(BagOpenStatus status) => status > BagOpenStatus.Pending && status <= BagOpenStatus.Failed;
        private static bool Valid(BagOpenRequest r, byte actor) => r != null && actor < byte.MaxValue
            && r.PlayerId == actor && r.ItemId != 0;
        private static bool SameRequest(BagOpenRequest a, BagOpenRequest b) => a.PlayerId == b.PlayerId
            && a.Sequence == b.Sequence && a.ItemId == b.ItemId && a.ExpectedRevision == b.ExpectedRevision && a.OpenAll == b.OpenAll;
        public static BagOpenRequest Copy(BagOpenRequest r) => new BagOpenRequest {
            PlayerId = r.PlayerId, Sequence = r.Sequence, ItemId = r.ItemId, ExpectedRevision = r.ExpectedRevision, OpenAll = r.OpenAll };
        public static BagOpenReceipt Receipt(BagOpenRequest r, BagOpenStatus status) => new BagOpenReceipt {
            PlayerId = r.PlayerId, Sequence = r.Sequence, ItemId = r.ItemId, ExpectedRevision = r.ExpectedRevision, OpenAll = r.OpenAll, Status = status };
        public static BagOpenReceipt Copy(BagOpenReceipt r) => new BagOpenReceipt {
            PlayerId = r.PlayerId, Sequence = r.Sequence, ItemId = r.ItemId, ExpectedRevision = r.ExpectedRevision, OpenAll = r.OpenAll, Status = r.Status };
    }
}
