using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class BeerCaseAuthority
    {
        private sealed class Connection { internal uint Token, Sequence; internal bool Live; }
        private readonly object _gate = new object();
        private readonly Dictionary<byte, Connection> _actors = new Dictionary<byte, Connection>();
        private readonly IBeerCaseHost _host;
        private BeerCaseUpdate _state;
        private bool _busy, _stopped;
        public bool Faulted { get; private set; }

        public BeerCaseAuthority(BeerCaseUpdate initial, IBeerCaseHost host)
        {
            if (!BeerCasePolicy.Valid(initial) || initial.PlayerId != 255 || host == null)
                throw new ArgumentException("Audited native beer-case snapshot and binding required.");
            _state = initial.Copy(); _host = host;
        }
        public BeerCaseUpdate Snapshot() { lock (_gate) return _state.Copy(); }
        // Tokens are issued by the host admission boundary, never taken from an
        // incoming request. Retain token high-water even when a player disconnects.
        public bool Connect(byte actor, uint token)
        {
            lock (_gate)
            {
                if (_busy || _stopped || Faulted || !BeerCasePolicy.Player(actor) || token == 0
                    || (_actors.TryGetValue(actor, out var prior) && token <= prior.Token)) return false;
                _actors[actor] = new Connection { Token = token, Live = true }; return true;
            }
        }
        public void Forget(byte actor) { lock (_gate) { if (_actors.TryGetValue(actor, out var c)) c.Live = false; } }
        public void Stop() { lock (_gate) _stopped = true; }
        // Snapshot polling observes vanilla changes (NPC extraction, load/retirement),
        // never writes cached counts back into a native object or a sidecar save.
        public BeerCaseUpdate? Capture()
        {
            lock (_gate)
            {
                if (_busy || _stopped || Faulted) return null;
                _busy = true;
                try
                {
                    var live = _host.Read();
                    if (_stopped || !LiveValid(live)) return null;
                    if (live.Remaining != _state.Remaining || live.Available != _state.Available)
                    {
                        if (_state.Revision == uint.MaxValue) return null;
                        var next = _state.Copy(); next.Revision++; next.Remaining = live.Remaining; next.Available = live.Available;
                        next.PlayerId = 255; next.Connection = next.Sequence = 0; _state = next;
                    }
                    var snapshot = _state.Copy();
                    snapshot.PlayerId = 255; snapshot.Connection = snapshot.Sequence = 0;
                    return snapshot;
                }
                catch { Faulted = true; return null; }
                finally { _busy = false; }
            }
        }
        private bool LiveValid(BeerCaseUpdate live) => BeerCasePolicy.Valid(live) && BeerCasePolicy.SameCase(live, _state);
        public bool TryAccept(BeerCaseExtractIntent request, byte authenticatedActor, out BeerCaseUpdate? result)
        {
            lock (_gate)
            {
                result = null;
                if (_stopped || Faulted || request == null || !BeerCasePolicy.Player(authenticatedActor) || request.PlayerId != authenticatedActor
                    || request.Epoch != _state.Epoch || request.Sequence == 0 || !_actors.TryGetValue(authenticatedActor, out var connection)
                    || !connection.Live || request.Connection != connection.Token || request.Sequence <= connection.Sequence) return false;
                // Authenticated denied attempts (including reentrant ones) are spent.
                connection.Sequence = request.Sequence;
                if (_busy || !BeerCasePolicy.Valid(request) || request.CaseId != _state.CaseId || request.NativeId != _state.NativeId
                    || request.ExpectedRevision != _state.Revision || request.ExpectedRemaining != _state.Remaining
                    || !_state.Available || _state.Remaining == 0 || _state.Revision == uint.MaxValue) return false;
                var intent = request.Copy(); var before = _state.Copy();
                _busy = true;
                try
                {
                    var contact = _host.ReadContact(authenticatedActor);
                    var live = _host.Read();
                    if (_stopped || !connection.Live || !LiveValid(live) || !live.Available || live.Remaining != before.Remaining
                        || !BeerCasePolicy.Contact(contact, before)) return false;
                    if (!_host.TryExtractOne(before.Copy(), authenticatedActor)) return false;
                    var after = _host.Read();
                    // Never publish a predicted count as if native execution worked.
                    // An uncertain/partial adapter failure is quarantined, not retried.
                    if (!LiveValid(after) || after.Remaining != before.Remaining - 1)
                    { Faulted = true; return false; }
                    var next = before.Copy(); next.Revision++; next.Remaining = after.Remaining; next.Available = after.Available;
                    next.PlayerId = authenticatedActor; next.Connection = intent.Connection; next.Sequence = intent.Sequence;
                    _state = next;
                    // Teardown during commit cannot publish into a replacement session.
                    if (_stopped) return false;
                    result = next.Copy(); return true;
                }
                catch { Faulted = true; return false; }
                finally { _busy = false; }
            }
        }
    }
}
