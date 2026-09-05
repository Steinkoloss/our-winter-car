using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared repair-shop (Fleetari) order record (COVERAGE-ROADMAP 2.3). The service you
    /// pay for — engine tune, gear ratios, paint, bodywork, tire/spring work — is chosen in
    /// the brochure and applied by the shop's <c>Work</c> FSMs, none of which are synced, so
    /// a service one player buys never reaches the other's copy of the shared car.
    ///
    /// Payment already routes through the host purchase path (the <c>OrderFleetari</c> Pay is
    /// a catalogued buy). This adds the missing half: <b>capture-and-pair</b> (mirror
    /// <see cref="MailOrderSync"/>) — the guest's exact <c>OrderFleetari</c> record is captured
    /// when it confirms and paired to that guest's next <c>PurchaseIntent(PAYMENT)</c>, so the
    /// host applies the guest's actual jobs authoritatively — plus a host broadcast of the
    /// record (jobs / codes / colours / cost) so observers and joiners agree on the pending
    /// service. The host's Work FSMs then apply it to the shared (host-owned) car.
    ///
    /// Note: this makes the order + payment + who-applies host-authoritative; the peer-side
    /// visual mesh repaint is left to the host's authoritative car (the shop car is host-owned
    /// during service) rather than replaying the shop's Work states on peers (several of which
    /// move/relocate the Satsuma and would be unsafe to force remotely).
    /// </summary>
    internal sealed class RepairShopSync
    {
        private const string OrderPath = "REPAIRSHOP/OrderFleetari";
        private const float ScanIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 4f;
        private const float IntentTtlSeconds = 6f;
        private const int MaxDataLength = 2048;

        private PlayMakerFSM? _fsm;
        private uint _netId;
        private FsmBool? _order;
        private FsmFloat? _jobTotalCost;
        private FsmString? _jobs;
        private FsmString? _orderCode;
        private FsmString? _paintCode;
        private FsmString? _axleCode;
        private FsmString? _tireCode;
        private FsmColor? _carPaint;
        private FsmColor? _rimPaint;

        private float _nextScanAt;
        private float _nextSendAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private byte _lastFlags;
        private float _lastCost = float.NaN;
        private string _lastJobs = string.Empty;

        // Capture-and-pair (host side), keyed per player like MailOrderSync: one shared
        // slot/latch would let guest A's confirmed order block or evict guest B's.
        private sealed class PendingIntent
        {
            public FleetariOrderIntent Message = null!;
            public float ExpiresAt;
        }

        private readonly System.Collections.Generic.Dictionary<byte, PendingIntent> _pendingIntents =
            new System.Collections.Generic.Dictionary<byte, PendingIntent>();
        private readonly System.Collections.Generic.Dictionary<byte, ushort> _lastGuestIntentSequences =
            new System.Collections.Generic.Dictionary<byte, ushort>();
        private ushort _outIntentSequence;

        private bool Ready => _fsm != null && _order != null;

        public void Clear()
        {
            _fsm = null;
            _order = null; _jobTotalCost = null;
            _jobs = _orderCode = _paintCode = _axleCode = _tireCode = null;
            _carPaint = _rimPaint = null;
            _netId = 0;
            _nextScanAt = _nextSendAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
            _hasLast = false;
            _lastCost = float.NaN;
            _lastJobs = string.Empty;
            _pendingIntents.Clear();
            _lastGuestIntentSequences.Clear();
            _outIntentSequence = 0;
        }

        /// <summary>Host: a player (re)joined — its intent counter restarted; drop stale state.</summary>
        public void ForgetPlayer(byte playerId)
        {
            _lastGuestIntentSequences.Remove(playerId);
            _pendingIntents.Remove(playerId);
        }

        public void Update(SessionManager session)
        {
            Scan();
            ExpirePendingIntent();
            if (!session.IsHost || session.PlayerCount == 0 || Time.unscaledTime < _nextSendAt) return;
            _nextSendAt = Time.unscaledTime + SendIntervalSeconds;

            var state = BuildState(changedOnly: true);
            if (state != null) session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        public FleetariOrderState? BuildSnapshot()
        {
            Scan(force: true);
            return BuildState(changedOnly: false);
        }

        // ---- Guest capture ---------------------------------------------------

        /// <summary>Guest: capture the local OrderFleetari record on its PAYMENT confirmation.</summary>
        public bool TryBuildIntent(string path, PlayMakerFSM fsm, out FleetariOrderIntent intent)
        {
            intent = new FleetariOrderIntent();
            if (fsm == null || fsm.FsmName != "Data" || path.IndexOf("OrderFleetari", StringComparison.Ordinal) < 0)
                return false;

            try
            {
                BindVars(fsm);
                if (!Ready) return false;

                byte flags = 0;
                if (_order != null && _order.Value) flags |= FleetariOrderState.FlagOrder;
                intent = new FleetariOrderIntent
                {
                    Flags = flags,
                    Sequence = ++_outIntentSequence,
                    JobTotalCost = _jobTotalCost != null ? _jobTotalCost.Value : 0f,
                    CarPaintColor = PackColor(_carPaint),
                    RimPaintColor = PackColor(_rimPaint),
                    Jobs = ReadString(_jobs),
                    OrderCode = ReadString(_orderCode),
                    PaintCode = ReadString(_paintCode),
                    AxleCode = ReadString(_axleCode),
                    TireCode = ReadString(_tireCode),
                };
                return IsValid(intent);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"RepairShopSync: could not capture Fleetari order: {e.Message}");
                return false;
            }
        }

        /// <summary>Host: queue a guest's captured order until its immediately following payment arrives.</summary>
        public bool RememberIntent(FleetariOrderIntent message)
        {
            if (!IsValid(message)) return false;

            if (_lastGuestIntentSequences.TryGetValue(message.PlayerId, out ushort previous))
            {
                ushort difference = (ushort)(message.Sequence - previous);
                if (difference == 0 || difference > short.MaxValue) return false;
            }
            _lastGuestIntentSequences[message.PlayerId] = message.Sequence;

            _pendingIntents[message.PlayerId] = new PendingIntent
            {
                Message = message,
                ExpiresAt = Time.unscaledTime + IntentTtlSeconds,
            };
            return true;
        }

        /// <summary>Host phase 1: bring the order FSM online so the purchase can scan/validate.</summary>
        public bool PrepareIntentForPurchase(PurchaseIntent purchase)
        {
            if (!IsFleetariPayment(purchase)) return true;
            if (!_pendingIntents.TryGetValue(purchase.PlayerId, out var pending)
                || Time.unscaledTime > pending.ExpiresAt)
            {
                WinterMPPlugin.Log.LogWarning($"RepairShopSync: missing/expired Fleetari order for player {purchase.PlayerId}.");
                return false;
            }
            Scan(force: true);
            try
            {
                if (_fsm != null && !_fsm.gameObject.activeInHierarchy) _fsm.gameObject.SetActive(true);
            }
            catch { /* best-effort */ }
            return true;
        }

        /// <summary>Host phase 2: payment validated — commit the guest's descriptor into the host order.</summary>
        public void CommitIntentForPurchase(PurchaseIntent purchase)
        {
            if (!IsFleetariPayment(purchase) || !_pendingIntents.TryGetValue(purchase.PlayerId, out var pending))
                return;
            var message = pending.Message;
            _pendingIntents.Remove(purchase.PlayerId);
            Scan(force: true);
            if (!Ready) return;
            ApplyRecord(message.Flags, message.JobTotalCost, message.CarPaintColor, message.RimPaintColor,
                message.Jobs, message.OrderCode, message.PaintCode, message.AxleCode, message.TireCode);
        }

        // ---- Guest apply -----------------------------------------------------

        public void Apply(FleetariOrderState message)
        {
            Scan();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            ApplyRecord(message.Flags, message.JobTotalCost, message.CarPaintColor, message.RimPaintColor,
                message.Jobs, message.OrderCode, message.PaintCode, message.AxleCode, message.TireCode);
        }

        // ---- internals -------------------------------------------------------

        private FleetariOrderState? BuildState(bool changedOnly)
        {
            if (!Ready) return null;

            byte flags = 0;
            if (_order != null && _order.Value) flags |= FleetariOrderState.FlagOrder;
            float cost = _jobTotalCost != null ? _jobTotalCost.Value : 0f;
            string jobs = ReadString(_jobs);

            bool changed = !_hasLast || _lastFlags != flags
                || Mathf.Abs(cost - _lastCost) > 0.01f
                || !string.Equals(jobs, _lastJobs, StringComparison.Ordinal);
            if (changedOnly && !changed) return null;

            if (changedOnly)
            {
                _hasLast = true;
                _lastFlags = flags;
                _lastCost = cost;
                _lastJobs = jobs;
            }

            return new FleetariOrderState
            {
                Flags = flags,
                Sequence = ++_outSequence,
                JobTotalCost = cost,
                CarPaintColor = PackColor(_carPaint),
                RimPaintColor = PackColor(_rimPaint),
                Jobs = jobs,
                OrderCode = ReadString(_orderCode),
                PaintCode = ReadString(_paintCode),
                AxleCode = ReadString(_axleCode),
                TireCode = ReadString(_tireCode),
            };
        }

        private void ApplyRecord(byte flags, float cost, uint carPaint, uint rimPaint,
            string jobs, string orderCode, string paintCode, string axleCode, string tireCode)
        {
            try
            {
                if (_order != null) _order.Value = (flags & FleetariOrderState.FlagOrder) != 0;
                if (_jobTotalCost != null) _jobTotalCost.Value = Mathf.Max(0f, cost);
                if (_jobs != null) _jobs.Value = jobs ?? string.Empty;
                if (_orderCode != null) _orderCode.Value = orderCode ?? string.Empty;
                if (_paintCode != null) _paintCode.Value = paintCode ?? string.Empty;
                if (_axleCode != null) _axleCode.Value = axleCode ?? string.Empty;
                if (_tireCode != null) _tireCode.Value = tireCode ?? string.Empty;
                if (_carPaint != null) _carPaint.Value = UnpackColor(carPaint);
                if (_rimPaint != null) _rimPaint.Value = UnpackColor(rimPaint);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"RepairShopSync: apply failed: {e.Message}");
            }
        }

        private void Scan(bool force = false)
        {
            if (Ready) return;
            if (!force && Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;

            GameObject? go;
            try { go = GameObject.Find(OrderPath); }
            catch { return; }
            if (go == null) return;

            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm != null && fsm.FsmName == "Data") { BindVars(fsm); break; }
            }
            if (Ready) WinterMPPlugin.Log.LogInfo("RepairShopSync: registered OrderFleetari record.");
        }

        private void BindVars(PlayMakerFSM fsm)
        {
            _fsm = fsm;
            _netId = StableHash.Fnv1a32(OrderPath + "::" + fsm.FsmName);
            var v = fsm.FsmVariables;
            if (_order == null) _order = v.FindFsmBool("Order");
            if (_jobTotalCost == null) _jobTotalCost = v.FindFsmFloat("JobTotalCost");
            if (_jobs == null) _jobs = v.FindFsmString("UTJobs");
            if (_orderCode == null) _orderCode = v.FindFsmString("UTOrder");
            if (_paintCode == null) _paintCode = v.FindFsmString("PaintCode");
            if (_axleCode == null) _axleCode = v.FindFsmString("AxleCode");
            if (_tireCode == null) _tireCode = v.FindFsmString("TireCode");
            if (_carPaint == null) _carPaint = v.FindFsmColor("CarPaintColor");
            if (_rimPaint == null) _rimPaint = v.FindFsmColor("RimPaintColor");
        }

        private void ExpirePendingIntent()
        {
            if (_pendingIntents.Count == 0) return;
            System.Collections.Generic.List<byte>? expired = null;
            foreach (var pair in _pendingIntents)
            {
                if (Time.unscaledTime > pair.Value.ExpiresAt)
                    (expired ?? (expired = new System.Collections.Generic.List<byte>())).Add(pair.Key);
            }
            if (expired != null)
                for (int i = 0; i < expired.Count; i++) _pendingIntents.Remove(expired[i]);
        }

        private static bool IsFleetariPayment(PurchaseIntent purchase)
        {
            return (purchase.EventName == "PAYMENT" || purchase.EventName == "PAY")
                && purchase.NetId == StableHash.Fnv1a32(OrderPath + "::Data");
        }

        private static bool IsValid(FleetariOrderIntent m)
        {
            return !float.IsNaN(m.JobTotalCost) && !float.IsInfinity(m.JobTotalCost) && m.JobTotalCost >= 0f
                && m.Jobs != null && m.Jobs.Length <= MaxDataLength
                && m.OrderCode != null && m.OrderCode.Length <= MaxDataLength
                && m.PaintCode != null && m.PaintCode.Length <= MaxDataLength
                && m.AxleCode != null && m.AxleCode.Length <= MaxDataLength
                && m.TireCode != null && m.TireCode.Length <= MaxDataLength;
        }

        private static string ReadString(FsmString? v) => v != null ? (v.Value ?? string.Empty) : string.Empty;

        private static uint PackColor(FsmColor? v)
        {
            if (v == null) return 0;
            Color c = v.Value;
            byte r = (byte)Mathf.Clamp(c.r * 255f, 0f, 255f);
            byte g = (byte)Mathf.Clamp(c.g * 255f, 0f, 255f);
            byte b = (byte)Mathf.Clamp(c.b * 255f, 0f, 255f);
            byte a = (byte)Mathf.Clamp(c.a * 255f, 0f, 255f);
            return (uint)((r << 24) | (g << 16) | (b << 8) | a);
        }

        private static Color UnpackColor(uint packed)
        {
            float r = ((packed >> 24) & 0xFF) / 255f;
            float g = ((packed >> 16) & 0xFF) / 255f;
            float b = ((packed >> 8) & 0xFF) / 255f;
            float a = (packed & 0xFF) / 255f;
            return new Color(r, g, b, a);
        }
    }
}
