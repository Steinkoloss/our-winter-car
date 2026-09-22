using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class UtilityBillSync
    {
        private sealed class BillPayment
        {
            internal UtilityPaymentsData? Config;
            internal PlayMakerFSM? Sheet, Pay;
            internal FsmFloat? Cash, Total;
            internal FsmString? TotalString;
            internal TextMesh? TotalText;
            internal FsmState? RequestState, CommitState;
            internal FsmStateAction[]? RequestActions, CommitActions;
            internal readonly FsmSuppressor Failure = new FsmSuppressor();
            internal bool Ready, Failed, Displayed;
            internal uint DisplayedRevision;
            internal ushort Sequence;
            internal UtilityPaymentIntent? Pending;
            internal float ProbeAt, RetryAt;
            internal FsmFloat[]? PhoneRates;
            internal TextMesh[]? PhoneDetails;
            internal FsmBool? OldBill;
            internal FsmState? CalculateState;
            internal FsmStateAction[]? CalculateActions;
        }

        private sealed class PaymentRequestAction : FsmStateAction
        {
            private readonly Action _request;
            internal PaymentRequestAction(Action request) { _request = request; }
            // Keep Check money waiting until the host returns its receipt.
            public override void OnEnter() { _request(); }
        }

        private void UpdatePayment(SessionManager session, Meter meter)
        {
            if (!meter.Ready || meter.Payment.Failed) return;
            var payment = meter.Payment;
            try
            {
                if (!payment.Ready && Time.unscaledTime >= payment.ProbeAt)
                {
                    payment.ProbeAt = Time.unscaledTime + 2;
                    LocatePayment(meter);
                }
                if (!payment.Ready) return;
                if (session.IsHost && meter.Settled)
                    ObserveInvoice(meter);
                RenderPayment(session, meter);
                if (payment.Pending == null || Time.unscaledTime < payment.RetryAt) return;
                payment.RetryAt = Time.unscaledTime + 1;
                if (session.IsHost) OnPayment(payment.Pending);
                else session.SendWorldMessage(payment.Pending, Channel.ReliableOrdered);
            }
            catch (Exception e) { FailPayment(meter, e); }
        }

        private void QueuePayment(Meter meter)
        {
            try
            {
                var payment = meter.Payment;
                var session = SessionManager.Instance;
                if (session == null || !payment.Ready || payment.Failed || payment.Pending != null) return;
                if (!payment.Displayed)
                {
                    session.AddSystemChat("* Waiting for the host's utility bill. Reopen the envelope shortly.");
                    EnterPayment(payment, "close"); return;
                }
                payment.Pending = new UtilityPaymentIntent { PlayerId = session.LocalPlayerId, Meter = meter.Kind,
                    Sequence = ++payment.Sequence, Revision = payment.DisplayedRevision };
                payment.RetryAt = 0;
            }
            catch (Exception e) { FailPayment(meter, e); }
        }

        public void OnPayment(UtilityPaymentIntent request)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || request.Meter > 3 || request.PlayerId == 255) return;
            EnsureBuilt();
            var meter = _meters[request.Meter]; var payment = meter.Payment;
            if (!payment.Ready || payment.Failed || !meter.Settled) return;
            try
            {
                ObserveInvoice(meter);
                if (!meter.Ledger.TryReceipt(request, out var result))
                {
                    bool nearby = GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)
                        && (position - meter.Bill!.transform.position).sqrMagnitude <= 36f;
                    result = meter.Ledger.Apply(request, payment.Cash!.Value, nearby, out float cash);
                    if (result.Result == UtilityPaymentResult.Accepted)
                    {
                        payment.Cash.Value = cash;
                        // Native Pay bills clears debt/cutoff timing, hides the envelope
                        // and restores supply without opening the host's menu.
                        meter.Data!.SendEvent(payment.Config!["payEvent"]);
                    }
                    SyncEventLog.Record("utility-payment", "meter " + meter.Kind + " player " + request.PlayerId
                        + " seq " + request.Sequence + " quote " + request.Revision + " result " + result.Result + " paid " + result.Paid);
                }
                HostBroadcastIfChanged(session, meter, true);
                var wallet = WorldSyncManager.Instance?.BuildWalletState();
                if (wallet != null) session.SendWorldMessage(wallet, Channel.ReliableOrdered);
                session.SendWorldMessage(result, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnPaymentResult(result);
            }
            catch (Exception e) { FailPayment(meter, e); }
        }

        public void OnPaymentResult(UtilityPaymentResult result)
        {
            var session = SessionManager.Instance;
            if (session == null || result.PlayerId != session.LocalPlayerId || result.Meter > 3) return;
            EnsureBuilt();
            var meter = _meters[result.Meter]; var payment = meter.Payment;
            if (payment.Pending == null || payment.Pending.Sequence != result.Sequence) return;
            payment.Pending = null;
            try
            {
                if (result.Result == UtilityPaymentResult.Accepted) EnterPayment(payment, "commit");
                else if (result.Result == UtilityPaymentResult.Funds) EnterPayment(payment, "funds");
                else
                {
                    EnterPayment(payment, "close");
                    session.AddSystemChat(result.Result == UtilityPaymentResult.Changed
                        ? "* The utility bill changed. Reopen it to review the current total."
                        : "* Utility payment declined. Return to the unpaid envelope to pay it.");
                }
            }
            catch (Exception e) { FailPayment(meter, e); }
        }

        public void ForgetPlayer(byte player)
        {
            foreach (var meter in _meters) meter.Ledger.ForgetPlayer(player);
        }

        private static void RenderPayment(SessionManager session, Meter meter)
        {
            var p = meter.Payment;
            if (p.Sheet == null || !p.Sheet.gameObject.activeInHierarchy) { p.Displayed = false; return; }
            if (!session.IsHost && meter.Received == null) { p.Displayed = false; return; }
            var state = session.IsHost ? ObserveInvoice(meter) : meter.Received!;
            float debt = state.PaymentTotal;
            if (state.Phone != null) RenderPhone(p, state.Phone);
            bool visible = state.BillVisible;
            p.Total!.Value = debt;
            string text = debt.ToString("0.00");
            p.TotalString!.Value = text; p.TotalText!.text = text;
            p.Displayed = true;
            p.DisplayedRevision = session.IsHost ? meter.Ledger.Revision : meter.Received!.Revision;
            if ((!visible || debt <= 0) && p.Pending == null) EnterPayment(p, "close");
        }

        private static void EnterPayment(BillPayment p, string key)
        {
            if (p.Pay != null && p.Config != null && p.Pay.gameObject.activeInHierarchy)
                FsmHook.FireRemoteEntry(p.Pay, p.Config[key]);
        }

        private static void FailPayment(Meter meter, Exception e)
        {
            var p = meter.Payment;
            if (p.Failed) return;
            p.Failed = true; p.Pending = null;
            try { EnterPayment(p, "close"); }
            catch (Exception closeError) { WinterMPPlugin.Log.LogDebug("Utility menu cleanup: " + closeError.Message); }
            p.Failure.Suppress(p.Pay);
            WinterMPPlugin.Log.LogError("Utility payment disabled for " + meter.ContainerPath + ": " + e);
        }

        private static void ClearPayment(Meter meter)
        {
            var p = meter.Payment;
            p.Failure.Restore();
            if (p.RequestActions != null || p.Pending != null) EnterPayment(p, "close");
            if (p.RequestState != null && p.RequestActions != null) p.RequestState.Actions = p.RequestActions;
            if (p.CommitState != null && p.CommitActions != null) p.CommitState.Actions = p.CommitActions;
            if (p.CalculateState != null && p.CalculateActions != null) p.CalculateState.Actions = p.CalculateActions;
            if (p.OldBill != null)
            {
                p.OldBill.Value = false;
                p.Total!.Value = 0;
            }
        }
    }
}
