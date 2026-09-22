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
        private TaxiFareBinding? _fare;
        private TaxiFareState? _remoteFare;
        private bool _fareFailed;
        private float _fareProbeAt, _fareTickAt, _fareKeepAt;
        private uint _fareSequence;
        private void ClearFare()
        {
            _fare?.Restore(); _fare = null; _remoteFare = null; _fareFailed = false;
            _fareProbeAt = _fareTickAt = _fareKeepAt = 0; _fareSequence = 0;
        }
        private void EnsureFare(SessionManager session)
        {
            if (_fare != null || _fareFailed || _meterFailed || Time.unscaledTime < _fareProbeAt) return;
            _fareProbeAt = Time.unscaledTime + 5;
            EnsureService(session); if (_serviceFailed || _service == null) return;
            EnsureMeter(session);
            if (_meter != null) _fare = TaxiFareBinding.TryBind(session, _meter, SendFareIntent);
        }
        private void UpdateFare(SessionManager session)
        {
            if (_fareFailed) return;
            try
            {
                EnsureFare(session); if (_fare == null) return;
                if (!session.IsHost) { if (_remoteFare != null) _fare.Present(_remoteFare); return; }
                if (Time.unscaledTime < _fareTickAt) return;
                _fareTickAt = Time.unscaledTime + .1f;
                bool keep = Time.unscaledTime >= _fareKeepAt; if (keep) _fareKeepAt = Time.unscaledTime + 2;
                var state = _fare.Capture(); if (_fare.ShouldSend(state, keep)) session.SendWorldMessage(state, Channel.ReliableOrdered);
            }
            catch (Exception e) { FareFailed(e); }
        }
        public TaxiFareState? BuildFareSnapshot()
        {
            var session = SessionManager.Instance;
            if (_fareFailed || session == null || !session.IsHost) return null;
            try { EnsureFare(session); return _fare?.Capture(); }
            catch (Exception e) { FareFailed(e); return null; }
        }
        public void ReceiveFare(TaxiFareState state)
        {
            var session = SessionManager.Instance;
            if (_fareFailed || session == null || session.IsHost || !TaxiFarePolicy.Valid(state)
                || (_remoteFare != null && !TaxiServicePolicy.Newer(_remoteFare.Revision, state.Revision))) return;
            _remoteFare = state;
            try { EnsureFare(session); _fare?.Present(state); } catch (Exception e) { FareFailed(e); }
        }
        private void SendFareIntent(TaxiFareAction action)
        {
            var session = SessionManager.Instance;
            if (_fareFailed || session == null || session.IsHost || _remoteFare == null || _remoteFare.FareId == 0) return;
            _fareSequence = unchecked(_fareSequence + 1); if (_fareSequence == 0) _fareSequence = 1;
            session.SendWorldMessage(new TaxiFareIntent { PlayerId = session.LocalPlayerId, Sequence = _fareSequence,
                FareId = _remoteFare.FareId, ExpectedControlRevision = _remoteFare.ControlRevision, Action = action }, Channel.ReliableOrdered);
        }
        public void ReceiveFareIntent(TaxiFareIntent intent, byte actor)
        {
            var session = SessionManager.Instance;
            if (_fareFailed || session == null || !session.IsHost) return;
            try { EnsureFare(session); if (_fare == null) return; _fare.Act(session, intent, actor);
                session.SendWorldMessage(_fare.Capture(), Channel.ReliableOrdered); }
            catch (Exception e) { FareFailed(e); }
        }
        public void ForgetFareIntents(byte actor) { _fare?.Forget(actor); }
        private void FareFailed(Exception e)
        {
            _fareFailed = true; _fare?.DisableGuestInput();
            WinterMPPlugin.Log.LogError("Shared taxi fare unavailable: " + e);
        }
    }
}
