using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class WelfareSync
    {
        private readonly DebtPaymentLedger _debtLedger = new DebtPaymentLedger();
        private readonly List<DebtHook> _debtHooks = new List<DebtHook>();
        private readonly Dictionary<byte, float> _debtRequestAt = new Dictionary<byte, float>();
        private readonly FsmSuppressor _debtPaySuppressor = new FsmSuppressor();
        private DebtLetterData? _debtConfig;
        private PlayMakerFSM? _debtRent, _debtSetup, _debtPay;
        private GameObject? _debtEnvelope;
        private FsmFloat? _debtCash, _nativeDebt, _debtInterest, _debtCost1, _debtCost2, _sheetDebt, _payDebtTotal;
        private FsmString? _debtOriginalString, _debtTotalString;
        private TextMesh? _debtOriginalText, _debtTotalText;
        private DebtLetterState? _debtView;
        private DebtPaymentIntent? _debtPending;
        private float _debtProbeAt, _debtSendAt, _debtRetryAt;
        private ushort _debtSequence;
        private uint _debtDisplayedRevision;
        private bool _debtHasDisplayedQuote;
        private bool _debtReady, _debtFailed, _debtHasRevision, _debtReplay;

        private sealed class DebtHook
        {
            public FsmState State = null!;
            public FsmStateAction[] Original = new FsmStateAction[0];
        }
        private sealed class DebtRequestAction : FsmStateAction
        {
            private readonly Action _request;
            public DebtRequestAction(Action request) { _request = request; }
            // Check money must stay open while the host answers. FINISHED would
            // run the vanilla No money transition before an acknowledgment arrives.
            public override void OnEnter() { _request(); }
        }

        private void UpdateDebtLetter(SessionManager session)
        {
            if (_debtFailed) return;
            try
            {
                if (!_debtReady && Time.unscaledTime >= _debtProbeAt)
                {
                    _debtProbeAt = Time.unscaledTime + 2f;
                    LocateDebtLetter();
                }
                if (!_debtReady) return;
                if (session.IsHost && RefreshDebtQuote() && Time.unscaledTime >= _debtSendAt)
                    SendDebtState(session);
                if (_debtPending != null && Time.unscaledTime >= _debtRetryAt)
                {
                    _debtRetryAt = Time.unscaledTime + 1f;
                    if (session.IsHost) OnDebtPayment(_debtPending);
                    else session.SendWorldMessage(_debtPending, Channel.ReliableOrdered);
                }
                RenderDebtQuote();
            }
            catch (Exception e) { DisableDebtLetter(e); }
        }

        private bool RefreshDebtQuote()
        {
            if (_debtRent == null || !_debtRent.Fsm.Started || _nativeDebt == null || _debtInterest == null
                || _debtCost1 == null || _debtCost2 == null || _debtEnvelope == null) return false;
            if (_debtLedger.Observe(_nativeDebt.Value, _debtInterest.Value, _debtCost1.Value, _debtCost2.Value,
                _debtEnvelope.activeSelf)) _debtSendAt = 0;
            _debtView = _debtLedger.Snapshot();
            return _debtView != null;
        }

        public DebtLetterState? BuildDebtSnapshot()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _debtFailed) return null;
            try
            {
                if (!_debtReady) LocateDebtLetter();
                return _debtReady && RefreshDebtQuote() ? _debtLedger.Snapshot() : null;
            }
            catch (Exception e) { DisableDebtLetter(e); return null; }
        }

        public void OnDebtState(DebtLetterState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !DebtPaymentLedger.IsValid(state)) return;
            if (_debtHasRevision && _debtView != null)
            {
                uint delta = state.Revision - _debtView.Revision;
                if (delta == 0 || delta > int.MaxValue) return;
            }
            _debtHasRevision = true; _debtView = state;
        }

        private void QueueDebtPayment()
        {
            try
            {
                var session = SessionManager.Instance;
                if (!_debtReady || _debtFailed || session == null || _debtPending != null) return;
                if (_debtView == null || !_debtView.Available || !_debtHasDisplayedQuote)
                {
                    session.AddSystemChat("* This debt letter is unavailable. Waiting for the host's current bill.");
                    EnterDebtState("closeState");
                    return;
                }
                _debtPending = new DebtPaymentIntent
                    { PlayerId = session.LocalPlayerId, Sequence = ++_debtSequence, Revision = _debtDisplayedRevision };
                _debtRetryAt = 0;
            }
            catch (Exception e) { DisableDebtLetter(e); }
        }

        public void OnDebtPayment(DebtPaymentIntent request)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _debtFailed || !_debtReady || _debtCash == null) return;
            try
            {
                float now = Time.unscaledTime;
                if (_debtRequestAt.TryGetValue(request.PlayerId, out float next) && now < next) return;
                _debtRequestAt[request.PlayerId] = now + .15f;
                if (!RefreshDebtQuote()) return;
                if (!_debtLedger.TryGetReceipt(request, out var result))
                {
                    if (_debtEnvelope == null || !GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)) return;
                    result = _debtLedger.Apply(request, _debtCash.Value,
                        (position - _debtEnvelope.transform.position).sqrMagnitude <= 36f, out float cash);
                    if (result.Result == DebtPaymentResult.Accepted)
                    {
                        _debtCash.Value = cash;
                        if (_nativeDebt != null) _nativeDebt.Value = 0;
                        _debtEnvelope.SetActive(false);
                    }
                    SyncEventLog.Record("debt-payment", "player " + request.PlayerId + " seq " + request.Sequence
                        + " quote " + request.Revision + " result " + result.Result + " paid " + result.Paid);
                }
                if (result.Result == DebtPaymentResult.Stale) return;
                SendDebtState(session);
                var wallet = WorldSyncManager.Instance?.BuildWalletState();
                if (wallet != null) session.SendWorldMessage(wallet, Channel.ReliableOrdered);
                var welfare = BuildSnapshot();
                if (welfare != null) session.SendWorldMessage(welfare, Channel.ReliableOrdered);
                session.SendWorldMessage(result, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnDebtResult(result);
            }
            catch (Exception e) { DisableDebtLetter(e); }
        }

        public void OnDebtResult(DebtPaymentResult result)
        {
            var session = SessionManager.Instance;
            if (session == null || result.PlayerId != session.LocalPlayerId || _debtPending == null
                || result.Sequence != _debtPending.Sequence || result.Result > DebtPaymentResult.Funds) return;
            _debtPending = null; _debtRetryAt = 0;
            try
            {
                if (result.Result == DebtPaymentResult.Accepted)
                {
                    _debtReplay = true;
                    try { EnterDebtState("commitState"); }
                    finally { _debtReplay = false; }
                }
                else if (_debtView == null || !_debtView.Available) EnterDebtState("closeState");
                else if (result.Result == DebtPaymentResult.Funds) EnterDebtState("fundsState");
                else
                {
                    RenderDebtQuote(); EnterDebtState("idleState");
                    session.AddSystemChat(result.Result == DebtPaymentResult.Changed
                        ? "* The debt letter changed. Review the updated total before paying again."
                        : "* Debt payment declined. Return to the letter to pay it.");
                }
            }
            catch (Exception e) { DisableDebtLetter(e); }
        }

        private void SendDebtState(SessionManager session)
        {
            _debtView = _debtLedger.Snapshot();
            if (_debtView != null) session.SendWorldMessage(_debtView, Channel.ReliableOrdered);
            _debtSendAt = Time.unscaledTime + 5f;
        }
        public void ForgetDebtPlayer(byte player) { _debtLedger.ForgetPlayer(player); _debtRequestAt.Remove(player); }

        private void RenderDebtQuote()
        {
            if (!_debtReady || _debtSetup == null) return;
            var session = SessionManager.Instance;
            if (_debtView != null && session != null && !session.IsHost && _debtEnvelope != null)
            {
                if (_debtEnvelope.activeSelf != _debtView.Available) _debtEnvelope.SetActive(_debtView.Available);
            }
            if (!_debtSetup.gameObject.activeInHierarchy) return;
            string original = _debtView != null ? _debtView.Debt.ToString("0") : "...";
            string total = _debtView != null ? _debtView.Total.ToString("0.0") : "...";
            if (_sheetDebt != null) _sheetDebt.Value = _debtView != null ? _debtView.Total : 0;
            if (_payDebtTotal != null) _payDebtTotal.Value = _debtView != null ? _debtView.Total : 0;
            if (_debtOriginalString != null) _debtOriginalString.Value = original;
            if (_debtTotalString != null) _debtTotalString.Value = total;
            if (_debtOriginalText != null) _debtOriginalText.text = original;
            if (_debtTotalText != null) _debtTotalText.text = total;
            _debtHasDisplayedQuote = _debtView != null;
            if (_debtView != null) _debtDisplayedRevision = _debtView.Revision;
            if (_debtView != null && !_debtView.Available && _debtPending == null) EnterDebtState("closeState");
        }

        private void EnterDebtState(string key)
        {
            if (_debtPay == null || _debtConfig == null || !_debtPay.gameObject.activeInHierarchy) return;
            FsmHook.FireRemoteEntry(_debtPay, _debtConfig[key]);
        }
        private void DisableDebtLetter(Exception e)
        {
            if (_debtFailed) return;
            _debtFailed = true;
            _debtPaySuppressor.Suppress(_debtPay);
            WinterMPPlugin.Log.LogError("Debt-letter payments disabled; welfare sync continues: " + e);
        }

        private void LocateDebtLetter()
        {
            if (_debtReady) return;
            SyncCatalog.EnsureLoaded();
            _debtConfig = SyncCatalog.DebtLetter;
            var c = _debtConfig;
            if (c == null || SyncCatalog.Banking == null) return;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (path == c["rentPath"] && fsm.FsmName == c["rentFsm"]) _debtRent = fsm;
                if (path == c["sheetPath"] && fsm.FsmName == c["setupFsm"]) _debtSetup = fsm;
                if (path == c["payPath"] && fsm.FsmName == c["payFsm"]) _debtPay = fsm;
            }
            if (_debtRent == null || _debtSetup == null || _debtPay == null) return;
            foreach (var fsm in new[] { _debtRent, _debtSetup, _debtPay })
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            _debtCash = FsmVariables.GlobalVariables.FindFsmFloat(SyncCatalog.Banking.CashGlobal);
            _nativeDebt = _debtRent.FsmVariables.FindFsmFloat(c["rentDebt"]);
            // Rent moves the entire mailbox on eviction. Its Letter reference survives
            // that reparenting; a hardcoded home path would stop validating payments.
            _debtEnvelope = _debtRent.FsmVariables.FindFsmGameObject(c["rentEnvelope"])?.Value;
            _debtInterest = _debtSetup.FsmVariables.FindFsmFloat(c["interest"]);
            _debtCost1 = _debtSetup.FsmVariables.FindFsmFloat(c["cost1"]);
            _debtCost2 = _debtSetup.FsmVariables.FindFsmFloat(c["cost2"]);
            _sheetDebt = _debtSetup.FsmVariables.FindFsmFloat(c["calculatedDebt"]);
            _debtOriginalString = _debtSetup.FsmVariables.FindFsmString(c["originalText"]);
            _debtTotalString = _debtSetup.FsmVariables.FindFsmString(c["totalText"]);
            _payDebtTotal = _debtPay.FsmVariables.FindFsmFloat(c["payTotal"]);
            if (_debtCash == null || _nativeDebt == null || _debtEnvelope == null || _debtInterest == null
                || _debtCost1 == null || _debtCost2 == null || _sheetDebt == null || _payDebtTotal == null
                || _debtOriginalString == null || _debtTotalString == null)
                throw new InvalidOperationException("Debt letter variables or envelope binding changed.");
            BindDebtActions(c);
            _debtReady = true;
            WinterMPPlugin.Log.LogInfo("Debt-letter host quotes and acknowledged payments ready.");
        }

        private void BindDebtActions(DebtLetterData c)
        {
            if (_debtSetup == null || _debtPay == null || _debtRent == null) return;
            var calculate = FsmHook.FindState(_debtSetup, c["calculateState"]);
            var request = FsmHook.FindState(_debtPay, c["requestState"]);
            var commit = FsmHook.FindState(_debtPay, c["commitState"]);
            if (calculate == null || request == null || commit == null) throw new InvalidOperationException("Debt letter states changed.");
            foreach (string key in new[] { "commitState", "idleState", "fundsState", "closeState" })
                if (!FsmHook.EnsureRemoteEntry(_debtPay, c[key])) throw new InvalidOperationException("Debt letter entry changed: " + key);
            var banking = SyncCatalog.Banking;
            if (banking == null) throw new InvalidOperationException("Missing cash binding.");
            var ca = RequireDebtActions(calculate, "GetFsmFloat", "ConvertFloatToString", "FloatMultiply", "FloatAdd", "FloatAdd", "ConvertFloatToString", "SetProperty", "SetProperty");
            var pa = RequireDebtActions(commit, "MasterAudioPlaySound", "FloatSubtract", "SetFsmFloat", "ActivateGameObject");
            var ra = RequireDebtActions(request, "SetMaterial", "GetFsmFloat", "FloatCompare");
            if (DebtField<FsmString>(ca[0], "variableName").Value != c["rentDebt"]
                || DebtField<FsmString>(ca[0], "fsmName").Value != c["rentFsm"]
                || DebtTarget(_debtSetup, ca[0]) != _debtRent.gameObject
                || DebtField<FsmFloat>(ca[0], "storeValue").Name != c["calculatedDebt"]
                || DebtField<FsmFloat>(ca[2], "floatVariable").Name != c["calculatedDebt"]
                || DebtField<FsmFloat>(ca[2], "multiplyBy").Name != c["interest"]
                || DebtField<FsmFloat>(ca[3], "floatVariable").Name != c["calculatedDebt"]
                || DebtField<FsmFloat>(ca[3], "add").Name != c["cost1"]
                || DebtField<FsmFloat>(ca[4], "floatVariable").Name != c["calculatedDebt"]
                || DebtField<FsmFloat>(ca[4], "add").Name != c["cost2"])
                throw new InvalidOperationException("Native debt calculation no longer matches the catalog.");
            var original = DebtField<FsmProperty>(ca[6], "targetProperty");
            var total = DebtField<FsmProperty>(ca[7], "targetProperty");
            _debtOriginalText = original.TargetObject.Value as TextMesh;
            _debtTotalText = total.TargetObject.Value as TextMesh;
            if (_debtOriginalText == null || _debtTotalText == null
                || original.StringParameter.Name != c["originalText"] || total.StringParameter.Name != c["totalText"])
                throw new InvalidOperationException("Debt letter text binding changed.");
            var clear = DebtField<FsmFloat>(pa[2], "setValue");
            if (DebtField<FsmFloat>(pa[1], "floatVariable").Name != banking.CashGlobal
                || DebtField<FsmFloat>(pa[1], "subtract").Name != c["payTotal"]
                || DebtField<FsmString>(pa[2], "fsmName").Value != c["rentFsm"]
                || DebtField<FsmString>(pa[2], "variableName").Value != c["rentDebt"]
                || clear.Value != 0 || clear.UseVariable || DebtField<FsmBool>(pa[3], "activate").Value
                || DebtTarget(_debtPay, pa[2]) != _debtRent.gameObject
                || DebtTarget(_debtPay, pa[3]) != _debtEnvelope
                || _debtPay.FsmVariables.FindFsmGameObject(c["payDatabase"])?.Value != _debtRent.gameObject
                || _debtPay.FsmVariables.FindFsmGameObject(c["payEnvelope"])?.Value != _debtEnvelope
                || _debtPay.FsmVariables.FindFsmGameObject(c["paySheet"])?.Value != _debtSetup.gameObject)
                throw new InvalidOperationException("Native debt payment no longer matches the catalog.");
            if (DebtTarget(_debtPay, ra[1]) != _debtSetup.gameObject
                || DebtField<FsmString>(ra[1], "fsmName").Value != c["setupFsm"]
                || DebtField<FsmString>(ra[1], "variableName").Value != c["calculatedDebt"]
                || DebtField<FsmFloat>(ra[1], "storeValue").Name != c["payTotal"]
                || DebtField<FsmFloat>(ra[2], "float1").Name != c["payTotal"]
                || DebtField<FsmFloat>(ra[2], "float2").Name != banking.CashGlobal)
                throw new InvalidOperationException("Native debt request no longer matches the catalog.");
            ReplaceDebtActions(calculate, new FsmHookAction(() =>
            {
                try { RenderDebtQuote(); }
                catch (Exception e) { DisableDebtLetter(e); }
            }));
            ReplaceDebtActions(request, new DebtRequestAction(QueueDebtPayment));
            ReplaceDebtActions(commit, new FsmHookAction(() =>
            {
                try { if (_debtReplay) pa[0].OnEnter(); }
                catch (Exception e) { DisableDebtLetter(e); }
            }));
        }

        private static FsmStateAction[] RequireDebtActions(FsmState state, params string[] types)
        {
            var actions = state.Actions;
            if (actions.Length != types.Length) throw new InvalidOperationException("Debt action count changed: " + state.Name);
            for (int i = 0; i < types.Length; i++)
                if (actions[i].GetType().Name != types[i] || !actions[i].Enabled)
                    throw new InvalidOperationException("Debt action changed: " + state.Name + "/" + i);
            return actions;
        }
        private static T DebtField<T>(object action, string field) where T : class
        {
            var member = action.GetType().GetField(field);
            var value = member != null ? member.GetValue(action) as T : null;
            if (value == null) throw new InvalidOperationException("Debt action field changed: " + field);
            return value;
        }
        private static GameObject? DebtTarget(PlayMakerFSM fsm, object action)
            => fsm.Fsm.GetOwnerDefaultTarget(DebtField<FsmOwnerDefault>(action, "gameObject"));

        private void ReplaceDebtActions(FsmState state, FsmStateAction replacement)
        {
            _debtHooks.Add(new DebtHook { State = state, Original = state.Actions });
            state.Actions = new[] { replacement };
        }

        private void ClearDebtLetter()
        {
            _debtPaySuppressor.Restore();
            // Close an awaiting Check money before restoring its vanilla actions;
            // otherwise a resumed UI could debit after the network session ended.
            if (_debtHooks.Count > 0 || _debtPending != null)
            {
                try { EnterDebtState("closeState"); }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("Debt menu cleanup: " + e.Message); }
            }
            foreach (var hook in _debtHooks)
            {
                try { hook.State.Actions = hook.Original; }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("Debt hook cleanup: " + e.Message); }
            }
            _debtHooks.Clear(); _debtLedger.Clear(); _debtRequestAt.Clear();
            _debtConfig = null; _debtRent = _debtSetup = _debtPay = null; _debtEnvelope = null;
            _debtCash = _nativeDebt = _debtInterest = _debtCost1 = _debtCost2 = _sheetDebt = _payDebtTotal = null;
            _debtOriginalString = _debtTotalString = null; _debtOriginalText = _debtTotalText = null;
            _debtView = null; _debtPending = null;
            _debtReady = _debtFailed = _debtHasRevision = _debtReplay = false;
            _debtHasDisplayedQuote = false; _debtDisplayedRevision = 0;
            _debtProbeAt = _debtSendAt = _debtRetryAt = 0; _debtSequence = 0;
        }
    }
}
