using System.Collections.Generic;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private const float SnapshotPoseTtlSeconds = 300f;
        private const int ItemSnapshotChunk = 40;

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

        internal IEnumerable<uint> SessionDespawnedIds => _sessionDespawnedItems;
        internal IDictionary<uint, SyncedItem> Items => _items;
        public int ItemCount => _items.Count;

        internal void Clear()
        {
            _items.Clear();
            _trackedBodies.Clear();
            _pendingItemPoses.Clear();
            _sessionDespawnedItems.Clear();
            _pendingDespawnedItems.Clear();
        }

        internal void ReleaseSession()
        {
            foreach (var item in _items.Values)
            {
                if (item.Body != null && item.RemoteOwner != WorldSyncIds.NoOwner)
                    item.Body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LocalDriveActive = false;
                item.LocallyOwned = false;
                item.LastRemoteAt = -999f;
                item.CargoFollowActive = false;
                item.CargoFollowVehicleId = 0;
                item.CargoFollowDriverId = WorldSyncIds.NoOwner;
                RestoreCargoColliders(item);
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
        }
    }
}
