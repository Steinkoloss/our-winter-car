using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>Host-owned table finances and supported shared listings.</summary>
    internal sealed partial class FleaSaleSync
    {
        private readonly List<FsmSuppressor> _failures = new List<FsmSuppressor>();
        private readonly FleaSaleLedger _ledger = new FleaSaleLedger();
        private FleaSaleBinding? _binding;
        private FleaSaleState? _received, _published;
        private FleaSaleIntent? _pending;
        private bool _failed;
        private float _probeAt, _tickAt, _keepAliveAt, _retryAt;
        private ushort _sequence;

        public void ForgetPlayer(byte player) { _ledger.ForgetPlayer(player); _listingReceipts.ForgetPlayer(player); }
        public void Clear()
        {
            foreach (var pause in _failures) pause.Restore();
            _failures.Clear();
            ClearListings();
            try { _binding?.Restore(); }
            catch (Exception e) { WinterMPPlugin.Log.LogError("Flea cleanup failed: " + e); }
            _binding = null; _received = _published = null; _pending = null;
            _failed = false; _probeAt = _tickAt = _keepAliveAt = _retryAt = 0; _sequence = 0; _ledger.Clear();
        }
        public void Update(SessionManager session)
        {
            try
            {
                if (!Locate(session)) return;
                UpdateListings(session);
                if (session.IsHost && Time.unscaledTime >= _tickAt)
                {
                    _tickAt = Time.unscaledTime + .5f;
                    Publish(session, Time.unscaledTime >= _keepAliveAt);
                }
                if (!session.IsHost && _received != null) _binding!.Apply(_received);
                if (_pending == null || Time.unscaledTime < _retryAt) return;
                _retryAt = Time.unscaledTime + 1;
                if (session.IsHost) TryAcceptIntent(_pending);
                else session.SendWorldMessage(_pending, Channel.ReliableOrdered);
            }
            catch (Exception e) { Fail(e); }
        }
        private bool Locate(SessionManager session)
        {
            if (_failed) return false;
            if (_binding != null) return true;
            if (Time.unscaledTime < _probeAt) return false;
            _probeAt = Time.unscaledTime + 2;
            _binding = FleaSaleBinding.Bind(!session.IsHost, Queue);
            if (_binding != null) _binding.InstallListings(OfferListing, SubmitListing, SellListing, Fail);
            if (_binding != null) WinterMPPlugin.Log.LogInfo("Flea paid checkout and proceeds bound (host=" + session.IsHost + ").");
            return _binding != null;
        }
        public FleaSaleState? BuildSnapshot()
        {
            try
            {
                var session = SessionManager.Instance;
                return session != null && session.IsHost && Locate(session) ? _ledger.Observe(_binding!.Capture()) : null;
            }
            catch (Exception e) { Fail(e); return null; }
        }
        public void Apply(FleaSaleState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !FleaSalePolicy.CanApply(_received, state)) return;
            _received = FleaSalePolicy.Copy(state);
            try { if (Locate(session)) _binding!.Apply(state); }
            catch (Exception e) { Fail(e); }
        }
        private void Queue(byte action)
        {
            try
            {
                var session = SessionManager.Instance;
                if (session == null || _binding == null || _failed || _pending != null) return;
                var quote = session.IsHost ? _ledger.Observe(_binding.Capture()) : _received;
                int weeks = action == FleaSaleIntent.PayRent ? _binding.Weeks : 0;
                if (quote == null || (action == FleaSaleIntent.PayRent && (!_binding.RentOnly || weeks < 1 || weeks > 52)))
                {
                    _binding.Finish(action, false);
                    session.AddSystemChat(quote == null ? "* Waiting for the host's flea table. Try again shortly."
                        : "* Shared flea checkout currently supports 1–52 rental weeks in an otherwise empty basket.");
                    return;
                }
                _pending = new FleaSaleIntent { PlayerId = session.LocalPlayerId, Action = action,
                    Sequence = ++_sequence, Revision = quote.Revision, Weeks = (ushort)weeks };
                _retryAt = 0;
            }
            catch (Exception e) { Fail(e); }
        }
        public bool TryAcceptIntent(FleaSaleIntent request)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !FleaSalePolicy.Valid(request)) return false;
            try
            {
                if (!Locate(session)) return false;
                var b = _binding!;
                _ledger.Observe(b.Capture());
                if (!_ledger.TryReceipt(request, out var result))
                {
                    bool rent = request.Action == FleaSaleIntent.PayRent;
                    var target = rent ? b.CashPosition : b.EnvelopePosition;
                    bool near = GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)
                        && (position - target).sqrMagnitude <= 16f;
                    result = _ledger.Apply(request, b.Wallet, near, rent ? b.RentAvailable : b.CollectAvailable,
                        out float cash, out var next);
                    if (result.Result == FleaSaleResult.Accepted) b.Commit(request, cash, next!);
                    SyncEventLog.Record("flea-payment", "player " + request.PlayerId + " seq " + request.Sequence
                        + " action " + request.Action + " revision " + request.Revision + " result " + result.Result);
                }
                Publish(session, true);
                var wallet = WorldSyncManager.Instance?.BuildWalletState();
                if (wallet != null) session.SendWorldMessage(wallet, Channel.ReliableOrdered);
                session.SendWorldMessage(result, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnResult(result);
                return result.Result == FleaSaleResult.Accepted;
            }
            catch (Exception e) { Fail(e); return false; }
        }
        public void OnResult(FleaSaleResult result)
        {
            var session = SessionManager.Instance;
            if (session == null || result.PlayerId != session.LocalPlayerId || _pending == null
                || _pending.Sequence != result.Sequence || _pending.Action != result.Action) return;
            _pending = null;
            try
            {
                _binding?.Finish(result.Action, result.Result == FleaSaleResult.Accepted, result.Result == FleaSaleResult.Funds);
                if (result.Result != FleaSaleResult.Accepted)
                    session.AddSystemChat(result.Result == FleaSaleResult.Changed ? "* The flea table changed. Review it and try again."
                        : result.Result == FleaSaleResult.Funds ? "* Not enough cash for this rental."
                        : "* Flea transaction declined. Return to the checkout or available sales envelope to try again.");
            }
            catch (Exception e) { Fail(e); }
        }
        private void Publish(SessionManager session, bool force)
        {
            var state = _ledger.Observe(_binding!.Capture());
            if (!force && _published != null && state.Revision == _published.Revision) return;
            _published = state; _keepAliveAt = Time.unscaledTime + 20;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }
        private void Fail(Exception e)
        {
            if (_failed) return;
            _failed = true; _pending = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (FleaSaleBinding.OwnsCheckout(path, fsm.FsmName)
                    || FleaSaleBinding.OwnsEnvelope(path, fsm.FsmName)
                    || path == "FleaMarket/SaleTable")
                {
                    var pause = new FsmSuppressor(); pause.Suppress(fsm); _failures.Add(pause);
                }
            }
            WinterMPPlugin.Log.LogError("Flea transactions disabled: " + e);
        }
    }
}
