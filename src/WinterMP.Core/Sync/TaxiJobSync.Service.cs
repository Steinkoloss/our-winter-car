using System;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiJobSync
    {
        private TaxiServiceBinding? _service;
        private TaxiServiceState? _remoteService;
        private bool _serviceFailed;
        private float _serviceProbeAt, _serviceTickAt, _serviceKeepAliveAt;

        private void ClearService()
        {
            _service?.Restore(); _service = null; _remoteService = null; _serviceFailed = false;
            _serviceProbeAt = _serviceTickAt = _serviceKeepAliveAt = 0;
        }
        private void EnsureService(SessionManager session)
        {
            if (_service != null || _serviceFailed || Time.unscaledTime < _serviceProbeAt) return;
            _serviceProbeAt = Time.unscaledTime + 5;
            _service = TaxiServiceBinding.TryBind(session, SendCall, SendPaydayRead);
            if (_service != null && _remoteService != null) _service.Present(_remoteService);
        }
        private void UpdateService(SessionManager session)
        {
            if (_serviceFailed) return;
            try
            {
                EnsureService(session);
                if (_service == null) return;
                if (!session.IsHost) { if (_remoteService != null) _service.Present(_remoteService); return; }
                _service.CheckCaller(session);
                if (Time.unscaledTime < _serviceTickAt) return;
                _serviceTickAt = Time.unscaledTime + .1f;
                bool keep = Time.unscaledTime >= _serviceKeepAliveAt;
                if (keep) _serviceKeepAliveAt = Time.unscaledTime + 2;
                var state = _service.Capture();
                if (_service.ShouldSend(state, keep)) session.SendWorldMessage(state, Channel.ReliableOrdered);
            }
            catch (Exception e) { ServiceFailed(e); }
        }
        public TaxiServiceState? BuildServiceSnapshot()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return null;
            try { EnsureService(session); return _service?.Capture(); }
            catch (Exception e) { ServiceFailed(e); return null; }
        }
        public void ReceiveService(TaxiServiceState state)
        {
            var session = SessionManager.Instance;
            if (_serviceFailed || session == null || session.IsHost || !TaxiServicePolicy.Valid(state)
                || (_remoteService != null && !TaxiServicePolicy.Newer(_remoteService.Revision, state.Revision))) return;
            _remoteService = state;
            try { EnsureService(session); _service?.Present(state); }
            catch (Exception e) { ServiceFailed(e); }
        }
        private void SendPaydayRead(uint paydayId)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            session.SendWorldMessage(new TaxiPaydayReadIntent { PlayerId = session.LocalPlayerId, PaydayId = paydayId }, Channel.ReliableOrdered);
        }
        public void ReceivePaydayRead(TaxiPaydayReadIntent intent, byte actor)
        {
            var session = SessionManager.Instance;
            if (_serviceFailed || session == null || !session.IsHost) return;
            try
            {
                EnsureService(session);
                if (_service == null) return;
                _service.ReadPayday(session, intent, actor);
                session.SendWorldMessage(_service.Capture(), Channel.ReliableOrdered);
            }
            catch (Exception e) { ServiceFailed(e); }
        }
        private void SendCall(TaxiCallAction action)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || _remoteService == null || _remoteService.CallId == 0) return;
            session.SendWorldMessage(new TaxiCallIntent { PlayerId = session.LocalPlayerId, CallId = _remoteService.CallId, Action = action }, Channel.ReliableOrdered);
        }
        public void ReceiveCall(TaxiCallIntent intent, byte actor)
        {
            var session = SessionManager.Instance;
            if (_serviceFailed || session == null || !session.IsHost) return;
            try
            {
                EnsureService(session);
                if (_service == null) return;
                _service.Act(session, intent, actor);
                session.SendWorldMessage(_service.Capture(), Channel.ReliableOrdered);
            }
            catch (Exception e) { ServiceFailed(e); }
        }
        public void ForgetCaller(byte actor)
        {
            try { _service?.ForgetCaller(actor); }
            catch (Exception e) { ServiceFailed(e); }
        }
        private void ServiceFailed(Exception e)
        {
            _serviceFailed = true;
            // Keep guest decisions paused on a presentation failure until session cleanup.
            // Resuming its independent job here could create another fare or payout.
            WinterMPPlugin.Log.LogError("Shared taxi service unavailable: " + e);
        }
    }
}
