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
    /// Kilju bucket fermentation as tracked-item scalar state (COVERAGE-ROADMAP 3.3). The
    /// bucket's <c>Alcohol</c>/<c>BrewTime</c>/finished/lid live on its own <c>Use</c> FSM and
    /// advance locally, so peers brew a different quality and the buyer pays a different price.
    /// This mirrors <see cref="FluidContainerSync"/>: whoever holds the bucket streams its
    /// <see cref="BrewState"/>, others apply it. Selling routes through the host wallet (the
    /// KiljuBuyer pay is a catalogued buy) and the price is derived from the agreed brew.
    /// </summary>
    internal sealed class KiljuSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 3f;
        private const float ChangeEpsilon = 0.005f;

        private readonly ItemWorldSync _items;
        private readonly Dictionary<uint, BrewState> _pending = new Dictionary<uint, BrewState>();

        public KiljuSync(ItemWorldSync items)
        {
            _items = items;
        }

        public void Update(SessionManager session)
        {
            float now = Time.unscaledTime;
            ApplyPending(now);
            if (session.PlayerCount == 0) return;

            foreach (var item in _items.Items.Values)
            {
                if (item.IsVehicle || item.Body == null) continue;
                Locate(item, now);
                if (item.BrewAlcoholVar == null) continue;

                // Guests only report a bucket they currently hold; the host owns resting ones.
                if (!session.IsHost && !item.LocallyOwned) continue;

                byte flags = ReadFlags(item);
                float alcohol = item.BrewAlcoholVar.Value;
                bool changed = float.IsNaN(item.LastSentBrewAlcohol)
                    || Mathf.Abs(alcohol - item.LastSentBrewAlcohol) > ChangeEpsilon
                    || flags != item.LastSentBrewFlags;
                if (!changed && now < item.NextBrewSendAt) continue;

                item.NextBrewSendAt = now + SendIntervalSeconds;
                item.LastSentBrewAlcohol = alcohol;
                item.LastSentBrewFlags = flags;

                session.SendWorldMessage(BuildState(item, session.LocalPlayerId, flags, alcohol), Channel.ReliableOrdered);
            }
        }

        public IEnumerable<BrewState> BuildSnapshots(byte ownerPlayerId)
        {
            float now = Time.unscaledTime;
            foreach (var item in _items.Items.Values)
            {
                if (item.IsVehicle || item.Body == null) continue;
                Locate(item, now);
                if (item.BrewAlcoholVar == null) continue;
                yield return BuildState(item, ownerPlayerId, ReadFlags(item), item.BrewAlcoholVar.Value);
            }
        }

        public bool TryAcceptGuestState(BrewState message, byte playerId)
        {
            if (message.OwnerPlayerId != playerId || !IsValid(message)) return false;
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost
                || !_items.Items.TryGetValue(message.ItemId, out var item)
                || item.IsVehicle || item.Body == null || item.RemoteOwner != playerId)
                return false;
            return TryApply(item, message);
        }

        public void OnRemoteState(BrewState message)
        {
            if (!_items.Items.TryGetValue(message.ItemId, out var item) || item.IsVehicle || item.Body == null)
            {
                _pending[message.ItemId] = message;
                return;
            }
            var session = SessionManager.Instance;
            if (session != null && message.OwnerPlayerId == session.LocalPlayerId) return;
            if (session != null && session.IsHost && item.RemoteOwner != message.OwnerPlayerId) return;
            TryApply(item, message);
        }

        private bool TryApply(SyncedItem item, BrewState message)
        {
            if (!IsValid(message)) return false;
            if (item.LastRemoteBrewOwner == message.OwnerPlayerId)
            {
                ushort diff = (ushort)(message.Sequence - item.LastRemoteBrewSequence);
                if (diff == 0 || diff > short.MaxValue) return false;
            }
            item.LastRemoteBrewOwner = message.OwnerPlayerId;
            item.LastRemoteBrewSequence = message.Sequence;

            Locate(item, Time.unscaledTime);
            if (item.BrewAlcoholVar == null) return false;

            try
            {
                item.BrewAlcoholVar.Value = Mathf.Max(0f, message.Alcohol);
                if (item.BrewTimeVar != null) item.BrewTimeVar.Value = Mathf.Max(0f, message.BrewTime);
                if (item.BrewFinishedVar != null) item.BrewFinishedVar.Value = message.Finished;
                if (item.BrewLidVar != null) item.BrewLidVar.Value = message.LidOn;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"KiljuSync: apply failed for '{item.Path}': {e.Message}");
                return false;
            }
            return true;
        }

        private void ApplyPending(float now)
        {
            if (_pending.Count == 0) return;
            var applied = new List<uint>();
            foreach (var pair in _pending)
            {
                if (!_items.Items.TryGetValue(pair.Key, out var item) || item.IsVehicle || item.Body == null) continue;
                Locate(item, now);
                if (item.BrewAlcoholVar == null) continue;
                OnRemoteState(pair.Value);
                applied.Add(pair.Key);
            }
            foreach (uint id in applied) _pending.Remove(id);
        }

        private static BrewState BuildState(SyncedItem item, byte ownerPlayerId, byte flags, float alcohol)
        {
            return new BrewState
            {
                ItemId = item.Id,
                OwnerPlayerId = ownerPlayerId,
                Sequence = ++item.OutBrewSequence,
                Flags = flags,
                Alcohol = alcohol,
                BrewTime = item.BrewTimeVar != null ? item.BrewTimeVar.Value : 0f,
            };
        }

        private static byte ReadFlags(SyncedItem item)
        {
            byte flags = 0;
            if (item.BrewFinishedVar != null && item.BrewFinishedVar.Value) flags |= BrewState.FlagFinished;
            if (item.BrewLidVar != null && item.BrewLidVar.Value) flags |= BrewState.FlagLidOn;
            return flags;
        }

        private static void Locate(SyncedItem item, float now)
        {
            if (item.BrewProbed) return;
            if (now < item.NextBrewProbeAt) return;
            item.NextBrewProbeAt = now + ProbeIntervalSeconds;

            try
            {
                foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    var alcohol = fsm.FsmVariables.FindFsmFloat("Alcohol");
                    var brewTime = fsm.FsmVariables.FindFsmFloat("BrewTime");
                    if (alcohol == null || brewTime == null) continue; // kilju bucket signature

                    item.BrewAlcoholVar = alcohol;
                    item.BrewTimeVar = brewTime;
                    item.BrewFinishedVar = fsm.FsmVariables.FindFsmBool("KiljuFinished");
                    item.BrewLidVar = fsm.FsmVariables.FindFsmBool("LidOn");
                    item.BrewProbed = true;
                    WinterMPPlugin.Log.LogInfo($"KiljuSync: registered kilju bucket '{item.Path}'.");
                    return;
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"KiljuSync: probe failed for '{item.Path}': {e.Message}");
            }
        }

        private static bool IsValid(BrewState message)
        {
            return (message.Flags & ~(BrewState.FlagFinished | BrewState.FlagLidOn)) == 0
                && !float.IsNaN(message.Alcohol) && !float.IsInfinity(message.Alcohol) && message.Alcohol >= 0f
                && !float.IsNaN(message.BrewTime) && !float.IsInfinity(message.BrewTime) && message.BrewTime >= 0f;
        }
    }
}
