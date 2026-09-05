using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private const float PendingTtlSeconds = 120f;
        private const float SnapshotPoseTtlSeconds = 300f;
        private static readonly string[] AllowedRawEvents = { "TIGHTEN", "UNTIGHTEN" };

        private readonly WorldSyncBridge _bridge;
        private readonly ItemWorldSync _items;

        public VehicleWorldSync(WorldSyncBridge bridge, ItemWorldSync items)
        {
            _bridge = bridge;
            _items = items;
        }


        private const float VehicleStateRateHz = 4f;
        /// <summary>Frost + heater knobs — slow enough to save bandwidth, fast enough to feel live.</summary>
        private const float VehicleClimateRateHz = 2f;
        private const float HeaterTempMax = 30f;
        private const float HeaterBlowerMax = 4f;
        private const float HeaterDirectionMax = 4f;
        /// <summary>Cabin/glass temp can read well below zero on a cold parked car — clamping
        /// it to [0, max] like the heater-knob settings would floor every unheated car at 0°C
        /// on observers (same divergence class as the pre-v28 frost/ice merge bug).</summary>
        private const float CabinTempMinC = -40f;
        private const float CabinTempMaxC = 40f;
        private const float CoolantTempMaxC = 120f;
        private const float ClimateProbeIntervalSeconds = 3f;
        /// <summary>Keep pushing frost/defrost visuals after the last climate packet.</summary>
        private const float ClimateHoldSeconds = 3f;
        private const float DefrostPulseSeconds = 0.5f;
        /// <summary>Throttle for the climate desync diagnostic trace (temporary).</summary>
        private const float ClimateDiagIntervalSeconds = 2f;
        /// <summary>Remote engine audio stops when no state arrived for this long.</summary>
        private const float EngineAudioHoldSeconds = 2f;
        private const float EnginePitchBase = 0.55f;
        private const float EnginePitchPerRpm = 1f / 6500f;
        private const float EnginePitchMin = 0.65f;
        private const float EnginePitchMax = 1.65f;
        private const float EngineAudioVolume = 0.8f;
        private const float SystemsProbeIntervalSeconds = 3f;
        /// <summary>FuelLine.Revs above this counts as engine running (idle ~800).</summary>
        private const float EngineRunningRevs = 250f;
        private const float TachMaxRpm = 7000f;
        private const float SpeedoMaxKmh = 140f;
        private const float FuelTankDefaultLiters = 30f;

        /// <summary>Host broadcasts the clock/weather this often (drift is slow).</summary>
        internal void LateUpdateRemoteVehicles(float now)
        {
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.LocallyOwned || item.Body == null) continue;

                // Spin the wheels of any car under a live remote stream (driven or pushed) so
                // it stops skating on frozen tires. Independent of the climate hold below,
                // which gates on climate packets; a still car self-limits (zero displacement).
                if (item.RemoteOwner != WorldSyncIds.NoOwner)
                    RollRemoteWheels(item);

                if (now >= item.RemoteClimateUntil) continue;
                UpdateRemoteClimatePresentation(item, now);
            }
        }

        internal IEnumerable<IMessage> BuildVehicleResyncMessages()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) yield break;

            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle) continue;
                foreach (var message in BuildVehicleStateMessages(item, session.LocalPlayerId))
                    yield return message;
            }
        }

        internal IEnumerable<IMessage> BuildVehicleStateMessages(SyncedItem item, byte ownerPlayerId)
        {
            var state = TryBuildVehicleStateMessage(item, ownerPlayerId);
            if (state != null) yield return state;

            var climate = TryBuildVehicleClimate(item);
            if (climate != null)
            {
                climate.OwnerPlayerId = ownerPlayerId;
                yield return climate;
            }

            var damage = TryBuildDamageSnapshot(item, ownerPlayerId);
            if (damage != null) yield return damage;

            var condition = TryBuildConditionSnapshot(item, ownerPlayerId);
            if (condition != null) yield return condition;
        }

        // Joins and targeted repairs need the same complete state, including healthy
        // parts: a zero damage mask must correct breakage inherited from a guest's save.
        internal IEnumerable<IMessage> BuildJoinVehicleSnapshots()
        {
            foreach (var message in BuildVehicleResyncMessages())
                yield return message;
        }

        internal bool TryReadVehicleChecksum(SyncedItem item, out byte flags, out ushort rpm, out byte fuel,
            out byte coolant, out byte frost, out byte fog, out byte cabinTemp)
        {
            flags = 0;
            rpm = 0;
            fuel = 0;
            coolant = 0;
            frost = 0;
            fog = 0;
            cabinTemp = 0;
            if (!item.IsVehicle || item.Body == null) return false;

            if (item.LocallyOwned || item.RemoteOwner == WorldSyncIds.NoOwner)
            {
                EnsureVehicleSystemsProbe(item);
                if (!item.SystemsReady) return false;

                float revs = ReadBestRpm(item);
                bool engineOn = revs > EngineRunningRevs;
                bool accOn = ReadAccOn(item) || engineOn;
                if (engineOn) flags |= VehicleState.FlagEngineOn;
                if (accOn) flags |= VehicleState.FlagAccOn;
                if (ReadBlinkerLeft(item)) flags |= VehicleState.FlagBlinkerLeft;
                if (ReadBlinkerRight(item)) flags |= VehicleState.FlagBlinkerRight;
                if (ReadHazardOn(item)) flags |= VehicleState.FlagHazard;
                rpm = (ushort)Mathf.Clamp(revs, 0f, ushort.MaxValue);
                fuel = ReadFuelLevelByte(item);
                coolant = ReadCoolantTempByte(item);

                EnsureClimateProbe(item);
                if (item.ClimateReady)
                {
                    frost = QuantizeFrost(ReadFrost(item));
                    fog = QuantizeFrost(ReadFog(item));
                    cabinTemp = QuantizeRange(ReadCabinTemp(item), CabinTempMinC, CabinTempMaxC);
                }

                return true;
            }

            if (item.RemoteEngineOn) flags |= VehicleState.FlagEngineOn;
            if (item.RemoteAccOn) flags |= VehicleState.FlagAccOn;
            if (item.RemoteBlinkerLeft) flags |= VehicleState.FlagBlinkerLeft;
            if (item.RemoteBlinkerRight) flags |= VehicleState.FlagBlinkerRight;
            if (item.RemoteHazard) flags |= VehicleState.FlagHazard;
            rpm = (ushort)Mathf.Clamp(item.RemoteRpm, 0f, ushort.MaxValue);
            fuel = item.RemoteFuelLevel;
            coolant = item.RemoteCoolantTemp;
            frost = item.RemoteFrost;
            fog = item.RemoteFog;
            cabinTemp = item.RemoteCabinTemp;
            return true;
        }

        private VehicleState? TryBuildVehicleStateMessage(SyncedItem item, byte ownerPlayerId)
        {
            if (!item.IsVehicle || item.Body == null) return null;

            EnsureVehicleSystemsProbe(item);
            if (!item.SystemsReady) return null;

            float revs = ReadBestRpm(item);
            bool engineOn = revs > EngineRunningRevs;
            bool accOn = ReadAccOn(item) || engineOn;
            float speedKmh = item.GaugeSpeedVar != null ? item.GaugeSpeedVar.Value : 0f;

            byte flags = 0;
            if (engineOn) flags |= VehicleState.FlagEngineOn;
            if (accOn) flags |= VehicleState.FlagAccOn;
            if (ReadBlinkerLeft(item)) flags |= VehicleState.FlagBlinkerLeft;
            if (ReadBlinkerRight(item)) flags |= VehicleState.FlagBlinkerRight;
            if (ReadHazardOn(item)) flags |= VehicleState.FlagHazard;

            return new VehicleState
            {
                VehicleId = item.Id,
                OwnerPlayerId = ownerPlayerId,
                // Snapshot/resync path (the live stream sets its own Sequence). Without
                // this the message defaults to Sequence 0 and the receiver's dedup drops
                // it against a fresh guest's LastVehicleStateSequence (also 0).
                Sequence = VehicleState.SnapshotSequence,
                Flags = flags,
                Rpm = (ushort)Mathf.Clamp(revs, 0f, ushort.MaxValue),
                SpeedTenthsKmh = (ushort)Mathf.Clamp(speedKmh * 10f, 0f, ushort.MaxValue),
                FuelLevel = ReadFuelLevelByte(item),
                CoolantTemp = ReadCoolantTempByte(item),
            };
        }
    }
}
