using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
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
        }

        private void RemoveTrackedItem(uint itemId, Rigidbody? body)
        {
            _items.Remove(itemId);
            if (body != null)
                _trackedBodies.Remove(body);
        }
    }
}
