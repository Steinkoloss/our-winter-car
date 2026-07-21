using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>Host-validated fuel-station transfer for a guest beside a parked car.</summary>
    internal sealed partial class VehicleWorldSync
    {
        private const string FuelStationPathPrefix = "PERAPORTTI/Building/LOD300/FuelPumps_1-2/";
        private const float NozzleProbeIntervalSeconds = 5f;
        private const float FuelIntentIntervalSeconds = 0.25f;
        private const float FuelLevelEpsilon = 0.01f;
        private const float PlayerPoseMaxAgeSeconds = 2f;
        private const float PlayerVehicleMaxDistance = 8f;
        private const float VehicleNozzleMaxDistance = 10f;
        private const float PlayerNozzleMaxDistance = 12f;
        private const float MaxStationaryVelocitySqr = 0.25f;
        private const float InitialFuelAllowanceLiters = 0.35f;
        private const float MaxFuelLitersPerSecond = 2.5f;
        private const float FuelTransferHoldSeconds = 1f;

        private sealed class StationNozzle
        {
            public uint NetId;
            public PlayMakerFSM Fsm = null!;
            public Transform Transform = null!;
        }

        private readonly Dictionary<uint, StationNozzle> _stationNozzles =
            new Dictionary<uint, StationNozzle>();
        private float _nextNozzleProbeAt;

        /// <summary>
        /// A vehicle driver already owns the normal VehicleState fuel stream. This
        /// additional path is only for the separate player standing at a station
        /// nozzle beside a parked vehicle.
        /// </summary>
        public void UpdateFuelTransfers(SessionManager session)
        {
            if (session.IsHost || session.PlayerCount == 0) return;

            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null) return;

            float now = Time.unscaledTime;
            if (!TryFindDispensingNozzle(now, out var nozzle)) return;
            if ((_bridge.LocalPlayer.position - nozzle.Transform.position).sqrMagnitude
                > PlayerNozzleMaxDistance * PlayerNozzleMaxDistance)
                return;

            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || item.LocallyOwned) continue;
                if ((item.Body.transform.position - nozzle.Transform.position).sqrMagnitude
                    > VehicleNozzleMaxDistance * VehicleNozzleMaxDistance)
                    continue;
                if ((_bridge.LocalPlayer.position - item.Body.transform.position).sqrMagnitude
                    > PlayerVehicleMaxDistance * PlayerVehicleMaxDistance)
                    continue;

                EnsureVehicleSystemsProbe(item);
                if (!TryReadFuelTank(item, out float level, out float capacity)) continue;

                if (float.IsNaN(item.LastObservedFuelTransferLevel))
                    item.LastObservedFuelTransferLevel = level;

                // A repeated target is intentional while the host catches up to a
                // bounded transfer. A decrease merely establishes a new local baseline.
                if (level + FuelLevelEpsilon < item.LastObservedFuelTransferLevel)
                    item.LastObservedFuelTransferLevel = level;
                else if (level > item.LastObservedFuelTransferLevel)
                    item.LastObservedFuelTransferLevel = level;

                if (now < item.NextFuelTransferAt) continue;
                item.NextFuelTransferAt = now + FuelIntentIntervalSeconds;

                session.SendWorldMessage(new VehicleFuelIntent
                {
                    VehicleId = item.Id,
                    NozzleNetId = nozzle.NetId,
                    PlayerId = session.LocalPlayerId,
                    Sequence = ++item.OutFuelTransferSequence,
                    TargetFuelLevel = NormalizeFuelLevel(level, capacity),
                }, Channel.ReliableOrdered);
            }
        }

        /// <summary>Host-side authority check and bounded reconciliation for a fuel intent.</summary>
        public bool TryAcceptFuelTransfer(VehicleFuelIntent message, out VehicleState state)
        {
            state = new VehicleState();
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;
            if (!_items.Items.TryGetValue(message.VehicleId, out var item)
                || !item.IsVehicle || item.Body == null)
                return false;

            float now = Time.unscaledTime;
            if (!TryGetStationNozzle(message.NozzleNetId, now, out var nozzle)
                || !TryGetFreshPlayerPose(session, message.PlayerId, now, out var playerPosition))
                return false;

            Vector3 vehiclePosition = item.Body.transform.position;
            if ((playerPosition - vehiclePosition).sqrMagnitude > PlayerVehicleMaxDistance * PlayerVehicleMaxDistance
                || (vehiclePosition - nozzle.Transform.position).sqrMagnitude > VehicleNozzleMaxDistance * VehicleNozzleMaxDistance
                || (playerPosition - nozzle.Transform.position).sqrMagnitude > PlayerNozzleMaxDistance * PlayerNozzleMaxDistance)
                return false;

            // A different player actively moving the car must not have its tank
            // overwritten by someone still standing at the pump.
            if ((item.RemoteOwner != WorldSyncIds.NoOwner && item.RemoteOwner != message.PlayerId)
                || item.Body.velocity.sqrMagnitude > MaxStationaryVelocitySqr)
                return false;

            // One physical filler neck: while a transfer is in flight, another player's
            // intents are rejected rather than taking over the slot. Without this,
            // alternating senders each reset the rate window below (fresh allowance +
            // fixed elapsed floor), roughly doubling the per-vehicle fill cap, and each
            // handover skipped the sequence dedup.
            if (item.LastFuelTransferPlayer != message.PlayerId
                && item.LastFuelTransferAt > 0f
                && now - item.LastFuelTransferAt < FuelTransferHoldSeconds)
                return false;

            if (item.LastFuelTransferPlayer == message.PlayerId)
            {
                ushort diff = (ushort)(message.Sequence - item.LastFuelTransferSequence);
                if (diff == 0 || diff > short.MaxValue) return false;
            }

            EnsureVehicleSystemsProbe(item);
            if (!TryReadFuelTank(item, out float current, out float capacity)) return false;

            float requested = message.TargetFuelLevel / 255f * capacity;
            if (requested <= current + FuelLevelEpsilon) return false;

            float elapsed = item.LastFuelTransferPlayer == message.PlayerId
                ? Mathf.Max(0f, now - item.LastFuelTransferAt)
                : FuelIntentIntervalSeconds;
            float maximum = Mathf.Min(capacity, current + InitialFuelAllowanceLiters + elapsed * MaxFuelLitersPerSecond);
            float accepted = Mathf.Min(requested, maximum);
            if (accepted <= current + FuelLevelEpsilon) return false;

            WriteFuelTank(item, accepted, capacity);
            item.LastFuelTransferPlayer = message.PlayerId;
            item.LastFuelTransferSequence = message.Sequence;
            item.LastFuelTransferAt = now;

            state = TryBuildVehicleStateMessage(item, session.LocalPlayerId) ?? new VehicleState
            {
                VehicleId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = VehicleState.SnapshotSequence,
                FuelLevel = NormalizeFuelLevel(accepted, capacity),
            };
            WinterMPPlugin.Log.LogDebug(
                $"FuelSync: accepted player {message.PlayerId} -> '{item.Path}' {current:0.00}L to {accepted:0.00}L.");
            return true;
        }

        private bool TryFindDispensingNozzle(float now, out StationNozzle nozzle)
        {
            nozzle = null!;
            RefreshStationNozzles(now);
            foreach (var candidate in _stationNozzles.Values)
            {
                if (candidate.Fsm == null || candidate.Transform == null) continue;
                try
                {
                    string state = candidate.Fsm.Fsm.ActiveStateName;
                    if (state == "Calculate" || state == "Wait drop" || state == "Pump stop")
                    {
                        nozzle = candidate;
                        return true;
                    }
                }
                catch
                {
                    // A nozzle can be inactive while the station scene is loading.
                }
            }
            return false;
        }

        private bool TryGetStationNozzle(uint id, float now, out StationNozzle nozzle)
        {
            RefreshStationNozzles(now);
            if (_stationNozzles.TryGetValue(id, out var found))
            {
                nozzle = found;
                return true;
            }

            nozzle = null!;
            return false;
        }

        private void RefreshStationNozzles(float now)
        {
            if (now < _nextNozzleProbeAt) return;
            _nextNozzleProbeAt = now + NozzleProbeIntervalSeconds;
            _stationNozzles.Clear();

            try
            {
                foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.FsmName != "Use" || !fsm.gameObject.name.StartsWith("FuelTrigger"))
                        continue;

                    string path = ScenePath.Of(fsm.transform);
                    if (!path.StartsWith(FuelStationPathPrefix, StringComparison.Ordinal)
                        || path.IndexOf("/Nozzles/", StringComparison.Ordinal) < 0)
                        continue;

                    uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
                    _stationNozzles[id] = new StationNozzle { NetId = id, Fsm = fsm, Transform = fsm.transform };
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("FuelSync: station-nozzle probe failed: " + e.Message);
            }
        }

        private static bool TryGetFreshPlayerPose(SessionManager session, byte playerId, float now, out Vector3 position)
        {
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > PlayerPoseMaxAgeSeconds)
                    break;
                position = player.Position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private static bool TryReadFuelTank(SyncedItem item, out float level, out float capacity)
        {
            level = 0f;
            capacity = 0f;
            if (item.FuelTankLevelVar == null) return false;

            capacity = item.FuelTankCapacityVar != null ? item.FuelTankCapacityVar.Value : FuelTankDefaultLiters;
            if (capacity <= 0f || float.IsNaN(capacity) || float.IsInfinity(capacity))
                capacity = FuelTankDefaultLiters;

            level = item.FuelTankLevelVar.Value;
            return !float.IsNaN(level) && !float.IsInfinity(level);
        }

        private static void WriteFuelTank(SyncedItem item, float level, float capacity)
        {
            float clamped = Mathf.Clamp(level, 0f, capacity);
            if (item.FuelTankLevelVar != null)
                item.FuelTankLevelVar.Value = clamped;
            if (item.GaugeFuelLevelVar != null)
                item.GaugeFuelLevelVar.Value = clamped / capacity;
        }

        private static byte NormalizeFuelLevel(float level, float capacity)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(level / capacity * 255f), 0, 255);
        }
    }
}
