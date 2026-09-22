using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FleaSaleSync
    {
        private ItemWorldSync _items = null!;
        private FleaListingState? _listingState, _listingReceived;
        private readonly FleaListingReceipts _listingReceipts = new FleaListingReceipts();
        private readonly Dictionary<uint, ListingPin> _listingPins = new Dictionary<uint, ListingPin>();
        private readonly Dictionary<uint, float> _offerAfter = new Dictionary<uint, float>();
        private FleaListingIntent? _listingPending;
        private uint _offered;
        private ushort _listingSequence;
        private float _listingTick, _listingKeepAlive, _listingRetry;
        public void BindItems(ItemWorldSync items) { _items = items; }

        private sealed class ListingPin
        {
            internal SyncedItem Item = null!;
            internal Transform? Parent;
            internal bool Kinematic;
            internal int Layer;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal void Apply(Transform table)
            {
                var b = Item.Body;
                if (b == null) return;
                Item.FleaListed = true; Item.LocallyOwned = false; Item.RemoteOwner = WorldSyncIds.NoOwner;
                Item.KinematicSaved = false;
                b.transform.parent = table; b.gameObject.layer = 0; b.isKinematic = true;
                b.position = Position; b.rotation = Rotation;
            }
            internal void Restore()
            {
                Item.FleaListed = false;
                var b = Item.Body;
                if (b == null) return;
                b.transform.parent = Parent; b.gameObject.layer = Layer; b.isKinematic = Kinematic;
            }
        }
        private void ClearListings()
        {
            bool host = _binding != null && !_binding.IsGuest;
            foreach (var pin in _listingPins.Values)
            {
                if (!host) pin.Restore();
                else pin.Item.FleaListed = false;
            }
            _listingPins.Clear(); _offerAfter.Clear(); _listingReceipts.Clear();
            _listingState = _listingReceived = null; _listingPending = null; _offered = 0; _listingSequence = 0;
            _listingTick = _listingKeepAlive = _listingRetry = 0;
        }
        private bool NativeListingItem(SyncedItem item, out uint number, out PlayMakerFSM? use)
        {
            number = 0; use = null;
            if (_binding == null || item.Body == null || item.Body.name != _binding.ListingName) return false;
            foreach (var f in item.Body.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Use") { use = f; break; }
            if (use == null || !use.Fsm.Initialized || !use.Fsm.Started || use.FsmVariables.FindFsmBool("Consumed")?.Value != false) return false;
            return FleaListingPolicy.TryNumber(use.FsmVariables.FindFsmString("ID")?.Value ?? "", _binding.ListingIdPrefix, out number);
        }
        private void UpdateListings(SessionManager session)
        {
            if (Time.unscaledTime >= _listingTick)
            {
                _listingTick = Time.unscaledTime + .25f;
                if (session.IsHost) PublishListings(session, Time.unscaledTime >= _listingKeepAlive);
                else if (_listingReceived != null) PinListings(_listingReceived);
                if (!session.IsHost && _binding!.ListingsAvailable && !_binding.PricingOpen && _listingPending == null)
                {
                    foreach (var item in _items.Items.Values)
                    {
                        if (item.Body == null || item.Body.name != _binding.ListingName || !_items.FleaCanList(item)
                            || !_binding.OnTable(item.Body.position) || item.Body.velocity.sqrMagnitude > .0001f) continue;
                        OfferListing(item.Body.gameObject); break;
                    }
                }
            }
            foreach (var pin in _listingPins.Values) pin.Apply(_binding!.Table);
            if (_offered != 0 && (_listingPins.ContainsKey(_offered) || !_binding!.ListingsAvailable || _items.FleaRetired(_offered)))
            {
                _offered = 0; _binding!.CancelSharedPrice();
            }
            if (_listingPending != null && Time.unscaledTime >= _listingRetry)
            {
                _listingRetry = Time.unscaledTime + 1;
                if (session.IsHost) TryAcceptListing(_listingPending);
                else session.SendWorldMessage(_listingPending, Channel.ReliableOrdered);
            }
        }
        private void OfferListing(GameObject go)
        {
            try
            {
                var s = SessionManager.Instance;
                if (s == null || _failed || _binding == null || !_binding.ListingsAvailable || _binding.PricingOpen || _listingPending != null
                    || !GamblingSync.TryPlayerPosition(s, s.LocalPlayerId, out var position) || (position - _binding.Table.position).sqrMagnitude > 16) return;
                foreach (var item in _items.Items.Values)
                {
                    if (item.Body == null || item.Body.gameObject != go || !_items.FleaCanList(item) || !_binding.OnTable(item.Body.position)) continue;
                    if (_offerAfter.TryGetValue(item.Id, out float until) && Time.unscaledTime < until) return;
                    _offered = item.Id; _binding.OpenPrice(); return;
                }
            }
            catch (Exception e) { Fail(e); }
        }
        private void SubmitListing(float price)
        {
            try
            {
                uint id = _offered; _offered = 0;
                if (id == 0) return;
                _offerAfter[id] = Time.unscaledTime + 3;
                var s = SessionManager.Instance;
                var quote = s != null && s.IsHost ? CaptureListings() : _listingReceived;
                if (s == null || quote == null || price < 0 || price > 999 || price != Math.Floor(price) || float.IsNaN(price)) return;
                _listingPending = new FleaListingIntent { PlayerId = s.LocalPlayerId, Sequence = ++_listingSequence,
                    ItemId = id, Price = (ushort)price, Revision = quote.Revision }; _listingRetry = 0;
            }
            catch (Exception e) { Fail(e); }
        }
        public bool TryAcceptListing(FleaListingIntent request)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !FleaListingPolicy.Valid(request)) return false;
            try
            {
                if (!Locate(session)) return false;
                if (!_listingReceipts.TryGet(request, out byte result))
                {
                    var current = CaptureListings();
                    result = FleaListingResult.Unavailable;
                    if (current.Revision != request.Revision) result = FleaListingResult.Changed;
                    else if (!GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)
                        || (position - _binding!.Table.position).sqrMagnitude > 16) result = FleaListingResult.Distant;
                    else if (_binding!.ListingsAvailable && current.Items.Count < FleaListingPolicy.Capacity
                        && _items.Items.TryGetValue(request.ItemId, out var item) && NativeListingItem(item, out uint number, out _))
                    {
                        string key = FleaListingPolicy.Key(_binding.ListingName, number);
                        if (!_items.FleaCanList(item)) result = FleaListingResult.Claimed;
                        else if (_binding.OnTable(item.Body.position) && item.Body.velocity.sqrMagnitude <= .0025f
                            && !_binding.TryPrice(key, out _))
                        {
                            bool unique = true;
                            foreach (var other in _items.Items.Values)
                                if (other.Id != item.Id && NativeListingItem(other, out uint n, out _) && n == number) unique = false;
                            if (unique)
                            {
                                _binding.AddListing(key, request.Price);
                                Pin(item, item.Body.position, item.Body.rotation);
                                result = FleaListingResult.Accepted;
                            }
                        }
                    }
                    _listingReceipts.Record(request, result);
                    SyncEventLog.Record("flea-list", "item " + request.ItemId + " player " + request.PlayerId + " seq " + request.Sequence + " result " + result);
                }
                PublishListings(session, true);
                var receipt = new FleaListingResult { PlayerId = request.PlayerId, Sequence = request.Sequence, ItemId = request.ItemId, Result = result };
                session.SendWorldMessage(receipt, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnListingResult(receipt);
                return result == FleaListingResult.Accepted;
            }
            catch (Exception e) { Fail(e); return false; }
        }
        public void OnListingResult(FleaListingResult result)
        {
            if (_listingPending == null || result.PlayerId != _listingPending.PlayerId || result.Sequence != _listingPending.Sequence
                || result.ItemId != _listingPending.ItemId) return;
            _listingPending = null;
            if (result.Result != FleaListingResult.Accepted)
                SessionManager.Instance?.AddSystemChat("* Listing declined. Drop the packet on the rented table and try again once it has stopped moving.");
        }
        private FleaListingState CaptureListings()
        {
            _binding!.ClearExpiredListings();
            var next = new FleaListingState();
            foreach (var item in _items.Items.Values)
            {
                if (_items.FleaRetired(item.Id) || !NativeListingItem(item, out uint number, out _)) continue;
                if (!_binding!.TryPrice(FleaListingPolicy.Key(_binding.ListingName, number), out float price)) continue;
                if (price < 0 || price > 999 || price != Math.Floor(price)) throw new InvalidOperationException("Invalid native shared listing price.");
                _listingPins.TryGetValue(item.Id, out var pin);
                next.Items.Add(new FleaListingState.Entry { ItemId = item.Id, NativeNumber = number, Price = (ushort)price,
                    Position = (pin == null ? item.Body.position : pin.Position).ToNet(),
                    Rotation = (pin == null ? item.Body.rotation : pin.Rotation).ToNet() });
            }
            next.Items.Sort((a, b) => a.ItemId.CompareTo(b.ItemId));
            if (!FleaListingPolicy.Valid(next)) throw new InvalidOperationException("Ambiguous native flea identities.");
            next.Revision = _listingState == null ? 1u : _listingState.Revision;
            if (_listingState != null && !FleaListingPolicy.Same(_listingState, next)) next.Revision = unchecked(next.Revision + 1);
            _listingState = FleaListingPolicy.Copy(next); PinListings(next); return next;
        }
        public FleaListingState? BuildListingSnapshot()
        {
            try { var s = SessionManager.Instance; return s != null && s.IsHost && Locate(s) ? CaptureListings() : null; }
            catch (Exception e) { Fail(e); return null; }
        }
        private void PublishListings(SessionManager session, bool force)
        {
            uint? old = _listingState?.Revision;
            var next = CaptureListings();
            if (!force && old == next.Revision) return;
            _listingKeepAlive = Time.unscaledTime + 10;
            session.SendWorldMessage(next, Channel.ReliableOrdered);
        }
        public void ApplyListings(FleaListingState state)
        {
            var s = SessionManager.Instance;
            if (s == null || s.IsHost || !FleaListingPolicy.CanApply(_listingReceived, state)) return;
            _listingReceived = FleaListingPolicy.Copy(state);
            try { if (Locate(s)) PinListings(state); } catch (Exception e) { Fail(e); }
        }
        private void PinListings(FleaListingState state)
        {
            var remaining = new HashSet<uint>();
            foreach (var entry in state.Items)
            {
                remaining.Add(entry.ItemId);
                if (!_items.FleaRetired(entry.ItemId) && _items.Items.TryGetValue(entry.ItemId, out var item) && item.Body != null)
                {
                    if (item.IsVehicle || item.Body.name != _binding!.ListingName)
                        throw new InvalidOperationException("Shared flea listing resolved to a different item family.");
                    Pin(item, entry.Position.ToUnity(), entry.Rotation.ToUnity());
                }
            }
            foreach (uint id in new List<uint>(_listingPins.Keys)) if (!remaining.Contains(id) || _items.FleaRetired(id))
            {
                if (!_items.FleaRetired(id)) _listingPins[id].Restore();
                _listingPins.Remove(id);
            }
        }
        private void Pin(SyncedItem item, Vector3 position, Quaternion rotation)
        {
            if (!_listingPins.TryGetValue(item.Id, out var pin))
            {
                // Native loading may freeze a saved product before item discovery.
                // Expiry must restore the loose product, not that loading-time pin.
                bool nativePin = item.Body.transform.parent == _binding!.Table;
                pin = new ListingPin { Item = item, Parent = nativePin ? null : item.Body.transform.parent,
                    Layer = nativePin ? 19 : item.Body.gameObject.layer,
                    Kinematic = !nativePin && (item.KinematicSaved ? item.OriginalKinematic : item.Body.isKinematic) };
                _listingPins.Add(item.Id, pin);
            }
            pin.Position = position; pin.Rotation = rotation; pin.Apply(_binding!.Table);
        }
        private void SellListing(string key)
        {
            try
            {
                var s = SessionManager.Instance;
                if (s == null || !s.IsHost || _failed || _binding == null || !_binding.ListingsAvailable || !_binding.TryPrice(key, out _)) return;
                var state = CaptureListings();
                foreach (var entry in state.Items)
                {
                    if (key != FleaListingPolicy.Key(_binding.ListingName, entry.NativeNumber)
                        || !_items.Items.TryGetValue(entry.ItemId, out var item) || !NativeListingItem(item, out _, out _)) continue;
                    var go = item.Body.gameObject;
                    _binding.SellListing(key, go);
                    _items.RetireFleaItem(entry.ItemId); _listingPins.Remove(entry.ItemId);
                    PublishListings(s, true); Publish(s, true);
                    SyncEventLog.Record("flea-sold", "item " + entry.ItemId + " key " + key + " price " + entry.Price);
                    return;
                }
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
