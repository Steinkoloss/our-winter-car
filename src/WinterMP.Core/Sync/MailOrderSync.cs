using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Mirrors the game's persisted mail-order records. Their saved strings encode
    /// the selected package, so copying only price/timing would leave a joiner with
    /// an order that looks valid but delivers the wrong thing.
    /// </summary>
    internal sealed class MailOrderSync
    {
        private const float ScanIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 4f;
        private const float ChangeEpsilon = 0.01f;
        private const int MaxOrderDataLength = 2048;
        private const float IntentTtlSeconds = 5f;

        private sealed class Order
        {
            public byte Kind;
            public uint NetId;
            public PlayMakerFSM Fsm = null!;
            public FsmFloat Price = null!;
            public FsmFloat WaitTime = null!;
            public FsmInt PriceInt = null!;
            public FsmBool Active = null!;
            public FsmBool Consumed = null!;
            public FsmBool SavePosition = null!;
            public FsmString Data1 = null!;
            public FsmString Data2 = null!;
            public FsmString Data3 = null!;
            public ushort OutSequence;
            public ushort LastRemoteSequence;
            public float LastPrice = float.NaN;
            public float LastWaitTime = float.NaN;
            public int LastPriceInt = int.MinValue;
            public byte LastFlags = byte.MaxValue;
            public string LastData1 = string.Empty;
            public string LastData2 = string.Empty;
            public string LastData3 = string.Empty;
            public bool HasLastState;
        }

        private sealed class PendingIntent
        {
            public MailOrderIntent Message = null!;
            public float ExpiresAt;
        }

        private readonly Dictionary<byte, Order> _orders = new Dictionary<byte, Order>();
        private readonly Dictionary<byte, MailOrderState> _pending = new Dictionary<byte, MailOrderState>();
        private readonly Dictionary<byte, PendingIntent> _pendingIntents = new Dictionary<byte, PendingIntent>();
        private readonly Dictionary<byte, ushort> _lastGuestIntentSequences = new Dictionary<byte, ushort>();
        private float _nextScanAt;
        private float _nextSendAt;
        private ushort _outIntentSequence;

        public void Clear()
        {
            _orders.Clear();
            _pending.Clear();
            _pendingIntents.Clear();
            _lastGuestIntentSequences.Clear();
            _nextScanAt = 0f;
            _nextSendAt = 0f;
            _outIntentSequence = 0;
        }

        public void Update(SessionManager session)
        {
            Scan();
            ApplyPending();
            if (!session.IsHost || session.PlayerCount == 0 || Time.unscaledTime < _nextSendAt)
                return;

            _nextSendAt = Time.unscaledTime + SendIntervalSeconds;
            foreach (var state in BuildStates(changedOnly: true))
                session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        public IEnumerable<MailOrderState> BuildSnapshots()
        {
            Scan(force: true);
            foreach (var state in BuildStates(changedOnly: false))
                yield return state;
        }

        public void Apply(MailOrderState message)
        {
            if (!IsKnownKind(message.Kind) || !IsFinite(message.Price) || !IsFinite(message.WaitTime))
                return;

            Scan();
            if (!_orders.TryGetValue(message.Kind, out var order))
            {
                _pending[message.Kind] = message;
                return;
            }

            ushort diff = (ushort)(message.Sequence - order.LastRemoteSequence);
            if (order.LastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            order.LastRemoteSequence = message.Sequence;

            ApplyValues(order, message.Flags, message.Price, message.WaitTime, message.PriceInt,
                message.Data1, message.Data2, message.Data3);
        }

        /// <summary>Captures the order which a guest just confirmed in its local phone UI.</summary>
        public bool TryBuildIntent(string path, PlayMakerFSM fsm, out MailOrderIntent intent)
        {
            intent = new MailOrderIntent();
            byte kind = GetKind(path);
            if (!IsKnownKind(kind) || fsm == null || fsm.FsmName != "Data") return false;

            try
            {
                var price = fsm.FsmVariables.FindFsmFloat("Price");
                var waitTime = fsm.FsmVariables.FindFsmFloat("WaitTime");
                var priceInt = fsm.FsmVariables.FindFsmInt("PriceInt");
                var active = fsm.FsmVariables.FindFsmBool("OrderActive");
                var consumed = fsm.FsmVariables.FindFsmBool("Consumed");
                var savePosition = fsm.FsmVariables.FindFsmBool("SavePos");
                var data1 = fsm.FsmVariables.FindFsmString("UTData1");
                var data2 = fsm.FsmVariables.FindFsmString("UTData2");
                var data3 = fsm.FsmVariables.FindFsmString("UTData3");
                if (price == null || waitTime == null || priceInt == null || active == null || consumed == null
                    || savePosition == null || data1 == null || data2 == null || data3 == null)
                    return false;

                string value1 = data1.Value ?? string.Empty;
                string value2 = data2.Value ?? string.Empty;
                string value3 = data3.Value ?? string.Empty;
                if (!IsValidValues(price.Value, waitTime.Value, value1, value2, value3)) return false;

                byte flags = 0;
                if (active.Value) flags |= MailOrderState.FlagActive;
                if (consumed.Value) flags |= MailOrderState.FlagConsumed;
                if (savePosition.Value) flags |= MailOrderState.FlagSavePosition;
                intent = new MailOrderIntent
                {
                    OrderNetId = StableHash.Fnv1a32(path + "::" + fsm.FsmName),
                    Kind = kind,
                    Flags = flags,
                    Sequence = ++_outIntentSequence,
                    Price = price.Value,
                    WaitTime = waitTime.Value,
                    PriceInt = priceInt.Value,
                    Data1 = value1,
                    Data2 = value2,
                    Data3 = value3,
                };
                return true;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"MailOrderSync: could not capture guest order: {e.Message}");
                return false;
            }
        }

        /// <summary>Queues a guest's order until its immediately following payment intent arrives.</summary>
        public bool RememberIntent(MailOrderIntent message)
        {
            if (!IsKnownKind(message.Kind)
                || !IsValidValues(message.Price, message.WaitTime, message.Data1, message.Data2, message.Data3))
                return false;

            ushort lastSequence;
            if (_lastGuestIntentSequences.TryGetValue(message.PlayerId, out lastSequence))
            {
                ushort difference = (ushort)(message.Sequence - lastSequence);
                if (difference == 0 || difference > short.MaxValue)
                {
                    WinterMPPlugin.Log.LogDebug(
                        $"MailOrderSync: dropped stale order intent from player {message.PlayerId} (sequence {message.Sequence}).");
                    return false;
                }
            }
            _lastGuestIntentSequences[message.PlayerId] = message.Sequence;

            _pendingIntents[message.PlayerId] = new PendingIntent
            {
                Message = message,
                ExpiresAt = Time.unscaledTime + IntentTtlSeconds,
            };
            return true;
        }

        /// <summary>
        /// Phase 1 of a guest mail-order payment: resolve the queued order and bring its
        /// (possibly inactive) data FSM online so the purchase can be scanned and
        /// proximity-validated. Deliberately does NOT write the guest's descriptor into
        /// authoritative host state — that waits until the purchase itself passes host
        /// validation (see <see cref="CommitIntentForPurchase"/>), so a rejected/spoofed
        /// payment can never leave the host order carrying a stranger's selection.
        /// Returns true (pass-through) for non-mail-order purchases.
        /// </summary>
        public bool PrepareIntentForPurchase(PurchaseIntent purchase)
        {
            if (purchase.EventName != "PAYMENT" || !IsMailOrderNetId(purchase.NetId)) return true;
            if (!_pendingIntents.TryGetValue(purchase.PlayerId, out var pending))
            {
                WinterMPPlugin.Log.LogWarning($"MailOrderSync: missing selected order data for player {purchase.PlayerId}.");
                return false;
            }
            if (Time.unscaledTime > pending.ExpiresAt)
            {
                _pendingIntents.Remove(purchase.PlayerId);
                WinterMPPlugin.Log.LogWarning($"MailOrderSync: selected order data expired for player {purchase.PlayerId}.");
                return false;
            }

            var message = pending.Message;
            Scan(force: true);
            if (!_orders.TryGetValue(message.Kind, out var order) || order.NetId != purchase.NetId
                || order.NetId != message.OrderNetId)
            {
                _pendingIntents.Remove(purchase.PlayerId);
                WinterMPPlugin.Log.LogWarning(
                    $"MailOrderSync: rejected mismatched {DescribeKind(message.Kind)} order for player {purchase.PlayerId}.");
                return false;
            }

            try
            {
                // The order data objects start disabled until a local phone call. The
                // host never made that call, so bring its matching data FSM online
                // before the normal purchase pipeline is asked to enter Spawn package.
                if (!order.Fsm.gameObject.activeInHierarchy)
                    order.Fsm.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                _pendingIntents.Remove(purchase.PlayerId);
                WinterMPPlugin.Log.LogWarning($"MailOrderSync: could not activate {DescribeKind(message.Kind)} order: {e.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Phase 2: the payment passed host proximity/sequence validation, so commit the
        /// guest's selected descriptor into the authoritative order FSM and consume the
        /// queued intent. No-op for non-mail-order purchases or if the queued intent is
        /// gone (e.g. the payment was rejected between prepare and commit).
        /// </summary>
        public void CommitIntentForPurchase(PurchaseIntent purchase)
        {
            if (purchase.EventName != "PAYMENT" || !IsMailOrderNetId(purchase.NetId)) return;
            if (!_pendingIntents.TryGetValue(purchase.PlayerId, out var pending)) return;
            _pendingIntents.Remove(purchase.PlayerId);

            var message = pending.Message;
            if (!_orders.TryGetValue(message.Kind, out var order) || order.NetId != message.OrderNetId)
                return;

            ApplyValues(order, message.Flags, message.Price, message.WaitTime, message.PriceInt,
                message.Data1, message.Data2, message.Data3);
        }

        private IEnumerable<MailOrderState> BuildStates(bool changedOnly)
        {
            foreach (var order in _orders.Values)
            {
                float price = order.Price.Value;
                float waitTime = order.WaitTime.Value;
                int priceInt = order.PriceInt.Value;
                byte flags = ReadFlags(order);
                string data1 = order.Data1.Value ?? string.Empty;
                string data2 = order.Data2.Value ?? string.Empty;
                string data3 = order.Data3.Value ?? string.Empty;
                bool changed = !order.HasLastState
                    || Mathf.Abs(price - order.LastPrice) > ChangeEpsilon
                    || Mathf.Abs(waitTime - order.LastWaitTime) > ChangeEpsilon
                    || priceInt != order.LastPriceInt
                    || flags != order.LastFlags
                    || !string.Equals(data1, order.LastData1, StringComparison.Ordinal)
                    || !string.Equals(data2, order.LastData2, StringComparison.Ordinal)
                    || !string.Equals(data3, order.LastData3, StringComparison.Ordinal);
                if (changedOnly && !changed) continue;

                // Only the periodic delta path owns the change-detection baseline. The
                // join-snapshot path (changedOnly==false) emits to the joining peer ONLY,
                // so advancing Last* here would mark a not-yet-broadcast change as sent and
                // strand already-connected guests on a stale pending order.
                if (changedOnly)
                {
                    order.HasLastState = true;
                    order.LastPrice = price;
                    order.LastWaitTime = waitTime;
                    order.LastPriceInt = priceInt;
                    order.LastFlags = flags;
                    order.LastData1 = data1;
                    order.LastData2 = data2;
                    order.LastData3 = data3;
                }
                yield return new MailOrderState
                {
                    Kind = order.Kind,
                    Flags = flags,
                    Sequence = ++order.OutSequence,
                    Price = price,
                    WaitTime = waitTime,
                    PriceInt = priceInt,
                    Data1 = data1,
                    Data2 = data2,
                    Data3 = data3,
                };
            }
        }

        private void Scan(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;

            var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
            foreach (var obj in fsms)
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || fsm.FsmName != "Data") continue;

                try
                {
                    byte kind = GetKind(ScenePath.Of(fsm.transform));
                    if (!IsKnownKind(kind) || _orders.ContainsKey(kind)) continue;

                    var price = fsm.FsmVariables.FindFsmFloat("Price");
                    var waitTime = fsm.FsmVariables.FindFsmFloat("WaitTime");
                    var priceInt = fsm.FsmVariables.FindFsmInt("PriceInt");
                    var active = fsm.FsmVariables.FindFsmBool("OrderActive");
                    var consumed = fsm.FsmVariables.FindFsmBool("Consumed");
                    var savePosition = fsm.FsmVariables.FindFsmBool("SavePos");
                    var data1 = fsm.FsmVariables.FindFsmString("UTData1");
                    var data2 = fsm.FsmVariables.FindFsmString("UTData2");
                    var data3 = fsm.FsmVariables.FindFsmString("UTData3");
                    if (price == null || waitTime == null || priceInt == null || active == null || consumed == null
                        || savePosition == null || data1 == null || data2 == null || data3 == null)
                        continue;

                    _orders[kind] = new Order
                    {
                        Kind = kind,
                        NetId = StableHash.Fnv1a32(ScenePath.Of(fsm.transform) + "::" + fsm.FsmName),
                        Fsm = fsm,
                        Price = price,
                        WaitTime = waitTime,
                        PriceInt = priceInt,
                        Active = active,
                        Consumed = consumed,
                        SavePosition = savePosition,
                        Data1 = data1,
                        Data2 = data2,
                        Data3 = data3,
                    };
                    WinterMPPlugin.Log.LogInfo($"MailOrderSync: registered {DescribeKind(kind)} order data.");
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"MailOrderSync: skipped FSM: {e.Message}");
                }
            }
        }

        private void ApplyPending()
        {
            if (_pending.Count > 0)
            {
                var applied = new List<byte>();
                foreach (var pair in _pending)
                {
                    if (!_orders.ContainsKey(pair.Key)) continue;
                    Apply(pair.Value);
                    applied.Add(pair.Key);
                }
                foreach (byte kind in applied) _pending.Remove(kind);
            }

            if (_pendingIntents.Count > 0)
            {
                var expired = new List<byte>();
                foreach (var pair in _pendingIntents)
                {
                    if (Time.unscaledTime > pair.Value.ExpiresAt) expired.Add(pair.Key);
                }
                foreach (byte playerId in expired) _pendingIntents.Remove(playerId);
            }
        }

        private static void ApplyValues(Order order, byte flags, float price, float waitTime, int priceInt,
            string data1, string data2, string data3)
        {
            order.Price.Value = Mathf.Max(0f, price);
            order.WaitTime.Value = Mathf.Max(0f, waitTime);
            order.PriceInt.Value = Mathf.Max(0, priceInt);
            order.Active.Value = (flags & MailOrderState.FlagActive) != 0;
            order.Consumed.Value = (flags & MailOrderState.FlagConsumed) != 0;
            order.SavePosition.Value = (flags & MailOrderState.FlagSavePosition) != 0;
            order.Data1.Value = data1 ?? string.Empty;
            order.Data2.Value = data2 ?? string.Empty;
            order.Data3.Value = data3 ?? string.Empty;
        }

        private static byte ReadFlags(Order order)
        {
            byte flags = 0;
            if (order.Active.Value) flags |= MailOrderState.FlagActive;
            if (order.Consumed.Value) flags |= MailOrderState.FlagConsumed;
            if (order.SavePosition.Value) flags |= MailOrderState.FlagSavePosition;
            return flags;
        }

        private static byte GetKind(string path)
        {
            if (path == "OrderAMIS") return MailOrderState.KindAmis;
            if (path == "OrderYP") return MailOrderState.KindYellowPages;
            if (path == "CARPARTS/Hidden/OrderYP") return MailOrderState.KindHiddenYellowPages;
            return 0;
        }

        private static bool IsKnownKind(byte kind)
        {
            return kind == MailOrderState.KindAmis
                || kind == MailOrderState.KindYellowPages
                || kind == MailOrderState.KindHiddenYellowPages;
        }

        private static string DescribeKind(byte kind)
        {
            if (kind == MailOrderState.KindAmis) return "AMIS";
            if (kind == MailOrderState.KindYellowPages) return "Yellow Pages";
            return "hidden Yellow Pages";
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsMailOrderNetId(uint netId)
        {
            return netId == StableHash.Fnv1a32("OrderAMIS::Data")
                || netId == StableHash.Fnv1a32("OrderYP::Data")
                || netId == StableHash.Fnv1a32("CARPARTS/Hidden/OrderYP::Data");
        }

        private static bool IsValidValues(float price, float waitTime, string data1, string data2, string data3)
        {
            return IsFinite(price) && IsFinite(waitTime) && price >= 0f && waitTime >= 0f
                && data1 != null && data1.Length <= MaxOrderDataLength
                && data2 != null && data2.Length <= MaxOrderDataLength
                && data3 != null && data3.Length <= MaxOrderDataLength;
        }
    }
}
