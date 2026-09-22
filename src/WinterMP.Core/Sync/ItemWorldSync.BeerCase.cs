using System;
using System.Collections.Generic;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private BeerCaseAuthority? _beerCaseHost;
        private BeerCaseReplica? _beerCaseReplica;
        private Action<BeerCaseUpdate>? _beerCaseView;
        private uint _beerCaseConnection, _beerCaseSequence;

        // No caller installs a native binding yet: the graph-only catalog cannot
        // establish capacity, exact saved identity or an atomic count mutation.
        // These seams are exercised through this production partial in portable
        // tests. A future audited binding must replace/suppress the native input
        // before calling RequestBeerCaseExtraction; never hook after Remove bottle.
        internal bool BindBeerCaseHost(BeerCaseUpdate initial, IBeerCaseHost binding)
        {
            if (SessionManager.Instance?.IsHost != true || _beerCaseHost != null || _beerCaseReplica != null) return false;
            _beerCaseHost = new BeerCaseAuthority(initial, binding); return true;
        }
        internal bool BindBeerCaseGuest(uint id, string nativeId, uint epoch, uint connection, Action<BeerCaseUpdate> absoluteView)
        {
            if (SessionManager.Instance?.IsHost != false || _beerCaseHost != null || _beerCaseReplica != null
                || connection == 0 || absoluteView == null) return false;
            _beerCaseReplica = new BeerCaseReplica(id, nativeId, epoch); _beerCaseConnection = connection; _beerCaseView = absoluteView;
            return true;
        }
        internal bool AdmitBeerCasePlayer(byte actor, uint hostIssuedConnection)
        {
            if (SessionManager.Instance?.IsHost != true || _beerCaseHost == null || !_beerCaseHost.Connect(actor, hostIssuedConnection)) return false;
            if (actor == SessionManager.Instance.LocalPlayerId) { _beerCaseConnection = hostIssuedConnection; _beerCaseSequence = 0; }
            return true;
        }
        internal void ForgetBeerCasePlayer(byte actor) { _beerCaseHost?.Forget(actor); }
        internal bool RequestBeerCaseExtraction()
        {
            var session = SessionManager.Instance;
            if (session == null || _beerCaseConnection == 0 || _beerCaseSequence == uint.MaxValue) return false;
            var state = _beerCaseHost?.Snapshot() ?? _beerCaseReplica?.Snapshot();
            if (state == null || !state.Available || state.Remaining == 0) return false;
            var request = new BeerCaseExtractIntent { CaseId = state.CaseId, NativeId = state.NativeId, Epoch = state.Epoch,
                Connection = _beerCaseConnection, Sequence = ++_beerCaseSequence, ExpectedRevision = state.Revision,
                ExpectedRemaining = state.Remaining, PlayerId = session.LocalPlayerId };
            if (session.IsHost) return OnBeerCaseExtract(request, session.LocalPlayerId);
            // Guest input sends only an intent; no count, native event, item/cargo or
            // drink-effect mutation occurs optimistically, even when packets delay.
            session.SendWorldMessage(request, Channel.ReliableOrdered); return true;
        }
        internal bool OnBeerCaseExtract(BeerCaseExtractIntent request, byte authenticatedActor)
        {
            var session = SessionManager.Instance;
            if (session?.IsHost != true || _beerCaseHost == null || !_beerCaseHost.TryAccept(request, authenticatedActor, out var result)
                || result == null) return false;
            session.SendWorldMessage(result, Channel.ReliableOrdered); return true;
        }
        internal bool OnBeerCaseUpdate(BeerCaseUpdate update)
        {
            if (SessionManager.Instance?.IsHost != false || _beerCaseReplica == null || !_beerCaseReplica.Apply(update)) return false;
            try { _beerCaseView?.Invoke(update.Copy()); return true; }
            catch { ClearBeerCase(); return false; } // isolate a failed view, never replay extraction as recovery
        }
        internal IEnumerable<BeerCaseUpdate> BuildBeerCaseStates()
        {
            var state = _beerCaseHost?.Capture(); if (state != null) yield return state;
        }
        internal BeerCaseUpdate? BuildBeerCaseState(uint id)
        {
            if (_beerCaseHost == null || _beerCaseHost.Snapshot().CaseId != id) return null;
            return _beerCaseHost.Capture();
        }
        private void ClearBeerCase()
        {
            _beerCaseHost?.Stop(); _beerCaseHost = null; _beerCaseReplica = null; _beerCaseView = null;
            _beerCaseConnection = _beerCaseSequence = 0;
        }
    }
}
