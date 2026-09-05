using System.Collections.Generic;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private const float SnapshotPoseTtlSeconds = 300f;

        private readonly WorldSyncBridge _bridge;
        private VehicleWorldSync _vehicles = null!;

        private readonly Dictionary<uint, SyncedItem> _items = new Dictionary<uint, SyncedItem>();
        private readonly Dictionary<Rigidbody, bool> _trackedBodies = new Dictionary<Rigidbody, bool>();
        private readonly Dictionary<uint, PendingPose> _pendingItemPoses = new Dictionary<uint, PendingPose>();
        private readonly HashSet<uint> _sessionDespawnedItems = new HashSet<uint>();
        private readonly HashSet<uint> _pendingDespawnedItems = new HashSet<uint>();
        private readonly List<uint> _deadItemIds = new List<uint>();

        public ItemWorldSync(WorldSyncBridge bridge)
        {
            _bridge = bridge;
        }

        public void BindVehicles(VehicleWorldSync vehicles) => _vehicles = vehicles;

        /// <summary>
        /// A player was (re)admitted — its per-item sequence counters restarted. Downgrade
        /// every same-sender latch to the owner-change sentinel (and zero the sequence) so
        /// the next packet rebases instead of being stale-dropped; the per-player dict
        /// resets in OnPlayerAdmitted don't cover these per-item fields. Runs on EVERY
        /// client (PlayerSpawn carries the signal to guests). RemoteOwner itself is left
        /// alone — clearing it has physics side effects the stale-clear timers own.
        /// </summary>
        public void ForgetPlayerItemSequences(byte playerId)
        {
            foreach (var item in _items.Values)
            {
                if (item.LastRemoteSequenceOwner == playerId || item.RemoteOwner == playerId)
                {
                    item.LastRemoteSequenceOwner = WorldSyncIds.NoOwner;
                    item.LastRemoteSequence = 0;
                }
                if (item.LastDamageSequenceOwner == playerId)
                {
                    item.LastDamageSequenceOwner = WorldSyncIds.NoOwner;
                    item.LastDamageSequence = 0;
                    item.HasDamageSequence = false;
                }
                if (item.LastConditionSequenceOwner == playerId)
                {
                    item.LastConditionSequenceOwner = WorldSyncIds.NoOwner;
                    item.LastConditionSequence = 0;
                }
                if (item.LastClimateSequenceOwner == playerId)
                {
                    item.LastClimateSequenceOwner = WorldSyncIds.NoOwner;
                    item.LastClimateSequence = 0;
                }
                if (item.LastRemoteBrewOwner == playerId)
                {
                    item.LastRemoteBrewOwner = WorldSyncIds.NoOwner;
                    item.LastRemoteBrewSequence = 0;
                }
                if (item.LastRemoteFluidOwner == playerId)
                {
                    item.LastRemoteFluidOwner = WorldSyncIds.NoOwner;
                    item.LastRemoteFluidSequence = 0;
                }
            }
        }

        /// <summary>The LOCAL player's world position (subsystems validating the host's own actions).</summary>
        public bool TryGetLocalPlayerPosition(out Vector3 position)
        {
            _bridge.FindLocalPlayer();
            var player = _bridge.LocalPlayer;
            if (player == null)
            {
                position = Vector3.zero;
                return false;
            }
            position = player.position;
            return true;
        }

        internal IEnumerable<uint> SessionDespawnedIds => _sessionDespawnedItems;
        internal IDictionary<uint, SyncedItem> Items => _items;
        public int ItemCount => _items.Count;

        internal void Clear()
        {
            _vehicles?.ClearDamageHooks();
            _items.Clear();
            _trackedBodies.Clear();
            _pendingItemPoses.Clear();
            _sessionDespawnedItems.Clear();
            _pendingDespawnedItems.Clear();
            _cargoCandidates.Clear();
            _pendingSpawns.Clear();
            _handledSpawns.Clear();
            _hostSpawnManifests.Clear();
            _snapshotSeenIds.Clear();
            _spawnEpochs.Clear();
            _lastGuestSpawnSequences.Clear();
        }

        internal void ReleaseSession()
        {
            _vehicles?.ClearDamageHooks();
            float now = Time.unscaledTime;
            foreach (var item in _items.Values)
            {
                ReleaseRemoteCargo(item, item.Body, now, seedVelocity: false);
                if (item.Body != null && item.RemoteOwner != WorldSyncIds.NoOwner)
                    item.Body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.HasRemoteVelocity = false;
                item.LocalDriveActive = false;
                item.LocallyOwned = false;
                item.LastRemoteAt = -999f;
                item.LocalCargoVehicleId = 0;
                item.LocalCargoBlockedUntil = -999f;
                item.LocalCargoWasStreaming = false;
                RestoreCargoPhysics(item);
                item.NextCargoSendAt = 0f;
                item.LastRemoteCargoOwner = WorldSyncIds.NoOwner;
                item.RemoteCargoAnnounced = false;
                item.RemoteEngineUntil = -999f;
                item.RemoteClimateUntil = -999f;
                item.RemoteEngineOn = false;
                item.RemoteAccOn = false;
                item.RemoteElectricsApplied = false;
                if (item.RemoteEngineAudio != null && item.RemoteEngineAudio.isPlaying)
                    item.RemoteEngineAudio.Stop();
                SetSeatBlocked(item, false);
            }

            _pendingItemPoses.Clear();
            _pendingDespawnedItems.Clear();

            // Spawn bookkeeping is session-scoped: a restarted host re-mints from
            // epoch 1, so a surviving _handledSpawns would drop its first manifests
            // as "duplicates" (same container, same epoch, same hashed ids), and a
            // stale _snapshotSeenIds would poison the steal-soundness test on the
            // next join. Parked capture/offer clones go back to the scanner.
            foreach (var pending in _pendingSpawns)
            {
                foreach (var body in pending.Captured)
                {
                    if (body != null) _trackedBodies.Remove(body);
                }
            }
            _pendingSpawns.Clear();
            _handledSpawns.Clear();
            _hostSpawnManifests.Clear();
            _snapshotSeenIds.Clear();
            _spawnEpochs.Clear();
            _lastGuestSpawnSequences.Clear();
        }
    }
}
