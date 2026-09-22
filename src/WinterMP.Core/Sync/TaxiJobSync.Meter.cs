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
        private TaxiMeterBinding? _meter;
        private TaxiMeterState? _remoteMeter;
        private bool _meterFailed;
        private float _meterProbeAt, _meterTickAt;
        private uint _meterSequence;

        private void ClearMeter()
        {
            ClearFare();
            _meter?.Restore(); _meter = null; _remoteMeter = null; _meterFailed = false;
            _meterProbeAt = _meterTickAt = 0; _meterSequence = 0;
        }
        private void EnsureMeter(SessionManager session)
        {
            if (_meter != null || _meterFailed || Time.unscaledTime < _meterProbeAt) return;
            _meterProbeAt = Time.unscaledTime + 5;
            _meter = TaxiMeterBinding.TryBind(session, SendMeterIntent);
        }
        private void UpdateMeter(SessionManager session)
        {
            if (_meterFailed) return;
            try
            {
                EnsureMeter(session);
                if (_meter == null) return;
                if (!session.IsHost) { if (_remoteMeter != null) _meter.Present(_remoteMeter); return; }
                if (Time.unscaledTime < _meterTickAt) return;
                _meterTickAt = Time.unscaledTime + .25f;
                session.SendWorldMessage(_meter.Capture(), Channel.ReliableOrdered);
            }
            catch (Exception e) { MeterFailed(e); }
        }
        public TaxiMeterState? BuildMeterSnapshot()
        {
            var session = SessionManager.Instance;
            if (_meterFailed || session == null || !session.IsHost) return null;
            try { EnsureMeter(session); return _meter?.Capture(); }
            catch (Exception e) { MeterFailed(e); return null; }
        }
        public void ReceiveMeter(TaxiMeterState state)
        {
            var session = SessionManager.Instance;
            if (_meterFailed || session == null || session.IsHost || !TaxiMeterPolicy.Valid(state)
                || (_remoteMeter != null && !TaxiServicePolicy.Newer(_remoteMeter.Revision, state.Revision))) return;
            _remoteMeter = state;
            try { EnsureMeter(session); _meter?.Present(state); }
            catch (Exception e) { MeterFailed(e); }
        }
        private void SendMeterIntent(TaxiMeterAction action)
        {
            var session = SessionManager.Instance;
            if (_meterFailed || session == null || session.IsHost || _remoteMeter == null) return;
            _meterSequence = unchecked(_meterSequence + 1); if (_meterSequence == 0) _meterSequence = 1;
            session.SendWorldMessage(new TaxiMeterIntent { PlayerId = session.LocalPlayerId, Sequence = _meterSequence,
                ExpectedControlRevision = _remoteMeter.ControlRevision, Action = action }, Channel.ReliableOrdered);
        }
        public void ReceiveMeterIntent(TaxiMeterIntent intent, byte actor)
        {
            var session = SessionManager.Instance;
            if (_meterFailed || session == null || !session.IsHost) return;
            try
            {
                EnsureMeter(session); if (_meter == null) return;
                _meter.Act(session, intent, actor);
                session.SendWorldMessage(_meter.Capture(), Channel.ReliableOrdered);
            }
            catch (Exception e) { MeterFailed(e); }
        }
        public void ForgetMeterIntents(byte actor) { _meter?.Forget(actor); }
        private void MeterFailed(Exception e)
        {
            _meterFailed = true;
            WinterMPPlugin.Log.LogError("Shared taxi meter unavailable: " + e);
        }
    }
}
