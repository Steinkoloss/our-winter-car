using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Taxi job scalar snapshots, shared calls/customer presentation and native
    /// host pickup, meter calculation and physical receipt handoff for guest drivers.
    /// Shared luggage and native payday reports complete the outputs. Customer Cost is the finalized offer;
    /// Tripmeter.Price is the live meter, IncomeTotal records completed fares,
    /// and Payments later settles wages into the bank (native build 23268598).
    /// </summary>
    internal sealed partial class TaxiJobSync
    {
        private const string LogicPath = "JOBS/TAXIJOB";
        private const string CustomerWalkerPath = "JOBS/TAXIJOB/Customer1/TaxiWalker";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;

        private PlayMakerFSM? _logic;      // JOBS/TAXIJOB :: Logic
        private PlayMakerFSM? _payments;   // TaxiFunctions :: Payments
        // Kept as the finalized customer offer to preserve message 103's contract.
        private FsmFloat? _fareCost;
        private FsmInt? _jobStage;
        private FsmFloat? _money;
        private FsmFloat? _kms;
        private FsmBool? _employed;
        private bool _loggedFound;
        private TaxiPickupBinding? _pickup;
        private bool _pickupFailed;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;

        private bool _hasLast;
        private int _lastStage;
        private int _lastMoney;
        private int _lastFare;
        private byte _lastFlags;

        private bool Ready => _logic != null && _jobStage != null;

        public void Clear()
        {
            ClearMeter();
            ClearService();
            _pickup?.Restore(); _pickup = null; _pickupFailed = false;
            _logic = _payments = null;
            _jobStage = null; _money = _kms = _fareCost = null; _employed = null;
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
            _hasLast = false;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0)
            {
                // Hosting continues after the last peer leaves; release its driver
                // context even though there is nobody to receive snapshots.
                if (session.IsHost)
                {
                    try { _pickup?.Update(session); } catch (System.Exception e) { PickupFailed(e); }
                    try { _service?.CheckCaller(session); } catch (System.Exception e) { ServiceFailed(e); }
                }
                return;
            }

            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                Locate();
                if (session.IsHost && _pickup == null && !_pickupFailed)
                {
                    try { _pickup = TaxiPickupBinding.TryBind(); }
                    catch (System.Exception e) { PickupFailed(e); }
                }
            }

            UpdateService(session);
            UpdateMeter(session);
            UpdateFare(session);
            if (!session.IsHost) return;
            try { _pickup?.Update(session); }
            catch (System.Exception e) { PickupFailed(e); }
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        private void PickupFailed(System.Exception e)
        {
            _pickupFailed = true;
            try { _pickup?.Restore(); }
            finally { _pickup = null; }
            WinterMPPlugin.Log.LogError("Guest taxi pickup unavailable; native host pickup retained: " + e);
        }

        public TaxiJobState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(TaxiJobState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            try
            {
                if (_jobStage != null) _jobStage.Value = message.JobStage;
                if (_money != null) _money.Value = message.Money;
                if (_kms != null) _kms.Value = message.KMsDriven;
                if (_employed != null) _employed.Value = message.Employed;
                if (_fareCost != null) _fareCost.Value = Mathf.Max(0f, message.FareCost);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("TaxiJobSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            Locate();
            if (!Ready) return;
            var state = BuildState();
            if (state == null) return;

            bool changed = !_hasLast || _lastStage != state.JobStage
                || _lastMoney != Mathf.RoundToInt(state.Money) || _lastFlags != state.Flags
                || _lastFare != Mathf.RoundToInt(state.FareCost);
            if (!changed && !keepAlive) return;

            _hasLast = true;
            _lastStage = state.JobStage;
            _lastMoney = Mathf.RoundToInt(state.Money);
            _lastFare = Mathf.RoundToInt(state.FareCost);
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private TaxiJobState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_employed != null && _employed.Value) flags |= TaxiJobState.FlagEmployed;
            return new TaxiJobState
            {
                Sequence = ++_outSequence,
                JobStage = _jobStage!.Value,
                Money = _money != null ? _money.Value : 0f,
                KMsDriven = _kms != null ? _kms.Value : 0f,
                Flags = flags,
                FareCost = _fareCost != null ? _fareCost.Value : 0f,
            };
        }

        private void Locate()
        {
            if (Ready && _payments != null && _fareCost != null) return;

            if (_fareCost == null)
            {
                GameObject? walker;
                try { walker = GameObject.Find(CustomerWalkerPath); }
                catch { walker = null; }
                if (walker != null)
                {
                    foreach (var fsm in walker.GetComponents<PlayMakerFSM>())
                    {
                        if (fsm != null && fsm.FsmName == "Logic")
                        {
                            _fareCost = fsm.FsmVariables.FindFsmFloat("Cost");
                            break;
                        }
                    }
                }
            }

            if (_logic == null)
            {
                GameObject? go;
                try { go = GameObject.Find(LogicPath); }
                catch { return; }
                if (go != null)
                {
                    foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                    {
                        if (fsm != null && fsm.FsmName == "Logic") { _logic = fsm; break; }
                    }
                    if (_logic != null) _jobStage = _logic.FsmVariables.FindFsmInt("JobStage");
                }
            }

            if (_payments == null)
            {
                var fsms = ScenePath.ScanFsms();
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || fsm.FsmName != "Payments") continue;
                    try
                    {
                        if (fsm.gameObject.name != "TaxiFunctions") continue;
                        _payments = fsm;
                        _money = fsm.FsmVariables.FindFsmFloat("Money");
                        _kms = fsm.FsmVariables.FindFsmFloat("KMsDriven");
                        _employed = fsm.FsmVariables.FindFsmBool("Employed");
                        break;
                    }
                    catch { /* destroyed-but-listed */ }
                }
            }

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("TaxiJobSync: located taxi job.");
            }
        }
    }
}
