using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Catalog;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>Host-owned utility ledger and effective electricity supply.</summary>
    internal sealed partial class UtilityBillSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 2f;
        private const float KeepAliveSeconds = 20f;

        private sealed class Meter
        {
            internal byte Kind;
            internal string ContainerPath = string.Empty;
            internal bool IsElectricity, LoggedFound, Failed;
            internal PlayMakerFSM? Data;
            internal FsmFloat? UnpaidBills;
            internal FsmBool? PowerBool, MainSwitch;
            internal GameObject? Bill;
            internal UtilityBillState? Sent, Received;
            internal readonly UtilityPaymentLedger Ledger = new UtilityPaymentLedger();
            internal readonly BillPayment Payment = new BillPayment();
            internal readonly FsmSuppressor Pause = new FsmSuppressor();
            internal bool Saved, OriginalPower, OriginalSwitch, OriginalBill;
            internal float OriginalUnpaid;
            internal FsmFloat[]? PhoneUsage;
            internal PhoneBillQuote? ObservedPhone;
            internal float[]? OriginalPhoneUsage;
            internal bool Ready => Data != null && !Failed && UnpaidBills != null && PowerBool != null
                && Bill != null && (IsElectricity ? MainSwitch != null : PhoneUsage != null);
            internal bool Settled => Ready && Data!.Fsm.Initialized && Data.Fsm.Started
                && (Data.ActiveStateName == "Wait" || Data.ActiveStateName == "State 2" || Data.ActiveStateName == "Cut off");
        }

        private readonly List<Meter> _meters = new List<Meter>();
        private bool _built;
        private float _nextProbeAt, _nextHostTickAt, _nextKeepAliveAt;

        public void Clear()
        {
            foreach (var meter in _meters)
            {
                try
                {
                    ClearPayment(meter);
                    if (meter.Saved && meter.Data != null)
                    {
                        meter.UnpaidBills!.Value = meter.OriginalUnpaid;
                        meter.PowerBool!.Value = meter.OriginalPower;
                        if (meter.MainSwitch != null) meter.MainSwitch.Value = meter.OriginalSwitch;
                        if (meter.OriginalPhoneUsage != null && meter.PhoneUsage != null)
                            for (int i = 0; i < 4; i++) meter.PhoneUsage[i].Value = meter.OriginalPhoneUsage[i];
                        if (meter.Bill != null) meter.Bill.SetActive(meter.OriginalBill);
                    }
                }
                catch (Exception e) { Warn(meter, "restore", e); }
                finally { meter.Pause.Restore(); }
            }
            _meters.Clear(); _built = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
        }

        public void Update(SessionManager session)
        {
            if (!session.IsHost && session.PlayerCount == 0) return;
            EnsureBuilt();
            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                foreach (var meter in _meters) Locate(meter);
            }
            if (!session.IsHost)
            {
                foreach (var meter in _meters) ApplyGuest(meter);
                foreach (var meter in _meters) UpdatePayment(session, meter);
                return;
            }
            foreach (var meter in _meters) UpdatePayment(session, meter);
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            foreach (var meter in _meters) HostBroadcastIfChanged(session, meter, keepAlive);
        }

        public void ForceBroadcast() { _nextHostTickAt = _nextKeepAliveAt = 0f; }

        public void Apply(UtilityBillState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !UtilityBillPolicy.Valid(message)) return;
            EnsureBuilt();
            var meter = _meters[message.Meter];
            if (meter.Received != null && message.Revision != meter.Received.Revision
                && message.Revision - meter.Received.Revision > int.MaxValue) return;
            // Four bounded slots retain host state arriving before native save loading finishes.
            meter.Received = UtilityBillPolicy.Copy(message);
            Locate(meter);
            ApplyGuest(meter);
        }

        private static void ApplyGuest(Meter meter)
        {
            if (!meter.Ready) return;
            try
            {
                if (!meter.Saved)
                {
                    if (!meter.Settled) return;
                    meter.OriginalUnpaid = meter.UnpaidBills!.Value;
                    meter.OriginalPower = meter.PowerBool!.Value;
                    meter.OriginalSwitch = meter.MainSwitch != null && meter.MainSwitch.Value;
                    if (meter.PhoneUsage != null)
                    {
                        meter.OriginalPhoneUsage = new float[4];
                        for (int i = 0; i < 4; i++) meter.OriginalPhoneUsage[i] = meter.PhoneUsage[i].Value;
                    }
                    meter.OriginalBill = meter.Bill!.activeSelf;
                    if (!meter.Pause.Suppress(meter.Data)) throw new InvalidOperationException("Could not pause local utility timer.");
                    meter.Saved = true;
                }
                var state = meter.Received;
                if (state == null) return;
                meter.UnpaidBills!.Value = state.UnpaidBills;
                meter.PowerBool!.Value = state.PowerOn;
                if (meter.IsElectricity)
                {
                    meter.MainSwitch!.Value = state.MainSwitchOn;
                }
                else if (state.Phone != null) ApplyPhoneUsage(meter, state.Phone);
                if (meter.Bill!.activeSelf != state.BillVisible) meter.Bill.SetActive(state.BillVisible);
            }
            catch (Exception e) { Warn(meter, "apply", e); }
        }

        private static void HostBroadcastIfChanged(SessionManager session, Meter meter, bool keepAlive)
        {
            Locate(meter);
            if (!meter.Ready || !meter.Settled || (!meter.IsElectricity && !meter.Payment.Ready)) return;
            try
            {
                var state = ObserveInvoice(meter);
                if (!UtilityBillPolicy.Valid(state)) return;
                if (!keepAlive && meter.Sent != null && UtilityBillPolicy.Same(meter.Sent, state)) return;
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                meter.Sent = state;
            }
            catch (Exception e) { Warn(meter, "capture", e); }
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            Add(UtilityBillState.MeterElectricity1, "Systems/ElectricityBills1", true);
            Add(UtilityBillState.MeterElectricity2, "Systems/ElectricityBills2", true);
            Add(UtilityBillState.MeterPhone1, "Systems/PhoneBills1", false);
            Add(UtilityBillState.MeterPhone2, "Systems/PhoneBills2", false);
        }

        private void Add(byte kind, string path, bool electricity) => _meters.Add(new Meter {
            Kind = kind, ContainerPath = path, IsElectricity = electricity });

        private static void Locate(Meter meter)
        {
            if (meter.Ready || meter.Failed) return;
            try
            {
                var go = GameObject.Find(meter.ContainerPath);
                if (go == null) return;
                foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                    if (fsm != null && fsm.FsmName == "Data") { meter.Data = fsm; break; }
                var data = meter.Data;
                if (data == null || !data.Fsm.Initialized || !data.Fsm.Started) return;
                meter.UnpaidBills = data.FsmVariables.FindFsmFloat("UnpaidBills");
                if (meter.IsElectricity)
                {
                    string name = meter.Kind == UtilityBillState.MeterElectricity1 ? "HouseElectricity" : "HouseElectricity2";
                    meter.PowerBool = FsmVariables.GlobalVariables.FindFsmBool(name);
                    meter.MainSwitch = data.FsmVariables.FindFsmBool("MainSwitch");
                    meter.Bill = data.FsmVariables.FindFsmGameObject("Bill")?.Value;
                    // Cut off writes the global every frame while leaving MainSwitch on.
                    // Verify both native writers before freezing this meter on a guest.
                    bool running = false, cutoff = false;
                    foreach (var state in data.FsmStates)
                        foreach (var action in state.Actions)
                        {
                            if (action.GetType().Name != "SetBoolValue" || !action.Enabled
                                || Field<FsmBool>(action, "boolVariable").Name != name || !Field<bool>(action, "everyFrame")) continue;
                            var value = Field<FsmBool>(action, "boolValue");
                            if (state.Name == "State 2" && value.Name == "MainSwitch" && value.UseVariable) running = true;
                            if (state.Name == "Cut off" && !value.UseVariable && !value.Value) cutoff = true;
                        }
                    if (!running || !cutoff || meter.Bill == null || meter.MainSwitch == null)
                        throw new InvalidOperationException("Electricity meter native layout changed.");
                }
                else
                {
                    meter.PowerBool = data.FsmVariables.FindFsmBool("PhonePaid");
                    meter.Bill = data.FsmVariables.FindFsmGameObject("Bill")?.Value;
                    var c = SyncCatalog.PhonePayments;
                    string[] keys = { "minutes", "longMinutes", "connects", "longConnects" };
                    string[] defaults = { "Minutes", "MinutesLong", "Connects", "ConnectsLong" };
                    var usage = new FsmFloat[4];
                    for (int i = 0; i < 4; i++)
                        usage[i] = data.FsmVariables.FindFsmFloat(c == null ? defaults[i] : c[keys[i]])
                            ?? throw new InvalidOperationException("Phone usage variable missing.");
                    meter.PhoneUsage = usage;
                }
                if (meter.UnpaidBills == null || meter.PowerBool == null) throw new InvalidOperationException("Meter variables missing.");
                if (!meter.LoggedFound)
                {
                    meter.LoggedFound = true;
                    WinterMPPlugin.Log.LogInfo("UtilityBillSync: located meter '" + meter.ContainerPath + "'.");
                }
            }
            catch (Exception e) { Warn(meter, "binding", e); }
        }

        private static T Field<T>(FsmStateAction action, string name) => (T)action.GetType().GetField(name).GetValue(action);

        private static void Warn(Meter meter, string stage, Exception error)
        {
            if (meter.Failed) return;
            meter.Failed = true;
            WinterMPPlugin.Log.LogWarning("UtilityBillSync paused " + meter.ContainerPath + " during " + stage + ": " + error.Message);
        }
    }
}
