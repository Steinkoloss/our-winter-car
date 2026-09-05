using System;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class WalletSync
    {
        private readonly BankTransferLedger _bankLedger = new BankTransferLedger();
        private readonly Queue<BankTransferIntent> _bankPending = new Queue<BankTransferIntent>();
        private readonly List<BankHook> _bankHooks = new List<BankHook>();
        private readonly Dictionary<byte, float> _bankRequestTimes = new Dictionary<byte, float>();
        private readonly FsmSuppressor _bankAccrual = new FsmSuppressor();
        private PlayMakerFSM? _bankData;
        private PlayMakerFSM? _atm;
        private PlayMakerFSM? _cashTrigger;
        private float _nextBankProbe;
        private float _nextBankRetry;
        private ushort _bankSequence;
        private bool _bankFailed;

        private sealed class BankHook
        {
            public PlayMakerFSM Fsm = null!;
            public FsmState State = null!;
            public FsmStateAction Mutation = null!;
            public FsmStateAction Callback = null!;
            public bool Enabled;
        }

        public void UpdateBanking(SessionManager session)
        {
            // Applying the retained state also closes the snapshot-before-binding gap.
            if (!session.IsHost && _lastReceived != null) ApplyBalances(_lastReceived);
            if (_bankFailed) return;
            try
            {
                if (Time.unscaledTime >= _nextBankProbe)
                {
                    _nextBankProbe = Time.unscaledTime + 5f;
                    LocateBanking();
                }
                if (session.IsHost) _bankAccrual.Restore();
                else
                {
                    _bankAccrual.Suppress(_bankData);
                    if (_bankPending.Count > 0 && Time.unscaledTime >= _nextBankRetry)
                    {
                        _nextBankRetry = Time.unscaledTime + 1f;
                        session.SendWorldMessage(_bankPending.Peek(), Channel.ReliableOrdered);
                    }
                }
            }
            catch (Exception e)
            {
                _bankFailed = true;
                WinterMPPlugin.Log.LogError("ATM sync disabled; balance replication continues: " + e);
            }
        }

        public void ForgetBankPlayer(byte playerId)
        {
            _bankLedger.ForgetPlayer(playerId);
            _bankRequestTimes.Remove(playerId);
        }

        public void OnBankTransfer(BankTransferIntent message, SessionManager session)
        {
            try { AcceptBankTransfer(message, session); }
            catch (Exception e)
            {
                _bankFailed = true;
                WinterMPPlugin.Log.LogError("ATM sync disabled; balance replication continues: " + e);
            }
        }

        private void AcceptBankTransfer(BankTransferIntent message, SessionManager session)
        {
            if (!session.IsHost || _bankFailed) return;
            float now = Time.unscaledTime;
            if (_bankRequestTimes.TryGetValue(message.PlayerId, out float next) && now < next) return;
            _bankRequestTimes[message.PlayerId] = now + 0.15f;

            if (_bankLedger.TryGetReceipt(message.PlayerId, message.Sequence, out bool accepted))
            {
                SendBankResult(session, message, accepted);
                return;
            }
            if (!_bankLedger.IsNew(message.PlayerId, message.Sequence)) return;
            Locate();
            if (_atm == null || _bankData == null) LocateBanking();
            // Missing game bindings and not-yet-arrived pose packets are transient.
            if (_moneyVar == null || _bankVar == null || _atm == null
                || !TryGetBankPlayerPosition(session, message.PlayerId, out var position)) return;

            bool nearby = Vector3.Distance(_atm.transform.position, position) <= 6f;
            bool canTransfer = BankTransferPolicy.TryTransfer(_moneyVar.Value, _bankVar.Value,
                message.Amount, out float money, out float bank);
            accepted = nearby && canTransfer;
            if (accepted)
            {
                _moneyVar.Value = money;
                _bankVar.Value = bank;
                NotifyMoneyChanged();
            }
            // Record before any presentation callback, which may throw or reenter.
            _bankLedger.Record(message.PlayerId, message.Sequence, accepted);
            if (accepted) RecordBankEntry(message.Amount);
            SyncEventLog.Record("bank-transfer", "player " + message.PlayerId + " seq "
                + message.Sequence + " amount " + message.Amount + " accepted " + accepted);
            SendBankResult(session, message, accepted);
        }

        private static bool TryGetBankPlayerPosition(SessionManager session, byte id, out Vector3 position)
        {
            position = Vector3.zero;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != id) continue;
                if (player.IsDead || player.LastTransformTime <= 0f
                    || Time.unscaledTime - player.LastTransformTime > 2f) return false;
                position = player.Position;
                return true;
            }
            return false;
        }

        public void OnBankResult(BankTransferResult message, SessionManager session)
        {
            if (session.IsHost || message.PlayerId != session.LocalPlayerId || _bankPending.Count == 0
                || _bankPending.Peek().Sequence != message.Sequence) return;
            _bankPending.Dequeue();
            _nextBankRetry = 0f;
            if (!message.Accepted)
                session.AddSystemChat("* ATM transfer declined. The shared balances have been refreshed.");
        }

        private void SendBankResult(SessionManager session, BankTransferIntent request, bool accepted)
        {
            var state = BuildMessage();
            if (state != null) session.SendWorldMessage(state, Channel.ReliableOrdered);
            session.SendWorldMessage(new BankTransferResult
            {
                PlayerId = request.PlayerId, Sequence = request.Sequence, Accepted = accepted,
            }, Channel.ReliableOrdered);
        }

        private void QueueBankTransfer(float amount)
        {
            var session = SessionManager.Instance;
            if (session == null || session.State != SessionState.Connected || session.IsHost) return;
            if (!BankTransferPolicy.IsFinite(amount) || amount < short.MinValue || amount > short.MaxValue
                || amount != (short)amount || !BankTransferPolicy.IsValidAmount((short)amount)
                || _bankPending.Count >= 16)
            {
                session.AddSystemChat("* ATM transfer unavailable. Try again after the pending transfer finishes.");
                return;
            }
            _bankPending.Enqueue(new BankTransferIntent
            {
                PlayerId = session.LocalPlayerId, Sequence = ++_bankSequence, Amount = (short)amount,
            });
            if (_bankPending.Count == 1) _nextBankRetry = 0f;
        }

        private void LocateBanking()
        {
            SyncCatalog.EnsureLoaded();
            var bindings = SyncCatalog.Banking;
            if (bindings == null) return;
            if (_bankData != null && _atm != null && _cashTrigger != null
                && _bankHooks.Count == bindings.Mutations.Count) return;
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                try
                {
                    string path = ScenePath.Of(fsm.transform);
                    if (path == bindings.BankPath && fsm.FsmName == bindings.BankFsm) _bankData = fsm;
                    else if (path == bindings.AtmPath && fsm.FsmName == bindings.AtmFsm) _atm = fsm;
                    else if (path == bindings.CashPath && fsm.FsmName == bindings.CashFsm) _cashTrigger = fsm;
                }
                catch (MissingReferenceException) { }
            }
            foreach (var mutation in bindings.Mutations)
            {
                var target = mutation.Target == "atm" ? _atm : _cashTrigger;
                if (target == null) continue;
                Action? transfer = null;
                if (mutation.Direction != 0f)
                    transfer = () =>
                    {
                        var amount = target.FsmVariables.FindFsmFloat(mutation.AmountVariable);
                        if (amount != null) QueueBankTransfer(amount.Value * mutation.Direction);
                    };
                HookBankMutation(target, mutation.State, mutation.ActionType,
                    mutation.Balance == "cash" ? bindings.CashGlobal : bindings.BankGlobal, transfer);
            }
        }

        private void HookBankMutation(PlayMakerFSM fsm, string stateName, string type,
            string variableName, Action? transfer)
        {
            var state = FsmHook.FindState(fsm, stateName);
            if (state == null) return;
            foreach (var existing in _bankHooks)
                if (existing.Fsm == fsm && ReferenceEquals(existing.State, state)) return;
            FsmStateAction[] actions;
            try { actions = state.Actions; }
            catch { return; } // inactive FSM has not Awakened; probe again later
            if (actions == null) return;
            foreach (var action in actions)
            {
                if (action == null || action.GetType().Name != type) continue;
                var field = action.GetType().GetField("floatVariable");
                var variable = field != null ? field.GetValue(action) as FsmFloat : null;
                if (variable == null || variable.Name != variableName) continue;
                var hook = new BankHook { Fsm = fsm, State = state, Mutation = action, Enabled = action.Enabled };
                hook.Callback = new FsmHookAction(() =>
                {
                    var session = SessionManager.Instance;
                    bool guest = session != null && session.State == SessionState.Connected && !session.IsHost;
                    hook.Mutation.Enabled = hook.Enabled && !guest;
                    if (guest && hook.Enabled && !_bankFailed && transfer != null) transfer();
                });
                // This hook gates only the one money mutation. The ATM's card,
                // buttons, note animations and receipt flow remain locally usable.
                var expanded = new FsmStateAction[actions.Length + 1];
                expanded[0] = hook.Callback;
                Array.Copy(actions, 0, expanded, 1, actions.Length);
                state.Actions = expanded;
                _bankHooks.Add(hook);
                return;
            }
            WinterMPPlugin.Log.LogWarning("ATM sync: missing " + type + " for " + variableName + " in " + stateName + ".");
        }

        private void RecordBankEntry(short amount)
        {
            if (_bankData == null) return;
            try
            {
                var description = _bankData.FsmVariables.FindFsmString("DataDescription");
                var value = _bankData.FsmVariables.FindFsmString("DataValue");
                if (description == null || value == null) return;
                description.Value = amount > 0 ? "WinterMP ATM deposit" : "WinterMP ATM withdrawal";
                value.Value = amount.ToString("+0;-0", CultureInfo.InvariantCulture);
                _bankData.SendEvent("UPDATE");
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("ATM statement entry failed: " + e.Message); }
        }

        private void ResetBanking()
        {
            _bankAccrual.Restore();
            foreach (var hook in _bankHooks)
            {
                if (hook.Fsm == null) continue;
                try
                {
                    hook.Mutation.Enabled = hook.Enabled;
                    var actions = new List<FsmStateAction>(hook.State.Actions);
                    actions.Remove(hook.Callback);
                    hook.State.Actions = actions.ToArray();
                }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("ATM hook cleanup: " + e.Message); }
            }
            _bankHooks.Clear();
            _bankPending.Clear();
            _bankLedger.Clear();
            _bankRequestTimes.Clear();
            _bankData = _atm = _cashTrigger = null;
            _nextBankProbe = _nextBankRetry = 0f;
            _bankSequence = 0;
            _bankFailed = false;
        }
    }
}
