using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class LottoTicketSync
    {
        private readonly LottoTicketLedger _ledger = new LottoTicketLedger();
        private readonly LottoTicketReplica _replica = new LottoTicketReplica();
        private readonly Dictionary<string, Ticket> _tickets = new Dictionary<string, Ticket>(StringComparer.Ordinal);
        private readonly Dictionary<byte, float> _requestAt = new Dictionary<byte, float>();
        private readonly List<GameObject> _hiddenLocalTickets = new List<GameObject>();
        private readonly ItemWorldSync _items;
        private readonly LotterySync _draw;
        private LottoTicketsData? _c;
        private PlayMakerFSM? _pay, _spawner, _claim;
        private GameObject? _prefab, _spawn, _database, _paper;
        private FsmFloat? _cash, _bank, _total;
        private FsmInt? _counter, _payRound;
        private FsmString? _saveId;
        private FsmGameObject? _claimObject;
        private IList[] _selection = new IList[0];
        private LottoTicketRequest? _pending;
        private readonly ulong _token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) | 1UL;
        private uint _requestSequence, _stateSequence;
        private float _probeAt, _scanAt, _sendAt, _retryAt, _pendingSince;
        private bool _ready, _failed, _resetClaim;

        private sealed class Ticket
        {
            public GameObject Object = null!;
            public PlayMakerFSM Use = null!, Data = null!;
            public FsmInt Round = null!;
            public FsmFloat Winnings = null!, UseWinnings = null!;
            public IList[] Lines = new IList[0];
            public LottoTicketState State = new LottoTicketState();
            public LottoTicketState? Broadcast;
            public bool GuestClone, Registered;
            public bool HasApplied;
            public uint AppliedSequence;
        }

        public LottoTicketSync(ItemWorldSync items, LotterySync draw)
        {
            _items = items; _draw = draw;
            _items.TicketDespawnHandler = OnItemDiscard;
        }
        public void ForgetPlayer(byte player) { _ledger.ForgetPlayer(player); _requestAt.Remove(player); }

        public void Update(SessionManager session)
        {
            if (_failed || session.PlayerCount == 0) return;
            try
            {
                if (!_ready && Time.unscaledTime >= _probeAt) { _probeAt = Time.unscaledTime + 2f; Locate(); }
                if (!_ready) return;
                if (Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 5f; Discover(session); }
                if (_resetClaim) { _resetClaim = false; Enter(_claim, "claimReset"); }
                if (!session.IsHost) ApplyPending();
                if (_pending != null && Time.unscaledTime >= _retryAt)
                {
                    _retryAt = Time.unscaledTime + 1f;
                    if (session.IsHost) OnRequest(_pending);
                    else session.SendWorldMessage(_pending, Channel.ReliableOrdered);
                    if (_pending != null && Time.unscaledTime - _pendingSince > 8f)
                    {
                        _pendingSince = Time.unscaledTime;
                        session.AddSystemChat("* Waiting for the host to confirm this Lotto ticket.");
                    }
                }
                foreach (var ticket in _tickets.Values) Register(ticket);
                if (!session.IsHost || Time.unscaledTime < _sendAt) return;
                bool keepalive = Time.unscaledTime >= _keepaliveAt;
                _sendAt = Time.unscaledTime + .5f;
                foreach (var ticket in _tickets.Values)
                {
                    var state = Read(ticket);
                    if (state == null) continue;
                    if (!keepalive && ticket.Broadcast != null && SameContent(ticket.Broadcast, state)) continue;
                    state.Sequence = unchecked(++_stateSequence);
                    session.SendWorldMessage(state, Channel.ReliableOrdered);
                    ticket.Broadcast = LottoTicketReplica.Copy(state);
                }
                if (keepalive) _keepaliveAt = Time.unscaledTime + 15f;
            }
            catch (Exception e) { Disable(e); }
        }
        private float _keepaliveAt;

        private void QueueBuy()
        {
            try
            {
                if (_failed || !_ready || _pending != null || _c == null || _total == null || _payRound == null) return;
                var session = SessionManager.Instance;
                if (session == null || session.PlayerCount == 0) return;
                float count = _total.Value / _c.LinePrice;
                if (count < 1 || count > 3 || count != (int)count
                    || !LottoTicketLedger.CaptureLines(_selection, (int)count, out var numbers))
                {
                    session.AddSystemChat("* Complete each Lotto row before paying."); Enter(_pay, "closeState"); return;
                }
                Queue(new LottoTicketRequest { Operation = LottoTicketRequest.Buy, LineCount = (byte)count,
                    Round = _payRound.Value, Numbers = numbers }, session);
            }
            catch (Exception e) { Disable(e); }
        }

        private bool QueueClaim()
        {
            var obj = _claimObject?.Value;
            if (_c == null || obj == null || obj.name != _c["ticketName"]) return false;
            try
            {
                var session = SessionManager.Instance;
                if (session == null || _failed || !_ready || _pending != null) { _resetClaim = true; return true; }
                foreach (var ticket in _tickets.Values)
                {
                    if (ticket.Object != obj) continue;
                    Queue(new LottoTicketRequest { Operation = LottoTicketRequest.Claim, TicketId = ticket.State.TicketId }, session);
                    return true;
                }
                session.AddSystemChat("* This Lotto ticket is not ready. Pick it up and try again.");
                _resetClaim = true;
            }
            catch (Exception e) { Disable(e); }
            return true;
        }

        private void Queue(LottoTicketRequest request, SessionManager session)
        {
            request.PlayerId = session.LocalPlayerId; request.Token = _token;
            request.Sequence = unchecked(++_requestSequence);
            _pending = request; _retryAt = 0; _pendingSince = Time.unscaledTime;
        }

        public void OnRequest(LottoTicketRequest request)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _failed || !_ready || _c == null || _cash == null || _bank == null) return;
            try
            {
                float now = Time.unscaledTime;
                if (_requestAt.TryGetValue(request.PlayerId, out float next) && now < next) return;
                _requestAt[request.PlayerId] = now + .15f;
                if (!_ledger.TryReceipt(request, out var receipt))
                {
                    if (!GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)) return;
                    if (request.Operation == LottoTicketRequest.Buy)
                    {
                        if (_spawn == null || !_draw.TryPurchaseRound(out int round)) return;
                        var result = _ledger.Buy(request, round, _c.LinePrice, _cash.Value,
                            (position - _spawn.transform.position).sqrMagnitude <= 36f,
                            () => CreateHostTicket(request), out float cash);
                        if (result == null) return;
                        receipt = result; _cash.Value = cash;
                    }
                    else
                    {
                        Ticket? ticket = null;
                        if (LottoTicketLedger.ValidId(request.TicketId)) _tickets.TryGetValue(request.TicketId, out ticket);
                        var current = ticket != null ? Read(ticket) : null;
                        bool nearby = _claim != null && ticket != null && ticket.Object != null
                            && (position - _claim.transform.position).sqrMagnitude <= 36f
                            && (ticket.Object.transform.position - _claim.transform.position).sqrMagnitude <= 9f;
                        // Native calculation can span frames. Never claim an intermediate tier sum.
                        if (ticket != null && !ticket.State.Retired && ticket.Data != null
                            && ticket.Data.ActiveStateName != _c["dataIdle"]) return;
                        receipt = _ledger.Claim(request, current != null ? (float?)current.Winnings : null,
                            _c.BankThreshold, _cash.Value, _bank.Value, nearby, out float cash, out float bank);
                        if (receipt.Result == LottoTicketReceipt.Accepted && ticket != null)
                        {
                            // Set the native tombstone before publishing money; SAVEGAME now deletes
                            // this ticket even if its visual garbage transition is delayed or fails.
                            ticket.Round.Value = 8888; ticket.Winnings.Value = ticket.UseWinnings.Value = 0;
                            ticket.State.Retired = true;
                            _cash.Value = cash; _bank.Value = bank;
                            RetireNative(ticket);
                        }
                    }
                    SyncEventLog.Record("lotto-ticket", "player " + request.PlayerId + " op " + request.Operation
                        + " seq " + request.Sequence + " result " + receipt.Result + " id " + receipt.TicketId + " amount " + receipt.Amount);
                }
                if (receipt.Result == LottoTicketReceipt.Stale) return;
                if (_tickets.TryGetValue(receipt.TicketId, out var changed))
                {
                    var state = Read(changed);
                    if (state != null) { state.Sequence = unchecked(++_stateSequence); session.SendWorldMessage(state, Channel.ReliableOrdered); }
                }
                var wallet = WorldSyncManager.Instance?.BuildWalletState();
                if (wallet != null) session.SendWorldMessage(wallet, Channel.ReliableOrdered);
                session.SendWorldMessage(receipt, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnReceipt(receipt);
                _sendAt = 0;
            }
            catch (Exception e) { Disable(e); }
        }

        public void OnReceipt(LottoTicketReceipt receipt)
        {
            var session = SessionManager.Instance;
            if (session == null || _pending == null || receipt.PlayerId != session.LocalPlayerId
                || receipt.Token != _pending.Token || receipt.Sequence != _pending.Sequence
                || receipt.Result > LottoTicketReceipt.Redeemed || !BankTransferPolicy.IsFinite(receipt.Amount) || receipt.Amount < 0) return;
            bool buying = _pending.Operation == LottoTicketRequest.Buy;
            _pending = null;
            try
            {
                if (buying && _c != null && _pay != null && _pay.ActiveStateName == _c["requestState"])
                    Enter(_pay, receipt.Result == LottoTicketReceipt.Accepted ? "commitState"
                        : receipt.Result == LottoTicketReceipt.Funds ? "fundsState" : "closeState");
                if (!buying) _resetClaim = true;
                if (receipt.Result != LottoTicketReceipt.Accepted)
                    session.AddSystemChat(receipt.Result == LottoTicketReceipt.Changed
                        ? "* The Lotto round changed. Reopen the form before paying."
                        : receipt.Result == LottoTicketReceipt.Redeemed ? "* This Lotto ticket has already been collected."
                        : "* Lotto transaction declined. Check your balance and try again at the shop.");
                else if (!buying && receipt.Amount > 0)
                    session.AddSystemChat("* Lotto paid " + receipt.Amount.ToString("0.##") + " mk to shared "
                        + (receipt.Destination == 1 ? "bank." : "cash."));
            }
            catch (Exception e) { Disable(e); }
        }

        public void OnState(LottoTicketState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || _failed || !_replica.Receive(state)) return;
            try { if (_ready) ApplyPending(); }
            catch (Exception e) { Disable(e); }
        }

        public IEnumerable<LottoTicketState> BuildSnapshots()
        {
            var result = new List<LottoTicketState>();
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _failed) return result;
            try
            {
                if (!_ready) Locate();
                if (!_ready) return result;
                Discover(session);
                foreach (var ticket in _tickets.Values)
                {
                    var state = Read(ticket);
                    if (state == null) continue;
                    state.Sequence = unchecked(++_stateSequence); result.Add(state);
                }
            }
            catch (Exception e) { Disable(e); }
            return result;
        }
        private static bool SameContent(LottoTicketState a, LottoTicketState b) => a.TicketId == b.TicketId
            && a.Round == b.Round && a.Retired == b.Retired && a.Winnings == b.Winnings
            && LottoTicketReplica.SameNumbers(a.Numbers, b.Numbers);

        private void Enter(PlayMakerFSM? fsm, string key)
        {
            if (fsm != null && _c != null && fsm.gameObject.activeInHierarchy) FsmHook.FireRemoteEntry(fsm, _c[key]);
        }
        private readonly FsmSuppressor _payFailure = new FsmSuppressor(), _claimFailure = new FsmSuppressor();
        private void Disable(Exception e)
        {
            if (_failed) return;
            _failed = true; _payFailure.Suppress(_pay); _claimFailure.Suppress(_claim);
            WinterMPPlugin.Log.LogError("Lotto ticket sync disabled; draw and other world sync continue: " + e);
        }
    }
}
