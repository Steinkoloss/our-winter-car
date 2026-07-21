using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Electricity + phone bill ledger and the resulting blackout as host-owned world state
    /// (COVERAGE-ROADMAP 1.3). Bills accrue on the shared host clock and, unpaid, flip
    /// <c>MainSwitch</c> off — killing home lights/heating/appliances (the winter-survival
    /// loop). The whole meter FSM runs per-client, so without this each home disagrees on
    /// what's owed and whether the power is on.
    ///
    /// The <b>host</b> owns the ledger: it reads each meter's unpaid total + power/line state
    /// and broadcasts <see cref="UtilityBillState"/> on change + keepalive + join; guests
    /// write those back onto their local FSM so both homes black out together. Paying a bill
    /// goes through the normal host purchase path (the Pay buttons are catalogued buys), so
    /// the shared wallet is debited once and the host's ledger — re-broadcast here — is the
    /// authority a guest reconciles to.
    /// </summary>
    internal sealed class UtilityBillSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;

        private sealed class Meter
        {
            public byte Kind;
            public string ContainerPath = string.Empty;
            public bool IsElectricity;
            public bool LoggedFound;

            public PlayMakerFSM? Data;
            public FsmFloat? UnpaidBills;
            public FsmBool? PowerBool;   // electricity MainSwitch / phone PhonePaid

            public bool HasLast;
            public byte LastFlags;
            public int LastUnpaid;

            public bool Ready => Data != null && UnpaidBills != null;
        }

        private readonly List<Meter> _meters = new List<Meter>();
        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;

        public void Clear()
        {
            _meters.Clear();
            _built = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            EnsureBuilt();

            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                for (int i = 0; i < _meters.Count; i++) Locate(_meters[i]);
            }

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;

            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;

            for (int i = 0; i < _meters.Count; i++)
                HostBroadcastIfChanged(session, _meters[i], keepAlive);
        }

        /// <summary>Host: re-broadcast every meter on the next tick (bills aren't in the join snapshot).</summary>
        public void ForceBroadcast()
        {
            _nextHostTickAt = 0f;
            _nextKeepAliveAt = 0f;
        }

        // ---- Guest: apply host ledger ----------------------------------------

        public void Apply(UtilityBillState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            EnsureBuilt();
            Meter? meter = FindByKind(message.Meter);
            if (meter == null) return;
            Locate(meter);
            if (!meter.Ready) return;

            try
            {
                if (meter.UnpaidBills != null) meter.UnpaidBills.Value = message.UnpaidBills;
                if (meter.PowerBool != null) meter.PowerBool.Value = message.PowerOn;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("UtilityBillSync: apply failed for " + meter.ContainerPath + ": " + e.Message);
            }
        }

        // ---- Host broadcast ---------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, Meter meter, bool keepAlive)
        {
            Locate(meter);
            if (!meter.Ready) return;

            byte flags = 0;
            int unpaid;
            try
            {
                if (meter.PowerBool == null || meter.PowerBool.Value) flags |= UtilityBillState.FlagPowerOn;
                unpaid = Mathf.RoundToInt(meter.UnpaidBills!.Value);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("UtilityBillSync: read failed for " + meter.ContainerPath + ": " + e.Message);
                return;
            }

            bool changed = !meter.HasLast || meter.LastFlags != flags || meter.LastUnpaid != unpaid;
            if (!changed && !keepAlive) return;

            meter.HasLast = true;
            meter.LastFlags = flags;
            meter.LastUnpaid = unpaid;

            session.SendWorldMessage(
                new UtilityBillState
                {
                    Meter = meter.Kind,
                    UnpaidBills = unpaid,
                    Flags = flags,
                },
                Channel.ReliableOrdered);
        }

        // ---- Discovery --------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            Add(UtilityBillState.MeterElectricity1, "Systems/ElectricityBills1", true);
            Add(UtilityBillState.MeterElectricity2, "Systems/ElectricityBills2", true);
            Add(UtilityBillState.MeterPhone1, "Systems/PhoneBills1", false);
            Add(UtilityBillState.MeterPhone2, "Systems/PhoneBills2", false);
        }

        private void Add(byte kind, string containerPath, bool electricity)
        {
            _meters.Add(new Meter { Kind = kind, ContainerPath = containerPath, IsElectricity = electricity });
        }

        private Meter? FindByKind(byte kind)
        {
            for (int i = 0; i < _meters.Count; i++)
                if (_meters[i].Kind == kind) return _meters[i];
            return null;
        }

        private void Locate(Meter meter)
        {
            if (meter.Ready) return;

            GameObject? go;
            try { go = GameObject.Find(meter.ContainerPath); }
            catch { return; }
            if (go == null) return;

            if (meter.Data == null)
            {
                foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                {
                    if (fsm != null && fsm.FsmName == "Data") { meter.Data = fsm; break; }
                }
            }
            if (meter.Data == null) return;

            if (meter.UnpaidBills == null) meter.UnpaidBills = meter.Data.FsmVariables.FindFsmFloat("UnpaidBills");
            if (meter.PowerBool == null)
                meter.PowerBool = meter.Data.FsmVariables.FindFsmBool(meter.IsElectricity ? "MainSwitch" : "PhonePaid");

            if (!meter.LoggedFound && meter.Ready)
            {
                meter.LoggedFound = true;
                WinterMPPlugin.Log.LogInfo($"UtilityBillSync: located meter '{meter.ContainerPath}'.");
            }
        }
    }
}
