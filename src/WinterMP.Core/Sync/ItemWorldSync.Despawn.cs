using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private const float GuestDespawnPoseMaxAgeSeconds = 2f;
        private const float GuestDespawnMaxDistance = 6f;

        private static bool SessionSyncActive(SessionManager session)
        {
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                return false;
            return session.IsHost ? session.PlayerCount > 0 : true;
        }

        private void AnnounceItemDespawn(uint itemId, string reason)
        {
            if (!_items.TryGetValue(itemId, out var item) || item.DespawnSent) return;

            var session = SessionManager.Instance;
            if (session == null || !SessionSyncActive(session)) return;

            item.DespawnSent = true;
            TrackSessionDespawn(itemId);
            WinterMPPlugin.Log.LogInfo($"WorldSync: item {itemId:X8} despawn — {reason} (local).");
            SyncEventLog.Record("despawn", $"{itemId:X8} {reason}");
            session.SendWorldMessage(new ItemDespawn { ItemId = itemId }, Channel.ReliableOrdered);
        }

        private void TrackSessionDespawn(uint itemId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            _sessionDespawnedItems.Add(itemId);
        }

        public void OnRemoteItemDespawn(ItemDespawn message)
        {
            TrackSessionDespawn(message.ItemId);

            if (!_items.TryGetValue(message.ItemId, out var item)) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: item {message.ItemId:X8} despawn (remote).");
            item.DespawnSent = true;

            _bridge.ApplyingRemote = true;
            try
            {
                if (item.Body != null)
                    UnityEngine.Object.Destroy(item.Body.gameObject);
            }
            finally
            {
                _bridge.ApplyingRemote = false;
            }

            RemoveTrackedItem(message.ItemId, item.Body);
            ReleaseCargoFollowingVehicle(message.ItemId);
        }

        /// <summary>
        /// A guest may consume/destroy only an item it still owns in the host's
        /// transform authority table and is physically beside. Without this gate a
        /// raw ItemDespawn packet can delete any tracked object on every peer.
        /// </summary>
        public bool TryAcceptGuestDespawn(ItemDespawn message, byte playerId)
        {
            if (!_items.TryGetValue(message.ItemId, out var item) || item.Body == null
                || (item.RemoteOwner != playerId && item.RemoteOwner != WorldSyncIds.NoOwner))
                return false;

            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > GuestDespawnPoseMaxAgeSeconds)
                    return false;
                if ((player.Position - item.Body.transform.position).sqrMagnitude
                    > GuestDespawnMaxDistance * GuestDespawnMaxDistance)
                    return false;
                OnRemoteItemDespawn(message);
                return true;
            }
            return false;
        }

        private void RemoveTrackedItem(uint itemId, Rigidbody? body)
        {
            _items.Remove(itemId);
            if (body != null)
                _trackedBodies.Remove(body);
        }

        /// <summary>
        /// When a vehicle is despawned, any loose item riding it would otherwise stay
        /// kinematic-pinned in mid-air against a dead vehicle id. Release every pin
        /// (restoring physics) and forget local cargo membership for that vehicle.
        /// </summary>
        private void ReleaseCargoFollowingVehicle(uint vehicleId)
        {
            ReleaseRemoteCargoForVehicle(vehicleId, Time.unscaledTime, seedVelocity: false);
            foreach (var other in _items.Values)
            {
                if (other.LocalCargoVehicleId == vehicleId)
                {
                    other.LocalCargoVehicleId = 0;
                    RestoreCargoPhysics(other);
                }
            }
        }
    }
}
