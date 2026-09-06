using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>A click names one observed part revision. Retries only recover its receipt.</summary>
    public sealed class PartFitLedger
    {
        private sealed class Entry
        {
            public PartFitRequest Request = null!;
            public PartFitStatus Status;
        }
        private readonly Dictionary<byte, Entry> _entries = new Dictionary<byte, Entry>();

        public PartFitReceipt? Inspect(PartFitRequest request, byte actor, out bool canBegin)
        {
            canBegin = false;
            if (request == null || actor == 0 || actor == byte.MaxValue || request.PlayerId != actor || request.Token == 0
                || !PartSlotPolicy.ValidRequest(request.Operation, request.SlotIndex)) return null;
            if (!_entries.TryGetValue(actor, out var previous)) { canBegin = true; return null; }
            if (request.Token != previous.Request.Token) return null;
            uint delta = unchecked(request.Sequence - previous.Request.Sequence);
            if (delta == 0) return Same(request, previous.Request) ? Receipt(request, previous.Status) : null;
            if (delta > int.MaxValue) return null;
            if (previous.Status == PartFitStatus.Pending) return Receipt(request, PartFitStatus.Busy);
            canBegin = true; return null;
        }

        public PartFitReceipt? Begin(PartFitRequest request, byte actor, PartFitStatus status)
        {
            var replay = Inspect(request, actor, out bool begin);
            if (!begin) return replay;
            if (status == PartFitStatus.Accepted || (byte)status > (byte)PartFitStatus.Bolted) return null;
            _entries[actor] = new Entry { Request = Copy(request), Status = status };
            return Receipt(request, status);
        }

        public PartFitReceipt? Complete(PartFitRequest request, bool accepted)
        {
            if (!_entries.TryGetValue(request.PlayerId, out var entry) || !Same(request, entry.Request)) return null;
            if (entry.Status == PartFitStatus.Pending) entry.Status = accepted ? PartFitStatus.Accepted : PartFitStatus.Failed;
            return Receipt(request, entry.Status);
        }
        public void ForgetPlayer(byte actor) => _entries.Remove(actor);
        public void Clear() => _entries.Clear();

        public static PartFitStatus Check(PartFitRequest request, ReplacementPartState? state,
            bool available, bool nearby, bool inRange, bool mountFree, bool authority, bool busy, byte selectedSlot = 0)
        {
            if (request.Operation != PartFitOperation.Install || !available || state == null
                || !PartIdentity.TryItemId(state.NativeId, out uint id) || id != request.ItemId)
                return PartFitStatus.Unavailable;
            if (request.ExpectedRevision != state.Revision || request.SlotIndex != selectedSlot) return PartFitStatus.Stale;
            if (state.Installed || state.AssemblyId != 0) return PartFitStatus.NotLoose;
            if (!nearby || !inRange) return PartFitStatus.TooFar;
            if (!mountFree) return PartFitStatus.Blocked;
            return !authority || busy ? PartFitStatus.Busy : PartFitStatus.Pending;
        }

        public static bool WithinMount(NetVector3 part, NetVector3 mount, float tolerance)
        {
            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance <= 0 || tolerance > 1) return false;
            double x = (double)part.X - mount.X, y = (double)part.Y - mount.Y, z = (double)part.Z - mount.Z;
            return x * x + y * y + z * z < (double)tolerance * tolerance;
        }

        public static bool HasAuthority(byte actor, bool hostOwns, byte remoteOwner, byte lastOwner, float releaseAge)
        {
            if (actor == 0 || actor == byte.MaxValue || hostOwns) return false;
            if (remoteOwner == actor) return true;
            return remoteOwner == byte.MaxValue && lastOwner == actor && releaseAge >= 0 && releaseAge <= .5f;
        }
        private static bool Same(PartFitRequest a, PartFitRequest b) => a.PlayerId == b.PlayerId && a.Token == b.Token
            && a.Sequence == b.Sequence && a.ItemId == b.ItemId && a.ExpectedRevision == b.ExpectedRevision
            && a.Operation == b.Operation && a.SlotIndex == b.SlotIndex;
        public static PartFitRequest Copy(PartFitRequest r) => new PartFitRequest { PlayerId = r.PlayerId, Token = r.Token,
            Sequence = r.Sequence, ItemId = r.ItemId, ExpectedRevision = r.ExpectedRevision, Operation = r.Operation, SlotIndex = r.SlotIndex };
        public static PartFitReceipt Receipt(PartFitRequest r, PartFitStatus status) => new PartFitReceipt {
            PlayerId = r.PlayerId, Token = r.Token, Sequence = r.Sequence, ItemId = r.ItemId, Status = status, Operation = r.Operation, SlotIndex = r.SlotIndex };
    }

    public sealed class PartFitClient
    {
        private readonly ulong _token;
        private uint _sequence;
        private PartFitRequest? _pending;
        private float _nextSend;
        public PartFitOperation Operation => _pending == null ? PartFitOperation.Install : _pending.Operation;
        public bool Pending => _pending != null;
        public PartFitClient(ulong token)
        {
            if (token == 0) throw new ArgumentException("Fitting token must be nonzero.");
            _token = token;
        }
        public bool TryBegin(byte player, uint item, uint revision, PartFitOperation operation = PartFitOperation.Install, byte slotIndex = 0)
        {
            if (Pending || player == 0 || player == byte.MaxValue || !PartSlotPolicy.ValidRequest(operation, slotIndex)) return false;
            _pending = new PartFitRequest { PlayerId = player, Token = _token, Sequence = unchecked(++_sequence),
                ItemId = item, ExpectedRevision = revision, Operation = operation, SlotIndex = slotIndex };
            _nextSend = 0; return true;
        }
        public PartFitRequest? Poll(float now)
        {
            if (_pending == null || float.IsNaN(now) || float.IsInfinity(now) || now < _nextSend) return null;
            _nextSend = now + .5f; return PartFitLedger.Copy(_pending);
        }
        public bool Receive(PartFitReceipt receipt)
        {
            if (_pending == null || receipt.PlayerId != _pending.PlayerId || receipt.Token != _pending.Token
                || receipt.Sequence != _pending.Sequence || receipt.ItemId != _pending.ItemId || receipt.Operation != _pending.Operation
                || receipt.SlotIndex != _pending.SlotIndex
                || receipt.Status == PartFitStatus.Pending || (byte)receipt.Status > (byte)PartFitStatus.Bolted) return false;
            _pending = null; return true;
        }
    }
}
