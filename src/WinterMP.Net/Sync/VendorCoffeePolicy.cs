using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class VendorCoffeePolicy
    {
        public const string InspectionRoot = "INSPECTION/LOD/CoffeeAutomatic";
        // This safety boundary survives absent/malformed catalog data. Never let an
        // unresolved dedicated machine silently fall back to generic Purchase replay.
        public static bool QuarantineBuy(string path, string fsm) =>
            path == InspectionRoot + "/Functions/CoffeeButton" && fsm == "Buy";
        public static bool FiniteNonnegative(float value) => value >= 0 && !float.IsInfinity(value);
        public static bool Player(byte value) => value > 0 && value < 255;
        public static bool Action(VendorCoffeeAction value) => value >= VendorCoffeeAction.Acquire && value <= VendorCoffeeAction.Drink;
        public static byte Bit(VendorCoffeeAction value) => (byte)(1 << ((int)value - 1));
        public static bool Valid(VendorCoffeeIntent i) => i.MachineId != 0 && i.CupId != 0 && i.Epoch != 0 && i.Generation != 0
            && i.ExpectedRevision != 0 && i.Connection != 0 && i.Sequence != 0 && Player(i.PlayerId) && Action(i.Action);
        public static bool Valid(VendorCoffeeState s)
        {
            if (s.MachineId == 0 || s.CupId == 0 || s.Epoch == 0 || s.Generation == 0 || s.Revision == 0
                || s.CompletedActions > 15 || !FiniteNonnegative(s.Contents)
                || s.Lifecycle < VendorCoffeeLifecycle.Available || s.Lifecycle > VendorCoffeeLifecycle.Retired
                || (s.Holder == 0 ? s.HolderConnection != 0 : !Player(s.Holder) || s.HolderConnection == 0)) return false;
            if (s.Lifecycle != VendorCoffeeLifecycle.Active && (s.Contents != 0 || s.Holder != 0)) return false;
            var p = s.Position; var q = s.Rotation; float norm = q.X*q.X + q.Y*q.Y + q.Z*q.Z + q.W*q.W;
            return Math.Abs(p.X) <= 100000 && Math.Abs(p.Y) <= 100000 && Math.Abs(p.Z) <= 100000 && norm >= .9f && norm <= 1.1f;
        }
        public static bool Valid(VendorCoffeeResult r) => r.MachineId != 0 && r.CupId != 0 && r.Epoch != 0 && r.Generation != 0
            && r.Revision > 1 && r.Connection != 0 && r.Sequence != 0 && Player(r.PlayerId) && Action(r.Action)
            && FiniteNonnegative(r.Consumed) && (r.Action == VendorCoffeeAction.Drink ? r.Consumed > 0 : r.Consumed == 0);
        public static bool SameIdentity(VendorCoffeeState a, VendorCoffeeState b) => a.MachineId == b.MachineId && a.CupId == b.CupId && a.Epoch == b.Epoch;
    }

    // These facts MUST come from the host's authenticated connection, fresh living
    // pose and audited geometry, never from fields claimed in an intent packet.
    public sealed class VendorCoffeeActor
    {
        public readonly byte PlayerId;
        public readonly uint Connection;
        public readonly bool Authenticated, Alive, FreshPose, InRange;
        public VendorCoffeeActor(byte player, uint connection, bool authenticated, bool alive, bool freshPose, bool inRange)
        { PlayerId = player; Connection = connection; Authenticated = authenticated; Alive = alive; FreshPose = freshPose; InRange = inRange; }
    }

    public interface IVendorCoffeeAdapter
    {
        // Prepare is read-only. Validate native readiness, exact bindings, holder,
        // funds and action signatures here. Commit must be atomic: false means NO
        // native change; it must not run both a manual debit and vanilla's debit.
        // Unity FSMs are not assumed transactional. No production adapter exists
        // until extraction establishes a safe prepare/execute/observe boundary.
        bool TryPrepare(VendorCoffeeState before, VendorCoffeeIntent intent, out VendorCoffeeMutation? mutation);
    }

    public sealed class VendorCoffeeMutation
    {
        internal readonly VendorCoffeeState After;
        internal readonly float Debit;
        internal readonly byte Outputs, Drinks;
        internal readonly Func<bool> Commit;
        public VendorCoffeeMutation(VendorCoffeeState after, float debit, byte outputs, byte drinks, Func<bool> commit)
        { After = after.Copy(); Debit = debit; Outputs = outputs; Drinks = drinks; Commit = commit; }
    }

    // One machine/cup serving ledger. All host and guest actions enter TryAccept.
    // Neither a native factory identity nor a save/reset branch is inferred here.
    public sealed class VendorCoffeeAuthority
    {
        private sealed class ConnectionState { internal uint Token, Sequence; internal bool Live; }
        private readonly object _gate = new object();
        private readonly Dictionary<byte, ConnectionState> _connections = new Dictionary<byte, ConnectionState>();
        private readonly IVendorCoffeeAdapter _adapter;
        private readonly float _nativePrice;
        private VendorCoffeeState _state;
        private bool _busy, _failed;
        public VendorCoffeeAuthority(VendorCoffeeState initial, float auditedNativePrice, IVendorCoffeeAdapter adapter)
        {
            if (!VendorCoffeePolicy.Valid(initial) || !VendorCoffeePolicy.FiniteNonnegative(auditedNativePrice) || adapter == null)
                throw new ArgumentException("Audited vendor coffee initial state, price and adapter required.");
            _state = initial.Copy(); _nativePrice = auditedNativePrice; _adapter = adapter;
        }
        public VendorCoffeeState Snapshot() { lock (_gate) return _state.Copy(); }
        public bool Connect(byte actor, uint token)
        {
            lock (_gate)
            {
                if (_busy || _failed || !VendorCoffeePolicy.Player(actor) || token == 0) return false;
                if (_connections.TryGetValue(actor, out var previous) && token <= previous.Token) return false;
                _connections[actor] = new ConnectionState { Token = token, Live = true }; return true;
            }
        }
        public void Forget(byte actor) { lock (_gate) { if (_connections.TryGetValue(actor, out var c)) c.Live = false; } }
        public void Stop() { lock (_gate) _failed = true; }
        public bool TryAccept(VendorCoffeeIntent request, VendorCoffeeActor actor, out VendorCoffeeResult? result)
        {
            lock (_gate)
            {
                result = null;
                if (_busy || _failed || !VendorCoffeePolicy.Valid(request) || !actor.Authenticated
                    || actor.PlayerId != request.PlayerId || actor.Connection != request.Connection
                    || !_connections.TryGetValue(actor.PlayerId, out var connection) || !connection.Live || connection.Token != actor.Connection
                    || request.Epoch != _state.Epoch || request.Sequence <= connection.Sequence) return false;
                // Rejected valid envelopes are spent too: a delayed retry cannot
                // become a fresh purchase after funds/range/availability changes.
                connection.Sequence = request.Sequence;
                if (!actor.Alive || !actor.FreshPose || !actor.InRange || request.MachineId != _state.MachineId || request.CupId != _state.CupId
                    || request.Generation != _state.Generation || request.ExpectedRevision != _state.Revision || _state.Revision == uint.MaxValue
                    || _state.Lifecycle == VendorCoffeeLifecycle.Retired || (_state.CompletedActions & VendorCoffeePolicy.Bit(request.Action)) != 0) return false;
                bool acquire = request.Action == VendorCoffeeAction.Acquire;
                if (acquire ? _state.Lifecycle != VendorCoffeeLifecycle.Available
                    : _state.Lifecycle != VendorCoffeeLifecycle.Active || _state.Holder != actor.PlayerId || _state.HolderConnection != actor.Connection) return false;
                if ((request.Action == VendorCoffeeAction.Fill || request.Action == VendorCoffeeAction.Drink)
                    && (_state.CompletedActions & VendorCoffeePolicy.Bit(VendorCoffeeAction.Purchase)) == 0) return false;
                var intent = request.Copy();
                _busy = true;
                try
                {
                    if (!_adapter.TryPrepare(_state.Copy(), intent.Copy(), out var mutation) || mutation == null) return false;
                    // Native read callbacks can reenter session teardown. The
                    // outer lock is reentrant, so revocation must be checked again.
                    if (_failed || !connection.Live) return false;
                    var next = mutation.After.Copy();
                    if (!ValidMutation(intent, mutation, next)) return false;
                    next.Revision = _state.Revision + 1;
                    next.CompletedActions |= VendorCoffeePolicy.Bit(intent.Action);
                    var receipt = new VendorCoffeeResult { MachineId = next.MachineId, CupId = next.CupId, Epoch = next.Epoch,
                        Generation = next.Generation, Revision = next.Revision, PlayerId = actor.PlayerId, Connection = actor.Connection,
                        Sequence = intent.Sequence, Action = intent.Action, Consumed = mutation.Drinks == 1 ? _state.Contents - next.Contents : 0 };
                    if (!VendorCoffeePolicy.Valid(next) || !VendorCoffeePolicy.Valid(receipt) || !mutation.Commit()) return false;
                    _state = next; result = receipt; return true;
                }
                catch { _failed = true; throw; } // Uncertain native execution must never be retried as a new action.
                finally { _busy = false; }
            }
        }
        private bool ValidMutation(VendorCoffeeIntent intent, VendorCoffeeMutation m, VendorCoffeeState next)
        {
            if (!VendorCoffeePolicy.Valid(next) || !VendorCoffeePolicy.SameIdentity(next, _state) || next.Generation != _state.Generation
                || next.Revision != _state.Revision || next.CompletedActions != _state.CompletedActions
                || !VendorCoffeePolicy.FiniteNonnegative(m.Debit) || m.Outputs > 1 || m.Drinks > 1) return false;
            if (intent.Action == VendorCoffeeAction.Acquire)
                return m.Debit == 0 && m.Outputs == 1 && m.Drinks == 0 && next.Lifecycle == VendorCoffeeLifecycle.Active
                    && next.Holder == intent.PlayerId && next.HolderConnection == intent.Connection && next.Contents == _state.Contents;
            if (intent.Action == VendorCoffeeAction.Drink)
                return m.Debit == 0 && m.Outputs == 0 && m.Drinks == 1 && next.Contents < _state.Contents
                    && (next.Lifecycle == VendorCoffeeLifecycle.Retired || next.Lifecycle == VendorCoffeeLifecycle.Active && next.Holder == _state.Holder && next.HolderConnection == _state.HolderConnection);
            if (next.Lifecycle != VendorCoffeeLifecycle.Active || next.Holder != _state.Holder || next.HolderConnection != _state.HolderConnection || m.Drinks != 0 || m.Outputs != 0) return false;
            if (intent.Action == VendorCoffeeAction.Purchase)
                return m.Debit == _nativePrice && next.Contents >= _state.Contents && (m.Debit > 0 || next.Contents > _state.Contents);
            return intent.Action == VendorCoffeeAction.Fill && m.Debit == 0 && next.Contents > _state.Contents;
        }
    }
}
