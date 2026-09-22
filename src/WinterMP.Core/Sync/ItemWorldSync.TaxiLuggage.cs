using System;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly SyncedItem?[] _taxiLuggage = new SyncedItem?[5];
        private byte _taxiLuggageMask;
        internal void RegisterTaxiLuggage(Rigidbody[] bodies, uint epoch)
        {
            UnregisterTaxiLuggage();
            for (int i = 0; i < 5; i++)
            {
                var body = bodies[i]; uint id = TaxiServicePolicy.LuggageItemId(epoch, i);
                if (_items.TryGetValue(id, out var collision) && collision.Body != body) throw new InvalidOperationException("Taxi luggage identity collision.");
                uint oldId = 0;
                foreach (var pair in _items) if (pair.Value.Body == body) { oldId = pair.Key; break; }
                if (oldId != 0) _items.Remove(oldId);
                var item = new SyncedItem { Id = id, Body = body, Path = "taxi:luggage:" + i,
                    LastPosition = body.position, LastMovedAt = Time.unscaledTime, KinematicSaved = true, OriginalKinematic = body.isKinematic };
                _taxiLuggage[i] = item; _items[id] = item; _trackedBodies[body] = true;
            }
        }
        private int TaxiLuggageSlot(Rigidbody body)
        {
            for (int i = 0; i < 5; i++) if (_taxiLuggage[i]?.Body == body) return i;
            return -1;
        }
        private bool IsTaxiLuggage(SyncedItem item) => Array.IndexOf(_taxiLuggage, item) >= 0;
        internal void SetTaxiLuggageMask(byte mask)
        {
            for (int i = 0; i < 5; i++)
            {
                var item = _taxiLuggage[i]; if (item == null || item.Body == null) continue;
                bool active = (mask & (1 << i)) != 0;
                if (active)
                { if ((_taxiLuggageMask & (1 << i)) == 0) item.Body.isKinematic = false; }
                else
                {
                    ReleaseHeldBag(item.Body);
                    RestoreCargoPhysics(item);
                    item.LocallyOwned = false; item.RemoteOwner = WorldSyncIds.NoOwner;
                    item.LastRemoteAt = -999; item.RemoteCargoVehicleId = item.LocalCargoVehicleId = 0;
                    item.Body.isKinematic = true; item.Body.velocity = item.Body.angularVelocity = Vector3.zero;
                }
            }
            _taxiLuggageMask = mask;
        }
        internal void UnregisterTaxiLuggage()
        {
            for (int i = 0; i < 5; i++)
            {
                var item = _taxiLuggage[i]; if (item == null) continue;
                ReleaseHeldBag(item.Body);
                RestoreCargoPhysics(item);
                if (item.Body != null) { item.Body.isKinematic = item.OriginalKinematic; _trackedBodies.Remove(item.Body); }
                _items.Remove(item.Id); _taxiLuggage[i] = null;
            }
            _taxiLuggageMask = 0;
        }
    }
}
