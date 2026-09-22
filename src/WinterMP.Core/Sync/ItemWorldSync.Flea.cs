using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal bool FleaCanList(SyncedItem item) => item.Body != null && !item.IsVehicle && !item.DespawnSent
            && !_spawnLifecycle.IsRetired(item.Id) && !item.FleaListed && CanSyncItemMotion(item)
            && item.RemoteOwner == WorldSyncIds.NoOwner && item.RemoteCargoVehicleId == 0 && item.LocalCargoVehicleId == 0
            && item.Body.gameObject.activeInHierarchy && !IsHeldByLocalPlayer(item.Body);
        internal bool FleaRetired(uint id) => _spawnLifecycle.IsRetired(id);
        internal void RetireFleaItem(uint id)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !_items.TryGetValue(id, out var item) || _spawnLifecycle.IsRetired(id)) return;
            item.DespawnSent = true; RecordItemRetirement(id);
            session.SendWorldMessage(new ItemDespawn { ItemId = id }, Channel.ReliableOrdered);
        }
    }
}
