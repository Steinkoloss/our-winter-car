using System;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private SyncedItem? _taxiReceipt;
        private bool _taxiReceiptLoose;
        internal void RegisterTaxiReceipt(Rigidbody body)
        {
            if (_taxiReceipt != null) throw new InvalidOperationException("Taxi receipt already bound.");
            uint id = TaxiFarePolicy.ReceiptItemId;
            if (_items.TryGetValue(id, out var collision) && collision.Body != body) throw new InvalidOperationException("Taxi receipt identity collision.");
            uint oldId = 0;
            foreach (var pair in _items) if (pair.Value.Body == body) { oldId = pair.Key; break; }
            if (oldId != 0) _items.Remove(oldId);
            _taxiReceipt = new SyncedItem { Id = id, Body = body, Path = "taxi:receipt", LastPosition = body.position,
                LastMovedAt = Time.unscaledTime, OriginalKinematic = body.isKinematic, KinematicSaved = true };
            _items[id] = _taxiReceipt; _trackedBodies[body] = true; _taxiReceiptLoose = false;
        }
        internal void SetTaxiReceiptLoose(bool loose)
        {
            if (_taxiReceipt == null) return;
            bool changed = _taxiReceiptLoose != loose; _taxiReceiptLoose = loose;
            if (loose) { if (changed && _taxiReceipt.Body != null) _taxiReceipt.Body.isKinematic = false; return; }
            var item = _taxiReceipt;
            ReleaseHeldBag(item.Body);
            item.LocallyOwned = false; item.RemoteOwner = WorldSyncIds.NoOwner;
            item.LastRemoteAt = -999; item.RemoteCargoVehicleId = 0;
            if (item.Body != null) { item.Body.isKinematic = true; item.Body.velocity = item.Body.angularVelocity = Vector3.zero; }
        }
        internal bool TaxiReceiptHeldLocally => _taxiReceipt != null && IsHeldByLocalPlayer(_taxiReceipt.Body);
        internal bool TaxiReceiptHeldBy(byte actor)
        {
            var item = _taxiReceipt; var session = SessionManager.Instance;
            if (item == null || session == null || !_taxiReceiptLoose) return false;
            if (actor == session.LocalPlayerId) return TaxiReceiptHeldLocally;
            return !TaxiReceiptHeldLocally && item.RemoteOwner == actor && Time.unscaledTime - item.LastRemoteAt <= 2;
        }
        internal void UnregisterTaxiReceipt()
        {
            if (_taxiReceipt == null) return;
            ReleaseHeldBag(_taxiReceipt.Body);
            if (_taxiReceipt.Body != null && _taxiReceipt.KinematicSaved) _taxiReceipt.Body.isKinematic = _taxiReceipt.OriginalKinematic;
            _items.Remove(_taxiReceipt.Id); if (_taxiReceipt.Body != null) _trackedBodies.Remove(_taxiReceipt.Body);
            _taxiReceipt = null; _taxiReceiptLoose = false;
        }
    }
}
