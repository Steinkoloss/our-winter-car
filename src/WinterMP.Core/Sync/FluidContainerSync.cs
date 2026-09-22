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
    /// Synchronizes the mutable contents of tracked fuel containers. Transform
    /// ownership already determines who can manipulate a jerrycan; this layer
    /// carries the otherwise-local FuelLevel / Pouring FSM variables alongside it.
    /// </summary>
    internal sealed partial class FluidContainerSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 2f;
        private const float ChangeEpsilon = 0.01f;

        private readonly ItemWorldSync _items;
        private readonly Dictionary<uint, FluidContainerState> _pending = new Dictionary<uint, FluidContainerState>();

        public FluidContainerSync(ItemWorldSync items)
        {
            _items = items;
        }

        /// <summary>Level change: path-derived item ids repeat after a reload, so a parked
        /// pre-reload state must not apply to the re-registered item.</summary>
        public void Clear()
        {
            _pending.Clear(); _fuelAuthority?.Clear(); _fuelSequence=0; _pendingFuelResult=null;
            foreach (var item in _items.Items.Values)
            {
                item.FuelRevision=0; item.AcceptedFluidLevel=float.NaN;
                item.LastRemoteFluidOwner=WorldSyncIds.NoOwner;
                item.LastRemoteFluidSequence=item.OutFluidSequence=0;
                item.LastSentFluidLevel=float.NaN; item.NextFluidSendAt=0;
            }
        }

        public void Update(SessionManager session)
        {
            float now = Time.unscaledTime;
            ApplyPending(now);
            UpdateFuelInput(session);
            if (session.PlayerCount == 0) return;

            foreach (var item in _items.Items.Values)
            {
                if (item.IsVehicle || item.Body == null) continue;
                Locate(item, now);
                if (item.FluidLevelVar == null) continue;

                if (!session.IsHost && IsFiniteSource(item))
                {
                    if (!float.IsNaN(item.AcceptedFluidLevel)) item.FluidLevelVar.Value=item.AcceptedFluidLevel;
                    continue;
                }

                // Guests may only report containers they currently simulate. The host
                // is the authority for resting and unclaimed containers.
                if (!session.IsHost && !item.LocallyOwned) continue;

                float level = item.FluidLevelVar.Value;
                float capacity = item.FluidCapacityVar != null ? item.FluidCapacityVar.Value : 0f;
                bool pouring = item.FluidPouringVar != null && item.FluidPouringVar.Value;
                bool changed = float.IsNaN(item.LastSentFluidLevel)
                    || Mathf.Abs(level - item.LastSentFluidLevel) > ChangeEpsilon
                    || pouring != item.LastSentFluidPouring;
                if (!changed && now < item.NextFluidSendAt) continue;

                item.NextFluidSendAt = now + SendIntervalSeconds;
                item.LastSentFluidLevel = level;
                item.LastSentFluidPouring = pouring;

                session.SendWorldMessage(new FluidContainerState
                {
                    ItemId = item.Id,
                    OwnerPlayerId = session.LocalPlayerId,
                    Sequence = ++item.OutFluidSequence,
                    Flags = pouring ? FluidContainerState.FlagPouring : (byte)0,
                    Level = level,
                    Capacity = capacity,
                    FuelRevision = item.FuelRevision,
                }, Channel.ReliableOrdered);
            }
        }

        public IEnumerable<FluidContainerState> BuildSnapshots(byte ownerPlayerId)
        {
            float now = Time.unscaledTime;
            foreach (var item in _items.Items.Values)
            {
                if (item.IsVehicle || item.Body == null) continue;
                Locate(item, now);
                if (item.FluidLevelVar == null) continue;

                yield return new FluidContainerState
                {
                    ItemId = item.Id,
                    OwnerPlayerId = ownerPlayerId,
                    Sequence = ++item.OutFluidSequence,
                    Flags = item.FluidPouringVar != null && item.FluidPouringVar.Value
                        ? FluidContainerState.FlagPouring : (byte)0,
                    Level = item.FluidLevelVar.Value,
                    Capacity = item.FluidCapacityVar != null ? item.FluidCapacityVar.Value : 0f,
                    FuelRevision = item.FuelRevision,
                };
            }
        }

        /// <summary>
        /// Host gate for a guest-owned container update. The item-transform stream
        /// establishes ownership first; a guest cannot use this scalar message to
        /// fill or drain another player's container and then have it relayed.
        /// </summary>
        public bool TryAcceptGuestState(FluidContainerState message, byte playerId)
        {
            if (message.OwnerPlayerId != playerId || !IsValidMessage(message)) return false;

            var session = SessionManager.Instance;
            if (session == null || !session.IsHost
                || !_items.Items.TryGetValue(message.ItemId, out var item)
                || item.IsVehicle || item.Body == null || item.RemoteOwner != playerId || IsFiniteSource(item)
                || message.FuelRevision != item.FuelRevision)
                return false;

            return TryApplyKnownState(item, message);
        }

        public void OnRemoteState(FluidContainerState message)
        {
            if (!_items.Items.TryGetValue(message.ItemId, out var item) || item.IsVehicle || item.Body == null)
            {
                _pending[message.ItemId] = message;
                return;
            }

            var session = SessionManager.Instance;
            if (session != null && message.OwnerPlayerId == session.LocalPlayerId)
                return;
            if (session != null && session.IsHost && IsFiniteSource(item)) return;

            // A guest can only change a container it currently owns. The transform
            // stream establishes RemoteOwner before this state arrives; accepting a
            // stale final-packet write would let a former holder overwrite the host.
            if (session != null && session.IsHost
                && item.RemoteOwner != message.OwnerPlayerId)
            {
                WinterMPPlugin.Log.LogDebug(
                    $"FluidSync: ignored non-owner state for {message.ItemId:X8} from {message.OwnerPlayerId}.");
                return;
            }

            if (!TryApplyKnownState(item, message) && IsFiniteSource(item) && item.FluidLevelVar == null)
                _pending[item.Id]=message;
        }

        private bool TryApplyKnownState(SyncedItem item, FluidContainerState message)
        {
            if (!IsValidMessage(message) || message.FuelRevision < item.FuelRevision) return false;

            Locate(item, Time.unscaledTime);
            if (item.FluidLevelVar == null) return false;

            if (item.LastRemoteFluidOwner == message.OwnerPlayerId)
            {
                ushort diff = (ushort)(message.Sequence - item.LastRemoteFluidSequence);
                if (diff == 0 || diff > short.MaxValue) return false;
            }
            item.LastRemoteFluidOwner = message.OwnerPlayerId;
            item.LastRemoteFluidSequence = message.Sequence;

            float capacity = item.FluidCapacityVar != null ? item.FluidCapacityVar.Value : message.Capacity;
            if (capacity > ChangeEpsilon)
                item.FluidLevelVar.Value = Mathf.Clamp(message.Level, 0f, capacity);
            else
                item.FluidLevelVar.Value = Mathf.Max(0f, message.Level);

            item.FuelRevision=message.FuelRevision;
            if (IsFiniteSource(item)) item.AcceptedFluidLevel=item.FluidLevelVar.Value;

            if (item.FluidPouringVar != null)
                item.FluidPouringVar.Value = message.IsPouring;
            return true;
        }

        private void ApplyPending(float now)
        {
            if (_pending.Count == 0) return;

            var applied = new List<uint>();
            foreach (var pair in _pending)
            {
                if (!_items.Items.TryGetValue(pair.Key, out var item) || item.IsVehicle || item.Body == null)
                    continue;
                Locate(item, now);
                if (item.FluidLevelVar == null) continue;

                // Re-enter through the normal ownership/dedup path now that the target exists.
                OnRemoteState(pair.Value);
                applied.Add(pair.Key);
            }
            foreach (uint itemId in applied) _pending.Remove(itemId);
        }

        private static void Locate(SyncedItem item, float now)
        {
            if (now < item.NextFluidProbeAt) return;
            item.NextFluidProbeAt = now + ProbeIntervalSeconds;
            if (item.FluidLevelVar != null) return;

            try
            {
                if (IsFiniteSource(item)) { LocateGasoline(item); return; }
                var fsms = item.Body.GetComponentsInChildren<PlayMakerFSM>(true);
                foreach (var fsm in fsms)
                {
                    var level = fsm.FsmVariables.FindFsmFloat("FuelLevel");
                    var capacity = fsm.FsmVariables.FindFsmFloat("MaxCapacity");
                    if (level == null || capacity == null) continue;

                    item.FluidLevelVar = level;
                    item.FluidCapacityVar = capacity;
                    item.FluidPouringVar = fsm.FsmVariables.FindFsmBool("Pouring");
                    WinterMPPlugin.Log.LogInfo($"FluidSync: registered '{item.Path}'.");
                    return;
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"FluidSync: probe failed for '{item.Path}': {e.Message}");
            }
        }

        private static bool IsValidMessage(FluidContainerState message)
        {
            return (message.Flags & ~FluidContainerState.FlagPouring) == 0
                && !float.IsNaN(message.Level) && !float.IsInfinity(message.Level) && message.Level >= 0f
                && !float.IsNaN(message.Capacity) && !float.IsInfinity(message.Capacity) && message.Capacity >= 0f;
        }
    }
}
