using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed class AtfRefillSync
    {
        private sealed class Lease
        {
            internal uint BottleId;
            internal float AcceptedAt;
        }

        private readonly ItemWorldSync _items;
        private readonly AtfIntentLedger _ledger = new AtfIntentLedger();
        private readonly Dictionary<byte, Lease> _leases = new Dictionary<byte, Lease>();
        private AtfFillerBinding? _filler;
        private SyncedItem? _vehicle;
        private AtfFillerState? _observed, _sent, _received;
        private float _nextBind, _nextWarning, _nextSend, _nextKeepalive, _nextGuestPour;
        private uint _guestBottle;
        private ushort _sequence;
        private bool _failed;

        internal AtfRefillSync(ItemWorldSync items) { _items = items; }

        internal void Update(SessionManager session)
        {
            if (_failed) return;
            try
            {
                Bind(session);
                if (_filler == null) return;
                _filler.Tick();
                if (session.IsHost)
                {
                    Transfer(session);
                    Publish(session);
                }
                else UpdateGuest(session);
            }
            catch (Exception e) { Fail(e); }
        }

        private void Bind(SessionManager session)
        {
            var rule = SyncCatalog.AtfRefill;
            if (_filler != null || rule == null || Time.unscaledTime < _nextBind) return;
            _nextBind = Time.unscaledTime + 1f;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || item.Path != rule["rootPath"]) continue;
                try
                {
                    _filler = AtfFillerBinding.Bind(item, rule, !session.IsHost, OnLocalCap);
                    _vehicle = item;
                    if (!session.IsHost && _received != null) _filler.Apply(_received);
                    SyncEventLog.Record("atf-filler-bind", item.Id.ToString("X8"));
                }
                catch (Exception e)
                {
                    if (_filler != null) throw;
                    if (Time.unscaledTime >= _nextWarning)
                    {
                        _nextWarning = Time.unscaledTime + 30f;
                        WinterMPPlugin.Log.LogWarning("ATF filler waiting: " + e.Message);
                    }
                }
                return;
            }
        }

        internal AtfFillerState? BuildFillerState()
        {
            if (_failed || SessionManager.Instance?.IsHost != true || _filler == null || _vehicle == null) return null;
            try
            {
                bool available = _filler.Mounted;
                var state = new AtfFillerState { VehicleId = _vehicle.Id, Revision = 1,
                    Rotation = _filler.Rotation, OilLevel = available ? _filler.OilLevel : 0,
                    Flags = available ? AtfFillerState.FlagAvailable : (byte)0,
                    CapLocalPosition = _filler.CapLocalPosition.ToNet(),
                    CapLocalRotation = _filler.CapLocalRotation.ToNet() };
                if (!AtfPolicy.Valid(state)) throw new InvalidOperationException("Invalid native ATF filler state.");
                if (_observed != null)
                {
                    // Keep the published pose as the comparison anchor, so
                    // repeated sub-millimetre engine movement cannot hide drift.
                    if (AtfPolicy.Same(_observed, state)) return AtfPolicy.Copy(_observed);
                    state.Revision = _observed.Revision;
                    state.Revision = unchecked(state.Revision + 1); if (state.Revision == 0) state.Revision = 1;
                }
                _observed = state;
                return AtfPolicy.Copy(state);
            }
            catch (Exception e) { Fail(e); return null; }
        }

        private void Publish(SessionManager session)
        {
            float now = Time.unscaledTime;
            if (now < _nextSend || session.PlayerCount == 0) return;
            _nextSend = now + .1f;
            var state = BuildFillerState();
            if (state == null || (_sent != null && _sent.Revision == state.Revision && AtfPolicy.Same(_sent, state) && now < _nextKeepalive)) return;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
            _sent = state; _nextKeepalive = now + 5f;
        }

        internal void OnFillerState(AtfFillerState state)
        {
            var session = SessionManager.Instance;
            var rule = SyncCatalog.AtfRefill;
            if (_failed || session == null || session.IsHost || rule == null
                || state.VehicleId != StableHash.Fnv1a32("vehicle:" + rule["rootPath"])
                || !AtfPolicy.CanReceive(_received, state)) return;
            try
            {
                _received = AtfPolicy.Copy(state);
                if (_filler != null) _filler.Apply(state);
            }
            catch (Exception e) { Fail(e); }
        }

        private void OnLocalCap(byte action)
        {
            var session = SessionManager.Instance;
            if (_failed || session == null || session.IsHost || _filler == null || _vehicle == null
                || !_filler.Mounted || !PlayerPosition(session, session.LocalPlayerId, out var player)
                || (player - _filler.CapTransform.position).sqrMagnitude > 9f) return;
            SendIntent(session, 0, action);
        }

        private void SendIntent(SessionManager session, uint bottleId, byte action)
        {
            if (_vehicle == null) return;
            session.SendWorldMessage(new AtfRefillIntent { VehicleId = _vehicle.Id, BottleId = bottleId,
                PlayerId = session.LocalPlayerId, Sequence = unchecked(++_sequence), Action = action }, Channel.ReliableOrdered);
        }

        private void UpdateGuest(SessionManager session)
        {
            uint pouring = 0;
            if (_filler != null && _filler.Mounted && _filler.Open
                && PlayerPosition(session, session.LocalPlayerId, out var player))
                foreach (var bottle in _items.AtfBottles)
                    if (_items.Items.TryGetValue(bottle.ItemId, out var item) && item.LocallyOwned
                        && CanPour(bottle, player)) { pouring = bottle.ItemId; break; }
            _filler?.SetPourGauge(pouring != 0);
            if (_guestBottle != pouring)
            {
                if (_guestBottle != 0) SendIntent(session, _guestBottle, AtfRefillIntent.StopPour);
                _guestBottle = pouring; _nextGuestPour = 0;
            }
            if (pouring == 0 || Time.unscaledTime < _nextGuestPour) return;
            _nextGuestPour = Time.unscaledTime + .2f;
            SendIntent(session, pouring, AtfRefillIntent.Pour);
        }

        internal void OnIntent(AtfRefillIntent request)
        {
            var session = SessionManager.Instance;
            if (_failed || session == null || !session.IsHost || !AtfPolicy.Valid(request)) return;
            try
            {
                bool target = _vehicle != null && request.VehicleId == _vehicle.Id && _filler != null;
                bool valid = target && (request.Action == AtfRefillIntent.StopPour
                    || (request.Action == AtfRefillIntent.Pour ? CanGuestPour(session, request.PlayerId, request.BottleId)
                        : _filler!.Mounted && PlayerPosition(session, request.PlayerId, out var p)
                            && (p - _filler.CapTransform.position).sqrMagnitude <= 9f));
                if (!_ledger.Accept(request, valid)) return;
                if (request.Action == AtfRefillIntent.StopPour)
                {
                    if (_leases.TryGetValue(request.PlayerId, out var lease) && lease.BottleId == request.BottleId)
                        _leases.Remove(request.PlayerId);
                }
                else if (request.Action == AtfRefillIntent.Pour)
                    _leases[request.PlayerId] = new Lease { BottleId = request.BottleId, AcceptedAt = Time.unscaledTime };
                else
                {
                    _filler!.Turn(request.Action == AtfRefillIntent.Unscrew);
                    if (!_filler.Open) _leases.Clear();
                    _nextSend = 0;
                    SyncEventLog.Record("atf-cap", request.PlayerId + ":" + request.Sequence + " rotation=" + _filler.Rotation);
                }
            }
            catch (Exception e) { Fail(e); }
        }

        private bool CanGuestPour(SessionManager session, byte actor, uint id)
        {
            float now = Time.unscaledTime;
            return _items.TryGetAtf(id, out var bottle) && !bottle.Replica
                && _items.Items.TryGetValue(id, out var item) && item.Body == bottle.Body
                && item.RemoteOwner == actor && !item.LocallyOwned && !_items.IsAtfHeldLocally(id)
                && item.LastRemoteAt > 0 && now >= item.LastRemoteAt && now - item.LastRemoteAt <= AtfPolicy.LeaseSeconds
                && PlayerPosition(session, actor, out var player)
                && ValidSourcePose(item.TargetPosition, item.TargetRotation, out var rotation)
                && CanPourAt(bottle, player, item.TargetPosition, rotation, true);
        }

        private bool CanPour(AtfBottleBinding bottle, Vector3 player)
            => bottle.Body != null && CanPourAt(bottle, player, bottle.Body.position, bottle.Body.rotation, false);

        private bool CanPourAt(AtfBottleBinding bottle, Vector3 player, Vector3 position, Quaternion rotation, bool remote)
        {
            float angle = remote ? rotation.eulerAngles.x : 0;
            return _items.TryGetAtf(bottle.ItemId, out var current) && ReferenceEquals(current, bottle)
                && _filler != null && _filler.Mounted && _filler.Open && bottle.Body != null
                && bottle.Body.gameObject.activeInHierarchy && !bottle.Empty
                && (remote ? angle >= 0 && angle < 80f : bottle.IsPouring)
                && AtfPolicy.ValidFluid(bottle.Fluid) && bottle.Fluid > 0
                && (player - position).sqrMagnitude <= 9f
                && (player - _filler.CapTransform.position).sqrMagnitude <= 9f
                && (remote ? OverlapsAtPose(bottle.PourCollider, _filler.FillCollider, bottle.Body.transform, position, rotation)
                    : Overlaps(bottle.PourCollider, _filler.FillCollider));
        }

        private static bool ValidSourcePose(Vector3 position, Quaternion rotation, out Quaternion normalized)
        {
            normalized = Quaternion.identity;
            if (!FiniteGeometry(position.x) || !FiniteGeometry(position.y) || !FiniteGeometry(position.z)
                || !FiniteGeometry(rotation.x) || !FiniteGeometry(rotation.y) || !FiniteGeometry(rotation.z) || !FiniteGeometry(rotation.w)) return false;
            double norm = (double)rotation.x * rotation.x + (double)rotation.y * rotation.y
                + (double)rotation.z * rotation.z + (double)rotation.w * rotation.w;
            if (norm < .9 || norm > 1.1) return false;
            float inverse = (float)(1 / Math.Sqrt(norm));
            normalized = new Quaternion(rotation.x * inverse, rotation.y * inverse, rotation.z * inverse, rotation.w * inverse);
            return true;
        }

        private static bool FiniteGeometry(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void Transfer(SessionManager session)
        {
            foreach (var pair in new List<KeyValuePair<byte, Lease>>(_leases))
            {
                if (!AtfPolicy.LeaseFresh(Time.unscaledTime, pair.Value.AcceptedAt)
                    || !CanGuestPour(session, pair.Key, pair.Value.BottleId))
                { _leases.Remove(pair.Key); continue; }
                if (_items.TryGetAtf(pair.Value.BottleId, out var bottle)) TransferBottle(bottle);
            }
            bool localPour = false;
            if (PlayerPosition(session, session.LocalPlayerId, out var player))
                foreach (var bottle in _items.AtfBottles)
                    if (!bottle.Replica && _items.Items.TryGetValue(bottle.ItemId, out var item)
                        && item.RemoteOwner == WorldSyncIds.NoOwner && CanPour(bottle, player))
                    { localPour = true; TransferBottle(bottle); }
            _filler?.SetPourGauge(localPour);
        }

        private void TransferBottle(AtfBottleBinding bottle)
        {
            if (_filler == null) return;
            float source = bottle.Fluid, target = _filler.OilLevel;
            float amount = AtfPolicy.TransferAmount(source, target, Time.deltaTime);
            if (amount <= 0) return;
            // Both setters validate first and write only scalar variables. Native
            // empty events run on the following presentation tick, after the pair.
            _filler.OilLevel = target + amount;
            bottle.SetHostFluid(source - amount);
        }

        private static bool PlayerPosition(SessionManager session, byte actor, out Vector3 position)
        {
            position = Vector3.zero;
            if (actor == session.LocalPlayerId)
            {
                var world = WorldSyncManager.Instance;
                if (world == null || DeathSyncManager.Instance?.IsLocalDead == true
                    || (!session.IsHost && PlayerSyncManager.Instance?.IsLocalSpawnReady != true)) return false;
                world.FindLocalPlayer();
                if (world.LocalPlayer == null) return false;
                position = world.LocalPlayer.position; return true;
            }
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
                if (player.PlayerId == actor && !player.IsDead && player.LastTransformTime > 0
                    && now >= player.LastTransformTime && now - player.LastTransformTime <= 2f)
                { position = player.Position; return true; }
            return false;
        }

        internal static bool Overlaps(CapsuleCollider? capsule, SphereCollider sphere)
            => OverlapsAtPose(capsule, sphere, null, Vector3.zero, Quaternion.identity);

        internal static bool OverlapsAtPose(CapsuleCollider? capsule, SphereCollider sphere, Transform? sourceRoot,
            Vector3 sourcePosition, Quaternion sourceRotation)
        {
            if (capsule == null || sphere == null || !capsule.enabled || !sphere.enabled
                || !capsule.gameObject.activeInHierarchy || !sphere.gameObject.activeInHierarchy) return false;
            var t = capsule.transform; var scale = t.lossyScale;
            float x = Mathf.Abs(scale.x), y = Mathf.Abs(scale.y), z = Mathf.Abs(scale.z);
            int direction = capsule.direction;
            float along = direction == 0 ? x : direction == 1 ? y : z;
            float across = direction == 0 ? Mathf.Max(y, z) : direction == 1 ? Mathf.Max(x, z) : Mathf.Max(x, y);
            float radius = capsule.radius * across;
            float half = Mathf.Max(0, capsule.height * along * .5f - radius);
            Vector3 center = t.TransformPoint(capsule.center);
            Vector3 axis = t.TransformDirection(direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward);
            if (sourceRoot != null)
            {
                // Rebase the native capsule, including its scaled child offset,
                // onto the last accepted owner pose. Rendering may still be easing
                // toward it and must not decide where a remote transfer happens.
                Quaternion change = sourceRotation * Quaternion.Inverse(sourceRoot.rotation);
                center = sourcePosition + change * (center - sourceRoot.position);
                axis = change * axis;
            }
            axis.Normalize();
            Vector3 point = sphere.transform.TransformPoint(sphere.center);
            var ss = sphere.transform.lossyScale;
            float reach = radius + sphere.radius * Mathf.Max(Mathf.Abs(ss.x), Mathf.Max(Mathf.Abs(ss.y), Mathf.Abs(ss.z)));
            float offset = Mathf.Clamp(Vector3.Dot(point - center, axis), -half, half);
            return (point - (center + axis * offset)).sqrMagnitude <= reach * reach;
        }

        internal void Forget(byte player) { _leases.Remove(player); _ledger.Forget(player); }
        internal void Clear()
        {
            try { _filler?.Restore(); }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("ATF filler cleanup: " + e.Message); }
            _filler = null; _vehicle = null; _observed = _sent = _received = null;
            _ledger.Clear(); _leases.Clear(); _guestBottle = 0; _sequence = 0; _failed = false;
            _nextBind = _nextWarning = _nextSend = _nextKeepalive = _nextGuestPour = 0;
        }
        private void Fail(Exception e)
        {
            _failed = true; _leases.Clear();
            // Keep competing native transfer writers suppressed until normal
            // cleanup; restoring them here could restart an unchecked pour.
            WinterMPPlugin.Log.LogWarning("ATF refill disabled: " + e.Message);
            SyncEventLog.Record("atf-refill-disabled", e.Message);
        }
    }
}
